import logging
import os
from collections.abc import Awaitable, Callable
from typing import Any

import httpx

from apps.interop.domain.acl import (
    EligibilityCriterionDTO,
    ExpressionNodeDTO,
    parse_dsl_dto,
)
from packages.security.gateway_client import GatewayBaseClient

EligibilityCriterion = EligibilityCriterionDTO
ExpressionNode = ExpressionNodeDTO
parse_dsl = parse_dsl_dto

logger = logging.getLogger("interop-designer-client")


class InteropDesignerClientError(Exception):
    """Domain exception raised when Interop Designer client operations fail."""

    pass


_eligibility_criteria_resolver: (
    Callable[[str], Awaitable[list[EligibilityCriterion]]] | None
) = None


def register_eligibility_criteria_resolver(
    resolver: Callable[[str], Awaitable[list[EligibilityCriterion]]] | None,
) -> None:
    """Registers an authoritative adapter resolver for fetching eligibility criteria."""
    global _eligibility_criteria_resolver
    _eligibility_criteria_resolver = resolver


def map_db_to_criterion(db_crit: dict[str, Any]) -> EligibilityCriterion:
    """Deserializes a database or API JSON dict into the shared EligibilityCriterion model."""
    reason = (
        db_crit.get("reason_for_change")
        or db_crit.get("change_reason")
        or "Initial setup"
    )
    created_by = db_crit.get("created_by") or "system"

    cond = db_crit.get("condition")
    if not cond and db_crit.get("dsl_source"):
        cond = parse_dsl(db_crit["dsl_source"])
    elif isinstance(cond, dict):
        cond = ExpressionNode(**cond)
    else:
        raise ValueError(
            "Criterion must provide a structured condition or a valid dsl_source."
        )

    return EligibilityCriterion(
        criterion_id=str(db_crit.get("criterion_id") or db_crit.get("id") or ""),
        criterion_type=db_crit["criterion_type"],
        description=db_crit["description"],
        dsl_source=db_crit["dsl_source"],
        condition=cond,
        expected_outcome=db_crit.get("expected_outcome", True),
        created_by=created_by,
        reason_for_change=reason,
        version_index=db_crit.get("version_index", 1),
    )


async def fetch_eligibility_criteria(study_id: str) -> list[EligibilityCriterion]:
    """Queries the central Designer service to fetch active eligibility criteria for a study."""
    if _eligibility_criteria_resolver is not None:
        try:
            return await _eligibility_criteria_resolver(study_id)
        except Exception as e:
            logger.error("Error in registered eligibility criteria resolver: %s", e)
            raise InteropDesignerClientError(
                f"Error resolving eligibility criteria: {e}"
            ) from e

    designer_url = os.getenv("DESIGNER_URL", "http://localhost:8001")
    path = f"/api/v1/studies/{study_id}/eligibility-criteria"

    gw_client = GatewayBaseClient(base_url=designer_url, timeout=5.0)

    try:
        response = await gw_client.request(
            method="GET",
            path=path,
            user_id="interop-service",
            roles="sponsor_dm",
            change_reason="",
        )

        if response.status_code == 200:
            raw_list = response.json()
            return [map_db_to_criterion(item) for item in raw_list]

        if response.status_code == 404:
            raise InteropDesignerClientError(
                f"Study {study_id} or eligibility criteria not found in Designer service."
            )

        raise InteropDesignerClientError(
            f"Failed to fetch eligibility criteria: Designer service returned {response.status_code}"
        )

    except InteropDesignerClientError:
        raise
    except httpx.RequestError as e:
        raise InteropDesignerClientError(
            f"Failed to connect to Designer service for eligibility criteria validation: {str(e)}"
        ) from e
    except Exception as e:
        raise InteropDesignerClientError(
            f"Unexpected error fetching eligibility criteria: {str(e)}"
        ) from e


__all__ = [
    "EligibilityCriterion",
    "ExpressionNode",
    "InteropDesignerClientError",
    "fetch_eligibility_criteria",
    "map_db_to_criterion",
    "parse_dsl",
    "register_eligibility_criteria_resolver",
]
