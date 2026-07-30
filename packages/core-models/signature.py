from datetime import datetime, timezone
from enum import Enum
from typing import Optional

from pydantic import BaseModel, Field


class SigningReason(str, Enum):
    """Controlled reasons for creating an electronic signature in compliance with 21 CFR Part 11."""

    AUTHOR = "AUTHOR"
    REVIEW = "REVIEW"
    APPROVAL = "APPROVAL"
    SPONSOR_APPROVAL = "SPONSOR_APPROVAL"
    INVESTIGATOR_SIGNATURE = "INVESTIGATOR_SIGNATURE"
    TECHNICAL_QC = "TECHNICAL_QC"
    CLINICAL_QC = "CLINICAL_QC"
    DATA_LOCK = "DATA_LOCK"
    SYSTEM_SEAL = "SYSTEM_SEAL"
    PROTOCOL_APPROVAL = "PROTOCOL_APPROVAL"
    REGULATORY_FORM_SIGNATURE = "REGULATORY_FORM_SIGNATURE"
    TRAINING_ACKNOWLEDGEMENT = "TRAINING_ACKNOWLEDGEMENT"
    SITE_VISIT_SIGN_OFF = "SITE_VISIT_SIGN_OFF"


class ApprovalStatus(str, Enum):
    """Controlled statuses for records requiring approval workflows."""

    PENDING = "PENDING"
    APPROVED = "APPROVED"
    REJECTED = "REJECTED"


class SignatureManifestation(BaseModel):
    """
    Pydantic model representing an electronic signature manifestation in compliance with 21 CFR Part 11.
    Contains signer identity, UTC timestamp, signing reason, network/device context, content hash,
    and the cryptographic signature and certificate details.
    """

    signer_id: str = Field(
        ..., description="Unique identifier of the user or system signing the record."
    )
    timestamp: datetime = Field(
        ..., description="UTC timestamp indicating when the signature was applied."
    )
    signing_reason: SigningReason = Field(
        ..., description="Controlled reason for creating this electronic signature."
    )
    ip_address: str = Field(
        ..., description="The network IP address of the client application."
    )
    user_agent: Optional[str] = Field(
        None, description="The user agent or device context of the client application."
    )
    sha256_hash: str = Field(
        ..., description="SHA-256 hash of the target record or content being signed."
    )

    # Cryptographic fields
    signature: Optional[str] = Field(
        None,
        description="Base64-encoded asymmetric cryptographic signature of the canonical manifestation bytes.",
    )
    certificate_pem: Optional[str] = Field(
        None,
        description="PEM-encoded X.509 public-key certificate bound to this signature.",
    )
    key_identifier: Optional[str] = Field(
        None,
        description="Unique identifier captured from the signing key or certificate.",
    )

    def get_canonical_bytes(self) -> bytes:
        """
        Generates deterministic, key-sorted, whitespace-stripped canonical bytes of the
        manifestation data fields, excluding cryptographic outputs (signature, certificate, key identifier).
        """
        # Ensure timestamp is normalized to UTC and serialized to a standard ISO-8601 string
        ts_utc = self.timestamp
        if ts_utc.tzinfo is None:
            ts_utc = ts_utc.replace(tzinfo=timezone.utc)
        else:
            ts_utc = ts_utc.astimezone(timezone.utc)

        payload = {
            "signer_id": self.signer_id,
            "timestamp": ts_utc.isoformat(),
            "signing_reason": self.signing_reason.value,
            "ip_address": self.ip_address,
            "user_agent": self.user_agent,
            "sha256_hash": self.sha256_hash,
        }
        # Avoid direct import loop inside security packages if possible
        from packages.security.signing import canonical_serialize

        return canonical_serialize(payload)

    def verify(self) -> bool:
        """
        Verifies that the certificate-bound signature is cryptographically valid for the
        canonical bytes of this signature manifestation.
        """
        if not self.signature or not self.certificate_pem:
            return False

        from packages.security.signing import asymmetric_verify

        return asymmetric_verify(
            data=self.get_canonical_bytes(),
            signature_b64=self.signature,
            public_key_pem_or_cert_pem=self.certificate_pem,
        )
