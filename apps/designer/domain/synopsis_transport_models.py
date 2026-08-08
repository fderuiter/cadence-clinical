"""Pydantic transport schemas for protocol synopsis export API endpoints.

Requirements: PRD-SYS-001
"""

from pydantic import BaseModel, Field


class SynopsisExportRequest(BaseModel):
    """Request payload for exporting a clinical protocol synopsis.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique protocol study identifier")
    format: str = Field(
        "pdf", description="Target export format: 'pdf', 'docx', or 'html'"
    )
    creator: str | None = Field(
        "Cadence Clinical DDF Engine", description="Author or creator username"
    )
    change_reason: str | None = Field(
        "Initial Baseline", description="GxP 21 CFR Part 11 change reason"
    )


class SynopsisExportResponse(BaseModel):
    """Response payload containing base64 encoded document export stream.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Protocol study identifier")
    format: str = Field(..., description="Export format")
    content_base64: str = Field(
        ..., description="Base64 encoded binary document stream"
    )
    filename: str = Field(..., description="Generated export filename")
