import os

from fastapi import FastAPI

from apps.econsent.domain.evaluator import (
    evaluate_comprehension,
)
from apps.econsent.infrastructure.cache import (
    ApprovedTranslationCache,
    get_approved_template_translation,
)
from apps.econsent.infrastructure.database import db_manager
from apps.econsent.infrastructure.models import Base
from apps.econsent.infrastructure.services import (
    submit_comprehension_answers,
)
from apps.econsent.presentation.dtos import (
    ArchivalDeliveryResponse,
    ComposedClauseResponse,
    ComposedTemplateResponse,
    ComprehensionCheckCreate,
    ComprehensionCheckResponse,
    ComprehensionSubmissionRequest,
    ComprehensionSubmissionResponse,
    ConsentClauseCreate,
    ConsentClauseResponse,
    ConsentClauseUpdate,
    ConsentDocumentCreate,
    ConsentDocumentResponse,
    ConsentSignatureRequest,
    ConsentSignatureResponse,
    ConsentTemplateCreate,
    ConsentTemplateResponse,
    ConsentTemplateUpdate,
    ConsentTranslationCreate,
    ConsentTranslationResponse,
    ConsentTranslationUpdate,
    SubjectConsentCaptureRequest,
    SubjectConsentResponse,
    SubjectConsentStatusResponse,
    TranslationTransitionRequest,
)
from apps.econsent.presentation.routers.econsent import (
    approved_translation_cache,
    fetch_composed_translation_from_db,
    map_document_to_response,
    write_audit_log,
)
from apps.econsent.presentation.routers.econsent import (
    router as econsent_router,
)
from apps.econsent.workers.archival_worker import (
    dispatcher_lifecycle_worker,
    econsent_shutdown,
    econsent_startup,
    poll_and_dispatch,
    start_dispatcher,
    stop_dispatcher,
)
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("ECONSENT_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("econsent", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


validate_branding("econsent")
app = FastAPI(
    title=f"{BRAND_NAME} - eConsent",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
        startup_hooks=[econsent_startup],
        shutdown_hooks=[econsent_shutdown],
    ),
)

app.add_middleware(GatewayAuthMiddleware)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "econsent"}


app.include_router(econsent_router)

__all__ = [
    "ApprovedTranslationCache",
    "ArchivalDeliveryResponse",
    "ComposedClauseResponse",
    "ComposedTemplateResponse",
    "ComprehensionCheckCreate",
    "ComprehensionCheckResponse",
    "ComprehensionSubmissionRequest",
    "ComprehensionSubmissionResponse",
    "ConsentClauseCreate",
    "ConsentClauseResponse",
    "ConsentClauseUpdate",
    "ConsentDocumentCreate",
    "ConsentDocumentResponse",
    "ConsentSignatureRequest",
    "ConsentSignatureResponse",
    "ConsentTemplateCreate",
    "ConsentTemplateResponse",
    "ConsentTemplateUpdate",
    "ConsentTranslationCreate",
    "ConsentTranslationResponse",
    "ConsentTranslationUpdate",
    "SubjectConsentCaptureRequest",
    "SubjectConsentResponse",
    "SubjectConsentStatusResponse",
    "TranslationTransitionRequest",
    "app",
    "approved_translation_cache",
    "dispatcher_lifecycle_worker",
    "econsent_shutdown",
    "econsent_startup",
    "evaluate_comprehension",
    "fetch_composed_translation_from_db",
    "get_approved_template_translation",
    "map_document_to_response",
    "poll_and_dispatch",
    "start_dispatcher",
    "stop_dispatcher",
    "submit_comprehension_answers",
    "write_audit_log",
]
