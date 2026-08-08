"""
Application use cases for Designer microservice.
Inherits from packages.hexagonal.UseCasePort.
"""

from typing import Any

from packages.hexagonal import UseCasePort


class CreateStudyUseCase(UseCasePort):
    """Use case for creating a new clinical study protocol."""

    async def execute(self, *args: Any, **kwargs: Any) -> Any:
        pass


class PublishStudyUseCase(UseCasePort):
    """Use case for publishing a clinical study version."""

    async def execute(self, *args: Any, **kwargs: Any) -> Any:
        pass
