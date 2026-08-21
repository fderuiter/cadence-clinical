"""Domain ports (interfaces) for the AI Gateway."""

from abc import ABC, abstractmethod

from apps.ai_gateway.domain.models import (
    EmbeddingRequest,
    EmbeddingResponse,
    GenerationRequest,
    GenerationResponse,
    TierInfo,
)


class AIEnginePort(ABC):
    """Abstract port for executing AI completions, structured extractions, and embeddings."""

    @abstractmethod
    async def generate(self, request: GenerationRequest) -> GenerationResponse:
        """Execute a text generation or structured JSON completion request.

        Args:
            request: The generation specification including prompt, tier, and schema.

        Returns:
            GenerationResponse with completion text, structured data, and telemetry.
        """
        ...

    @abstractmethod
    async def embed(self, request: EmbeddingRequest) -> EmbeddingResponse:
        """Execute vector embedding generation for a batch of text inputs.

        Args:
            request: The embedding request containing input texts and model tier.

        Returns:
            EmbeddingResponse containing generated dense float vectors.
        """
        ...

    @abstractmethod
    def get_tier_info(self) -> list[TierInfo]:
        """Retrieve active configuration and model mapping across all execution tiers.

        Returns:
            List of TierInfo objects describing current routing destinations.
        """
        ...
