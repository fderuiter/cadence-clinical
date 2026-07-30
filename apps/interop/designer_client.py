import logging
import os
import time
from typing import Any, Dict, List

import httpx
from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl
from fastapi import HTTPException

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("interop-designer-client")


def map_db_to_criterion(db_crit: Dict[str, Any]) -> EligibilityCriterion:
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
    elif isinstance(cond, dict):
        cond = ExpressionNode(**cond)
    else:
        # Fallback if both condition and dsl_source are somehow empty (should not happen for valid criteria)
        raise ValueError(
            "Criterion must provide a structured condition or a valid dsl_source."
        )

    import datetime

    created_at = db_crit.get("created_at")
    if not created_at:
        created_at = datetime.datetime.now(datetime.timezone.utc)
    elif isinstance(created_at, str):
        try:
            created_at = datetime.datetime.fromisoformat(
                created_at.replace("Z", "+00:00")
            )
        except Exception:
            created_at = datetime.datetime.now(datetime.timezone.utc)
    else:
        try:
            if hasattr(created_at, "isoformat"):
                created_at = datetime.datetime.fromisoformat(
                    created_at.isoformat().replace("Z", "+00:00")
                )
            else:
                created_at = datetime.datetime.now(datetime.timezone.utc)
        except Exception:
            created_at = datetime.datetime.now(datetime.timezone.utc)

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


async def fetch_eligibility_criteria(study_id: str) -> List[EligibilityCriterion]:
    """
    Queries the central Designer service to fetch active eligibility criteria for a study.
    Uses Gateway signature for secure inter-service authorization.
    """
    designer_url = os.getenv("DESIGNER_URL", "http://localhost:8001")
    gateway_secret_env = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    gateway_secret = (
        gateway_secret_env.encode("utf-8")
        if isinstance(gateway_secret_env, str)
        else gateway_secret_env
    )

    user_id = "interop-service"
    roles = "sponsor_dm"
    timestamp = str(time.time())

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

    url = f"{designer_url.rstrip('/')}/api/v1/studies/{study_id}/eligibility-criteria"
    try:
        async with httpx.AsyncClient(timeout=5.0) as client:
            response = await client.get(url, headers=headers)
            if response.status_code == 200:
                raw_list = response.json()
                return [map_db_to_criterion(item) for item in raw_list]
            elif response.status_code == 404:
                raise HTTPException(
                    status_code=404,
                    detail=f"Study {study_id} or eligibility criteria not found in Designer service.",
                )
            else:
                raise HTTPException(
                    status_code=502,
                    detail=f"Failed to fetch eligibility criteria: Designer service returned {response.status_code}",
                )
    except httpx.RequestError as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to connect to Designer service for eligibility criteria validation: {str(e)}",
        )
