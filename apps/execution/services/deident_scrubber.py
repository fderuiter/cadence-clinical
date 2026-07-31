"""De-identification scrubber engine for HIPAA compliance.

Requirements: PRD-SYS-001
"""

import hashlib
import hmac
import re
from datetime import timedelta
from typing import Any, Dict, List, Tuple

from dateutil import parser as date_parser
from sdtm.scrubber_models import DeidentConfig, DeidentSummary

# Regex patterns for free-text PII filtering
SSN_PATTERN = re.compile(r"\b\d{3}-\d{2}-\d{4}\b")
PHONE_PATTERN = re.compile(r"\b(?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}\b")
EMAIL_PATTERN = re.compile(r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")
ZIP_PATTERN = re.compile(r"\b\d{5}(?:-\d{4})?\b")
ADDRESS_PATTERN = re.compile(
    r"\b\d+\s+[A-Za-z0-9\s.,]+?\s+(?:Street|St|Avenue|Ave|Road|Rd|Drive|Dr|Boulevard|Blvd|Lane|Ln|Way|Court|Ct|Circle|Cir|Box|PO\s+Box)\b",
    re.IGNORECASE,
)

SDTM_DATE_FIELDS = {
    "RFSTDTC",
    "RFENDTC",
    "BRTHDTC",
    "AELDTC",
    "AESTDTC",
    "AEENDTC",
    "VSDTC",
    "LBDTC",
    "MHSTDTC",
    "MHENDTC",
    "CMSTDTC",
    "CMENDTC",
}


def shift_partial_date(date_str: str, shift_days: int) -> str:
    """Parses full or partial dates and shifts them while keeping relative ordering

    and leaving imprecise/placeholder components untouched.

    Requirements: PRD-SYS-001
    """
    if not date_str:
        return date_str

    original_str = date_str.strip()
    if not original_str:
        return date_str

    # Split timestamp if present (e.g., T12:00:00)
    t_split = original_str.split("T")
    date_part = t_split[0]
    time_part = "T" + t_split[1] if len(t_split) > 1 else ""

    # Split by common date separators - or /
    parts = re.split(r"([- /])", date_part)

    # We expect a valid year as parts[0] to proceed with structured shifting
    if len(parts) >= 1 and parts[0].isdigit() and len(parts[0]) == 4:
        year_str = parts[0]
        month_str = "06"  # Default mid-year for partial/missing month
        day_str = "15"  # Default mid-month for partial/missing day

        if len(parts) >= 3 and parts[2].isdigit():
            month_str = parts[2]
        if len(parts) >= 5 and parts[4].isdigit():
            day_str = parts[4]

        # Form a full dummy date string for calculation
        dummy_date_str = f"{year_str}-{month_str}-{day_str}"
        try:
            dt = date_parser.parse(dummy_date_str)
            shifted_dt = dt + timedelta(days=shift_days)

            shifted_year = f"{shifted_dt.year:04d}"
            shifted_month = f"{shifted_dt.month:02d}"
            shifted_day = f"{shifted_dt.day:02d}"

            # Reconstruct the original string, inserting shifted numeric parts
            new_parts = list(parts)
            new_parts[0] = shifted_year
            if len(parts) >= 3:
                if parts[2].isdigit():
                    new_parts[2] = shifted_month
            if len(parts) >= 5:
                if parts[4].isdigit():
                    new_parts[4] = shifted_day

            return "".join(new_parts) + time_part
        except Exception:
            pass

    return original_str


class HIPAADataScrubber:
    """HIPAA compliance data scrubber.

    Requirements: PRD-SYS-001
    """

    def __init__(self, study_salt: str):
        self.salt = study_salt.encode("utf-8")

    def get_subject_date_offset(self, subject_id: str) -> int:
        """Compute deterministic date-shift offset in days for subject.

        Requirements: PRD-SYS-001
        """
        h = hmac.new(self.salt, subject_id.encode("utf-8"), hashlib.sha256).hexdigest()
        return (int(h[:8], 16) % 365) - 180

    def pseudonymize_usubjid(self, study_id: str, subject_id: str) -> str:
        """Generate non-reversible pseudonymized USUBJID token.

        Requirements: PRD-SYS-001
        """
        h = hmac.new(
            self.salt, f"{study_id}:{subject_id}".encode("utf-8"), hashlib.sha256
        ).hexdigest()
        return f"{study_id}-{h[:10].upper()}"


def scrub_free_text_pii(text: str) -> str:
    """Scrub free-text for SSN, phone numbers, email addresses, and postal addresses."""
    if not isinstance(text, str):
        return text
    text = SSN_PATTERN.sub("[REDACTED_SSN]", text)
    text = PHONE_PATTERN.sub("[REDACTED_PHONE]", text)
    text = EMAIL_PATTERN.sub("[REDACTED_EMAIL]", text)
    text = ADDRESS_PATTERN.sub("[REDACTED_ADDRESS]", text)
    text = ZIP_PATTERN.sub("[REDACTED_ZIP]", text)
    return text


def scrub_dataset(
    dataset_rows: List[Dict[str, Any]], config: DeidentConfig
) -> Tuple[List[Dict[str, Any]], DeidentSummary]:
    """Applies HIPAA de-identification and pseudonymization onto dataset rows.

    Requirements: PRD-SYS-001
    """
    scrubber = HIPAADataScrubber(study_salt=config.study_salt)
    records_processed = len(dataset_rows)
    fields_pseudonymized = 0
    dates_shifted = 0

    scrubbed_rows = []

    for row in dataset_rows:
        r = dict(row)
        # Determine subject_id for date shifting offset and pseudonymization
        # Use SUBJID if present, else USUBJID, else subject_id
        subject_id = r.get("SUBJID") or r.get("USUBJID") or r.get("subject_id") or ""
        study_id = r.get("STUDYID") or "STUDY"

        offset = 0
        if subject_id and config.enable_date_shift:
            offset = scrubber.get_subject_date_offset(str(subject_id))

        pseudo_id = ""
        if subject_id:
            pseudo_id = scrubber.pseudonymize_usubjid(str(study_id), str(subject_id))

        for k, v in list(r.items()):
            # 1. Pseudonymize USUBJID / SUBJID
            if k in ("USUBJID", "SUBJID") and v is not None and str(v).strip():
                r[k] = pseudo_id
                fields_pseudonymized += 1

            # 2. Date Shifting
            elif (
                (k in SDTM_DATE_FIELDS or k.endswith("DTC") or k.endswith("DT"))
                and v is not None
                and isinstance(v, str)
                and v.strip()
            ):
                if config.enable_date_shift:
                    r[k] = shift_partial_date(v, offset)
                    dates_shifted += 1

            # 3. Free-Text PII Scrubbing
            elif config.scrub_free_text and v is not None and isinstance(v, str):
                r[k] = scrub_free_text_pii(v)

        scrubbed_rows.append(r)

    summary = DeidentSummary(
        records_processed=records_processed,
        fields_pseudonymized=fields_pseudonymized,
        dates_shifted=dates_shifted,
    )

    return scrubbed_rows, summary
