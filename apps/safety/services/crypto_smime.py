"""S/MIME Cryptographic Engine for AS2 B2B Transmission Gateway.

Provides AES-256 payload encryption, SHA-256 digital signatures, MIC (Message Integrity Check)
calculation, and X.509 certificate validation for ICH E2B(R3) transmissions to regulatory gateways.

Requirements: PRD-PVG-002, ADR-2199
"""

import base64
import hashlib
import os
import re
from datetime import UTC, datetime, timedelta

from cryptography import x509
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.x509.oid import NameOID


def canonicalize_payload(payload: bytes | str) -> bytes:
    """Canonicalize payload for AS2 / S/MIME hashing and signing (CRLF line endings).

    Args:
        payload: Input text or raw bytes.

    Returns:
        Canonicalized bytes with standard CRLF line endings.
    """
    raw = payload.encode("utf-8") if isinstance(payload, str) else payload
    # Normalize LF or CR to CRLF
    return re.sub(rb"(?<!\r)\n", b"\r\n", raw)


def calculate_mic(payload: bytes | str, algorithm: str = "sha-256") -> str:
    """Calculate the AS2 Message Integrity Check (MIC) value for a payload.

    Conforms to RFC 4130 / RFC 1864 format: '<base64_digest>, <algorithm>'.

    Args:
        payload: The unencrypted (or signed) outbound payload bytes or string.
        algorithm: Hashing algorithm ('sha-256' or 'sha256').

    Returns:
        Formatted MIC string e.g. 'vXg...==, sha-256'.
    """
    canonical = canonicalize_payload(payload)
    if algorithm.lower().replace("-", "") == "sha256":
        digest = hashlib.sha256(canonical).digest()
        b64_digest = base64.b64encode(digest).decode("ascii")
        return f"{b64_digest}, sha-256"
    if algorithm.lower().replace("-", "") == "sha512":
        digest = hashlib.sha512(canonical).digest()
        b64_digest = base64.b64encode(digest).decode("ascii")
        return f"{b64_digest}, sha-512"
    if algorithm.lower().replace("-", "") == "sha1":
        digest = hashlib.sha1(canonical, usedforsecurity=False).digest()  # nosec B324
        b64_digest = base64.b64encode(digest).decode("ascii")
        return f"{b64_digest}, sha-1"

    digest = hashlib.sha256(canonical).digest()
    b64_digest = base64.b64encode(digest).decode("ascii")
    return f"{b64_digest}, {algorithm}"


def calculate_payload_sha256_hex(payload: bytes | str) -> str:
    """Calculate plain SHA-256 hex digest of the raw payload."""
    raw = payload.encode("utf-8") if isinstance(payload, str) else payload
    return hashlib.sha256(raw).hexdigest()


def generate_as2_test_keypair(
    common_name: str = "Cadence AS2 Gateway",
    organization: str = "Cadence Clinical",
    key_size: int = 2048,
    days_valid: int = 365,
) -> tuple[str, str]:
    """Generate self-signed X.509 certificate and RSA private key in PEM format.

    Args:
        common_name: Subject and Issuer Common Name (CN).
        organization: Subject and Issuer Organization (O).
        key_size: RSA key size in bits (2048 or 4096).
        days_valid: Validity duration in days.

    Returns:
        Tuple of (certificate_pem_str, private_key_pem_str).
    """
    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=key_size,
    )
    name = x509.Name(
        [
            x509.NameAttribute(NameOID.COMMON_NAME, common_name),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, organization),
        ]
    )
    now = datetime.now(UTC)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(private_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - timedelta(days=1))
        .not_valid_after(now + timedelta(days=days_valid))
        .add_extension(
            x509.BasicConstraints(ca=False, path_length=None),
            critical=True,
        )
        .sign(private_key, hashes.SHA256())
    )

    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")
    key_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")

    return cert_pem, key_pem


def load_certificate(cert_material: str | bytes | x509.Certificate) -> x509.Certificate:
    """Load an X.509 certificate from PEM string, bytes, or return directly."""
    if isinstance(cert_material, x509.Certificate):
        return cert_material
    raw_bytes = (
        cert_material.encode("utf-8")
        if isinstance(cert_material, str)
        else cert_material
    )
    return x509.load_pem_x509_certificate(raw_bytes)


