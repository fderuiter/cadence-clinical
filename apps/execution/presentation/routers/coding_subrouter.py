from fastapi import APIRouter

from apps.execution.presentation.routers.dictionaries import router as dictionaries_router

coding_subrouter = APIRouter()

coding_subrouter.include_router(dictionaries_router)
