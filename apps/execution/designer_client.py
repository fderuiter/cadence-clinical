import logging
import os
import time
from typing import List, Optional

import httpx
from eligibility.models import EligibilityCriterion
from fastapi import HTTPException

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("execution-designer-client")


class DesignerCriteriaClientError(Exception):
    """Base exception for Designer Criteria client errors."""

    pass


class DesignerCriteriaClient(GatewayBaseClient):
    """Asynchronous client to retrieve eligibility criteria from the Designer service."""

    def __init__(
        self,
        base_url: Optional[str] = None,
        timeout: float = 10.0,
    ) -> None:
        url = (
            base_url or os.getenv("DESIGNER_URL") or "http://localhost:8001"
        ).rstrip("/")
        super().__init__(base_url=url, timeout=timeout)

    async def get_eligibility_criteria(
        self, study_id: str, client: Optional[httpx.AsyncClient] = None
    ) -> List[EligibilityCriterion]:
        """Fetch eligibility criteria from Designer service by study ID.

        Args:
            study_id (str): The unique study identifier.
            client (Optional[httpx.AsyncClient]): Optional shared HTTPX async client.

        Returns:
            List[EligibilityCriterion]: List of deserialized eligibility criteria.
        """
        path = f"/api/v1/studies/{study_id}/eligibility-criteria"

        try:
            # We call self.request to use the centralized GatewayBaseClient request logic
            response = await self.request(
                method="GET",
                path=path,
                user_id="execution-service",
                roles="system",
                change_reason="",
                timeout=self.timeout,
            )

            if response.status_code != 200:
                raise HTTPException(
                    status_code=502,
                    detail=f"Designer service returned error status {response.status_code}: {response.text}",
                )

            data = response.json()
            criteria = []
            for item in data:
                # We need to construct EligibilityCriterion from the dict.
                # In eligibility/models.py, EligibilityCriterion subclasses AuditFields.
                # GxP fields (created_by, reason_for_change) might need default mapping if absent.
                if "created_by" not in item:
                    item["created_by"] = item.get("created_by") or "designer"
                if "reason_for_change" not in item:
                    item["reason_for_change"] = (
                        item.get("reason_for_change") or "Initial definition"
                    )

                # EligibilityCriterion can be constructed via standard pydantic parse
                criteria.append(EligibilityCriterion(**item))

            return criteria

        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to Designer service for eligibility criteria: %s", e
            )
            raise HTTPException(
                status_code=502,
                detail=f"Failed to connect to Designer service: {str(e)}",
            )
        except Exception as e:
            if isinstance(e, HTTPException):
                raise e
            logger.error("Unexpected error in Designer criteria client: %s", e)
            raise HTTPException(
                status_code=502,
                detail=f"Error parsing eligibility criteria: {str(e)}",
            )


# Module level helper function style modeling etmf/lock_client.py
async def fetch_study_criteria(study_id: str) -> List[EligibilityCriterion]:
    """Fetch eligibility criteria module-function style."""
    client = DesignerCriteriaClient()
    return await client.get_eligibility_criteria(study_id)
