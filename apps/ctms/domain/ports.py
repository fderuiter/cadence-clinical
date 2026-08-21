from abc import ABC, abstractmethod
from typing import Any

from apps.ctms.domain.models import (
    CountryRegulatoryMilestoneEntity,
    CTMSAuditLogEntity,
    CTMSDelegationEntity,
    DeviationActionItemEntity,
    EssentialDocumentEntity,
    ETMFSyncRecordEntity,
    FinancialInvoiceEntity,
    IPDestructionCertificateEntity,
    IPKitRecordEntity,
    IPTemperatureExcursionEntity,
    ProcedurePaymentGridEntity,
    ProtocolDeviationEntity,
    RBQMKRIMetricEntity,
    SiteGreenlightGateEntity,
    SiteRiskScoreEntity,
)
from packages.hexagonal import RepositoryPort


class ICTMSDelegationRepository(RepositoryPort[CTMSDelegationEntity]):
    """Repository port for CTMS Delegation of Authority (DOA)."""

    async def get_by_id(self, entity_id: str) -> CTMSDelegationEntity | None:
        return None

    async def save(self, entity: CTMSDelegationEntity) -> CTMSDelegationEntity:
        return entity

    @abstractmethod
    async def get_by_site_id(self, site_id: str) -> list[CTMSDelegationEntity]:
        pass

    @abstractmethod
    async def save_audit_log(self, audit: CTMSAuditLogEntity) -> None:
        pass

    @abstractmethod
    async def get_audit_logs_by_site(self, site_id: str) -> list[CTMSAuditLogEntity]:
        pass


class ISiteStartupRepository(RepositoryPort[Any]):
    """Repository port for Site Startup, Regulatory Milestones & Essential Documents."""

    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    async def save(self, entity: Any) -> Any:
        return entity

    @abstractmethod
    async def save_country_milestone(
        self, milestone: CountryRegulatoryMilestoneEntity
    ) -> CountryRegulatoryMilestoneEntity:
        pass

    @abstractmethod
    async def list_country_milestones(
        self, study_id: str, country_code: str | None = None
    ) -> list[CountryRegulatoryMilestoneEntity]:
        pass

    @abstractmethod
    async def save_essential_document(
        self, document: EssentialDocumentEntity
    ) -> EssentialDocumentEntity:
        pass

    @abstractmethod
    async def get_essential_document(
        self, doc_id: str
    ) -> EssentialDocumentEntity | None:
        pass

    @abstractmethod
    async def list_essential_documents(
        self, study_id: str, site_id: str | None = None
    ) -> list[EssentialDocumentEntity]:
        pass

    @abstractmethod
    async def save_greenlight_gate(
        self, gate: SiteGreenlightGateEntity
    ) -> SiteGreenlightGateEntity:
        pass

    @abstractmethod
    async def get_greenlight_gate(
        self, site_id: str
    ) -> SiteGreenlightGateEntity | None:
        pass


class IProtocolDeviationRepository(RepositoryPort[Any]):
    """Repository port for Protocol Deviations and Action Items."""

    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    async def save(self, entity: Any) -> Any:
        return entity

    @abstractmethod
    async def save_deviation(
        self, deviation: ProtocolDeviationEntity
    ) -> ProtocolDeviationEntity:
        pass

    @abstractmethod
    async def get_deviation(self, deviation_id: str) -> ProtocolDeviationEntity | None:
        pass

    @abstractmethod
    async def list_deviations(
        self,
        study_id: str,
        site_id: str | None = None,
        severity: str | None = None,
    ) -> list[ProtocolDeviationEntity]:
        pass

    @abstractmethod
    async def save_action_item(
        self, action_item: DeviationActionItemEntity
    ) -> DeviationActionItemEntity:
        pass

    @abstractmethod
    async def get_action_item(
        self, action_item_id: str
    ) -> DeviationActionItemEntity | None:
        pass

    @abstractmethod
    async def list_action_items(
        self, deviation_id: str | None = None, site_id: str | None = None
    ) -> list[DeviationActionItemEntity]:
        pass


class IRBQMRepository(RepositoryPort[Any]):
    """Repository port for Risk-Based Quality Management & Key Risk Indicators."""

    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    async def save(self, entity: Any) -> Any:
        return entity

    @abstractmethod
    async def save_kri_metric(self, metric: RBQMKRIMetricEntity) -> RBQMKRIMetricEntity:
        pass

    @abstractmethod
    async def list_kri_metrics(
        self, study_id: str, site_id: str | None = None
    ) -> list[RBQMKRIMetricEntity]:
        pass

    @abstractmethod
    async def save_site_risk_score(
        self, score: SiteRiskScoreEntity
    ) -> SiteRiskScoreEntity:
        pass

    @abstractmethod
    async def get_latest_site_risk_score(
        self, site_id: str
    ) -> SiteRiskScoreEntity | None:
        pass


