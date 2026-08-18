"""
Domain services and analysis engines for Tickets microservice.
"""

from datetime import UTC, datetime, timedelta
from typing import Any

from apps.tickets.domain.exceptions import ValidationError
from apps.tickets.domain.models import (
    GxPSeverity,
    ResolutionCode,
    RootCauseCategory,
    TicketCategory,
    TicketPriority,
    TicketStatus,
)

DEFAULT_SLA_HOURS: dict[TicketPriority | str, int] = {
    TicketPriority.CRITICAL: 4,
    TicketPriority.HIGH: 24,
    TicketPriority.MEDIUM: 72,
    TicketPriority.LOW: 168,
}

CATEGORY_SLA_MULTIPLIERS: dict[TicketCategory | str, float] = {
    TicketCategory.SAFETY_ADVERSE_EVENT: 0.5,  # Expedited 50% response time
    TicketCategory.PROTOCOL_DEVIATION: 0.75,
    TicketCategory.SUPPLY_EXCURSION: 0.75,
    TicketCategory.DATA_QUERY: 1.0,
    TicketCategory.TECHNICAL: 1.0,
    TicketCategory.ACCESS: 1.0,
    TicketCategory.HARDWARE: 1.0,
    TicketCategory.CLINICAL: 1.0,
    TicketCategory.SITE_OPERATIONS: 1.0,
    TicketCategory.REGULATORY_ETMF: 1.0,
    TicketCategory.CHANGE_REQUEST: 1.5,
    TicketCategory.OTHER: 1.0,
}


def calculate_sla_target(
    created_at: datetime,
    priority: TicketPriority | str,
    category: TicketCategory | str | None = None,
    custom_sla_hours: int | None = None,
) -> datetime:
    """
    Computes the SLA target completion datetime given creation time, priority, and optional category tuning.
    """
    if custom_sla_hours is not None and custom_sla_hours > 0:
        base_hours = float(custom_sla_hours)
    else:
        base_hours = float(DEFAULT_SLA_HOURS.get(priority, 72))

    if category:
        mult = CATEGORY_SLA_MULTIPLIERS.get(category, 1.0)
        base_hours *= mult

    return created_at + timedelta(hours=base_hours)


def evaluate_sla_status(
    created_at: datetime,
    sla_target: datetime | None,
    total_paused_seconds: int = 0,
    current_time: datetime | None = None,
) -> dict[str, Any]:
    """
    Evaluates current SLA progression, adjusted for frozen/paused durations.
    Returns breach indicator, amber warning (75% threshold), elapsed percent, and remaining duration.
    """
    if sla_target is None:
        return {
            "is_breached": False,
            "is_amber_warning": False,
            "elapsed_percent": 0.0,
            "remaining_seconds": 0.0,
            "effective_duration_seconds": 0.0,
        }

    now = current_time or datetime.now(UTC)
    if created_at.tzinfo is None and now.tzinfo is not None:
        now = now.replace(tzinfo=None)
    elif created_at.tzinfo is not None and now.tzinfo is None:
        now = now.replace(tzinfo=UTC)

    # Adjust target forward by total paused time
    effective_target = sla_target + timedelta(seconds=max(0, total_paused_seconds))
    total_allocated_seconds = (effective_target - created_at).total_seconds()
    if total_allocated_seconds <= 0:
        total_allocated_seconds = 3600.0  # fallback 1h

    elapsed_seconds = (now - created_at).total_seconds() - max(0, total_paused_seconds)
    remaining_seconds = (effective_target - now).total_seconds()

    elapsed_percent = round(
        min(999.0, max(0.0, (elapsed_seconds / total_allocated_seconds) * 100.0)),
        2,
    )
    is_breached = remaining_seconds <= 0
    is_amber_warning = not is_breached and elapsed_percent >= 75.0

    return {
        "is_breached": is_breached,
        "is_amber_warning": is_amber_warning,
        "elapsed_percent": elapsed_percent,
        "remaining_seconds": max(0.0, remaining_seconds),
        "effective_target": effective_target,
    }


def validate_resolution_requirements(
    status: TicketStatus | str,
    gxp_severity: GxPSeverity | str | None,
    root_cause_category: RootCauseCategory | str | None,
    resolution_code: ResolutionCode | str | None,
) -> None:
    """
    Validates that Major and Critical GxP tickets have an assigned RCA root cause and resolution code
    upon transition to RESOLVED or CLOSED states.
    """
    status_str = str(status)
    severity_str = str(gxp_severity) if gxp_severity else None

    if status_str in (TicketStatus.RESOLVED.value, TicketStatus.CLOSED.value):
        if severity_str in (GxPSeverity.CRITICAL.value, GxPSeverity.MAJOR.value):
            if not root_cause_category:
                raise ValidationError(
                    f"Root cause category (RCA) is required to resolve or close a {severity_str} GxP ticket."
                )
            if not resolution_code:
                raise ValidationError(
                    f"Formal resolution code is required to resolve or close a {severity_str} GxP ticket."
                )


def parse_value(val: str | None) -> Any:
    """
    Parses a string value to its Python equivalent for type-aware diff comparison.
    """
    if val is None:
        return None
    val_clean = val.strip()
    val_lower = val_clean.lower()
    if val_lower in ("true", "yes", "enabled", "on"):
        return True
    if val_lower in ("false", "no", "disabled", "off"):
        return False
    try:
        if "." in val_clean:
            return float(val_clean)
        return int(val_clean)
    except ValueError:
        return val_clean


def evaluate_setting_risk(key: str, old_val: str, new_val: str) -> dict[str, Any]:
    """
    Evaluate setting change and return risk metrics.
    """
    parsed_old = parse_value(old_val)
    parsed_new = parse_value(new_val)

    if "audit" in key.lower() and parsed_new is False:
        raise ValidationError(
            "Disabling audit trail logging is strictly prohibited under 21 CFR Part 11."
        )

    if parsed_old == parsed_new:
        return {
            "risk_level": "LOW_RISK",
            "affected_gxp_clauses": [],
            "requires_qa_signoff": False,
            "summary": "No functional configuration delta detected.",
            "risk_summary": "No functional configuration delta detected.",
        }

    if (
        key.startswith("audit_")
        or key.startswith("esignature_")
        or "esignature" in key.lower()
        or "double_auth" in key.lower()
        or "data_lock" in key.lower()
        or key.startswith("lock_")
    ):
        return {
            "risk_level": "HIGH_RISK",
            "affected_gxp_clauses": ["21 CFR Part 11.10(e)", "Annex 11.9"],
            "requires_qa_signoff": True,
            "summary": "High-risk change modifying core audit or eSignature compliance parameters.",
            "risk_summary": "High-risk change modifying core audit or eSignature compliance parameters.",
        }

    if (
        "password" in key.lower()
        or "session" in key.lower()
        or "timeout" in key.lower()
        or "export_filter" in key.lower()
    ):
        return {
            "risk_level": "MEDIUM_RISK",
            "affected_gxp_clauses": ["21 CFR Part 11.10(g)", "ISO 27001 A.9"],
            "requires_qa_signoff": False,
            "summary": "Medium-risk security or session configuration change.",
            "risk_summary": "Medium-risk security or session configuration change.",
        }

    return {
        "risk_level": "LOW_RISK",
        "affected_gxp_clauses": [],
        "requires_qa_signoff": False,
        "summary": "Low-risk configuration change.",
        "risk_summary": "Low-risk configuration change.",
    }
