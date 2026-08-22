"""Application layer for AI Gateway microservice."""

from apps.ai_gateway.application.inference_service import (
    InferenceApplicationService,
)

__all__ = ["InferenceApplicationService"]
