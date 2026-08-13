import os
from typing import Any

import httpx


class SafetyDatabaseAdapter:
    """
    Adapter for communicating with external Safety databases / Pharmacovigilance gateways.
    Highly configurable and mockable for clean GxP testability.
    """

    def __init__(
        self,
        endpoint_url: str | None = None,
        ingestion_url: str | None = None,
        client: httpx.AsyncClient | None = None,
    ):
        self.endpoint_url = endpoint_url or os.getenv(
            "SAFETY_DB_TRANSMISSION_ENDPOINT",
            "http://localhost:8006/api/v1/safety/transmit-mock",
        )
        self.ingestion_url = ingestion_url or os.getenv(
            "SAFETY_DB_INGESTION_ENDPOINT",
            "http://localhost:8006/api/v1/safety/cases-mock",
        )
        self.client = client

    async def transmit(self, xml_content: str) -> httpx.Response:
        """
        Transmits the rendered E2B XML payload to the configured safety endpoint.

        Args:
            xml_content (str): The pseudonymized E2B XML content.

        Returns:
            httpx.Response: The HTTP response from the gateway endpoint.
        """
        if self.client is not None:
            return await self.client.post(
                self.endpoint_url,
                content=xml_content,
                headers={"Content-Type": "application/xml"},
            )
        async with httpx.AsyncClient() as client:
            return await client.post(
                self.endpoint_url,
                content=xml_content,
                headers={"Content-Type": "application/xml"},
            )

    async def fetch_case(self, case_id: str) -> dict[str, Any]:
        """
        Fetches a specific safety case payload from the external safety database.
        """
        url = f"{self.ingestion_url.rstrip('/')}/{case_id}"
        if self.client is not None:
            response = await self.client.get(url)
        else:
            async with httpx.AsyncClient() as client:
                response = await client.get(url)

            response.raise_for_status()
        return response.json()

    async def fetch_cases(self) -> list[dict[str, Any]]:
        """
        Fetches all safety cases from the external safety database ingestion endpoint.
        """
        url = self.ingestion_url
        if self.client is not None:
            response = await self.client.get(url)
        else:
            async with httpx.AsyncClient() as client:
                response = await client.get(url)

            response.raise_for_status()
        return response.json()
