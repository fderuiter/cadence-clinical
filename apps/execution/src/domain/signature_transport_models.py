"""Pydantic transport schemas for 21 CFR Part 11 batch eSignature execution API.

Requirements: PRD-SYS-001
"""

from pydantic import BaseModel, Field, model_validator


class BatchSignatureRequest(BaseModel):
    """Request payload for Principal Investigator batch eSignature casebook sign-off.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    target_type: str = Field(
        "FORM", description="Target artifact type: FORM, CASEBOOK, DOC"
    )
    target_ids: list[str] = Field(
        default_factory=list, description="List of target artifact IDs"
    )
    target_form_ids: list[str] = Field(
        default_factory=list, description="List of eCRF form IDs to sign"
    )
    signing_reason: str = Field(
        ..., description="21 CFR Part 11 signature purpose/meaning"
    )
    password: str = Field(
        ..., description="Re-authentication password for identity confirmation"
    )
    printed_name: str = Field(
        ..., description="Printed full name of Principal Investigator"
    )

    @model_validator(mode="after")
    def sync_target_ids(self) -> BatchSignatureRequest:
        """Synchronize target_ids and target_form_ids fields."""
        if not self.target_ids and self.target_form_ids:
            self.target_ids = list(self.target_form_ids)
        elif not self.target_form_ids and self.target_ids:
            self.target_form_ids = list(self.target_ids)
        return self


class BatchSignatureResponse(BaseModel):
    """Response payload following successful batch eSignature execution.

    Requirements: PRD-SYS-001
    """

    signature_id: str = Field(
        ..., description="Unique cryptographic signature record ID"
    )
    study_id: str = Field(..., description="Target study ID")
    subject_id: str = Field(..., description="Target subject ID")
    signed_forms_count: int = Field(
        ..., description="Total number of signed eCRF forms"
    )
    content_digest: str = Field(
        ..., description="SHA-256 digest of signed casebook data"
    )
    timestamp_utc: str = Field(
        ..., description="UTC ISO timestamp of signature execution"
    )
    audit_tx: str = Field(..., description="Immutable GxP audit ledger transaction ID")
