import logging
import os
import time
from typing import Any

import httpx
from fastapi import HTTPException

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("safety-execution-client")

mock_ae_records: dict[str, Any] | None = None
mock_meddra_resolution: dict[str, Any] | None = None


class ExecutionClient:
    """
    Asynchronous client to retrieve AE Dataset-JSON records and resolve MedDRA codes
    from the central clinical execution service.
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

        user_id = "safety-service"
        roles = "sponsor_statistician"
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

    async def fetch_ae_data(
        self, study_id: str, client: httpx.AsyncClient | None = None
    ) -> dict[str, list[dict[str, Any]]]:
        global mock_ae_records
        if mock_ae_records is not None:
            return mock_ae_records

        url = f"{self.base_url}/api/v1/execution/biostat/sdtm/AE"
        headers = self._get_auth_headers()
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
                logger.error(
                    "Execution service returned error %d: %s",
                    response.status_code,
                    response.text,
                )
                raise HTTPException(
                    status_code=502,
                    detail=f"Execution service returned error status {response.status_code}: {response.text}",
                )

            data = response.json()

            clinical_data = data.get("clinicalData", {})
            item_groups = clinical_data.get("itemGroupData", {})

            result: dict[str, list[dict[str, Any]]] = {"AE": [], "SUPPAE": []}

            for ig_key, ig_data in item_groups.items():
                domain_key = ig_key.replace("IG.", "")
                if domain_key not in ("AE", "SUPPAE"):
                    continue

                variables = [item["name"] for item in ig_data.get("items", [])]
                rows = []
                for row_data in ig_data.get("itemData", []):
                    rows.append(dict(zip(variables, row_data)))
                result[domain_key] = rows

            return result

        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to clinical execution service for AE data: %s", e
            )
            raise HTTPException(
                status_code=502,
                detail=f"Failed to connect to execution service: {str(e)}",
            )
        except Exception as e:
            if isinstance(e, HTTPException):
                raise e
            logger.error(
                "Unexpected error fetching AE data from execution service: %s", e
            )
            raise HTTPException(
                status_code=502,
                detail=f"Unexpected error fetching AE data: {str(e)}",
            )

    async def resolve_meddra_code(
        self,
        term: str,
        version: str = "26.0",
        target_level: str = "LLT",
        client: httpx.AsyncClient | None = None,
    ) -> dict[str, Any]:
        global mock_meddra_resolution
        if mock_meddra_resolution is not None:
            return mock_meddra_resolution

        url = f"{self.base_url}/api/v1/dictionaries/meddra/code"
        headers = self._get_auth_headers()
        params = {"term": term, "version": version, "target_level": target_level}

        try:
            if client is not None:
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, headers=headers, params=params)

            if response.status_code != 200:
                logger.error(
                    "Execution service dictionary endpoint returned error %d: %s",
                    response.status_code,
                    response.text,
                )
                raise HTTPException(
                    status_code=502,
                    detail=f"Execution service dictionary endpoint returned error status {response.status_code}: {response.text}",
                )

            return response.json()

        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to clinical execution service for MedDRA resolution: %s",
                e,
            )
            raise HTTPException(
                status_code=502,
                detail=f"Failed to connect to execution service dictionaries: {str(e)}",
            )
        except Exception as e:
            if isinstance(e, HTTPException):
                raise e
            logger.error(
                "Unexpected error resolving MedDRA term in execution service: %s", e
            )
            raise HTTPException(
                status_code=502,
                detail=f"Unexpected error resolving MedDRA term: {str(e)}",
            )
