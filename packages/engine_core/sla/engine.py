"""Deterministic Event-Driven SLA Escalation Engine.

Calculates SLA target completion datetimes, live countdown clock status,
frozen/paused state intervals, and multi-tier operational escalation triggers.
"""

from __future__ import annotations

from datetime import UTC, date, datetime, timedelta

from packages.engine_core.sla.models import (
    AlertLevel,
    CountdownClock,
    EscalationEvaluationResult,
    EscalationTier,
    SLAPolicy,
)


def _parse_timestamp(dt_val: str | datetime | date) -> datetime:
    """Safely parse various datetime inputs into timezone-aware UTC datetime."""
    if isinstance(dt_val, datetime):
        return (
            dt_val.replace(tzinfo=UTC)
            if dt_val.tzinfo is None
            else dt_val.astimezone(UTC)
        )
    if isinstance(dt_val, date):
        return datetime(dt_val.year, dt_val.month, dt_val.day, tzinfo=UTC)
    if isinstance(dt_val, str):
        cleaned = dt_val.strip()
        if cleaned.endswith("Z"):
            cleaned = cleaned[:-1] + "+00:00"
        try:
            parsed = datetime.fromisoformat(cleaned)
            return (
                parsed.replace(tzinfo=UTC)
                if parsed.tzinfo is None
                else parsed.astimezone(UTC)
            )
        except ValueError:
            if len(cleaned) == 10:
                return datetime.strptime(cleaned, "%Y-%m-%d").replace(tzinfo=UTC)
            raise ValueError(f"Unable to parse timestamp string: '{dt_val}'")
    raise ValueError(f"Unsupported timestamp type: {type(dt_val)}")


class SLAEngine:
    """Deterministic, pure in-memory SLA and countdown evaluation engine."""

    def __init__(self, policy: SLAPolicy) -> None:
        self.policy = policy

    def is_state_paused(self, state: str) -> bool:
        """Returns True if the given state freezes/pauses the SLA clock."""
        return state in self.policy.paused_states

    def compute_sla_due_date(
        self,
        start_date: datetime | str,
        priority: str,
        category: str | None = None,
        custom_hours: float | None = None,
    ) -> datetime:
        """Computes the baseline SLA target completion datetime."""
        start_dt = _parse_timestamp(start_date)

        if custom_hours is not None and custom_hours > 0:
            allocated_hours = float(custom_hours)
        else:
            allocated_hours = float(
                self.policy.base_hours_by_priority.get(priority.upper(), 72.0)
            )

        if category:
            multiplier = self.policy.category_multipliers.get(category, 1.0)
            allocated_hours *= multiplier

        # 24x7 vs business hours calculation
        if self.policy.schedule.is_24x7:
            return start_dt + timedelta(hours=allocated_hours)

        # Business hours schedule calculation
        curr = start_dt
        hours_left = allocated_hours
        sched = self.policy.schedule

        while hours_left > 0:
            date_str = curr.strftime("%Y-%m-%d")
            is_workday = (
                curr.weekday() in sched.work_days and date_str not in sched.holidays
            )

            if is_workday:
                if curr.hour < sched.work_start_hour:
                    curr = curr.replace(
                        hour=sched.work_start_hour, minute=0, second=0, microsecond=0
                    )

                if curr.hour < sched.work_end_hour:
                    available_today = (sched.work_end_hour - curr.hour) - (
                        curr.minute / 60.0
                    )
                    if hours_left <= available_today:
                        curr += timedelta(hours=hours_left)
                        hours_left = 0
                        break
                    hours_left -= available_today

            # Advance to start of next day
            curr = (curr + timedelta(days=1)).replace(
                hour=sched.work_start_hour, minute=0, second=0, microsecond=0
            )

        return curr

    def compute_countdown_clock(
        self,
        start_date: datetime | str,
        due_date: datetime | str,
        total_paused_seconds: float = 0.0,
        as_of: datetime | None = None,
    ) -> CountdownClock:
        """Evaluates live countdown progression adjusted for frozen intervals."""
        start_dt = _parse_timestamp(start_date)
        base_due_dt = _parse_timestamp(due_date)
        now_dt = _parse_timestamp(as_of or datetime.now(UTC))

        # Adjust effective due date forward by total paused time
        effective_due_dt = base_due_dt + timedelta(
            seconds=max(0.0, total_paused_seconds)
        )

        total_allocated_seconds = (base_due_dt - start_dt).total_seconds()
        if total_allocated_seconds <= 0:
            total_allocated_seconds = 3600.0  # Fallback 1 hour

        total_allocated_hours = round(total_allocated_seconds / 3600.0, 2)

        # Net elapsed time excluding paused duration
        raw_elapsed_seconds = (now_dt - start_dt).total_seconds()
        net_elapsed_seconds = max(
            0.0, raw_elapsed_seconds - max(0.0, total_paused_seconds)
        )
        elapsed_hours = round(net_elapsed_seconds / 3600.0, 2)

        elapsed_pct = round((net_elapsed_seconds / total_allocated_seconds) * 100.0, 2)
        remaining_seconds = (effective_due_dt - now_dt).total_seconds()
        remaining_hours = max(0.0, round(remaining_seconds / 3600.0, 2))
        is_overdue = now_dt > effective_due_dt

        # Determine alert level
        if is_overdue or elapsed_pct >= 100.0:
            alert_level: AlertLevel = "OVERDUE"
        elif elapsed_pct >= self.policy.critical_threshold_pct:
            alert_level = "CRITICAL"
        elif elapsed_pct >= self.policy.warning_threshold_pct:
            alert_level = "WARNING"
        else:
            alert_level = "NORMAL"

        return CountdownClock(
            clock_start_date=start_dt.isoformat(),
            clock_due_date=base_due_dt.isoformat(),
            effective_due_date=effective_due_dt.isoformat(),
            total_allocated_hours=total_allocated_hours,
            total_paused_seconds=total_paused_seconds,
            elapsed_hours=elapsed_hours,
            elapsed_percentage=elapsed_pct,
            remaining_hours=remaining_hours,
            is_overdue=is_overdue,
            alert_level=alert_level,
        )

    def evaluate_escalations(
        self,
        clock: CountdownClock,
        current_priority: str | None = None,
    ) -> EscalationEvaluationResult:
        """Evaluates active clock metrics against defined policy escalation tiers."""
        triggered: list[EscalationTier] = []
        recipients: set[str] = set()
        highest_rec_priority: str | None = None

        # Sort tiers ascending by breach threshold
        sorted_tiers = sorted(
            self.policy.escalation_tiers, key=lambda t: t.breach_threshold_pct
        )

        for tier in sorted_tiers:
            if clock.elapsed_percentage >= tier.breach_threshold_pct:
                triggered.append(tier)
                for role in tier.action.notify_roles:
                    recipients.add(role)
                if tier.action.target_priority:
                    highest_rec_priority = tier.action.target_priority

        summary = (
            f"SLA status: {clock.alert_level} ({clock.elapsed_percentage}% elapsed). "
            f"Triggered tiers: {[t.name for t in triggered]}. "
            f"Recommended priority: {highest_rec_priority or current_priority}."
        )

        return EscalationEvaluationResult(
            current_alert_level=clock.alert_level,
            triggered_tiers=triggered,
            recommended_priority=highest_rec_priority,
            notification_roles=sorted(recipients),
            audit_summary=summary,
        )
