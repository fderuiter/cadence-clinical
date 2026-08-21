"""In-flight de-identification air-gap engine and ephemeral surrogate token vault.

PRD-SYS-051: Privacy air-gap interceptor replacing patient identifiers with
ephemeral surrogate tokens and re-hydrating completion responses in memory.
"""

from collections.abc import Sequence
from copy import deepcopy
from typing import Any, Self

from packages.deid.detector import DeidDetector, resolve_overlaps
from packages.deid.models import ComplianceProfile


class DeidAirGapVault:
    """Ephemeral in-memory vault for in-flight prompt de-identification and completion re-hydration.

    Maintains a single-request ephemeral bidirectional mapping between raw patient
    identifiers and surrogate tokens (e.g. `[SURROGATE_SSN_1]`, `[SURROGATE_CUSTOM_1]`),
    preserving entity co-references across prompt turns, and guaranteeing in-memory
    cleanup upon request completion.
    """

    def __init__(self, detector: DeidDetector | None = None) -> None:
        """Initialize an ephemeral air-gap vault.

        Args:
            detector: Optional DeidDetector instance (defaults to a new DeidDetector).
        """
        self.detector = detector or DeidDetector()
        self.surrogate_to_raw: dict[str, str] = {}
        self.raw_to_surrogate: dict[str, str] = {}
        self.category_counters: dict[str, int] = {}

    @property
    def has_surrogates(self) -> bool:
        """Return True if any surrogate replacements were registered."""
        return bool(self.surrogate_to_raw)

    @property
    def surrogate_count(self) -> int:
        """Return the count of unique surrogate tokens created."""
        return len(self.surrogate_to_raw)

    def get_or_create_surrogate(self, raw_value: str, category: str) -> str:
        """Retrieve existing surrogate token for raw value or create a new indexed token.

        Args:
            raw_value: Raw sensitive text string.
            category: DetectorCategory name string.

        Returns:
            Surrogate token string (e.g. `[SURROGATE_SSN_NATIONAL_ID_1]`).
        """
        if raw_value in self.raw_to_surrogate:
            return self.raw_to_surrogate[raw_value]

        cat_upper = category.upper()
        next_idx = self.category_counters.get(cat_upper, 0) + 1
        self.category_counters[cat_upper] = next_idx

        surrogate = f"[SURROGATE_{cat_upper}_{next_idx}]"
        self.raw_to_surrogate[raw_value] = surrogate
        self.surrogate_to_raw[surrogate] = raw_value
        return surrogate

    def deidentify_text(
        self,
        text: str,
        profile: ComplianceProfile = ComplianceProfile.HIPAA,
        custom_terms: list[str] | None = None,
    ) -> str:
        """De-identify text by replacing detected PHI/PII matches with surrogate tokens.

        Args:
            text: Raw input prompt or text string.
            profile: ComplianceProfile determining active detector categories.
            custom_terms: Optional list of literal custom terms (e.g. patient names).

        Returns:
            Sanitized text string containing surrogate tokens.
        """
        if not text:
            return text

        detections = self.detector.detect(
            text=text, profile=profile, custom_terms=custom_terms
        )
        clean_detections = resolve_overlaps(detections, text)
        if not clean_detections:
            return text

        parts = list(text)
        # Substitute right-to-left to keep start and end character offsets valid
        for res in reversed(clean_detections):
            surrogate = self.get_or_create_surrogate(
                raw_value=res.value, category=res.category
            )
            parts[res.start : res.end] = list(surrogate)

        return "".join(parts)

    def deidentify_messages[T](
        self,
        messages: Sequence[T],
        profile: ComplianceProfile = ComplianceProfile.HIPAA,
        custom_terms: list[str] | None = None,
    ) -> list[T]:
        """De-identify a sequence of chat messages while preserving co-reference mappings.

        Args:
            messages: List of chat message objects (with .role and .content) or dicts.
            profile: ComplianceProfile for PHI detection.
            custom_terms: Optional custom terms/names to redact.

        Returns:
            New list of chat message objects/dicts with de-identified content.
        """
        sanitized_messages: list[T] = []
        for msg in messages:
            if isinstance(msg, dict):
                msg_copy = deepcopy(msg)
                msg_copy["content"] = self.deidentify_text(
                    text=msg.get("content", ""),
                    profile=profile,
                    custom_terms=custom_terms,
                )
                sanitized_messages.append(msg_copy)  # type: ignore[arg-type]
            elif hasattr(msg, "model_copy") and hasattr(msg, "content"):
                sanitized_content = self.deidentify_text(
                    text=getattr(msg, "content", ""),
                    profile=profile,
                    custom_terms=custom_terms,
                )
                sanitized_messages.append(
                    msg.model_copy(update={"content": sanitized_content})  # type: ignore[union-attr]
                )
            elif hasattr(msg, "role") and hasattr(msg, "content"):
                sanitized_content = self.deidentify_text(
                    text=getattr(msg, "content", ""),
                    profile=profile,
                    custom_terms=custom_terms,
                )
                sanitized_messages.append(
                    msg.__class__(role=getattr(msg, "role"), content=sanitized_content)  # type: ignore[call-arg]
                )
            else:
                sanitized_messages.append(msg)
        return sanitized_messages

    def deidentify_texts(
        self,
        texts: Sequence[str],
        profile: ComplianceProfile = ComplianceProfile.HIPAA,
        custom_terms: list[str] | None = None,
    ) -> list[str]:
        """De-identify a batch of text strings (e.g. for vector embedding batches).

        Args:
            texts: Batch of raw input text strings.
            profile: ComplianceProfile for PHI detection.
            custom_terms: Optional custom terms/names to redact.

        Returns:
            List of sanitized text strings.
        """
        return [
            self.deidentify_text(t, profile=profile, custom_terms=custom_terms)
            for t in texts
        ]

    def rehydrate_text(self, text: str) -> str:
        """Re-hydrate text by replacing surrogate tokens with their original raw values.

        Args:
            text: Model-generated text potentially containing surrogate tokens.

        Returns:
            Re-hydrated text string with raw patient identifiers restored in memory.
        """
        if not text or not self.surrogate_to_raw:
            return text

        rehydrated = text
        # Sort by surrogate token length descending to prevent partial token match collision
        for surrogate, raw_value in sorted(
            self.surrogate_to_raw.items(), key=lambda x: len(x[0]), reverse=True
        ):
            rehydrated = rehydrated.replace(surrogate, raw_value)
        return rehydrated

    def rehydrate_structured_data(self, data: Any) -> Any:
        """Recursively re-hydrate string fields in structured dictionaries and lists.

        Args:
            data: Arbitrary structured data payload (dict, list, primitive, or None).

        Returns:
            Deep copy of structured data with all nested string surrogate tokens replaced.
        """
        if not self.surrogate_to_raw or data is None:
            return data

        if isinstance(data, dict):
            return {k: self.rehydrate_structured_data(v) for k, v in data.items()}
        if isinstance(data, list):
            return [self.rehydrate_structured_data(item) for item in data]
        if isinstance(data, str):
            return self.rehydrate_text(data)
        return data

    def clear(self) -> None:
        """Purge all ephemeral surrogate mappings from memory."""
        self.surrogate_to_raw.clear()
        self.raw_to_surrogate.clear()
        self.category_counters.clear()

    def __enter__(self) -> Self:
        """Context manager entry point."""
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: Any,
    ) -> None:
        """Context manager exit point guaranteeing in-memory cleanup."""
        self.clear()


__all__ = ["DeidAirGapVault"]
