"""Unit tests for eTMF microservice storage module.

Requirements: PRD-SYS-001, PRD-DOC-001
"""

import base64
from types import SimpleNamespace

import apps.etmf.storage as etmf_storage
import pytest
from packages.storage import reset_storage_adapter
from packages.testing.fakes import InMemoryStoragePort


def test_etmf_storage_delegation():
    """Verify eTMF storage adapter delegates to shared factory without local global variables."""
    # Ensure no local global singleton variable exists
    assert not hasattr(etmf_storage, "_GLOBAL_STORAGE_ADAPTER")

    fake = InMemoryStoragePort()
    etmf_storage.set_global_storage_adapter(fake)

    assert etmf_storage.get_storage_adapter() is fake

    etmf_storage.reset_global_storage_adapter()
    reset_storage_adapter()


@pytest.mark.asyncio
async def test_etmf_get_document_bytes():
    """Verify dual-read fallback in get_document_bytes using active storage adapter."""
    fake = InMemoryStoragePort()
    etmf_storage.set_global_storage_adapter(fake)

    # 1. Object storage retrieval via object_key
    await fake.put_object("docs/test.pdf", b"pdf content")
    doc_object = SimpleNamespace(object_key="docs/test.pdf")
    content = await etmf_storage.get_document_bytes(doc_object)
    assert content == b"pdf content"

    # 2. Dual-read fallback: legacy base64 in-db content
    b64_content = base64.b64encode(b"legacy pdf content").decode("utf-8")
    doc_legacy_b64 = SimpleNamespace(
        object_key=None, content=b64_content, mime_type="application/pdf"
    )
    fallback_content = await etmf_storage.get_document_bytes(doc_legacy_b64)
    assert fallback_content == b"legacy pdf content"

    reset_storage_adapter()
