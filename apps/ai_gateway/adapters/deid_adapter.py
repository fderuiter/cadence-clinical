"""De-identification air-gap wrapper adapter for AI Gateway engine execution.

Intercepts outbound generation and embedding requests, replaces patient identifiers
with ephemeral surrogate tokens via packages/deid, executes inference over sanitized
prompts, and re-hydrates completions in memory before returning to the caller.

Requirements: PRD-SYS-051
"""

import logging

from apps.ai_gateway.domain.models import (
    EmbeddingRequest,
    EmbeddingResponse,
    GenerationRequest,
    GenerationResponse,
    TierInfo,
)
from apps.ai_gateway.domain.ports import AIEnginePort
from packages.deid.air_gap import DeidAirGapVault
from packages.deid.models import ComplianceProfile

logger = logging.getLogger(__name__)


class DeidentifiedAIEngineAdapter(AIEnginePort):
    """Hexagonal adapter wrapper implementing in-flight de-identification air-gap."""

    def __init__(self, inner_engine: AIEnginePort) -> None:
        """Initialize the de-identified AI engine adapter.

        Args:
            inner_engine: The underlying AIEnginePort implementation (e.g. LiteLLMAdapter, MockAIEngineAdapter).
        """
        self._inner_engine = inner_engine

    @property
    def inner_engine(self) -> AIEnginePort:
        """Access the underlying inner AI engine adapter."""
        return self._inner_engine

    def _resolve_profile(self, profile_str: str | None) -> ComplianceProfile:
        """Resolve string profile name to ComplianceProfile enum."""
        if not profile_str:
            return ComplianceProfile.HIPAA
        try:
            return ComplianceProfile(profile_str)
        except ValueError:
            return ComplianceProfile.HIPAA

    async def generate(self, request: GenerationRequest) -> GenerationResponse:
        """Intercept outbound generation prompt, apply surrogate tokens, and re-hydrate completion.

        Args:
            request: Inbound GenerationRequest.

        Returns:
            Re-hydrated GenerationResponse containing telemetry and sanitized-then-unmasked content.
        """
        if not request.enable_deid:
            return await self._inner_engine.generate(request)

        profile = self._resolve_profile(request.compliance_profile)

        with DeidAirGapVault() as vault:
            sanitized_prompt = (
                vault.deidentify_text(
                    text=request.prompt,
                    profile=profile,
                    custom_terms=request.custom_terms,
                )
                if request.prompt
                else None
            )

            sanitized_messages = (
                vault.deidentify_messages(
                    messages=request.messages,
                    profile=profile,
                    custom_terms=request.custom_terms,
                )
                if request.messages
                else None
            )

            sanitized_request = request.model_copy(
                update={
                    "prompt": sanitized_prompt,
                    "messages": sanitized_messages,
                }
            )

            # Execute model inference with sanitized prompts
            raw_response = await self._inner_engine.generate(sanitized_request)

            # Re-hydrate response content and structured JSON data in memory
            if vault.has_surrogates:
                rehydrated_content = vault.rehydrate_text(raw_response.content)
                rehydrated_structured_data = (
                    vault.rehydrate_structured_data(raw_response.structured_data)
                    if raw_response.structured_data is not None
                    else None
                )
                return raw_response.model_copy(
                    update={
                        "content": rehydrated_content,
                        "structured_data": rehydrated_structured_data,
                        "deid_applied": True,
                        "deid_tokens_count": vault.surrogate_count,
                    }
                )

            return raw_response.model_copy(
                update={
                    "deid_applied": False,
                    "deid_tokens_count": 0,
                }
            )

    async def embed(self, request: EmbeddingRequest) -> EmbeddingResponse:
        """Intercept input texts for embeddings and sanitize PHI prior to vector generation.

        Args:
            request: Inbound EmbeddingRequest.

        Returns:
            EmbeddingResponse with deid telemetry.
        """
        if not request.enable_deid:
            return await self._inner_engine.embed(request)

        profile = self._resolve_profile(request.compliance_profile)

        with DeidAirGapVault() as vault:
            sanitized_texts = vault.deidentify_texts(
                texts=request.input_texts,
                profile=profile,
                custom_terms=request.custom_terms,
            )

            sanitized_request = request.model_copy(
                update={
                    "input_texts": sanitized_texts,
                }
            )

            raw_response = await self._inner_engine.embed(sanitized_request)

            return raw_response.model_copy(
                update={
                    "deid_applied": vault.has_surrogates,
                    "deid_tokens_count": vault.surrogate_count,
                }
            )

    def get_tier_info(self) -> list[TierInfo]:
        """Delegate tier descriptor retrieval to the inner engine."""
        return self._inner_engine.get_tier_info()


__all__ = ["DeidentifiedAIEngineAdapter"]
