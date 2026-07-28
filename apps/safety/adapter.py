import os
from typing import Optional
import httpx


class SafetyDatabaseAdapter:
    """
    Adapter for communicating with external Safety databases / Pharmacovigilance gateways.
    Highly configurable and mockable for clean GxP testability.
    """
    def __init__(self, endpoint_url: Optional[str] = None, client: Optional[httpx.AsyncClient] = None):
        self.endpoint_url = endpoint_url or os.getenv(
            "SAFETY_DB_TRANSMISSION_ENDPOINT", "http://localhost:8006/api/v1/safety/transmit-mock"
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
        else:
            async with httpx.AsyncClient() as client:
                return await client.post(
                    self.endpoint_url,
                    content=xml_content,
                    headers={"Content-Type": "application/xml"},
                )
