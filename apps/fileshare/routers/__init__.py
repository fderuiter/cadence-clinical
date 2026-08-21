"""Router package for Fileshare microservice."""

from apps.fileshare.presentation.routers.files import router as files_router

__all__ = ["files_router"]
