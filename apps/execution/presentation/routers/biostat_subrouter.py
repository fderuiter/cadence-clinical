from fastapi import APIRouter

from apps.execution.presentation.routers.exports import router as exports_router

biostat_subrouter = APIRouter()

biostat_subrouter.include_router(exports_router)
