"""Domain ports for eConsent microservice.

Follows Hexagonal Architecture separating inbound drivers from outbound driven adapters.
"""

from abc import abstractmethod
from typing import Any

from apps.econsent.domain.entities import (
    ComprehensionCheckEntity,
    ConsentAuditLogEntity,
    ConsentClauseEntity,
    ConsentSignatureEntity,
    ConsentTemplateEntity,
    ConsentTranslationEntity,
    ConsentWithdrawalEntity,
    GranularConsentOptionEntity,
    GranularOptionSelectionEntity,
    ReconsentRequirementEntity,
    SubjectConsentEntity,
)
from packages.hexagonal import RepositoryPort


class IEConsentRepository(RepositoryPort[Any]):
    """Legacy compatibility repository port."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> Any | None:
        pass

    @abstractmethod
    async def save(self, entity: Any) -> Any:
        pass


class IConsentClauseRepository(RepositoryPort[ConsentClauseEntity]):
    """Driven repository port for Informed Consent Form clauses."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ConsentClauseEntity | None:
        pass

    @abstractmethod
    async def get_latest_by_clause_id(
        self, clause_id: str
    ) -> ConsentClauseEntity | None:
        pass

    @abstractmethod
    async def get_by_clause_and_version(
        self, clause_id: str, version_index: int
    ) -> ConsentClauseEntity | None:
        pass

    @abstractmethod
    async def list_clauses(
        self,
        study_id: str | None = None,
        clause_id: str | None = None,
        all_versions: bool = False,
    ) -> list[ConsentClauseEntity]:
        pass

    @abstractmethod
    async def save(self, entity: ConsentClauseEntity) -> ConsentClauseEntity:
        pass


class IConsentTemplateRepository(RepositoryPort[ConsentTemplateEntity]):
    """Driven repository port for versioned consent templates."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ConsentTemplateEntity | None:
        pass

    @abstractmethod
    async def get_latest_by_template_id(
        self, template_id: str
    ) -> ConsentTemplateEntity | None:
        pass

    @abstractmethod
    async def get_by_template_and_version(
        self, template_id: str, version_index: int
    ) -> ConsentTemplateEntity | None:
        pass

    @abstractmethod
    async def list_templates(
        self,
        study_id: str | None = None,
        template_id: str | None = None,
        all_versions: bool = False,
    ) -> list[ConsentTemplateEntity]:
        pass

    @abstractmethod
    async def save(self, entity: ConsentTemplateEntity) -> ConsentTemplateEntity:
        pass


class IConsentTranslationRepository(RepositoryPort[ConsentTranslationEntity]):
    """Driven repository port for multilingual consent translations."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ConsentTranslationEntity | None:
        pass

    @abstractmethod
    async def get_latest_by_translation_id(
        self, translation_id: str
    ) -> ConsentTranslationEntity | None:
        pass

    @abstractmethod
    async def get_by_source(
        self,
        source_id: str,
        source_version_index: int,
        language_code: str,
        source_type: str = "TEMPLATE",
    ) -> ConsentTranslationEntity | None:
        pass

    @abstractmethod
    async def list_translations(
        self,
        source_id: str | None = None,
        source_type: str | None = None,
        language_code: str | None = None,
        status: str | None = None,
        all_versions: bool = False,
    ) -> list[ConsentTranslationEntity]:
        pass

    @abstractmethod
    async def save(self, entity: ConsentTranslationEntity) -> ConsentTranslationEntity:
        pass


class IComprehensionRepository(RepositoryPort[ComprehensionCheckEntity]):
    """Driven repository port for comprehension check definitions and result histories."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ComprehensionCheckEntity | None:
        pass

    @abstractmethod
    async def get_check(
        self, template_id: str, version_index: int
    ) -> ComprehensionCheckEntity | None:
        pass

    @abstractmethod
    async def save_check(
        self, entity: ComprehensionCheckEntity
    ) -> ComprehensionCheckEntity:
        pass

    @abstractmethod
    async def save(self, entity: ComprehensionCheckEntity) -> ComprehensionCheckEntity:
        pass


class ISubjectConsentRepository(RepositoryPort[SubjectConsentEntity]):
    """Driven repository port for immutable subject consent records."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> SubjectConsentEntity | None:
        pass

    @abstractmethod
    async def get_latest_active_consent(
        self, study_id: str, subject_pseudonym: str
    ) -> SubjectConsentEntity | None:
        pass

    @abstractmethod
    async def list_subject_consents(
        self,
        study_id: str,
        site_id: str | None = None,
        subject_pseudonym: str | None = None,
    ) -> list[SubjectConsentEntity]:
        pass

    @abstractmethod
    async def save(self, entity: SubjectConsentEntity) -> SubjectConsentEntity:
        pass


