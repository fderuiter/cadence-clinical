from enum import Enum
from typing import Any, Optional

from datetime_helpers import AwareDatetime
from pydantic import BaseModel, Field, model_validator


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


class SigningReasonCode(str, Enum):
    """Controlled reasons for creating an electronic signature in compliance with 21 CFR Part 11 §11.50."""

    AUTHOR = "author"
    PI_APPROVAL = "approve"
    VERIFY = "verify"
    REVIEW = "review"
    AUTHORIZE_UNBLINDING = "authorize-unblinding"

    @property
    def meaning(self) -> str:
        return SIGNING_REASON_MEANINGS.get(self, self.value)


SIGNING_REASON_MEANINGS = {
    SigningReasonCode.AUTHOR: "The author of the document or record.",
    SigningReasonCode.PI_APPROVAL: "Principal Investigator approval of the clinical trial record.",
    SigningReasonCode.VERIFY: "Verification of source data or record compliance.",
    SigningReasonCode.REVIEW: "Reviewer acknowledgement and validation.",
    SigningReasonCode.AUTHORIZE_UNBLINDING: "Authorization of emergency treatment unblinding.",
}


class ApprovalStatus(str, Enum):
    """Controlled statuses for records requiring approval workflows."""

    PENDING = "PENDING"
    APPROVED = "APPROVED"
    REJECTED = "REJECTED"
    DRAFT = "DRAFT"
    PENDING_SIGNATURE = "PENDING_SIGNATURE"
    SIGNED = "SIGNED"


class SignatureManifestation(BaseModel):
    """
    Pydantic model representing an electronic signature manifestation in compliance with 21 CFR Part 11.
    Contains signer identity, UTC timestamp, signing reason, network/device context, content hash,
    and the cryptographic signature and certificate details.
    """

    # §11.50 requirements
    signer_username: str = Field(..., description="The username of the signer.")
    signer_full_name: str = Field(..., description="The full name of the signer.")
    signing_timestamp_utc: AwareDatetime = Field(
        ..., description="UTC timestamp of the signature."
    )
    signing_reason_code: SigningReasonCode = Field(
        ..., description="System-declared, role-restricted reason code."
    )
    signing_reason_text: str = Field(
        ...,
        description="Human-readable text or meaning associated with the signature reason.",
    )
    network_ip_address: str = Field(
        ..., description="Network IP address of the client."
    )
    device_user_agent: Optional[str] = Field(
        None, description="User agent of the client device."
    )
    signature_hash_sha256: str = Field(
        ..., description="SHA-256 hash of the content being signed."
    )

    # Legacy fields (with defaults for backward compatibility, populated/synced via model_validator)
    signer_id: Optional[str] = Field(
        None, description="Unique identifier of the user or system signing."
    )
    timestamp: Optional[AwareDatetime] = Field(
        None, description="UTC timestamp indicating when applied."
    )
    signing_reason: Optional[SigningReason] = Field(
        None, description="Controlled reason for creating signature."
    )
    ip_address: Optional[str] = Field(
        None, description="The network IP address of the client application."
    )
    user_agent: Optional[str] = Field(
        None, description="The user agent or device context of the client."
    )
    sha256_hash: Optional[str] = Field(
        None, description="SHA-256 hash of the target record or content."
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

    @model_validator(mode="before")
    @classmethod
    def populate_and_fallback_fields(cls, data: Any) -> Any:
        if not isinstance(data, dict):
            return data

        # Mapping legacy input to new fields
        if "signer_id" in data and "signer_username" not in data:
            data["signer_username"] = data["signer_id"]
        if "signer_id" in data and "signer_full_name" not in data:
            data["signer_full_name"] = data["signer_id"]
        if "timestamp" in data and "signing_timestamp_utc" not in data:
            data["signing_timestamp_utc"] = data["timestamp"]
        if "signing_reason" in data and "signing_reason_code" not in data:
            reason = data["signing_reason"]
            if hasattr(reason, "value"):
                reason_str = reason.value
            else:
                reason_str = str(reason)
            if reason_str == "AUTHOR":
                data["signing_reason_code"] = "author"
            elif reason_str in (
                "APPROVAL",
                "SPONSOR_APPROVAL",
                "INVESTIGATOR_SIGNATURE",
            ):
                data["signing_reason_code"] = "approve"
            elif reason_str == "REVIEW":
                data["signing_reason_code"] = "review"
            elif reason_str in ("TECHNICAL_QC", "CLINICAL_QC"):
                data["signing_reason_code"] = "verify"
            else:
                data["signing_reason_code"] = "verify"
        if "signing_reason" in data and "signing_reason_text" not in data:
            val = data["signing_reason"]
            data["signing_reason_text"] = (
                val.value if hasattr(val, "value") else str(val)
            )
        if "ip_address" in data and "network_ip_address" not in data:
            data["network_ip_address"] = data["ip_address"]
        if "user_agent" in data and "device_user_agent" not in data:
            data["device_user_agent"] = data["user_agent"]
        if "sha256_hash" in data and "signature_hash_sha256" not in data:
            data["signature_hash_sha256"] = data["sha256_hash"]

        # Mapping new input to legacy fields
        if "signer_username" in data and "signer_id" not in data:
            data["signer_id"] = data["signer_username"]
        if "signing_timestamp_utc" in data and "timestamp" not in data:
            data["timestamp"] = data["signing_timestamp_utc"]
        if "signing_reason_code" in data and "signing_reason" not in data:
            code = data["signing_reason_code"]
            if hasattr(code, "value"):
                code_str = code.value
            else:
                code_str = str(code)
            if code_str == "author":
                data["signing_reason"] = "AUTHOR"
            elif code_str == "approve":
                data["signing_reason"] = "APPROVAL"
            elif code_str == "review":
                data["signing_reason"] = "REVIEW"
            elif code_str == "verify":
                data["signing_reason"] = "TECHNICAL_QC"
            else:
                data["signing_reason"] = "TECHNICAL_QC"
        if "network_ip_address" in data and "ip_address" not in data:
            data["ip_address"] = data["network_ip_address"]
        if "device_user_agent" in data and "user_agent" not in data:
            data["user_agent"] = data["device_user_agent"]
        if "signature_hash_sha256" in data and "sha256_hash" not in data:
            data["sha256_hash"] = data["signature_hash_sha256"]

        return data

    def get_canonical_bytes(self) -> bytes:
        """
        Generates deterministic, key-sorted, whitespace-stripped canonical bytes of the
        manifestation data fields, excluding cryptographic outputs (signature, certificate, key identifier).
        """
        from packages.security.signing import serialize_manifestation_canonically

        return serialize_manifestation_canonically(self)

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
