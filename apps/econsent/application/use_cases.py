"""Application use cases and domain services for eConsent.

Orchestrates template composition, multi-party signatures, granular options,
adaptive comprehension quizzes, re-consent tracking, formal withdrawal, and CDISC ODM exports.
Strictly decoupled from concrete database drivers and web frameworks (Hexagonal Architecture).
"""

import hashlib
import logging
import uuid
from datetime import UTC, datetime
from typing import Any

from apps.econsent.domain.cdisc_odm import generate_econsent_cdisc_odm_xml
from apps.econsent.domain.diff_engine import (
    TemplateDiffReport,
    compare_templates,
)
from apps.econsent.domain.document_renderer import (
    render_verifiable_consent_html,
)
from apps.econsent.domain.entities import (
    ComprehensionCheckEntity,
    ConsentAuditLogEntity,
    ConsentClauseEntity,
    ConsentSignatureEntity,
    ConsentTemplateEntity,
    ConsentTranslationEntity,
    ConsentWithdrawalEntity,
    GranularConsentOptionEntity,
    GranularOptionSelectionEntity,
    ReconsentRequirementEntity,
    SignerRole,
    SubjectConsentEntity,
    TranslationStatus,
    WithdrawalScope,
)
from apps.econsent.domain.evaluator import (
    evaluate_detailed_comprehension,
)
from apps.econsent.domain.exceptions import (
    ClauseNotFoundError,
    ComprehensionCheckNotFoundError,
    InvalidTranslationTransitionError,
    TemplateNotFoundError,
    TranslationNotFoundError,
)
from apps.econsent.domain.ports import (
    IComprehensionRepository,
    IConsentAuditRepository,
    IConsentClauseRepository,
    IConsentSignatureRepository,
    IConsentTemplateRepository,
    IConsentTranslationRepository,
    IConsentWithdrawalRepository,
    IGranularOptionRepository,
    IReconsentRepository,
    ISubjectConsentRepository,
)

logger = logging.getLogger("econsent-application")


# =========================================================================
# 1. Clause Management Service
# =========================================================================
class ClauseManagementService:
    """Application service managing reusable consent clauses."""

    def __init__(
        self,
        clause_repo: IConsentClauseRepository,
        audit_repo: IConsentAuditRepository | None = None,
    ) -> None:
        self.clause_repo = clause_repo
        self.audit_repo = audit_repo

    async def create_clause(
        self,
        study_id: str,
        title: str,
        text: str,
        clause_id: str | None = None,
        created_by: str = "system",
        reason_for_change: str = "Initial clause creation",
    ) -> ConsentClauseEntity:
        entity = ConsentClauseEntity(
            id=str(uuid.uuid4()),
            clause_id=clause_id or f"clause-{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            title=title,
            text=text,
            version_index=1,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.clause_repo.save(entity)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="designer",
                    action="CREATE_CLAUSE",
                    document_id=saved.id,
                    details=f"Created clause '{saved.clause_id}' v1 for study '{study_id}'.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def update_clause(
        self,
        clause_id: str,
        title: str,
        text: str,
        created_by: str = "system",
        reason_for_change: str = "Amended clause content",
    ) -> ConsentClauseEntity:
        latest = await self.clause_repo.get_latest_by_clause_id(clause_id)
        if not latest:
            raise ClauseNotFoundError(f"Clause '{clause_id}' not found.")

        updated = ConsentClauseEntity(
            id=str(uuid.uuid4()),
            clause_id=clause_id,
            study_id=latest.study_id,
            title=title,
            text=text,
            version_index=latest.version_index + 1,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.clause_repo.save(updated)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="designer",
                    action="UPDATE_CLAUSE",
                    document_id=saved.id,
                    details=f"Updated clause '{clause_id}' to version {saved.version_index}.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def get_clause(
        self, clause_id: str, version_index: int | None = None
    ) -> ConsentClauseEntity:
        if version_index is not None:
            clause = await self.clause_repo.get_by_clause_and_version(
                clause_id, version_index
            )
        else:
            clause = await self.clause_repo.get_latest_by_clause_id(clause_id)

        if not clause:
            raise ClauseNotFoundError(
                f"Clause '{clause_id}'"
                + (f" v{version_index}" if version_index else "")
                + " not found."
            )
        return clause

    async def list_clauses_for_study(self, study_id: str) -> list[ConsentClauseEntity]:
        return await self.clause_repo.list_clauses(study_id=study_id)


