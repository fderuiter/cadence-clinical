"""
Domain ports for Designer microservice.
All repository ports inherit from packages.hexagonal.RepositoryPort.
"""

from abc import abstractmethod
from typing import Any

from packages.hexagonal import RepositoryPort, UseCasePort


class DesignerRepositoryPort(RepositoryPort[Any]):
    """
    Base repository port for Designer entities.
    Inherits from packages.hexagonal.RepositoryPort.
    """

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        """Retrieve entity by ID."""
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        """Save or persist entity."""
        pass


class StudyRepositoryPort(RepositoryPort[Any]):
    """
    Repository port for Clinical Study persistence.
    """

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass

    @abstractmethod
    async def get_study_version(self, study_id: str, version_id: str) -> Any | None:
        pass


class LibraryRepositoryPort(RepositoryPort[Any]):
    """
    Repository port for Global MDR Library objects.
    """

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass

    @abstractmethod
    async def get_latest_version(self, object_id: str) -> Any | None:
        pass


class ProtocolRepositoryPort(RepositoryPort[Any]):
    """
    Repository port for Protocol structures and amendments.
    """

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass


class RulesRepositoryPort(RepositoryPort[Any]):
    """
    Repository port for authored clinical validation rules.
    """

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass


class DigitizationJobRepositoryPort(RepositoryPort[Any]):
    """
    Repository port for Protocol Digitization DAG job state and checkpoints.
    """

    @abstractmethod
    async def create_job(self, job: Any) -> Any:
        pass

    @abstractmethod
    async def get_job(self, job_id: str) -> Any | None:
        pass

    @abstractmethod
    async def update_job(self, job: Any) -> Any:
        pass

    @abstractmethod
    async def save_checkpoint(self, job_id: str, checkpoint: Any) -> Any:
        pass

    @abstractmethod
    async def list_jobs(
        self, study_id: str | None = None, limit: int = 50
    ) -> list[Any]:
        pass


__all__ = [
    "DesignerRepositoryPort",
    "DigitizationJobRepositoryPort",
    "LibraryRepositoryPort",
    "ProtocolRepositoryPort",
    "RulesRepositoryPort",
    "StudyRepositoryPort",
    "UseCasePort",
]
