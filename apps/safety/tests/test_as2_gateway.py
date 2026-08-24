"""Unit tests for Native S/MIME AS2 Transmission Gateway & E2B(R3) MDN Parser.

@req:PRD-PVG-002
@req:PRD-SYS-001
"""

from apps.safety.services.as2_gateway import (
    AS2DispositionStatus,
    AS2GatewayService,
)


def test_mic_calculation_and_envelope_generation():
    """Verify SHA-256 MIC calculation and AS2 headers formatting.

    @req:PRD-PVG-002
    @req:PRD-SYS-168
    @req:ADR-2199
    """
    xml_sample = """<?xml version="1.0" encoding="UTF-8"?>
    <ichicsr lang="en">
        <safetyreport>
            <safetyreportid>US-SPONSOR-2026-0001</safetyreportid>
        </safetyreport>
    </ichicsr>"""

    envelope = AS2GatewayService.create_outbound_envelope(
        as2_from="CADENCE-SPONSOR",
        as2_to="FDA-ESG-GATEWAY",
        xml_payload=xml_sample,
    )

    assert envelope.as2_from == "CADENCE-SPONSOR"
    assert envelope.as2_to == "FDA-ESG-GATEWAY"
    assert "sha-256" in envelope.content_mic_sha256
    assert envelope.headers["AS2-Version"] == "1.2"
    assert envelope.headers["AS2-From"] == "CADENCE-SPONSOR"
    assert envelope.headers["AS2-To"] == "FDA-ESG-GATEWAY"
    assert envelope.headers["Disposition-Notification-Options"].startswith(
        "signed-receipt-protocol"
    )


def test_parse_valid_mdn_receipt():
    """Verify parsing and MIC non-repudiation matching of a valid processed MDN.

    @req:PRD-PVG-002
    @req:PRD-SYS-168
    @req:ADR-2199
    """
    xml_sample = "<ichicsr><safetyreport><id>123</id></safetyreport></ichicsr>"
    envelope = AS2GatewayService.create_outbound_envelope(
        as2_from="CADENCE-SPONSOR",
        as2_to="EMA-EV-HUMAN",
        xml_payload=xml_sample,
        custom_message_id="<msg-12345@cadence.ccrsoft.com>",
    )

    raw_mdn = f"""Message-ID: <mdn-99999@ema.europa.eu>
Reporting-UA: EMA EudraVigilance Gateway v4.2
Original-Message-ID: <msg-12345@cadence.ccrsoft.com>
Disposition: automatic-action/MDN-sent-automatically; processed
Received-Content-MIC: {envelope.content_mic_sha256}
"""

    receipt = AS2GatewayService.parse_and_verify_mdn(
        raw_mdn_text=raw_mdn,
        expected_outbound_envelope=envelope,
    )

    assert receipt.disposition_status == AS2DispositionStatus.PROCESSED
    assert receipt.original_message_id == "<msg-12345@cadence.ccrsoft.com>"
    assert receipt.is_mic_verified is True
    assert receipt.verification_error is None


def test_parse_tampered_mdn_receipt_mismatch():
    """Verify that an altered MIC in the MDN receipt is flagged as TAMPERED.

    @req:PRD-PVG-002
    @req:PRD-SYS-168
    @req:ADR-2199
    """
    xml_sample = "<ichicsr><safetyreport><id>123</id></safetyreport></ichicsr>"
    envelope = AS2GatewayService.create_outbound_envelope(
        as2_from="CADENCE-SPONSOR",
        as2_to="FDA-ESG-GATEWAY",
        xml_payload=xml_sample,
    )

    tampered_mdn = f"""Message-ID: <mdn-88888@fda.gov>
Reporting-UA: FDA Electronic Submissions Gateway
Original-Message-ID: {envelope.message_id}
Disposition: automatic-action/MDN-sent-automatically; processed
Received-Content-MIC: dGFtcGVyZWRfbWljX3ZhbHVl, sha-256
"""

    receipt = AS2GatewayService.parse_and_verify_mdn(
        raw_mdn_text=tampered_mdn,
        expected_outbound_envelope=envelope,
    )

    assert receipt.disposition_status == AS2DispositionStatus.TAMPERED
    assert receipt.is_mic_verified is False
    assert "MIC mismatch" in (receipt.verification_error or "")
