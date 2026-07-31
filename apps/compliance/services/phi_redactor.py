"""Automated PHI Redactor Service.

Requirements: PRD-SYS-001
"""

from typing import List

import packages  # noqa: F401
from packages.security.ner_scrubber import PHINameEntityScrubber


class PHIRedactorService:
    """Service for identifying and redacting PHI/PII from regulatory documents non-destructively.

    Requirements: PRD-SYS-001
    """

    def __init__(self) -> None:
        """Initialize PHI NER scrubber."""
        self.scrubber = PHINameEntityScrubber()

    def redact_content(self, content: bytes, phi_terms: List[str]) -> bytes:
        """Redacts specified PHI terms and auto-detected PHI from content bytes.

        Args:
            content: Raw document content bytes.
            phi_terms: List of specific string terms to redact.

        Returns:
            Redacted content bytes.
        """
        text = content.decode("utf-8", errors="ignore")

        # Redact specific literal terms
        for term in phi_terms:
            if term:
                text = text.replace(term, "[REDACTED]")

        # Also redact standard PHI via scrubber
        text = self.scrubber.scrub_phi(text)

        return text.encode("utf-8")
