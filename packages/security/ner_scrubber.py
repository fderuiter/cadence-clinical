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
        entities: list[dict[str, Any]] = []

        # 1. Match standard HIPAA regex patterns
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

        # 2. Match custom terms (if provided)
        if custom_terms:
            valid_terms = [t for t in custom_terms if t and t.strip()]
            if valid_terms:
                # Sort descending to match longer strings first
                valid_terms.sort(key=len, reverse=True)
                patterns = []
                for term in valid_terms:
                    escaped = re.escape(term)
                    start_b = r"\b" if re.match(r"^\w", term) else ""
                    end_b = r"\b" if re.search(r"\w$", term) else ""
                    patterns.append(f"{start_b}{escaped}{end_b}")

                custom_regex = re.compile("|".join(patterns), re.IGNORECASE)
                for match in custom_regex.finditer(text):
                    entities.append(
                        {
                            "entity_type": "CUSTOM",
                            "text": match.group(0),
                            "start_char": match.start(),
                            "end_char": match.end(),
                            "confidence": 0.99,
                        }
                    )

        # 3. Deterministic Overlap Resolution
        # Sort sequence:
        # 1. start_char index (ascending)
        # 2. end_char index (descending - wider intervals prioritized)
        # 3. entity_type (alphabetically)
        # 4. Match length (descending)
        entities.sort(
            key=lambda x: (
                x["start_char"],
                -x["end_char"],
                x["entity_type"],
                -len(x["text"]),
            )
        )

        # Resolve overlapping intervals, discarding subordinate/nested overlaps
        accepted: list[dict[str, Any]] = []
        for candidate in entities:
            overlap = False
            for acc in accepted:
                if max(candidate["start_char"], acc["start_char"]) < min(
                    candidate["end_char"], acc["end_char"]
                ):
                    overlap = True
                    break
            if not overlap:
                accepted.append(candidate)

        return accepted

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
