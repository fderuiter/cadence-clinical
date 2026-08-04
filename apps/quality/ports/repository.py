from abc import ABC, abstractmethod
from typing import Sequence
from ..models import Deviation, RootCauseAnalysis, CAPARecord, QualityAuditLog


class QualityRepositoryPort(ABC):
    @abstractmethod
    async def get_deviations(self) -> Sequence[Deviation]:
        pass

    @abstractmethod
    async def get_deviation_by_id(self, dev_id: str) -> Deviation | None:
        pass

    @abstractmethod
    async def save_deviation(self, dev: Deviation) -> Deviation:
        pass

    @abstractmethod
    async def get_rca_by_deviation_id(self, dev_id: str) -> RootCauseAnalysis | None:
        pass

    @abstractmethod
    async def get_rca_by_id(self, rca_id: str) -> RootCauseAnalysis | None:
        pass

    @abstractmethod
    async def save_rca(self, rca: RootCauseAnalysis) -> RootCauseAnalysis:
        pass

    @abstractmethod
    async def get_capa_by_id(self, capa_id: str) -> CAPARecord | None:
        pass

    @abstractmethod
    async def get_capas(self) -> Sequence[CAPARecord]:
        pass

    @abstractmethod
    async def save_capa(self, capa: CAPARecord) -> CAPARecord:
        pass

    @abstractmethod
    async def get_audit_logs(self) -> Sequence[QualityAuditLog]:
        pass

    @abstractmethod
    async def save_audit_log(self, log: QualityAuditLog) -> QualityAuditLog:
        pass