# =========================================================================
# 2. Template Authoring & Version Diffing Service
# =========================================================================
class TemplateAuthoringService:
    """Application service for authoring, composing, publishing, and diffing consent templates."""

    def __init__(
        self,
        template_repo: IConsentTemplateRepository,
        clause_repo: IConsentClauseRepository,
        audit_repo: IConsentAuditRepository | None = None,
    ) -> None:
        self.template_repo = template_repo
        self.clause_repo = clause_repo
        self.audit_repo = audit_repo

    async def create_template(
        self,
        study_id: str,
        template_name: str,
        protocol_version: str,
        clauses: list[str],
        workflow_steps: list[dict[str, Any]],
        template_id: str | None = None,
        requires_reconsent: bool = False,
        created_by: str = "system",
        reason_for_change: str = "Initial template authoring",
    ) -> ConsentTemplateEntity:
        entity = ConsentTemplateEntity(
            id=str(uuid.uuid4()),
            template_id=template_id or f"tpl-{uuid.uuid4().hex[:8]}",
            study_id=study_id,
            template_name=template_name,
            protocol_version=protocol_version,
            is_published=False,
            requires_reconsent=requires_reconsent,
            clauses=clauses,
            workflow_steps=workflow_steps,
            version_index=1,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.template_repo.save(entity)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="designer",
                    action="CREATE_TEMPLATE",
                    document_id=saved.id,
                    details=f"Authored template '{saved.template_id}' v1 for study '{study_id}'.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def update_template(
        self,
        template_id: str,
        template_name: str | None = None,
        protocol_version: str | None = None,
        clauses: list[str] | None = None,
        workflow_steps: list[dict[str, Any]] | None = None,
        requires_reconsent: bool | None = None,
        is_published: bool | None = None,
        created_by: str = "system",
        reason_for_change: str = "Amended template revision",
    ) -> ConsentTemplateEntity:
        latest = await self.template_repo.get_latest_by_template_id(template_id)
        if not latest:
            raise TemplateNotFoundError(f"Template '{template_id}' not found.")

        updated = ConsentTemplateEntity(
            id=str(uuid.uuid4()),
            template_id=template_id,
            study_id=latest.study_id,
            template_name=(
                template_name if template_name is not None else latest.template_name
            ),
            protocol_version=(
                protocol_version
                if protocol_version is not None
                else latest.protocol_version
            ),
            clauses=clauses if clauses is not None else list(latest.clauses),
            workflow_steps=(
                workflow_steps
                if workflow_steps is not None
                else list(latest.workflow_steps)
            ),
            requires_reconsent=(
                requires_reconsent
                if requires_reconsent is not None
                else latest.requires_reconsent
            ),
            is_published=(
                is_published if is_published is not None else latest.is_published
            ),
            version_index=latest.version_index + 1,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.template_repo.save(updated)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="designer",
                    action="UPDATE_TEMPLATE",
                    document_id=saved.id,
                    details=f"Updated template '{template_id}' to version {saved.version_index}.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def publish_template(
        self,
        template_id: str,
        created_by: str = "system",
        reason_for_change: str = "Template publication approval",
    ) -> ConsentTemplateEntity:
        latest = await self.template_repo.get_latest_by_template_id(template_id)
        if not latest:
            raise TemplateNotFoundError(f"Template '{template_id}' not found.")

        latest.is_published = True
        latest.reason_for_change = reason_for_change
        saved = await self.template_repo.save(latest)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="designer",
                    action="PUBLISH_TEMPLATE",
                    document_id=saved.id,
                    details=f"Published template '{template_id}' version {saved.version_index}.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def get_template(
        self, template_id: str, version_index: int | None = None
    ) -> ConsentTemplateEntity:
        if version_index is not None:
            tpl = await self.template_repo.get_by_template_and_version(
                template_id, version_index
            )
        else:
            tpl = await self.template_repo.get_latest_by_template_id(template_id)

        if not tpl:
            raise TemplateNotFoundError(
                f"Template '{template_id}'"
                + (f" v{version_index}" if version_index else "")
                + " not found."
            )
        return tpl

    async def compose_template(
        self, template_id: str, version_index: int | None = None
    ) -> dict[str, Any]:
        template = await self.get_template(template_id, version_index)

        composed_clauses = []
        for cid in template.clauses:
            clause = await self.clause_repo.get_latest_by_clause_id(cid)
            if clause:
                composed_clauses.append(
                    {
                        "clause_id": clause.clause_id,
                        "title": clause.title,
                        "text": clause.text,
                        "version_index": clause.version_index,
                    }
                )

        return {
            "template_id": template.template_id,
            "template_name": template.template_name,
            "protocol_version": template.protocol_version,
            "version_index": template.version_index,
            "is_published": template.is_published,
            "requires_reconsent": template.requires_reconsent,
            "clauses": composed_clauses,
            "workflow_steps": template.workflow_steps,
        }

    async def diff_template_versions(
        self,
        template_id: str,
        base_version_index: int,
        target_version_index: int,
    ) -> TemplateDiffReport:
        base_composed = await self.compose_template(template_id, base_version_index)
        target_composed = await self.compose_template(template_id, target_version_index)

        return compare_templates(
            template_id=template_id,
            base_version_index=base_version_index,
            target_version_index=target_version_index,
            base_clauses=base_composed["clauses"],
            target_clauses=target_composed["clauses"],
        )


