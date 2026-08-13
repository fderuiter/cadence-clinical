from fastapi import APIRouter

from apps.execution.presentation.routers.amendments import router as amendments_router
from apps.execution.presentation.routers.anonymization import (
    router as anonymization_router,
)
from apps.execution.presentation.routers.auditor import router as auditor_router
from apps.execution.presentation.routers.doa import router as doa_router
from apps.execution.presentation.routers.documents import router as documents_router
from apps.execution.presentation.routers.eisf import router as eisf_router
from apps.execution.presentation.routers.locks import router as locks_router
from apps.execution.presentation.routers.offline import router as offline_router
from apps.execution.presentation.routers.queries import router as queries_router
from apps.execution.presentation.routers.safety import router as safety_router
from apps.execution.presentation.routers.sdv import router as sdv_router
from apps.execution.presentation.routers.signatures import router as signatures_router
from apps.execution.presentation.routers.unblinding import router as unblinding_router

edc_subrouter = APIRouter()

edc_subrouter.include_router(locks_router)
edc_subrouter.include_router(signatures_router)
edc_subrouter.include_router(amendments_router)
edc_subrouter.include_router(auditor_router)
edc_subrouter.include_router(safety_router)
edc_subrouter.include_router(eisf_router)
edc_subrouter.include_router(anonymization_router)
edc_subrouter.include_router(doa_router)
edc_subrouter.include_router(offline_router)
edc_subrouter.include_router(documents_router)
edc_subrouter.include_router(sdv_router)
edc_subrouter.include_router(unblinding_router)
edc_subrouter.include_router(queries_router)
