from apps.execution.presentation.routers.amendments import router as amendments_router
from apps.execution.presentation.routers.anonymization import (
    router as anonymization_router,
)
from apps.execution.presentation.routers.auditor import router as auditor_router
from apps.execution.presentation.routers.doa import router as doa_router
from apps.execution.presentation.routers.documents import router as documents_router
from apps.execution.presentation.routers.eisf import router as eisf_router
from apps.execution.presentation.routers.labs import router as labs_router
from apps.execution.presentation.routers.locks import router as locks_router
from apps.execution.presentation.routers.offline import router as offline_router
from apps.execution.presentation.routers.safety import router as safety_router
from apps.execution.presentation.routers.sdv import router as sdv_router
from apps.execution.presentation.routers.signatures import router as signatures_router

__all__ = [
    "amendments_router",
    "anonymization_router",
    "auditor_router",
    "doa_router",
    "documents_router",
    "eisf_router",
    "labs_router",
    "locks_router",
    "offline_router",
    "safety_router",
    "sdv_router",
    "signatures_router",
]