# =========================================================================
# 3. Translation Management Service
# =========================================================================
class TranslationService:
    """Application service for managing multilingual translation lifecycles."""

    def __init__(
        self,
        translation_repo: IConsentTranslationRepository,
        audit_repo: IConsentAuditRepository | None = None,
    ) -> None:
        self.translation_repo = translation_repo
        self.audit_repo = audit_repo

    async def create_translation(
        self,
        source_id: str,
        source_type: str,
        source_version_index: int,
        language_code: str,
        translated_title: str,
        translated_text: str,
        translation_id: str | None = None,
        created_by: str = "system",
        reason_for_change: str = "Initial translation",
    ) -> ConsentTranslationEntity:
        entity = ConsentTranslationEntity(
            id=str(uuid.uuid4()),
            translation_id=translation_id or f"tr-{uuid.uuid4().hex[:8]}",
            source_id=source_id,
            source_type=source_type,
            source_version_index=source_version_index,
            language_code=language_code,
            translated_title=translated_title,
            translated_text=translated_text,
            status=TranslationStatus.DRAFT,
            version_index=1,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.translation_repo.save(entity)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="translator",
                    action="CREATE_TRANSLATION",
                    document_id=saved.id,
                    details=f"Authored translation '{saved.translation_id}' ({language_code}) for '{source_id}'.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def transition_status(
        self,
        translation_id: str,
        target_status: str | TranslationStatus,
        created_by: str = "system",
        reason_for_change: str = "Translation status update",
    ) -> ConsentTranslationEntity:
        latest = await self.translation_repo.get_latest_by_translation_id(
            translation_id
        )
        if not latest:
            raise TranslationNotFoundError(f"Translation '{translation_id}' not found.")

        target_status_str = str(target_status).upper()
        current_status_str = str(latest.status).upper()

        allowed_transitions = {
            "DRAFT": ["IN_REVIEW", "REJECTED"],
            "IN_REVIEW": ["APPROVED", "DRAFT", "REJECTED"],
            "APPROVED": ["PUBLISHED", "IN_REVIEW"],
            "PUBLISHED": ["RETIRED"],
            "REJECTED": ["DRAFT"],
            "RETIRED": [],
        }

        if (
            target_status_str not in allowed_transitions.get(current_status_str, [])
            and target_status_str != current_status_str
        ):
            raise InvalidTranslationTransitionError(
                f"Cannot transition translation from {current_status_str} to {target_status_str}."
            )

        latest.status = target_status_str
        latest.reason_for_change = reason_for_change
        saved = await self.translation_repo.save(latest)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="translator",
                    action="TRANSITION_TRANSLATION",
                    document_id=saved.id,
                    details=f"Transitioned translation '{translation_id}' to {target_status_str}.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved


