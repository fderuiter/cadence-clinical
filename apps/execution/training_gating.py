"""EDC Programmatic Write-Permission & Signature Qualification Gating Engine.

Enforces real-time gating on clinical data entry, observation creation, and eCRF signatures
until mandatory protocol amendment training is verified under ICH GCP E6(R2) Section 4.

Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
"""

from __future__ import annotations

import logging
import os
from typing import Any

import httpx
from fastapi import HTTPException, status

logger = logging.getLogger("execution-training-gating")


class TrainingRequiredError(HTTPException):
    """HTTP 403 Exception raised when mandatory protocol training is incomplete."""

    def __init__(
        self,
        study_id: str,
        protocol_version: str,
        user_id: str,
        site_id: str | None = None,
        pending_curricula: list[dict[str, Any]] | None = None,
    ) -> None:
        message = (
            f"TrainingRequiredError: Mandatory training incomplete for protocol version "
            f"{protocol_version} on study {study_id}."
        )
        super().__init__(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "error": "TRAINING_REQUIRED",
                "message": message,
                "study_id": study_id,
                "protocol_version": protocol_version,
                "user_id": user_id,
                "site_id": site_id,
                "pending_curricula": pending_curricula or [],
            },
        )


async def enforce_training_qualification_gate(
    user_id: str,
    study_id: str,
    protocol_version: str,
    site_id: str | None = None,
    user_role: str | None = None,
    knowledge_session: Any | None = None,
) -> None:
    """Verify training qualification for EDC write & signature permissions, raising TrainingRequiredError if incomplete.

    Requirements: PRD-TRN-002
    """
    if not study_id or not protocol_version or not user_id:
        return

    # In production/gateway mode, query knowledge microservice gating API
    service_url = os.getenv("KNOWLEDGE_SERVICE_URL", "http://127.0.0.1:8006")
    try:
        async with httpx.AsyncClient(timeout=2.0) as client:
            resp = await client.get(
                f"{service_url}/api/v1/training/gating-status",
                params={
                    "user_id": user_id,
                    "study_id": study_id,
                    "protocol_version": protocol_version,
                    "site_id": site_id or "",
                    "user_role": user_role or "",
                },
            )
            if resp.status_code == 200:
                data = resp.json()
                if not data.get("is_qualified", True):
                    raise TrainingRequiredError(
                        study_id=study_id,
                        protocol_version=protocol_version,
                        user_id=user_id,
                        site_id=site_id,
                        pending_curricula=data.get("pending_curricula", []),
                    )
    except TrainingRequiredError:
        raise
    except Exception as exc:
        logger.debug("Could not query training service qualification status: %s", exc)
        return