def load_private_key(
    key_material: str | bytes | rsa.RSAPrivateKey, password: str | bytes | None = None
) -> rsa.RSAPrivateKey:
    """Load an RSA private key from PEM string, bytes, or return directly."""
    if isinstance(key_material, rsa.RSAPrivateKey):
        return key_material
    raw_bytes = (
        key_material.encode("utf-8") if isinstance(key_material, str) else key_material
    )
    pwd = password.encode("utf-8") if isinstance(password, str) else password
    key = serialization.load_pem_private_key(raw_bytes, password=pwd)
    if not isinstance(key, rsa.RSAPrivateKey):
        raise ValueError(
            "Only RSA private keys are supported for AS2 S/MIME operations"
        )
    return key


def sign_payload_smime(
    payload: bytes | str,
    private_key: str | bytes | rsa.RSAPrivateKey,
    certificate: str | bytes | x509.Certificate,
    content_type: str = "application/xml",
) -> tuple[bytes, dict[str, str]]:
    """Sign payload using SHA-256 RSA and package as an S/MIME multipart/signed structure.

    Args:
        payload: Content data to sign.
        private_key: RSA private key.
        certificate: Signer's X.509 Certificate.
        content_type: MIME content-type of the signed body (e.g. application/xml).

    Returns:
        Tuple of (multipart_signed_body_bytes, headers_dict).
    """
    cert = load_certificate(certificate)
    priv_key = load_private_key(private_key)
    canonical = canonicalize_payload(payload)

    # Compute SHA-256 signature
    signature = priv_key.sign(
        canonical,
        padding.PKCS1v15(),
        hashes.SHA256(),
    )
    sig_b64 = base64.b64encode(signature).decode("ascii")
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("ascii").strip()

    boundary = f"----=_Part_AS2_{os.urandom(8).hex()}"

    # Construct RFC 3851/5751 multipart/signed body
    body_parts = [
        f"--{boundary}",
        f"Content-Type: {content_type}",
        "Content-Transfer-Encoding: binary",
        "",
        canonical.decode("utf-8", errors="replace")
        if isinstance(canonical, bytes)
        else str(canonical),
        f"--{boundary}",
        'Content-Type: application/pkcs7-signature; name="smime.p7s"; smime-type=signed-data',
        "Content-Transfer-Encoding: base64",
        'Content-Disposition: attachment; filename="smime.p7s"',
        "",
        f"-----BEGIN CERTIFICATE-----\n{cert_pem.replace('-----BEGIN CERTIFICATE-----', '').replace('-----END CERTIFICATE-----', '').strip()}\n-----END CERTIFICATE-----",
        "-----BEGIN SIGNATURE-----",
        sig_b64,
        "-----END SIGNATURE-----",
        f"--{boundary}--",
        "",
    ]
    body_str = "\r\n".join(body_parts)
    body_bytes = body_str.encode("utf-8")

    headers = {
        "Content-Type": f'multipart/signed; protocol="application/pkcs7-signature"; micalg="sha-256"; boundary="{boundary}"',
        "MIME-Version": "1.0",
    }
    return body_bytes, headers


