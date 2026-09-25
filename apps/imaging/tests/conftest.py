"""Pytest fixtures for Imaging microservice storage testing."""

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
def fake_imaging_storage():
    """Provide an in-memory storage adapter registered for imaging operations."""
    adapter = InMemoryStoragePort()
    set_storage_adapter(adapter)
    yield adapter
    reset_storage_adapter()
