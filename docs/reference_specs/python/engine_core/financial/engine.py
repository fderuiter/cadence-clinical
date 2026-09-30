"""Unified Event-Triggered Payable Ledger & Fee Matrix Engine Facade.

Provides high-level interfaces for managing site grant fee schedules, evaluating
milestone streams, and generating 21 CFR Part 11 compliant payable ledger statements.
"""

from __future__ import annotations

from datetime import datetime

from packages.engine_core.financial.ledger import PayableLedgerEngine
from packages.engine_core.financial.models import (
    ClinicalEvent,
    GrantContract,
    LedgerSummary,
    PayableLineItem,
)


class EventTriggeredPayableEngine:
    """Facade for the Event-Triggered Payable Ledger and Grant Fee Matrix Engine."""

    @staticmethod
    def evaluate_event(
        contract: GrantContract,
        event: ClinicalEvent,
    ) -> list[PayableLineItem]:
        """Evaluate a single clinical execution event against a grant contract."""
        engine = PayableLedgerEngine(contract)
        return engine.process_event(event)

    @staticmethod
    def evaluate_ledger(
        contract: GrantContract,
        events: list[ClinicalEvent],
        period_start: datetime | None = None,
        period_end: datetime | None = None,
    ) -> LedgerSummary:
        """Evaluate a batch of clinical events and produce a certified LedgerSummary."""
        engine = PayableLedgerEngine(contract)
        return engine.process_batch(
            events=events,
            period_start=period_start,
            period_end=period_end,
        )

    @staticmethod
    def create_ledger_session(contract: GrantContract) -> PayableLedgerEngine:
        """Create a stateful ledger evaluation session for streaming event processing."""
        return PayableLedgerEngine(contract)
