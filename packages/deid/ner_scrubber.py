"""Named Entity Recognition (NER) and regex scrubber for HIPAA 18 PHI identifier detection and redaction.

Requirements: PRD-SYS-001
"""

from typing import Any

import packages  # noqa: F401


class PHINameEntityScrubber:
    """Scrubber service detecting and masking Protected Health Information (PHI) identifiers in text.

    Requirements: PRD-SYS-001
    """

    # HIPAA 18 PHI regex pattern rules (kept for backwards compatibility metadata)
    _PATTERNS = {
        "SSN": r"\b\d{3}-\d{2}-\d{4}\b",
        "EMAIL": r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
        "PHONE": r"\b(?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}\b",
        "MRN": r"\bMRN[:#\s]*\d{6,10}\b",
        "DOB": r"\bDOB[:#\s]*\d{2}/\d{2}/\d{4}\b|\bDOB[:#\s]*\d{4}-\d{2}-\d{2}\b",
    }

    def detect_phi(
        self, text: str, custom_terms: list[str] | None = None
    ) -> list[dict[str, Any]]:
        """Detect PHI entities in input text.

        Args:
            text: Input document or clinical note text.
            custom_terms: Optional list of custom terms/names to redact.

        Returns:
            List of detected PHI entity dicts containing type, text, start, and end indices.
        """
        from packages.deid.detector import DeidDetector
        from packages.deid.models import ComplianceProfile, DetectorCategory

        legacy_type_map = {
            DetectorCategory.EMAIL: "EMAIL",
            DetectorCategory.TELEPHONE_FAX: "PHONE",
            DetectorCategory.SSN_NATIONAL_ID: "SSN",
            DetectorCategory.DATES: "DOB",
            DetectorCategory.MEDICAL_RECORD_ACCOUNT: "MRN",
            DetectorCategory.ZIP_GEOGRAPHIC: "ZIP_GEOGRAPHIC",
            DetectorCategory.URLS: "URLS",
            DetectorCategory.IP_MAC_ADDRESSES: "IP_MAC_ADDRESSES",
            DetectorCategory.AGE: "AGE",
            DetectorCategory.CUSTOM: "CUSTOM",
            DetectorCategory.HEALTH_PLAN_BENEFICIARY: "HEALTH_PLAN_BENEFICIARY",
            DetectorCategory.CERTIFICATE_LICENSE: "CERTIFICATE_LICENSE",
            DetectorCategory.VEHICLE_IDENTIFIERS: "VEHICLE_IDENTIFIERS",
            DetectorCategory.DEVICE_SERIAL: "DEVICE_SERIAL",
        }

        detector = DeidDetector()
        results = detector.detect(
            text, profile=ComplianceProfile.HIPAA, custom_terms=custom_terms
        )

        entities: list[dict[str, Any]] = []
        for r in results:
            etype = legacy_type_map.get(r.category, r.category.upper())
            entities.append(
                {
                    "entity_type": etype,
                    "text": r.value,
                    "start_char": r.start,
                    "end_char": r.end,
                    "confidence": 0.99,
                }
            )
        return entities

    def scrub_phi(
        self,
        text: str,
        replacement_template: str = "[REDACTED_{type}]",
        custom_terms: list[str] | None = None,
        custom_replacement: str | None = None,
    ) -> str:
        """Replace all detected PHI entities in text with redaction placeholder tags.

        Args:
            text: Input text string.
            replacement_template: Template string for redaction banner.
            custom_terms: Optional list of custom terms/names to redact.
            custom_replacement: Optional exact replacement string to use for CUSTOM/NAME entities.

        Returns:
            Anonymized text with PHI scrubbed out.
        """
        entities = self.detect_phi(text, custom_terms=custom_terms)
        if not entities:
            return text

        scrubbed = text
        # Replace backwards from end to preserve indices (right-to-left reverse slice substitution)
        for entity in reversed(entities):
            start = entity["start_char"]
            end = entity["end_char"]
            etype = entity["entity_type"]
            if etype == "CUSTOM" and custom_replacement is not None:
                replacement = custom_replacement
            else:
                replacement = replacement_template.format(type=etype)
            scrubbed = scrubbed[:start] + replacement + scrubbed[end:]

        return scrubbed
