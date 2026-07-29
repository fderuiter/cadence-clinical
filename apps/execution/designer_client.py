import logging
import os
import time
from typing import Any, Dict, List, Optional

import httpx
from fastapi import HTTPException

from eligibility.models import EligibilityCriterion
from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("execution-designer-client")


class DesignerCriteriaClientError(Exception):
    """Base exception for Designer Criteria client errors."""
    pass


class DesignerCriteriaClient:
    """Asynchronous client to retrieve eligibility criteria from the Designer service."""

    def __init__(
        self,
        base_url: Optional[str] = None,
        timeout: float = 10.0,
    ) -> None:
        self.base_url = (
            base_url
            or os.getenv("DESIGNER_URL")
            or "http://localhost:8001"
        ).rstrip("/")
        self.timeout = timeout

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
        gateway_secret_env = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
        gateway_secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

        user_id = "execution-service"
        roles = "system"
        timestamp = str(time.time())

        # Generate signature
        signature = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=gateway_secret,
            change_reason="",
        )

        headers = {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": signature,
            "X-Signature-Version": "2",
            "X-Change-Reason": "",
        }

        url = f"{self.base_url}/api/v1/studies/{study_id}/eligibility-criteria"

        try:
            if client is not None:
                response = await client.get(url, headers=headers, timeout=self.timeout)
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, headers=headers)

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
                    item["reason_for_change"] = item.get("reason_for_change") or "Initial definition"

                # EligibilityCriterion can be constructed via standard pydantic parse
                criteria.append(EligibilityCriterion(**item))

            return criteria

        except httpx.RequestError as e:
            logger.error("Failed to connect to Designer service for eligibility criteria: %s", e)
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
