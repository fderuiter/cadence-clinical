import base64
import json
import os
from typing import Any, Dict, Optional

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

from packages.security.signing import canonical_serialize

ENVELOPE_VERSION = 1


def derive_session_key(
    session_material: bytes | str, salt: bytes | str, info: bytes | str
) -> bytes:
    """Derives a 256-bit key from session token material using HKDF.

    Never returns or persists the raw session token.
    """
    if isinstance(session_material, str):
        session_material = session_material.encode("utf-8")
    if isinstance(salt, str):
        salt = salt.encode("utf-8")
    if isinstance(info, str):
        info = info.encode("utf-8")

    hkdf = HKDF(
        algorithm=hashes.SHA256(),
        length=32,
        salt=salt,
        info=info,
    )
    return hkdf.derive(session_material)


def encrypt(
    payload: Dict[str, Any],
    key: bytes,
    version: int = ENVELOPE_VERSION,
    aad: Optional[bytes] = None,
) -> str:
    """Encrypts a payload dictionary using AES-GCM and packages it in a versioned envelope.

    The envelope format is base64(version(4B, big-endian) || nonce(12B) || ciphertext+tag).
    """
    serialized = canonical_serialize(payload)
    nonce = os.urandom(12)
    aesgcm = AESGCM(key)
    ciphertext = aesgcm.encrypt(nonce, serialized, aad)

    version_bytes = version.to_bytes(4, byteorder="big")
    packed_payload = version_bytes + nonce + ciphertext
    return base64.b64encode(packed_payload).decode("utf-8")


def decrypt(
    encrypted_str: str,
    key: bytes,
    expected_version: int = ENVELOPE_VERSION,
    aad: Optional[bytes] = None,
) -> Dict[str, Any]:
    """Decrypts a versioned AES-GCM envelope and deserializes the JSON payload.

    Raises ValueError on unrecognized version, tampered data, or invalid AAD.
    """
    try:
        raw_bytes = base64.b64decode(encrypted_str.encode("utf-8"))
    except Exception as e:
        raise ValueError("Invalid base64 payload") from e

    if len(raw_bytes) < 16:
        raise ValueError("Invalid envelope format: payload too short")

    version = int.from_bytes(raw_bytes[:4], byteorder="big")
    if version != expected_version:
        raise ValueError(f"Unrecognized version marker: {version}")

    nonce = raw_bytes[4:16]
    ciphertext = raw_bytes[16:]

    aesgcm = AESGCM(key)
    try:
        decrypted_bytes = aesgcm.decrypt(nonce, ciphertext, aad)
    except Exception as e:
        raise ValueError("Decryption failed: tampered ciphertext, nonce, or AAD") from e

    try:
        return json.loads(decrypted_bytes.decode("utf-8"))
    except Exception as e:
        raise ValueError("Deserialization failed: invalid JSON") from e
