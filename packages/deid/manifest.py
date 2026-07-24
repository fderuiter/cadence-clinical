"""
Tamper-evident, privacy-preserving signed manifests for clinical redaction operations.
"""

from datetime import datetime, timezone
from typing import Dict, List, Optional

from pydantic import BaseModel, Field, field_validator

from packages.deid.transforms import RedactionRecordItem
from packages.security.signing import (
    asymmetric_sign,
    asymmetric_verify,
    canonical_serialize,
    generate_canonical_signature,
    verify_canonical_signature,
)


class RedactionManifest(BaseModel):
    """
    A canonical, signed manifest summarizing a redaction operation.
    Ensures cryptographic integrity and auditability of the de-identification pipeline
    without exposing any raw matched identifiers.
    """

    categories_counts: Dict[str, int] = Field(
        ..., description="Count of redacted items per category"
    )
    strategies: Dict[str, str] = Field(
        ..., description="Mapping of category to applied redaction strategy"
    )
    operator_identity: str = Field(
        ..., description="Identity/name of the operator performing redaction"
    )
    reason: str = Field(..., description="Non-empty justification for the redaction")
    timestamp: str = Field(
        ..., description="ISO 8601 formatted timestamp of the operation"
    )
    source_version: str = Field(
        ..., description="Reference/hash of the source document/version"
    )
    target_version: str = Field(
        ..., description="Reference/hash of the target document/version"
    )
    signature: Optional[str] = Field(
        None, description="The cryptographic signature (HMAC hex or asymmetric base64)"
    )

    @field_validator("reason")
    @classmethod
    def validate_non_empty_reason(cls, v: str) -> str:
        if not v or not v.strip():
            raise ValueError("Reason for redaction must be a non-empty string.")
        return v


def build_redaction_manifest(
    redaction_record: List[RedactionRecordItem],
    operator_identity: str,
    reason: str,
    source_version: str,
    target_version: str,
    timestamp: Optional[str] = None,
) -> RedactionManifest:
    """
    Builds a RedactionManifest from a redaction record.

    Args:
        redaction_record (List[RedactionRecordItem]): Detailed record of performed redactions.
        operator_identity (str): Identity of the operator.
        reason (str): Reason for the operation.
        source_version (str): Source version reference.
        target_version (str): Target version reference.
        timestamp (Optional[str]): Optional ISO timestamp. Defaults to UTC now.

    Returns:
        RedactionManifest: Unsigned redaction manifest.
    """
    categories_counts: Dict[str, int] = {}
    strategies: Dict[str, str] = {}

    for item in redaction_record:
        categories_counts[item.category] = categories_counts.get(item.category, 0) + 1
        strategies[item.category] = item.strategy

    if not timestamp:
        timestamp = datetime.now(timezone.utc).isoformat()

    return RedactionManifest(
        categories_counts=categories_counts,
        strategies=strategies,
        operator_identity=operator_identity,
        reason=reason,
        timestamp=timestamp,
        source_version=source_version,
        target_version=target_version,
    )


def sign_manifest_symmetric(
    manifest: RedactionManifest, secret_key: bytes
) -> RedactionManifest:
    """
    Signs the manifest canonically using HMAC-SHA256 with a secret key.

    Args:
        manifest (RedactionManifest): The manifest to sign.
        secret_key (bytes): HMAC secret key.

    Returns:
        RedactionManifest: Signed manifest.
    """
    payload = manifest.model_dump(exclude={"signature"})
    manifest.signature = generate_canonical_signature(payload, secret_key)
    return manifest


def verify_manifest_symmetric(manifest: RedactionManifest, secret_key: bytes) -> bool:
    """
    Verifies that the HMAC-SHA256 signature matches the canonically serialized manifest.

    Args:
        manifest (RedactionManifest): The manifest to verify.
        secret_key (bytes): HMAC secret key.

    Returns:
        bool: True if signature is valid and intact, False otherwise.
    """
    if not manifest.signature:
        return False
    payload = manifest.model_dump(exclude={"signature"})
    return verify_canonical_signature(payload, manifest.signature, secret_key)


def sign_manifest_asymmetric(
    manifest: RedactionManifest, private_key_pem: str, password: Optional[bytes] = None
) -> RedactionManifest:
    """
    Signs the manifest canonically using a private key (RSA or Elliptic Curve).

    Args:
        manifest (RedactionManifest): The manifest to sign.
        private_key_pem (str): PEM-encoded private key.
        password (Optional[bytes]): Passphrase for private key if encrypted.

    Returns:
        RedactionManifest: Signed manifest.
    """
    payload = manifest.model_dump(exclude={"signature"})
    data = canonical_serialize(payload)
    manifest.signature = asymmetric_sign(data, private_key_pem, password=password)
    return manifest


def verify_manifest_asymmetric(
    manifest: RedactionManifest, public_key_pem_or_cert_pem: str
) -> bool:
    """
    Verifies the asymmetric signature of a canonically serialized manifest.

    Args:
        manifest (RedactionManifest): The manifest to verify.
        public_key_pem_or_cert_pem (str): PEM-encoded public key or certificate.

    Returns:
        bool: True if signature is valid and intact, False otherwise.
    """
    if not manifest.signature:
        return False
    payload = manifest.model_dump(exclude={"signature"})
    data = canonical_serialize(payload)
    return asymmetric_verify(data, manifest.signature, public_key_pem_or_cert_pem)
