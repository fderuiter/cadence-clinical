from datetime import UTC, datetime

from apps.ctms.domain.models import (
    CTMSAuditLogEntity,
    ETMFSyncRecordEntity,
)
from apps.ctms.domain.ports import (
    ICTMSDelegationRepository,
    IETMFClientPort,
    IETMFSyncRepository,
)


class ETMFSyncService:
    """Application service for orchestrating CTMS artifact synchronization with eTMF."""

    def __init__(
        self,
        sync_repo: IETMFSyncRepository,
        etmf_client: IETMFClientPort,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.sync_repo = sync_repo
        self.etmf_client = etmf_client
        self.doa_repo = doa_repo

    async def sync_artifact_to_etmf(
        self,
        study_id: str,
        site_id: str,
        artifact_type: str,  # MVR_REPORT, DOA_LOG, GREENLIGHT_PACKAGE, DEVIATION_REPORT, IP_DESTRUCTION_CERT
        source_record_id: str,
        title: str,
        content_text: str,
        dia_zone: str,
        dia_section: str,
        dia_artifact: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> ETMFSyncRecordEntity:
        roles_list = [r.strip() for r in user_roles.split(",") if r.strip()]

        res = await self.etmf_client.push_document(
            study_id=study_id,
            site_id=site_id,
            title=title,
            content_text=content_text,
            dia_zone=dia_zone,
            dia_section=dia_section,
            dia_artifact=dia_artifact,
            user_id=user_id,
            user_roles=roles_list,
            reason_for_change=reason_for_change,
        )

        doc_id = res.get("document_id", f"etmf-doc-{source_record_id}")
        sync_status = res.get("status", "SYNCED")

        record = ETMFSyncRecordEntity(
            study_id=study_id,
            site_id=site_id,
            artifact_type=artifact_type,
            source_record_id=source_record_id,
            etmf_document_id=doc_id,
            dia_zone=dia_zone,
            dia_section=dia_section,
            dia_artifact=dia_artifact,
            sync_status=sync_status,
            synced_at=datetime.now(UTC).isoformat(),
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.sync_repo.save_sync_record(record)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="ETMF_ARTIFACT_SYNCED",
                details=f"Pushed {artifact_type} ({source_record_id}) to eTMF Zone {dia_zone} Section {dia_section}. Doc ID: {doc_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_sync_records(
        self, study_id: str, site_id: str | None = None
    ) -> list[ETMFSyncRecordEntity]:
        return await self.sync_repo.list_sync_records(study_id, site_id)
