import logging
import os
from collections.abc import Awaitable, Callable
from typing import Any

import httpx

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("safety-execution-client")

mock_ae_records: dict[str, Any] | None = None
mock_meddra_resolution: dict[str, Any] | None = None


class SafetyExecutionClientError(Exception):
    """Domain exception raised when Safety execution client operations fail."""

    pass


_sdtm_domain_resolver: (
    Callable[[str, str], Awaitable[dict[str, list[dict[str, Any]]]]] | None
) = None
_meddra_code_resolver: (
    Callable[[str, str, str], Awaitable[dict[str, Any]]] | None
) = None
_execution_client_instance: "ExecutionClient | None" = None


def register_sdtm_domain_resolver(
    resolver: Callable[[str, str], Awaitable[dict[str, list[dict[str, Any]]]]] | None,
) -> None:
    """Registers a pluggable resolver hook for fetching SDTM domain data."""
    global _sdtm_domain_resolver
    _sdtm_domain_resolver = resolver


def register_meddra_code_resolver(
    resolver: Callable[[str, str, str], Awaitable[dict[str, Any]]] | None,
) -> None:
    """Registers a pluggable resolver hook for resolving MedDRA codes."""
    global _meddra_code_resolver
    _meddra_code_resolver = resolver


def set_execution_client(client: "ExecutionClient | None") -> None:
    """Sets or overrides the default Safety ExecutionClient instance."""
    global _execution_client_instance
    _execution_client_instance = client


def get_execution_client() -> "ExecutionClient":
    """Retrieves the active Safety ExecutionClient instance."""
    global _execution_client_instance
    if _execution_client_instance is None:
        _execution_client_instance = ExecutionClient()
    return _execution_client_instance


class ExecutionClient:
    """Asynchronous client to retrieve AE Dataset-JSON records and resolve MedDRA codes

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
        self.gw_client = GatewayBaseClient(base_url=self.base_url, timeout=self.timeout)

    async def fetch_sdtm_domain(
        self,
        study_id: str,
        domain: str,
        client: httpx.AsyncClient | None = None,
    ) -> dict[str, list[dict[str, Any]]]:
        """Fetch raw SDTM domain Dataset-JSON records (e.g. DM, MH, CM, AE, LB, VS)."""
        dom_upper = domain.strip().upper()

        if _sdtm_domain_resolver is not None:
            return await _sdtm_domain_resolver(study_id, dom_upper)

        if dom_upper == "AE":
            global mock_ae_records
            if mock_ae_records is not None:
                return mock_ae_records

        path = f"/api/v1/execution/biostat/sdtm/{dom_upper}"
        params = {"study_id": study_id}

        try:
            if client is not None:
                headers = self.gw_client.build_headers(
                    user_id="safety-service",
                    roles="sponsor_statistician",
                    change_reason="",
                )
                url = f"{self.base_url}{path}"
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                response = await self.gw_client.request(
                    method="GET",
                    path=path,
                    user_id="safety-service",
                    roles="sponsor_statistician",
                    change_reason="",
                    params=params,
                )

            if response.status_code != 200:
                logger.error(
                    "Execution service returned error %d for domain %s: %s",
                    response.status_code,
                    dom_upper,
                    response.text,
                )
                raise SafetyExecutionClientError(
                    f"Execution service returned error status {response.status_code} for domain {dom_upper}: {response.text}"
                )

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

        except SafetyExecutionClientError:
            raise
        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to clinical execution service for %s data: %s",
                dom_upper,
                e,
            )
            raise SafetyExecutionClientError(
                f"Failed to connect to execution service for domain {dom_upper}: {str(e)}"
            ) from e
        except Exception as e:
            logger.error(
                "Unexpected error fetching %s data from execution service: %s",
                dom_upper,
                e,
            )
            raise SafetyExecutionClientError(
                f"Unexpected error fetching {dom_upper} data: {str(e)}"
            ) from e

    async def fetch_ae_data(
        self, study_id: str, client: httpx.AsyncClient | None = None
    ) -> dict[str, list[dict[str, Any]]]:
        return await self.fetch_sdtm_domain(
            study_id=study_id, domain="AE", client=client
        )

    async def resolve_meddra_code(
        self,
        term: str,
        version: str = "26.0",
        target_level: str = "LLT",
        client: httpx.AsyncClient | None = None,
    ) -> dict[str, Any]:
        if _meddra_code_resolver is not None:
            return await _meddra_code_resolver(term, version, target_level)

        global mock_meddra_resolution
        if mock_meddra_resolution is not None:
            return mock_meddra_resolution

        path = "/api/v1/dictionaries/meddra/code"
        params = {"term": term, "version": version, "target_level": target_level}

        try:
            if client is not None:
                headers = self.gw_client.build_headers(
                    user_id="safety-service",
                    roles="sponsor_statistician",
                    change_reason="",
                )
                url = f"{self.base_url}{path}"
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                response = await self.gw_client.request(
                    method="GET",
                    path=path,
                    user_id="safety-service",
                    roles="sponsor_statistician",
                    change_reason="",
                    params=params,
                )

            if response.status_code != 200:
                logger.error(
                    "Execution service dictionary endpoint returned error %d: %s",
                    response.status_code,
                    response.text,
                )
                raise SafetyExecutionClientError(
                    f"Execution service dictionary endpoint returned error status {response.status_code}: {response.text}"
                )

            return response.json()

        except SafetyExecutionClientError:
            raise
        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to clinical execution service for MedDRA resolution: %s",
                e,
            )
            raise SafetyExecutionClientError(
                f"Failed to connect to execution service dictionaries: {str(e)}"
            ) from e
        except Exception as e:
            logger.error(
                "Unexpected error resolving MedDRA term in execution service: %s", e
            )
            raise SafetyExecutionClientError(
                f"Unexpected error resolving MedDRA term: {str(e)}"
            ) from e


__all__ = [
    "ExecutionClient",
    "SafetyExecutionClientError",
    "get_execution_client",
    "mock_ae_records",
    "mock_meddra_resolution",
    "register_meddra_code_resolver",
    "register_sdtm_domain_resolver",
    "set_execution_client",
]