class IConsentSignatureRepository(RepositoryPort[ConsentSignatureEntity]):
    """Driven repository port for 21 CFR Part 11 electronic signatures."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ConsentSignatureEntity | None:
        pass

    @abstractmethod
    async def get_signatures_for_template_version(
        self, template_id: str, version_index: int, subject_pseudonym: str
    ) -> list[ConsentSignatureEntity]:
        pass

    @abstractmethod
    async def save(self, entity: ConsentSignatureEntity) -> ConsentSignatureEntity:
        pass


class IGranularOptionRepository(RepositoryPort[GranularConsentOptionEntity]):
    """Driven repository port for tiered optional consent items."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> GranularConsentOptionEntity | None:
        pass

    @abstractmethod
    async def list_options_for_template(
        self, template_id: str, version_index: int
    ) -> list[GranularConsentOptionEntity]:
        pass

    @abstractmethod
    async def save_option(
        self, entity: GranularConsentOptionEntity
    ) -> GranularConsentOptionEntity:
        pass

    @abstractmethod
    async def save_selections(
        self, selections: list[GranularOptionSelectionEntity]
    ) -> list[GranularOptionSelectionEntity]:
        pass

    @abstractmethod
    async def get_selections_for_consent(
        self, consent_id: str
    ) -> list[GranularOptionSelectionEntity]:
        pass

    @abstractmethod
    async def save(
        self, entity: GranularConsentOptionEntity
    ) -> GranularConsentOptionEntity:
        pass


class IReconsentRepository(RepositoryPort[ReconsentRequirementEntity]):
    """Driven repository port for tracking re-consent requirements."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ReconsentRequirementEntity | None:
        pass

    @abstractmethod
    async def get_pending_requirements(
        self, study_id: str, subject_pseudonym: str | None = None
    ) -> list[ReconsentRequirementEntity]:
        pass

    @abstractmethod
    async def save_requirement(
        self, entity: ReconsentRequirementEntity
    ) -> ReconsentRequirementEntity:
        pass

    @abstractmethod
    async def save(
        self, entity: ReconsentRequirementEntity
    ) -> ReconsentRequirementEntity:
        pass


class IConsentWithdrawalRepository(RepositoryPort[ConsentWithdrawalEntity]):
    """Driven repository port for subject consent revocations."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ConsentWithdrawalEntity | None:
        pass

    @abstractmethod
    async def get_withdrawal(
        self, study_id: str, subject_pseudonym: str
    ) -> ConsentWithdrawalEntity | None:
        pass

    @abstractmethod
    async def save_withdrawal(
        self, entity: ConsentWithdrawalEntity
    ) -> ConsentWithdrawalEntity:
        pass

    @abstractmethod
    async def save(self, entity: ConsentWithdrawalEntity) -> ConsentWithdrawalEntity:
        pass


class IConsentAuditRepository(RepositoryPort[ConsentAuditLogEntity]):
    """Driven repository port for 21 CFR Part 11 audit trails."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ConsentAuditLogEntity | None:
        pass

    @abstractmethod
    async def log_action(
        self,
        actor_id: str,
        actor_role: str,
        action: str,
        document_id: str | None,
        details: str,
        reason_for_change: str,
    ) -> ConsentAuditLogEntity:
        pass

    @abstractmethod
    async def list_logs(
        self,
        document_id: str | None = None,
        actor_id: str | None = None,
        limit: int = 100,
    ) -> list[ConsentAuditLogEntity]:
        pass

    @abstractmethod
    async def save(self, entity: ConsentAuditLogEntity) -> ConsentAuditLogEntity:
        pass