# =========================================================================
# 4. Comprehension Check Service
# =========================================================================
class ComprehensionService:
    """Application service for configuring quizzes and evaluating subject submissions."""

    def __init__(
        self,
        comprehension_repo: IComprehensionRepository,
        audit_repo: IConsentAuditRepository | None = None,
    ) -> None:
        self.comprehension_repo = comprehension_repo
        self.audit_repo = audit_repo

    async def define_check(
        self,
        template_id: str,
        version_index: int,
        questions: list[dict[str, Any]],
        expected_answers: dict[str, str],
        threshold_policy: dict[str, Any],
        created_by: str = "system",
        reason_for_change: str = "Comprehension check setup",
    ) -> ComprehensionCheckEntity:
        entity = ComprehensionCheckEntity(
            id=str(uuid.uuid4()),
            template_id=template_id,
            version_index=version_index,
            questions=questions,
            expected_answers=expected_answers,
            threshold_policy=threshold_policy,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.comprehension_repo.save_check(entity)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="designer",
                    action="CREATE_COMPREHENSION_CHECK",
                    document_id=saved.id,
                    details=f"Defined comprehension quiz for template '{template_id}' v{version_index}.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def evaluate_submission(
        self,
        template_id: str,
        version_index: int,
        subject_pseudonym: str,
        submitted_answers: dict[str, str],
        created_by: str = "system",
        reason_for_change: str = "Subject comprehension evaluation",
    ) -> dict[str, Any]:
        check = await self.comprehension_repo.get_check(template_id, version_index)
        if not check:
            raise ComprehensionCheckNotFoundError(
                f"No comprehension check found for template '{template_id}' v{version_index}."
            )

        eval_result = evaluate_detailed_comprehension(
            questions=check.questions,
            expected_answers=check.expected_answers,
            submitted_answers=submitted_answers,
            threshold_policy=check.threshold_policy,
        )

        passed = eval_result["passed"]
        score = eval_result["score"]

        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="subject",
                    action="EVALUATE_COMPREHENSION",
                    document_id=f"{template_id}:{version_index}:{subject_pseudonym}",
                    details=f"Comprehension quiz result for '{subject_pseudonym}': Passed={passed}, Score={score}%.",
                    reason_for_change=reason_for_change,
                )
            )

        return eval_result


# =========================================================================
# 5. Granular Options Service
# =========================================================================
class GranularOptionService:
    """Application service for authoring and selecting tiered optional research consents."""

    def __init__(
        self,
        granular_repo: IGranularOptionRepository,
        audit_repo: IConsentAuditRepository | None = None,
    ) -> None:
        self.granular_repo = granular_repo
        self.audit_repo = audit_repo

    async def create_option(
        self,
        template_id: str,
        version_index: int,
        option_code: str,
        title: str,
        description: str,
        category: str = "OTHER",
        is_mandatory: bool = False,
        default_selected: bool = False,
        created_by: str = "system",
        reason_for_change: str = "Add granular option",
    ) -> GranularConsentOptionEntity:
        entity = GranularConsentOptionEntity(
            id=str(uuid.uuid4()),
            template_id=template_id,
            version_index=version_index,
            option_code=option_code,
            title=title,
            description=description,
            category=category,
            is_mandatory=is_mandatory,
            default_selected=default_selected,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved = await self.granular_repo.save_option(entity)
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="designer",
                    action="CREATE_GRANULAR_OPTION",
                    document_id=saved.id,
                    details=f"Created optional research choice '{option_code}' for template '{template_id}' v{version_index}.",
                    reason_for_change=reason_for_change,
                )
            )
        return saved

    async def list_options(
        self, template_id: str, version_index: int
    ) -> list[GranularConsentOptionEntity]:
        return await self.granular_repo.list_options_for_template(
            template_id, version_index
        )


