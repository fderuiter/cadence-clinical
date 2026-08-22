"""Cross-service client interfacing with AI Gateway for Tier 2 jargon simplification."""

import json
import logging
import os
import time
from typing import Any

import httpx

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("econsent-ai-client")


class AIReadabilityGatewayClient:
    """HTTP client communicating with the centralized AI Gateway service for Tier 2 model execution."""

    def __init__(
        self,
        base_url: str | None = None,
        client: httpx.AsyncClient | None = None,
        timeout: float = 5.0,
    ) -> None:
        raw_url = base_url or os.getenv("AI_GATEWAY_URL") or "http://localhost:8000"
        self.base_url: str = raw_url.rstrip("/")
        self._external_client = client
        self._client: httpx.AsyncClient | None = client
        self._timeout = timeout
        self._limits = httpx.Limits(max_keepalive_connections=20, max_connections=50)

    async def _get_client(self) -> httpx.AsyncClient:
        """Retrieves or lazily instantiates the persistent pooled HTTP client."""
        if self._client is None or self._client.is_closed:
            self._client = httpx.AsyncClient(
                timeout=httpx.Timeout(self._timeout, connect=2.0),
                limits=self._limits,
            )
        return self._client

    async def close(self) -> None:
        """Closes the underlying HTTP client session if internally managed."""
        if self._client and not self._external_client and not self._client.is_closed:
            await self._client.aclose()

    async def generate_simplification_suggestions(
        self,
        text: str,
        target_grade_level: float = 8.0,
        study_id: str | None = None,
        tenant_id: str = "tenant_default",
    ) -> list[dict[str, Any]]:
        """Invokes AI Gateway Tier 2 model to extract clinical jargon and propose patient-friendly terms.

        Args:
            text: Informed consent clause or narrative text.
            target_grade_level: Target reading level (e.g. 6.0 - 8.0).
            study_id: Optional study identifier scope.
            tenant_id: Multi-tenant scope.

        Returns:
            List of suggested term substitutions conforming to structured schema.
        """
        url = f"{self.base_url}/api/v1/ai/generate"
        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET", default="internal-gateway-secret-12345"
        )
        gateway_secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

        user_id = "econsent-service"
        roles = "sponsor_designer"
        change_reason = "eConsent Readability Jargon Simplification"
        timestamp = str(time.time())

        signature = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=gateway_secret,
            change_reason=change_reason,
            tenant_id=tenant_id,
        )

        headers = {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": signature,
            "X-Signature-Version": "2",
            "X-Change-Reason": change_reason,
            "X-Tenant-Id": tenant_id,
        }

        prompt = (
            f"You are an expert clinical research readability and health literacy specialist.\n"
            f"Review the following clinical trial informed consent clause and identify complex medical "
            f"jargon or formal scientific expressions that exceed a Grade {target_grade_level} reading level.\n"
            f"Propose patient-friendly, plain-language replacements while strictly preserving the clinical, "
            f"ethical, and legal meaning.\n\n"
            f"Target Reading Level: Grade {target_grade_level}\n\n"
            f"Clinical Clause Text:\n'''{text}'''\n"
        )

        response_schema = {
            "type": "object",
            "properties": {
                "substitutions": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "original_term": {"type": "string"},
                            "suggested_term": {"type": "string"},
                            "rationale": {"type": "string"},
                            "category": {"type": "string"},
                            "confidence_score": {"type": "number"},
                        },
                        "required": [
                            "original_term",
                            "suggested_term",
                            "rationale",
                        ],
                    },
                }
            },
            "required": ["substitutions"],
        }

        payload = {
            "prompt": prompt,
            "tier": "tier_2_fast",
            "temperature": 0.1,
            "max_tokens": 1000,
            "response_schema": response_schema,
            "study_id": study_id,
            "tenant_id": tenant_id,
            "enable_deid": True,
            "compliance_profile": "HIPAA",
        }

        try:
            client = await self._get_client()
            response = await client.post(url, json=payload, headers=headers)
            if response.status_code == 200:
                data = response.json()
                structured = data.get("structured_data")
                if isinstance(structured, dict) and "substitutions" in structured:
                    return structured["substitutions"]
                # Fallback to parsing raw text content if structured_data is empty
                content = data.get("content", "")
                if content:
                    parsed = json.loads(content)
                    if isinstance(parsed, dict) and "substitutions" in parsed:
                        return parsed["substitutions"]
            else:
                logger.warning(
                    "AI Gateway returned status code %s: %s",
                    response.status_code,
                    response.text,
                )
        except Exception as err:
            logger.warning("Failed to invoke AI Gateway for readability: %s", err)

        return []
