import re
from collections.abc import Callable

from packages.deid.models import (
    PROFILE_CATEGORIES,
    ComplianceProfile,
    DetectionResult,
    DetectorCategory,
)

# Curated, compiled regular expressions for PII/PHI categories
# Use lookbehind assertions to preserve and redact leading non-word symbols like + or ( in phone/email
EMAIL_REGEX = re.compile(r"(?<!\w)[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b")

# Robust telephone and fax number regex (matches standard formats without capturing dates)
PHONE_FAX_REGEX = re.compile(
    r"(?<!\w)(?:\+(?:[-.\s]*1)?[-.\s]*\(?\d{3}\)?|\(?\d{3}\)?)[-.\s]*\d{3}[-.\s]*\d{4}\b"
    r"|(?<!\w)(?:\(?\d{3}\)?)[-.\s]*\d{4}\b"
    r"|(?:\+\d{1,3}[-.\s]*)?\(?\d{2,4}\)?[-.\s]*\d{3,4}[-.\s]*\d{4}\b"
    r"|\b\d{3}[-.\s]*\d{4}\b"
)

HEALTH_PLAN_BENEFICIARY_REGEX = re.compile(
    r"\b(?:HICN|MBI|Medicare|Beneficiary|Health Plan|Policy(?: Number)?|Plan ID)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,15}\b",
    re.IGNORECASE,
)

CERTIFICATE_LICENSE_REGEX = re.compile(
    r"\b(?:License|Certificate|Cert|Lic|Medical License|State License|Driver License)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,15}\b",
    re.IGNORECASE,
)

VEHICLE_IDENTIFIERS_REGEX = re.compile(
    r"\b(?:VIN|Vehicle ID|License Plate|Plate Number|Engine Number)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,17}\b",
    re.IGNORECASE,
)

DEVICE_SERIAL_REGEX = re.compile(
    r"\b(?:Device ID|Serial Number|Serial|Device Serial|Model Number|UDI)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,20}\b",
    re.IGNORECASE,
)

# Social security numbers and simple national ID formats
SSN_NATIONAL_ID_REGEX = re.compile(
    r"\b\d{3}-\d{2}-\d{4}\b|\b[A-CEGHJ-PR-TW-Z][A-CEGHJ-NPR-TW-Z]\s*\d{2}\s*\d{2}\s*\d{2}\s*[A-D]\b",
    re.IGNORECASE,
)

# Standard clinical date patterns
DATE_PATTERNS = [
    # YYYY-MM-DD or YYYY/MM/DD
    re.compile(r"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b"),
    # MM/DD/YYYY or DD/MM/YYYY or MM-DD-YYYY or DD-MM-YYYY or short years (MM/DD/YY)
    re.compile(r"\b\d{1,2}[-/]\d{1,2}[-/]\d{2,4}\b"),
    # Textual dates: 15-Jan-2026, 15 Jan 2026, Jan 15 2026
    re.compile(
        r"\b\d{1,2}[-\s](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-zA-Z]*[-\s]\d{2,4}\b",
        re.IGNORECASE,
    ),
    re.compile(
        r"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-zA-Z]*\s+\d{1,2}(?:st|nd|rd|th)?[\s,]+\d{2,4}\b",
        re.IGNORECASE,
    ),
]

# ZIP and common postal/geographic code formats
ZIP_GEOGRAPHIC_REGEX = re.compile(
    r"\b\d{5}(?:-\d{4})?\b|\b[A-Za-z]\d[A-Za-z][ -]?\d[A-Za-z]\d\b|\b[A-Za-z]{1,2}\d[A-Za-z\d]?\s*\d[A-Za-z]{2}\b"
)

# Standard Universal Resource Locators
URLS_REGEX = re.compile(
    r"\b(?:https?://|www\.)[a-zA-Z0-9-._~:/?#\[\]@!$&'()*+,;=]+\b", re.IGNORECASE
)

# IP (IPv4 and IPv6) and MAC address regex
IP_MAC_REGEX = re.compile(
    # IPv4
    r"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b"
    # MAC
    r"|\b(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}\b"
    # IPv6 (comprehensive, matching full/compressed without splits or overlapping MACs)
    # Order matters: longest/most-specific matches are prioritized
    r"|\b(?:[0-9a-fA-F]{1,4}:)+(?::[0-9a-fA-F]{1,4})+\b"
    r"|\b(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}\b"
    r"|\b::(?:[0-9a-fA-F]{1,4}:){0,6}[0-9a-fA-F]{1,4}\b"
    r"|\b(?:[0-9a-fA-F]{1,4}:){1,7}:\b"
)