# =========================================================================
# 6. Consent Capture & Multi-Party Signing Service
# =========================================================================
class ConsentCaptureService:
    """Application service for 21 CFR Part 11 electronic signature execution."""

    def __init__(
        self,
        consent_repo: ISubjectConsentRepository,
        signature_repo: IConsentSignatureRepository,
        granular_repo: IGranularOptionRepository,
        audit_repo: IConsentAuditRepository | None = None,
    ) -> None:
        self.consent_repo = consent_repo
        self.signature_repo = signature_repo
        self.granular_repo = granular_repo
        self.audit_repo = audit_repo

    async def capture_consent(
        self,
        study_id: str,
        site_id: str,
        subject_pseudonym: str,
        template_id: str,
        version_index: int,
        protocol_version: str,
        source_content_identity: str,
        signatures: list[dict[str, Any]] | None = None,
        granular_selections: list[dict[str, Any]] | None = None,
        device_timestamp: datetime | None = None,
        created_by: str = "system",
        reason_for_change: str = "Subject consent execution",
    ) -> SubjectConsentEntity:
        signatures = signatures or []
        granular_selections = granular_selections or []
        server_timestamp = datetime.now(UTC)

        # 1. Compute canonical payload hash
        hasher = hashlib.sha256()
        hasher.update(
            f"{study_id}:{subject_pseudonym}:{template_id}:{version_index}:{source_content_identity}".encode()
        )
        for sig in signatures:
            hasher.update(
                f"{sig.get('role')}:{sig.get('signer_name')}:{sig.get('meaning')}".encode()
            )
        for sel in granular_selections:
            hasher.update(f"{sel.get('option_code')}:{sel.get('selected')}".encode())
        manifest_digest = hasher.hexdigest()

        # 2. Save SubjectConsent record
        consent_entity = SubjectConsentEntity(
            id=str(uuid.uuid4()),
            subject_pseudonym=subject_pseudonym,
            study_id=study_id,
            site_id=site_id,
            template_id=template_id,
            version_index=version_index,
            protocol_version=protocol_version,
            source_content_identity=source_content_identity,
            status="ACTIVE",
            server_timestamp=server_timestamp,
            device_timestamp=device_timestamp,
            signature_manifest={
                "manifest_digest_sha256": manifest_digest,
                "total_signatures": len(signatures),
                "total_granular_selections": len(granular_selections),
            },
            created_at=server_timestamp,
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        saved_consent = await self.consent_repo.save(consent_entity)

        # 3. Save multi-party signatures
        for s in signatures:
            role = s.get("role", SignerRole.SUBJECT)
            signer_name = s.get("signer_name") or created_by
            sig_hasher = hashlib.sha256()
            sig_hasher.update(f"{role}:{signer_name}:{manifest_digest}".encode())
            sig_digest = sig_hasher.hexdigest()

            sig_entity = ConsentSignatureEntity(
                id=str(uuid.uuid4()),
                template_id=template_id,
                version_index=version_index,
                subject_pseudonym=subject_pseudonym,
                role=role,
                signer_name=signer_name,
                signer_email=s.get("signer_email"),
                meaning=s.get("meaning", "Consent Execution"),
                signature_data=s.get("signature_data"),
                signed_at=datetime.now(UTC),
                digest_sha256=sig_digest,
                lar_relationship=s.get("lar_relationship"),
                lar_authority_basis=s.get("lar_authority_basis"),
                created_by=created_by,
                reason_for_change=reason_for_change,
            )
            await self.signature_repo.save(sig_entity)

        # 4. Save granular selections
        if granular_selections:
            selection_entities = [
                GranularOptionSelectionEntity(
                    id=str(uuid.uuid4()),
                    consent_id=saved_consent.id,
                    subject_pseudonym=subject_pseudonym,
                    option_code=sel["option_code"],
                    selected=sel["selected"],
                    selected_at=server_timestamp,
                    created_by=created_by,
                    reason_for_change=reason_for_change,
                )
                for sel in granular_selections
            ]
            await self.granular_repo.save_selections(selection_entities)

        # 5. Record 21 CFR Part 11 Audit Log
        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=server_timestamp,
                    actor_id=created_by,
                    actor_role="subject",
                    action="CAPTURE_CONSENT",
                    document_id=saved_consent.id,
                    details=f"Captured consent for '{subject_pseudonym}' under template '{template_id}' v{version_index}.",
                    reason_for_change=reason_for_change,
                )
            )

        return saved_consent


