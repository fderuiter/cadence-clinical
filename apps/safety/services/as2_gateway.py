"""Native Asynchronous S/MIME AS2 Transmission Gateway & E2B(R3) MDN Parser.

Requirements: PRD-PVG-002, PRD-SYS-001, ADR-2199 (21 CFR Part 11, RFC 4130)
"""

import re
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
    """High-performance AS2 messaging gateway supporting RFC 4130 and E2B(R3) transmission.

    Delegates cryptographic MIC calculations, envelope sealing, and MDN verification
    to the centralized RegulatoryAuditLedger.
    """

    @classmethod
    def calculate_mic(cls, payload: str | bytes) -> str:
        """Calculates SHA-256 Message Integrity Check (MIC) formatted per RFC 4130.

        Args:
            payload: Raw XML or binary content.

        Returns:
            Base64 encoded SHA-256 digest string followed by algorithm tag (e.g. '... , sha-256').
        """
        from apps.safety.services.regulatory_audit_ledger import (
            RegulatoryAuditLedger,
        )

        return RegulatoryAuditLedger.calculate_as2_mic(payload)

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
        from apps.safety.services.regulatory_audit_ledger import (
            RegulatoryAuditLedger,
        )

        return RegulatoryAuditLedger.seal_outbound_as2_envelope(
            as2_from=as2_from,
            as2_to=as2_to,
            custom_message_id=custom_message_id,
            xml_payload=xml_payload,
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
        from apps.safety.services.regulatory_audit_ledger import (
            RegulatoryAuditLedger,
        )

        return RegulatoryAuditLedger.verify_as2_mdn_receipt(
            expected_outbound_envelope=expected_outbound_envelope,
            raw_mdn_text=raw_mdn_text,
        )

    @staticmethod
    def _normalize_mic(mic: str) -> str:
        """Normalizes MIC string by stripping whitespace and standardizing algorithm tag."""
        return re.sub(r"\s+", "", mic).lower()
