"""Application Service orchestrating Generative Safety Narrative drafting, Part 11 signing, and E2B XML export.

Requirements: PRD-SYS-051, PRD-SYS-052
"""

import logging
import uuid
from datetime import UTC, datetime
from typing import Any

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.adapters.ai_narrative_client import AISafetyNarrativeClient
from apps.safety.domain.narrative_models import (
    ClinicalTimelineEvent,
    GroundedClaim,
    NarrativeSectionType,
    NarrativeSignResponse,
    SafetyNarrativeDTO,
    SafetyNarrativeSection,
)
from apps.safety.infrastructure.execution_client import ExecutionClient
from apps.safety.infrastructure.models import (
    SafetyNarrative,
    write_audit_log,
)
from apps.safety.services.timeline_aggregator import (
    build_timeline_from_sdtm_records,
)
from packages.compliance.services.esignature_verifier import (
    UnapprovedAIRecordError,
    assert_ai_record_approved,
)
from packages.database.audit import AIReviewStatus
from packages.security.signing import generate_canonical_signature

logger = logging.getLogger("safety-narrative-service")

ALLOWED_SIGNING_ROLES = {
    "safety_physician",
    "sponsor_medical_monitor",
    "safety_reviewer",
    "admin",
    "super_admin",
}


class SafetyNarrativeService:
    """Orchestrates end-to-end regulatory safety narrative lifecycle."""

    def __init__(
        self,
        execution_client: ExecutionClient | None = None,
        ai_client: AISafetyNarrativeClient | None = None,
    ) -> None:
        self.execution_client = execution_client or ExecutionClient()
        self.ai_client = ai_client or AISafetyNarrativeClient()

    async def generate_narrative(
        self,
        session: AsyncSession,
        study_id: str,
        subject_id: str,
        sae_event_key: str,
        created_by: str,
        reason_for_change: str,
        worldwide_unique_case_id: str | None = None,
        additional_context: str | None = None,
        test_client: Any | None = None,
    ) -> SafetyNarrativeDTO:
        """Draft a new AI-generated Serious Adverse Event safety narrative.

        Requirements: PRD-SYS-051, PRD-SYS-052
        """
        case_id = (
            worldwide_unique_case_id
            or f"WW-{study_id}-{subject_id}-{uuid.uuid4().hex[:6]}"
        )

        # 1. Retrieve all clinical domains from apps/execution
        sdtm_bundle: dict[str, list[dict[str, Any]]] = {}
        for domain in ["DM", "MH", "CM", "AE", "LB", "VS", "EX"]:
            try:
                dom_res = await self.execution_client.fetch_sdtm_domain(
                    study_id=study_id, domain=domain, client=test_client
                )
                sdtm_bundle[domain] = dom_res.get(domain, [])
            except Exception as e:
                logger.info(
                    "Could not fetch domain %s for study %s: %s", domain, study_id, e
                )
                sdtm_bundle[domain] = []

        # 2. Compile chronological de-identified clinical timeline
        timeline = build_timeline_from_sdtm_records(
            study_id=study_id,
            subject_id=subject_id,
            sdtm_bundle=sdtm_bundle,
            target_sae_key=sae_event_key,
        )

        # 3. Invoke Tier 3 Frontier model
        ai_output = await self.ai_client.generate_safety_narrative(
            timeline=timeline,
            sae_event_key=sae_event_key,
            additional_context=additional_context,
            client=test_client,
        )

        narrative_id = str(uuid.uuid4())
        sections = ai_output["sections"]
        claims = ai_output["grounded_claims"]
        raw_text = ai_output["raw_narrative_text"]

        sections_json = [s.model_dump() for s in sections]
        claims_json = [c.model_dump() for c in claims]
        timeline_json = [ev.model_dump() for ev in timeline.events]

        title = f"Safety Narrative for {subject_id} - SAE {sae_event_key}"

        narrative_orm = SafetyNarrative(
            id=narrative_id,
            study_id=study_id,
            subject_id=subject_id,
            case_id=case_id,
            sae_event_key=sae_event_key,
            title=title,
            sections=sections_json,
            raw_narrative_text=raw_text,
            timeline_events=timeline_json,
            grounded_claims=claims_json,
            model_identifier=ai_output["model_identifier"],
            prompt_hash=ai_output["prompt_hash"],
            confidence_score=ai_output["confidence_score"],
            review_status=AIReviewStatus.DRAFT_AI.value,
            approved_by_user_id=None,
            approved_at=None,
            esignature_manifest_id=None,
            created_by=created_by,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        session.add(narrative_orm)
        await session.flush()

        # Audit logging
        await write_audit_log(
            session=session,
            created_by=created_by,
            action="SAFETY_NARRATIVE_GENERATED",
            details=f"Drafted AI safety narrative {narrative_id} for subject {subject_id}, SAE {sae_event_key}.",
            record_id=narrative_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )

        return self._map_orm_to_dto(narrative_orm)

    async def get_narrative(
        self,
        session: AsyncSession,
        narrative_id: str,
        user_id: str,
        reason_for_change: str | None = None,
    ) -> SafetyNarrativeDTO:
        """Retrieve a specific safety narrative by ID."""
        stmt = select(SafetyNarrative).where(SafetyNarrative.id == narrative_id)
        res = await session.execute(stmt)
        narrative = res.scalars().first()
        if not narrative:
            raise HTTPException(
                status_code=404,
                detail=f"Safety narrative with ID '{narrative_id}' not found.",
            )

        await write_audit_log(
            session=session,
            created_by=user_id,
            action="SAFETY_NARRATIVE_VIEW",
            details=f"Viewed safety narrative {narrative_id}.",
            record_id=narrative_id,
            reason_for_change=reason_for_change or "View narrative",
            version_index=narrative.version_index,
        )

        return self._map_orm_to_dto(narrative)

    async def list_narratives(
        self,
        session: AsyncSession,
        study_id: str | None = None,
        subject_id: str | None = None,
        review_status: str | None = None,
    ) -> list[SafetyNarrativeDTO]:
        """List safety narratives matching filters."""
        stmt = select(SafetyNarrative)
        if study_id:
            stmt = stmt.where(SafetyNarrative.study_id == study_id)
        if subject_id:
            stmt = stmt.where(SafetyNarrative.subject_id == subject_id)
        if review_status:
            stmt = stmt.where(SafetyNarrative.review_status == review_status)

        stmt = stmt.order_by(SafetyNarrative.created_at.desc())
        res = await session.execute(stmt)
        narratives = res.scalars().all()
        return [self._map_orm_to_dto(n) for n in narratives]

    async def sign_narrative(
        self,
        session: AsyncSession,
        narrative_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        signature_secret: str | None = None,
    ) -> NarrativeSignResponse:
        """Applies a 21 CFR Part 11 cryptographic electronic signature to approve a narrative.

        Requirements: PRD-SYS-052
        """
        # 1. Role verification
        roles_set = {r.strip().lower() for r in user_roles.split(",") if r.strip()}
        if not roles_set.intersection(ALLOWED_SIGNING_ROLES):
            raise HTTPException(
                status_code=403,
                detail=(
                    f"Forbidden: User '{user_id}' with roles '{user_roles}' is not authorized "
                    "to execute 21 CFR Part 11 Safety Physician electronic signatures."
                ),
            )

        if not reason_for_change or not reason_for_change.strip():
            raise HTTPException(
                status_code=400,
                detail="Reason for change is mandatory for 21 CFR Part 11 electronic signatures.",
            )

        stmt = select(SafetyNarrative).where(SafetyNarrative.id == narrative_id)
        res = await session.execute(stmt)
        narrative = res.scalars().first()
        if not narrative:
            raise HTTPException(
                status_code=404,
                detail=f"Safety narrative with ID '{narrative_id}' not found.",
            )

        # 2. Cryptographic signature generation
        now_dt = datetime.now(UTC)
        signing_secret = signature_secret or "safety-physician-signing-secret"
        payload_to_sign = {
            "narrative_id": narrative.id,
            "study_id": narrative.study_id,
            "subject_id": narrative.subject_id,
            "prompt_hash": narrative.prompt_hash,
            "raw_narrative_text": narrative.raw_narrative_text,
            "signer": user_id,
            "timestamp": now_dt.isoformat(),
            "reason_for_change": reason_for_change,
        }
        manifest_id = f"sig_man_{uuid.uuid4().hex[:12]}"
        _ = generate_canonical_signature(
            payload_to_sign, signing_secret.encode("utf-8")
        )

        # 3. Mutate approval state
        narrative.review_status = AIReviewStatus.APPROVED.value
        narrative.approved_by_user_id = user_id
        narrative.approved_at = now_dt.replace(tzinfo=None)
        narrative.esignature_manifest_id = manifest_id
        narrative.version_index += 1
        narrative.reason_for_change = reason_for_change

        await session.flush()

        # 4. Audit ledger entry
        await write_audit_log(
            session=session,
            created_by=user_id,
            action="SAFETY_NARRATIVE_SIGNED",
            details=(
                f"21 CFR Part 11 e-signature applied by {user_id} ({user_roles}). "
                f"Manifest: {manifest_id}. Review Status: APPROVED."
            ),
            record_id=narrative.id,
            reason_for_change=reason_for_change,
            version_index=narrative.version_index,
        )

        return NarrativeSignResponse(
            narrative_id=narrative.id,
            review_status=AIReviewStatus.APPROVED,
            approved_by_user_id=user_id,
            approved_at=now_dt.isoformat(),
            esignature_manifest_id=manifest_id,
            message="Safety narrative successfully approved with 21 CFR Part 11 electronic signature.",
        )

    async def export_narrative_to_e2b_xml(
        self,
        session: AsyncSession,
        narrative_id: str,
        user_id: str,
        reason_for_change: str,
    ) -> str:
        """Exports approved narrative into ICH E2B(R3) XML clinical course block.

        Raises UnapprovedAIRecordError if narrative is not in APPROVED state.

        Requirements: PRD-SYS-052
        """
        stmt = select(SafetyNarrative).where(SafetyNarrative.id == narrative_id)
        res = await session.execute(stmt)
        narrative = res.scalars().first()
        if not narrative:
            raise HTTPException(
                status_code=404,
                detail=f"Safety narrative with ID '{narrative_id}' not found.",
            )

        # 21 CFR Part 11 Approval Guard
        dto = self._map_orm_to_dto(narrative)
        try:
            assert_ai_record_approved(dto)
        except UnapprovedAIRecordError as err:
            raise HTTPException(
                status_code=412,
                detail=f"Precondition Failed: Cannot export unapproved AI narrative. {str(err)}",
            )

        # Render into ICH E2B(R3) XML Section B.5.1 Narrative Block
        xml_escaped_text = (
            narrative.raw_narrative_text.replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace('"', "&quot;")
        )

        xml_output = (
            f'<?xml version="1.0" encoding="UTF-8"?>\n'
            f'<ichicsr lang="en">\n'
            f"  <safetyreport>\n"
            f"    <safetyreportversion>{narrative.version_index}</safetyreportversion>\n"
            f"    <safetyreportid>{narrative.case_id}</safetyreportid>\n"
            f"    <patient>\n"
            f"      <patientinitial>{narrative.subject_id}</patientinitial>\n"
            f"      <summary>\n"
            f"        <narrativeincludeclinicalcourse>{xml_escaped_text}</narrativeincludeclinicalcourse>\n"
            f"        <reportercomment>Grounded regulatory narrative approved by {narrative.approved_by_user_id} (Manifest: {narrative.esignature_manifest_id})</reportercomment>\n"
            f"      </summary>\n"
            f"    </patient>\n"
            f"  </safetyreport>\n"
            f"</ichicsr>"
        )

        await write_audit_log(
            session=session,
            created_by=user_id,
            action="SAFETY_NARRATIVE_EXPORTED_E2B",
            details=f"Exported approved safety narrative {narrative_id} to ICH E2B(R3) XML.",
            record_id=narrative_id,
            reason_for_change=reason_for_change,
            version_index=narrative.version_index,
        )

        return xml_output

    def _map_orm_to_dto(self, orm: SafetyNarrative) -> SafetyNarrativeDTO:
        sections = [
            SafetyNarrativeSection(
                section_type=NarrativeSectionType(s["section_type"]),
                section_title=s.get("section_title", s["section_type"]),
                content=s.get("content", ""),
                grounded_claims=[
                    GroundedClaim(
                        claim_id=c["claim_id"],
                        sentence_text=c["sentence_text"],
                        section_type=NarrativeSectionType(c["section_type"]),
                        grounded_event_ids=c.get("grounded_event_ids", []),
                        confidence_score=float(c.get("confidence_score", 1.0)),
                    )
                    for c in s.get("grounded_claims", [])
                ],
                order_index=int(s.get("order_index", 0)),
            )
            for s in (orm.sections or [])
        ]

        claims = [
            GroundedClaim(
                claim_id=c["claim_id"],
                sentence_text=c["sentence_text"],
                section_type=NarrativeSectionType(c["section_type"]),
                grounded_event_ids=c.get("grounded_event_ids", []),
                confidence_score=float(c.get("confidence_score", 1.0)),
            )
            for c in (orm.grounded_claims or [])
        ]

        events = [
            ClinicalTimelineEvent(
                event_id=ev["event_id"],
                event_type=ev["event_type"],
                event_date=ev.get("event_date"),
                title=ev.get("title", ""),
                description=ev.get("description", ""),
                domain=ev.get("domain"),
                sequence=ev.get("sequence"),
                source_record_id=ev.get("source_record_id"),
                details=ev.get("details", {}),
            )
            for ev in (orm.timeline_events or [])
        ]

        approved_at_utc = (
            orm.approved_at.replace(tzinfo=UTC) if orm.approved_at else None
        )
        created_at_utc = (
            orm.created_at.replace(tzinfo=UTC) if orm.created_at else datetime.now(UTC)
        )

        return SafetyNarrativeDTO(
            id=orm.id,
            study_id=orm.study_id,
            subject_id=orm.subject_id,
            case_id=orm.case_id,
            sae_event_key=orm.sae_event_key,
            title=orm.title,
            sections=sections,
            raw_narrative_text=orm.raw_narrative_text,
            timeline_events=events,
            grounded_claims=claims,
            model_identifier=orm.model_identifier,
            prompt_hash=orm.prompt_hash,
            confidence_score=orm.confidence_score,
            review_status=AIReviewStatus(orm.review_status),
            approved_by_user_id=orm.approved_by_user_id,
            approved_at=approved_at_utc,
            esignature_manifest_id=orm.esignature_manifest_id,
            created_at=created_at_utc,
            created_by=orm.created_by,
            reason_for_change=orm.reason_for_change,
            version_index=orm.version_index,
        )


__all__ = [
    "ALLOWED_SIGNING_ROLES",
    "SafetyNarrativeService",
]
