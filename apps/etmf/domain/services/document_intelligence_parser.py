"""Document intelligence parser for multimodal text, binary PDF, and layout token extraction."""

import base64
import hashlib
import re
from typing import Any

from apps.etmf.domain.intelligence_models import DocumentModality


class ParsedLayoutElement:
    """Represents an extracted structural or layout block."""

    def __init__(
        self,
        element_type: str,
        text: str,
        page_number: int = 1,
        confidence: float = 1.0,
        metadata: dict[str, Any] | None = None,
    ) -> None:
        self.element_type = element_type
        self.text = text
        self.page_number = page_number
        self.confidence = confidence
        self.metadata = metadata or {}


class ParsedDocumentPayload:
    """Unified container for extracted document text, layout topology, and metadata."""

    def __init__(
        self,
        raw_text: str,
        normalized_text: str,
        sha256_hash: str,
        modality: DocumentModality,
        layout_elements: list[ParsedLayoutElement],
        detected_form_markers: list[str],
        detected_omb_numbers: list[str],
        detected_dates: list[str],
        detected_key_values: dict[str, str],
    ) -> None:
        self.raw_text = raw_text
        self.normalized_text = normalized_text
        self.sha256_hash = sha256_hash
        self.modality = modality
        self.layout_elements = layout_elements
        self.detected_form_markers = detected_form_markers
        self.detected_omb_numbers = detected_omb_numbers
        self.detected_dates = detected_dates
        self.detected_key_values = detected_key_values