# =========================================================================
# 7. Reconsent Service
# =========================================================================
class ReconsentService:
    """Application service for triggering cohort re-consent on protocol amendments."""

    def __init__(
        self,
        reconsent_repo: IReconsentRepository,
        consent_repo: ISubjectConsentRepository,
        audit_repo: IConsentAuditRepository | None = None,
        notification_dispatcher: Any | None = None,
    ) -> None:
        self.reconsent_repo = reconsent_repo
        self.consent_repo = consent_repo
        self.audit_repo = audit_repo
        self.notification_dispatcher = notification_dispatcher

    async def trigger_reconsent_for_active_cohort(
        self,
        study_id: str,
        site_id: str | None,
        template_id: str,
        prior_version_index: int,
        new_version_index: int,
        change_summary: str,
        substantive_changes: list[dict[str, Any]],
        deadline_at: datetime | None = None,
        created_by: str = "system",
        reason_for_change: str = "Protocol Amendment Re-Consent Trigger",
    ) -> list[ReconsentRequirementEntity]:
        # 1. Fetch active subjects who consented under prior versions
        active_consents = await self.consent_repo.list_subject_consents(
            study_id=study_id, site_id=site_id
        )

        requirements = []
        now = datetime.now(UTC)
        for consent in active_consents:
            req = ReconsentRequirementEntity(
                id=str(uuid.uuid4()),
                study_id=study_id,
                site_id=consent.site_id,
                template_id=template_id,
                prior_version_index=prior_version_index,
                new_version_index=new_version_index,
                subject_pseudonym=consent.subject_pseudonym,
                status="PENDING",
                change_summary=change_summary,
                substantive_changes=substantive_changes,
                deadline_at=deadline_at,
                completed_consent_id=None,
                created_at=now,
                created_by=created_by,
                reason_for_change=reason_for_change,
            )
            saved_req = await self.reconsent_repo.save(req)
            requirements.append(saved_req)

        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=now,
                    actor_id=created_by,
                    actor_role="sponsor_designer",
                    action="TRIGGER_RECONSENT",
                    document_id=f"{template_id}:v{new_version_index}",
                    details=f"Triggered re-consent requirements for {len(requirements)} active subjects in study '{study_id}'.",
                    reason_for_change=reason_for_change,
                )
            )

        # Dispatch automated notification events via HTTP client
        if self.notification_dispatcher:
            try:
                for req in requirements:
                    await self.notification_dispatcher(
                        {
                            "recipient_user_id": req.subject_pseudonym,
                            "category": "ALERTS",
                            "priority": "CRITICAL",
                            "channels": "IN_APP,EMAIL",
                            "message_content": (
                                f"URGENT: Protocol amendment re-consent required for study {study_id}. "
                                f"Version: {new_version_index}.0"
                            ),
                            "related_entity_id": req.id,
                            "related_entity_type": "RECONSENT_REQUIRED",
                        }
                    )
            except Exception:
                pass

        return requirements

    async def get_pending_reconsents(
        self, study_id: str, subject_pseudonym: str | None = None
    ) -> list[ReconsentRequirementEntity]:
        return await self.reconsent_repo.get_pending_requirements(
            study_id, subject_pseudonym
        )

    async def complete_reconsent_requirement(
        self,
        requirement_id: str,
        completed_consent_id: str | None = None,
        created_by: str = "system",
        reason_for_change: str = "Subject completed protocol re-consent signature",
    ) -> ReconsentRequirementEntity:
        req = await self.reconsent_repo.get_by_id(requirement_id)
        if not req:
            raise ValueError(f"Re-consent requirement '{requirement_id}' not found.")

        req.status = "COMPLETED"
        if completed_consent_id:
            req.completed_consent_id = completed_consent_id

        saved = await self.reconsent_repo.save(req)

        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="subject",
                    action="COMPLETE_RECONSENT",
                    document_id=saved.id,
                    details=f"Subject '{saved.subject_pseudonym}' completed re-consent requirement '{saved.id}'.",
                    reason_for_change=reason_for_change,
                )
            )

        return saved


