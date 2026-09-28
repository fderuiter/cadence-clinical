"""Contract Calculation Helpers & Multi-Tier Overhead Math.

Provides pure functions for tiered overhead computations, split payee share
distributions, withholding tax reductions, and 21 CFR Part 11 cryptographic hashing.
"""

from __future__ import annotations

import hashlib
import json
from decimal import ROUND_HALF_UP, Decimal
from typing import Any

from packages.engine_core.financial.models import (
    OverheadTier,
    PayeeSplit,
)


def _quantize_currency(amount: float | Decimal) -> float:
    """Round currency amount to 2 decimal places using standard ROUND_HALF_UP."""
    dec = Decimal(str(amount)) if isinstance(amount, float) else amount
    return float(dec.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP))


def calculate_tiered_overhead(
    base_amount: float,
    cumulative_spent: float,
    tiers: list[OverheadTier],
    default_rate: float = 0.0,
) -> tuple[float, float]:
    """Calculate overhead amount across multi-tier marginal brackets.

    Args:
        base_amount: Base procedure or milestone cost.
        cumulative_spent: Historical base expenditure before this line item.
        tiers: Sorted list of overhead brackets.
        default_rate: Fallback flat overhead rate if no tiers match.

    Returns:
        tuple[float, float]: (overhead_amount, effective_overhead_rate)
    """
    if base_amount <= 0:
        return 0.0, 0.0

    if not tiers:
        overhead = _quantize_currency(
            Decimal(str(base_amount)) * Decimal(str(default_rate))
        )
        eff_rate = (
            _quantize_currency(overhead / base_amount)
            if base_amount > 0
            else default_rate
        )
        return overhead, eff_rate

    # Sort tiers by min_threshold ascending
    sorted_tiers = sorted(tiers, key=lambda t: t.min_threshold)
    total_overhead = Decimal("0.0")
    remaining_base = Decimal(str(base_amount))
    current_spent = Decimal(str(cumulative_spent))

    for tier in sorted_tiers:
        tier_min = Decimal(str(tier.min_threshold))
        tier_max = (
            Decimal(str(tier.max_threshold))
            if tier.max_threshold is not None
            else Decimal("Infinity")
        )
        tier_rate = Decimal(str(tier.overhead_rate))

        # Check if current span overlaps with this tier
        if current_spent < tier_max:
            # Capacity in this tier above current_spent
            tier_capacity = tier_max - max(current_spent, tier_min)
            if tier_capacity > 0:
                applicable_base = min(remaining_base, tier_capacity)
                tier_overhead = applicable_base * tier_rate
                total_overhead += tier_overhead
                remaining_base -= applicable_base
                current_spent += applicable_base

        if remaining_base <= Decimal("0"):
            break

    # If any base remains above all tier caps, apply default rate or last tier rate
    if remaining_base > Decimal("0"):
        fallback_rate = (
            Decimal(str(sorted_tiers[-1].overhead_rate))
            if sorted_tiers
            else Decimal(str(default_rate))
        )
        total_overhead += remaining_base * fallback_rate

    quantized_overhead = _quantize_currency(total_overhead)
    effective_rate = (
        _quantize_currency(Decimal(str(quantized_overhead)) / Decimal(str(base_amount)))
        if base_amount > 0
        else 0.0
    )
    return quantized_overhead, effective_rate


def calculate_payee_splits(
    base_amount: float,
    overhead_amount: float,
    withholding_amount: float,
    holdback_amount: float,
    splits: list[PayeeSplit],
) -> list[dict[str, Any]]:
    """Distribute gross, withholding, holdback, and net amounts across split payees.

    Guarantees penny-perfect summation matching total net payable.
    """
    gross_total = Decimal(str(base_amount)) + Decimal(str(overhead_amount))
    net_total = (
        gross_total - Decimal(str(withholding_amount)) - Decimal(str(holdback_amount))
    )

    if not splits:
        return [
            {
                "payee_id": "DEFAULT",
                "payee_name": "Primary Site Institution",
                "split_percentage": 1.0,
                "base_amount": base_amount,
                "overhead_amount": overhead_amount,
                "gross_amount": float(gross_total),
                "withholding_amount": withholding_amount,
                "holdback_amount": holdback_amount,
                "net_payable": float(net_total),
            }
        ]

    allocated_records = []
    accumulated_net = Decimal("0.0")
    primary_idx = 0

    for idx, split in enumerate(splits):
        pct = Decimal(str(split.percentage))
        split_base = _quantize_currency(Decimal(str(base_amount)) * pct)
        split_overhead = _quantize_currency(Decimal(str(overhead_amount)) * pct)
        split_gross = _quantize_currency(gross_total * pct)
        split_withholding = _quantize_currency(Decimal(str(withholding_amount)) * pct)
        split_holdback = _quantize_currency(Decimal(str(holdback_amount)) * pct)
        split_net = split_gross - split_withholding - split_holdback

        accumulated_net += Decimal(str(split_net))
        if split.is_primary:
            primary_idx = idx

        allocated_records.append(
            {
                "payee_id": split.payee_id,
                "payee_name": split.payee_name,
                "split_percentage": split.percentage,
                "base_amount": split_base,
                "overhead_amount": split_overhead,
                "gross_amount": split_gross,
                "withholding_amount": split_withholding,
                "holdback_amount": split_holdback,
                "net_payable": split_net,
            }
        )

    # Reconcile penny rounding discrepancy on primary payee
    discrepancy = net_total - accumulated_net
    if discrepancy != Decimal("0.0") and allocated_records:
        rec = allocated_records[primary_idx]
        rec["net_payable"] = _quantize_currency(
            Decimal(str(rec["net_payable"])) + discrepancy
        )

    return allocated_records


def generate_line_item_hash(
    line_item_id: str,
    event_id: str,
    contract_id: str,
    payee_id: str,
    net_payable: float,
    currency: str,
    occurred_at_iso: str,
) -> str:
    """Produce deterministic SHA-256 audit digest for 21 CFR Part 11 compliance."""
    canonical_payload = {
        "line_item_id": line_item_id,
        "event_id": event_id,
        "contract_id": contract_id,
        "payee_id": payee_id,
        "net_payable": f"{net_payable:.2f}",
        "currency": currency,
        "occurred_at": occurred_at_iso,
    }
    raw_str = json.dumps(canonical_payload, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(raw_str.encode("utf-8")).hexdigest()
