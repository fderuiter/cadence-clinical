import datetime
import logging
import os
from typing import Any

import httpx
from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl
from fastapi import HTTPException

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("packages.security.designer_client")


class DesignerCriteriaClientError(Exception):
    """Base exception for Designer Criteria client errors."""

    pass


def map_db_to_criterion(db_crit: dict[str, Any]) -> EligibilityCriterion:
    """
    Deserializes a database or API JSON dict into the shared EligibilityCriterion model.
    Rehydrates the nested AST condition, or re-parses dsl_source if absent.
    """
    reason = (
        db_crit.get("reason_for_change")
        or db_crit.get("change_reason")
        or "Initial setup"
    )
    created_by = db_crit.get("created_by") or "system"

    cond = db_crit.get("condition")
    if not cond and db_crit.get("dsl_source"):
        cond = parse_dsl(db_crit["dsl_source"])
    elif (
        isinstance(cond, dict)
        or cond is not None
        and not isinstance(cond, ExpressionNode)
    ):
        cond = ExpressionNode(**cond)
    else:
        # Fallback if both condition and dsl_source are somehow empty (should not happen for valid criteria)
        raise ValueError(
            "Criterion must provide a structured condition or a valid dsl_source."
        )

    created_at = db_crit.get("created_at")
    if not created_at:
        created_at = datetime.datetime.now(datetime.UTC)
    elif isinstance(created_at, str):
        try:
            created_at = datetime.datetime.fromisoformat(
                created_at.replace("Z", "+00:00")
            )
        except Exception:
            created_at = datetime.datetime.now(datetime.UTC)
    else:
        try:
            if hasattr(created_at, "isoformat"):
                created_at = datetime.datetime.fromisoformat(
                    created_at.isoformat().replace("Z", "+00:00")
                )
            else:
                created_at = datetime.datetime.now(datetime.UTC)
        except Exception:
            created_at = datetime.datetime.now(datetime.UTC)

    return EligibilityCriterion(
        criterion_id=db_crit.get("criterion_id") or db_crit.get("id"),
        criterion_type=db_crit["criterion_type"],
        description=db_crit["description"],
        dsl_source=db_crit["dsl_source"],
        condition=cond,
        expected_outcome=db_crit.get("expected_outcome", True),
        created_by=created_by,
        reason_for_change=reason,
        version_index=db_crit.get("version_index", 1),
        created_at=created_at,
    )


class DesignerCriteriaClient(GatewayBaseClient):
    """
    Consolidated client to retrieve eligibility criteria from the Designer service.
    Subclasses GatewayBaseClient for secure gateways and signature handling.
    """

    def __init__(self, base_url: str | None = None, timeout: float = 10.0) -> None:
        url = (base_url or os.getenv("DESIGNER_URL") or "http://localhost:8001").rstrip(
            "/"
        )
        super().__init__(base_url=url, timeout=timeout)

    async def get_eligibility_criteria(
        self,
        study_id: str,
        user_id: str = "execution-service",
        roles: str = "system",
        change_reason: str = "",
        client: httpx.AsyncClient | None = None,
    ) -> list[EligibilityCriterion]:
        """
        Fetch eligibility criteria from Designer service by study ID.
        """
        path = f"/api/v1/studies/{study_id}/eligibility-criteria"
        try:
            response = await self.request(
                method="GET",
                path=path,
                user_id=user_id,
                roles=roles,
                change_reason=change_reason,
                client=client,
            )
            if response.status_code == 200:
                data = response.json()
                return [map_db_to_criterion(item) for item in data]
            if response.status_code == 404:
                raise HTTPException(
                    status_code=404,
                    detail=f"Study {study_id} or eligibility criteria not found in Designer service.",
                )
            raise HTTPException(
                status_code=502,
                detail=f"Designer service returned error status {response.status_code}: {response.text}",
            )
        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to Designer service for eligibility criteria: %s",
                e,
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
async def fetch_study_criteria(study_id: str) -> list[EligibilityCriterion]:
    """Fetch eligibility criteria module-function style."""
    client = DesignerCriteriaClient()
    return await client.get_eligibility_criteria(study_id)


async def fetch_eligibility_criteria(study_id: str) -> list[EligibilityCriterion]:
    """Fetch eligibility criteria module-function style (alternative name for interop-service)."""
    # Use roles="sponsor_dm" and user_id="interop-service" matching original interop_client
    client = DesignerCriteriaClient()
    return await client.get_eligibility_criteria(
        study_id, user_id="interop-service", roles="sponsor_dm"
    )
