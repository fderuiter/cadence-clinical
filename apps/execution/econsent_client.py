import logging
import os
from abc import ABC, abstractmethod
from collections.abc import Awaitable, Callable
from typing import Any

import httpx

from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("execution-econsent-client")


class EConsentClientError(Exception):
    """Base exception for eConsent client errors."""

    pass


class IConsentVerificationClient(ABC):
    """Abstract interface for verifying subject consent status with consent services."""

    @abstractmethod
    async def get_subject_consent_status(
        self,
        subject_pseudonym: str,
        study_id: str | None = None,
        client: httpx.AsyncClient | None = None,
    ) -> dict[str, Any]:
        """Fetch canonical subject consent status from consent verification service."""
        pass


IConsentClient = IConsentVerificationClient

_subject_consent_resolver: (
    Callable[[str, str | None], Awaitable[dict[str, Any]]] | None
) = None
_consent_client_instance: IConsentVerificationClient | None = None


def register_subject_consent_resolver(
    resolver: Callable[[str, str | None], Awaitable[dict[str, Any]]] | None,
) -> None:
    """Registers a pluggable resolver hook for retrieving subject consent status."""
    global _subject_consent_resolver
    _subject_consent_resolver = resolver


def get_consent_verification_client() -> IConsentVerificationClient:
    """Retrieve the active consent verification client instance."""
    global _consent_client_instance
    if _consent_client_instance is None:
        _consent_client_instance = EConsentClient()
    return _consent_client_instance


def set_consent_verification_client(
    client: IConsentVerificationClient | None,
) -> None:
    """Set or override the active consent verification client instance."""
    global _consent_client_instance
    _consent_client_instance = client


class EConsentClient(IConsentVerificationClient):
    """Asynchronous client to retrieve subject consent status from the eConsent service."""

    def __init__(
        self,
        base_url: str | None = None,
        timeout: float = 10.0,
    ) -> None:
        self.base_url = (
            base_url or os.getenv("ECONSENT_URL") or "http://localhost:8011"
        ).rstrip("/")
        self.timeout = timeout
        self.gw_client = GatewayBaseClient(base_url=self.base_url, timeout=self.timeout)

    async def get_subject_consent_status(
        self,
        subject_pseudonym: str,
        study_id: str | None = None,
        client: httpx.AsyncClient | None = None,
    ) -> dict[str, Any]:
        """Fetch canonical subject consent status from the eConsent service."""
        if _subject_consent_resolver is not None:
            return await _subject_consent_resolver(subject_pseudonym, study_id)

        path = f"/api/v1/econsent/subjects/{subject_pseudonym}/consent-status"
        params: dict[str, Any] = {}
        if study_id:
            params["study_id"] = study_id

        try:
            if client is not None:
                headers = self.gw_client.build_headers(
                    user_id="execution-service",
                    roles="system",
                    change_reason="",
                )
                url = f"{self.base_url}{path}"
                response = await client.get(
                    url, headers=headers, params=params, timeout=self.timeout
                )
            else:
                response = await self.gw_client.request(
                    method="GET",
                    path=path,
                    user_id="execution-service",
                    roles="system",
                    change_reason="",
                    params=params,
                )

            if response.status_code != 200:
                raise EConsentClientError(
                    f"eConsent service returned error status {response.status_code}: {response.text}"
                )

            return response.json()

        except EConsentClientError:
            raise
        except httpx.RequestError as e:
            logger.error(
                "Failed to connect to eConsent service for subject consent status: %s",
                e,
            )
            raise EConsentClientError(
                f"Failed to connect to eConsent service: {str(e)}"
            ) from e
        except Exception as e:
            logger.error("Unexpected error in eConsent client: %s", e)
            raise EConsentClientError(f"Error parsing eConsent status: {str(e)}") from e


async def fetch_subject_consent_status(
    subject_pseudonym: str,
    study_id: str | None = None,
) -> dict[str, Any]:
    """Convenience module helper to fetch canonical subject consent status."""
    client = get_consent_verification_client()
    return await client.get_subject_consent_status(subject_pseudonym, study_id)


__all__ = [
    "EConsentClient",
    "EConsentClientError",
    "IConsentClient",
    "IConsentVerificationClient",
    "fetch_subject_consent_status",
    "get_consent_verification_client",
    "register_subject_consent_resolver",
    "set_consent_verification_client",
]
