from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field


class EConsentSignRequest(BaseModel):
    subject_id: str = Field(..., description="Unique patient identifier")
    icf_version_id: str = Field(..., description="Unique ICF version identifier")
    printed_name: str = Field(..., description="Printed/Full name of the signer")
    relationship_to_subject: Literal["SELF", "PARENT_GUARDIAN", "LAR"] = Field(
        ..., description="Signer relationship to candidate trial participant"
    )
    signature_svg: str = Field(
        ..., description="SVG path representation of interactive drawing"
    )
    otp_auth_code: str = Field(
        ..., description="Step-up OTP authentication verification code"
    )
    reason_for_change: str = Field(
        ..., description="GxP Part 11 required change justification"
    )


class EConsentSignResponse(BaseModel):
    consent_record_id: str = Field(
        ..., description="Unique identifier of persisted database signature record"
    )
    signed_pdf_url: str = Field(
        ..., description="Local/Cloud URL pointing to immutable signed PDF blob"
    )
    signature_timestamp_utc: datetime = Field(
        ..., description="Chronological UTC sign timestamp"
    )
    verification_hash: str = Field(
        ..., description="SHA-256 integrity digest binding details"
    )
