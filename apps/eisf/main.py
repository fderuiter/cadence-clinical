import os

from fastapi import FastAPI

from apps.eisf.domain.eisf_transport_models import (
    EISFDocumentDetail,
    EISFDocumentUploadRequest,
    EISFFolderNode,
)
from apps.eisf.domain.ports import EISFRepositoryPort
from apps.eisf.infrastructure.database import db_manager
from apps.eisf.infrastructure.models import Base, ISFAuditLog, ISFDocument
from apps.eisf.infrastructure.repositories import SQLEISFRepository
from apps.eisf.presentation.dtos import (
    BinderCompletenessResponse,
    BinderSectionStatus,
    DocumentCreate,
    DocumentResponse,
    DocumentUpdate,
    EISFIngestionRequest,
    EISFSyncItem,
    EISFSyncRequest,
    EISFSyncResponse,
)
from apps.eisf.presentation.routers.eisf import (
    REQUIRED_BINDER_SECTIONS,
    create_document,
    delete_document,
    download_document,
    download_site_document,
    enforce_document_site_visibility,
    enforce_site_isolation,
    get_binder_completeness,
    get_document,
    get_site_binder_endpoint,
    get_site_document_detail,
    get_site_eisf_binder,
    ingest_document,
    list_documents,
    propagate_to_etmf,
    sync_documents,
    update_document,
    upload_site_document,
    write_audit_log,
    write_local_audit_log,
)
from apps.eisf.presentation.routers.eisf import (
    router as eisf_router,
)
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("EISF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("eisf", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})

BRAND_NAME, BRAND_DOMAIN = validate_branding("eisf")


app = FastAPI(
    title=f"{BRAND_NAME} - eISF Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

app.add_middleware(GatewayAuthMiddleware)


_repo_instance = SQLEISFRepository()


def get_eisf_repository() -> EISFRepositoryPort:
    return _repo_instance


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "eisf"}


app.include_router(eisf_router)

__all__ = [
    "BinderCompletenessResponse",
    "BinderSectionStatus",
    "DocumentCreate",
    "DocumentResponse",
    "DocumentUpdate",
    "EISFDocumentDetail",
    "EISFDocumentUploadRequest",
    "EISFFolderNode",
    "EISFIngestionRequest",
    "EISFSyncItem",
    "EISFSyncRequest",
    "EISFSyncResponse",
    "ISFAuditLog",
    "ISFDocument",
    "REQUIRED_BINDER_SECTIONS",
    "SQLEISFRepository",
    "_repo_instance",
    "app",
    "create_document",
    "delete_document",
    "download_document",
    "download_site_document",
    "enforce_document_site_visibility",
    "enforce_site_isolation",
    "get_binder_completeness",
    "get_document",
    "get_eisf_repository",
    "get_site_binder_endpoint",
    "get_site_document_detail",
    "get_site_eisf_binder",
    "ingest_document",
    "list_documents",
    "propagate_to_etmf",
    "sync_documents",
    "update_document",
    "upload_site_document",
    "write_audit_log",
    "write_local_audit_log",
]
