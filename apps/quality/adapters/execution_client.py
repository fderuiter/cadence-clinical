"""Execution Microservice Client for Centralized Statistical Monitoring & Quality Surveillance.

Requirements:
- PRD-CSM-001: Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis
- PRD-CSM-002: Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection
- PRD-CSM-003: Calendar & Timestamp Pattern Clustering
- PRD-CSM-004: Duplicate Patient Record & Anatomical Fabrication Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2213: Autonomous Cross-Domain Quality Surveillance Runner Architecture
"""

import logging
import os
import time
from typing import Any

import httpx

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("quality-execution-client")

# Global mock overrides for unit and isolated integration tests
mock_sdtm_domain_records: dict[str, dict[str, list[dict[str, Any]]]] = {}
mock_audit_events_records: dict[str, list[dict[str, Any]]] = {}
mock_image_attachments_records: dict[str, list[dict[str, Any]]] = {}


class QualityExecutionClient:
    """Asynchronous client to harvest clinical observation data, SDTM datasets,

    audit event streams, and medical image attachments from the central
    execution microservice via HMAC-authenticated REST endpoints.
    """

    def __init__(
        self,
        base_url: str | None = None,
        timeout: float = 10.0,
    ) -> None:
        self.base_url = (
            base_url or os.getenv("EXECUTION_URL") or "http://localhost:8002"
        ).rstrip("/")
        self.timeout = timeout

    def _get_auth_headers(self, change_reason: str = "") -> dict[str, str]:
        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        )
        gateway_secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

        user_id = "quality-surveillance-runner"
        roles = "quality_oversight"
        timestamp = str(time.time())

        signature = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=gateway_secret,
            change_reason=change_reason,
        )

        return {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": signature,
            "X-Signature-Version": "2",
            "X-Change-Reason": change_reason,
        }

    async def fetch_sdtm_domain(
        self,
        study_id: str,
        domain: str,
        client: httpx.AsyncClient | None = None,
    ) -> dict[str, list[dict[str, Any]]]:
        """Harvests Dataset-JSON records for a given SDTM domain (e.g. DM, VS, LB, EG, MH)."""
        dom_upper = domain.strip().upper()
        if (
            study_id in mock_sdtm_domain_records
            and dom_upper in mock_sdtm_domain_records[study_id]
        ):
            return mock_sdtm_domain_records[study_id][dom_upper]

        url = f"{self.base_url}/api/v1/execution/biostat/sdtm/{dom_upper}"
        headers = self._get_auth_headers(
            change_reason=f"Quality surveillance harvest of SDTM {dom_upper}"
        )
        params = {"study_id": study_id}

        try:
            if client is not None:
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, headers=headers, params=params)

            if response.status_code != 200:
                logger.warning(
                    "Execution service returned status %d for domain %s: %s",
                    response.status_code,
                    dom_upper,
                    response.text,
                )
                return {dom_upper: [], f"SUPP{dom_upper}": []}

            data = response.json()
            clinical_data = data.get("clinicalData", {})
            item_groups = clinical_data.get("itemGroupData", {})

            result: dict[str, list[dict[str, Any]]] = {
                dom_upper: [],
                f"SUPP{dom_upper}": [],
            }

            for ig_key, ig_data in item_groups.items():
                domain_key = ig_key.replace("IG.", "")
                if domain_key not in (dom_upper, f"SUPP{dom_upper}"):
                    continue

                variables = [item["name"] for item in ig_data.get("items", [])]
                rows = []
                for row_data in ig_data.get("itemData", []):
                    rows.append(dict(zip(variables, row_data)))
                result[domain_key] = rows

            return result

        except Exception as e:
            logger.warning(
                "Error harvesting SDTM %s from execution service (%s): %s",
                dom_upper,
                url,
                e,
            )
            return {dom_upper: [], f"SUPP{dom_upper}": []}

    async def fetch_audit_events(
        self,
        study_id: str,
        client: httpx.AsyncClient | None = None,
    ) -> list[dict[str, Any]]:
        """Harvests immutable execution audit trail events for temporal clustering."""
        if study_id in mock_audit_events_records:
            return mock_audit_events_records[study_id]

        url = f"{self.base_url}/api/v1/execution/audits"
        headers = self._get_auth_headers(
            change_reason="Quality surveillance audit trail harvest"
        )
        params = {"study_id": study_id}

        try:
            if client is not None:
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, headers=headers, params=params)

            if response.status_code == 200:
                data = response.json()
                return data if isinstance(data, list) else data.get("items", [])
            return []
        except Exception as e:
            logger.warning(
                "Failed to harvest audit events for study %s: %s", study_id, e
            )
            return []

    async def fetch_image_attachments(
        self,
        study_id: str,
        client: httpx.AsyncClient | None = None,
    ) -> list[dict[str, Any]]:
        """Harvests clinical document and imaging attachments for pHash verification."""
        if study_id in mock_image_attachments_records:
            return mock_image_attachments_records[study_id]

        url = f"{self.base_url}/api/v1/execution/attachments/images"
        headers = self._get_auth_headers(
            change_reason="Quality surveillance image perceptual hash harvest"
        )
        params = {"study_id": study_id}

        try:
            if client is not None:
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, headers=headers, params=params)

            if response.status_code == 200:
                data = response.json()
                return data if isinstance(data, list) else data.get("items", [])
            return []
        except Exception as e:
            logger.warning(
                "Failed to harvest image attachments for study %s: %s", study_id, e
            )
            return []
