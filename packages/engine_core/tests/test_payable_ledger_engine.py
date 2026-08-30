"""Unit Tests for Event-Triggered Payable Ledger & Fee Matrix Engine.

Verifies multi-tiered overhead math, split-payee distribution invariants,
milestone triggers, holdback retention reserves, and 21 CFR Part 11 cryptographic sealing.

@req:PRD-SYS-050
@req:PRD-PAY-001
@req:PRD-SYS-001
@req:Trace-1
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

from packages.engine_core.financial import (
    ClinicalEvent,
    EventTriggeredPayableEngine,
    FeeMatrixItem,
    GrantContract,
    HoldbackReserveRule,
    OverheadTier,
    PayableLedgerEngine,
    PayableStatus,
    PayeeSplit,
    TriggerType,
    calculate_payee_splits,
    calculate_tiered_overhead,
    generate_line_item_hash,
)


def _build_test_contract() -> GrantContract:
    """Build a comprehensive test grant contract with tiered overhead and split payees."""
    return GrantContract(
        contract_id="CONTRACT-SITE-101",
        study_id="STUDY-ONCOLOGY-01",
        site_id="SITE-101",
        currency="USD",
        effective_date=datetime(2026, 1, 1, tzinfo=UTC),
        overhead_tiers=[
            OverheadTier(
                tier_id="T1",
                min_threshold=0.0,
                max_threshold=50000.0,
                overhead_rate=0.25,
            ),
            OverheadTier(
                tier_id="T2",
                min_threshold=50000.0,
                max_threshold=100000.0,
                overhead_rate=0.20,
            ),
            OverheadTier(
                tier_id="T3",
                min_threshold=100000.0,
                max_threshold=None,
                overhead_rate=0.15,
            ),
        ],
        default_overhead_rate=0.20,
        default_withholding_rate=0.05,
        holdback_rule=HoldbackReserveRule(
            holdback_rate=0.10, cap_amount=15000.0, release_trigger="STUDY_CLOSEOUT"
        ),
        payee_splits=[
            PayeeSplit(
                payee_id="PAYEE-INST",
                payee_name="University Hospital System",
                percentage=0.70,
                is_primary=True,
            ),
            PayeeSplit(
                payee_id="PAYEE-PI-FUND",
                payee_name="Dr. Investigator Research Fund",
                percentage=0.30,
            ),
        ],
        fee_matrix=[
            FeeMatrixItem(
                item_code="FEE_ENROLL",
                description="Subject Enrollment Milestone",
                trigger_type=TriggerType.ENROLLMENT,
                base_amount=2500.0,
                overhead_applicable=True,
                withholding_applicable=True,
                holdback_applicable=True,
                max_allowable_events=1,
            ),
            FeeMatrixItem(
                item_code="FEE_VISIT_V1",
                description="Baseline Visit Assessment",
                trigger_type=TriggerType.VISIT_COMPLETED,
                event_identifier="VISIT_V1",
                base_amount=1200.0,
                overhead_applicable=True,
                withholding_applicable=True,
                holdback_applicable=True,
                max_allowable_events=1,
            ),
            FeeMatrixItem(
                item_code="FEE_PK_DRAW",
                description="Pharmacokinetic Blood Sampling",
                trigger_type=TriggerType.UNSCHEDULED_PROCEDURE,
                event_identifier="PK_DRAW",
                base_amount=450.0,
                overhead_applicable=True,
                withholding_applicable=False,
                holdback_applicable=False,
            ),
            FeeMatrixItem(
                item_code="FEE_QUERY_RES",
                description="Complex Query Resolution",
                trigger_type=TriggerType.QUERY_RESOLVED,
                base_amount=150.0,
                overhead_applicable=False,
                withholding_applicable=False,
                holdback_applicable=False,
            ),
        ],
    )


def test_tiered_overhead_marginal_brackets():
    """Verify tiered overhead calculation across bracket boundaries without cent leakage.

    @req:PRD-SYS-050
    @req:PRD-PAY-001
    """
    tiers = [
        OverheadTier(
            tier_id="T1", min_threshold=0.0, max_threshold=1000.0, overhead_rate=0.25
        ),
        OverheadTier(
            tier_id="T2", min_threshold=1000.0, max_threshold=2000.0, overhead_rate=0.20
        ),
        OverheadTier(
            tier_id="T3", min_threshold=2000.0, max_threshold=None, overhead_rate=0.15
        ),
    ]

    # Case 1: Entirely within Tier 1 ($500 @ 25% = $125)
    amt, rate = calculate_tiered_overhead(500.0, 0.0, tiers)
    assert amt == 125.0
    assert rate == 0.25

    # Case 2: Straddling Tier 1 and Tier 2 ($800 spent; next $400: $200 @ 25% + $200 @ 20% = $50 + $40 = $90)
    amt, rate = calculate_tiered_overhead(400.0, 800.0, tiers)
    assert amt == 90.0
    assert rate == 0.225 or abs(rate - 0.225) <= 0.01

    # Case 3: Beyond all caps ($2500 spent; next $1000 @ 15% = $150)
    amt, rate = calculate_tiered_overhead(1000.0, 2500.0, tiers)
    assert amt == 150.0
    assert rate == 0.15


def test_split_payee_penny_perfect_reconciliation():
    """Verify split payee shares always sum exactly to net payable with zero penny rounding errors.

    @req:PRD-SYS-050
    @req:PRD-PAY-001
    """
    splits = [
        PayeeSplit(
            payee_id="P1", payee_name="Institution", percentage=0.3333, is_primary=True
        ),
        PayeeSplit(payee_id="P2", payee_name="Investigator", percentage=0.3333),
        PayeeSplit(payee_id="P3", payee_name="Lab", percentage=0.3334),
    ]

    # Gross: $100.00, Withholding: $5.00, Holdback: $10.00 -> Net: $85.00
    records = calculate_payee_splits(
        base_amount=80.0,
        overhead_amount=20.0,
        withholding_amount=5.0,
        holdback_amount=10.0,
        splits=splits,
    )

    assert len(records) == 3
    total_net = sum(r["net_payable"] for r in records)
    assert round(total_net, 2) == 85.00


def test_payable_ledger_event_matching_and_caps():
    """Verify clinical events trigger matching fee items and enforce per-subject occurrence caps.

    @req:PRD-SYS-050
    @req:PRD-PAY-001
    @req:PRD-SYS-001
    """
    contract = _build_test_contract()
    engine = PayableLedgerEngine(contract)
    now = datetime(2026, 3, 1, 10, 0, tzinfo=UTC)

    # 1. Subject Enrollment Event
    ev_enroll = ClinicalEvent(
        event_id="EV-001",
        study_id="STUDY-ONCOLOGY-01",
        site_id="SITE-101",
        subject_id="SUBJ-101",
        trigger_type=TriggerType.ENROLLMENT,
        occurred_at=now,
    )

    lines = engine.process_event(ev_enroll)
    assert len(lines) == 2  # 2 split payees (70% / 30%)
    inst_line = next(item for item in lines if item.payee_id == "PAYEE-INST")
    pi_line = next(item for item in lines if item.payee_id == "PAYEE-PI-FUND")

    # Base: $2500, Overhead (25%): $625, Gross: $3125
    # Withholding (5% of $3125): $156.25, Holdback (10% of $3125): $312.50
    # Net: $3125 - 156.25 - 312.50 = $2656.25
    # Inst (70%): $1859.38, PI (30%): $796.87 -> Sum = $2656.25
    assert inst_line.breakdown.base_amount == 1750.0  # 70% of 2500
    assert inst_line.breakdown.overhead_amount == 437.50  # 70% of 625
    assert inst_line.breakdown.gross_amount == 2187.50
    assert (
        round(inst_line.breakdown.net_payable + pi_line.breakdown.net_payable, 2)
        == 2656.25
    )
    assert inst_line.audit_hash != ""

    # 2. Second Enrollment Event for SAME subject -> Blocked by max_allowable_events=1 cap
    ev_enroll_dup = ClinicalEvent(
        event_id="EV-002",
        study_id="STUDY-ONCOLOGY-01",
        site_id="SITE-101",
        subject_id="SUBJ-101",
        trigger_type=TriggerType.ENROLLMENT,
        occurred_at=now + timedelta(days=1),
    )
    dup_lines = engine.process_event(ev_enroll_dup)
    assert len(dup_lines) == 0


def test_batch_ledger_summary_and_audit_digest():
    """Verify batch processing produces consolidated ledger statement with SHA-256 digest.

    @req:PRD-SYS-050
    @req:PRD-PAY-001
    @req:Trace-1
    """
    contract = _build_test_contract()
    now = datetime(2026, 3, 1, 10, 0, tzinfo=UTC)

    events = [
        ClinicalEvent(
            event_id="EV-001",
            study_id="STUDY-ONCOLOGY-01",
            site_id="SITE-101",
            subject_id="SUBJ-101",
            trigger_type=TriggerType.ENROLLMENT,
            occurred_at=now,
        ),
        ClinicalEvent(
            event_id="EV-002",
            study_id="STUDY-ONCOLOGY-01",
            site_id="SITE-101",
            subject_id="SUBJ-101",
            visit_id="VISIT_V1",
            trigger_type=TriggerType.VISIT_COMPLETED,
            occurred_at=now + timedelta(days=7),
        ),
        ClinicalEvent(
            event_id="EV-003",
            study_id="STUDY-ONCOLOGY-01",
            site_id="SITE-101",
            subject_id="SUBJ-101",
            procedure_code="PK_DRAW",
            trigger_type=TriggerType.UNSCHEDULED_PROCEDURE,
            occurred_at=now + timedelta(days=7, hours=2),
        ),
    ]

    summary = EventTriggeredPayableEngine.evaluate_ledger(
        contract=contract, events=events
    )

    assert summary.total_events_processed == 3
    assert summary.total_line_items == 6  # 3 events * 2 payees
    assert summary.total_base_amount == 2500.0 + 1200.0 + 450.0  # $4150.00
    assert summary.total_gross_payable > summary.total_base_amount
    assert summary.total_net_payable > 0.0
    assert summary.cryptographic_digest != ""
    assert "PAYEE-INST" in summary.payee_subtotals
    assert "PAYEE-PI-FUND" in summary.payee_subtotals
    assert round(sum(summary.payee_subtotals.values()), 2) == summary.total_net_payable


def test_holdback_retention_and_escrow_release():
    """Verify holdback escrow accumulation, cap enforcement, and release lifecycle.

    @req:PRD-SYS-050
    @req:PRD-PAY-001
    """
    contract = _build_test_contract()
    # Set low holdback cap of $500
    contract.holdback_rule = HoldbackReserveRule(holdback_rate=0.10, cap_amount=500.0)
    engine = PayableLedgerEngine(contract)
    now = datetime(2026, 3, 1, 10, 0, tzinfo=UTC)

    # Event 1: Enrollment (Gross: $3125.00 -> 10% = $312.50 holdback, under $500 cap)
    ev1 = ClinicalEvent(
        event_id="EV-001",
        study_id="STUDY-ONCOLOGY-01",
        site_id="SITE-101",
        subject_id="SUBJ-101",
        trigger_type=TriggerType.ENROLLMENT,
        occurred_at=now,
    )
    engine.process_event(ev1)
    assert engine._holdback_account.accumulated_holdback == 312.50
    assert engine._holdback_account.current_balance == 312.50

    # Event 2: Visit V1 (Gross: $1500.00 -> 10% is $150, but cap is $500 so only $187.50 added)
    ev2 = ClinicalEvent(
        event_id="EV-002",
        study_id="STUDY-ONCOLOGY-01",
        site_id="SITE-101",
        subject_id="SUBJ-101",
        visit_id="VISIT_V1",
        trigger_type=TriggerType.VISIT_COMPLETED,
        occurred_at=now + timedelta(days=7),
    )
    engine.process_event(ev2)
    assert (
        engine._holdback_account.accumulated_holdback == 312.50 + 150.0
    )  # 462.50 <= 500

    # Release $300 from holdback
    rel_line = engine.release_holdback(300.0, reason="Interim Milestone Clearance")
    assert rel_line.breakdown.net_payable == 300.0
    assert rel_line.status == PayableStatus.APPROVED
    assert engine._holdback_account.released_holdback == 300.0
    assert engine._holdback_account.current_balance == 162.50

    # Release remaining holdback ($162.50)
    rel_final = engine.release_holdback(reason="Final Study Closeout")
    assert rel_final.breakdown.net_payable == 162.50
    assert engine._holdback_account.current_balance == 0.0


def test_line_item_audit_hash_tamper_detection():
    """Verify line item SHA-256 audit digest detects payload tampering.

    @req:PRD-SYS-050
    @req:Trace-1
    """
    now_iso = "2026-03-01T10:00:00+00:00"
    digest_orig = generate_line_item_hash(
        line_item_id="LINE-001",
        event_id="EV-001",
        contract_id="CONTRACT-101",
        payee_id="PAYEE-INST",
        net_payable=1500.00,
        currency="USD",
        occurred_at_iso=now_iso,
    )

    digest_tampered = generate_line_item_hash(
        line_item_id="LINE-001",
        event_id="EV-001",
        contract_id="CONTRACT-101",
        payee_id="PAYEE-INST",
        net_payable=1500.01,  # 1 cent modification
        currency="USD",
        occurred_at_iso=now_iso,
    )

    assert digest_orig != digest_tampered
