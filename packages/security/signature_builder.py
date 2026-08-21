"""Cryptographic signature payload builder service for 21 CFR Part 11 electronic signatures.

Requirements: PRD-SYS-001
"""

import base64
import json
from datetime import UTC, datetime
from typing import Any

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding
from cryptography.hazmat.primitives.serialization import (
    load_pem_private_key,
    load_pem_public_key,
)

from packages.security.signing import compute_sha256_hash


class CryptographicSignatureBuilder:
    """Builder and verifier service for SHA-256 + RSA electronic signature payloads.

    Requirements: PRD-SYS-001
    """

    def compute_content_digest(self, content: Any) -> str:
        """Compute SHA-256 digest hex string for arbitrary content or dictionary payload.

        Args:
            content: Target payload object or dictionary.

        Returns:
            SHA-256 digest string in hex format.
        """
        if isinstance(content, (dict, list)):
            serialized = json.dumps(content, sort_keys=True, separators=(",", ":"))
        else:
            serialized = str(content)

        return compute_sha256_hash(serialized)

    def build_signature_payload(
        self,
        user_id: str,
        purpose: str,
        content_digest: str,
        timestamp_utc: str | None = None,
    ) -> dict[str, Any]:
        """Construct canonical 21 CFR Part 11 electronic signature payload.

        Args:
            user_id: Signer user ID.
            purpose: Signature purpose (e.g. Principal Investigator Approval).
            content_digest: SHA-256 digest of signed casebook data.
            timestamp_utc: Optional ISO timestamp. Defaults to current UTC time.

        Returns:
            Structured signature payload dictionary.
        """
        now_iso = timestamp_utc or datetime.now(UTC).isoformat()
        return {
            "user_id": user_id,
            "purpose": purpose,
            "content_digest": content_digest,
            "timestamp_utc": now_iso,
        }

    def sign_payload_rsa(self, payload: dict[str, Any], private_key_pem: bytes) -> str:
        """Sign electronic signature payload using RSA-SHA256 private key.

        Args:
            payload: Signature payload dictionary.
            private_key_pem: PEM encoded RSA private key bytes.

        Returns:
            Base64 encoded digital signature string.
        """
        private_key = load_pem_private_key(private_key_pem, password=None)
        serialized = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode(
            "utf-8"
        )

        signature_bytes = private_key.sign(
            serialized,
            padding.PKCS1v15(),
            hashes.SHA256(),
        )
        return base64.b64encode(signature_bytes).decode("utf-8")

    def verify_signature_rsa(
        self, payload: dict[str, Any], signature_base64: str, public_key_pem: bytes
    ) -> bool:
        """Verify validity of RSA-SHA256 digital signature against public key.

        Args:
            payload: Signature payload dictionary.
            signature_base64: Base64 encoded digital signature string.
            public_key_pem: PEM encoded RSA public key bytes.

        Returns:
            True if signature is valid; False otherwise.
        """
        try:
            public_key = load_pem_public_key(public_key_pem)
            signature_bytes = base64.b64decode(signature_base64)
            serialized = json.dumps(
                payload, sort_keys=True, separators=(",", ":")
            ).encode("utf-8")

            public_key.verify(
                signature_bytes,
                serialized,
                padding.PKCS1v15(),
                hashes.SHA256(),
            )
            return True
        except Exception:
            return False