class IFinancialsRepository(RepositoryPort[Any]):
    """Repository port for Procedure Payment Grids and Invoices."""

    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    async def save(self, entity: Any) -> Any:
        return entity

    @abstractmethod
    async def save_procedure_grid(
        self, grid: ProcedurePaymentGridEntity
    ) -> ProcedurePaymentGridEntity:
        pass

    @abstractmethod
    async def list_procedure_grids(
        self, grant_id: str
    ) -> list[ProcedurePaymentGridEntity]:
        pass

    @abstractmethod
    async def save_invoice(
        self, invoice: FinancialInvoiceEntity
    ) -> FinancialInvoiceEntity:
        pass

    @abstractmethod
    async def get_invoice(self, invoice_id: str) -> FinancialInvoiceEntity | None:
        pass

    @abstractmethod
    async def list_invoices(
        self, study_id: str, site_id: str | None = None
    ) -> list[FinancialInvoiceEntity]:
        pass


class IIPAccountabilityRepository(RepositoryPort[Any]):
    """Repository port for Site IP Kits, Temperature Excursions, and Destruction."""

    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    async def save(self, entity: Any) -> Any:
        return entity

    @abstractmethod
    async def save_ip_kit(self, kit: IPKitRecordEntity) -> IPKitRecordEntity:
        pass

    @abstractmethod
    async def get_ip_kit(self, kit_id: str) -> IPKitRecordEntity | None:
        pass

    @abstractmethod
    async def get_ip_kit_by_number(
        self, site_id: str, kit_number: str
    ) -> IPKitRecordEntity | None:
        pass

    @abstractmethod
    async def list_ip_kits(
        self, study_id: str, site_id: str | None = None, status: str | None = None
    ) -> list[IPKitRecordEntity]:
        pass

    @abstractmethod
    async def save_temperature_excursion(
        self, excursion: IPTemperatureExcursionEntity
    ) -> IPTemperatureExcursionEntity:
        pass

    @abstractmethod
    async def get_temperature_excursion(
        self, excursion_id: str
    ) -> IPTemperatureExcursionEntity | None:
        pass

    @abstractmethod
    async def list_temperature_excursions(
        self, study_id: str, site_id: str | None = None
    ) -> list[IPTemperatureExcursionEntity]:
        pass

    @abstractmethod
    async def save_destruction_certificate(
        self, cert: IPDestructionCertificateEntity
    ) -> IPDestructionCertificateEntity:
        pass

    @abstractmethod
    async def list_destruction_certificates(
        self, study_id: str, site_id: str | None = None
    ) -> list[IPDestructionCertificateEntity]:
        pass


class IETMFSyncRepository(RepositoryPort[Any]):
    """Repository port for eTMF synchronization records."""

    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    async def save(self, entity: Any) -> Any:
        return entity

    @abstractmethod
    async def save_sync_record(
        self, record: ETMFSyncRecordEntity
    ) -> ETMFSyncRecordEntity:
        pass

    @abstractmethod
    async def list_sync_records(
        self, study_id: str, site_id: str | None = None
    ) -> list[ETMFSyncRecordEntity]:
        pass


class ETMFClientPort(ABC):
    """Port for pushing artifacts to eTMF."""

    @abstractmethod
    async def push_document(
        self,
        study_id: str,
        site_id: str | None,
        title: str,
        content_text: str,
        dia_zone: str,
        dia_section: str,
        dia_artifact: str,
        user_id: str,
        user_roles: list[str],
        reason_for_change: str,
    ) -> dict[str, str]:
        pass


class QualityClientPort(ABC):
    """Port for escalating major/critical deviations to Quality CAPA."""

    @abstractmethod
    async def create_capa_from_deviation(
        self,
        study_id: str,
        site_id: str,
        title: str,
        description: str,
        severity: str,
        root_cause_summary: str,
        corrective_action: str,
        user_id: str,
        user_roles: list[str],
        reason_for_change: str,
        deviation_id: str | None = None,
    ) -> dict[str, str]:
        pass


class SafetyClientPort(ABC):
    """Port for notifying safety of critical deviations."""

    @abstractmethod
    async def notify_deviation_event(
        self,
        study_id: str,
        site_id: str,
        deviation_id: str,
        title: str,
        severity: str,
        user_id: str,
    ) -> bool:
        pass


# Aliases for backward compatibility
IETMFClientPort = ETMFClientPort
IQualityClientPort = QualityClientPort
ISafetyClientPort = SafetyClientPort
