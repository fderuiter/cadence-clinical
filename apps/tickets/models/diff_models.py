"""
Pydantic schemas for automated setting diff analyzer and clinical impact assessment.
"""

from typing import List, Literal

from pydantic import BaseModel


class SettingDiffEntry(BaseModel):
    """
    Schema representing a configuration setting difference entry.
    """

    setting_key: str
    old_value: str
    new_value: str
    data_type: str = ""


class RegulatoryRiskAssessment(BaseModel):
    """
    Schema representing a clinical and regulatory risk assessment for a setting change.
    """

    risk_level: Literal["HIGH_RISK", "MEDIUM_RISK", "LOW_RISK"]
    affected_gxp_clauses: List[str]
    requires_qa_signoff: bool
    summary: str
    risk_summary: str
