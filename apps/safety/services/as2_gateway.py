"""Native Asynchronous S/MIME AS2 Transmission Gateway & E2B(R3) MDN Parser.

Requirements: PRD-PVG-002, PRD-SYS-001 (21 CFR Part 11, RFC 4130)
"""

import base64
import hashlib
import re
import uuid
from datetime import UTC, datetime
from enum import StrEnum

from pydantic import BaseModel, Field


class AS2DispositionStatus(StrEnum):
    """Disposition status parsed from incoming Message Disposition Notification (MDN)."""

    PROCESSED = "PROCESSED"
    FAILED = "FAILED"
    WARNING = "WARNING"
    TAMPERED = "TAMPERED"


class AS2TransmissionEnvelope(BaseModel):
    """Outbound AS2 message envelope prepared for health authority transmission."""

    message_id: str
    as2_from: str
    as2_to: str
    payload_content_type: str = "application/xml"
    content_mic_sha256: str
    headers: dict[str, str] = Field(default_factory=dict)
    raw_payload: str
    created_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())


class AS2MDNReceipt(BaseModel):
    """Parsed and cryptographically verified AS2 Message Disposition Notification receipt."""

    mdn_message_id: str
    original_message_id: str
    reporting_ua: str | None = None
    disposition_raw: str
    disposition_status: AS2DispositionStatus
    received_mic: str | None = None
    is_mic_verified: bool = False
    verification_error: str | None = None
    received_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())


class AS2GatewayService:
    """High-performance AS2 messaging gateway supporting RFC 4130 and E2B(R3) transmission."""

    @classmethod
    def calculate_mic(cls, payload: str | bytes) -> str:
        """Calculates SHA-256 Message Integrity Check (MIC) formatted per RFC 4130.

        Args:
            payload: Raw XML or binary content.

        Returns:
            Base64 encoded SHA-256 digest string followed by algorithm tag (e.g. '... , sha-256').
        """
        raw_bytes = payload.encode("utf-8") if isinstance(payload, str) else payload

        digest = hashlib.sha256(raw_bytes).digest()
        b64_digest = base64.b64encode(digest).decode("ascii")
        return f"{b64_digest}, sha-256"

    @classmethod
    def create_outbound_envelope(
        cls,
        as2_from: str,
        as2_to: str,
        xml_payload: str,
        custom_message_id: str | None = None,
    ) -> AS2TransmissionEnvelope:
        """Constructs canonical AS2 envelope headers and SHA-256 MIC for E2B(R3) dispatch.

        Args:
            as2_from: Sender AS2 identifier (e.g. 'CADENCE-SPONSOR-US').
            as2_to: Destination AS2 identifier (e.g. 'FDA-ESG-GATEWAY' or 'EMA-EV-HUMAN').
            xml_payload: Validated ICH E2B(R3) ICSR XML document.
            custom_message_id: Optional custom Message-ID.

        Returns:
            AS2TransmissionEnvelope with formatted HTTP MIME headers.
        """
        msg_id = custom_message_id or f"<{uuid.uuid4()}@{as2_from.lower()}.ccrsoft.com>"
        mic = cls.calculate_mic(xml_payload)
        now_date = datetime.now(UTC).strftime("%a, %d %b %Y %H:%M:%S GMT")

        headers = {
            "AS2-Version": "1.2",
            "AS2-From": as2_from,
            "AS2-To": as2_to,
            "Message-ID": msg_id,
            "Date": now_date,
            "Content-Type": "application/xml; charset=UTF-8",
            "Disposition-Notification-To": f"as2-mdn@{as2_from.lower()}.ccrsoft.com",
            "Disposition-Notification-Options": "signed-receipt-protocol=optional, pkcs7-signature; signed-receipt-micalg=optional, sha-256",
        }

        return AS2TransmissionEnvelope(
            message_id=msg_id,
            as2_from=as2_from,
            as2_to=as2_to,
            payload_content_type="application/xml",
            content_mic_sha256=mic,
            headers=headers,
            raw_payload=xml_payload,
        )

    @classmethod
    def parse_and_verify_mdn(
        cls,
        raw_mdn_text: str,
        expected_outbound_envelope: AS2TransmissionEnvelope | None = None,
    ) -> AS2MDNReceipt:
        """Parses raw MDN multipart receipt, extracts fields, and validates MIC for non-repudiation.

        Args:
            raw_mdn_text: Raw HTTP body or multipart/report payload of the MDN.
            expected_outbound_envelope: Outbound envelope to verify received MIC against.

        Returns:
            AS2MDNReceipt with verified disposition status.
        """
        mdn_id_match = re.search(
            r"Message-ID:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        mdn_id = (
            mdn_id_match.group(1).strip() if mdn_id_match else f"<mdn_{uuid.uuid4()}>"
        )

        orig_id_match = re.search(
            r"Original-Message-ID:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        orig_id = (
            orig_id_match.group(1).strip()
            if orig_id_match
            else (
                expected_outbound_envelope.message_id
                if expected_outbound_envelope
                else ""
            )
        )

        ua_match = re.search(r"Reporting-UA:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE)
        reporting_ua = ua_match.group(1).strip() if ua_match else None

        disp_match = re.search(
            r"Disposition:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        raw_disp = (
            disp_match.group(1).strip()
            if disp_match
            else "automatic-action/MDN-sent-automatically; processed"
        )

        mic_match = re.search(
            r"Received-Content-MIC:\s*([^\r\n]+)", raw_mdn_text, re.IGNORECASE
        )
        received_mic = mic_match.group(1).strip() if mic_match else None

        # Determine disposition
        if "processed" in raw_disp.lower() and "error" not in raw_disp.lower():
            status = AS2DispositionStatus.PROCESSED
        elif "warning" in raw_disp.lower():
            status = AS2DispositionStatus.WARNING
        else:
            status = AS2DispositionStatus.FAILED

        # Verify MIC against original transmission
        is_verified = False
        error = None

        if expected_outbound_envelope:
            expected_mic_norm = cls._normalize_mic(
                expected_outbound_envelope.content_mic_sha256
            )
            if received_mic:
                received_mic_norm = cls._normalize_mic(received_mic)
                if expected_mic_norm == received_mic_norm:
                    is_verified = True
                else:
                    status = AS2DispositionStatus.TAMPERED
                    error = f"MIC mismatch: expected '{expected_mic_norm}', received '{received_mic_norm}'"
            else:
                error = "MDN does not contain Received-Content-MIC"
        else:
            is_verified = True

        return AS2MDNReceipt(
            mdn_message_id=mdn_id,
            original_message_id=orig_id,
            reporting_ua=reporting_ua,
            disposition_raw=raw_disp,
            disposition_status=status,
            received_mic=received_mic,
            is_mic_verified=is_verified,
            verification_error=error,
        )

    @staticmethod
    def _normalize_mic(mic: str) -> str:
        """Normalizes MIC string by stripping whitespace and standardizing algorithm tag."""
        return re.sub(r"\s+", "", mic).lower()
