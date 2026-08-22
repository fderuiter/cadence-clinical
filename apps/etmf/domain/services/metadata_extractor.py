"""Regulatory metadata extraction service for clinical documents."""

import re
from datetime import date, datetime

from apps.etmf.domain.intelligence_models import ExtractedRegulatoryMetadata
from apps.etmf.domain.services.document_intelligence_parser import (
    ParsedDocumentPayload,
)
from packages.deid.detector import DeidDetector
from packages.deid.models import ComplianceProfile


def parse_date_safely(date_str: str | None) -> date | None:
    """Parse date string into date object supporting multiple formats."""
    if not date_str:
        return None
    cleaned = date_str.strip()
    formats = [
        "%Y-%m-%d",
        "%m/%d/%Y",
        "%d/%m/%Y",
        "%B %d, %Y",
        "%b %d, %Y",
        "%d-%b-%Y",
        "%d %b %Y",
    ]
    for fmt in formats:
        try:
            return datetime.strptime(cleaned, fmt).date()
        except ValueError:
            continue
    # Try ISO regex substring match
    m = re.search(r"(\d{4})-(\d{2})-(\d{2})", cleaned)
    if m:
        try:
            return date(int(m.group(1)), int(m.group(2)), int(m.group(3)))
        except ValueError:
            pass
    return None


class RegulatoryMetadataExtractor:
    """Extracts regulatory and trial metadata entities from parsed document content."""

    def __init__(self) -> None:
        self.deid_detector = DeidDetector()

    def extract(
        self,
        parsed_doc: ParsedDocumentPayload,
        study_id_hint: str | None = None,
        site_id_hint: str | None = None,
    ) -> ExtractedRegulatoryMetadata:
        """Extract protocol, site, investigator, date, and form metadata.

        Args:
            parsed_doc: ParsedDocumentPayload containing text and layout cues.
            study_id_hint: Optional study ID hint from request context.
            site_id_hint: Optional site ID hint from request context.

        Returns:
            ExtractedRegulatoryMetadata object.
        """
        kvs = parsed_doc.detected_key_values

        protocol_number = kvs.get("protocol_number")
        if not protocol_number and study_id_hint:
            protocol_number = study_id_hint

        study_id = study_id_hint or protocol_number
        site_id = kvs.get("site_id") or site_id_hint
        investigator_name = kvs.get("investigator_name")

        # Parse issue and expiration dates
        issue_date_raw = kvs.get("issue_date")
        if not issue_date_raw and parsed_doc.detected_dates:
            issue_date_raw = parsed_doc.detected_dates[0]
        issue_date = parse_date_safely(issue_date_raw)

        expiration_date_raw = kvs.get("expiration_date")
        if not expiration_date_raw and len(parsed_doc.detected_dates) > 1:
            # Check if second date is after first date
            d2 = parse_date_safely(parsed_doc.detected_dates[1])
            expiration_date = d2 if d2 and issue_date and d2 > issue_date else None
        else:
            expiration_date = parse_date_safely(expiration_date_raw)

        form_identifier = None
        if parsed_doc.detected_omb_numbers:
            form_identifier = f"OMB-{parsed_doc.detected_omb_numbers[0]}"
        elif parsed_doc.detected_form_markers:
            form_identifier = parsed_doc.detected_form_markers[0]

        version_tag = kvs.get("version_tag")

        # Scan for PII/PHI detections
        phi_detected = False
        try:
            detections = self.deid_detector.detect(
                parsed_doc.raw_text, profile=ComplianceProfile.HIPAA
            )
            phi_detected = len(detections) > 0
        except Exception:
            phi_detected = False

        return ExtractedRegulatoryMetadata(
            protocol_number=protocol_number,
            study_id=study_id,
            site_id=site_id,
            investigator_name=investigator_name,
            issue_date=issue_date,
            expiration_date=expiration_date,
            form_identifier=form_identifier,
            version_tag=version_tag,
            raw_extracted_fields=dict(kvs),
            phi_pii_detected=phi_detected,
        )
