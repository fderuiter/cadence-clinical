import os
import hmac
import hashlib
from typing import Optional, Any
import httpx
from sae_icsr import IndividualCaseSafetyReport


def pseudonymize_value(value: str, salt: str) -> str:
    """
    Generate an irreversible HMAC-SHA256 pseudonym for patient direct identifiers.
    Reuses the pattern from apps/interop/fhir_adapter.py.
    """
    return hmac.new(salt.encode(), value.encode(), hashlib.sha256).hexdigest()


class SafetyAdapterInterface:
    async def transmit(self, xml_content: str) -> httpx.Response:
        raise NotImplementedError


class OutboundTransmissionAdapter(SafetyAdapterInterface):
    """
    Outbound transmission adapter for submitting E2B ICSR to the external safety PV database.
    """
    def __init__(
        self,
        endpoint_url: Optional[str] = None,
        client: Optional[httpx.AsyncClient] = None,
    ):
        self.endpoint_url = endpoint_url or os.getenv(
            "SAFETY_DB_TRANSMISSION_ENDPOINT",
            "http://localhost:8006/api/v1/safety/transmit-mock",
        )
        self.client = client

    async def transmit(self, xml_content: str) -> httpx.Response:
        headers = {"Content-Type": "application/xml"}
        if self.client is not None:
            return await self.client.post(self.endpoint_url, content=xml_content, headers=headers)
        else:
            async with httpx.AsyncClient() as client:
                return await client.post(self.endpoint_url, content=xml_content, headers=headers)


def prepare_and_deidentify_icsr(icsr: IndividualCaseSafetyReport, salt: Optional[str] = None) -> IndividualCaseSafetyReport:
    """
    Pseudonymize patient_id and strip direct birth_date (PII) before transmission.
    """
    import copy
    if salt is None:
        salt = os.getenv("SAFETY_SALT", "internal-safety-salt-12345")

    icsr_copy = copy.deepcopy(icsr)
    if icsr_copy.patient:
        raw_patient_id = icsr_copy.patient.patient_id
        if raw_patient_id:
            icsr_copy.patient.patient_id = pseudonymize_value(raw_patient_id, salt)
        icsr_copy.patient.birth_date = None  # Remove DOB
    return icsr_copy
