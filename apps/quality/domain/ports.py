from abc import abstractmethod
from collections.abc import Sequence
from typing import Any

from packages.hexagonal import RepositoryPort


class QualityRepositoryPort(RepositoryPort[Any]):
    """Driven repository port for Quality microservice."""

    # --- Deviations & RCA ---
    @abstractmethod
    def create_deviation_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_rca_entity(self, **kwargs) -> Any:
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

    # --- CAPAs, Action Items & Effectiveness Checks ---
    @abstractmethod
    def create_capa_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_action_item_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_effectiveness_check_entity(self, **kwargs) -> Any:
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
    async def get_action_items_by_capa(self, capa_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_action_item(self, action_item: Any) -> Any:
        pass

    @abstractmethod
    async def get_effectiveness_checks_by_capa(self, capa_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_effectiveness_check(self, check: Any) -> Any:
        pass

    # --- RBQM, KRIs & QTLs ---
    @abstractmethod
    def create_ctq_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_kri_definition_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_kri_evaluation_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_site_risk_profile_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_qtl_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_qtl_breach_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    async def get_ctq_factors(self, study_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_ctq(self, ctq: Any) -> Any:
        pass

    @abstractmethod
    async def get_kri_definitions(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_kri_definition_by_code(self, code: str) -> Any | None:
        pass

    @abstractmethod
    async def save_kri_definition(self, kri: Any) -> Any:
        pass

    @abstractmethod
    async def get_kri_evaluations(
        self, study_id: str, site_id: str | None = None
    ) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_kri_evaluation(self, eval_entity: Any) -> Any:
        pass

    @abstractmethod
    async def get_site_risk_profiles(self, study_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_site_risk_profile(self, profile: Any) -> Any:
        pass

    @abstractmethod
    async def get_qtls(self, study_id: str | None = None) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_qtl_by_id(self, qtl_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save_qtl(self, qtl: Any) -> Any:
        pass

    @abstractmethod
    async def get_qtl_breaches(self, study_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_qtl_breach(self, breach: Any) -> Any:
        pass

    # --- Clinical Audits & Findings ---
    @abstractmethod
    def create_audit_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    def create_audit_finding_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    async def get_audits(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_audit_by_id(self, audit_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save_audit(self, audit: Any) -> Any:
        pass

    @abstractmethod
    async def get_findings_by_audit(self, audit_id: str) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_finding_by_id(self, finding_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save_audit_finding(self, finding: Any) -> Any:
        pass

    # --- Serious Breaches ---
    @abstractmethod
    def create_serious_breach_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    async def get_serious_breaches(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def get_serious_breach_by_id(self, breach_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save_serious_breach(self, breach: Any) -> Any:
        pass

    # --- Audit Logs ---
    @abstractmethod
    def create_audit_log_entity(self, **kwargs) -> Any:
        pass

    @abstractmethod
    async def get_audit_logs(self) -> Sequence[Any]:
        pass

    @abstractmethod
    async def save_audit_log(self, log: Any) -> Any:
        pass
