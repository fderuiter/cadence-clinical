"""Regulatory Acknowledgement (ACK A, ACK B, ACK C) Parser & Validator.

Validates multi-tier health authority responses:
- ACK A: Perimeter AS2 Transport / MDN Receipt
- ACK B: Technical / Syntactic Schema Validation Receipt (ICH E2B(R3) ACK XML)
- ACK C: Regulatory Health Authority Business Acceptance / DB Ingestion Receipt (FDA Center / EMA EV ACK)

Requirements: PRD-PVG-002, ADR-2199
"""

import re
from datetime import UTC, datetime

import defusedxml.ElementTree as ET

from apps.safety.domain.as2_models import (
    AckStatus,
    AckType,
    AS2Authority,
    RegulatoryAckParseResult,
)


class RegulatoryAckParser:
    """Parser and validator for multi-tier global regulatory health authority acknowledgements."""

    @classmethod
    def parse_ack_payload(
        cls,
        payload_content: str,
        ack_type: AckType = AckType.ACK_B,
        authority: AS2Authority | None = None,
    ) -> RegulatoryAckParseResult:
        """Parse acknowledgement XML/text and determine acceptance status.

        Args:
            payload_content: The raw XML or EDI ACK payload.
            ack_type: The expected acknowledgement tier (ACK_B or ACK_C).
            authority: Target authority if known.

        Returns:
            RegulatoryAckParseResult with extracted identifiers, error codes, and acceptance flags.
        """
        if ack_type == AckType.ACK_B:
            return cls.parse_ack_b(payload_content)
        if ack_type == AckType.ACK_C:
            return cls.parse_ack_c(payload_content)
        # Default or fallback
        return cls.parse_generic_ack(payload_content, ack_type=ack_type)

    @classmethod
    def parse_ack_b(cls, xml_content: str) -> RegulatoryAckParseResult:
        """Parse an ICH E2B(R3) Syntactic Validation Acknowledgement (ACK B).

        Args:
            xml_content: E2B(R3) ACK XML payload.

        Returns:
            RegulatoryAckParseResult with syntactic errors or acceptance confirmation.
        """
        try:
            root = ET.fromstring(xml_content.encode("utf-8"))
        except Exception as exc:
            return RegulatoryAckParseResult(
                ack_type=AckType.ACK_B,
                ack_status=AckStatus.PARSING_ERROR,
                is_accepted=False,
                errors=[f"Failed to parse ACK B XML payload: {str(exc)}"],
                details={"raw_xml": xml_content[:500]},
            )

        # Strip XML namespaces for flexible tag querying
        ns_pattern = re.compile(r"\{.*?\}")

        def strip_ns(tag: str) -> str:
            return ns_pattern.sub("", tag).lower()

        # Find elements regardless of namespace
        elements_map: dict[str, str] = {}
        error_list: list[str] = []
        warning_list: list[str] = []

        for elem in root.iter():
            tag_name = strip_ns(elem.tag)
            text_val = (elem.text or "").strip()
            if text_val:
                elements_map[tag_name] = text_val

            if tag_name in (
                "errormessage",
                "parsingerrordescription",
                "error_message",
                "errormessagecomment",
            ):
                if text_val:
                    error_list.append(text_val)
            elif tag_name in ("warningmessage", "warning_message", "warningcomment"):
                if text_val:
                    warning_list.append(text_val)

        # Extract transmission / message identifiers
        message_id = (
            elements_map.get("messageid")
            or elements_map.get("originalmessageid")
            or elements_map.get("message_id")
        )
        receipt_id = (
            elements_map.get("receiptid")
            or elements_map.get("transmissionacknowledgementnumber")
            or elements_map.get("acknowledgementnumber")
        )
        case_id = (
            elements_map.get("worldwideuniquecaseid")
            or elements_map.get("worldwide_unique_case_id")
            or elements_map.get("safetyreportid")
            or elements_map.get("localreportnumber")
        )

        # Extract acknowledgement codes
        trans_ack_code = (
            elements_map.get("transmissionacknowledgementcode")
            or elements_map.get("transmission_ack_code")
            or elements_map.get("acknowledgementcode")
            or elements_map.get("ack_code")
            or elements_map.get("reportacknowledgementcode")
            or ""
        ).upper()

        # Evaluation of standard E2B / HL7 ACK codes
        # 01 / AA / CA = Application Accept (Accepted)
        # 02 / AE / CE = Application Error (Syntactic failure)
        # 03 / AR / CR = Application Reject (Fatal parsing failure)
        is_accepted = False
        ack_status = AckStatus.REJECTED

        if trans_ack_code in ("01", "AA", "CA", "ACCEPTED", "SUCCESS"):
            is_accepted = True
            ack_status = AckStatus.ACCEPTED
            error_list = []
        elif trans_ack_code in ("02", "AE", "CE", "SYNTAX_ERROR", "ERROR"):
            is_accepted = False
            ack_status = AckStatus.SYNTAX_ERROR
        elif trans_ack_code in ("03", "AR", "CR", "REJECTED"):
            is_accepted = False
            ack_status = AckStatus.REJECTED
        else:
            # Fallback based on error list presence
            if not error_list and "error" not in xml_content.lower():
                is_accepted = True
                ack_status = AckStatus.ACCEPTED
                error_list = []
            else:
                is_accepted = False
                ack_status = AckStatus.SYNTAX_ERROR

        return RegulatoryAckParseResult(
            ack_type=AckType.ACK_B,
            ack_status=ack_status,
            is_accepted=is_accepted,
            authority_receipt_id=receipt_id,
            worldwide_unique_case_id=case_id,
            message_id=message_id,
            authority_status_code=trans_ack_code,
            errors=error_list,
            warnings=warning_list,
            details=elements_map,
        )

    @classmethod
    def parse_ack_c(cls, xml_content: str) -> RegulatoryAckParseResult:
        """Parse a Regulatory Health Authority Business Acceptance / DB Ingestion Receipt (ACK C).

        Args:
            xml_content: Authority Center / EudraVigilance Level 3 ACK XML payload.

        Returns:
            RegulatoryAckParseResult with ingestion outcome and authority tracking IDs.
        """
        try:
            root = ET.fromstring(xml_content.encode("utf-8"))
        except Exception as exc:
            return RegulatoryAckParseResult(
                ack_type=AckType.ACK_C,
                ack_status=AckStatus.PARSING_ERROR,
                is_accepted=False,
                errors=[f"Failed to parse ACK C XML payload: {str(exc)}"],
                details={"raw_xml": xml_content[:500]},
            )

        ns_pattern = re.compile(r"\{.*?\}")

        def strip_ns(tag: str) -> str:
            return ns_pattern.sub("", tag).lower()

        elements_map: dict[str, str] = {}
        error_list: list[str] = []
        warning_list: list[str] = []

        for elem in root.iter():
            tag_name = strip_ns(elem.tag)
            text_val = (elem.text or "").strip()
            if text_val:
                elements_map[tag_name] = text_val

            if tag_name in (
                "errormessage",
                "businesserror",
                "rejectionreason",
                "medicalreviewrejection",
            ):
                if text_val:
                    error_list.append(text_val)
            elif tag_name in (
                "warningmessage",
                "warning",
                "ingestionnotice",
                "meddracodingnotice",
            ):
                if text_val:
                    warning_list.append(text_val)

        # Center / Authority Tracking Number (FDA CTN / EMA EV Ingestion Ticket)
        receipt_id = (
            elements_map.get("authoritytrackingnumber")
            or elements_map.get("centertrackingnumber")
            or elements_map.get("eudravigilancebatchnumber")
            or elements_map.get("ev_batch_id")
            or elements_map.get("core3ticketid")
            or elements_map.get("ticket_id")
            or elements_map.get("submission_id")
        )
        case_id = (
            elements_map.get("worldwideuniquecaseid")
            or elements_map.get("worldwide_unique_case_id")
            or elements_map.get("safetyreportid")
            or elements_map.get("localreportnumber")
        )
        message_id = (
            elements_map.get("messageid")
            or elements_map.get("originalmessageid")
            or elements_map.get("message_id")
        )

        # Ingestion status code
        status_code = (
            elements_map.get("authorityacceptancecode")
            or elements_map.get("centercode")
            or elements_map.get("ingestionstatus")
            or elements_map.get("reportacknowledgementcode")
            or elements_map.get("ack_code")
            or ""
        ).upper()

        is_accepted = False
        ack_status = AckStatus.REJECTED

        if status_code in (
            "01",
            "AA",
            "CA",
            "ACCEPTED",
            "INGESTED",
            "LOADED",
            "SUCCESS",
        ):
            is_accepted = True
            ack_status = (
                AckStatus.ACCEPTED_WITH_WARNINGS if warning_list else AckStatus.ACCEPTED
            )
            error_list = []
        elif status_code in ("02", "AE", "WARNING", "ACCEPTED_WITH_WARNINGS"):
            is_accepted = True
            ack_status = AckStatus.ACCEPTED_WITH_WARNINGS
            error_list = []
        elif status_code in (
            "03",
            "AR",
            "CR",
            "REJECTED",
            "DUPLICATE",
            "MEDICAL_REJECTION",
        ):
            is_accepted = False
            ack_status = AckStatus.REJECTED
        else:
            if not error_list and "reject" not in xml_content.lower():
                is_accepted = True
                ack_status = (
                    AckStatus.ACCEPTED_WITH_WARNINGS
                    if warning_list
                    else AckStatus.ACCEPTED
                )
                error_list = []
            else:
                is_accepted = False
                ack_status = AckStatus.REJECTED

        return RegulatoryAckParseResult(
            ack_type=AckType.ACK_C,
            ack_status=ack_status,
            is_accepted=is_accepted,
            authority_receipt_id=receipt_id,
            worldwide_unique_case_id=case_id,
            message_id=message_id,
            authority_status_code=status_code,
            errors=error_list,
            warnings=warning_list,
            details=elements_map,
        )

    @classmethod
    def parse_generic_ack(
        cls, content: str, ack_type: AckType = AckType.ACK_B
    ) -> RegulatoryAckParseResult:
        """Fallback generic acknowledgement parser."""
        if content.strip().startswith("<"):
            return cls.parse_ack_b(content)

        # Plain text
        is_accepted = "success" in content.lower() or "accepted" in content.lower()
        return RegulatoryAckParseResult(
            ack_type=ack_type,
            ack_status=AckStatus.ACCEPTED if is_accepted else AckStatus.REJECTED,
            is_accepted=is_accepted,
            details={"raw_text": content},
        )

    @classmethod
    def generate_mock_ack_b(
        cls,
        message_id: str,
        worldwide_unique_case_id: str,
        is_accepted: bool = True,
        error_message: str | None = None,
        sender_org: str = "FDA-ESG",
        receiver_org: str = "CADENCE-SPONSOR",
    ) -> str:
        """Generate a standard ICH E2B(R3) ACK B XML payload for test simulation.

        Args:
            message_id: Original transmission Message-ID.
            worldwide_unique_case_id: Target ICSR worldwide unique case ID.
            is_accepted: True for technical accept (01/AA), False for syntax error (02/AE).
            error_message: Error description if is_accepted is False.
            sender_org: Gateway sender organisation.
            receiver_org: Sponsor receiver organisation.

        Returns:
            E2B(R3) ACK XML string.
        """
        ack_code = "01" if is_accepted else "02"
        ack_text = (
            "Accepted by technical syntactic parser"
            if is_accepted
            else (error_message or "XML Schema Validation Error")
        )
        now_iso = datetime.now(UTC).strftime("%Y%m%d%H%M%S")
        clean_msg_id = message_id.strip("<>")

        return f"""<?xml version="1.0" encoding="UTF-8"?>
<ichicsrack xmlns="urn:hl7-org:v3">
    <ichicsrmessageheader>
        <messagetype>ICHICSRAck</messagetype>
        <messageid>{clean_msg_id}</messageid>
        <messagesenderidentifier>{sender_org}</messagesenderidentifier>
        <messagereceiveridentifier>{receiver_org}</messagereceiveridentifier>
        <messagedate>{now_iso}</messagedate>
    </ichicsrmessageheader>
    <acknowledgement>
        <transmissionacknowledgement>
            <transmissionacknowledgementcode>{ack_code}</transmissionacknowledgementcode>
            <parsingerrordescription>{ack_text}</parsingerrordescription>
        </transmissionacknowledgement>
        <reportacknowledgement>
            <safetyreportid>{worldwide_unique_case_id}</safetyreportid>
            <localreportnumber>LOC-{worldwide_unique_case_id}</localreportnumber>
            <reportacknowledgementcode>{ack_code}</reportacknowledgementcode>
            <errormessagecomment>{ack_text}</errormessagecomment>
        </reportacknowledgement>
    </acknowledgement>
</ichicsrack>"""

    @classmethod
    def generate_mock_ack_c(
        cls,
        message_id: str,
        worldwide_unique_case_id: str,
        is_accepted: bool = True,
        authority_tracking_number: str | None = None,
        warning_message: str | None = None,
        error_message: str | None = None,
        authority: str = "US FDA CDER / CBER",
    ) -> str:
        """Generate a standard Regulatory Authority Center Acceptance (ACK C) XML for test simulation.

        Args:
            message_id: Original transmission Message-ID.
            worldwide_unique_case_id: Target ICSR worldwide unique case ID.
            is_accepted: True for authority acceptance (AA/01), False for medical reject (AR/03).
            authority_tracking_number: CTN / EudraVigilance receipt ticket number.
            warning_message: Optional warning notice (e.g. MedDRA auto-mapping).
            error_message: Optional rejection reason if is_accepted is False.
            authority: Regulatory center name.

        Returns:
            Center Acceptance ACK XML string.
        """
        ack_code = "AA" if is_accepted else "AR"
        ctn = (
            authority_tracking_number
            or f"CTN-2026-{datetime.now(UTC).strftime('%Y%m%d%H%M%S')}"
        )
        now_iso = datetime.now(UTC).isoformat()
        clean_msg_id = message_id.strip("<>")

        xml = f"""<?xml version="1.0" encoding="UTF-8"?>
<center_acceptance_acknowledgement xmlns="http://fda.gov/esg/ack_c">
    <authority_name>{authority}</authority_name>
    <centertrackingnumber>{ctn}</centertrackingnumber>
    <message_id>{clean_msg_id}</message_id>
    <worldwide_unique_case_id>{worldwide_unique_case_id}</worldwide_unique_case_id>
    <authorityacceptancecode>{ack_code}</authorityacceptancecode>
    <ingestion_timestamp>{now_iso}</ingestion_timestamp>
    <ingestion_status>{"LOADED_SUCCESSFULLY" if is_accepted else "REJECTED_BY_CENTER"}</ingestion_status>"""

        if warning_message:
            xml += f"\n    <warningmessage>{warning_message}</warningmessage>"
        if error_message:
            xml += f"\n    <rejectionreason>{error_message}</rejectionreason>"

        xml += "\n</center_acceptance_acknowledgement>"
        return xml
