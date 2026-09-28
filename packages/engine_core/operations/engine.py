"""Site Startup (SSU) & Operations Engine Facade.

Provides high-level interfaces for evaluating regulatory clearances, staff training,
delegation of authority, and investigational product release greenlight gating.
"""

from __future__ import annotations

from datetime import datetime

from packages.engine_core.operations.evaluator import SiteGreenlightEngine
from packages.engine_core.operations.models import (
    GreenlightDecision,
    SiteStartupCriteria,
)


class SiteStartupGreenlightEngine:
    """Facade for the Site Startup Regulatory Clearance and Greenlight Engine."""

    @staticmethod
    def evaluate(
        criteria: SiteStartupCriteria,
        as_of_date: datetime | None = None,
    ) -> GreenlightDecision:
        """Evaluate site startup dossier and return certified greenlight decision."""
        return SiteGreenlightEngine.evaluate_readiness(
            criteria=criteria,
            as_of_date=as_of_date,
        )