# Medical record numbers, EHR numbers, NHS numbers, and general patient/account IDs
# Requires the identifier sequence to contain at least one digit to avoid matching general words
MEDICAL_RECORD_ACCOUNT_REGEX = re.compile(
    r"\b(?:MRN|EHR|ACC|NHS)[-:#\s]*[a-zA-Z0-9-]*\d[a-zA-Z0-9-]*\b"
    r"|\b\d{3}[-\s]?\d{3}[-\s]?\d{4}\b",
    re.IGNORECASE,
)

# Standard age patterns matching "age: 95", "age 92", "aged 84", "91 years old", "93-year-old", "91yo", "91-yo", etc.
AGE_REGEX = re.compile(
    r"\b(?:age[sd]?|age of)\s*[:\- ]?\s*\d{1,3}\b"
    r"|\b\d{1,3}\s*(?:years?\s*(?:of\s*age)?\s*old|-years?-old|yo|-yo)\b",
    re.IGNORECASE,
)


def resolve_overlaps(
    results: list[DetectionResult], text: str = ""
) -> list[DetectionResult]:
    """
    Resolve overlapping detection results deterministically.
    Longer/wider matches are prioritized. For overlapping intervals, the match
    retained is determined by:
      1. start offset ascending
      2. end offset descending (wider/longer intervals processed first)
      3. category name alphabetically
      4. value length descending
    """
    sorted_results = sorted(
        results, key=lambda x: (x.start, -x.end, x.category, -len(x.value))
    )

    resolved: list[DetectionResult] = []
    for res in sorted_results:
        overlaps = False
        for accepted in resolved:
            # Intervals [start, end) overlap if max(start1, start2) < min(end1, end2)
            if max(res.start, accepted.start) < min(res.end, accepted.end):
                overlaps = True
                break
        if not overlaps:
            resolved.append(res)

    # Sort resolved back by start offset ascending for caller convenience
    return sorted(resolved, key=lambda x: x.start)


def redact_text(
    text: str,
    results: list[DetectionResult],
    placeholder_func: Callable[[DetectionResult], str] | None = None,
) -> str:
    """
    Redact identified terms in the source text using sequential right-to-left
    character slicing.

    If placeholder_func is provided, it is invoked to compute the placeholder
    for each result. Otherwise, defaults to f"[{result.category.upper()}]".
    """
    # Resolve overlaps to ensure safety and determinism
    clean_results = resolve_overlaps(results, text)

    parts = list(text)
    # Process from right-to-left so offsets remain valid during substitution
    for res in reversed(clean_results):
        replacement = (
            placeholder_func(res) if placeholder_func else f"[{res.category.upper()}]"
        )
        parts[res.start : res.end] = list(replacement)
    return "".join(parts)


