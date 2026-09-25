"""Unit tests for Imaging microservice storage adapter module.

Requirements: ADR-2199, PRD-IMG-001
"""

import apps.imaging.adapters.storage as imaging_storage
from packages.storage import reset_storage_adapter
from packages.testing.fakes import InMemoryStoragePort


def test_imaging_storage_delegation():
    """Verify Imaging storage adapter delegates to shared factory without local global variables."""
    # Ensure no local global singleton variable exists
    assert not hasattr(imaging_storage, "_storage_singleton")

    fake = InMemoryStoragePort()
    imaging_storage.set_storage_port(fake)

    assert imaging_storage.get_storage_port() is fake
    assert imaging_storage.get_storage_adapter() is fake

    imaging_storage.reset_storage_port()
    reset_storage_adapter()
