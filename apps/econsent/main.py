import os

from fastapi import FastAPI

from apps.econsent.adapters.cache import (
    ApprovedTranslationCache,
    get_approved_template_translation,
)
from apps.econsent.adapters.comprehension import (
    submit_comprehension_answers,
)
from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import Base
from apps.econsent.adapters.workers.archival_worker import (
    dispatcher_lifecycle_worker,
    econsent_shutdown,
    econsent_startup,
    poll_and_dispatch,
    start_dispatcher,
    stop_dispatcher,
)
from apps.econsent.domain.evaluator import (
    evaluate_comprehension,
)
from apps.econsent.presentation.dtos import (
    ArchivalDeliveryResponse,
    ClauseDiffDTO,
    ClauseHarmonizationApplyRequest,
    ClauseHarmonizationApplyResponse,
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
    ConsentWithdrawalRequest,
    ConsentWithdrawalResponse,
    GranularOptionCreate,
    GranularOptionResponse,
    JargonSubstitutionDTO,
    ReadabilityAnalysisRequest,
    ReadabilityAnalysisResponse,
    ReadabilityHarmonizationRequest,
    ReadabilityHarmonizationResponse,
    ReadabilityMetricsDTO,
    ReconsentRequirementResponse,
    ReconsentTriggerRequest,
    SubjectConsentCaptureRequest,
    SubjectConsentResponse,
    SubjectConsentStatusResponse,
    TemplateDiffResponse,
    TranslationTransitionRequest,
)
from apps.econsent.presentation.routers.audit import (
    router as audit_router,
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
from apps.econsent.presentation.routers.export import (
    router as export_router,
)
from apps.econsent.presentation.routers.granular import (
    router as granular_router,
)
from apps.econsent.presentation.routers.readability import (
    router as readability_router,
)
from apps.econsent.presentation.routers.reconsent import (
    router as reconsent_router,
)
from apps.econsent.presentation.routers.withdrawal import (
    router as withdrawal_router,
)
from packages.database import get_relational_db_lifespan
from packages.hexagonal import register_rfc7807_handlers
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
register_rfc7807_handlers(app)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "econsent"}


app.include_router(econsent_router)
app.include_router(reconsent_router)
app.include_router(withdrawal_router)
app.include_router(export_router)
app.include_router(granular_router)
app.include_router(audit_router)
app.include_router(readability_router)

__all__ = [
    "ApprovedTranslationCache",
    "ArchivalDeliveryResponse",
    "ClauseDiffDTO",
    "ClauseHarmonizationApplyRequest",
    "ClauseHarmonizationApplyResponse",
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
    "ConsentWithdrawalRequest",
    "ConsentWithdrawalResponse",
    "GranularOptionCreate",
    "GranularOptionResponse",
    "JargonSubstitutionDTO",
    "ReadabilityAnalysisRequest",
    "ReadabilityAnalysisResponse",
    "ReadabilityHarmonizationRequest",
    "ReadabilityHarmonizationResponse",
    "ReadabilityMetricsDTO",
    "ReconsentRequirementResponse",
    "ReconsentTriggerRequest",
    "SubjectConsentCaptureRequest",
    "SubjectConsentResponse",
    "SubjectConsentStatusResponse",
    "TemplateDiffResponse",
    "TranslationTransitionRequest",
    "app",
    "approved_translation_cache",
    "audit_router",
    "dispatcher_lifecycle_worker",
    "econsent_router",
    "econsent_shutdown",
    "econsent_startup",
    "evaluate_comprehension",
    "export_router",
    "fetch_composed_translation_from_db",
    "get_approved_template_translation",
    "granular_router",
    "map_document_to_response",
    "poll_and_dispatch",
    "readability_router",
    "reconsent_router",
    "start_dispatcher",
    "stop_dispatcher",
    "submit_comprehension_answers",
    "withdrawal_router",
    "write_audit_log",
]