class DeidDetector:
    """
    Pure-Python detection layer for structured PII/PHI matching in clinical document text.
    """

    def __init__(self) -> None:
        pass

    def detect(
        self,
        text: str,
        profile: ComplianceProfile = ComplianceProfile.HIPAA,
        custom_terms: list[str] | None = None,
    ) -> list[DetectionResult]:
        """
        Scan text for PII/PHI candidates based on compliance profile and custom terms.

        Args:
            text (str): Source text to scan.
            profile (ComplianceProfile): Compliance profile determining active categories.
            custom_terms (Optional[List[str]]): Additional literal terms (e.g. names/initials) to detect.

        Returns:
            List[DetectionResult]: Resolved, non-overlapping structured detection results.
        """
        if not text:
            return []

        active_categories = PROFILE_CATEGORIES.get(profile, set())
        candidates: list[DetectionResult] = []

        # 1. Regex scanning for standard categories
        if DetectorCategory.EMAIL in active_categories:
            for m in EMAIL_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.EMAIL,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.TELEPHONE_FAX in active_categories:
            for m in PHONE_FAX_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.TELEPHONE_FAX,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.SSN_NATIONAL_ID in active_categories:
            for m in SSN_NATIONAL_ID_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.SSN_NATIONAL_ID,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.DATES in active_categories:
            for pattern in DATE_PATTERNS:
                for m in pattern.finditer(text):
                    candidates.append(
                        DetectionResult(
                            category=DetectorCategory.DATES,
                            start=m.start(),
                            end=m.end(),
                            value=m.group(),
                        )
                    )

        if DetectorCategory.ZIP_GEOGRAPHIC in active_categories:
            for m in ZIP_GEOGRAPHIC_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.ZIP_GEOGRAPHIC,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.URLS in active_categories:
            for m in URLS_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.URLS,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.IP_MAC_ADDRESSES in active_categories:
            for m in IP_MAC_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.IP_MAC_ADDRESSES,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.MEDICAL_RECORD_ACCOUNT in active_categories:
            for m in MEDICAL_RECORD_ACCOUNT_REGEX.finditer(text):
                val = m.group()
                # If it's a 10-digit/phone-like format, check for a valid prefix in preceding text
                if re.match(r"^\d{3}[-.\s]?\d{3}[-.\s]?\d{4}$", val):
                    preceding = text[max(0, m.start() - 25) : m.start()].lower()
                    if not any(
                        p in preceding for p in ("nhs", "acc", "record", "mrn", "ehr")
                    ):
                        # Skip matching as medical_record_account, since it's likely a phone number
                        continue
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.MEDICAL_RECORD_ACCOUNT,
                        start=m.start(),
                        end=m.end(),
                        value=val,
                    )
                )

        if DetectorCategory.AGE in active_categories:
            for m in AGE_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.AGE,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.HEALTH_PLAN_BENEFICIARY in active_categories:
            for m in HEALTH_PLAN_BENEFICIARY_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.HEALTH_PLAN_BENEFICIARY,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.CERTIFICATE_LICENSE in active_categories:
            for m in CERTIFICATE_LICENSE_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.CERTIFICATE_LICENSE,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.VEHICLE_IDENTIFIERS in active_categories:
            for m in VEHICLE_IDENTIFIERS_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.VEHICLE_IDENTIFIERS,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        if DetectorCategory.DEVICE_SERIAL in active_categories:
            for m in DEVICE_SERIAL_REGEX.finditer(text):
                candidates.append(
                    DetectionResult(
                        category=DetectorCategory.DEVICE_SERIAL,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

        # 2. Scanning for custom/literal terms
        if DetectorCategory.CUSTOM in active_categories and custom_terms:
            valid_terms = [t for t in custom_terms if t and t.strip()]
            if valid_terms:
                # Sort descending to match longer strings first
                valid_terms.sort(key=len, reverse=True)
                escaped_terms = [re.escape(term) for term in valid_terms]
                patterns = []
                for term in escaped_terms:
                    raw_term = valid_terms[escaped_terms.index(term)]
                    # Only enforce word boundaries for short alphanumeric terms/initials of length <= 3
                    if len(raw_term) <= 3 and re.match(r"^\w+$", raw_term):
                        start_b = r"\b"
                        end_b = r"\b"
                    else:
                        start_b = ""
                        end_b = ""
                    patterns.append(f"{start_b}{term}{end_b}")

                custom_regex = re.compile("|".join(patterns), re.IGNORECASE)
                for m in custom_regex.finditer(text):
                    candidates.append(
                        DetectionResult(
                            category=DetectorCategory.CUSTOM,
                            start=m.start(),
                            end=m.end(),
                            value=m.group(),
                        )
                    )

        # Redact trailing hyphens, slashes, or special punctuation that form part of formatted clinical identifiers
        clinical_categories = {
            DetectorCategory.MEDICAL_RECORD_ACCOUNT,
            DetectorCategory.SSN_NATIONAL_ID,
            DetectorCategory.HEALTH_PLAN_BENEFICIARY,
            DetectorCategory.CERTIFICATE_LICENSE,
            DetectorCategory.VEHICLE_IDENTIFIERS,
            DetectorCategory.DEVICE_SERIAL,
        }
        for cand in candidates:
            if cand.category in clinical_categories:
                new_end = cand.end
                while new_end < len(text) and text[new_end] in ("-", "/", "\\"):
                    new_end += 1
                if new_end > cand.end:
                    cand.end = new_end
                    cand.value = text[cand.start : cand.end]

        # 3. Resolve overlaps deterministically
        return resolve_overlaps(candidates)
