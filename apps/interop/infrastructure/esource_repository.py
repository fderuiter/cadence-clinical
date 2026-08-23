"""SQLAlchemy Repository implementation for eSource Ingestion and CRC Reconciliation.

Requirements: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, ADR-2199
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.domain.esource_models import (
    EhrCandidateRecord,
    ReconciliationAction,
    ReconciliationStatus,
)
from apps.interop.domain.ports import IEsourceReconciliationRepositoryPort
from apps.interop.infrastructure.models import (
    ClinicalQuery,
    EhrReconciliationCandidateRecord,
    InteropAuditLog,
)


class EsourceReconciliationRepository(IEsourceReconciliationRepositoryPort):
    """SQLAlchemy-backed repository for eSource candidates and reconciliation records."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(
        self, entity_id: str
    ) -> EhrReconciliationCandidateRecord | None:
        stmt = select(EhrReconciliationCandidateRecord).where(
            EhrReconciliationCandidateRecord.id == entity_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def save(self, entity: Any) -> Any:
        self.session.add(entity)
        await self.session.flush()
        return entity

    async def save_candidates_batch(
        self,
        candidates: list[EhrCandidateRecord],
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> list[EhrReconciliationCandidateRecord]:
        records: list[EhrReconciliationCandidateRecord] = []
        for c in candidates:
            ts = c.source_timestamp
            if isinstance(ts, str):
                try:
                    ts = datetime.fromisoformat(ts.replace("Z", "+00:00"))
                except Exception:
                    ts = datetime.now()

            rec = EhrReconciliationCandidateRecord(
                id=c.id,
                study_id=c.study_id,
                site_id=c.site_id,
                subject_id=c.subject_id,
                subject_pseudonym=c.subject_pseudonym,
                ehr_system_id=c.ehr_system_id,
                fhir_resource_type=c.fhir_resource_type,
                fhir_resource_id=c.fhir_resource_id,
                source_timestamp=ts,
                target_domain=str(c.target_domain),
                target_variable=c.target_variable,
                cdash_testcd=c.cdash_testcd,
                cdash_test=c.cdash_test,
                candidate_value=c.candidate_value,
                candidate_unit=c.candidate_unit,
                current_ecrf_value=c.current_ecrf_value,
                current_ecrf_updated_at=c.current_ecrf_updated_at
                if isinstance(c.current_ecrf_updated_at, datetime)
                else None,
                discrepancy_type=str(c.discrepancy_type),
                confidence_score=c.confidence_score,
                mapping_provenance=c.mapping_provenance,
                raw_fhir_resource=c.raw_fhir_resource,
                status=str(c.status),
                created_by=user_id,
                reason_for_change=change_reason,
                version_index=1,
            )
            self.session.add(rec)
            records.append(rec)

        await self.session.flush()

        if records:
            audit = InteropAuditLog(
                user_id=user_id,
                user_role=user_role,
                action="ESOURCE_INGESTION_BATCH",
                details=(
                    f"Ingested {len(records)} candidate eSource records for study '{candidates[0].study_id}'. "
                    f"Subject: '{candidates[0].subject_pseudonym}'."
                ),
                change_reason=change_reason,
            )
            self.session.add(audit)
            await self.session.flush()

        return records

    async def list_candidates(
        self,
        study_id: str | None = None,
        subject_id: str | None = None,
        site_id: str | None = None,
        domain: str | None = None,
        status: str | None = None,
        skip: int = 0,
        limit: int = 100,
    ) -> tuple[list[EhrReconciliationCandidateRecord], int]:
        query = select(EhrReconciliationCandidateRecord)
        count_query = select(func.count()).select_from(EhrReconciliationCandidateRecord)

        if study_id:
            query = query.where(EhrReconciliationCandidateRecord.study_id == study_id)
            count_query = count_query.where(
                EhrReconciliationCandidateRecord.study_id == study_id
            )

        if subject_id:
            query = query.where(
                (EhrReconciliationCandidateRecord.subject_id == subject_id)
                | (EhrReconciliationCandidateRecord.subject_pseudonym == subject_id)
            )
            count_query = count_query.where(
                (EhrReconciliationCandidateRecord.subject_id == subject_id)
                | (EhrReconciliationCandidateRecord.subject_pseudonym == subject_id)
            )

        if site_id:
            query = query.where(EhrReconciliationCandidateRecord.site_id == site_id)
            count_query = count_query.where(
                EhrReconciliationCandidateRecord.site_id == site_id
            )

        if domain:
            query = query.where(
                EhrReconciliationCandidateRecord.target_domain == domain.upper()
            )
            count_query = count_query.where(
                EhrReconciliationCandidateRecord.target_domain == domain.upper()
            )

        if status:
            query = query.where(
                EhrReconciliationCandidateRecord.status == status.upper()
            )
            count_query = count_query.where(
                EhrReconciliationCandidateRecord.status == status.upper()
            )

        total_count = (await self.session.execute(count_query)).scalar_one()

        query = (
            query.order_by(EhrReconciliationCandidateRecord.source_timestamp.desc())
            .offset(skip)
            .limit(limit)
        )
        result = await self.session.execute(query)
        return list(result.scalars().all()), total_count

    async def accept_candidate(
        self,
        candidate_id: str,
        user_id: str,
        user_role: str,
        signature_token: str | None = None,
        commit_reason: str | None = None,
        change_reason: str = "CRC Accept & Commit",
    ) -> EhrReconciliationCandidateRecord:
        rec = await self.get_by_id(candidate_id)
        if not rec:
            raise ValueError(f"Candidate record with ID '{candidate_id}' not found")

        if rec.status != ReconciliationStatus.PENDING_REVIEW.value:
            raise ValueError(
                f"Cannot accept candidate in status '{rec.status}'. Must be in PENDING_REVIEW."
            )

        now = datetime.now()
        rec.status = ReconciliationStatus.ACCEPTED.value
        rec.resolution_action = ReconciliationAction.ACCEPT_COMMIT.value
        rec.resolution_reason = (
            commit_reason or "CRC accepted EHR candidate data for eCRF inclusion"
        )
        rec.resolved_by = user_id
        rec.resolved_at = now
        rec.version_index += 1

        if signature_token:
            rec.electronic_signature = {
                "signer_id": user_id,
                "signer_role": user_role,
                "signed_at": now.isoformat(),
                "token": signature_token,
                "intent": "eSource Data Acceptance & Commit Approval",
            }

        audit = InteropAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="ESOURCE_ACCEPT_COMMIT",
            details=(
                f"CRC accepted and committed EHR candidate '{rec.target_variable}' = '{rec.candidate_value} {rec.candidate_unit or ''}'. "
                f"Study: '{rec.study_id}', Subject: '{rec.subject_pseudonym}'. "
                f"Source EHR: '{rec.ehr_system_id}', FHIR: {rec.fhir_resource_type}/{rec.fhir_resource_id}, "
                f"Obs Date: {rec.source_timestamp.isoformat()}."
            ),
            change_reason=change_reason,
        )
        self.session.add(audit)
        await self.session.flush()
        return rec

    async def reject_candidate(
        self,
        candidate_id: str,
        user_id: str,
        user_role: str,
        rejection_reason: str,
        change_reason: str = "CRC Reject Candidate",
    ) -> EhrReconciliationCandidateRecord:
        if not rejection_reason or not rejection_reason.strip():
            raise ValueError(
                "Rejection rationale is required under GxP regulatory guidelines"
            )

        rec = await self.get_by_id(candidate_id)
        if not rec:
            raise ValueError(f"Candidate record with ID '{candidate_id}' not found")

        if rec.status != ReconciliationStatus.PENDING_REVIEW.value:
            raise ValueError(
                f"Cannot reject candidate in status '{rec.status}'. Must be in PENDING_REVIEW."
            )

        now = datetime.now()
        rec.status = ReconciliationStatus.REJECTED.value
        rec.resolution_action = ReconciliationAction.REJECT.value
        rec.resolution_reason = rejection_reason.strip()
        rec.resolved_by = user_id
        rec.resolved_at = now
        rec.version_index += 1

        audit = InteropAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="ESOURCE_REJECT",
            details=(
                f"CRC rejected EHR candidate '{rec.target_variable}' = '{rec.candidate_value}'. "
                f"Reason: '{rejection_reason.strip()}'. Study: '{rec.study_id}', Subject: '{rec.subject_pseudonym}'. "
                f"Source: {rec.fhir_resource_type}/{rec.fhir_resource_id}."
            ),
            change_reason=change_reason,
        )
        self.session.add(audit)
        await self.session.flush()
        return rec

    async def raise_clinical_query(
        self,
        candidate_id: str,
        user_id: str,
        user_role: str,
        query_explanation: str,
        discrepancy_type: str = "VALUE_MISMATCH",
        change_reason: str = "CRC Raise Discrepancy Query",
    ) -> tuple[EhrReconciliationCandidateRecord, ClinicalQuery]:
        if not query_explanation or not query_explanation.strip():
            raise ValueError("Query discrepancy explanation is required")

        rec = await self.get_by_id(candidate_id)
        if not rec:
            raise ValueError(f"Candidate record with ID '{candidate_id}' not found")

        if rec.status != ReconciliationStatus.PENDING_REVIEW.value:
            raise ValueError(
                f"Cannot raise query on candidate in status '{rec.status}'. Must be in PENDING_REVIEW."
            )

        now = datetime.now()
        query_id = str(uuid.uuid4())

        clinical_query = ClinicalQuery(
            id=query_id,
            study_id=rec.study_id,
            subject_id=rec.subject_pseudonym,
            domain=rec.target_domain,
            test_code=rec.cdash_testcd or rec.target_variable,
            status="OPEN",
            explanation=(
                f"[eSource Discrepancy - {discrepancy_type}] EHR Value: '{rec.candidate_value} {rec.candidate_unit or ''}' "
                f"vs eCRF Value: '{rec.current_ecrf_value}'. Details: {query_explanation.strip()}"
            ),
        )
        self.session.add(clinical_query)

        rec.status = ReconciliationStatus.QUERIED.value
        rec.resolution_action = ReconciliationAction.QUERY.value
        rec.resolution_reason = query_explanation.strip()
        rec.resolved_by = user_id
        rec.resolved_at = now
        rec.clinical_query_id = query_id
        rec.version_index += 1

        audit = InteropAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="ESOURCE_QUERY_RAISED",
            details=(
                f"CRC raised clinical discrepancy query '{query_id}' for field '{rec.target_variable}'. "
                f"EHR value: '{rec.candidate_value}' vs eCRF value: '{rec.current_ecrf_value}'. "
                f"Explanation: '{query_explanation.strip()}'. Study: '{rec.study_id}', Subject: '{rec.subject_pseudonym}'."
            ),
            change_reason=change_reason,
        )
        self.session.add(audit)
        await self.session.flush()
        return rec, clinical_query

    async def get_provenance_audit(self, candidate_id: str) -> dict[str, Any]:
        rec = await self.get_by_id(candidate_id)
        if not rec:
            raise ValueError(f"Candidate record with ID '{candidate_id}' not found")

        stmt = (
            select(InteropAuditLog)
            .where(InteropAuditLog.details.like(f"%{rec.fhir_resource_id}%"))
            .order_by(InteropAuditLog.timestamp.desc())
        )
        audit_res = await self.session.execute(stmt)
        audit_logs = [
            {
                "id": a.id,
                "timestamp": a.timestamp.isoformat() if a.timestamp else None,
                "user_id": a.user_id,
                "user_role": a.user_role,
                "action": a.action,
                "details": a.details,
                "change_reason": a.change_reason,
            }
            for a in audit_res.scalars().all()
        ]

        return {
            "candidate_id": rec.id,
            "study_id": rec.study_id,
            "site_id": rec.site_id,
            "subject_id": rec.subject_id,
            "subject_pseudonym": rec.subject_pseudonym,
            "ehr_system_id": rec.ehr_system_id,
            "fhir_resource_type": rec.fhir_resource_type,
            "fhir_resource_id": rec.fhir_resource_id,
            "source_timestamp": rec.source_timestamp.isoformat()
            if rec.source_timestamp
            else None,
            "target_domain": rec.target_domain,
            "target_variable": rec.target_variable,
            "cdash_testcd": rec.cdash_testcd,
            "cdash_test": rec.cdash_test,
            "candidate_value": rec.candidate_value,
            "candidate_unit": rec.candidate_unit,
            "current_ecrf_value": rec.current_ecrf_value,
            "discrepancy_type": rec.discrepancy_type,
            "confidence_score": rec.confidence_score,
            "mapping_provenance": rec.mapping_provenance,
            "raw_fhir_resource": rec.raw_fhir_resource,
            "status": rec.status,
            "resolution_action": rec.resolution_action,
            "resolution_reason": rec.resolution_reason,
            "resolved_by": rec.resolved_by,
            "resolved_at": rec.resolved_at.isoformat() if rec.resolved_at else None,
            "electronic_signature": rec.electronic_signature,
            "clinical_query_id": rec.clinical_query_id,
            "audit_trail": audit_logs,
        }
