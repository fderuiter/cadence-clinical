"""
Service for analyzing configuration changes and assessing GxP and regulatory risks.
"""

from typing import Optional

from fastapi import HTTPException

from apps.tickets.models.diff_models import RegulatoryRiskAssessment


def parse_value(val: Optional[str]) -> any:
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


def analyze_setting_change(
    key: str, old_val: str, new_val: str
) -> RegulatoryRiskAssessment:
    """Analyze proposed setting change and evaluate GxP regulatory risk level.

    Requirements: PRD-SYS-001
    """
    parsed_old = parse_value(old_val)
    parsed_new = parse_value(new_val)

    # 1. Block disabling of audit trail logging outright to satisfy 21 CFR Part 11 requirements
    if "audit" in key.lower():
        if parsed_new is False:
            raise HTTPException(
                status_code=400,
                detail="Disabling audit trail logging is strictly prohibited under 21 CFR Part 11.",
            )

    # 2. No-op comparison
    if parsed_old == parsed_new:
        return RegulatoryRiskAssessment(
            risk_level="LOW_RISK",
            affected_gxp_clauses=[],
            requires_qa_signoff=False,
            summary="No functional configuration delta detected.",
            risk_summary="No functional configuration delta detected.",
        )

    # 3. High Risk: Changes affecting audit trail immutability, eSignature verification, or data locking.
    if (
        key.startswith("audit_")
        or key.startswith("esignature_")
        or "esignature" in key.lower()
        or "double_auth" in key.lower()
        or "data_lock" in key.lower()
        or key.startswith("lock_")
    ):
        return RegulatoryRiskAssessment(
            risk_level="HIGH_RISK",
            affected_gxp_clauses=["21 CFR Part 11.10(e)", "Annex 11.9"],
            requires_qa_signoff=True,
            summary="High-risk change modifying core audit or eSignature compliance parameters.",
            risk_summary="High-risk change modifying core audit or eSignature compliance parameters.",
        )

    # 4. Medium Risk: Changes affecting password expiration, session timeouts, or export filters.
    if (
        "password" in key.lower()
        or "session" in key.lower()
        or "timeout" in key.lower()
        or "export_filter" in key.lower()
    ):
        return RegulatoryRiskAssessment(
            risk_level="MEDIUM_RISK",
            affected_gxp_clauses=["21 CFR Part 11.10(g)", "ISO 27001 A.9"],
            requires_qa_signoff=False,
            summary="Medium-risk security or session configuration change.",
            risk_summary="Medium-risk security or session configuration change.",
        )

    # 5. Low Risk: Changes affecting UI themes, default pagination limits, or display formats.
    return RegulatoryRiskAssessment(
        risk_level="LOW_RISK",
        affected_gxp_clauses=[],
        requires_qa_signoff=False,
        summary="Low-risk configuration change.",
        risk_summary="Low-risk configuration change.",
    )


class SettingChangeAnalyzer:
    """
    Class implementation of the setting change analyzer for interface compatibility.
    """

    def analyze_change(
        self, setting_key: str, old_val: str, new_val: str
    ) -> RegulatoryRiskAssessment:
        """Analyze proposed setting change and evaluate GxP regulatory risk level.

        Requirements: PRD-SYS-001
        """
        return analyze_setting_change(setting_key, old_val, new_val)
