"""Unit tests for the Event-Driven SLA Escalation Engine.

@req:PRD-PVG-001, PRD-SYS-050
"""

from datetime import UTC, datetime, timedelta

from packages.engine_core.sla.engine import SLAEngine
from packages.engine_core.sla.models import (
    BusinessHoursSchedule,
    EscalationAction,
    EscalationTier,
    SLAPolicy,
)


def _build_test_policy() -> SLAPolicy:
    """Helper building standard SLA policy with tiers and multipliers."""
    tiers = [
        EscalationTier(
            tier_id="TIER-WARNING",
            name="75% Warning Alert",
            breach_threshold_pct=75.0,
            action=EscalationAction(notify_roles=["site_crc", "data_manager"]),
        ),
        EscalationTier(
            tier_id="TIER-CRITICAL",
            name="90% Critical Escalation",
            breach_threshold_pct=90.0,
            action=EscalationAction(
                target_priority="HIGH",
                notify_roles=["lead_cra", "sponsor_medical_monitor"],
            ),
        ),
        EscalationTier(
            tier_id="TIER-OVERDUE",
            name="100% Statutory Breach",
            breach_threshold_pct=100.0,
            action=EscalationAction(
                target_priority="CRITICAL",
                notify_roles=["lead_cra", "sponsor_qa_manager", "super_admin"],
                webhook_channel="https://webhook.cadence.clinical/alerts/sla",
            ),
        ),
    ]

    return SLAPolicy(
        policy_id="SLA-ICSR-SAFETY",
        name="Global Safety & Query Resolution SLA",
        base_hours_by_priority={
            "CRITICAL": 4.0,
            "HIGH": 24.0,
            "MEDIUM": 72.0,
            "LOW": 168.0,
        },
        category_multipliers={
            "EXPEDITED_7_DAY": 1.0,
            "EXPEDITED_15_DAY": 1.0,
            "ROUTINE_QUERY": 0.5,
        },
        paused_states=["PENDING_SPONSOR_INPUT", "UNDER_MEDICAL_QUERY"],
        warning_threshold_pct=75.0,
        critical_threshold_pct=90.0,
        escalation_tiers=tiers,
        reason_for_change="Authoring standard safety and operational SLA policy",
    )


def test_sla_due_date_computation_24x7():
    """Verify SLA target date calculation for 24x7 schedule with priority and multipliers.

    @req:PRD-PVG-001
    """
    policy = _build_test_policy()
    engine = SLAEngine(policy)

    start_dt = datetime(2026, 8, 20, 10, 0, 0, tzinfo=UTC)

    # 4 hours CRITICAL
    due_crit = engine.compute_sla_due_date(start_dt, priority="CRITICAL")
    assert due_crit == datetime(2026, 8, 20, 14, 0, 0, tzinfo=UTC)

    # 24 hours HIGH
    due_high = engine.compute_sla_due_date(start_dt, priority="HIGH")
    assert due_high == datetime(2026, 8, 21, 10, 0, 0, tzinfo=UTC)

    # 72 hours MEDIUM with 0.5 category multiplier -> 36 hours
    due_med_mult = engine.compute_sla_due_date(
        start_dt, priority="MEDIUM", category="ROUTINE_QUERY"
    )
    assert due_med_mult == datetime(2026, 8, 21, 22, 0, 0, tzinfo=UTC)


def test_sla_due_date_computation_business_hours():
    """Verify SLA target date calculation adhering to business hours (9-17) and weekends.

    @req:PRD-SYS-050
    """
    sched = BusinessHoursSchedule(
        is_24x7=False,
        work_start_hour=9,
        work_end_hour=17,
        work_days=[0, 1, 2, 3, 4],  # Mon-Fri
        holidays=["2026-08-21"],  # Friday holiday
    )
    policy = SLAPolicy(
        policy_id="SLA-BIZ",
        name="Business Hours SLA",
        schedule=sched,
        base_hours_by_priority={"HIGH": 16.0},  # 2 full work days
        reason_for_change="Business hours policy setup",
    )
    engine = SLAEngine(policy)

    # Thursday Aug 20, 2026 at 10:00 UTC (7 hours left in workday -> ends 17:00, 9 hours remaining)
    # Friday Aug 21 is holiday -> skip to Monday Aug 24
    # Monday Aug 24: 9:00 to 17:00 (8 hours used -> 1 hour remaining)
    # Tuesday Aug 25: 9:00 + 1 hour = 10:00
    start_dt = datetime(2026, 8, 20, 10, 0, 0, tzinfo=UTC)
    due_dt = engine.compute_sla_due_date(start_dt, priority="HIGH")
    assert due_dt.strftime("%Y-%m-%d %H:%M") == "2026-08-25 10:00"


