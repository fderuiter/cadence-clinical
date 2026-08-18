from datetime import UTC, datetime

from apps.ctms.domain.exceptions import ActionItemNotFoundError, DeviationNotFoundError
from apps.ctms.domain.models import (
    CTMSAuditLogEntity,
    DeviationActionItemEntity,
    ProtocolDeviationEntity,
)
from apps.ctms.domain.ports import (
    ICTMSDelegationRepository,
    IProtocolDeviationRepository,
    IQualityClientPort,
    ISafetyClientPort,
)


class ProtocolDeviationService:
    """Application service for Protocol Deviations, 5-Why RCA, and CAPA Escalation."""

    def __init__(
        self,
        deviation_repo: IProtocolDeviationRepository,
        quality_client: IQualityClientPort | None = None,
        safety_client: ISafetyClientPort | None = None,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.deviation_repo = deviation_repo
        self.quality_client = quality_client
        self.safety_client = safety_client
        self.doa_repo = doa_repo

    async def log_deviation(
        self,
        study_id: str,
        site_id: str,
        deviation_category: str,
        severity: str,  # MINOR, MAJOR, CRITICAL
        title: str,
        description: str,
        date_occurred: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        subject_id: str | None = None,
        visit_id: str | None = None,
    ) -> ProtocolDeviationEntity:
        entity = ProtocolDeviationEntity(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            deviation_category=deviation_category.upper(),
            severity=severity.upper(),
            title=title,
            description=description,
            date_occurred=date_occurred,
            date_identified=datetime.now(UTC).date().isoformat(),
            status="IDENTIFIED",
            reported_by=user_id,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.deviation_repo.save_deviation(entity)

        # Notify safety if critical
        if severity.upper() == "CRITICAL" and self.safety_client and saved.id:
            await self.safety_client.notify_deviation_event(
                study_id=study_id,
                site_id=site_id,
                deviation_id=saved.id,
                title=title,
                severity=severity,
                user_id=user_id,
            )

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="PROTOCOL_DEVIATION_LOGGED",
                details=f"Logged {severity} deviation '{title}' for site {site_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def perform_root_cause_analysis(
        self,
        deviation_id: str,
        root_cause_5whys: list[str],
        root_cause_summary: str,
        corrective_action_plan: str,
        preventive_action_plan: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> ProtocolDeviationEntity:
        dev = await self.deviation_repo.get_deviation(deviation_id)
        if not dev:
            raise DeviationNotFoundError(f"Protocol deviation {deviation_id} not found")

        dev.root_cause_5whys = root_cause_5whys
        dev.root_cause_summary = root_cause_summary
        dev.corrective_action_plan = corrective_action_plan
        dev.preventive_action_plan = preventive_action_plan
        dev.status = "UNDER_REVIEW"
        dev.version_index += 1
        dev.reason_for_change = reason_for_change

        saved = await self.deviation_repo.save_deviation(dev)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="DEVIATION_RCA_PERFORMED",
                details=f"Performed 5-Why RCA on deviation {deviation_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def escalate_to_quality_capa(
        self,
        deviation_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> ProtocolDeviationEntity:
        dev = await self.deviation_repo.get_deviation(deviation_id)
        if not dev:
            raise DeviationNotFoundError(f"Protocol deviation {deviation_id} not found")

        roles_list = [r.strip() for r in user_roles.split(",") if r.strip()]

        if self.quality_client:
            res = await self.quality_client.create_capa_from_deviation(
                study_id=dev.study_id,
                site_id=dev.site_id,
                title=dev.title,
                description=dev.description,
                severity=dev.severity,
                root_cause_summary=dev.root_cause_summary
                or "Root cause analysis pending",
                corrective_action=dev.corrective_action_plan
                or "Corrective action plan pending",
                user_id=user_id,
                user_roles=roles_list,
                reason_for_change=reason_for_change,
            )
            dev.quality_capa_id = res.get("capa_id")

        dev.status = "CAPA_ESCALATED"
        dev.version_index += 1
        dev.reason_for_change = reason_for_change

        saved = await self.deviation_repo.save_deviation(dev)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="DEVIATION_CAPA_ESCALATED",
                details=f"Escalated deviation {deviation_id} to Quality CAPA ({dev.quality_capa_id}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def resolve_deviation(
        self,
        deviation_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> ProtocolDeviationEntity:
        dev = await self.deviation_repo.get_deviation(deviation_id)
        if not dev:
            raise DeviationNotFoundError(f"Protocol deviation {deviation_id} not found")

        dev.status = "RESOLVED"
        dev.resolved_by = user_id
        dev.resolved_at = datetime.now(UTC).isoformat()
        dev.version_index += 1
        dev.reason_for_change = reason_for_change

        saved = await self.deviation_repo.save_deviation(dev)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="DEVIATION_RESOLVED",
                details=f"Resolved deviation {deviation_id} by {user_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_deviations(
        self,
        study_id: str,
        site_id: str | None = None,
        severity: str | None = None,
    ) -> list[ProtocolDeviationEntity]:
        return await self.deviation_repo.list_deviations(study_id, site_id, severity)

    async def create_action_item(
        self,
        deviation_id: str,
        site_id: str,
        description: str,
        assignee_user_id: str,
        assignee_role: str,
        due_date: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> DeviationActionItemEntity:
        entity = DeviationActionItemEntity(
            deviation_id=deviation_id,
            site_id=site_id,
            description=description,
            assignee_user_id=assignee_user_id,
            assignee_role=assignee_role,
            due_date=due_date,
            status="OPEN",
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        return await self.deviation_repo.save_action_item(entity)

    async def complete_action_item(
        self,
        action_item_id: str,
        resolution_notes: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> DeviationActionItemEntity:
        item = await self.deviation_repo.get_action_item(action_item_id)
        if not item:
            raise ActionItemNotFoundError(f"Action item {action_item_id} not found")

        item.status = "COMPLETED"
        item.resolution_notes = resolution_notes
        item.completed_by = user_id
        item.completed_at = datetime.now(UTC).isoformat()
        item.version_index += 1
        item.reason_for_change = reason_for_change

        return await self.deviation_repo.save_action_item(item)

    async def list_action_items(
        self, deviation_id: str | None = None, site_id: str | None = None
    ) -> list[DeviationActionItemEntity]:
        return await self.deviation_repo.list_action_items(deviation_id, site_id)
