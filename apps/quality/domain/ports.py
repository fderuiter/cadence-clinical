from abc import abstractmethod
from collections.abc import Sequence
from typing import Any

from packages.hexagonal import RepositoryPort


class QualityRepositoryPort(RepositoryPort[Any]):
    """Driven repository port for Quality microservice."""

    @abstractmethod
    def create_deviation_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_rca_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_capa_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_audit_log_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    async def get_deviations(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_deviation_by_id(self, dev_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save_deviation(self, dev: Any) -> Any:
        pass

    @abstractmethod
    async def get_rca_by_deviation_id(self, dev_id: str) -> Any | None:
        pass

    @abstractmethod
    async def get_rca_by_id(self, rca_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save_rca(self, rca: Any) -> Any:
        pass

    @abstractmethod
    async def get_capa_by_id(self, capa_id: str) -> Any | None:
        pass

    @abstractmethod
    async def get_capas(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_capa(self, capa: Any) -> Any:
        pass

    @abstractmethod
    async def get_audit_logs(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_audit_log(self, log: Any) -> Any:
        pass
