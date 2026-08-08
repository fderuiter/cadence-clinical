"""
Domain services and analysis engines for Tickets microservice.
"""

from typing import Any

from apps.tickets.domain.exceptions import ValidationError


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