# =========================================================================
# 8. Withdrawal Service
# =========================================================================
class WithdrawalService:
    """Application service for formal subject consent revocation and status locking."""

    def __init__(
        self,
        withdrawal_repo: IConsentWithdrawalRepository,
        consent_repo: ISubjectConsentRepository,
        audit_repo: IConsentAuditRepository | None = None,
    ) -> None:
        self.withdrawal_repo = withdrawal_repo
        self.consent_repo = consent_repo
        self.audit_repo = audit_repo

    async def withdraw_consent(
        self,
        study_id: str,
        site_id: str,
        subject_pseudonym: str,
        template_id: str,
        withdrawal_date: datetime,
        reason_category: str,
        reason_detail: str,
        scope: str = WithdrawalScope.STOP_ALL_DATA_COLLECTION,
        investigator_id: str | None = None,
        created_by: str = "system",
        reason_for_change: str = "Subject withdrawal from study",
    ) -> ConsentWithdrawalEntity:
        entity = ConsentWithdrawalEntity(
            id=str(uuid.uuid4()),
            study_id=study_id,
            site_id=site_id,
            subject_pseudonym=subject_pseudonym,
            template_id=template_id,
            withdrawal_date=withdrawal_date,
            reason_category=reason_category,
            reason_detail=reason_detail,
            scope=scope,
            acknowledged_by_investigator=True,
            investigator_id=investigator_id,
            created_at=datetime.now(UTC),
            created_by=created_by,
            reason_for_change=reason_for_change,
        )

        saved = await self.withdrawal_repo.save_withdrawal(entity)

        # Transition active subject consents to WITHDRAWN
        active_consents = await self.consent_repo.list_subject_consents(
            study_id=study_id,
            subject_pseudonym=subject_pseudonym,
        )
        for consent in active_consents:
            consent.status = "WITHDRAWN"
            consent.reason_for_change = f"Revoked: {reason_category} - {reason_detail}"
            await self.consent_repo.save(consent)

        if self.audit_repo:
            await self.audit_repo.save(
                ConsentAuditLogEntity(
                    id=str(uuid.uuid4()),
                    timestamp=datetime.now(UTC),
                    actor_id=created_by,
                    actor_role="investigator",
                    action="WITHDRAW_CONSENT",
                    document_id=saved.id,
                    details=f"Subject '{subject_pseudonym}' withdrew consent. Scope: {scope}. Category: {reason_category}.",
                    reason_for_change=reason_for_change,
                )
            )

        return saved


