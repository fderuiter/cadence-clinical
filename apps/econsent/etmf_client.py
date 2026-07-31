import logging
import os
import time
from typing import Optional

import httpx

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("econsent-etmf-client")


class ETMFClient(GatewayBaseClient):
    """
    Client for interacting with the eTMF service, inheriting from GatewayBaseClient.
    """

    def __init__(self, base_url: Optional[str] = None, timeout: float = 5.0) -> None:
        url = (
            base_url or os.getenv("ETMF_URL") or "http://localhost:8003"
        ).rstrip("/")
        super().__init__(base_url=url, timeout=timeout)

    async def ingest_document(
        self,
        study_id: str,
        site_id: str | None,
        filename: str,
        content: str,
        mime_type: str,
        protocol_version: str,
        metadata_json: dict | None,
        idempotency_key: str,
        client: Optional[httpx.AsyncClient] = None,
    ) -> str:
        """
        Sends a POST request to {ETMF_URL}/api/v1/etmf/ingest.
        Returns the ingested document ID on success, and raises on failure.
        """
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

        response = await self.request(
            method="POST",
            path="/api/v1/etmf/ingest",
            user_id="econsent-service",
            roles="admin",
            change_reason="eConsent ICF Archival Delivery",
            site_id=site_id,
            tenant_id="tenant_default",
            json=payload,
            client=client,
            timeout=self.timeout,
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
    client = ETMFClient()
    return await client.ingest_document(
        study_id=study_id,
        site_id=site_id,
        filename=filename,
        content=content,
        mime_type=mime_type,
        protocol_version=protocol_version,
        metadata_json=metadata_json,
        idempotency_key=idempotency_key,
    )
