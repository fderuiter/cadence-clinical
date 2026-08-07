import pytest

from apps.execution.cryptography import AllocationKeyManager


def test_key_splitting():
    # @req:Trace-2
    # @req:PRD-MDR-005
    manager = AllocationKeyManager()
    master_key = manager.generate_master_key()
    shares = manager.split_key(master_key, n=5, k=3)

    assert len(shares) == 5
    # Should reconstruct with 3
    reconstructed = manager.reconstruct_key(shares[:3])
    assert reconstructed == master_key

    # With only 2 shares, it should not equal the master key
    wrong_reconstruction = manager.reconstruct_key(shares[:2])
    assert wrong_reconstruction != master_key


def test_encryption_decryption_with_rotation():
    # @req:Trace-2
    # @req:PRD-MDR-005
    manager = AllocationKeyManager()
    data = {"treatment": "Drug A"}

    encrypted_v1 = manager.encrypt(data)
    assert manager.decrypt(encrypted_v1) == data

    # Rotate keys (simulating 365 days passed)
    manager.rotate_keys()

    encrypted_v2 = manager.encrypt(data)
    # Different ciphertext
    assert encrypted_v1 != encrypted_v2

    # Should still decrypt older data
    assert manager.decrypt(encrypted_v1) == data
    assert manager.decrypt(encrypted_v2) == data


def test_dual_custody_positive():
    """
    Verifies that two valid custody shares can reconstruct the key and
    decrypt the encrypted allocation correctly.
    """
    # @req:PRD-SYS-001
    manager = AllocationKeyManager()

    # Create a custody-restricted version 2
    version = 2
    shares = manager.create_custody_key_version(version)

    assert len(shares) == 2
    custodians = {s["custodian"] for s in shares}
    assert custodians == {"Lead Unblinded Statistician", "IDMC"}

    # Encrypt some allocation data
    manager._current_version = version
    data = {"allocation": "Active Treatment Arm", "subject_id": "SUBJ-001"}

    encrypted = manager.encrypt(data)

    # Normal decrypt must fail because it's custody restricted
    with pytest.raises(PermissionError) as exc_info:
        manager.decrypt(encrypted)
    assert "requires dual-share reconstruction" in str(exc_info.value)

    # Decrypt with both valid shares must succeed
    decrypted = manager.decrypt_with_shares(encrypted, shares)
    assert decrypted == data


def test_dual_custody_negative_single_share():
    """
    Verifies that a single share cannot decrypt an allocation.
    """
    # @req:PRD-SYS-001
    manager = AllocationKeyManager()
    version = 2
    shares = manager.create_custody_key_version(version)

    manager._current_version = version
    data = {"allocation": "Placebo"}
    encrypted = manager.encrypt(data)

    # Try passing only one share (loss/single-share case)
    with pytest.raises(ValueError) as exc_info:
        manager.decrypt_with_shares(encrypted, [shares[0]])
    assert "Exactly two shares are required" in str(exc_info.value)


def test_dual_custody_negative_duplicate_shares():
    """
    Verifies that two copies of the same custodian's share fail to decrypt.
    """
    # @req:PRD-SYS-001
    manager = AllocationKeyManager()
    version = 2
    shares = manager.create_custody_key_version(version)

    manager._current_version = version
    data = {"allocation": "Placebo"}
    encrypted = manager.encrypt(data)

    # Try passing two copies of the "Lead Unblinded Statistician" share
    statistician_share = [
        s for s in shares if s["custodian"] == "Lead Unblinded Statistician"
    ][0]
    mismatched_shares = [statistician_share, dict(statistician_share)]

    with pytest.raises(PermissionError) as exc_info:
        manager.decrypt_with_shares(encrypted, mismatched_shares)
    assert "roles must be exactly" in str(exc_info.value)


def test_dual_custody_negative_tampered_share():
    """
    Verifies that tampered shares reconstruct to a wrong key and fail decryption safely.
    """
    # @req:PRD-SYS-001
    manager = AllocationKeyManager()
    version = 2
    shares = manager.create_custody_key_version(version)

    manager._current_version = version
    data = {"allocation": "Active"}
    encrypted = manager.encrypt(data)

    # Tamper with the y coordinate of the IDMC share
    idmc_share = [s for s in shares if s["custodian"] == "IDMC"][0]
    tampered_idmc = dict(idmc_share)
    tampered_idmc["y"] = (
        tampered_idmc["y"] + 1
    ) % 170141183460469231731687303715884105727

    tampered_shares = [
        [s for s in shares if s["custodian"] == "Lead Unblinded Statistician"][0],
        tampered_idmc,
    ]

    # Decryption must fail safely
    with pytest.raises(ValueError) as exc_info:
        manager.decrypt_with_shares(encrypted, tampered_shares)
    assert "Decryption failed: invalid key reconstruction" in str(exc_info.value)
    # Ensure raw secret keys or plaintexts are not in the exception message
    assert str(tampered_idmc["y"]) not in str(exc_info.value)
    assert "Active" not in str(exc_info.value)


def test_dual_custody_negative_malformed_share():
    """
    Verifies that malformed shares are caught and validated safely.
    """
    # @req:PRD-SYS-001
    manager = AllocationKeyManager()
    version = 2
    shares = manager.create_custody_key_version(version)

    manager._current_version = version
    encrypted = manager.encrypt({"a": 1})

    # 1. Missing keys
    bad_share_1 = {"custodian": "IDMC", "version": 2, "x": 1}  # missing y
    bad_share_2 = [shares[0], bad_share_1]
    with pytest.raises(ValueError) as exc_info:
        manager.decrypt_with_shares(encrypted, bad_share_2)
    assert "missing key" in str(exc_info.value)

    # 2. Invalid types
    bad_share_3 = {"custodian": "IDMC", "version": 2, "x": "invalid_x", "y": 123}
    bad_share_4 = [shares[0], bad_share_3]
    with pytest.raises(ValueError) as exc_info:
        manager.decrypt_with_shares(encrypted, bad_share_4)
    assert "must be integers" in str(exc_info.value)


def test_dual_custody_negative_mismatched_versions():
    """
    Verifies that mismatched versions are caught and validated safely.
    """
    # @req:PRD-SYS-001
    manager = AllocationKeyManager()

    shares_v2 = manager.create_custody_key_version(2)
    shares_v3 = manager.create_custody_key_version(3)

    manager._current_version = 3
    encrypted_v3 = manager.encrypt({"a": 1})

    # 1. Mismatched versions between shares (one share v2, one share v3)
    mixed_shares = [
        [
            s
            for s2 in shares_v2
            if (s := s2)["custodian"] == "Lead Unblinded Statistician"
        ][0],
        [s for s3 in shares_v3 if (s := s3)["custodian"] == "IDMC"][0],
    ]
    with pytest.raises(ValueError) as exc_info:
        manager.decrypt_with_shares(encrypted_v3, mixed_shares)
    assert "Mismatched key versions between custody shares" in str(exc_info.value)

    # 2. Mismatched version between shares (v2) and ciphertext (v3)
    with pytest.raises(ValueError) as exc_info:
        manager.decrypt_with_shares(encrypted_v3, shares_v2)
    assert "Mismatched key version between custody shares and encrypted data" in str(
        exc_info.value
    )
