from apps.ctms.presentation.routers.ctms import router as ctms_router
from apps.ctms.presentation.routers.deviations import router as deviations_router
from apps.ctms.presentation.routers.doa import router as doa_router
from apps.ctms.presentation.routers.etmf_sync import router as etmf_sync_router
from apps.ctms.presentation.routers.financials import router as financials_router
from apps.ctms.presentation.routers.ip_accountability import (
    router as ip_accountability_router,
)
from apps.ctms.presentation.routers.rbqm import router as rbqm_router
from apps.ctms.presentation.routers.site_startup import router as site_startup_router

__all__ = [
    "ctms_router",
    "deviations_router",
    "doa_router",
    "etmf_sync_router",
    "financials_router",
    "ip_accountability_router",
    "rbqm_router",
    "site_startup_router",
]
