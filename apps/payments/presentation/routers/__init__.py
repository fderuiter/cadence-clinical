"""FastAPI routers for Site Payments."""

from apps.payments.presentation.routers.payments import router as payments_router

__all__ = ["payments_router"]
