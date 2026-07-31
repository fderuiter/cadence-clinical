import logging
import os
import time
from typing import Any, Dict, List, Optional

import httpx
from fastapi import HTTPException

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("safety-execution-client")

# For local in-process testing / short-circuit overrides
mock_ae_records: Optional[Dict[str, Any]] = None
mock_meddra_resolution: Optional[Dict[str, Any]] = None


class ExecutionClient(GatewayBaseClient):
    """
    Asynchronous client to retrieve AE Dataset-JSON records and resolve MedDRA codes
    from the central clinical execution service.
    """

    def __init__(
        self,
        base_url: Optional[str] = None,
        timeout: float = 10.0,
    ) -> None:
        url = (
            base_url or os.getenv("EXECUTION_URL") or "http://localhost:8002"
        ).rstrip("/")
        super().__init__(base_url=url, timeout=timeout)

    async def fetch_ae_data(
        self, study_id: str, client: Optional[httpx.AsyncClient] = None
    ) -> Dict[str, List[Dict[str, Any]]]:
        """
        Fetch AE Dataset-JSON records (both IG.AE and IG.SUPPAE) from EDC execution service.
        Returns parsed itemGroupData maps for downstream mapping.
        """
        global mock_ae_records
        if mock_ae_records is not None:
            return mock_ae_records

        url = "/api/v1/execution/biostat/sdtm/AE"
        params = {"study_id": study_id}

        try:
            # We call self.request to use the centralized GatewayBaseClient request logic
            response = await self.request(
                method="GET",
                path=url,
                user_id="safety-service",
                roles="sponsor_statistician",
                change_reason="",
                params=params,
                timeout=self.timeout,
                client=client,
            )

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

            # Parse CDISC Dataset-JSON structure back to standard list of dicts.
            # Dataset-JSON format nested under clinicalData -> itemGroupData -> IG.AE & IG.SUPPAE
            clinical_data = data.get("clinicalData", {})
            item_groups = clinical_data.get("itemGroupData", {})

            result: Dict[str, List[Dict[str, Any]]] = {"AE": [], "SUPPAE": []}

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
        client: Optional[httpx.AsyncClient] = None,
    ) -> Dict[str, Any]:
        """
        Resolve a verbatim Adverse Event term using execution service's medical coding dictionaries endpoint.
        Returns the parsed MedDRACodingResult hierarchy.
        """
        global mock_meddra_resolution
        if mock_meddra_resolution is not None:
            return mock_meddra_resolution

        url = "/api/v1/dictionaries/meddra/code"
        params = {"term": term, "version": version, "target_level": target_level}

        try:
            # We call self.request to use the centralized GatewayBaseClient request logic
            response = await self.request(
                method="GET",
                path=url,
                user_id="safety-service",
                roles="sponsor_statistician",
                change_reason="",
                params=params,
                timeout=self.timeout,
                client=client,
            )

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
