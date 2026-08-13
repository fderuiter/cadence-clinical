from fastapi import APIRouter

from apps.execution.presentation.routers.randomization import router as randomization_router

rtsm_subrouter = APIRouter()

rtsm_subrouter.include_router(randomization_router)
