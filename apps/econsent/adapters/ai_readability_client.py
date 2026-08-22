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

    def __init__(self, base_url: str | None = None) -> None:
        self.base_url = (
            base_url or os.getenv("AI_GATEWAY_URL", "http://localhost:8000")
        ).rstrip("/")

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
        }

        prompt = (
            f"Analyze the following clinical consent clause text and identify medical jargon terms:\n\n"
            f'"""{text}"""\n\n'
            "Return a JSON object with a 'substitutions' list where each item has:\n"
            "- original_term: The complex medical or legal jargon phrase\n"
            "- suggested_term: The plain-language, patient-friendly alternative\n"
            "- rationale: Explanation of why this preserves meaning while improving comprehension\n"
            "- category: 'clinical_terminology', 'procedure', 'risk', or 'legal'\n"
            "- confidence_score: Confidence score between 0.0 and 1.0"
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

        async with httpx.AsyncClient(timeout=10.0) as client:
            try:
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
