"""Payable Ledger Engine for Event-Triggered Grant Disbursements.

Evaluates clinical events against fee matrices, computes multi-payee split line items,
applies tiered overheads, manages holdback retention reserves, and produces 21 CFR Part 11
cryptographically sealed ledger statements.
"""

from __future__ import annotations

import hashlib
import json
import uuid
from collections import defaultdict
from datetime import UTC, datetime
from decimal import Decimal

from packages.engine_core.financial.contracts import (
    _quantize_currency,
    calculate_payee_splits,
    calculate_tiered_overhead,
    generate_line_item_hash,
)
from packages.engine_core.financial.models import (
    ClinicalEvent,
    FeeMatrixItem,
    GrantContract,
    HoldbackLedgerAccount,
    LedgerSummary,
    PayableBreakdown,
    PayableLineItem,
    PayableStatus,
    TriggerType,
)


class PayableLedgerEngine:
    """High-performance in-memory financial engine evaluating event-triggered grant payables."""

    def __init__(self, contract: GrantContract) -> None:
        """Initialize engine with a validated Site Grant Contract."""
        self.contract = contract
        self._cumulative_base_spent = 0.0
        self._event_occurrence_counts: dict[str, int] = defaultdict(int)
        self._holdback_account = HoldbackLedgerAccount(
            contract_id=contract.contract_id,
            site_id=contract.site_id,
            currency=contract.currency,
        )

    def process_event(
        self,
        event: ClinicalEvent,
    ) -> list[PayableLineItem]:
        """Evaluate a single clinical event against the fee matrix and generate line items.

        Args:
            event: The clinical milestone or procedure event.

        Returns:
            list[PayableLineItem]: Generated payable line items for all split payees.
        """
        matched_items = self._match_fee_matrix_items(event)
        if not matched_items and event.trigger_type == TriggerType.MANUAL_ADJUSTMENT:
            # Handle ad-hoc manual adjustment
            base_amount = event.custom_amount or 0.0
            return self._generate_line_items_for_amount(
                event=event,
                item_code="MANUAL_ADJ",
                item_description="Manual Financial Adjustment",
                base_amount=base_amount,
                overhead_applicable=True,
                withholding_applicable=True,
                holdback_applicable=False,
            )

        generated_lines: list[PayableLineItem] = []
        for fee_item in matched_items:
            # Check maximum allowable events cap
            cap_key = f"{event.subject_id or 'GLOBAL'}:{fee_item.item_code}"
            if fee_item.max_allowable_events is not None:
                current_count = self._event_occurrence_counts[cap_key]
                if current_count >= fee_item.max_allowable_events:
                    continue

            base_amount = (
                event.custom_amount
                if event.custom_amount is not None
                else fee_item.base_amount
            )

            lines = self._generate_line_items_for_amount(
                event=event,
                item_code=fee_item.item_code,
                item_description=fee_item.description,
                base_amount=base_amount,
                overhead_applicable=fee_item.overhead_applicable,
                withholding_applicable=fee_item.withholding_applicable,
                holdback_applicable=fee_item.holdback_applicable,
            )
            generated_lines.extend(lines)
            self._event_occurrence_counts[cap_key] += 1

        return generated_lines

    def process_batch(
        self,
        events: list[ClinicalEvent],
        period_start: datetime | None = None,
        period_end: datetime | None = None,
    ) -> LedgerSummary:
        """Process a batch of clinical events and produce a consolidated LedgerSummary."""
        all_lines: list[PayableLineItem] = []
        # Sort events chronologically
        sorted_events = sorted(events, key=lambda e: e.occurred_at)

        for ev in sorted_events:
            lines = self.process_event(ev)
            all_lines.extend(lines)

        now_utc = datetime.now(UTC)
        p_start = period_start or (
            sorted_events[0].occurred_at if sorted_events else now_utc
        )
        p_end = period_end or (
            sorted_events[-1].occurred_at if sorted_events else now_utc
        )

        total_base = _quantize_currency(
            sum(
                Decimal(str(line_item.breakdown.base_amount)) for line_item in all_lines
            )
        )
        total_overhead = _quantize_currency(
            sum(
                Decimal(str(line_item.breakdown.overhead_amount))
                for line_item in all_lines
            )
        )
        total_gross = _quantize_currency(
            sum(
                Decimal(str(line_item.breakdown.gross_amount))
                for line_item in all_lines
            )
        )
        total_withholding = _quantize_currency(
            sum(
                Decimal(str(line_item.breakdown.withholding_amount))
                for line_item in all_lines
            )
        )
        total_holdback = _quantize_currency(
            sum(
                Decimal(str(line_item.breakdown.holdback_amount))
                for line_item in all_lines
            )
        )
        total_net = _quantize_currency(
            sum(
                Decimal(str(line_item.breakdown.net_payable)) for line_item in all_lines
            )
        )

        payee_subtotals: dict[str, float] = defaultdict(float)
        for line in all_lines:
            payee_subtotals[line.payee_id] = _quantize_currency(
                Decimal(str(payee_subtotals[line.payee_id]))
                + Decimal(str(line.breakdown.net_payable))
            )

        # Generate cryptographic digest of ledger statement
        digest = self._compute_summary_digest(
            contract_id=self.contract.contract_id,
            site_id=self.contract.site_id,
            total_net=total_net,
            line_item_hashes=[line_item.audit_hash for line_item in all_lines],
        )

        return LedgerSummary(
            summary_id=str(uuid.uuid4()),
            study_id=self.contract.study_id,
            site_id=self.contract.site_id,
            contract_id=self.contract.contract_id,
            currency=self.contract.currency,
            period_start=p_start,
            period_end=p_end,
            total_events_processed=len(events),
            total_line_items=len(all_lines),
            total_base_amount=total_base,
            total_overhead_amount=total_overhead,
            total_gross_payable=total_gross,
            total_withholding_deducted=total_withholding,
            total_holdback_reserved=total_holdback,
            total_net_payable=total_net,
            line_items=all_lines,
            holdback_account=self._holdback_account,
            payee_subtotals=dict(payee_subtotals),
            cryptographic_digest=digest,
        )

    def release_holdback(
        self,
        amount_to_release: float | None = None,
        reason: str = "Milestone Release",
    ) -> PayableLineItem:
        """Create a payable line item releasing accumulated holdback retention escrow."""
        avail = self._holdback_account.current_balance
        if amount_to_release is None or amount_to_release > avail:
            release_amt = avail
        else:
            release_amt = _quantize_currency(amount_to_release)

        if release_amt <= 0:
            raise ValueError(f"No holdback available for release (balance={avail:.2f})")

        self._holdback_account.released_holdback = _quantize_currency(
            Decimal(str(self._holdback_account.released_holdback))
            + Decimal(str(release_amt))
        )
        self._holdback_account.current_balance = _quantize_currency(
            Decimal(str(self._holdback_account.accumulated_holdback))
            - Decimal(str(self._holdback_account.released_holdback))
        )

        line_id = str(uuid.uuid4())
        now_utc = datetime.now(UTC)

        breakdown = PayableBreakdown(
            base_amount=release_amt,
            overhead_amount=0.0,
            gross_amount=release_amt,
            withholding_amount=0.0,
            holdback_amount=0.0,
            net_payable=release_amt,
            effective_overhead_rate=0.0,
            withholding_rate=0.0,
            holdback_rate=0.0,
        )

        primary_payee_id = (
            self.contract.payee_splits[0].payee_id
            if self.contract.payee_splits
            else "DEFAULT"
        )
        primary_payee_name = (
            self.contract.payee_splits[0].payee_name
            if self.contract.payee_splits
            else "Primary Site Institution"
        )

        audit_hash = generate_line_item_hash(
            line_item_id=line_id,
            event_id=f"HOLDBACK_REL_{line_id[:8]}",
            contract_id=self.contract.contract_id,
            payee_id=primary_payee_id,
            net_payable=release_amt,
            currency=self.contract.currency,
            occurred_at_iso=now_utc.isoformat(),
        )

        return PayableLineItem(
            line_item_id=line_id,
            event_id=f"HOLDBACK_REL_{line_id[:8]}",
            contract_id=self.contract.contract_id,
            item_code="HOLDBACK_RELEASE",
            item_description=f"Holdback Retention Escrow Release: {reason}",
            payee_id=primary_payee_id,
            payee_name=primary_payee_name,
            split_percentage=1.0,
            currency=self.contract.currency,
            breakdown=breakdown,
            status=PayableStatus.APPROVED,
            occurred_at=now_utc,
            calculated_at=now_utc,
            audit_hash=audit_hash,
        )

    def _match_fee_matrix_items(self, event: ClinicalEvent) -> list[FeeMatrixItem]:
        """Find matching fee matrix items for a given clinical event."""
        matches: list[FeeMatrixItem] = []
        for item in self.contract.fee_matrix:
            if item.trigger_type != event.trigger_type:
                continue

            # Check event identifier filter if specified
            if item.event_identifier:
                id_to_check = (
                    event.procedure_code
                    or event.visit_id
                    or event.form_id
                    or event.metadata.get("identifier")
                )
                if id_to_check != item.event_identifier:
                    continue

            matches.append(item)
        return matches

    def _generate_line_items_for_amount(
        self,
        event: ClinicalEvent,
        item_code: str,
        item_description: str,
        base_amount: float,
        overhead_applicable: bool,
        withholding_applicable: bool,
        holdback_applicable: bool,
    ) -> list[PayableLineItem]:
        """Compute deductions, overheads, and split payee allocations for a matched fee item."""
        if base_amount <= 0:
            return []

        # 1. Overhead calculation
        if overhead_applicable:
            overhead_amt, eff_overhead_rate = calculate_tiered_overhead(
                base_amount=base_amount,
                cumulative_spent=self._cumulative_base_spent,
                tiers=self.contract.overhead_tiers,
                default_rate=self.contract.default_overhead_rate,
            )
        else:
            overhead_amt, eff_overhead_rate = 0.0, 0.0

        # Update cumulative base expenditure
        self._cumulative_base_spent = _quantize_currency(
            Decimal(str(self._cumulative_base_spent)) + Decimal(str(base_amount))
        )

        gross_amt = _quantize_currency(
            Decimal(str(base_amount)) + Decimal(str(overhead_amt))
        )

        # 2. Withholding tax
        if withholding_applicable and self.contract.default_withholding_rate > 0:
            withholding_amt = _quantize_currency(
                Decimal(str(gross_amt))
                * Decimal(str(self.contract.default_withholding_rate))
            )
            w_rate = self.contract.default_withholding_rate
        else:
            withholding_amt, w_rate = 0.0, 0.0

        # 3. Holdback retention reserve
        if holdback_applicable and self.contract.holdback_rule.holdback_rate > 0:
            holdback_rule = self.contract.holdback_rule
            potential_holdback = _quantize_currency(
                Decimal(str(gross_amt)) * Decimal(str(holdback_rule.holdback_rate))
            )
            # Apply holdback cap if defined
            if holdback_rule.cap_amount is not None:
                current_bal = self._holdback_account.current_balance
                room_under_cap = max(0.0, holdback_rule.cap_amount - current_bal)
                holdback_amt = min(potential_holdback, room_under_cap)
            else:
                holdback_amt = potential_holdback
            h_rate = holdback_rule.holdback_rate
        else:
            holdback_amt, h_rate = 0.0, 0.0

        # Update holdback ledger account
        if holdback_amt > 0:
            self._holdback_account.accumulated_holdback = _quantize_currency(
                Decimal(str(self._holdback_account.accumulated_holdback))
                + Decimal(str(holdback_amt))
            )
            self._holdback_account.current_balance = _quantize_currency(
                Decimal(str(self._holdback_account.accumulated_holdback))
                - Decimal(str(self._holdback_account.released_holdback))
            )

        # 4. Split payee distribution
        split_records = calculate_payee_splits(
            base_amount=base_amount,
            overhead_amount=overhead_amt,
            withholding_amount=withholding_amt,
            holdback_amount=holdback_amt,
            splits=self.contract.payee_splits,
        )

        now_utc = datetime.now(UTC)
        line_items: list[PayableLineItem] = []

        for rec in split_records:
            line_id = str(uuid.uuid4())
            breakdown = PayableBreakdown(
                base_amount=rec["base_amount"],
                overhead_amount=rec["overhead_amount"],
                gross_amount=rec["gross_amount"],
                withholding_amount=rec["withholding_amount"],
                holdback_amount=rec["holdback_amount"],
                net_payable=rec["net_payable"],
                effective_overhead_rate=eff_overhead_rate,
                withholding_rate=w_rate,
                holdback_rate=h_rate,
            )

            audit_hash = generate_line_item_hash(
                line_item_id=line_id,
                event_id=event.event_id,
                contract_id=self.contract.contract_id,
                payee_id=rec["payee_id"],
                net_payable=rec["net_payable"],
                currency=self.contract.currency,
                occurred_at_iso=event.occurred_at.isoformat(),
            )

            line_items.append(
                PayableLineItem(
                    line_item_id=line_id,
                    event_id=event.event_id,
                    contract_id=self.contract.contract_id,
                    item_code=item_code,
                    item_description=item_description,
                    payee_id=rec["payee_id"],
                    payee_name=rec["payee_name"],
                    split_percentage=rec["split_percentage"],
                    currency=self.contract.currency,
                    breakdown=breakdown,
                    status=PayableStatus.PENDING,
                    occurred_at=event.occurred_at,
                    calculated_at=now_utc,
                    audit_hash=audit_hash,
                )
            )

        return line_items

    @staticmethod
    def _compute_summary_digest(
        contract_id: str,
        site_id: str,
        total_net: float,
        line_item_hashes: list[str],
    ) -> str:
        """Create deterministic summary SHA-256 digest from child line item hashes."""
        payload = {
            "contract_id": contract_id,
            "site_id": site_id,
            "total_net": f"{total_net:.2f}",
            "item_hashes": sorted(line_item_hashes),
        }
        raw_str = json.dumps(payload, sort_keys=True, separators=(",", ":"))
        return hashlib.sha256(raw_str.encode("utf-8")).hexdigest()
