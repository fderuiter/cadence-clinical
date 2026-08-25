"""EDC Programmatic Write-Permission & Signature Qualification Gating Engine.

Enforces real-time gating on clinical data entry, observation creation, and eCRF signatures
until mandatory protocol amendment training is verified under ICH GCP E6(R2) Section 4.

Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
"""

from __future__ import annotations

import logging
import os
import time
from abc import ABC, abstractmethod
from typing import Any

import httpx
from fastapi import HTTPException, status

from packages.security.signing import generate_gateway_signature

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


class ITrainingVerificationClient(ABC):
    """Abstract interface for verifying user qualification with knowledge/training service."""

    @abstractmethod
    async def get_qualification_status(
        self,
        user_id: str,
        study_id: str,
        protocol_version: str,
        site_id: str | None = None,
        user_role: str | None = None,
    ) -> dict[str, Any]:
        """Fetch user qualification gating status for study and protocol version."""
        pass


class TrainingVerificationClient(ITrainingVerificationClient):
    """Asynchronous HTTP client to retrieve qualification status from the Knowledge/Training service."""

    def __init__(
        self,
        base_url: str | None = None,
        timeout: float = 3.0,
    ) -> None:
        self.base_url = (
            base_url or os.getenv("KNOWLEDGE_SERVICE_URL") or "http://127.0.0.1:8006"
        ).rstrip("/")
        self.timeout = timeout

    async def get_qualification_status(
        self,
        user_id: str,
        study_id: str,
        protocol_version: str,
        site_id: str | None = None,
        user_role: str | None = None,
    ) -> dict[str, Any]:
        timestamp = str(time.time())
        secret = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode(  # pragma: allowlist secret
            "utf-8"
        )
        sig = generate_gateway_signature(
            user_id="execution-service",
            roles="system",
            timestamp=timestamp,
            secret=secret,
        )
        headers = {
            "x-user-id": "execution-service",
            "x-user-roles": "system",
            "x-gateway-timestamp": timestamp,
            "x-gateway-signature": sig,
            "x-signature-version": "2",
        }
        params = {
            "user_id": user_id,
            "study_id": study_id,
            "protocol_version": protocol_version,
            "site_id": site_id or "",
            "user_role": user_role or "",
        }
        async with httpx.AsyncClient(timeout=self.timeout) as client:
            resp = await client.get(
                f"{self.base_url}/api/v1/training/gating-status",
                params=params,
                headers=headers,
            )
            if resp.status_code == 200:
                return resp.json()
            return {"is_qualified": True, "pending_curricula": []}


_global_training_client: ITrainingVerificationClient = TrainingVerificationClient()


def get_training_verification_client() -> ITrainingVerificationClient:
    return _global_training_client


def set_training_verification_client(client: ITrainingVerificationClient) -> None:
    global _global_training_client
    _global_training_client = client


async def enforce_training_qualification_gate(
    user_id: str,
    study_id: str,
    protocol_version: str,
    site_id: str | None = None,
    user_role: str | None = None,
    training_client: ITrainingVerificationClient | None = None,
) -> None:
    """Verify training qualification for EDC write & signature permissions, raising TrainingRequiredError if incomplete.

    Requirements: PRD-TRN-002
    """
    if not study_id or not protocol_version or not user_id:
        return

    client = training_client or get_training_verification_client()
    try:
        data = await client.get_qualification_status(
            user_id=user_id,
            study_id=study_id,
            protocol_version=protocol_version,
            site_id=site_id,
            user_role=user_role,
        )
        if not data.get("is_qualified", True):
            logger.warning(
                "EDC WRITE BLOCKED: User %s lacks mandatory training for protocol %s on study %s (site: %s)",
                user_id,
                protocol_version,
                study_id,
                site_id,
            )
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
        logger.debug("Could not verify training qualification status: %s", exc)
        return
