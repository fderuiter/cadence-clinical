from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.quality.adapters.database import get_session
from apps.quality.adapters.models import (
    AuditFinding,
    CAPAActionItem,
    CAPAEffectivenessCheck,
    CAPARecord,
    CtQFactor,
    Deviation,
    IntegrationOutbox,
    KRIDefinition,
    KRIMetricEvaluation,
    QTLBreachEvent,
    QualityAudit,
    QualityAuditLog,
    QualityToleranceLimit,
    RootCauseAnalysis,
    SeriousBreachRecord,
    SiteRiskProfile,
)
from apps.quality.domain.ports import QualityRepositoryPort
from packages.database import map_database_exceptions


class SQLQualityRepository(QualityRepositoryPort):
    """SQLAlchemy implementation of QualityRepositoryPort."""

    def __init__(self, session: AsyncSession | None = None) -> None:
        self._session = session

    @property
    def session(self) -> AsyncSession:
        if self._session is not None:
            return self._session
        return get_session()

    # --- Deviations & RCA Entity Factories & Queries ---

    def create_deviation_entity(self, **kwargs) -> Deviation:
        return Deviation(**kwargs)

    def create_rca_entity(self, **kwargs) -> RootCauseAnalysis:
        return RootCauseAnalysis(**kwargs)

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Deviation | None:
        return await self.get_deviation_by_id(entity_id)

    @map_database_exceptions
    async def save(self, entity: Deviation) -> Deviation:
        return await self.save_deviation(entity)

    @map_database_exceptions
    async def get_deviations(self) -> Sequence[Deviation]:
        stmt = (
            select(Deviation)
            .options(
                selectinload(Deviation.root_cause_analysis),
                selectinload(Deviation.capa_records),
            )
            .order_by(Deviation.created_at.desc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_deviation_by_id(self, dev_id: str) -> Deviation | None:
        stmt = (
            select(Deviation)
            .where(Deviation.id == dev_id)
            .options(
                selectinload(Deviation.root_cause_analysis),
                selectinload(Deviation.capa_records),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_deviation(self, dev: Deviation) -> Deviation:
        self.session.add(dev)
        await self.session.flush()
        return dev

    @map_database_exceptions
    async def get_rca_by_deviation_id(self, dev_id: str) -> RootCauseAnalysis | None:
        stmt = select(RootCauseAnalysis).where(RootCauseAnalysis.deviation_id == dev_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_rca_by_id(self, rca_id: str) -> RootCauseAnalysis | None:
        stmt = select(RootCauseAnalysis).where(RootCauseAnalysis.id == rca_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_rca(self, rca: RootCauseAnalysis) -> RootCauseAnalysis:
        self.session.add(rca)
        await self.session.flush()
        return rca

    # --- CAPAs, Action Items & Effectiveness Checks ---

    def create_capa_entity(self, **kwargs) -> CAPARecord:
        return CAPARecord(**kwargs)

    def create_action_item_entity(self, **kwargs) -> CAPAActionItem:
        return CAPAActionItem(**kwargs)

    def create_effectiveness_check_entity(self, **kwargs) -> CAPAEffectivenessCheck:
        return CAPAEffectivenessCheck(**kwargs)

    @map_database_exceptions
    async def get_capa_by_id(self, capa_id: str) -> CAPARecord | None:
        stmt = (
            select(CAPARecord)
            .where(CAPARecord.id == capa_id)
            .options(
                selectinload(CAPARecord.deviation),
                selectinload(CAPARecord.rca),
                selectinload(CAPARecord.action_items),
                selectinload(CAPARecord.effectiveness_checks),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_capas(self) -> Sequence[CAPARecord]:
        stmt = (
            select(CAPARecord)
            .options(
                selectinload(CAPARecord.deviation),
                selectinload(CAPARecord.action_items),
                selectinload(CAPARecord.effectiveness_checks),
            )
            .order_by(CAPARecord.created_at.desc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_capa(self, capa: CAPARecord) -> CAPARecord:
        self.session.add(capa)
        await self.session.flush()
        return capa

    @map_database_exceptions
    async def get_action_items_by_capa(self, capa_id: str) -> Sequence[CAPAActionItem]:
        stmt = select(CAPAActionItem).where(CAPAActionItem.capa_id == capa_id)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_action_item(self, action_item: CAPAActionItem) -> CAPAActionItem:
        self.session.add(action_item)
        await self.session.flush()
        return action_item

    @map_database_exceptions
    async def get_effectiveness_checks_by_capa(
        self, capa_id: str
    ) -> Sequence[CAPAEffectivenessCheck]:
        stmt = select(CAPAEffectivenessCheck).where(
            CAPAEffectivenessCheck.capa_id == capa_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_effectiveness_check(
        self, check: CAPAEffectivenessCheck
    ) -> CAPAEffectivenessCheck:
        self.session.add(check)
        await self.session.flush()
        return check

    # --- RBQM, KRIs & QTLs ---

    def create_ctq_entity(self, **kwargs) -> CtQFactor:
        return CtQFactor(**kwargs)

    def create_kri_definition_entity(self, **kwargs) -> KRIDefinition:
        return KRIDefinition(**kwargs)

    def create_kri_evaluation_entity(self, **kwargs) -> KRIMetricEvaluation:
        return KRIMetricEvaluation(**kwargs)

    def create_site_risk_profile_entity(self, **kwargs) -> SiteRiskProfile:
        return SiteRiskProfile(**kwargs)

    def create_qtl_entity(self, **kwargs) -> QualityToleranceLimit:
        return QualityToleranceLimit(**kwargs)

    def create_qtl_breach_entity(self, **kwargs) -> QTLBreachEvent:
        return QTLBreachEvent(**kwargs)

    @map_database_exceptions
    async def get_ctq_factors(self, study_id: str | None = None) -> Sequence[CtQFactor]:
        stmt = select(CtQFactor)
        if study_id:
            stmt = stmt.where(CtQFactor.study_id == study_id)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_ctq(self, ctq: CtQFactor) -> CtQFactor:
        self.session.add(ctq)
        await self.session.flush()
        return ctq

    @map_database_exceptions
    async def get_kri_definitions(self) -> Sequence[KRIDefinition]:
        stmt = (
            select(KRIDefinition)
            .where(KRIDefinition.is_active.is_(True))
            .order_by(KRIDefinition.code.asc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_kri_definition_by_code(self, code: str) -> KRIDefinition | None:
        stmt = select(KRIDefinition).where(KRIDefinition.code == code)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_kri_definition(self, kri: KRIDefinition) -> KRIDefinition:
        self.session.add(kri)
        await self.session.flush()
        return kri

    @map_database_exceptions
    async def get_kri_evaluations(
        self, study_id: str, site_id: str | None = None
    ) -> Sequence[KRIMetricEvaluation]:
        stmt = select(KRIMetricEvaluation)
        if study_id:
            stmt = stmt.where(KRIMetricEvaluation.study_id == study_id)
        if site_id:
            stmt = stmt.where(KRIMetricEvaluation.site_id == site_id)
        stmt = stmt.order_by(KRIMetricEvaluation.evaluation_date.desc())
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_kri_evaluation(
        self, eval_entity: KRIMetricEvaluation
    ) -> KRIMetricEvaluation:
        self.session.add(eval_entity)
        await self.session.flush()
        return eval_entity

    @map_database_exceptions
    async def get_site_risk_profiles(
        self, study_id: str | None = None
    ) -> Sequence[SiteRiskProfile]:
        stmt = select(SiteRiskProfile)
        if study_id:
            stmt = stmt.where(SiteRiskProfile.study_id == study_id)
        stmt = stmt.order_by(SiteRiskProfile.composite_risk_score.desc())
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_site_risk_profile(self, profile: SiteRiskProfile) -> SiteRiskProfile:
        self.session.add(profile)
        await self.session.flush()
        return profile

    @map_database_exceptions
    async def get_qtls(
        self, study_id: str | None = None
    ) -> Sequence[QualityToleranceLimit]:
        stmt = select(QualityToleranceLimit)
        if study_id:
            stmt = stmt.where(QualityToleranceLimit.study_id == study_id)
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_qtl_by_id(self, qtl_id: str) -> QualityToleranceLimit | None:
        stmt = select(QualityToleranceLimit).where(QualityToleranceLimit.id == qtl_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_qtl(self, qtl: QualityToleranceLimit) -> QualityToleranceLimit:
        self.session.add(qtl)
        await self.session.flush()
        return qtl

    @map_database_exceptions
    async def get_qtl_breaches(
        self, study_id: str | None = None
    ) -> Sequence[QTLBreachEvent]:
        stmt = select(QTLBreachEvent)
        if study_id:
            stmt = stmt.where(QTLBreachEvent.study_id == study_id)
        stmt = stmt.order_by(QTLBreachEvent.breach_date.desc())
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_qtl_breach(self, breach: QTLBreachEvent) -> QTLBreachEvent:
        self.session.add(breach)
        await self.session.flush()
        return breach

    # --- Clinical Audits & Findings ---

    def create_audit_entity(self, **kwargs) -> QualityAudit:
        return QualityAudit(**kwargs)

    def create_audit_finding_entity(self, **kwargs) -> AuditFinding:
        return AuditFinding(**kwargs)

    @map_database_exceptions
    async def get_audits(self) -> Sequence[QualityAudit]:
        stmt = (
            select(QualityAudit)
            .options(selectinload(QualityAudit.findings))
            .order_by(QualityAudit.planned_start_date.desc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_audit_by_id(self, audit_id: str) -> QualityAudit | None:
        stmt = (
            select(QualityAudit)
            .where(QualityAudit.id == audit_id)
            .options(selectinload(QualityAudit.findings))
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_audit(self, audit: QualityAudit) -> QualityAudit:
        self.session.add(audit)
        await self.session.flush()
        return audit

    @map_database_exceptions
    async def get_findings_by_audit(self, audit_id: str) -> Sequence[AuditFinding]:
        stmt = (
            select(AuditFinding)
            .where(AuditFinding.audit_id == audit_id)
            .order_by(AuditFinding.finding_number.asc())
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_finding_by_id(self, finding_id: str) -> AuditFinding | None:
        stmt = select(AuditFinding).where(AuditFinding.id == finding_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_audit_finding(self, finding: AuditFinding) -> AuditFinding:
        self.session.add(finding)
        await self.session.flush()
        return finding

    # --- Serious Breaches ---

    def create_serious_breach_entity(self, **kwargs) -> SeriousBreachRecord:
        return SeriousBreachRecord(**kwargs)

    @map_database_exceptions
    async def get_serious_breaches(self) -> Sequence[SeriousBreachRecord]:
        stmt = select(SeriousBreachRecord).order_by(
            SeriousBreachRecord.event_date.desc()
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def get_serious_breach_by_id(
        self, breach_id: str
    ) -> SeriousBreachRecord | None:
        stmt = select(SeriousBreachRecord).where(SeriousBreachRecord.id == breach_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save_serious_breach(
        self, breach: SeriousBreachRecord
    ) -> SeriousBreachRecord:
        self.session.add(breach)
        await self.session.flush()
        return breach

    # --- Audit Logs & Outbox ---

    def create_outbox_entity(self, **kwargs) -> IntegrationOutbox:
        return IntegrationOutbox(**kwargs)

    @map_database_exceptions
    async def save_outbox_event(
        self, outbox_event: IntegrationOutbox
    ) -> IntegrationOutbox:
        self.session.add(outbox_event)
        await self.session.flush()
        return outbox_event

    def create_audit_log_entity(self, **kwargs) -> QualityAuditLog:
        return QualityAuditLog(**kwargs)

    @map_database_exceptions
    async def get_audit_logs(self) -> Sequence[QualityAuditLog]:
        stmt = select(QualityAuditLog).order_by(QualityAuditLog.timestamp.desc())
        result = await self.session.execute(stmt)
        return result.scalars().all()

    @map_database_exceptions
    async def save_audit_log(self, log: QualityAuditLog) -> QualityAuditLog:
        self.session.add(log)
        await self.session.flush()
        return log
