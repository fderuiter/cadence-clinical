from datetime import UTC, datetime

from apps.ctms.domain.exceptions import GreenlightPrerequisiteError
from apps.ctms.domain.models import (
    CountryRegulatoryMilestoneEntity,
    CTMSAuditLogEntity,
    EssentialDocumentEntity,
    SiteGreenlightGateEntity,
)
from apps.ctms.domain.ports import ICTMSDelegationRepository, ISiteStartupRepository


class SiteStartupService:
    """Application service for Site Startup, Regulatory Milestones, and Greenlight Gating."""

    def __init__(
        self,
        startup_repo: ISiteStartupRepository,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.startup_repo = startup_repo
        self.doa_repo = doa_repo

    async def create_or_update_country_milestone(
        self,
        study_id: str,
        country_code: str,
        milestone_type: str,
        status: str,
        planned_date: str | None,
        actual_date: str | None,
        approval_number: str | None,
        authority_name: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        milestone_id: str | None = None,
    ) -> CountryRegulatoryMilestoneEntity:
        entity = CountryRegulatoryMilestoneEntity(
            id=milestone_id,
            study_id=study_id,
            country_code=country_code.upper(),
            milestone_type=milestone_type.upper(),
            planned_date=planned_date,
            actual_date=actual_date,
            status=status.upper(),
            approval_number=approval_number,
            authority_name=authority_name,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.startup_repo.save_country_milestone(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="REGULATORY_MILESTONE_UPDATED",
                details=f"Updated milestone {milestone_type} for country {country_code} in study {study_id}. Status: {status}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_country_milestones(
        self, study_id: str, country_code: str | None = None
    ) -> list[CountryRegulatoryMilestoneEntity]:
        return await self.startup_repo.list_country_milestones(study_id, country_code)

    async def submit_essential_document(
        self,
        study_id: str,
        site_id: str,
        document_type: str,
        file_name: str,
        file_hash: str,
        expiration_date: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> EssentialDocumentEntity:
        entity = EssentialDocumentEntity(
            study_id=study_id,
            site_id=site_id,
            document_type=document_type.upper(),
            file_name=file_name,
            file_hash=file_hash,
            expiration_date=expiration_date,
            status="SUBMITTED",
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.startup_repo.save_essential_document(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="ESSENTIAL_DOCUMENT_SUBMITTED",
                details=f"Submitted essential document {document_type} ({file_name}) for site {site_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def review_essential_document(
        self,
        document_id: str,
        status: str,  # APPROVED, REJECTED, EXPIRED
        review_notes: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> EssentialDocumentEntity:
        doc = await self.startup_repo.get_essential_document(document_id)
        if not doc:
            raise GreenlightPrerequisiteError(
                f"Essential document {document_id} not found"
            )

        doc.status = status.upper()
        doc.review_notes = review_notes
        doc.reviewed_by = user_id
        doc.reviewed_at = datetime.now(UTC).isoformat()
        doc.version_index += 1
        doc.reason_for_change = reason_for_change

        saved = await self.startup_repo.save_essential_document(doc)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="ESSENTIAL_DOCUMENT_REVIEWED",
                details=f"Reviewed document {document_id} ({doc.document_type}). Status: {status}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_essential_documents(
        self, study_id: str, site_id: str | None = None
    ) -> list[EssentialDocumentEntity]:
        return await self.startup_repo.list_essential_documents(study_id, site_id)

    async def evaluate_site_greenlight(
        self, study_id: str, site_id: str
    ) -> SiteGreenlightGateEntity:
        docs = await self.startup_repo.list_essential_documents(study_id, site_id)
        approved_types = {d.document_type for d in docs if d.status == "APPROVED"}

        contract_approved = "SITE_CONTRACT" in approved_types
        irb_approved = (
            "LOCAL_IRB_APPROVAL" in approved_types or "IRB_APPROVAL" in approved_types
        )
        form_1572_approved = (
            "FDA_1572" in approved_types or "INVESTIGATOR_STATEMENT" in approved_types
        )
        doa_signed_off = (
            "GCP_CERTIFICATE" in approved_types or "CV_INVESTIGATOR" in approved_types
        )
        ip_ready = (
            "IP_RELEASE_AUTHORIZATION" in approved_types or len(approved_types) >= 3
        )

        all_passed = contract_approved and irb_approved and form_1572_approved

        gate = await self.startup_repo.get_greenlight_gate(site_id)
        if not gate:
            gate = SiteGreenlightGateEntity(
                study_id=study_id,
                site_id=site_id,
                overall_status="APPROVED" if all_passed else "PENDING",
                contract_approved=contract_approved,
                irb_approved=irb_approved,
                form_1572_approved=form_1572_approved,
                doa_signed_off=doa_signed_off,
                ip_ready=ip_ready,
                created_by="system",
                reason_for_change="Automated greenlight evaluation",
                version_index=1,
            )
        else:
            gate.contract_approved = contract_approved
            gate.irb_approved = irb_approved
            gate.form_1572_approved = form_1572_approved
            gate.doa_signed_off = doa_signed_off
            gate.ip_ready = ip_ready
            gate.overall_status = "APPROVED" if all_passed else "PENDING"
            gate.version_index += 1
            gate.reason_for_change = "Automated greenlight re-evaluation"

        return await self.startup_repo.save_greenlight_gate(gate)

    async def certify_site_greenlight(
        self,
        study_id: str,
        site_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> SiteGreenlightGateEntity:
        gate = await self.evaluate_site_greenlight(study_id, site_id)
        if not (
            gate.contract_approved and gate.irb_approved and gate.form_1572_approved
        ):
            missing = []
            if not gate.contract_approved:
                missing.append("SITE_CONTRACT")
            if not gate.irb_approved:
                missing.append("LOCAL_IRB_APPROVAL")
            if not gate.form_1572_approved:
                missing.append("FDA_1572")
            raise GreenlightPrerequisiteError(
                f"Cannot certify greenlight for site {site_id}. Missing mandatory approvals: {', '.join(missing)}"
            )

        gate.overall_status = "APPROVED"
        gate.greenlight_certified_by = user_id
        gate.greenlight_certified_at = datetime.now(UTC).isoformat()
        gate.version_index += 1
        gate.reason_for_change = reason_for_change

        saved = await self.startup_repo.save_greenlight_gate(gate)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="SITE_GREENLIGHT_CERTIFIED",
                details=f"Site {site_id} certified for GREENLIGHT activation by {user_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved
