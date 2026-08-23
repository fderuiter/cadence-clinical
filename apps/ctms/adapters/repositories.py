import uuid
from datetime import UTC, datetime
from typing import Any

from fastapi import Depends
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.ctms.adapters.database import db_manager
from apps.ctms.adapters.models import (
    BudgetAdjustment,
    CountryPrerequisiteRule,
    CountryRegulatoryMilestone,
    CurrencyExchangeRate,
    DeviationActionItem,
    EssentialDocument,
    ETMFSyncRecord,
    FinancialInvoice,
    GrantContractAmendment,
    IntegrationOutbox,
    InvestigatorGrant,
    IPDestructionCertificate,
    IPKitRecord,
    IPTemperatureExcursion,
    ProcedurePaymentGrid,
    ProtocolDeviation,
    RBQMKRIMetric,
    SiteBudgetTemplate,
    SiteGreenlightGate,
    SiteRiskScore,
)
from apps.ctms.domain.models import (
    BudgetAdjustmentEntity,
    CountryPrerequisiteRuleEntity,
    CountryRegulatoryMilestoneEntity,
    CurrencyExchangeRateEntity,
    DeviationActionItemEntity,
    EssentialDocumentEntity,
    ETMFSyncRecordEntity,
    FinancialInvoiceEntity,
    GrantContractAmendmentEntity,
    InvestigatorGrantEntity,
    IPDestructionCertificateEntity,
    IPKitRecordEntity,
    IPTemperatureExcursionEntity,
    ProcedurePaymentGridEntity,
    ProtocolDeviationEntity,
    RBQMKRIMetricEntity,
    SiteBudgetTemplateEntity,
    SiteGreenlightGateEntity,
    SiteRiskScoreEntity,
)
from apps.ctms.domain.ports import (
    IETMFSyncRepository,
    IFinancialsRepository,
    IIPAccountabilityRepository,
    IProtocolDeviationRepository,
    IRBQMRepository,
    ISiteStartupRepository,
)
from apps.ctms.infrastructure.repositories.ctms_delegation_repository import (
    SQLAlchemyCTMSDelegationRepository,
)
from packages.database import DatabaseSessionDependency, map_database_exceptions

get_db_session = DatabaseSessionDependency(db_manager)

SQLAlchemCTMSDelegationRepository = SQLAlchemyCTMSDelegationRepository


