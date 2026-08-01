"""eConsent signature processing and workflow engine service.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import os
import time
from datetime import datetime

import httpx
from pydantic import BaseModel

from packages.security.signing import generate_gateway_signature

EXECUTION_URL = (os.getenv("EXECUTION_URL") or "http://localhost:8002").rstrip("/")


def _get_auth_headers() -> dict[str, str]:
    gateway_secret_env = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    gateway_secret = (
        gateway_secret_env.encode("utf-8")
        if isinstance(gateway_secret_env, str)
        else gateway_secret_env
    )

    user_id = "econsent-service"
    roles = "system"
    timestamp = str(time.time())

    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=gateway_secret,
        change_reason="system_operation",
    )

    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": "system_operation",
    }


class EConsentSignRequest(BaseModel):
    subject_id: str
    icf_version_id: str
    printed_name: str
    relationship_to_subject: str
    signature_svg: str
    otp_auth_code: str
    reason_for_change: str


class EConsentSignResponse(BaseModel):
    consent_record_id: str
    signed_pdf_url: str
    signature_timestamp_utc: datetime
    verification_hash: str


class ConsentSignatureObj:
    def __init__(self, data: dict):
        self.id = data.get("id")
        self.subject_id = data.get("subject_id")
        self.icf_version_id = data.get("icf_version_id")
        self.printed_name = data.get("printed_name")
        self.signature_svg = data.get("signature_svg")
        self.verification_hash = data.get("verification_hash")
        self.status = data.get("status")
        self.reason_for_change = data.get("reason_for_change")

        signed_at_raw = data.get("signed_at")
        if signed_at_raw:
            self.signed_at = datetime.fromisoformat(
                signed_at_raw.replace("Z", "+00:00")
            )
        else:
            self.signed_at = None


async def process_econsent_signature(
    session, payload: EConsentSignRequest
) -> EConsentSignResponse:
    """Process subject eConsent signature and generate a GxP consent signature certificate.

    Requirements: PRD-SYS-001
    """
    headers = _get_auth_headers()
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{EXECUTION_URL}/api/v1/execution/signatures/process-econsent",
            headers=headers,
            json=payload.model_dump() if hasattr(payload, "model_dump") else payload,
        )
        if response.status_code == 400:
            raise ValueError(
                response.json().get("detail", "Error processing eConsent signature")
            )
        if response.status_code != 200:
            raise ValueError(f"HTTP error {response.status_code}: {response.text}")

        res_data = response.json()
        timestamp_raw = res_data.get("signature_timestamp_utc")
        if timestamp_raw:
            ts = datetime.fromisoformat(timestamp_raw.replace("Z", "+00:00"))
        else:
            ts = datetime.now()

        return EConsentSignResponse(
            consent_record_id=res_data["consent_record_id"],
            signed_pdf_url=res_data["signed_pdf_url"],
            signature_timestamp_utc=ts,
            verification_hash=res_data["verification_hash"],
        )


class EConsentWorkflowEngine:
    """Workflow engine handling eConsent state transitions."""

    def __init__(self, db_session):
        self.session = db_session

    async def execute_signature_capture(
        self,
        subject_id: str,
        icf_version_id: str,
        printed_name: str,
        signature_svg: str,
        reason_for_change: str,
    ) -> ConsentSignatureObj:
        """Capture patient eConsent signature and generate GxP compliant consent certificate.

        Requirements: PRD-SYS-001
        """
        headers = _get_auth_headers()
        payload = {
            "subject_id": subject_id,
            "icf_version_id": icf_version_id,
            "printed_name": printed_name,
            "signature_svg": signature_svg,
            "reason_for_change": reason_for_change,
        }
        async with httpx.AsyncClient() as client:
            response = await client.post(
                f"{EXECUTION_URL}/api/v1/execution/signatures/capture",
                headers=headers,
                json=payload,
            )
            if response.status_code == 400:
                raise ValueError(
                    response.json().get("detail", "Error capturing signature")
                )
            if response.status_code != 200:
                raise ValueError(f"HTTP error {response.status_code}: {response.text}")
            return ConsentSignatureObj(response.json())
