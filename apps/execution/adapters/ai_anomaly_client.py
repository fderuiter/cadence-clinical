"""AI Gateway adapter for cross-domain semantic anomaly reasoning.

Requirements: PRD-QRY-008, PRD-SYS-051
"""

import hashlib
import json
import logging
import os
import time
from typing import Any

import httpx

from apps.execution.domain.anomaly import (
    AnomalySeverity,
    CrossDomainAnomaly,
    CrossDomainAnomalyType,
)
from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("execution-ai-anomaly-client")


class AIAnomalyGatewayClient:
    """HTTP client communicating with the centralized AI Gateway for cross-domain anomaly reasoning."""

    def __init__(self, base_url: str | None = None) -> None:
        raw_url = base_url or os.getenv("AI_GATEWAY_URL")
        self.base_url: str | None = raw_url.rstrip("/") if raw_url else None

    async def analyze_cross_domain_consistency(
        self,
        subject_id: str,
        study_id: str,
        events_summary: str,
        site_id: str | None = None,
        tenant_id: str = "tenant_default",
    ) -> list[CrossDomainAnomaly]:
        """Invokes AI Gateway Tier 2 model to detect semantic cross-domain discrepancies in subject records.

        Args:
            subject_id: The clinical subject identifier.
            study_id: The clinical study identifier.
            events_summary: Serialized text timeline of cross-domain observations.
            site_id: Optional clinical site identifier.
            tenant_id: Multi-tenant scope identifier.

        Returns:
            List of CrossDomainAnomaly objects with AI attribution metadata.
        """
        if not self.base_url:
            return []

        url = f"{self.base_url}/api/v1/ai/generate"
        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET", default="internal-gateway-secret-12345"
        )
        gateway_secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

        user_id = "execution-anomaly-service"
        roles = "Data Manager"
        change_reason = "Cross-Domain eCRF Anomaly Detection Evaluation"
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
            f"You are a clinical data monitoring expert reviewing clinical trial eCRF data.\n"
            f"Analyze the following cross-domain subject event timeline for subject {subject_id} (Study {study_id}) "
            f"and identify clinical discrepancies, inconsistencies, or contradictions across Adverse Events (AE), "
            f"Concomitant Medications (CM), Laboratory Diagnostics (LB), Vital Signs (VS), and Disposition/Exposure (DS/EX):\n\n"
            f'"""{events_summary}"""\n\n'
            f"Identify any contextual anomalies and return a JSON object with an 'anomalies' list."
        )

        prompt_hash = hashlib.sha256(prompt.encode("utf-8")).hexdigest()

        response_schema = {
            "type": "object",
            "properties": {
                "anomalies": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "anomaly_type": {"type": "string"},
                            "primary_domain": {"type": "string"},
                            "primary_test_code": {"type": "string"},
                            "correlated_domain": {"type": "string"},
                            "correlated_test_code": {"type": "string"},
                            "severity": {"type": "string"},
                            "message": {"type": "string"},
                            "explanation": {"type": "string"},
                            "confidence_score": {"type": "number"},
                        },
                        "required": [
                            "primary_domain",
                            "primary_test_code",
                            "correlated_domain",
                            "message",
                            "explanation",
                        ],
                    },
                }
            },
            "required": ["anomalies"],
        }

        payload = {
            "prompt": prompt,
            "tier": "tier_2_fast",
            "temperature": 0.0,
            "max_tokens": 1200,
            "response_schema": response_schema,
            "study_id": study_id,
            "tenant_id": tenant_id,
            "enable_deid": True,
            "compliance_profile": "HIPAA",
        }

        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.post(url, json=payload, headers=headers)
                if response.status_code == 200:
                    data = response.json()
                    model_id = data.get("model", "ai-gateway-tier-2")
                    structured = data.get("structured_data")
                    raw_items: list[dict[str, Any]] = []

                    if isinstance(structured, dict) and "anomalies" in structured:
                        raw_items = structured["anomalies"]
                    else:
                        content = data.get("content", "")
                        if content:
                            parsed = json.loads(content)
                            if isinstance(parsed, dict) and "anomalies" in parsed:
                                raw_items = parsed["anomalies"]

                    results: list[CrossDomainAnomaly] = []
                    for item in raw_items:
                        sev_str = str(item.get("severity", "MEDIUM")).upper()
                        severity = (
                            AnomalySeverity[sev_str]
                            if sev_str in AnomalySeverity.__members__
                            else AnomalySeverity.MEDIUM
                        )

                        results.append(
                            CrossDomainAnomaly(
                                anomaly_type=CrossDomainAnomalyType.AI_CONTEXTUAL_INCONSISTENCY,
                                study_id=study_id,
                                subject_id=subject_id,
                                site_id=site_id,
                                primary_domain=str(
                                    item.get("primary_domain", "AE")
                                ).upper(),
                                primary_test_code=str(
                                    item.get("primary_test_code", "UNKNOWN")
                                ),
                                correlated_domain=str(
                                    item.get("correlated_domain", "CM")
                                ).upper(),
                                correlated_test_code=item.get("correlated_test_code"),
                                severity=severity,
                                message=str(
                                    item.get(
                                        "message",
                                        "AI-detected cross-domain discrepancy",
                                    )
                                ),
                                explanation=str(item.get("explanation", "")),
                                confidence_score=float(
                                    item.get("confidence_score", 0.85)
                                ),
                                model_identifier=model_id,
                                prompt_hash=prompt_hash,
                            )
                        )
                    return results
                logger.warning(
                    "AI Gateway returned status code %s: %s",
                    response.status_code,
                    response.text,
                )
        except Exception as err:
            logger.warning("Failed to invoke AI Gateway for anomaly detection: %s", err)

        return []