# =========================================================================
# 9. Inspection & Export Service
# =========================================================================
class InspectionExportService:
    """Application service for CDISC ODM XML and verifiable certificate exports."""

    def __init__(
        self,
        template_repo: IConsentTemplateRepository,
        clause_repo: IConsentClauseRepository,
        signature_repo: IConsentSignatureRepository,
        granular_repo: IGranularOptionRepository,
        audit_repo: IConsentAuditRepository,
        consent_repo: ISubjectConsentRepository | None = None,
    ) -> None:
        self.template_repo = template_repo
        self.clause_repo = clause_repo
        self.signature_repo = signature_repo
        self.granular_repo = granular_repo
        self.audit_repo = audit_repo
        self.consent_repo = consent_repo

    async def export_cdisc_odm(
        self,
        study_id: str,
        subject_pseudonym: str,
        template_id: str,
        version_index: int,
    ) -> str:
        tpl = await self.template_repo.get_by_template_and_version(
            template_id, version_index
        )
        if not tpl:
            raise TemplateNotFoundError(
                f"Template '{template_id}' v{version_index} not found."
            )

        signatures = await self.signature_repo.get_signatures_for_template_version(
            template_id, version_index, subject_pseudonym
        )
        sig_dicts = [
            {
                "role": s.role,
                "signer_name": s.signer_name,
                "signed_at": s.signed_at.isoformat(),
                "meaning": s.meaning,
                "digest_sha256": s.digest_sha256,
                "created_by": s.created_by,
            }
            for s in signatures
        ]

        granular_selections = await self.granular_repo.get_selections_for_consent(
            f"{template_id}:{version_index}:{subject_pseudonym}"
        )
        gran_dicts = [
            {
                "option_code": g.option_code,
                "selected": g.selected,
                "selected_at": g.selected_at.isoformat(),
            }
            for g in granular_selections
        ]

        audit_logs = await self.audit_repo.list_logs(
            document_id=f"{template_id}:{version_index}:{subject_pseudonym}"
        )
        audit_dicts = [
            {
                "id": a.id,
                "timestamp": a.timestamp.isoformat(),
                "actor_id": a.actor_id,
                "actor_role": a.actor_role,
                "action": a.action,
                "reason_for_change": a.reason_for_change,
            }
            for a in audit_logs
        ]

        return generate_econsent_cdisc_odm_xml(
            study_id=study_id,
            subject_pseudonym=subject_pseudonym,
            template_id=template_id,
            template_name=tpl.template_name,
            protocol_version=tpl.protocol_version,
            version_index=version_index,
            signatures=sig_dicts,
            granular_selections=gran_dicts,
            audit_logs=audit_dicts,
        )

    async def export_verifiable_html_certificate(
        self,
        study_id: str,
        subject_pseudonym: str,
        template_id: str,
        version_index: int,
    ) -> str:
        tpl = await self.template_repo.get_by_template_and_version(
            template_id, version_index
        )
        if not tpl:
            raise TemplateNotFoundError(
                f"Template '{template_id}' v{version_index} not found."
            )

        clauses = []
        for cid in tpl.clauses:
            c = await self.clause_repo.get_latest_by_clause_id(cid)
            if c:
                clauses.append({"title": c.title, "text": c.text})

        signatures = await self.signature_repo.get_signatures_for_template_version(
            template_id, version_index, subject_pseudonym
        )
        sig_dicts = [
            {
                "role": s.role,
                "signer_name": s.signer_name,
                "signed_at": s.signed_at.strftime("%Y-%m-%d %H:%M:%S UTC"),
                "meaning": s.meaning,
                "digest_sha256": s.digest_sha256,
                "lar_relationship": s.lar_relationship,
                "created_by": s.created_by,
            }
            for s in signatures
        ]

        granular_selections = await self.granular_repo.get_selections_for_consent(
            f"{template_id}:{version_index}:{subject_pseudonym}"
        )
        gran_dicts = [
            {
                "option_code": g.option_code,
                "selected": g.selected,
            }
            for g in granular_selections
        ]

        audit_logs = await self.audit_repo.list_logs(
            document_id=f"{template_id}:{version_index}:{subject_pseudonym}"
        )
        audit_dicts = [
            {
                "timestamp": a.timestamp.strftime("%Y-%m-%d %H:%M:%S UTC"),
                "actor_id": a.actor_id,
                "actor_role": a.actor_role,
                "action": a.action,
                "reason_for_change": a.reason_for_change,
            }
            for a in audit_logs
        ]

        return render_verifiable_consent_html(
            study_id=study_id,
            site_id="SITE-DEFAULT",
            subject_pseudonym=subject_pseudonym,
            template_id=template_id,
            template_name=tpl.template_name,
            protocol_version=tpl.protocol_version,
            version_index=version_index,
            clauses=clauses,
            signatures=sig_dicts,
            granular_selections=gran_dicts,
            audit_logs=audit_dicts,
        )
