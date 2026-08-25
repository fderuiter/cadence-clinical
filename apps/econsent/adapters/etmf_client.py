import logging
import os
from collections.abc import Awaitable, Callable
from typing import Any

import httpx

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("econsent-etmf-client")

_icf_forward_resolver: Callable[[dict[str, Any]], Awaitable[str]] | None = None


def register_icf_forward_resolver(
    resolver: Callable[[dict[str, Any]], Awaitable[str]] | None,
) -> None:
    """Registers a pluggable resolver hook for forwarding ICF documents to eTMF."""
    global _icf_forward_resolver
    _icf_forward_resolver = resolver


async def forward_icf_to_etmf(
    study_id: str,
    site_id: str | None,
    filename: str,
    content: str,
    mime_type: str,
    protocol_version: str,
    metadata_json: dict | None,
    idempotency_key: str,
) -> str:
    """Sends a POST request to {ETMF_URL}/api/v1/etmf/ingest.

    Uses HMAC-SHA256 Gateway signature V2 via GatewayBaseClient. Returns the
    ingested document ID on success, and raises on failure.
    """
    protocol_version_payload = None
    if protocol_version:
        protocol_version_payload = {
            "study_id": study_id,
            "version_tag": protocol_version,
            "version_index": 1,
            "status": "PUBLISHED",
        }

    payload = {
        "study_id": study_id,
        "site_id": site_id,
        "artifact_type": "Informed Consent Form",
        "filename": filename,
        "content": content,
        "mime_type": mime_type,
        "protocol_version": protocol_version_payload,
        "metadata_json": metadata_json,
        "idempotency_key": idempotency_key,
    }

    if _icf_forward_resolver is not None:
        return await _icf_forward_resolver(payload)

    etmf_base_url = os.getenv("ETMF_URL", "http://localhost:8003").rstrip("/")
    gw_client = GatewayBaseClient(base_url=etmf_base_url, timeout=5.0)

    response = await gw_client.request(
        method="POST",
        path="/api/v1/etmf/ingest",
        user_id="econsent-service",
        roles="admin",
        change_reason="eConsent ICF Archival Delivery",
        json=payload,
    )

    if response.status_code not in (200, 201):
        logger.error(
            "Failed to forward ICF to eTMF, status code: %s, response: %s",
            response.status_code,
            response.text,
        )
        raise httpx.HTTPStatusError(
            f"Unexpected status code {response.status_code}",
            request=response.request,
            response=response,
        )

    data = response.json()
    doc_id = data.get("document_id") or data.get("id")
    if not doc_id:
        raise ValueError("No document ID returned from eTMF service")
    return doc_id


__all__ = [
    "forward_icf_to_etmf",
    "register_icf_forward_resolver",
]
