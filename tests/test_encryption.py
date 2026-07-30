# @Req:PRD-EDC-007
import pytest

from packages.security.encryption import (
    ENVELOPE_VERSION,
    decrypt,
    derive_session_key,
    encrypt,
)


def test_encryption_roundtrip():
    """Tests encrypt/decrypt round-trip works correctly with and without AAD."""
    payload = {"answers": {"vssbp": 120, "vsdpb": 80}, "subject_id": "sub_123"}
    key = b"a" * 32  # 32-byte key
    aad = b"client_abc:sequence_1"

    # With AAD
    encrypted_str_with_aad = encrypt(payload, key, ENVELOPE_VERSION, aad)
    assert isinstance(encrypted_str_with_aad, str)
    decrypted_with_aad = decrypt(encrypted_str_with_aad, key, ENVELOPE_VERSION, aad)
    assert decrypted_with_aad == payload

    # Without AAD
    encrypted_str_no_aad = encrypt(payload, key, ENVELOPE_VERSION)
    decrypted_no_aad = decrypt(encrypted_str_no_aad, key, ENVELOPE_VERSION)
    assert decrypted_no_aad == payload


def test_encryption_tamper_rejection():
    """Tests that any tampering with ciphertext, nonce, AAD, or version marker is rejected."""
    payload = {"test": "data"}
    key = b"b" * 32
    aad = b"authenticated_data"

    encrypted_str = encrypt(payload, key, ENVELOPE_VERSION, aad)

    # 1. Tampered AAD
    with pytest.raises(ValueError, match="Decryption failed"):
        decrypt(encrypted_str, key, ENVELOPE_VERSION, b"wrong_aad")

    # 2. Unknown/Unrecognized version marker
    with pytest.raises(ValueError, match="Unrecognized version marker"):
        decrypt(encrypted_str, key, expected_version=99, aad=aad)

    # 3. Tampered ciphertext (altering the base64 string)
    # Let's decode, alter a byte in ciphertext, re-encode
    import base64

    raw_bytes = bytearray(base64.b64decode(encrypted_str))
    # Alter ciphertext byte (somewhere after the first 16 bytes)
    raw_bytes[-1] ^= 0xFF
    tampered_str = base64.b64encode(raw_bytes).decode("utf-8")

    with pytest.raises(ValueError, match="Decryption failed"):
        decrypt(tampered_str, key, ENVELOPE_VERSION, aad)

    # 4. Tampered nonce (altering the bytes 4 to 16)
    raw_bytes_nonce = bytearray(base64.b64decode(encrypted_str))
    raw_bytes_nonce[5] ^= 0xFF
    tampered_nonce_str = base64.b64encode(raw_bytes_nonce).decode("utf-8")

    with pytest.raises(ValueError, match="Decryption failed"):
        decrypt(tampered_nonce_str, key, ENVELOPE_VERSION, aad)


def test_hkdf_determinism():
    """Tests that HKDF key derivation is deterministic and secure."""
    session_material = "very_long_session_token_material_from_keycloak"
    salt = "subject_101"
    info = "client_device_id_99"

    key1 = derive_session_key(session_material, salt, info)
    key2 = derive_session_key(session_material, salt, info)

    # Deterministic
    assert key1 == key2
    assert len(key1) == 32  # 256 bits

    # Secure and non-persisting (independent from session material representation)
    key_diff_salt = derive_session_key(session_material, "different_subject", info)
    assert key1 != key_diff_salt

    key_diff_info = derive_session_key(session_material, salt, "different_client")
    assert key1 != key_diff_info


def test_rejection_of_invalid_key_material():
    """Tests that decrypt fails if given a different key (equivalent to session expiry/invalidation)."""
    payload = {"secure": "vault"}
    key_correct = b"k" * 32
    key_expired = b"x" * 32

    encrypted_str = encrypt(payload, key_correct, ENVELOPE_VERSION)

    # Decrypting with expired/wrong key should fail immediately
    with pytest.raises(ValueError, match="Decryption failed"):
        decrypt(encrypted_str, key_expired, ENVELOPE_VERSION)
