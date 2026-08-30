from apps.execution.presentation.routers.amendments import router as amendments_router
from apps.execution.presentation.routers.anomalies import router as anomalies_router
from apps.execution.presentation.routers.anonymization import (
    router as anonymization_router,
)
from apps.execution.presentation.routers.auditor import router as auditor_router
from apps.execution.presentation.routers.cec import router as cec_router
from apps.execution.presentation.routers.devices import router as devices_router
from apps.execution.presentation.routers.dictionaries import (
    router as dictionaries_router,
)
from apps.execution.presentation.routers.doa import router as doa_router
from apps.execution.presentation.routers.documents import router as documents_router
from apps.execution.presentation.routers.eisf import router as eisf_router
from apps.execution.presentation.routers.exports import router as exports_router
from apps.execution.presentation.routers.forms import router as forms_router
from apps.execution.presentation.routers.labs import router as labs_router
from apps.execution.presentation.routers.locks import router as locks_router
from apps.execution.presentation.routers.observations import (
    router as observations_router,
)
from apps.execution.presentation.routers.offline import router as offline_router
from apps.execution.presentation.routers.queries import router as queries_router
from apps.execution.presentation.routers.randomization import (
    router as randomization_router,
)
from apps.execution.presentation.routers.rtsm import router as rtsm_router
from apps.execution.presentation.routers.rules import router as rules_router
from apps.execution.presentation.routers.safety import router as safety_router
from apps.execution.presentation.routers.sdv import router as sdv_router
from apps.execution.presentation.routers.signatures import router as signatures_router
from apps.execution.presentation.routers.subjects import router as subjects_router
from apps.execution.presentation.routers.unblinding import router as unblinding_router
from apps.execution.presentation.routers.visits import router as visits_router

__all__ = [
    "amendments_router",
    "anomalies_router",
    "anonymization_router",
    "auditor_router",
    "cec_router",
    "devices_router",
    "dictionaries_router",
    "doa_router",
    "documents_router",
    "eisf_router",
    "exports_router",
    "forms_router",
    "labs_router",
    "locks_router",
    "observations_router",
    "offline_router",
    "queries_router",
    "randomization_router",
    "rtsm_router",
    "rules_router",
    "safety_router",
    "sdv_router",
    "signatures_router",
    "subjects_router",
    "unblinding_router",
    "visits_router",
]
