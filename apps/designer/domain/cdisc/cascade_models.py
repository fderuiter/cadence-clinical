"""Pydantic data models for downstream artifact cascade to eCRF and SoA.

Requirements: PRD-SYS-001
"""

from typing import Any

from pydantic import BaseModel, Field


class CascadedFormTemplate(BaseModel):
    """Auto-generated eCRF form template derived from USDM activities.

    Requirements: PRD-SYS-001
    """

    form_id: str = Field(..., description="Generated eCRF form template ID")
    form_name: str = Field(..., description="eCRF form name")
    domain: str = Field(
        ..., description="Target CDASH/SDTM domain code (e.g. VS, LB, AE)"
    )
    fields: list[dict[str, Any]] = Field(
        default_factory=list, description="Form field definitions"
    )
    auto_generated: bool = Field(
        True, description="True if cascaded from DDF protocol graph"
    )


class CascadeSummaryReport(BaseModel):
    """Summary report of downstream eCRF and SoA cascade propagation.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    amendment_version: int = Field(1, description="Protocol amendment version index")
    forms_created: int = Field(
        ..., description="Number of eCRF form templates generated"
    )
    visits_created: int = Field(..., description="Number of SoA visits synchronized")
    rules_synced: int = Field(..., description="Number of edit check rules generated")
    forms: list[CascadedFormTemplate] = Field(
        default_factory=list, description="Cascaded form templates"
    )
