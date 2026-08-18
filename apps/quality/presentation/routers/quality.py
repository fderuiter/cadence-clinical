"""FastAPI Router for Quality microservice aggregating all modular domain routers."""

from fastapi import APIRouter

from apps.quality.presentation.routers.audit_logs import router as audit_logs_router
from apps.quality.presentation.routers.audits import (
    map_audit_to_response,
    map_finding_to_response,
)
from apps.quality.presentation.routers.audits import (
    router as audits_router,
)
from apps.quality.presentation.routers.capas import (
    authorize_quality_oversight,
    authorize_quality_write,
    get_quality_service,
    get_user_context,
    map_action_item_to_response,
    map_capa_to_response,
    map_effectiveness_check_to_response,
)
from apps.quality.presentation.routers.capas import (
    router as capas_router,
)
from apps.quality.presentation.routers.deviations import (
    map_deviation_to_response,
)
from apps.quality.presentation.routers.deviations import (
    router as deviations_router,
)
from apps.quality.presentation.routers.rbqm import (
    map_ctq_to_response,
    map_kri_definition_to_response,
    map_kri_eval_to_response,
    map_profile_to_response,
    map_qtl_breach_to_response,
    map_qtl_to_response,
)
from apps.quality.presentation.routers.rbqm import (
    router as rbqm_router,
)
from apps.quality.presentation.routers.rca import (
    map_rca_to_response,
)
from apps.quality.presentation.routers.rca import (
    router as rca_router,
)
from apps.quality.presentation.routers.serious_breaches import (
    map_breach_to_response,
)
from apps.quality.presentation.routers.serious_breaches import (
    router as serious_breaches_router,
)

router = APIRouter()

# Include all modular routers
router.include_router(deviations_router)
router.include_router(rca_router)
router.include_router(capas_router)
router.include_router(rbqm_router)
router.include_router(audits_router)
router.include_router(serious_breaches_router)
router.include_router(audit_logs_router)

__all__ = [
    "authorize_quality_oversight",
    "authorize_quality_write",
    "get_quality_service",
    "get_user_context",
    "map_action_item_to_response",
    "map_audit_to_response",
    "map_breach_to_response",
    "map_capa_to_response",
    "map_ctq_to_response",
    "map_deviation_to_response",
    "map_effectiveness_check_to_response",
    "map_finding_to_response",
    "map_kri_definition_to_response",
    "map_kri_eval_to_response",
    "map_profile_to_response",
    "map_qtl_breach_to_response",
    "map_qtl_to_response",
    "map_rca_to_response",
    "router",
]
