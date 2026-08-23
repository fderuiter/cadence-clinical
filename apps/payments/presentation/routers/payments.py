"""Aggregator FastAPI router for Site Payments microservice.

Aggregates sub-routers from:
- SitePaymentLedgerEngine (site_ledger.py)
- BillingAndErpGateway (billing_erp.py)
- ParticipantCompensationService (participant.py)

Maintains 100% backward compatibility for all /api/v1/payments endpoints.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-004, PRD-PAY-006
"""

from fastapi import APIRouter

from apps.payments.presentation.routers.billing_erp import (
    router as billing_erp_router,
)
from apps.payments.presentation.routers.participant import (
    router as participant_router,
)
from apps.payments.presentation.routers.site_ledger import (
    router as site_ledger_router,
)

router = APIRouter(prefix="/api/v1/payments")

# Include modular sub-routers
router.include_router(site_ledger_router)
router.include_router(billing_erp_router)
router.include_router(participant_router)

__all__ = ["router"]
