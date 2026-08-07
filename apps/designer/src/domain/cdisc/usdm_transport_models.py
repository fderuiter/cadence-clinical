"""Transport models for USDM Gateway REST API endpoints.

Requirements: PRD-SYS-001
"""

from typing import Any

from pydantic import BaseModel, Field


class UsdmImportRequest(BaseModel):
    """Request payload for importing a CDISC USDM protocol JSON specification."""

    raw_usdm_json: dict[str, Any] = Field(
        description="Raw USDM protocol JSON dictionary payload"
    )
    target_version: str = Field(
        default="v3.0", description="Target USDM spec version ('v2.0' or 'v3.0')"
    )
    reason_for_change: str | None = Field(
        default=None, description="GxP audit reason for import/change"
    )


class UsdmImportResponse(BaseModel):
    """Response payload returned after USDM protocol import."""

    study_id: str
    nodes_created: int
    relationships_created: int
    validation_warnings: list[str] = Field(default_factory=list)


class UsdmExportResponse(BaseModel):
    """Response payload for exported USDM protocol graph specification."""

    study_id: str
    usdm_json: dict[str, Any]
