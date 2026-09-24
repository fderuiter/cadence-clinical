"""
HTTP client adapter for querying the Knowledge microservice from the Tickets service.

Uses HMAC-SHA256 Gateway signature V2 for secure internal service authentication.
Requirements: PRD-TCK-005, PRD-SYS-051
"""

from __future__ import annotations

import logging
import os
import time
from collections.abc import Callable
from typing import Any

import httpx

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("tickets-knowledge-client")

KNOWLEDGE_SERVICE_URL = os.environ.get("KNOWLEDGE_SERVICE_URL", "http://127.0.0.1:8006")
INTERNAL_SECRET = os.environ.get(
    "GATEWAY_INTERNAL_SECRET", "cadence-dev-secret-change-in-prod"
).encode("utf-8")

_IN_PROCESS_SEARCH_PROVIDER: Callable[..., Any] | None = None


def register_in_process_knowledge_provider(
    provider: Callable[..., Any] | None,
) -> None:
    """Registers an in-process protocol chunk search provider (used for test harnesses)."""
    global _IN_PROCESS_SEARCH_PROVIDER
    _IN_PROCESS_SEARCH_PROVIDER = provider


class KnowledgeServiceClient:
    """Async client for querying protocol chunks from Knowledge Hub."""

    @staticmethod
    async def search_protocol_chunks(
        query: str,
        study_id: str,
        protocol_version: str | None = None,
        only_approved: bool = True,
        top_k: int = 5,
    ) -> list[dict[str, Any]]:
        """Queries the Knowledge microservice for matching protocol chunks."""
        # 1. If an in-process search provider is registered (e.g. test harness), use it
        if _IN_PROCESS_SEARCH_PROVIDER is not None:
            return await _IN_PROCESS_SEARCH_PROVIDER(
                query=query,
                study_id=study_id,
                protocol_version=protocol_version,
                only_approved=only_approved,
                top_k=top_k,
            )

        # 2. Otherwise attempt direct REST endpoint call via HTTP client
        timestamp = str(time.time())
        user_id = "tickets-service"
        roles = "data_manager"

        sig = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=INTERNAL_SECRET,
            change_reason="Query protocol chunks for support ticket RAG triage",
        )

        headers = {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": sig,
            "X-Signature-Version": "2",
        }

        payload = {
            "query": query,
            "study_id": study_id,
            "protocol_version": protocol_version,
            "only_approved": only_approved,
            "top_k": top_k,
        }

        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                resp = await client.post(
                    f"{KNOWLEDGE_SERVICE_URL}/api/v1/knowledge/protocols/search",
                    json=payload,
                    headers=headers,
                )
                if resp.status_code == 200:
                    data = resp.json()
                    return data.get("matches", [])
        except Exception as exc:
            logger.debug(
                "Knowledge REST endpoint unavailable (%s). Returning empty match set.",
                exc,
            )

        return []


__all__ = [
    "KnowledgeServiceClient",
    "register_in_process_knowledge_provider",
]
