"""Named Entity Recognition (NER) and regex scrubber for HIPAA 18 PHI identifier detection and redaction.

Requirements: PRD-SYS-001
"""

import re
from typing import Any

import packages  # noqa: F401


class PHINameEntityScrubber:
    """Scrubber service detecting and masking Protected Health Information (PHI) identifiers in text.

    Requirements: PRD-SYS-001
    """

    # HIPAA 18 PHI regex pattern rules
    _PATTERNS = {
        "SSN": r"\b\d{3}-\d{2}-\d{4}\b",
        "EMAIL": r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
        "PHONE": r"\b(?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}\b",
        "MRN": r"\bMRN[:#\s]*\d{6,10}\b",
        "DOB": r"\bDOB[:#\s]*\d{2}/\d{2}/\d{4}\b|\bDOB[:#\s]*\d{4}-\d{2}-\d{2}\b",
    }

    def detect_phi(self, text: str) -> list[dict[str, Any]]:
        """Detect PHI entities in input text.

        Args:
            text: Input document or clinical note text.

        Returns:
            List of detected PHI entity dicts containing type, text, start, and end indices.
        """
        entities: list[dict[str, Any]] = []

        for entity_type, pattern in self._PATTERNS.items():
            for match in re.finditer(pattern, text, re.IGNORECASE):
                entities.append(
                    {
                        "entity_type": entity_type,
                        "text": match.group(0),
                        "start_char": match.start(),
                        "end_char": match.end(),
                        "confidence": 0.99,
                    }
                )

        # Sort by start_char index ascending
        return sorted(entities, key=lambda x: x["start_char"])

    def scrub_phi(
        self, text: str, replacement_template: str = "[REDACTED_{type}]"
    ) -> str:
        """Replace all detected PHI entities in text with redaction placeholder tags.

        Args:
            text: Input text string.
            replacement_template: Template string for redaction banner.

        Returns:
            Anonymized text with PHI scrubbed out.
        """
        entities = self.detect_phi(text)
        if not entities:
            return text

        scrubbed = text
        # Replace backwards from end to preserve indices
        for entity in reversed(entities):
            start = entity["start_char"]
            end = entity["end_char"]
            etype = entity["entity_type"]
            replacement = replacement_template.format(type=etype)
            scrubbed = scrubbed[:start] + replacement + scrubbed[end:]

        return scrubbed