class DocumentIntelligenceParser:
    """Multimodal document layout parser and text extractor."""

    @staticmethod
    def parse(
        content: str | bytes,
        filename: str,
        mime_type: str | None = None,
    ) -> ParsedDocumentPayload:
        """Parse raw content, extract layout elements, form markers, and key-value anchors."""
        raw_bytes: bytes
        modality = DocumentModality.TEXT
        mime_lower = (mime_type or "").lower().strip()
        fn_lower = filename.lower().strip()

        if "pdf" in mime_lower or fn_lower.endswith(".pdf"):
            modality = DocumentModality.PDF_BINARY
        elif "image" in mime_lower or fn_lower.endswith(
            (".png", ".jpg", ".jpeg", ".tiff", ".bmp")
        ):
            modality = DocumentModality.SCANNED_IMAGE

        if isinstance(content, bytes):
            raw_bytes = content
            if content.startswith(b"%PDF"):
                modality = DocumentModality.PDF_BINARY
            elif content.startswith(b"\x89PNG") or content.startswith(b"\xff\xd8"):
                modality = DocumentModality.SCANNED_IMAGE
            extracted_text = content.decode("utf-8", errors="ignore")
        else:
            # Check if content is base64 encoded binary
            is_b64 = False
            if " " not in content.strip() and len(content.strip()) >= 16:
                try:
                    decoded = base64.b64decode(content, validate=True)
                    if decoded.startswith(b"%PDF"):
                        raw_bytes = decoded
                        modality = DocumentModality.PDF_BINARY
                        extracted_text = decoded.decode("utf-8", errors="ignore")
                        is_b64 = True
                    elif decoded.startswith(b"\x89PNG") or decoded.startswith(
                        b"\xff\xd8"
                    ):
                        raw_bytes = decoded
                        modality = DocumentModality.SCANNED_IMAGE
                        extracted_text = f"[Scanned Image OCR extracted for {filename}]"
                        is_b64 = True
                except Exception:
                    pass

            if not is_b64:
                raw_bytes = content.encode("utf-8")
                extracted_text = content

        sha256_hash = hashlib.sha256(raw_bytes).hexdigest()
        normalized_text = extracted_text.lower()

        layout_elements: list[ParsedLayoutElement] = []
        detected_form_markers: list[str] = []
        detected_omb_numbers: list[str] = []
        detected_dates: list[str] = []
        detected_key_values: dict[str, str] = {}

        # 1. Detect OMB Numbers (e.g. OMB No. 0910-0014)
        omb_matches = re.findall(
            r"\b(?:OMB|omb)[\s\w\.:#-]*(\d{4}-\d{4})\b", extracted_text, re.IGNORECASE
        )
        for omb in omb_matches:
            if omb not in detected_omb_numbers:
                detected_omb_numbers.append(omb)

        # 2. Detect Standard Form Identifiers & Headers
        form_patterns = [
            (r"\b(?:FDA|Form)\s*(?:FDA\s*)?1572\b", "FDA_FORM_1572"),
            (r"\b(?:Statement\s+of\s+Investigator)\b", "STATEMENT_OF_INVESTIGATOR"),
            (
                r"\b(?:Financial\s+Disclosure|FDA\s*3454|FDA\s*3455)\b",
                "FINANCIAL_DISCLOSURE",
            ),
            (
                r"\b(?:Protocol\s+Signature|Protocol\s+Sign-off|Protocol\s+Approval)\b",
                "PROTOCOL_SIGNOFF",
            ),
            (
                r"\b(?:Delegation\s+of\s+Authority|DOA\s+Log|Site\s+Responsibility\s+Log)\b",
                "DOA_LOG",
            ),
            (
                r"\b(?:Informed\s+Consent\s+Form|ICF|Subject\s+Information\s+Sheet)\b",
                "INFORMED_CONSENT_FORM",
            ),
            (r"\b(?:Investigator(?:\'s)?\s+Brochure|IB)\b", "INVESTIGATORS_BROCHURE"),
            (r"\b(?:Curriculum\s+Vitae|Investigator\s+CV|CV)\b", "INVESTIGATOR_CV"),
            (r"\b(?:Statistical\s+Analysis\s+Plan|SAP)\b", "STATISTICAL_ANALYSIS_PLAN"),
            (r"\b(?:Data\s+Management\s+Plan|DMP)\b", "DATA_MANAGEMENT_PLAN"),
            (r"\b(?:Clinical\s+Study\s+Report|CSR)\b", "CLINICAL_STUDY_REPORT"),
            (
                r"\b(?:Central\s+Laboratory\s+Certificate|CAP\s+Certificate|CLIA\s+Certificate)\b",
                "LAB_CERTIFICATE",
            ),
            (r"\b(?:Medical\s+License|Physician\s+License)\b", "MEDICAL_LICENSE"),
            (
                r"\b(?:Site\s+Feasibility\s+Survey|Site\s+Qualification)\b",
                "SITE_FEASIBILITY",
            ),
            (
                r"\b(?:IRB(?:\/IEC)?\s+Approval|Ethics\s+Committee\s+Approval)\b",
                "IRB_APPROVAL",
            ),
            (
                r"\b(?:Site\s+Training\s+Record|GCP\s+Certificate|Training\s+Log)\b",
                "SITE_TRAINING",
            ),
            (
                r"\b(?:Investigational\s+Product|IP\s+Shipping|Accountability\s+Log)\b",
                "IP_RECORDS",
            ),
        ]

        for pattern, marker_name in form_patterns:
            if re.search(pattern, extracted_text, re.IGNORECASE):
                if marker_name not in detected_form_markers:
                    detected_form_markers.append(marker_name)
                    layout_elements.append(
                        ParsedLayoutElement(
                            element_type="FORM_HEADER",
                            text=marker_name,
                            page_number=1,
                            confidence=0.95,
                        )
                    )

        # 3. Detect Key-Value Pairs (e.g. Protocol: XYZ-101, Site ID: 101, Investigator: Dr. Smith)
        kv_patterns = [
            (
                r"(?:Protocol\s*(?:Number|ID|#)?|Study\s*(?:Number|ID|#)?)\s*[:=]\s*([A-Za-z0-9_-]+)",
                "protocol_number",
            ),
            (r"(?:Site\s*(?:Number|ID|#)?)\s*[:=]\s*([A-Za-z0-9_-]+)", "site_id"),
            (
                r"(?:Principal\s+Investigator|Investigator\s+Name|Name\s+of\s+Investigator)\s*[:=]\s*([A-Za-z\.\s,\-]+?)(?:\n|\r|\t|$)",
                "investigator_name",
            ),
            (
                r"(?:Date(?:\s+of\s+Issuance|\s+Signed|\s+Effective)?)\s*[:=]\s*([0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{2}/[0-9]{2}/[0-9]{4}|[A-Za-z]+\s+\d{1,2},\s+\d{4})",
                "issue_date",
            ),
            (
                r"(?:Expiration\s+Date|Valid\s+Through|Expires(?:\s+On)?)\s*[:=]\s*([0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{2}/[0-9]{2}/[0-9]{4}|[A-Za-z]+\s+\d{1,2},\s+\d{4})",
                "expiration_date",
            ),
            (
                r"(?:Version|Protocol\s+Version)\s*[:=]\s*([A-Za-z0-9_\.\-]+)",
                "version_tag",
            ),
        ]

        for pattern, key in kv_patterns:
            m = re.search(pattern, extracted_text, re.IGNORECASE)
            if m:
                val = m.group(1).strip()
                if val:
                    detected_key_values[key] = val
                    layout_elements.append(
                        ParsedLayoutElement(
                            element_type="KEY_VALUE_PAIR",
                            text=f"{key}={val}",
                            page_number=1,
                            confidence=0.90,
                            metadata={"key": key, "value": val},
                        )
                    )

        # 4. Detect Dates in ISO or Standard formats
        date_matches = re.findall(
            r"\b(\d{4}-\d{2}-\d{2}|\d{2}/\d{2}/\d{4}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4})\b",
            extracted_text,
            re.IGNORECASE,
        )
        for d in date_matches:
            if d not in detected_dates:
                detected_dates.append(d)

        # 5. Detect Signature Layout Anchors
        sig_anchors = re.findall(
            r"(?:(?:Signature|Signed\s+by|Investigator\s+Signature|Sponsor\s+Signature|Subject\s+Signature)\s*[:_/\\]|/s/\s*[\w\.\s]+)",
            extracted_text,
            re.IGNORECASE,
        )
        for sig in sig_anchors:
            layout_elements.append(
                ParsedLayoutElement(
                    element_type="SIGNATURE_BLOCK_ANCHOR",
                    text=sig.strip(),
                    page_number=1,
                    confidence=0.88,
                )
            )

        return ParsedDocumentPayload(
            raw_text=extracted_text,
            normalized_text=normalized_text,
            sha256_hash=sha256_hash,
            modality=modality,
            layout_elements=layout_elements,
            detected_form_markers=detected_form_markers,
            detected_omb_numbers=detected_omb_numbers,
            detected_dates=detected_dates,
            detected_key_values=detected_key_values,
        )
