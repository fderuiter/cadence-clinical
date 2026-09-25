"""Storage integration and dual-read fallback resolver for eTMF documents.

Delegates storage adapter creation and global registry management to packages.storage.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import base64
from typing import Any

from packages.storage import (
    get_storage_adapter as _shared_get_storage_adapter,
    reset_storage_adapter as _shared_reset_storage_adapter,
    set_storage_adapter as _shared_set_storage_adapter,
)
from packages.storage.ports.storage_port import StoragePort


def get_storage_adapter(override: StoragePort[Any] | None = None) -> StoragePort[Any]:
    """Get or initialize the global storage adapter for eTMF binary storage (delegated to packages.storage).

    @req:PRD-DOC-001
    """
    return _shared_get_storage_adapter(override=override)


def set_global_storage_adapter(adapter: StoragePort[Any] | None) -> None:
    """Set or clear global storage adapter override (delegated to packages.storage)."""
    _shared_set_storage_adapter(adapter)


def reset_global_storage_adapter() -> None:
    """Reset global storage adapter singleton (delegated to packages.storage)."""
    _shared_reset_storage_adapter()


async def get_document_bytes(
    doc: Any, storage: StoragePort[Any] | None = None
) -> bytes:
    """Fetch document content bytes with zero-downtime dual-read fallback.

    1. If doc.object_key is populated, stream bytes from object storage.
    2. Otherwise, fall back to legacy in-database base64/string content.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    # 1. Primary: Object storage
    if getattr(doc, "object_key", None):
        adapter = get_storage_adapter(storage)
        content_bytes, _ = await adapter.get_object(doc.object_key)
        return content_bytes

    # 2. Dual-Read Fallback: Legacy database _content / content
    raw_val = getattr(doc, "_content", None) or getattr(doc, "content", None)
    if not raw_val:
        return b""

    if isinstance(raw_val, bytes):
        return raw_val

    mime_lower = (
        getattr(doc, "mime_type", "").lower().strip()
        if hasattr(doc, "mime_type")
        else ""
    )
    is_binary = (
        "pdf" in mime_lower
        or "wordprocessingml" in mime_lower
        or "docx" in mime_lower
        or mime_lower == "application/octet-stream"
    )

    if is_binary:
        try:
            return base64.b64decode(raw_val)
        except Exception:
            return raw_val.encode("utf-8", errors="surrogateescape")

    return raw_val.encode("utf-8", errors="surrogateescape")


__all__ = [
    "get_document_bytes",
    "get_storage_adapter",
    "reset_global_storage_adapter",
    "set_global_storage_adapter",
]
