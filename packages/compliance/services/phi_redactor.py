"""Automated PHI Redactor Service.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from packages.security.ner_scrubber import PHINameEntityScrubber


class PHIRedactorService:
    """Service for identifying and redacting PHI/PII from regulatory documents non-destructively.

    Requirements: PRD-SYS-001
    """

    def __init__(self) -> None:
        """Initialize PHI NER scrubber."""
        self.scrubber = PHINameEntityScrubber()

    def redact_content(self, content: bytes, phi_terms: list[str]) -> bytes:
        """Redacts specified PHI terms and auto-detected PHI from content bytes.

        Args:
            content: Raw document content bytes.
            phi_terms: List of specific string terms to redact.

        Returns:
            Redacted content bytes.
        """
        text = content.decode("utf-8", errors="ignore")

        # Delegate direct name and pattern matching to the primary scrubber
        text = self.scrubber.scrub_phi(
            text, custom_terms=phi_terms, custom_replacement="[REDACTED]"
        )

        return text.encode("utf-8")