def test_countdown_clock_and_alert_level_progression():
    """Verify live countdown metrics, elapsed percentages, and alert level progression.

    @req:PRD-PVG-001, PRD-SYS-050
    """
    policy = _build_test_policy()
    engine = SLAEngine(policy)

    start_dt = datetime(2026, 8, 20, 0, 0, 0, tzinfo=UTC)
    due_dt = datetime(2026, 8, 20, 20, 0, 0, tzinfo=UTC)  # 20 hours total

    # 1. At 5 hours (25% elapsed) -> NORMAL
    as_of_normal = datetime(2026, 8, 20, 5, 0, 0, tzinfo=UTC)
    clock_normal = engine.compute_countdown_clock(start_dt, due_dt, as_of=as_of_normal)
    assert clock_normal.elapsed_percentage == 25.0
    assert clock_normal.remaining_hours == 15.0
    assert clock_normal.alert_level == "NORMAL"
    assert clock_normal.is_overdue is False

    # 2. At 16 hours (80% elapsed) -> WARNING
    as_of_warn = datetime(2026, 8, 20, 16, 0, 0, tzinfo=UTC)
    clock_warn = engine.compute_countdown_clock(start_dt, due_dt, as_of=as_of_warn)
    assert clock_warn.elapsed_percentage == 80.0
    assert clock_warn.alert_level == "WARNING"

    # 3. At 19 hours (95% elapsed) -> CRITICAL
    as_of_crit = datetime(2026, 8, 20, 19, 0, 0, tzinfo=UTC)
    clock_crit = engine.compute_countdown_clock(start_dt, due_dt, as_of=as_of_crit)
    assert clock_crit.elapsed_percentage == 95.0
    assert clock_crit.alert_level == "CRITICAL"

    # 4. At 21 hours (105% elapsed) -> OVERDUE
    as_of_over = datetime(2026, 8, 20, 21, 0, 0, tzinfo=UTC)
    clock_over = engine.compute_countdown_clock(start_dt, due_dt, as_of=as_of_over)
    assert clock_over.is_overdue is True
    assert clock_over.alert_level == "OVERDUE"


def test_countdown_clock_paused_time_adjustment():
    """Verify that paused/frozen state duration extends effective due date.

    @req:PRD-SYS-050
    """
    policy = _build_test_policy()
    engine = SLAEngine(policy)

    start_dt = datetime(2026, 8, 20, 0, 0, 0, tzinfo=UTC)
    due_dt = datetime(2026, 8, 20, 10, 0, 0, tzinfo=UTC)  # 10 hours allocated

    # At 8 hours elapsed, but 4 hours were paused (14,400 seconds)
    # Net elapsed = 4 hours (40% of 10 hours) -> NORMAL alert level
    # Effective due date extended by 4 hours to 14:00 UTC
    as_of = datetime(2026, 8, 20, 8, 0, 0, tzinfo=UTC)
    paused_secs = 4 * 3600.0

    clock = engine.compute_countdown_clock(
        start_dt, due_dt, total_paused_seconds=paused_secs, as_of=as_of
    )
    assert clock.elapsed_hours == 4.0
    assert clock.elapsed_percentage == 40.0
    assert clock.remaining_hours == 6.0
    assert clock.alert_level == "NORMAL"
    assert clock.effective_due_date == (due_dt + timedelta(hours=4)).isoformat()


def test_evaluate_multi_tier_escalation_policies():
    """Verify multi-tier escalation evaluation recommending priority and recipients.

    @req:PRD-PVG-001, PRD-SYS-050
    """
    policy = _build_test_policy()
    engine = SLAEngine(policy)

    start_dt = datetime(2026, 8, 20, 0, 0, 0, tzinfo=UTC)
    due_dt = datetime(2026, 8, 20, 10, 0, 0, tzinfo=UTC)

    # 1. At 80% elapsed (WARNING) -> Triggers TIER-WARNING
    clock_80 = engine.compute_countdown_clock(
        start_dt, due_dt, as_of=datetime(2026, 8, 20, 8, 0, 0, tzinfo=UTC)
    )
    res_80 = engine.evaluate_escalations(clock_80, current_priority="MEDIUM")
    assert len(res_80.triggered_tiers) == 1
    assert res_80.triggered_tiers[0].tier_id == "TIER-WARNING"
    assert "data_manager" in res_80.notification_roles

    # 2. At 105% elapsed (OVERDUE) -> Triggers all 3 tiers, recommends CRITICAL
    clock_105 = engine.compute_countdown_clock(
        start_dt, due_dt, as_of=datetime(2026, 8, 20, 10, 30, 0, tzinfo=UTC)
    )
    res_105 = engine.evaluate_escalations(clock_105, current_priority="MEDIUM")
    assert len(res_105.triggered_tiers) == 3
    assert res_105.recommended_priority == "CRITICAL"
    assert "sponsor_qa_manager" in res_105.notification_roles
