"""SQLAlchemy implementations of driven repository ports for eConsent microservice.

Complies with 21 CFR Part 11 auditing and GxP boolean filter standards (.is_(True)/.is_(False)).
"""

from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.models import (
    ComprehensionCheck,
    ConsentAuditLog,
    ConsentClause,
    ConsentDocument,
    ConsentSignature,
    ConsentTemplate,
    ConsentTranslation,
    ConsentWithdrawal,
    GranularConsentOption,
    ReconsentRequirement,
    SubjectConsent,
    SubjectConsentOptionSelection,
)
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
    TranslationStatus,
)
from apps.econsent.domain.ports import (
    IComprehensionRepository,
    IConsentAuditRepository,
    IConsentClauseRepository,
    IConsentSignatureRepository,
    IConsentTemplateRepository,
    IConsentTranslationRepository,
    IConsentWithdrawalRepository,
    IEConsentRepository,
    IGranularOptionRepository,
    IReconsentRepository,
    ISubjectConsentRepository,
)
from packages.database import map_database_exceptions


class SQLEConsentRepository(IEConsentRepository):
    """Legacy compatibility SQLAlchemy repository implementation."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> ConsentDocument | None:
        stmt = select(ConsentDocument).where(ConsentDocument.id == entity_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save(self, entity: ConsentDocument) -> ConsentDocument:
        self.session.add(entity)
        await self.session.flush()
        return entity


class SQLConsentClauseRepository(IConsentClauseRepository):
    """SQLAlchemy repository for Informed Consent Form clauses."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ConsentClauseEntity | None:
        stmt = select(ConsentClause).where(ConsentClause.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_latest_by_clause_id(
        self, clause_id: str
    ) -> ConsentClauseEntity | None:
        stmt = (
            select(ConsentClause)
            .where(ConsentClause.clause_id == clause_id)
            .order_by(desc(ConsentClause.version_index))
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_by_clause_and_version(
        self, clause_id: str, version_index: int
    ) -> ConsentClauseEntity | None:
        stmt = select(ConsentClause).where(
            ConsentClause.clause_id == clause_id,
            ConsentClause.version_index == version_index,
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def list_clauses(
        self,
        study_id: str | None = None,
        clause_id: str | None = None,
        all_versions: bool = False,
    ) -> list[ConsentClauseEntity]:
        stmt = select(ConsentClause)
        if study_id:
            stmt = stmt.where(ConsentClause.study_id == study_id)
        if clause_id:
            stmt = stmt.where(ConsentClause.clause_id == clause_id)
        if not all_versions:
            stmt = stmt.order_by(
                ConsentClause.clause_id, desc(ConsentClause.version_index)
            )
        else:
            stmt = stmt.order_by(desc(ConsentClause.version_index))
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    @map_database_exceptions
    async def save(self, entity: ConsentClauseEntity) -> ConsentClauseEntity:
        model = ConsentClause(
            id=entity.id,
            clause_id=entity.clause_id,
            study_id=entity.study_id,
            title=entity.title,
            text=entity.text,
            version_index=entity.version_index,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: ConsentClause) -> ConsentClauseEntity:
        return ConsentClauseEntity(
            id=row.id,
            clause_id=row.clause_id,
            study_id=row.study_id,
            title=row.title,
            text=row.text,
            version_index=row.version_index,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLConsentTemplateRepository(IConsentTemplateRepository):
    """SQLAlchemy repository for versioned consent templates."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ConsentTemplateEntity | None:
        stmt = select(ConsentTemplate).where(ConsentTemplate.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_latest_by_template_id(
        self, template_id: str
    ) -> ConsentTemplateEntity | None:
        stmt = (
            select(ConsentTemplate)
            .where(ConsentTemplate.template_id == template_id)
            .order_by(desc(ConsentTemplate.version_index))
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_by_template_and_version(
        self, template_id: str, version_index: int
    ) -> ConsentTemplateEntity | None:
        stmt = select(ConsentTemplate).where(
            ConsentTemplate.template_id == template_id,
            ConsentTemplate.version_index == version_index,
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def list_templates(
        self,
        study_id: str | None = None,
        template_id: str | None = None,
        all_versions: bool = False,
    ) -> list[ConsentTemplateEntity]:
        stmt = select(ConsentTemplate)
        if study_id:
            stmt = stmt.where(ConsentTemplate.study_id == study_id)
        if template_id:
            stmt = stmt.where(ConsentTemplate.template_id == template_id)
        if not all_versions:
            stmt = stmt.order_by(
                ConsentTemplate.template_id, desc(ConsentTemplate.version_index)
            )
        else:
            stmt = stmt.order_by(desc(ConsentTemplate.version_index))
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    @map_database_exceptions
    async def save(self, entity: ConsentTemplateEntity) -> ConsentTemplateEntity:
        # Check if existing row to update
        stmt = select(ConsentTemplate).where(ConsentTemplate.id == entity.id)
        res = await self.session.execute(stmt)
        existing = res.scalars().first()
        if existing:
            existing.template_name = entity.template_name
            existing.protocol_version = entity.protocol_version
            existing.is_published = entity.is_published
            existing.requires_reconsent = entity.requires_reconsent
            existing.clauses = entity.clauses
            existing.workflow_steps = entity.workflow_steps
            existing.reason_for_change = entity.reason_for_change
            await self.session.flush()
            return self._to_entity(existing)

        model = ConsentTemplate(
            id=entity.id,
            template_id=entity.template_id,
            study_id=entity.study_id,
            template_name=entity.template_name,
            protocol_version=entity.protocol_version,
            is_published=entity.is_published,
            requires_reconsent=entity.requires_reconsent,
            clauses=entity.clauses,
            workflow_steps=entity.workflow_steps,
            version_index=entity.version_index,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: ConsentTemplate) -> ConsentTemplateEntity:
        return ConsentTemplateEntity(
            id=row.id,
            template_id=row.template_id,
            study_id=row.study_id,
            template_name=row.template_name,
            protocol_version=row.protocol_version,
            is_published=row.is_published,
            requires_reconsent=row.requires_reconsent,
            clauses=row.clauses,
            workflow_steps=row.workflow_steps,
            version_index=row.version_index,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLConsentTranslationRepository(IConsentTranslationRepository):
    """SQLAlchemy repository for multilingual consent translations."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ConsentTranslationEntity | None:
        stmt = select(ConsentTranslation).where(ConsentTranslation.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_latest_by_translation_id(
        self, translation_id: str
    ) -> ConsentTranslationEntity | None:
        stmt = (
            select(ConsentTranslation)
            .where(ConsentTranslation.translation_id == translation_id)
            .order_by(desc(ConsentTranslation.version_index))
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_by_source(
        self,
        source_id: str,
        source_version_index: int,
        language_code: str,
        source_type: str = "TEMPLATE",
    ) -> ConsentTranslationEntity | None:
        stmt = (
            select(ConsentTranslation)
            .where(
                ConsentTranslation.source_id == source_id,
                ConsentTranslation.source_version_index == source_version_index,
                ConsentTranslation.language_code == language_code,
                ConsentTranslation.source_type == source_type.lower(),
            )
            .order_by(desc(ConsentTranslation.version_index))
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def list_translations(
        self,
        source_id: str | None = None,
        source_type: str | None = None,
        language_code: str | None = None,
        status: str | None = None,
        all_versions: bool = False,
    ) -> list[ConsentTranslationEntity]:
        stmt = select(ConsentTranslation)
        if source_id:
            stmt = stmt.where(ConsentTranslation.source_id == source_id)
        if source_type:
            stmt = stmt.where(ConsentTranslation.source_type == source_type.lower())
        if language_code:
            stmt = stmt.where(ConsentTranslation.language_code == language_code)
        if status:
            stmt = stmt.where(ConsentTranslation.status == status.upper())
        if not all_versions:
            stmt = stmt.order_by(
                ConsentTranslation.translation_id,
                desc(ConsentTranslation.version_index),
            )
        else:
            stmt = stmt.order_by(desc(ConsentTranslation.version_index))
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    @map_database_exceptions
    async def save(self, entity: ConsentTranslationEntity) -> ConsentTranslationEntity:
        stmt = select(ConsentTranslation).where(ConsentTranslation.id == entity.id)
        res = await self.session.execute(stmt)
        existing = res.scalars().first()
        if existing:
            existing.status = entity.status
            existing.translated_title = entity.translated_title
            existing.translated_text = entity.translated_text
            existing.reason_for_change = entity.reason_for_change
            await self.session.flush()
            return self._to_entity(existing)

        model = ConsentTranslation(
            id=entity.id,
            translation_id=entity.translation_id,
            source_id=entity.source_id,
            source_type=entity.source_type.lower(),
            source_version_index=entity.source_version_index,
            language_code=entity.language_code,
            translated_title=entity.translated_title,
            translated_text=entity.translated_text,
            status=entity.status,
            version_index=entity.version_index,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: ConsentTranslation) -> ConsentTranslationEntity:
        return ConsentTranslationEntity(
            id=row.id,
            translation_id=row.translation_id,
            source_id=row.source_id,
            source_type=row.source_type,
            source_version_index=row.source_version_index,
            language_code=row.language_code,
            translated_title=row.translated_title,
            translated_text=row.translated_text,
            status=TranslationStatus(row.status)
            if isinstance(row.status, str)
            else row.status,
            version_index=row.version_index,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLComprehensionRepository(IComprehensionRepository):
    """SQLAlchemy repository for comprehension check definitions."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ComprehensionCheckEntity | None:
        stmt = select(ComprehensionCheck).where(ComprehensionCheck.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_check(
        self, template_id: str, version_index: int
    ) -> ComprehensionCheckEntity | None:
        stmt = select(ComprehensionCheck).where(
            ComprehensionCheck.template_id == template_id,
            ComprehensionCheck.version_index == version_index,
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def save_check(
        self, entity: ComprehensionCheckEntity
    ) -> ComprehensionCheckEntity:
        return await self.save(entity)

    @map_database_exceptions
    async def save(self, entity: ComprehensionCheckEntity) -> ComprehensionCheckEntity:
        model = ComprehensionCheck(
            id=entity.id,
            template_id=entity.template_id,
            version_index=entity.version_index,
            questions=entity.questions,
            expected_answers=entity.expected_answers,
            threshold_policy=entity.threshold_policy,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: ComprehensionCheck) -> ComprehensionCheckEntity:
        return ComprehensionCheckEntity(
            id=row.id,
            template_id=row.template_id,
            version_index=row.version_index,
            questions=row.questions,
            expected_answers=row.expected_answers,
            threshold_policy=row.threshold_policy,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLSubjectConsentRepository(ISubjectConsentRepository):
    """SQLAlchemy repository for subject consent records."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> SubjectConsentEntity | None:
        stmt = select(SubjectConsent).where(SubjectConsent.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_latest_active_consent(
        self, study_id: str, subject_pseudonym: str
    ) -> SubjectConsentEntity | None:
        stmt = (
            select(SubjectConsent)
            .where(
                SubjectConsent.study_id == study_id,
                SubjectConsent.subject_pseudonym == subject_pseudonym,
                SubjectConsent.status == "ACTIVE",
            )
            .order_by(desc(SubjectConsent.created_at))
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def list_subject_consents(
        self,
        study_id: str,
        site_id: str | None = None,
        subject_pseudonym: str | None = None,
    ) -> list[SubjectConsentEntity]:
        stmt = select(SubjectConsent).where(SubjectConsent.study_id == study_id)
        if site_id:
            stmt = stmt.where(SubjectConsent.site_id == site_id)
        if subject_pseudonym:
            stmt = stmt.where(SubjectConsent.subject_pseudonym == subject_pseudonym)
        stmt = stmt.order_by(desc(SubjectConsent.created_at))
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    @map_database_exceptions
    async def save(self, entity: SubjectConsentEntity) -> SubjectConsentEntity:
        stmt = select(SubjectConsent).where(SubjectConsent.id == entity.id)
        res = await self.session.execute(stmt)
        existing = res.scalars().first()
        if existing:
            existing.status = getattr(entity, "status", existing.status)
            existing.reason_for_change = entity.reason_for_change
            await self.session.flush()
            return self._to_entity(existing)

        model = SubjectConsent(
            id=entity.id,
            subject_pseudonym=entity.subject_pseudonym,
            study_id=entity.study_id,
            site_id=entity.site_id,
            template_id=entity.template_id,
            version_index=entity.version_index,
            protocol_version=entity.protocol_version,
            source_content_identity=entity.source_content_identity,
            status=getattr(entity, "status", "ACTIVE"),
            server_timestamp=entity.server_timestamp,
            device_timestamp=entity.device_timestamp,
            signature_manifest=entity.signature_manifest,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: SubjectConsent) -> SubjectConsentEntity:
        return SubjectConsentEntity(
            id=row.id,
            subject_pseudonym=row.subject_pseudonym,
            study_id=row.study_id,
            site_id=row.site_id,
            template_id=row.template_id,
            version_index=row.version_index,
            protocol_version=row.protocol_version,
            source_content_identity=row.source_content_identity,
            server_timestamp=row.server_timestamp,
            device_timestamp=row.device_timestamp,
            signature_manifest=row.signature_manifest,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
            status=row.status,
        )


class SQLConsentSignatureRepository(IConsentSignatureRepository):
    """SQLAlchemy repository for 21 CFR Part 11 electronic signatures."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ConsentSignatureEntity | None:
        stmt = select(ConsentSignature).where(ConsentSignature.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_signatures_for_template_version(
        self, template_id: str, version_index: int, subject_pseudonym: str
    ) -> list[ConsentSignatureEntity]:
        stmt = (
            select(ConsentSignature)
            .where(
                ConsentSignature.template_id == template_id,
                ConsentSignature.version_index == version_index,
                ConsentSignature.subject_pseudonym == subject_pseudonym,
            )
            .order_by(ConsentSignature.signed_at)
        )
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    @map_database_exceptions
    async def save(self, entity: ConsentSignatureEntity) -> ConsentSignatureEntity:
        model = ConsentSignature(
            id=entity.id,
            template_id=entity.template_id,
            version_index=entity.version_index,
            subject_pseudonym=entity.subject_pseudonym,
            role=str(entity.role),
            signer_name=entity.signer_name,
            signer_email=entity.signer_email,
            meaning=entity.meaning,
            signature_data=entity.signature_data,
            signed_at=entity.signed_at,
            digest_sha256=entity.digest_sha256,
            lar_relationship=entity.lar_relationship,
            lar_authority_basis=entity.lar_authority_basis,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: ConsentSignature) -> ConsentSignatureEntity:
        return ConsentSignatureEntity(
            id=row.id,
            template_id=row.template_id,
            version_index=row.version_index,
            subject_pseudonym=row.subject_pseudonym,
            role=row.role,  # type: ignore[arg-type]
            signer_name=row.signer_name or row.subject_pseudonym,
            signer_email=row.signer_email,
            meaning=row.meaning or "Consent Execution",
            signature_data=row.signature_data,
            signed_at=row.signed_at,
            digest_sha256=row.digest_sha256,
            lar_relationship=row.lar_relationship,
            lar_authority_basis=row.lar_authority_basis,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLGranularOptionRepository(IGranularOptionRepository):
    """SQLAlchemy repository for granular optional consent items."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> GranularConsentOptionEntity | None:
        stmt = select(GranularConsentOption).where(GranularConsentOption.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def list_options_for_template(
        self, template_id: str, version_index: int
    ) -> list[GranularConsentOptionEntity]:
        stmt = select(GranularConsentOption).where(
            GranularConsentOption.template_id == template_id,
            GranularConsentOption.version_index == version_index,
        )
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    @map_database_exceptions
    async def save_option(
        self, entity: GranularConsentOptionEntity
    ) -> GranularConsentOptionEntity:
        return await self.save(entity)

    @map_database_exceptions
    async def save(
        self, entity: GranularConsentOptionEntity
    ) -> GranularConsentOptionEntity:
        model = GranularConsentOption(
            id=entity.id,
            template_id=entity.template_id,
            version_index=entity.version_index,
            option_code=entity.option_code,
            title=entity.title,
            description=entity.description,
            category=str(entity.category),
            is_mandatory=entity.is_mandatory,
            default_selected=entity.default_selected,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    @map_database_exceptions
    async def save_selections(
        self, selections: list[GranularOptionSelectionEntity]
    ) -> list[GranularOptionSelectionEntity]:
        out = []
        for sel in selections:
            model = SubjectConsentOptionSelection(
                id=sel.id,
                consent_id=sel.consent_id,
                subject_pseudonym=sel.subject_pseudonym,
                option_code=sel.option_code,
                selected=sel.selected,
                selected_at=sel.selected_at,
                created_by=sel.created_by,
                reason_for_change=sel.reason_for_change,
            )
            self.session.add(model)
            out.append(sel)
        await self.session.flush()
        return out

    @map_database_exceptions
    async def get_selections_for_consent(
        self, consent_id: str
    ) -> list[GranularOptionSelectionEntity]:
        stmt = select(SubjectConsentOptionSelection).where(
            SubjectConsentOptionSelection.consent_id == consent_id
        )
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [
            GranularOptionSelectionEntity(
                id=r.id,
                consent_id=r.consent_id,
                subject_pseudonym=r.subject_pseudonym,
                option_code=r.option_code,
                selected=r.selected,
                selected_at=r.selected_at,
                created_by=r.created_by,
                reason_for_change=r.reason_for_change,
            )
            for r in rows
        ]

    def _to_entity(self, row: GranularConsentOption) -> GranularConsentOptionEntity:
        return GranularConsentOptionEntity(
            id=row.id,
            template_id=row.template_id,
            version_index=row.version_index,
            option_code=row.option_code,
            title=row.title,
            description=row.description,
            category=row.category,  # type: ignore[arg-type]
            is_mandatory=row.is_mandatory,
            default_selected=row.default_selected,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLReconsentRepository(IReconsentRepository):
    """SQLAlchemy repository for tracking re-consent requirements."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ReconsentRequirementEntity | None:
        stmt = select(ReconsentRequirement).where(ReconsentRequirement.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_pending_requirements(
        self, study_id: str, subject_pseudonym: str | None = None
    ) -> list[ReconsentRequirementEntity]:
        stmt = select(ReconsentRequirement).where(
            ReconsentRequirement.study_id == study_id,
            ReconsentRequirement.status == "PENDING",
        )
        if subject_pseudonym:
            stmt = stmt.where(
                ReconsentRequirement.subject_pseudonym == subject_pseudonym
            )
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    @map_database_exceptions
    async def save_requirement(
        self, entity: ReconsentRequirementEntity
    ) -> ReconsentRequirementEntity:
        return await self.save(entity)

    @map_database_exceptions
    async def save(
        self, entity: ReconsentRequirementEntity
    ) -> ReconsentRequirementEntity:
        model = ReconsentRequirement(
            id=entity.id,
            study_id=entity.study_id,
            site_id=entity.site_id,
            template_id=entity.template_id,
            prior_version_index=entity.prior_version_index,
            new_version_index=entity.new_version_index,
            subject_pseudonym=entity.subject_pseudonym,
            status=str(entity.status),
            change_summary=entity.change_summary,
            substantive_changes=entity.substantive_changes,
            deadline_at=entity.deadline_at,
            completed_consent_id=entity.completed_consent_id,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: ReconsentRequirement) -> ReconsentRequirementEntity:
        return ReconsentRequirementEntity(
            id=row.id,
            study_id=row.study_id,
            site_id=row.site_id,
            template_id=row.template_id,
            prior_version_index=row.prior_version_index,
            new_version_index=row.new_version_index,
            subject_pseudonym=row.subject_pseudonym,
            status=row.status,  # type: ignore[arg-type]
            change_summary=row.change_summary,
            substantive_changes=row.substantive_changes,
            deadline_at=row.deadline_at,
            completed_consent_id=row.completed_consent_id,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLConsentWithdrawalRepository(IConsentWithdrawalRepository):
    """SQLAlchemy repository for subject consent revocations."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ConsentWithdrawalEntity | None:
        stmt = select(ConsentWithdrawal).where(ConsentWithdrawal.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def get_withdrawal(
        self, study_id: str, subject_pseudonym: str
    ) -> ConsentWithdrawalEntity | None:
        stmt = (
            select(ConsentWithdrawal)
            .where(
                ConsentWithdrawal.study_id == study_id,
                ConsentWithdrawal.subject_pseudonym == subject_pseudonym,
            )
            .order_by(desc(ConsentWithdrawal.withdrawal_date))
        )
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def save_withdrawal(
        self, entity: ConsentWithdrawalEntity
    ) -> ConsentWithdrawalEntity:
        return await self.save(entity)

    @map_database_exceptions
    async def save(self, entity: ConsentWithdrawalEntity) -> ConsentWithdrawalEntity:
        model = ConsentWithdrawal(
            id=entity.id,
            study_id=entity.study_id,
            site_id=entity.site_id,
            subject_pseudonym=entity.subject_pseudonym,
            template_id=entity.template_id,
            withdrawal_date=entity.withdrawal_date,
            reason_category=entity.reason_category,
            reason_detail=entity.reason_detail,
            scope=str(entity.scope),
            acknowledged_by_investigator=entity.acknowledged_by_investigator,
            investigator_id=entity.investigator_id,
            created_at=entity.created_at,
            created_by=entity.created_by,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_entity(model)

    def _to_entity(self, row: ConsentWithdrawal) -> ConsentWithdrawalEntity:
        return ConsentWithdrawalEntity(
            id=row.id,
            study_id=row.study_id,
            site_id=row.site_id,
            subject_pseudonym=row.subject_pseudonym,
            template_id=row.template_id,
            withdrawal_date=row.withdrawal_date,
            reason_category=row.reason_category,
            reason_detail=row.reason_detail,
            scope=row.scope,  # type: ignore[arg-type]
            acknowledged_by_investigator=row.acknowledged_by_investigator,
            investigator_id=row.investigator_id,
            created_at=row.created_at,
            created_by=row.created_by,
            reason_for_change=row.reason_for_change,
        )


class SQLConsentAuditRepository(IConsentAuditRepository):
    """SQLAlchemy repository for 21 CFR Part 11 audit trails."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, id: str) -> ConsentAuditLogEntity | None:
        stmt = select(ConsentAuditLog).where(ConsentAuditLog.id == id)
        res = await self.session.execute(stmt)
        row = res.scalars().first()
        return self._to_entity(row) if row else None

    @map_database_exceptions
    async def save(self, entity: ConsentAuditLogEntity) -> ConsentAuditLogEntity:
        log_entry = ConsentAuditLog(
            id=entity.id,
            timestamp=entity.timestamp,
            actor_id=entity.actor_id,
            actor_role=entity.actor_role,
            action=entity.action,
            document_id=entity.document_id,
            details=entity.details,
            reason_for_change=entity.reason_for_change,
        )
        self.session.add(log_entry)
        await self.session.flush()
        return self._to_entity(log_entry)

    @map_database_exceptions
    async def log_action(
        self,
        actor_id: str,
        actor_role: str,
        action: str,
        document_id: str | None,
        details: str,
        reason_for_change: str,
    ) -> ConsentAuditLogEntity:
        log_entry = ConsentAuditLog(
            actor_id=actor_id,
            actor_role=actor_role,
            action=action,
            document_id=document_id,
            details=details,
            reason_for_change=reason_for_change,
        )
        self.session.add(log_entry)
        await self.session.flush()
        return self._to_entity(log_entry)

    @map_database_exceptions
    async def list_logs(
        self,
        document_id: str | None = None,
        actor_id: str | None = None,
        limit: int = 100,
    ) -> list[ConsentAuditLogEntity]:
        stmt = select(ConsentAuditLog)
        if document_id:
            stmt = stmt.where(ConsentAuditLog.document_id == document_id)
        if actor_id:
            stmt = stmt.where(ConsentAuditLog.actor_id == actor_id)
        stmt = stmt.order_by(desc(ConsentAuditLog.timestamp)).limit(limit)
        res = await self.session.execute(stmt)
        rows = res.scalars().all()
        return [self._to_entity(r) for r in rows]

    def _to_entity(self, row: ConsentAuditLog) -> ConsentAuditLogEntity:
        return ConsentAuditLogEntity(
            id=row.id,
            timestamp=row.timestamp,
            actor_id=row.actor_id,
            actor_role=row.actor_role,
            action=row.action,
            document_id=row.document_id,
            details=row.details,
            reason_for_change=row.reason_for_change,
        )
