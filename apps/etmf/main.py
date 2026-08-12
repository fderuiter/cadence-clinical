import os

from fastapi import FastAPI

from apps.etmf.domain.acl import ProtocolVersionRef, ProtocolVersionRefDTO
from apps.etmf.domain.ports import ETMFRepositoryPort
from apps.etmf.infrastructure.database import db_manager, transactional
from apps.etmf.infrastructure.models import (
    Base,
    DocumentExpirationAlertState,
    DocumentQCTransition,
    DocumentStatus,
    ExpectedDocument,
    TMFAuditLedgerSeal,
    TMFAuditLog,
    TMFDocument,
    TMFDocumentType,
    is_site_level_artifact,
)
from apps.etmf.infrastructure.repositories import SQLETMFRepository
from apps.etmf.presentation.dtos import (
    ArtifactDetail,
    AuditLogResponse,
    AutomatedRedactRequest,
    AutomatedRedactResponse,
    BinderArtifactNode,
    BinderSectionNode,
    BinderStructureResponse,
    BinderZoneNode,
    CompletenessResponse,
    DocumentExpirationUpdate,
    DocumentResponse,
    DocumentVersionEntry,
    DocumentVersionsResponse,
    ExpectedDocumentCreate,
    ExpectedDocumentResponse,
    IngestionRequest,
    ManualRedactRequest,
    ManualRedactResponse,
    PaginatedAuditLogResponse,
    RedactRequest,
    SignDocumentRequest,
    StudyArchiveItemResult,
    StudyArchiveRequest,
    StudyArchiveResponse,
    TransitionRequest,
    TransitionResponse,
    to_document_response,
)
from apps.etmf.presentation.routers.etmf import (
    auto_redact_document_endpoint,
    bulk_archive_study_documents,
    check_completeness,
    create_expectation,
    download_document,
    download_watermarked_document,
    enforce_document_site_visibility,
    export_regulatory_binder,
    get_artifact_history,
    get_audit_trail,
    get_binder_structure,
    get_document_qc_history,
    get_document_transition_history,
    get_document_versions,
    get_etmf_repository,
    ingest_document,
    list_documents,
    list_expectations,
    manual_redact_document_endpoint,
    map_artifact_to_tmf,
    normalize_milestone,
    parse_recipient_address,
    receive_inbound_email,
    redact_document_endpoint,
    resolve_binder_hint,
    seed_default_edl,
    sign_document_endpoint,
    transition_document_status_endpoint,
    update_document_expiration_endpoint,
    update_expectation,
    view_document,
    write_audit_log,
)
from apps.etmf.presentation.routers.etmf import (
    router as etmf_router,
)
from apps.etmf.routers.archive import router as archive_router
from apps.etmf.routers.taxonomy import router as taxonomy_router
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("ETMF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("etmf", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})


async def etmf_startup() -> None:
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        from apps.etmf.database.context import current_session

        token = current_session.set(session)
        try:
            repo = get_etmf_repository()
            for study_id in [
                "study_001",
                "study_abc",
                "study_xyz",
                "study_123",
                "study_111",
            ]:
                for milestone in ["INITIATION", "CONDUCT", "CLOSEOUT"]:
                    await seed_default_edl(repo, study_id, milestone)
            await session.commit()
        finally:
            current_session.reset(token)

    from apps.etmf.sealer import start_background_etmf_sealer

    await start_background_etmf_sealer(db_manager.get_session_maker())

    from apps.etmf.expiration_scanner import start_background_etmf_expiration_scanner

    await start_background_etmf_expiration_scanner(db_manager.get_session_maker())

    from apps.etmf.workers.outbox_worker import start_outbox_worker

    start_outbox_worker()


async def etmf_shutdown() -> None:
    from apps.etmf.workers.outbox_worker import stop_outbox_worker

    stop_outbox_worker()

    from apps.etmf.sealer import stop_background_etmf_sealer

    await stop_background_etmf_sealer()

    from apps.etmf.expiration_scanner import stop_background_etmf_expiration_scanner

    await stop_background_etmf_expiration_scanner()


BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


validate_branding("etmf")
app = FastAPI(
    title=f"{BRAND_NAME} - Event-Driven eTMF Module",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
        startup_hooks=[etmf_startup],
        shutdown_hooks=[etmf_shutdown],
    ),
)

app.add_middleware(GatewayAuthMiddleware)

app.include_router(archive_router)
app.include_router(taxonomy_router)
app.include_router(etmf_router)


_repo_instance = SQLETMFRepository()


__all__ = [
    "ArtifactDetail",
    "AuditLogResponse",
    "AutomatedRedactRequest",
    "AutomatedRedactResponse",
    "Base",
    "BinderArtifactNode",
    "BinderSectionNode",
    "BinderStructureResponse",
    "BinderZoneNode",
    "CompletenessResponse",
    "DocumentExpirationAlertState",
    "DocumentExpirationUpdate",
    "DocumentQCTransition",
    "DocumentResponse",
    "DocumentStatus",
    "DocumentVersionEntry",
    "DocumentVersionsResponse",
    "ETMFRepositoryPort",
    "ExpectedDocument",
    "ExpectedDocumentCreate",
    "ExpectedDocumentResponse",
    "IngestionRequest",
    "ManualRedactRequest",
    "ManualRedactResponse",
    "PaginatedAuditLogResponse",
    "ProtocolVersionRef",
    "ProtocolVersionRefDTO",
    "RedactRequest",
    "SQLETMFRepository",
    "SignDocumentRequest",
    "StudyArchiveItemResult",
    "StudyArchiveRequest",
    "StudyArchiveResponse",
    "TMFAuditLedgerSeal",
    "TMFAuditLog",
    "TMFDocument",
    "TMFDocumentType",
    "TransitionRequest",
    "TransitionResponse",
    "_repo_instance",
    "app",
    "auto_redact_document_endpoint",
    "bulk_archive_study_documents",
    "check_completeness",
    "create_expectation",
    "db_manager",
    "download_document",
    "download_watermarked_document",
    "enforce_document_site_visibility",
    "export_regulatory_binder",
    "get_artifact_history",
    "get_audit_trail",
    "get_binder_structure",
    "get_document_qc_history",
    "get_document_transition_history",
    "get_document_versions",
    "get_etmf_repository",
    "ingest_document",
    "is_site_level_artifact",
    "list_documents",
    "list_expectations",
    "manual_redact_document_endpoint",
    "map_artifact_to_tmf",
    "normalize_milestone",
    "parse_recipient_address",
    "receive_inbound_email",
    "redact_document_endpoint",
    "resolve_binder_hint",
    "seed_default_edl",
    "sign_document_endpoint",
    "to_document_response",
    "transactional",
    "transition_document_status_endpoint",
    "update_document_expiration_endpoint",
    "update_expectation",
    "view_document",
    "write_audit_log",
]
