import logging
import os
import time

import httpx

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("econsent-etmf-client")


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
    """
    Sends a POST request to {ETMF_URL}/api/v1/etmf/ingest.
    Uses HMAC-SHA256 Gateway signature V2 for secure internal service authentication.
    Returns the ingested document ID on success, and raises on failure.
    """
    etmf_base_url = os.getenv("ETMF_URL", "http://localhost:8003").rstrip("/")
    url = f"{etmf_base_url}/api/v1/etmf/ingest"

    gateway_secret_env = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    gateway_secret = (
        gateway_secret_env.encode("utf-8")
        if isinstance(gateway_secret_env, str)
        else gateway_secret_env
    )

    user_id = "econsent-service"
    roles = "admin"
    change_reason = "eConsent ICF Archival Delivery"
    timestamp = str(time.time())

    # Generate gateway signature covering parameters
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=gateway_secret,
        change_reason=change_reason,
        site_id=site_id,
        tenant_id="tenant_default",
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }

    # Structure protocol_version model if present
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

    async with httpx.AsyncClient(timeout=5.0) as client:
        response = await client.post(url, json=payload, headers=headers)
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
