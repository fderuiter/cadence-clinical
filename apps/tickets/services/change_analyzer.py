"""
Service for analyzing configuration changes and assessing GxP and regulatory risks.
"""

from fastapi import HTTPException, status

from apps.tickets.application.change_analyzer import (
    SettingChangeAnalyzer,
    parse_value,
)
from apps.tickets.application.change_analyzer import (
    analyze_setting_change as app_analyze_setting_change,
)
from apps.tickets.presentation.dtos import RegulatoryRiskAssessment
from packages.hexagonal import DomainError, ValidationError


def analyze_setting_change(
    key: str, old_val: str, new_val: str
) -> RegulatoryRiskAssessment:
    """Analyze proposed setting change and evaluate GxP regulatory risk level.

    Requirements: PRD-SYS-001
    """
    try:
        res = app_analyze_setting_change(key, old_val, new_val)
        return RegulatoryRiskAssessment(
            risk_level=res.risk_level,
            affected_gxp_clauses=res.affected_gxp_clauses,
            requires_qa_signoff=res.requires_qa_signoff,
            summary=res.summary,
            risk_summary=res.risk_summary,
        )
    except (ValidationError, DomainError) as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(e),
        ) from e


__all__ = [
    "SettingChangeAnalyzer",
    "analyze_setting_change",
    "parse_value",
]