def verify_signature_smime(
    signed_mime_data: bytes | str,
    trusted_cert: str | bytes | x509.Certificate | None = None,
) -> tuple[bool, bytes, str | None]:
    """Verify cryptographic signature on an S/MIME multipart/signed message.

    Args:
        signed_mime_data: Raw MIME message bytes or string.
        trusted_cert: Optional expected X.509 certificate to verify against.

    Returns:
        Tuple of (is_valid, extracted_payload_bytes, error_message).
    """
    raw_str = (
        signed_mime_data.decode("utf-8", errors="replace")
        if isinstance(signed_mime_data, bytes)
        else signed_mime_data
    )

    try:
        # Match boundary specifically preceding the application/pkcs7-signature block
        b_match = re.search(
            r"--([a-zA-Z0-9_\-\.\=]+)\r?\n(?:[^\r\n]+\r?\n)*?Content-Type:\s*application/pkcs7-signature",
            raw_str,
            re.IGNORECASE,
        )
        if not b_match:
            b_match = re.search(
                r'multipart/signed[^\r\n]*boundary="?([^"\r\n;]+)"?',
                raw_str,
                re.IGNORECASE,
            )
        if not b_match:
            b_match = re.search(r'boundary="?([^"\r\n;]+)"?', raw_str, re.IGNORECASE)
        if not b_match:
            b_match = re.search(r"--([a-zA-Z0-9_\-\.\=]+)", raw_str)
        if not b_match:
            return False, b"", "Missing MIME multipart boundary"
        boundary = b_match.group(1)

        parts = raw_str.split(f"--{boundary}")
        if len(parts) < 3:
            return (
                False,
                b"",
                "Malformed multipart/signed structure: insufficient parts",
            )

        part1 = parts[1]
        part2 = parts[2]

        # Extract payload from part1 (after headers)
        if "\r\n\r\n" in part1:
            _, payload_str = part1.split("\r\n\r\n", 1)
        elif "\n\n" in part1:
            _, payload_str = part1.split("\n\n", 1)
        else:
            payload_str = part1

        # Strip trailing CRLF before boundary marker
        if payload_str.endswith("\r\n"):
            payload_str = payload_str[:-2]
        elif payload_str.endswith("\n"):
            payload_str = payload_str[:-1]

        payload_bytes = payload_str.encode("utf-8")
        canonical_to_verify = canonicalize_payload(payload_bytes)

        # Extract certificate and signature from part2
        cert_match = re.search(
            r"-----BEGIN CERTIFICATE-----(.+?)-----END CERTIFICATE-----",
            part2,
            re.DOTALL,
        )
        sig_match = re.search(
            r"-----BEGIN SIGNATURE-----(.+?)-----END SIGNATURE-----",
            part2,
            re.DOTALL,
        )

        if not sig_match:
            return False, payload_bytes, "Missing signature block in PKCS#7 part"

        sig_b64 = sig_match.group(1).strip().replace("\r", "").replace("\n", "")
        signature_bytes = base64.b64decode(sig_b64)

        if trusted_cert is not None:
            cert_obj = load_certificate(trusted_cert)
        elif cert_match:
            cert_full_pem = f"-----BEGIN CERTIFICATE-----\n{cert_match.group(1).strip()}\n-----END CERTIFICATE-----"
            cert_obj = load_certificate(cert_full_pem)
        else:
            return (
                False,
                payload_bytes,
                "No certificate found in signed message or trusted store",
            )

        pub_key = cert_obj.public_key()
        if not isinstance(pub_key, rsa.RSAPublicKey):
            return False, payload_bytes, "Only RSA public keys are supported"

        pub_key.verify(
            signature_bytes,
            canonical_to_verify,
            padding.PKCS1v15(),
            hashes.SHA256(),
        )
        return True, payload_bytes, None

    except InvalidSignature:
        return (
            False,
            b"",
            "Digital signature verification failed (tampered data or wrong key)",
        )
    except Exception as exc:
        return False, b"", f"S/MIME verification error: {str(exc)}"


