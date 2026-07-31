import logging
import os
import time
from typing import Optional

import httpx
from fastapi import HTTPException

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("execution-econsent-client")


class EConsentClientError(Exception):
    """Base exception for eConsent client errors."""

    pass


class EConsentClient(GatewayBaseClient):
    """Asynchronous client to retrieve subject consent status from the eConsent service."""

    def __init__(
        self,
        base_url: Optional[str] = None,
        timeout: float = 10.0,
    ) -> None:
        url = (
            base_url or os.getenv("ECONSENT_URL") or "http://localhost:8011"
        ).rstrip("/")
        super().__init__(base_url=url, timeout=timeout)

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
        path = f"/api/v1/econsent/subjects/{subject_pseudonym}/consent-status"
        params = {}
        if study_id:
            params["study_id"] = study_id

        try:
            # We call self.request to use the centralized GatewayBaseClient request logic
            response = await self.request(
                method="GET",
                path=path,
                user_id="execution-service",
                roles="system",
                change_reason="",
                params=params,
                timeout=self.timeout,
            )

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
