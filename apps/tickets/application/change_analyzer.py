"""
Service for analyzing configuration changes and assessing GxP and regulatory risks.
"""

from apps.tickets.domain.models import RegulatoryRiskAssessment
from apps.tickets.domain.services import evaluate_setting_risk, parse_value


def analyze_setting_change(
    key: str, old_val: str, new_val: str
) -> RegulatoryRiskAssessment:
    """Analyze proposed setting change and evaluate GxP regulatory risk level.

    Requirements: PRD-SYS-001
    """
    metrics = evaluate_setting_risk(key, old_val, new_val)
    return RegulatoryRiskAssessment(
        risk_level=metrics["risk_level"],
        affected_gxp_clauses=metrics["affected_gxp_clauses"],
        requires_qa_signoff=metrics["requires_qa_signoff"],
        summary=metrics["summary"],
        risk_summary=metrics["risk_summary"],
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


__all__ = [
    "SettingChangeAnalyzer",
    "analyze_setting_change",
    "parse_value",
]
