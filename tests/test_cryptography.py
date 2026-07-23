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
