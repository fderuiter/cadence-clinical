"""Application service coordinating AI inference, model tiering, and privacy air-gap execution.

Requirements: PRD-SYS-051
"""

from apps.ai_gateway.domain.models import (
    EmbeddingRequest,
    EmbeddingResponse,
    GenerationRequest,
    GenerationResponse,
    TierInfo,
)
from apps.ai_gateway.domain.ports import AIEnginePort


class InferenceApplicationService:
    """Coordinates application-level inference orchestration across domain ports."""

    def __init__(self, engine: AIEnginePort) -> None:
        """Initialize the application service with an injected AI engine port.

        Args:
            engine: The underlying AIEnginePort implementation.
        """
        self._engine = engine

    async def execute_generation(
        self, request: GenerationRequest
    ) -> GenerationResponse:
        """Execute a generation request through the engine port.

        Args:
            request: The domain generation request.

        Returns:
            GenerationResponse from the engine.
        """
        return await self._engine.generate(request)

    async def execute_embedding(self, request: EmbeddingRequest) -> EmbeddingResponse:
        """Execute an embedding request through the engine port.

        Args:
            request: The domain embedding request.

        Returns:
            EmbeddingResponse from the engine.
        """
        return await self._engine.embed(request)

    def retrieve_tier_info(self) -> list[TierInfo]:
        """Retrieve available model tier configurations.

        Returns:
            List of TierInfo objects.
        """
        return self._engine.get_tier_info()


__all__ = ["InferenceApplicationService"]
