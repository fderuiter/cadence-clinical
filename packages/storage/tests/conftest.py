"""Pytest configuration and fixtures for packages/storage tests."""

import pytest
from packages.storage import reset_storage_adapter, set_storage_adapter
from packages.testing.fakes import InMemoryStoragePort


@pytest.fixture(autouse=True)
def auto_reset_storage_registry():
    """Guarantee storage registry isolation before and after every test run."""
    reset_storage_adapter()
    yield
    reset_storage_adapter()


@pytest.fixture
def in_memory_storage():
    """Provide an InMemoryStoragePort instance registered as global adapter."""
    fake = InMemoryStoragePort()
    set_storage_adapter(fake)
    yield fake
    reset_storage_adapter()
