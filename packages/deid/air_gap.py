"""In-flight de-identification air-gap engine and ephemeral surrogate token vault.

PRD-SYS-051: Privacy air-gap interceptor replacing patient identifiers with
ephemeral surrogate tokens and re-hydrating completion responses in memory.
"""

import json
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

    def deidentify_structured_data(
        self,
        data: Any,
        profile: ComplianceProfile = ComplianceProfile.HIPAA,
        custom_terms: list[str] | None = None,
    ) -> Any:
        """Recursively de-identify string fields in structured dictionaries and lists.

        Args:
            data: Arbitrary structured data payload (dict, list, primitive, or None).
            profile: ComplianceProfile for PHI detection.
            custom_terms: Optional custom terms/names to redact.

        Returns:
            Sanitized structured data with nested string values de-identified.
        """
        if data is None:
            return data

        if isinstance(data, dict):
            return {
                k: self.deidentify_structured_data(
                    v, profile=profile, custom_terms=custom_terms
                )
                for k, v in data.items()
            }
        if isinstance(data, list):
            return [
                self.deidentify_structured_data(
                    item, profile=profile, custom_terms=custom_terms
                )
                for item in data
            ]
        if isinstance(data, str):
            stripped = data.strip()
            if (stripped.startswith("{") and stripped.endswith("}")) or (
                stripped.startswith("[") and stripped.endswith("]")
            ):
                try:
                    parsed = json.loads(data)
                    sanitized_parsed = self.deidentify_structured_data(
                        parsed, profile=profile, custom_terms=custom_terms
                    )
                    return json.dumps(sanitized_parsed)
                except Exception:
                    pass
            return self.deidentify_text(
                data, profile=profile, custom_terms=custom_terms
            )
        return data

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
                if msg_copy.get("content") is not None:
                    msg_copy["content"] = self.deidentify_structured_data(
                        msg_copy.get("content"),
                        profile=profile,
                        custom_terms=custom_terms,
                    )
                if msg_copy.get("tool_calls"):
                    sanitized_tcs = []
                    for tc in msg_copy["tool_calls"]:
                        if (
                            isinstance(tc, dict)
                            and "function" in tc
                            and "arguments" in tc["function"]
                        ):
                            tc_c = deepcopy(tc)
                            tc_c["function"]["arguments"] = (
                                self.deidentify_structured_data(
                                    tc_c["function"]["arguments"],
                                    profile=profile,
                                    custom_terms=custom_terms,
                                )
                            )
                            sanitized_tcs.append(tc_c)
                        else:
                            sanitized_tcs.append(tc)
                    msg_copy["tool_calls"] = sanitized_tcs
                sanitized_messages.append(msg_copy)  # type: ignore[arg-type]
            elif hasattr(msg, "model_copy"):
                raw_content = getattr(msg, "content", None)
                sanitized_content = (
                    self.deidentify_structured_data(
                        raw_content, profile=profile, custom_terms=custom_terms
                    )
                    if raw_content is not None
                    else None
                )
                updates: dict[str, Any] = {"content": sanitized_content}
                if hasattr(msg, "tool_calls") and getattr(msg, "tool_calls", None):
                    sanitized_tcs = []
                    for tc in getattr(msg, "tool_calls"):
                        if hasattr(tc, "function") and hasattr(
                            tc.function, "arguments"
                        ):
                            sanitized_args = self.deidentify_structured_data(
                                tc.function.arguments,
                                profile=profile,
                                custom_terms=custom_terms,
                            )
                            sanitized_fn = tc.function.model_copy(
                                update={"arguments": sanitized_args}
                            )
                            sanitized_tcs.append(
                                tc.model_copy(update={"function": sanitized_fn})
                            )
                        else:
                            sanitized_tcs.append(tc)
                    updates["tool_calls"] = sanitized_tcs
                sanitized_messages.append(
                    msg.model_copy(update=updates)  # type: ignore[union-attr]
                )
            elif hasattr(msg, "role") and hasattr(msg, "content"):
                raw_content = getattr(msg, "content", None)
                sanitized_content = (
                    self.deidentify_structured_data(
                        raw_content, profile=profile, custom_terms=custom_terms
                    )
                    if raw_content is not None
                    else None
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
