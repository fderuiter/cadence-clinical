"""Unit tests for Fileshare microservice storage adapter module.

Requirements: PRD-SYS-001, PRD-DOC-001
"""

import apps.fileshare.adapters.storage as fileshare_storage
from packages.storage import reset_storage_adapter
from packages.testing.fakes import InMemoryStoragePort


def test_fileshare_storage_delegation():
    """Verify Fileshare storage adapter delegates to shared factory without local global variables."""
    # Ensure no local global singleton variable exists
    assert not hasattr(fileshare_storage, "_global_storage_adapter")

    fake = InMemoryStoragePort()
    fileshare_storage.set_storage_adapter(fake)

    assert fileshare_storage.get_storage_adapter() is fake

    fileshare_storage.reset_storage_adapter()
    reset_storage_adapter()
