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
from typing import Any

import httpx

from packages.security.gateway_client import GatewayBaseClient

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
        self.gw_client = GatewayBaseClient(base_url=self.base_url, timeout=self.timeout)

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

        path = f"/api/v1/execution/biostat/sdtm/{dom_upper}"
        reason = f"Quality surveillance harvest of SDTM {dom_upper}"
        params = {"study_id": study_id}

        try:
            if client is not None:
                headers = self.gw_client.build_headers(
                    user_id="quality-surveillance-runner",
                    roles="quality_oversight",
                    change_reason=reason,
                )
                url = f"{self.base_url}{path}"
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                response = await self.gw_client.request(
                    method="GET",
                    path=path,
                    user_id="quality-surveillance-runner",
                    roles="quality_oversight",
                    change_reason=reason,
                    params=params,
                )

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
                path,
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

        path = "/api/v1/execution/audits"
        reason = "Quality surveillance audit trail harvest"
        params = {"study_id": study_id}

        try:
            if client is not None:
                headers = self.gw_client.build_headers(
                    user_id="quality-surveillance-runner",
                    roles="quality_oversight",
                    change_reason=reason,
                )
                url = f"{self.base_url}{path}"
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                response = await self.gw_client.request(
                    method="GET",
                    path=path,
                    user_id="quality-surveillance-runner",
                    roles="quality_oversight",
                    change_reason=reason,
                    params=params,
                )

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

        path = "/api/v1/execution/attachments/images"
        reason = "Quality surveillance image perceptual hash harvest"
        params = {"study_id": study_id}

        try:
            if client is not None:
                headers = self.gw_client.build_headers(
                    user_id="quality-surveillance-runner",
                    roles="quality_oversight",
                    change_reason=reason,
                )
                url = f"{self.base_url}{path}"
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                response = await self.gw_client.request(
                    method="GET",
                    path=path,
                    user_id="quality-surveillance-runner",
                    roles="quality_oversight",
                    change_reason=reason,
                    params=params,
                )

            if response.status_code == 200:
                data = response.json()
                return data if isinstance(data, list) else data.get("items", [])
            return []
        except Exception as e:
            logger.warning(
                "Failed to harvest image attachments for study %s: %s", study_id, e
            )
            return []
