import logging
import os
import time
from typing import Optional

import httpx
from fastapi import HTTPException

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("execution-econsent-client")


class EConsentClientError(Exception):
    """Base exception for eConsent client errors."""

    pass


class EConsentClient:
    """Asynchronous client to retrieve subject consent status from the eConsent service."""

    def __init__(
        self,
        base_url: Optional[str] = None,
        timeout: float = 10.0,
    ) -> None:
        self.base_url = (
            base_url or os.getenv("ECONSENT_URL") or "http://localhost:8011"
        ).rstrip("/")
        self.timeout = timeout

    async def get_subject_consent_status(
        self,
        subject_pseudonym: str,
        study_id: Optional[str] = None,
        client: Optional[httpx.AsyncClient] = None,
    ) -> dict:
        """Fetch canonical subject consent status from the eConsent service.

        Args:
            subject_pseudonym (str): The pseudonym identifier of the subject.
            study_id (Optional[str]): Optional study identifier to filter consent.
            client (Optional[httpx.AsyncClient]): Optional shared HTTPX async client.

        Returns:
            dict: The JSON response containing subject consent status.
        """
        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        )
        gateway_secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

        user_id = "execution-service"
        roles = "system"
        timestamp = str(time.time())

        # Generate gateway signature v2
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

        url = f"{self.base_url}/api/v1/econsent/subjects/{subject_pseudonym}/consent-status"
        params = {}
        if study_id:
            params["study_id"] = study_id

        try:
            if client is not None:
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, headers=headers, params=params)

            if response.status_code != 200:
                raise HTTPException(
                    status_code=502,
                    detail=f"eConsent service returned error status {response.status_code}: {response.text}",
                )

            return response.json()

        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to eConsent service for subject consent status: %s",
                e,
            )
            raise HTTPException(
                status_code=502,
                detail=f"Failed to connect to eConsent service: {str(e)}",
            )
        except Exception as e:
            if isinstance(e, HTTPException):
                raise e
            logger.error("Unexpected error in eConsent client: %s", e)
            raise HTTPException(
                status_code=502,
                detail=f"Error parsing eConsent status: {str(e)}",
            )


# Module level convenience helper function
async def fetch_subject_consent_status(
    subject_pseudonym: str,
    study_id: Optional[str] = None,
) -> dict:
    """Convenience module helper to fetch canonical subject consent status."""
    client = EConsentClient()
    return await client.get_subject_consent_status(subject_pseudonym, study_id)
