import logging
import os
from collections.abc import Awaitable, Callable
from typing import Any

import httpx

from apps.execution.domain.acl.designer_eligibility_dto import (
    DesignerEligibilityCriterionDTO,
)
from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("execution-designer-client")


class DesignerCriteriaClientError(Exception):
    """Base exception for Designer Criteria client errors."""

    pass


_study_criteria_resolver: Callable[[str], Awaitable[list[Any]]] | None = None
_designer_client_instance: DesignerCriteriaClient | None = None


def register_study_criteria_resolver(
    resolver: Callable[[str], Awaitable[list[Any]]] | None,
) -> None:
    """Registers a pluggable resolver hook for retrieving study criteria (used for isolated tests)."""
    global _study_criteria_resolver
    _study_criteria_resolver = resolver


def set_designer_client(client: DesignerCriteriaClient | None) -> None:
    """Sets or overrides the default DesignerCriteriaClient instance."""
    global _designer_client_instance
    _designer_client_instance = client


def get_designer_client() -> DesignerCriteriaClient:
    """Retrieves the active DesignerCriteriaClient instance."""
    global _designer_client_instance
    if _designer_client_instance is None:
        _designer_client_instance = DesignerCriteriaClient()
    return _designer_client_instance


class DesignerCriteriaClient:
    """Asynchronous client to retrieve eligibility criteria from the Designer service."""

    def __init__(
        self,
        base_url: str | None = None,
        timeout: float = 10.0,
    ) -> None:
        self.base_url = (
            base_url or os.getenv("DESIGNER_URL") or "http://localhost:8001"
        ).rstrip("/")
        self.timeout = timeout
        self.gw_client = GatewayBaseClient(base_url=self.base_url, timeout=self.timeout)

    async def get_eligibility_criteria(
        self, study_id: str, client: httpx.AsyncClient | None = None
    ) -> list[DesignerEligibilityCriterionDTO]:
        """Fetch eligibility criteria from Designer service by study ID.

        Args:
            study_id (str): The unique study identifier.
            client (Optional[httpx.AsyncClient]): Optional shared HTTPX async client.

        Returns:
            List[DesignerEligibilityCriterionDTO]: List of deserialized eligibility criteria.
        """
        if _study_criteria_resolver is not None:
            res = await _study_criteria_resolver(study_id)
            return [
                item
                if isinstance(item, DesignerEligibilityCriterionDTO)
                else DesignerEligibilityCriterionDTO(**item)
                for item in res
            ]

        path = f"/api/v1/studies/{study_id}/eligibility-criteria"

        try:
            if client is not None:
                headers = self.gw_client.build_headers(
                    user_id="execution-service",
                    roles="system",
                    change_reason="",
                )
                url = f"{self.base_url}{path}"
                response = await client.get(url, headers=headers, timeout=self.timeout)
            else:
                response = await self.gw_client.request(
                    method="GET",
                    path=path,
                    user_id="execution-service",
                    roles="system",
                    change_reason="",
                )

            if response.status_code != 200:
                raise DesignerCriteriaClientError(
                    f"Designer service returned error status {response.status_code}: {response.text}"
                )

            data = response.json()
            criteria = []
            for item in data:
                if "created_by" not in item:
                    item["created_by"] = item.get("created_by") or "designer"
                if "reason_for_change" not in item:
                    item["reason_for_change"] = (
                        item.get("reason_for_change") or "Initial definition"
                    )

                criteria.append(DesignerEligibilityCriterionDTO(**item))

            return criteria

        except DesignerCriteriaClientError:
            raise
        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to Designer service for eligibility criteria: %s", e
            )
            raise DesignerCriteriaClientError(
                f"Failed to connect to Designer service: {str(e)}"
            ) from e
        except Exception as e:
            logger.error("Unexpected error in Designer criteria client: %s", e)
            raise DesignerCriteriaClientError(
                f"Error parsing eligibility criteria: {str(e)}"
            ) from e


async def fetch_study_criteria(
    study_id: str,
) -> list[DesignerEligibilityCriterionDTO]:
    """Fetch eligibility criteria module-function style."""
    client = get_designer_client()
    return await client.get_eligibility_criteria(study_id)


__all__ = [
    "DesignerCriteriaClient",
    "DesignerCriteriaClientError",
    "fetch_study_criteria",
    "get_designer_client",
    "register_study_criteria_resolver",
    "set_designer_client",
]
