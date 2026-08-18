"""GxP Markdown document signing service.

Generates identity-bound electronic signatures for GxP release documentation
using asymmetric ECDSA or RSA-PSS keys in compliance with 21 CFR Part 11.

Requirements: PRD-SYS-001
"""

import base64
import os
from datetime import UTC, datetime, timedelta

os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "internal-audit-key-for-gxp-sync"
)  # pragma: allowlist secret
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "internal-email-hmac-secret-12345"
)  # pragma: allowlist secret

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, padding, rsa

from packages.security.cert_store import get_active_cert_store


def generate_gxp_signing_credentials(
    signer_id: str = "cadence-gxp-runner",
    key_type: str = "RSA",
) -> tuple[str, str]:
    """Generate an asymmetric key pair and X.509 certificate for GxP document signing.

    Args:
        signer_id: Unique signer identifier.
        key_type: Cryptographic key type ('RSA' for RSA-PSS or 'ECDSA').

    Returns:
        Tuple of (private_key_pem_string, cert_pem_string).
    """
    if key_type.upper() == "ECDSA":
        private_key = ec.generate_private_key(ec.SECP256R1())
    else:
        private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)

    subject = issuer = x509.Name(
        [
            x509.NameAttribute(
                x509.NameOID.COMMON_NAME, f"Cadence GxP Validation Runner ({signer_id})"
            ),
            x509.NameAttribute(
                x509.NameOID.ORGANIZATION_NAME, "Cadence Clinical Software"
            ),
        ]
    )

    now_utc = datetime.now(UTC)
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(private_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now_utc - timedelta(days=1))
        .not_valid_after(now_utc + timedelta(days=365))
        .sign(private_key, hashes.SHA256())
    )

    private_key_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")

    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    return private_key_pem, cert_pem


def sign_gxp_markdown(
    content: str,
    signer_id: str | None = None,
    signing_reason: str = "GxP Qualification Execution Sign-Off",
    timestamp: str | None = None,
    private_key_pem: str | None = None,
    cert_pem: str | None = None,
    key_type: str = "RSA",
) -> str:
    """Sign a Markdown document payload and append an embedded electronic signature block.

    Args:
        content: Markdown document string.
        signer_id: Unique signer identifier.
        signing_reason: Controlled reason for creating this electronic signature.
        timestamp: Real execution timestamp string in UTC.
        private_key_pem: Optional PEM encoded private key string.
        cert_pem: Optional PEM encoded X.509 certificate string.
        key_type: Key algorithm to use if generating credentials ('RSA' or 'ECDSA').

    Returns:
        Full signed Markdown document string containing the embedded electronic signature block footer.
    """
    if not signer_id:
        signer_id = (
            os.getenv("GXP_SIGNER_ID")
            or os.getenv("GITHUB_ACTOR")
            or os.getenv("USER")
            or "cadence-validation-runner"
        )

    if not timestamp:
        timestamp = datetime.now(UTC).strftime("%Y-%m-%d %H:%M:%S UTC")

    # Retrieve from environment or generate fresh pair if not provided
    if not private_key_pem or not cert_pem:
        env_priv = os.getenv("GXP_SIGNING_PRIVATE_KEY")
        env_cert = os.getenv("GXP_SIGNING_CERT")
        if env_priv and env_cert:
            private_key_pem = env_priv
            cert_pem = env_cert
        else:
            private_key_pem, cert_pem = generate_gxp_signing_credentials(
                signer_id=signer_id, key_type=key_type
            )

    # Register certificate in the active certificate store
    cert_store = get_active_cert_store()
    cert_store.register_certificate(signer_id, cert_pem)

    # Compute SHA-256 digest of main Markdown content
    content_bytes = content.strip().encode("utf-8")
    content_digest = hashes.Hash(hashes.SHA256())
    content_digest.update(content_bytes)
    sha256_hash = content_digest.finalize().hex()

    # Construct human-readable Electronic Signature Block header
    footer_text = (
        f"{content.rstrip()}\n\n"
        f"---\n\n"
        f"## Electronic Signature Block\n\n"
        f"- **Signer Identity:** {signer_id}\n"
        f"- **Timestamp:** {timestamp}\n"
        f"- **Meaning / Purpose:** {signing_reason}\n"
        f"- **Cryptographic Hash (SHA-256):** {sha256_hash}\n\n"
    )

    original_data = footer_text.rstrip().encode("utf-8")
    cert_pem_bytes = cert_pem.strip().encode("utf-8")

    private_key = serialization.load_pem_private_key(
        private_key_pem.encode("utf-8"), password=None
    )

    signed_payload = original_data + cert_pem_bytes

    if isinstance(private_key, rsa.RSAPrivateKey):
        sig_bytes = private_key.sign(
            signed_payload,
            padding.PSS(
                mgf=padding.MGF1(hashes.SHA256()),
                salt_length=padding.PSS.MAX_LENGTH,
            ),
            hashes.SHA256(),
        )
    elif isinstance(private_key, ec.EllipticCurvePrivateKey):
        sig_bytes = private_key.sign(
            signed_payload,
            ec.ECDSA(hashes.SHA256()),
        )
    else:
        raise ValueError("Unsupported private key algorithm for asymmetric signing.")

    sig_b64 = base64.b64encode(sig_bytes).decode("utf-8")

    # Assemble complete signed Markdown output
    return (
        f"{footer_text}"
        f"{cert_pem.strip()}\n"
        f"-----BEGIN SIGNATURE-----\n"
        f"{sig_b64}\n"
        f"-----END SIGNATURE-----\n"
    )


__all__ = [
    "generate_gxp_signing_credentials",
    "sign_gxp_markdown",
]