class SQLAlchemySiteStartupRepository(ISiteStartupRepository):
    """SQLAlchemy implementation of ISiteStartupRepository."""

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Any | None:
        return await self.get_essential_document(entity_id)

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        return entity

    @map_database_exceptions
    async def save_country_milestone(
        self, milestone: CountryRegulatoryMilestoneEntity
    ) -> CountryRegulatoryMilestoneEntity:
        if milestone.id:
            stmt = select(CountryRegulatoryMilestone).where(
                CountryRegulatoryMilestone.id.is_(milestone.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.status = milestone.status
                model.actual_date = (
                    datetime.fromisoformat(milestone.actual_date)
                    if milestone.actual_date
                    else None
                )
                model.approval_number = milestone.approval_number
                model.authority_name = milestone.authority_name
                model.reason_for_change = milestone.reason_for_change
                model.version_index = milestone.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_country_milestone_entity(model)

        model = CountryRegulatoryMilestone(
            study_id=milestone.study_id,
            country_code=milestone.country_code,
            milestone_type=milestone.milestone_type,
            planned_date=(
                datetime.fromisoformat(milestone.planned_date)
                if milestone.planned_date
                else None
            ),
            actual_date=(
                datetime.fromisoformat(milestone.actual_date)
                if milestone.actual_date
                else None
            ),
            status=milestone.status,
            approval_number=milestone.approval_number,
            authority_name=milestone.authority_name,
            created_by=milestone.created_by,
            reason_for_change=milestone.reason_for_change,
            version_index=milestone.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_country_milestone_entity(model)

    @map_database_exceptions
    async def list_country_milestones(
        self, study_id: str, country_code: str | None = None
    ) -> list[CountryRegulatoryMilestoneEntity]:
        stmt = select(CountryRegulatoryMilestone).where(
            CountryRegulatoryMilestone.study_id.is_(study_id)
        )
        if country_code:
            stmt = stmt.where(CountryRegulatoryMilestone.country_code.is_(country_code))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_country_milestone_entity(m) for m in models]

    @map_database_exceptions
    async def save_essential_document(
        self, document: EssentialDocumentEntity
    ) -> EssentialDocumentEntity:
        if document.id:
            stmt = select(EssentialDocument).where(
                EssentialDocument.id.is_(document.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.status = document.status
                model.review_notes = document.review_notes
                model.reviewed_by = document.reviewed_by
                model.reviewed_at = (
                    datetime.fromisoformat(document.reviewed_at)
                    if document.reviewed_at
                    else None
                )
                model.reason_for_change = document.reason_for_change
                model.version_index = document.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_essential_document_entity(model)

        model = EssentialDocument(
            study_id=document.study_id,
            site_id=document.site_id,
            document_type=document.document_type,
            file_name=document.file_name,
            file_hash=document.file_hash,
            expiration_date=(
                datetime.fromisoformat(document.expiration_date)
                if document.expiration_date
                else None
            ),
            status=document.status,
            review_notes=document.review_notes,
            reviewed_by=document.reviewed_by,
            reviewed_at=(
                datetime.fromisoformat(document.reviewed_at)
                if document.reviewed_at
                else None
            ),
            created_by=document.created_by,
            reason_for_change=document.reason_for_change,
            version_index=document.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_essential_document_entity(model)

    @map_database_exceptions
    async def get_essential_document(
        self, doc_id: str
    ) -> EssentialDocumentEntity | None:
        stmt = select(EssentialDocument).where(EssentialDocument.id.is_(doc_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_essential_document_entity(model)

    @map_database_exceptions
    async def list_essential_documents(
        self, study_id: str, site_id: str | None = None
    ) -> list[EssentialDocumentEntity]:
        stmt = select(EssentialDocument).where(EssentialDocument.study_id.is_(study_id))
        if site_id:
            stmt = stmt.where(EssentialDocument.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_essential_document_entity(m) for m in models]

    @map_database_exceptions
    async def save_greenlight_gate(
        self, gate: SiteGreenlightGateEntity
    ) -> SiteGreenlightGateEntity:
        stmt = select(SiteGreenlightGate).where(
            SiteGreenlightGate.site_id.is_(gate.site_id)
        )
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if model:
            model.country_code = gate.country_code
            model.overall_status = gate.overall_status
            model.facet_regulatory_irb = gate.facet_regulatory_irb
            model.facet_cta_budget = gate.facet_cta_budget
            model.facet_eisf_baseline = gate.facet_eisf_baseline
            model.facet_doa_log = gate.facet_doa_log
            model.facet_staff_training = gate.facet_staff_training
            model.facet_ip_release = gate.facet_ip_release
            model.contract_approved = gate.contract_approved
            model.irb_approved = gate.irb_approved
            model.form_1572_approved = gate.form_1572_approved
            model.doa_signed_off = gate.doa_signed_off
            model.ip_ready = gate.ip_ready
            model.greenlight_token = gate.greenlight_token
            model.greenlight_certified_by = gate.greenlight_certified_by
            model.greenlight_certified_at = (
                datetime.fromisoformat(gate.greenlight_certified_at)
                if gate.greenlight_certified_at
                else None
            )
            model.is_overridden = gate.is_overridden
            model.override_sponsor_user_id = gate.override_sponsor_user_id
            model.override_medical_user_id = gate.override_medical_user_id
            model.override_justification = gate.override_justification
            model.override_authorized_at = (
                datetime.fromisoformat(gate.override_authorized_at)
                if gate.override_authorized_at
                else None
            )
            model.evaluation_details = gate.evaluation_details
            model.rejection_reason = gate.rejection_reason
            model.reason_for_change = gate.reason_for_change
            model.version_index = gate.version_index
            self.session.add(model)
            await self.session.flush()
            return self._to_greenlight_gate_entity(model)

        model = SiteGreenlightGate(
            study_id=gate.study_id,
            site_id=gate.site_id,
            country_code=gate.country_code,
            overall_status=gate.overall_status,
            facet_regulatory_irb=gate.facet_regulatory_irb,
            facet_cta_budget=gate.facet_cta_budget,
            facet_eisf_baseline=gate.facet_eisf_baseline,
            facet_doa_log=gate.facet_doa_log,
            facet_staff_training=gate.facet_staff_training,
            facet_ip_release=gate.facet_ip_release,
            contract_approved=gate.contract_approved,
            irb_approved=gate.irb_approved,
            form_1572_approved=gate.form_1572_approved,
            doa_signed_off=gate.doa_signed_off,
            ip_ready=gate.ip_ready,
            greenlight_token=gate.greenlight_token,
            greenlight_certified_by=gate.greenlight_certified_by,
            greenlight_certified_at=(
                datetime.fromisoformat(gate.greenlight_certified_at)
                if gate.greenlight_certified_at
                else None
            ),
            is_overridden=gate.is_overridden,
            override_sponsor_user_id=gate.override_sponsor_user_id,
            override_medical_user_id=gate.override_medical_user_id,
            override_justification=gate.override_justification,
            override_authorized_at=(
                datetime.fromisoformat(gate.override_authorized_at)
                if gate.override_authorized_at
                else None
            ),
            evaluation_details=gate.evaluation_details,
            rejection_reason=gate.rejection_reason,
            created_by=gate.created_by,
            reason_for_change=gate.reason_for_change,
            version_index=gate.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_greenlight_gate_entity(model)

    @map_database_exceptions
    async def get_greenlight_gate(
        self, site_id: str
    ) -> SiteGreenlightGateEntity | None:
        stmt = select(SiteGreenlightGate).where(SiteGreenlightGate.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_greenlight_gate_entity(model)

    @map_database_exceptions
    async def save_country_prerequisite_rule(
        self, rule: CountryPrerequisiteRuleEntity
    ) -> CountryPrerequisiteRuleEntity:
        if rule.id:
            stmt = select(CountryPrerequisiteRule).where(
                CountryPrerequisiteRule.id.is_(rule.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.facet = rule.facet
                model.criterion_code = rule.criterion_code
                model.criterion_name = rule.criterion_name
                model.is_mandatory = rule.is_mandatory
                model.description = rule.description
                model.reason_for_change = rule.reason_for_change
                model.version_index = rule.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_country_prerequisite_rule_entity(model)

        model = CountryPrerequisiteRule(
            study_id=rule.study_id,
            country_code=rule.country_code.upper(),
            facet=rule.facet,
            criterion_code=rule.criterion_code,
            criterion_name=rule.criterion_name,
            is_mandatory=rule.is_mandatory,
            description=rule.description,
            created_by=rule.created_by,
            reason_for_change=rule.reason_for_change,
            version_index=rule.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_country_prerequisite_rule_entity(model)

    @map_database_exceptions
    async def list_country_prerequisite_rules(
        self, study_id: str, country_code: str | None = None
    ) -> list[CountryPrerequisiteRuleEntity]:
        stmt = select(CountryPrerequisiteRule).where(
            CountryPrerequisiteRule.study_id.is_(study_id)
        )
        if country_code:
            stmt = stmt.where(
                CountryPrerequisiteRule.country_code.in_(
                    [country_code.upper(), "GLOBAL"]
                )
            )
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_country_prerequisite_rule_entity(m) for m in models]

    @map_database_exceptions
    async def save_outbox_event(
        self,
        event_type: str,
        payload: dict[str, Any],
        correlation_id: str,
        created_by: str,
        reason_for_change: str,
    ) -> None:
        outbox = IntegrationOutbox(
            event_type=event_type,
            payload=payload,
            correlation_id=correlation_id,
            status="PENDING",
            created_by=created_by,
            reason_for_change=reason_for_change,
        )
        self.session.add(outbox)
        await self.session.flush()

    def _to_country_prerequisite_rule_entity(
        self, model: CountryPrerequisiteRule
    ) -> CountryPrerequisiteRuleEntity:
        return CountryPrerequisiteRuleEntity(
            id=model.id,
            study_id=model.study_id,
            country_code=model.country_code,
            facet=model.facet,
            criterion_code=model.criterion_code,
            criterion_name=model.criterion_name,
            is_mandatory=model.is_mandatory,
            description=model.description,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_country_milestone_entity(
        self, model: CountryRegulatoryMilestone
    ) -> CountryRegulatoryMilestoneEntity:
        return CountryRegulatoryMilestoneEntity(
            id=model.id,
            study_id=model.study_id,
            country_code=model.country_code,
            milestone_type=model.milestone_type,
            planned_date=model.planned_date.isoformat() if model.planned_date else None,
            actual_date=model.actual_date.isoformat() if model.actual_date else None,
            status=model.status,
            approval_number=model.approval_number,
            authority_name=model.authority_name,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_essential_document_entity(
        self, model: EssentialDocument
    ) -> EssentialDocumentEntity:
        return EssentialDocumentEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            document_type=model.document_type,
            file_name=model.file_name,
            file_hash=model.file_hash,
            expiration_date=(
                model.expiration_date.isoformat() if model.expiration_date else None
            ),
            status=model.status,
            review_notes=model.review_notes,
            reviewed_by=model.reviewed_by,
            reviewed_at=(model.reviewed_at.isoformat() if model.reviewed_at else None),
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_greenlight_gate_entity(
        self, model: SiteGreenlightGate
    ) -> SiteGreenlightGateEntity:
        return SiteGreenlightGateEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            country_code=model.country_code,
            overall_status=model.overall_status,
            facet_regulatory_irb=model.facet_regulatory_irb,
            facet_cta_budget=model.facet_cta_budget,
            facet_eisf_baseline=model.facet_eisf_baseline,
            facet_doa_log=model.facet_doa_log,
            facet_staff_training=model.facet_staff_training,
            facet_ip_release=model.facet_ip_release,
            contract_approved=model.contract_approved,
            irb_approved=model.irb_approved,
            form_1572_approved=model.form_1572_approved,
            doa_signed_off=model.doa_signed_off,
            ip_ready=model.ip_ready,
            greenlight_token=model.greenlight_token,
            greenlight_certified_by=model.greenlight_certified_by,
            greenlight_certified_at=(
                model.greenlight_certified_at.isoformat()
                if model.greenlight_certified_at
                else None
            ),
            is_overridden=model.is_overridden,
            override_sponsor_user_id=model.override_sponsor_user_id,
            override_medical_user_id=model.override_medical_user_id,
            override_justification=model.override_justification,
            override_authorized_at=(
                model.override_authorized_at.isoformat()
                if model.override_authorized_at
                else None
            ),
            evaluation_details=model.evaluation_details,
            rejection_reason=model.rejection_reason,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )


class SQLAlchemyProtocolDeviationRepository(IProtocolDeviationRepository):
    """SQLAlchemy implementation of IProtocolDeviationRepository."""

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> ProtocolDeviationEntity | None:
        return await self.get_deviation(entity_id)

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        if isinstance(entity, ProtocolDeviationEntity):
            return await self.save_deviation(entity)
        if isinstance(entity, DeviationActionItemEntity):
            return await self.save_action_item(entity)
        return entity

    @map_database_exceptions
    async def save_deviation(
        self, deviation: ProtocolDeviationEntity
    ) -> ProtocolDeviationEntity:
        if deviation.id:
            stmt = select(ProtocolDeviation).where(
                ProtocolDeviation.id.is_(deviation.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.status = deviation.status
                model.root_cause_5whys = deviation.root_cause_5whys
                model.root_cause_summary = deviation.root_cause_summary
                model.corrective_action_plan = deviation.corrective_action_plan
                model.preventive_action_plan = deviation.preventive_action_plan
                model.quality_capa_id = deviation.quality_capa_id
                model.resolved_by = deviation.resolved_by
                model.resolved_at = (
                    datetime.fromisoformat(deviation.resolved_at)
                    if deviation.resolved_at
                    else None
                )
                model.reason_for_change = deviation.reason_for_change
                model.version_index = deviation.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_deviation_entity(model)

        model = ProtocolDeviation(
            study_id=deviation.study_id,
            site_id=deviation.site_id,
            subject_id=deviation.subject_id,
            visit_id=deviation.visit_id,
            deviation_category=deviation.deviation_category,
            severity=deviation.severity,
            title=deviation.title,
            description=deviation.description,
            date_occurred=datetime.fromisoformat(deviation.date_occurred),
            date_identified=datetime.fromisoformat(deviation.date_identified),
            status=deviation.status,
            root_cause_5whys=deviation.root_cause_5whys,
            root_cause_summary=deviation.root_cause_summary,
            corrective_action_plan=deviation.corrective_action_plan,
            preventive_action_plan=deviation.preventive_action_plan,
            quality_capa_id=deviation.quality_capa_id,
            reported_by=deviation.reported_by,
            resolved_by=deviation.resolved_by,
            resolved_at=(
                datetime.fromisoformat(deviation.resolved_at)
                if deviation.resolved_at
                else None
            ),
            created_by=deviation.created_by,
            reason_for_change=deviation.reason_for_change,
            version_index=deviation.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_deviation_entity(model)

    @map_database_exceptions
    async def get_deviation(self, deviation_id: str) -> ProtocolDeviationEntity | None:
        stmt = select(ProtocolDeviation).where(ProtocolDeviation.id.is_(deviation_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_deviation_entity(model)

    @map_database_exceptions
    async def list_deviations(
        self,
        study_id: str,
        site_id: str | None = None,
        severity: str | None = None,
    ) -> list[ProtocolDeviationEntity]:
        stmt = select(ProtocolDeviation).where(ProtocolDeviation.study_id.is_(study_id))
        if site_id:
            stmt = stmt.where(ProtocolDeviation.site_id.is_(site_id))
        if severity:
            stmt = stmt.where(ProtocolDeviation.severity.is_(severity))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_deviation_entity(m) for m in models]

    @map_database_exceptions
    async def save_action_item(
        self, action_item: DeviationActionItemEntity
    ) -> DeviationActionItemEntity:
        if action_item.id:
            stmt = select(DeviationActionItem).where(
                DeviationActionItem.id.is_(action_item.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.status = action_item.status
                model.resolution_notes = action_item.resolution_notes
                model.completed_by = action_item.completed_by
                model.completed_at = (
                    datetime.fromisoformat(action_item.completed_at)
                    if action_item.completed_at
                    else None
                )
                model.reason_for_change = action_item.reason_for_change
                model.version_index = action_item.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_action_item_entity(model)

        model = DeviationActionItem(
            deviation_id=action_item.deviation_id,
            site_id=action_item.site_id,
            description=action_item.description,
            assignee_user_id=action_item.assignee_user_id,
            assignee_role=action_item.assignee_role,
            due_date=datetime.fromisoformat(action_item.due_date),
            status=action_item.status,
            resolution_notes=action_item.resolution_notes,
            completed_by=action_item.completed_by,
            completed_at=(
                datetime.fromisoformat(action_item.completed_at)
                if action_item.completed_at
                else None
            ),
            created_by=action_item.created_by,
            reason_for_change=action_item.reason_for_change,
            version_index=action_item.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_action_item_entity(model)

    @map_database_exceptions
    async def get_action_item(
        self, action_item_id: str
    ) -> DeviationActionItemEntity | None:
        stmt = select(DeviationActionItem).where(
            DeviationActionItem.id.is_(action_item_id)
        )
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_action_item_entity(model)

    @map_database_exceptions
    async def list_action_items(
        self, deviation_id: str | None = None, site_id: str | None = None
    ) -> list[DeviationActionItemEntity]:
        stmt = select(DeviationActionItem)
        if deviation_id:
            stmt = stmt.where(DeviationActionItem.deviation_id.is_(deviation_id))
        if site_id:
            stmt = stmt.where(DeviationActionItem.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_action_item_entity(m) for m in models]

    def _to_deviation_entity(self, model: ProtocolDeviation) -> ProtocolDeviationEntity:
        return ProtocolDeviationEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            subject_id=model.subject_id,
            visit_id=model.visit_id,
            deviation_category=model.deviation_category,
            severity=model.severity,
            title=model.title,
            description=model.description,
            date_occurred=model.date_occurred.isoformat(),
            date_identified=model.date_identified.isoformat(),
            status=model.status,
            root_cause_5whys=model.root_cause_5whys or [],
            root_cause_summary=model.root_cause_summary,
            corrective_action_plan=model.corrective_action_plan,
            preventive_action_plan=model.preventive_action_plan,
            quality_capa_id=model.quality_capa_id,
            reported_by=model.reported_by,
            resolved_by=model.resolved_by,
            resolved_at=model.resolved_at.isoformat() if model.resolved_at else None,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_action_item_entity(
        self, model: DeviationActionItem
    ) -> DeviationActionItemEntity:
        return DeviationActionItemEntity(
            id=model.id,
            deviation_id=model.deviation_id,
            site_id=model.site_id,
            description=model.description,
            assignee_user_id=model.assignee_user_id,
            assignee_role=model.assignee_role,
            due_date=model.due_date.isoformat(),
            status=model.status,
            resolution_notes=model.resolution_notes,
            completed_by=model.completed_by,
            completed_at=model.completed_at.isoformat() if model.completed_at else None,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )


class SQLAlchemyRBQMRepository(IRBQMRepository):
    """SQLAlchemy implementation of IRBQMRepository."""

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Any | None:
        return await self.get_latest_site_risk_score(entity_id)

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        return entity

    @map_database_exceptions
    async def save_kri_metric(self, metric: RBQMKRIMetricEntity) -> RBQMKRIMetricEntity:
        model = RBQMKRIMetric(
            study_id=metric.study_id,
            site_id=metric.site_id,
            metric_type=metric.metric_type,
            metric_value=metric.metric_value,
            threshold_low=metric.threshold_low,
            threshold_high=metric.threshold_high,
            breach_status=metric.breach_status,
            calculation_date=datetime.fromisoformat(metric.calculation_date),
            notes=metric.notes,
            created_by=metric.created_by,
            reason_for_change=metric.reason_for_change,
            version_index=metric.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_kri_entity(model)

    @map_database_exceptions
    async def list_kri_metrics(
        self, study_id: str, site_id: str | None = None
    ) -> list[RBQMKRIMetricEntity]:
        stmt = select(RBQMKRIMetric).where(RBQMKRIMetric.study_id.is_(study_id))
        if site_id:
            stmt = stmt.where(RBQMKRIMetric.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_kri_entity(m) for m in models]

    @map_database_exceptions
    async def save_site_risk_score(
        self, score: SiteRiskScoreEntity
    ) -> SiteRiskScoreEntity:
        model = SiteRiskScore(
            study_id=score.study_id,
            site_id=score.site_id,
            composite_score=score.composite_score,
            risk_level=score.risk_level,
            assessment_date=datetime.fromisoformat(score.assessment_date),
            recommended_monitoring_type=score.recommended_monitoring_type,
            monitoring_interval_days=score.monitoring_interval_days,
            created_by=score.created_by,
            reason_for_change=score.reason_for_change,
            version_index=score.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_risk_score_entity(model)

    @map_database_exceptions
    async def get_latest_site_risk_score(
        self, site_id: str
    ) -> SiteRiskScoreEntity | None:
        stmt = (
            select(SiteRiskScore)
            .where(SiteRiskScore.site_id.is_(site_id))
            .order_by(SiteRiskScore.assessment_date.desc())
        )
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_risk_score_entity(model)

    def _to_kri_entity(self, model: RBQMKRIMetric) -> RBQMKRIMetricEntity:
        return RBQMKRIMetricEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            metric_type=model.metric_type,
            metric_value=model.metric_value,
            threshold_low=model.threshold_low,
            threshold_high=model.threshold_high,
            breach_status=model.breach_status,
            calculation_date=model.calculation_date.isoformat(),
            notes=model.notes,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_risk_score_entity(self, model: SiteRiskScore) -> SiteRiskScoreEntity:
        return SiteRiskScoreEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            composite_score=model.composite_score,
            risk_level=model.risk_level,
            assessment_date=model.assessment_date.isoformat(),
            recommended_monitoring_type=model.recommended_monitoring_type,
            monitoring_interval_days=model.monitoring_interval_days,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )


class SQLAlchemyFinancialsRepository(IFinancialsRepository):
    """SQLAlchemy implementation of IFinancialsRepository."""

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> FinancialInvoiceEntity | None:
        return await self.get_invoice(entity_id)

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        if isinstance(entity, FinancialInvoiceEntity):
            return await self.save_invoice(entity)
        if isinstance(entity, ProcedurePaymentGridEntity):
            return await self.save_procedure_grid(entity)
        if isinstance(entity, SiteBudgetTemplateEntity):
            return await self.save_budget_template(entity)
        if isinstance(entity, GrantContractAmendmentEntity):
            return await self.save_grant_amendment(entity)
        if isinstance(entity, CurrencyExchangeRateEntity):
            return await self.save_exchange_rate(entity)
        if isinstance(entity, BudgetAdjustmentEntity):
            return await self.save_budget_adjustment(entity)
        return entity

    @map_database_exceptions
    async def save_budget_template(
        self, template: SiteBudgetTemplateEntity
    ) -> SiteBudgetTemplateEntity:
        if template.id:
            stmt = select(SiteBudgetTemplate).where(
                SiteBudgetTemplate.id.is_(template.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.template_name = template.template_name
                model.study_id = template.study_id
                model.tier_level = template.tier_level
                model.currency = template.currency
                model.overhead_percentage = template.overhead_percentage
                model.withholding_percentage = template.withholding_percentage
                model.fixed_startup_fee = template.fixed_startup_fee
                model.fixed_closeout_fee = template.fixed_closeout_fee
                model.irb_fee = template.irb_fee
                model.patient_stipend_amount = template.patient_stipend_amount
                model.travel_reimbursement_rate = template.travel_reimbursement_rate
                model.fixed_fees = template.fixed_fees
                model.procedure_items = template.procedure_items
                model.is_active = template.is_active
                model.reason_for_change = template.reason_for_change
                model.version_index = template.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_budget_template_entity(model)

        model = SiteBudgetTemplate(
            template_name=template.template_name,
            study_id=template.study_id,
            tier_level=template.tier_level,
            currency=template.currency,
            overhead_percentage=template.overhead_percentage,
            withholding_percentage=template.withholding_percentage,
            fixed_startup_fee=template.fixed_startup_fee,
            fixed_closeout_fee=template.fixed_closeout_fee,
            irb_fee=template.irb_fee,
            patient_stipend_amount=template.patient_stipend_amount,
            travel_reimbursement_rate=template.travel_reimbursement_rate,
            fixed_fees=template.fixed_fees,
            procedure_items=template.procedure_items,
            is_active=template.is_active,
            created_by=template.created_by,
            reason_for_change=template.reason_for_change,
            version_index=template.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_budget_template_entity(model)

    @map_database_exceptions
    async def get_budget_template(
        self, template_id: str
    ) -> SiteBudgetTemplateEntity | None:
        stmt = select(SiteBudgetTemplate).where(SiteBudgetTemplate.id.is_(template_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_budget_template_entity(model)

    @map_database_exceptions
    async def list_budget_templates(
        self, study_id: str | None = None
    ) -> list[SiteBudgetTemplateEntity]:
        stmt = select(SiteBudgetTemplate).where(SiteBudgetTemplate.is_active.is_(True))
        if study_id:
            stmt = stmt.where(
                (SiteBudgetTemplate.study_id.is_(study_id))
                | (SiteBudgetTemplate.study_id.is_(None))
            )
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_budget_template_entity(m) for m in models]

    @map_database_exceptions
    async def get_investigator_grant(
        self, grant_id: str
    ) -> InvestigatorGrantEntity | None:
        stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return InvestigatorGrantEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            contract_title=model.contract_title,
            template_id=model.template_id,
            tier_level=model.tier_level,
            institution_name=model.institution_name,
            total_budget=model.total_budget,
            currency=model.currency,
            sponsor_currency=model.sponsor_currency,
            exchange_rate=model.exchange_rate,
            overhead_percentage=model.overhead_percentage,
            withholding_percentage=model.withholding_percentage,
            fixed_startup_fee=model.fixed_startup_fee,
            fixed_closeout_fee=model.fixed_closeout_fee,
            irb_fee=model.irb_fee,
            patient_stipend_per_visit=model.patient_stipend_per_visit,
            travel_reimbursement_per_visit=model.travel_reimbursement_per_visit,
            status=model.status,
            executed_at=model.executed_at.isoformat() if model.executed_at else None,
            executed_by=model.executed_by,
            is_active=getattr(model, "is_active", True),
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    @map_database_exceptions
    async def update_grant_budget_and_version(
        self, grant_id: str, revised_budget: float, new_version: int
    ) -> None:
        stmt = select(InvestigatorGrant).where(InvestigatorGrant.id.is_(grant_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if model:
            model.total_budget = revised_budget
            model.version_index = new_version
            await self.session.flush()

    @map_database_exceptions
    async def save_grant_amendment(
        self, amendment: GrantContractAmendmentEntity
    ) -> GrantContractAmendmentEntity:
        model = GrantContractAmendment(
            grant_id=amendment.grant_id,
            amendment_number=amendment.amendment_number,
            effective_date=datetime.fromisoformat(amendment.effective_date)
            if amendment.effective_date
            else datetime.now(UTC),
            previous_budget=amendment.previous_budget,
            revised_budget=amendment.revised_budget,
            previous_version_index=amendment.previous_version_index,
            new_version_index=amendment.new_version_index,
            summary_of_changes=amendment.summary_of_changes,
            status=amendment.status,
            approved_by=amendment.approved_by,
            approved_at=datetime.fromisoformat(amendment.approved_at)
            if amendment.approved_at
            else None,
            signature_manifestation=amendment.signature_manifestation,
            created_by=amendment.created_by,
            reason_for_change=amendment.reason_for_change,
            version_index=amendment.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_amendment_entity(model)

    @map_database_exceptions
    async def get_grant_amendment(
        self, amendment_id: str
    ) -> GrantContractAmendmentEntity | None:
        stmt = select(GrantContractAmendment).where(
            GrantContractAmendment.id.is_(amendment_id)
        )
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_amendment_entity(model)

    @map_database_exceptions
    async def list_grant_amendments(
        self, grant_id: str
    ) -> list[GrantContractAmendmentEntity]:
        stmt = (
            select(GrantContractAmendment)
            .where(GrantContractAmendment.grant_id.is_(grant_id))
            .order_by(GrantContractAmendment.amendment_number.asc())
        )
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_amendment_entity(m) for m in models]

    @map_database_exceptions
    async def save_exchange_rate(
        self, rate: CurrencyExchangeRateEntity
    ) -> CurrencyExchangeRateEntity:
        if rate.id:
            stmt = select(CurrencyExchangeRate).where(
                CurrencyExchangeRate.id.is_(rate.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.rate = rate.rate
                model.expiry_date = (
                    datetime.fromisoformat(rate.expiry_date)
                    if rate.expiry_date
                    else None
                )
                model.is_active = rate.is_active
                model.reason_for_change = rate.reason_for_change
                model.version_index = rate.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_exchange_rate_entity(model)

        model = CurrencyExchangeRate(
            base_currency=rate.base_currency.upper(),
            target_currency=rate.target_currency.upper(),
            rate=rate.rate,
            effective_date=datetime.fromisoformat(rate.effective_date)
            if rate.effective_date
            else datetime.now(UTC),
            expiry_date=datetime.fromisoformat(rate.expiry_date)
            if rate.expiry_date
            else None,
            is_active=rate.is_active,
            created_by=rate.created_by,
            reason_for_change=rate.reason_for_change,
            version_index=rate.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_exchange_rate_entity(model)

    @map_database_exceptions
    async def get_exchange_rate(
        self, base_currency: str, target_currency: str
    ) -> CurrencyExchangeRateEntity | None:
        if base_currency.upper() == target_currency.upper():
            return CurrencyExchangeRateEntity(
                base_currency=base_currency.upper(),
                target_currency=target_currency.upper(),
                rate=1.0,
                effective_date=datetime.now(UTC).isoformat(),
                is_active=True,
                created_by="system",
                reason_for_change="Parity rate",
                version_index=1,
            )

        stmt = (
            select(CurrencyExchangeRate)
            .where(
                CurrencyExchangeRate.base_currency.is_(base_currency.upper()),
                CurrencyExchangeRate.target_currency.is_(target_currency.upper()),
                CurrencyExchangeRate.is_active.is_(True),
            )
            .order_by(CurrencyExchangeRate.effective_date.desc())
        )
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            # Check inverse
            stmt_inv = (
                select(CurrencyExchangeRate)
                .where(
                    CurrencyExchangeRate.base_currency.is_(target_currency.upper()),
                    CurrencyExchangeRate.target_currency.is_(base_currency.upper()),
                    CurrencyExchangeRate.is_active.is_(True),
                )
                .order_by(CurrencyExchangeRate.effective_date.desc())
            )
            res_inv = await self.session.execute(stmt_inv)
            model_inv = res_inv.scalars().first()
            if model_inv and model_inv.rate > 0:
                return CurrencyExchangeRateEntity(
                    base_currency=base_currency.upper(),
                    target_currency=target_currency.upper(),
                    rate=round(1.0 / model_inv.rate, 6),
                    effective_date=model_inv.effective_date.isoformat(),
                    is_active=True,
                    created_by=model_inv.created_by,
                    reason_for_change=f"Inverted from {target_currency}/{base_currency}",
                    version_index=model_inv.version_index,
                )
            return None
        return self._to_exchange_rate_entity(model)

    @map_database_exceptions
    async def list_exchange_rates(self) -> list[CurrencyExchangeRateEntity]:
        stmt = (
            select(CurrencyExchangeRate)
            .where(CurrencyExchangeRate.is_active.is_(True))
            .order_by(CurrencyExchangeRate.base_currency.asc())
        )
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_exchange_rate_entity(m) for m in models]

    @map_database_exceptions
    async def save_budget_adjustment(
        self, adjustment: BudgetAdjustmentEntity
    ) -> BudgetAdjustmentEntity:
        if adjustment.id:
            stmt = select(BudgetAdjustment).where(
                BudgetAdjustment.id.is_(adjustment.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.status = adjustment.status
                model.approved_by = adjustment.approved_by
                model.approved_at = (
                    datetime.fromisoformat(adjustment.approved_at)
                    if adjustment.approved_at
                    else None
                )
                model.reason_for_change = adjustment.reason_for_change
                model.version_index = adjustment.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_adjustment_entity(model)

        model = BudgetAdjustment(
            grant_id=adjustment.grant_id,
            site_id=adjustment.site_id,
            study_id=adjustment.study_id,
            adjustment_type=adjustment.adjustment_type,
            description=adjustment.description,
            amount=adjustment.amount,
            currency=adjustment.currency.upper(),
            status=adjustment.status,
            approved_by=adjustment.approved_by,
            approved_at=datetime.fromisoformat(adjustment.approved_at)
            if adjustment.approved_at
            else None,
            created_by=adjustment.created_by,
            reason_for_change=adjustment.reason_for_change,
            version_index=adjustment.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_adjustment_entity(model)

    @map_database_exceptions
    async def get_budget_adjustment(
        self, adjustment_id: str
    ) -> BudgetAdjustmentEntity | None:
        stmt = select(BudgetAdjustment).where(BudgetAdjustment.id.is_(adjustment_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_adjustment_entity(model)

    @map_database_exceptions
    async def list_budget_adjustments(
        self,
        grant_id: str | None = None,
        study_id: str | None = None,
        site_id: str | None = None,
    ) -> list[BudgetAdjustmentEntity]:
        stmt = select(BudgetAdjustment)
        if grant_id:
            stmt = stmt.where(BudgetAdjustment.grant_id.is_(grant_id))
        if study_id:
            stmt = stmt.where(BudgetAdjustment.study_id.is_(study_id))
        if site_id:
            stmt = stmt.where(BudgetAdjustment.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_adjustment_entity(m) for m in models]

    @map_database_exceptions
    async def save_procedure_grid(
        self, grid: ProcedurePaymentGridEntity
    ) -> ProcedurePaymentGridEntity:
        if grid.id:
            stmt = select(ProcedurePaymentGrid).where(
                ProcedurePaymentGrid.id.is_(grid.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.visit_name = grid.visit_name
                model.activity_id = grid.activity_id
                model.form_id = grid.form_id
                model.procedure_code = grid.procedure_code
                model.procedure_name = grid.procedure_name
                model.base_amount = grid.base_amount
                model.overhead_percentage = grid.overhead_percentage
                model.withholding_percentage = grid.withholding_percentage
                model.patient_stipend = grid.patient_stipend
                model.travel_reimbursement = grid.travel_reimbursement
                model.is_active = grid.is_active
                model.reason_for_change = grid.reason_for_change
                model.version_index = grid.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_grid_entity(model)

        model = ProcedurePaymentGrid(
            grant_id=grid.grant_id,
            visit_name=grid.visit_name,
            activity_id=grid.activity_id,
            form_id=grid.form_id,
            procedure_code=grid.procedure_code,
            procedure_name=grid.procedure_name,
            base_amount=grid.base_amount,
            overhead_percentage=grid.overhead_percentage,
            withholding_percentage=grid.withholding_percentage,
            patient_stipend=grid.patient_stipend,
            travel_reimbursement=grid.travel_reimbursement,
            is_active=grid.is_active,
            created_by=grid.created_by,
            reason_for_change=grid.reason_for_change,
            version_index=grid.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_grid_entity(model)

    @map_database_exceptions
    async def list_procedure_grids(
        self,
        grant_id: str,
        is_active_only: bool = False,
        version_index: int | None = None,
    ) -> list[ProcedurePaymentGridEntity]:
        stmt = select(ProcedurePaymentGrid).where(
            ProcedurePaymentGrid.grant_id.is_(grant_id)
        )
        if is_active_only:
            stmt = stmt.where(ProcedurePaymentGrid.is_active.is_(True))
        if version_index is not None:
            stmt = stmt.where(ProcedurePaymentGrid.version_index.is_(version_index))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_grid_entity(m) for m in models]

    @map_database_exceptions
    async def save_invoice(
        self, invoice: FinancialInvoiceEntity
    ) -> FinancialInvoiceEntity:
        if invoice.id:
            stmt = select(FinancialInvoice).where(FinancialInvoice.id.is_(invoice.id))
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.status = invoice.status
                model.approved_by = invoice.approved_by
                model.approved_at = (
                    datetime.fromisoformat(invoice.approved_at)
                    if invoice.approved_at
                    else None
                )
                model.disbursed_at = (
                    datetime.fromisoformat(invoice.disbursed_at)
                    if invoice.disbursed_at
                    else None
                )
                model.reason_for_change = invoice.reason_for_change
                model.version_index = invoice.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_invoice_entity(model)

        model = FinancialInvoice(
            study_id=invoice.study_id,
            site_id=invoice.site_id,
            grant_id=invoice.grant_id,
            invoice_number=invoice.invoice_number
            or f"INV-{uuid.uuid4().hex[:8].upper()}",
            invoice_type=invoice.invoice_type,
            gross_amount=invoice.gross_amount,
            withholding_amount=invoice.withholding_amount,
            net_amount=invoice.net_amount,
            currency=invoice.currency,
            status=invoice.status,
            payable_ids=invoice.payable_ids,
            approved_by=invoice.approved_by,
            approved_at=(
                datetime.fromisoformat(invoice.approved_at)
                if invoice.approved_at
                else None
            ),
            disbursed_at=(
                datetime.fromisoformat(invoice.disbursed_at)
                if invoice.disbursed_at
                else None
            ),
            created_by=invoice.created_by,
            reason_for_change=invoice.reason_for_change,
            version_index=invoice.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_invoice_entity(model)

    @map_database_exceptions
    async def get_invoice(self, invoice_id: str) -> FinancialInvoiceEntity | None:
        stmt = select(FinancialInvoice).where(FinancialInvoice.id.is_(invoice_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_invoice_entity(model)

    @map_database_exceptions
    async def list_invoices(
        self, study_id: str, site_id: str | None = None
    ) -> list[FinancialInvoiceEntity]:
        stmt = select(FinancialInvoice).where(FinancialInvoice.study_id.is_(study_id))
        if site_id:
            stmt = stmt.where(FinancialInvoice.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_invoice_entity(m) for m in models]

    def _to_budget_template_entity(
        self, model: SiteBudgetTemplate
    ) -> SiteBudgetTemplateEntity:
        return SiteBudgetTemplateEntity(
            id=model.id,
            template_name=model.template_name,
            study_id=model.study_id,
            tier_level=model.tier_level,
            currency=model.currency,
            overhead_percentage=model.overhead_percentage,
            withholding_percentage=model.withholding_percentage,
            fixed_startup_fee=model.fixed_startup_fee,
            fixed_closeout_fee=model.fixed_closeout_fee,
            irb_fee=model.irb_fee,
            patient_stipend_amount=model.patient_stipend_amount,
            travel_reimbursement_rate=model.travel_reimbursement_rate,
            fixed_fees=model.fixed_fees or [],
            procedure_items=model.procedure_items or [],
            is_active=model.is_active,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_amendment_entity(
        self, model: GrantContractAmendment
    ) -> GrantContractAmendmentEntity:
        return GrantContractAmendmentEntity(
            id=model.id,
            grant_id=model.grant_id,
            amendment_number=model.amendment_number,
            effective_date=model.effective_date.isoformat(),
            previous_budget=model.previous_budget,
            revised_budget=model.revised_budget,
            previous_version_index=model.previous_version_index,
            new_version_index=model.new_version_index,
            summary_of_changes=model.summary_of_changes,
            status=model.status,
            approved_by=model.approved_by,
            approved_at=model.approved_at.isoformat() if model.approved_at else None,
            signature_manifestation=model.signature_manifestation,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_exchange_rate_entity(
        self, model: CurrencyExchangeRate
    ) -> CurrencyExchangeRateEntity:
        return CurrencyExchangeRateEntity(
            id=model.id,
            base_currency=model.base_currency,
            target_currency=model.target_currency,
            rate=model.rate,
            effective_date=model.effective_date.isoformat(),
            expiry_date=model.expiry_date.isoformat() if model.expiry_date else None,
            is_active=model.is_active,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_adjustment_entity(self, model: BudgetAdjustment) -> BudgetAdjustmentEntity:
        return BudgetAdjustmentEntity(
            id=model.id,
            grant_id=model.grant_id,
            site_id=model.site_id,
            study_id=model.study_id,
            adjustment_type=model.adjustment_type,
            description=model.description,
            amount=model.amount,
            currency=model.currency,
            status=model.status,
            approved_by=model.approved_by,
            approved_at=model.approved_at.isoformat() if model.approved_at else None,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_grid_entity(
        self, model: ProcedurePaymentGrid
    ) -> ProcedurePaymentGridEntity:
        return ProcedurePaymentGridEntity(
            id=model.id,
            grant_id=model.grant_id,
            visit_name=model.visit_name,
            activity_id=model.activity_id,
            form_id=model.form_id,
            procedure_code=model.procedure_code,
            procedure_name=model.procedure_name,
            base_amount=model.base_amount,
            overhead_percentage=model.overhead_percentage,
            withholding_percentage=model.withholding_percentage,
            patient_stipend=model.patient_stipend,
            travel_reimbursement=model.travel_reimbursement,
            is_active=model.is_active,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_invoice_entity(self, model: FinancialInvoice) -> FinancialInvoiceEntity:
        return FinancialInvoiceEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            grant_id=model.grant_id,
            invoice_number=model.invoice_number,
            invoice_type=model.invoice_type,
            gross_amount=model.gross_amount,
            withholding_amount=model.withholding_amount,
            net_amount=model.net_amount,
            currency=model.currency,
            status=model.status,
            payable_ids=model.payable_ids or [],
            approved_by=model.approved_by,
            approved_at=model.approved_at.isoformat() if model.approved_at else None,
            disbursed_at=model.disbursed_at.isoformat() if model.disbursed_at else None,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )


class SQLAlchemyIPAccountabilityRepository(IIPAccountabilityRepository):
    """SQLAlchemy implementation of IIPAccountabilityRepository."""

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> IPKitRecordEntity | None:
        return await self.get_ip_kit(entity_id)

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        if isinstance(entity, IPKitRecordEntity):
            return await self.save_ip_kit(entity)
        if isinstance(entity, IPTemperatureExcursionEntity):
            return await self.save_temperature_excursion(entity)
        if isinstance(entity, IPDestructionCertificateEntity):
            return await self.save_destruction_certificate(entity)
        return entity

    @map_database_exceptions
    async def save_ip_kit(self, kit: IPKitRecordEntity) -> IPKitRecordEntity:
        if kit.id:
            stmt = select(IPKitRecord).where(IPKitRecord.id.is_(kit.id))
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.status = kit.status
                model.dispensed_subject_id = kit.dispensed_subject_id
                model.dispensed_visit_id = kit.dispensed_visit_id
                model.dispensed_date = (
                    datetime.fromisoformat(kit.dispensed_date)
                    if kit.dispensed_date
                    else None
                )
                model.returned_units_count = kit.returned_units_count
                model.expected_units_count = kit.expected_units_count
                model.compliance_percentage = kit.compliance_percentage
                model.notes = kit.notes
                model.reason_for_change = kit.reason_for_change
                model.version_index = kit.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_kit_entity(model)

        model = IPKitRecord(
            study_id=kit.study_id,
            site_id=kit.site_id,
            kit_number=kit.kit_number,
            lot_number=kit.lot_number,
            kit_type=kit.kit_type,
            shipment_tracking_number=kit.shipment_tracking_number,
            expiration_date=datetime.fromisoformat(kit.expiration_date),
            status=kit.status,
            received_date=(
                datetime.fromisoformat(kit.received_date) if kit.received_date else None
            ),
            dispensed_subject_id=kit.dispensed_subject_id,
            dispensed_visit_id=kit.dispensed_visit_id,
            dispensed_date=(
                datetime.fromisoformat(kit.dispensed_date)
                if kit.dispensed_date
                else None
            ),
            returned_units_count=kit.returned_units_count,
            expected_units_count=kit.expected_units_count,
            compliance_percentage=kit.compliance_percentage,
            notes=kit.notes,
            created_by=kit.created_by,
            reason_for_change=kit.reason_for_change,
            version_index=kit.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_kit_entity(model)

    @map_database_exceptions
    async def get_ip_kit(self, kit_id: str) -> IPKitRecordEntity | None:
        stmt = select(IPKitRecord).where(IPKitRecord.id.is_(kit_id))
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_kit_entity(model)

    @map_database_exceptions
    async def get_ip_kit_by_number(
        self, site_id: str, kit_number: str
    ) -> IPKitRecordEntity | None:
        stmt = select(IPKitRecord).where(
            IPKitRecord.site_id.is_(site_id),
            IPKitRecord.kit_number.is_(kit_number),
        )
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_kit_entity(model)

    @map_database_exceptions
    async def list_ip_kits(
        self, study_id: str, site_id: str | None = None, status: str | None = None
    ) -> list[IPKitRecordEntity]:
        stmt = select(IPKitRecord).where(IPKitRecord.study_id.is_(study_id))
        if site_id:
            stmt = stmt.where(IPKitRecord.site_id.is_(site_id))
        if status:
            stmt = stmt.where(IPKitRecord.status.is_(status))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_kit_entity(m) for m in models]

    @map_database_exceptions
    async def save_temperature_excursion(
        self, excursion: IPTemperatureExcursionEntity
    ) -> IPTemperatureExcursionEntity:
        if excursion.id:
            stmt = select(IPTemperatureExcursion).where(
                IPTemperatureExcursion.id.is_(excursion.id)
            )
            res = await self.session.execute(stmt)
            model = res.scalars().first()
            if model:
                model.disposition_status = excursion.disposition_status
                model.qa_reviewed_by = excursion.qa_reviewed_by
                model.qa_reviewed_at = (
                    datetime.fromisoformat(excursion.qa_reviewed_at)
                    if excursion.qa_reviewed_at
                    else None
                )
                model.qa_rationale = excursion.qa_rationale
                model.reason_for_change = excursion.reason_for_change
                model.version_index = excursion.version_index
                self.session.add(model)
                await self.session.flush()
                return self._to_excursion_entity(model)

        model = IPTemperatureExcursion(
            study_id=excursion.study_id,
            site_id=excursion.site_id,
            kit_ids=excursion.kit_ids,
            excursion_type=excursion.excursion_type,
            min_temp_celsius=excursion.min_temp_celsius,
            max_temp_celsius=excursion.max_temp_celsius,
            duration_hours=excursion.duration_hours,
            occurred_at=datetime.fromisoformat(excursion.occurred_at),
            disposition_status=excursion.disposition_status,
            qa_reviewed_by=excursion.qa_reviewed_by,
            qa_reviewed_at=(
                datetime.fromisoformat(excursion.qa_reviewed_at)
                if excursion.qa_reviewed_at
                else None
            ),
            qa_rationale=excursion.qa_rationale,
            created_by=excursion.created_by,
            reason_for_change=excursion.reason_for_change,
            version_index=excursion.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_excursion_entity(model)

    @map_database_exceptions
    async def get_temperature_excursion(
        self, excursion_id: str
    ) -> IPTemperatureExcursionEntity | None:
        stmt = select(IPTemperatureExcursion).where(
            IPTemperatureExcursion.id.is_(excursion_id)
        )
        res = await self.session.execute(stmt)
        model = res.scalars().first()
        if not model:
            return None
        return self._to_excursion_entity(model)

    @map_database_exceptions
    async def list_temperature_excursions(
        self, study_id: str, site_id: str | None = None
    ) -> list[IPTemperatureExcursionEntity]:
        stmt = select(IPTemperatureExcursion).where(
            IPTemperatureExcursion.study_id.is_(study_id)
        )
        if site_id:
            stmt = stmt.where(IPTemperatureExcursion.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_excursion_entity(m) for m in models]

    @map_database_exceptions
    async def save_destruction_certificate(
        self, cert: IPDestructionCertificateEntity
    ) -> IPDestructionCertificateEntity:
        model = IPDestructionCertificate(
            study_id=cert.study_id,
            site_id=cert.site_id,
            certificate_number=cert.certificate_number
            or f"DOC-DEST-{uuid.uuid4().hex[:8].upper()}",
            kit_ids=cert.kit_ids,
            destruction_method=cert.destruction_method,
            destruction_date=datetime.fromisoformat(cert.destruction_date),
            witness_user_id=cert.witness_user_id,
            witness_role=cert.witness_role,
            pi_signature_hash=cert.pi_signature_hash,
            pi_signed_at=datetime.fromisoformat(cert.pi_signed_at),
            reason_for_destruction=cert.reason_for_destruction,
            created_by=cert.created_by,
            reason_for_change=cert.reason_for_change,
            version_index=cert.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_cert_entity(model)

    @map_database_exceptions
    async def list_destruction_certificates(
        self, study_id: str, site_id: str | None = None
    ) -> list[IPDestructionCertificateEntity]:
        stmt = select(IPDestructionCertificate).where(
            IPDestructionCertificate.study_id.is_(study_id)
        )
        if site_id:
            stmt = stmt.where(IPDestructionCertificate.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_cert_entity(m) for m in models]

    def _to_kit_entity(self, model: IPKitRecord) -> IPKitRecordEntity:
        return IPKitRecordEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            kit_number=model.kit_number,
            lot_number=model.lot_number,
            kit_type=model.kit_type,
            shipment_tracking_number=model.shipment_tracking_number,
            expiration_date=model.expiration_date.isoformat(),
            status=model.status,
            received_date=model.received_date.isoformat()
            if model.received_date
            else None,
            dispensed_subject_id=model.dispensed_subject_id,
            dispensed_visit_id=model.dispensed_visit_id,
            dispensed_date=model.dispensed_date.isoformat()
            if model.dispensed_date
            else None,
            returned_units_count=model.returned_units_count,
            expected_units_count=model.expected_units_count,
            compliance_percentage=model.compliance_percentage,
            notes=model.notes,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_excursion_entity(
        self, model: IPTemperatureExcursion
    ) -> IPTemperatureExcursionEntity:
        return IPTemperatureExcursionEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            kit_ids=model.kit_ids or [],
            excursion_type=model.excursion_type,
            min_temp_celsius=model.min_temp_celsius,
            max_temp_celsius=model.max_temp_celsius,
            duration_hours=model.duration_hours,
            occurred_at=model.occurred_at.isoformat(),
            disposition_status=model.disposition_status,
            qa_reviewed_by=model.qa_reviewed_by,
            qa_reviewed_at=model.qa_reviewed_at.isoformat()
            if model.qa_reviewed_at
            else None,
            qa_rationale=model.qa_rationale,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )

    def _to_cert_entity(
        self, model: IPDestructionCertificate
    ) -> IPDestructionCertificateEntity:
        return IPDestructionCertificateEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            certificate_number=model.certificate_number,
            kit_ids=model.kit_ids or [],
            destruction_method=model.destruction_method,
            destruction_date=model.destruction_date.isoformat(),
            witness_user_id=model.witness_user_id,
            witness_role=model.witness_role,
            pi_signature_hash=model.pi_signature_hash,
            pi_signed_at=model.pi_signed_at.isoformat(),
            reason_for_destruction=model.reason_for_destruction,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )


class SQLAlchemyETMFSyncRepository(IETMFSyncRepository):
    """SQLAlchemy implementation of IETMFSyncRepository."""

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Any | None:
        return None

    @map_database_exceptions
    async def save(self, entity: Any) -> Any:
        if isinstance(entity, ETMFSyncRecordEntity):
            return await self.save_sync_record(entity)
        return entity

    @map_database_exceptions
    async def save_sync_record(
        self, record: ETMFSyncRecordEntity
    ) -> ETMFSyncRecordEntity:
        model = ETMFSyncRecord(
            study_id=record.study_id,
            site_id=record.site_id,
            artifact_type=record.artifact_type,
            source_record_id=record.source_record_id,
            etmf_document_id=record.etmf_document_id,
            dia_zone=record.dia_zone,
            dia_section=record.dia_section,
            dia_artifact=record.dia_artifact,
            sync_status=record.sync_status,
            error_message=record.error_message,
            synced_at=(
                datetime.fromisoformat(record.synced_at)
                if record.synced_at
                else datetime.now(UTC)
            ),
            created_by=record.created_by,
            reason_for_change=record.reason_for_change,
            version_index=record.version_index,
        )
        self.session.add(model)
        await self.session.flush()
        return self._to_sync_entity(model)

    @map_database_exceptions
    async def list_sync_records(
        self, study_id: str, site_id: str | None = None
    ) -> list[ETMFSyncRecordEntity]:
        stmt = select(ETMFSyncRecord).where(ETMFSyncRecord.study_id.is_(study_id))
        if site_id:
            stmt = stmt.where(ETMFSyncRecord.site_id.is_(site_id))
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [self._to_sync_entity(m) for m in models]

    def _to_sync_entity(self, model: ETMFSyncRecord) -> ETMFSyncRecordEntity:
        return ETMFSyncRecordEntity(
            id=model.id,
            study_id=model.study_id,
            site_id=model.site_id,
            artifact_type=model.artifact_type,
            source_record_id=model.source_record_id,
            etmf_document_id=model.etmf_document_id,
            dia_zone=model.dia_zone,
            dia_section=model.dia_section,
            dia_artifact=model.dia_artifact,
            sync_status=model.sync_status,
            error_message=model.error_message,
            synced_at=model.synced_at.isoformat() if model.synced_at else None,
            created_at=model.created_at.isoformat() if model.created_at else None,
            created_by=model.created_by,
            reason_for_change=model.reason_for_change,
            version_index=model.version_index,
        )


async def get_ctms_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyCTMSDelegationRepository:
    return SQLAlchemyCTMSDelegationRepository(session)


async def get_site_startup_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemySiteStartupRepository:
    return SQLAlchemySiteStartupRepository(session)


async def get_protocol_deviation_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyProtocolDeviationRepository:
    return SQLAlchemyProtocolDeviationRepository(session)


async def get_rbqm_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyRBQMRepository:
    return SQLAlchemyRBQMRepository(session)


async def get_financials_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyFinancialsRepository:
    return SQLAlchemyFinancialsRepository(session)


async def get_ip_accountability_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyIPAccountabilityRepository:
    return SQLAlchemyIPAccountabilityRepository(session)


async def get_etmf_sync_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyETMFSyncRepository:
    return SQLAlchemyETMFSyncRepository(session)