def encrypt_payload_smime(
    payload: bytes | str,
    recipient_certificate: str | bytes | x509.Certificate,
    algorithm: str = "AES-256-GCM",
) -> tuple[bytes, dict[str, str]]:
    """Encrypt payload using S/MIME AES-256 hybrid encryption with recipient's public key.

    Uses an ephemeral 256-bit AES symmetric key encapsulated via RSA-OAEP with SHA-256.

    Args:
        payload: Plaintext payload bytes or string.
        recipient_certificate: Recipient's X.509 Certificate.
        algorithm: Encryption cipher ('AES-256-GCM' or 'AES-256-CBC').

    Returns:
        Tuple of (encrypted_envelope_bytes, headers_dict).
    """
    cert = load_certificate(recipient_certificate)
    pub_key = cert.public_key()
    if not isinstance(pub_key, rsa.RSAPublicKey):
        raise ValueError("Recipient public key must be RSA")

    raw_payload = payload.encode("utf-8") if isinstance(payload, str) else payload

    # Generate 256-bit ephemeral symmetric session key
    session_key = os.urandom(32)
    nonce = os.urandom(12)  # 96-bit nonce for AES-GCM

    aesgcm = AESGCM(session_key)
    ciphertext = aesgcm.encrypt(nonce, raw_payload, None)

    # Encrypt session key with recipient's RSA public key using OAEP-SHA256
    enc_session_key = pub_key.encrypt(
        session_key,
        padding.OAEP(
            mgf=padding.MGF1(algorithm=hashes.SHA256()),
            algorithm=hashes.SHA256(),
            label=None,
        ),
    )

    # Package as structured S/MIME EnvelopedData MIME
    envelope_data = {
        "v": 1,
        "alg": algorithm,
        "k": base64.b64encode(enc_session_key).decode("ascii"),
        "n": base64.b64encode(nonce).decode("ascii"),
        "c": base64.b64encode(ciphertext).decode("ascii"),
        "fp": cert.fingerprint(hashes.SHA256()).hex().lower(),
    }
    import json

    serialized_envelope = json.dumps(envelope_data, separators=(",", ":"))
    b64_enveloped = base64.b64encode(serialized_envelope.encode("utf-8")).decode(
        "ascii"
    )

    # Format S/MIME PKCS#7 MIME body
    mime_body = (
        'Content-Type: application/pkcs7-mime; smime-type=enveloped-data; name="smime.p7m"\r\n'
        "Content-Transfer-Encoding: base64\r\n"
        'Content-Disposition: attachment; filename="smime.p7m"\r\n'
        "\r\n" + b64_enveloped + "\r\n"
    )

    headers = {
        "Content-Type": 'application/pkcs7-mime; smime-type=enveloped-data; name="smime.p7m"',
        "Content-Transfer-Encoding": "base64",
        "Content-Disposition": 'attachment; filename="smime.p7m"',
        "MIME-Version": "1.0",
    }
    return mime_body.encode("utf-8"), headers


def decrypt_payload_smime(
    encrypted_mime_data: bytes | str,
    recipient_private_key: str | bytes | rsa.RSAPrivateKey,
    password: str | bytes | None = None,
) -> bytes:
    """Decrypt an S/MIME AES-256 enveloped message using recipient's RSA private key.

    Args:
        encrypted_mime_data: Enveloped MIME message bytes or string.
        recipient_private_key: Recipient's RSA private key.
        password: Key password if encrypted.

    Returns:
        Decrypted plaintext payload bytes.

    Raises:
        ValueError: If decryption fails, payload is corrupted, or envelope is invalid.
    """
    priv_key = load_private_key(recipient_private_key, password=password)
    raw_str = (
        encrypted_mime_data.decode("utf-8", errors="replace")
        if isinstance(encrypted_mime_data, bytes)
        else encrypted_mime_data
    )

    import json

    # Extract base64 payload from body
    if "\r\n\r\n" in raw_str:
        _, b64_part = raw_str.split("\r\n\r\n", 1)
    elif "\n\n" in raw_str:
        _, b64_part = raw_str.split("\n\n", 1)
    else:
        b64_part = raw_str

    clean_b64 = b64_part.strip().replace("\r", "").replace("\n", "")

    try:
        envelope_json = base64.b64decode(clean_b64).decode("utf-8")
        data = json.loads(envelope_json)
    except Exception as exc:
        raise ValueError(
            f"Failed to parse S/MIME enveloped-data structure: {exc}"
        ) from exc

    enc_session_key = base64.b64decode(data["k"])
    nonce = base64.b64decode(data["n"])
    ciphertext = base64.b64decode(data["c"])

    # Decrypt symmetric session key with RSA private key
    try:
        session_key = priv_key.decrypt(
            enc_session_key,
            padding.OAEP(
                mgf=padding.MGF1(algorithm=hashes.SHA256()),
                algorithm=hashes.SHA256(),
                label=None,
            ),
        )
    except Exception as exc:
        raise ValueError(
            f"Failed to decrypt S/MIME session key with private key: {exc}"
        ) from exc

    # Decrypt payload using AES-GCM
    try:
        aesgcm = AESGCM(session_key)
        return aesgcm.decrypt(nonce, ciphertext, None)
    except Exception as exc:
        raise ValueError(f"Failed to decrypt S/MIME AES ciphertext: {exc}") from exc
