from datetime import datetime
from enum import StrEnum

from pydantic import BaseModel, Field


class UnblindingReasonCode(StrEnum):
    """Controlled vocabulary of approved reason codes for emergency unblinding.

    Only these three regulatory-approved scenarios authorise an emergency
    treatment-allocation disclosure outside of the standard end-of-study
    unblinding process.

    Attributes:
        SAE_LIFE_THREATENING_EVENT: Serious Adverse Event that is immediately
            life-threatening and requires knowledge of the treatment assignment.
        ACCIDENTAL_OVERDOSE: Accidental administration of an overdose requiring
            immediate clinical intervention with knowledge of the treatment arm.
        REQUIRED_BY_REGULATORY_AUTHORITY: A competent regulatory authority has
            formally requested disclosure of the blinded assignment.
    """

    SAE_LIFE_THREATENING_EVENT = "SAE-Life-Threatening-Event"
    ACCIDENTAL_OVERDOSE = "Accidental-Overdose"
    REQUIRED_BY_REGULATORY_AUTHORITY = "Required-by-Regulatory-Authority"


class CustodianEnum(StrEnum):
    """Enumeration of the two permissible dual-custody key holders.

    The Shamir secret-sharing scheme used for emergency unblinding mandates
    that exactly one share comes from each of these two custodians.  Any
    other custodian identity is rejected with a 422 validation error before
    the request reaches the cryptographic layer.

    Attributes:
        LEAD_UNBLINDED_STATISTICIAN: The lead unblinded statistician who holds
            one half of the Shamir key share.
        IDMC: The Independent Data Monitoring Committee representative who holds
            the second half of the Shamir key share.
    """

    LEAD_UNBLINDED_STATISTICIAN = "Lead Unblinded Statistician"
    IDMC = "IDMC"


class CustodianShare(BaseModel):
    """A single custodian's Shamir secret share for dual-custody unblinding.

    Both shares must be present in the request body before the encrypted
    allocation record can be reconstructed.  Field constraints are enforced
    at the schema boundary so malformed shares produce structured 422
    responses rather than opaque crypto-layer failures.

    Attributes:
        custodian: The identity of the key custodian; must be one of the two
            approved dual-custody holders defined by ``CustodianEnum``.
        version: The version of the key material associated with this share;
            used to select the correct key generation from the database.
        x: The x-coordinate of the Shamir share point; must be strictly
            positive (> 0) as required by the polynomial reconstruction.
        y: The y-coordinate of the Shamir share point; must be non-negative
            (>= 0) and less than the prime modulus used by the crypto layer.
    """

    custodian: CustodianEnum
    version: int
    x: int = Field(..., gt=0, description="Shamir x-coordinate; must be > 0")
    y: int = Field(..., ge=0, description="Shamir y-coordinate; must be >= 0")


MIN_JUSTIFICATION_LENGTH = 50


class UnblindRequest(BaseModel):
    """Request body for an emergency treatment-allocation unblinding operation.

    The dual-custody contract requires exactly two custodian shares — one from
    each approved custodian.  Requests with fewer or more shares, or with an
    insufficiently detailed justification, are rejected at the schema layer.

    Attributes:
        reason_code: One of the three regulatory-approved unblinding scenarios
            from ``UnblindingReasonCode``.
        justification: A free-text clinical justification of at least
            ``MIN_JUSTIFICATION_LENGTH`` characters.  Stored only in the
            immutable audit record; never broadcast in notifications.
        shares: Exactly two ``CustodianShare`` objects — one per approved
            custodian — supplying the Shamir secret shares needed to
            reconstruct the blinded allocation key.
    """

    reason_code: UnblindingReasonCode
    justification: str = Field(..., min_length=MIN_JUSTIFICATION_LENGTH)
    shares: list[CustodianShare] = Field(
        ...,
        min_length=2,
        max_length=2,
        description="Exactly two custodian shares are required (dual-custody contract).",
    )


class SubjectUnblindResponse(BaseModel):
    """Pydantic schema for returning emergency unblind details."""

    subject_id: str
    status: str
    is_unblinded: bool
    treatment_arm: str | None = None
    drug_code: str | None = None
    unblinded_at: datetime | None = None
    unblinded_by: str | None = None
    unblinded_reason: str | None = None
