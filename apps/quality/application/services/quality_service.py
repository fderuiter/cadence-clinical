import uuid
from collections.abc import Sequence
from datetime import datetime
from typing import Any

from apps.quality.domain.models import (
    ActionItemStatus,
    CAPAStatus,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    EffectivenessOutcome,
    RCAMethodology,
)
from apps.quality.domain.ports import QualityRepositoryPort
from packages.hexagonal import DomainError

STAGE_MAPPING = {
    CAPAStatus.INITIATED: "CAPA_ESCALATED",
    CAPAStatus.UNDER_REVIEW: "UNDER_REVIEW",
    CAPAStatus.APPROVED: "UNDER_REVIEW",
    CAPAStatus.IMPLEMENTATION: "UNDER_REVIEW",
    CAPAStatus.IMPLEMENTATION_VERIFIED: "UNDER_REVIEW",
    CAPAStatus.EFFECTIVENESS_CHECK: "UNDER_REVIEW",
    CAPAStatus.CLOSED: "RESOLVED",
    CAPAStatus.INEFFECTIVE: "UNDER_REVIEW",
    CAPAStatus.CANCELLED: "RESOLVED",
}

CAPA_TRANSITIONS = {
    CAPAStatus.INITIATED: {CAPAStatus.UNDER_REVIEW, CAPAStatus.CANCELLED},
    CAPAStatus.UNDER_REVIEW: {
        CAPAStatus.APPROVED,
        CAPAStatus.IMPLEMENTATION,
        CAPAStatus.INITIATED,
        CAPAStatus.CANCELLED,
    },
    CAPAStatus.APPROVED: {
        CAPAStatus.IMPLEMENTATION,
        CAPAStatus.CANCELLED,
    },
    CAPAStatus.IMPLEMENTATION: {
        CAPAStatus.IMPLEMENTATION_VERIFIED,
        CAPAStatus.EFFECTIVENESS_CHECK,
        CAPAStatus.CANCELLED,
    },
    CAPAStatus.IMPLEMENTATION_VERIFIED: {
        CAPAStatus.EFFECTIVENESS_CHECK,
        CAPAStatus.CANCELLED,
    },
    CAPAStatus.EFFECTIVENESS_CHECK: {
        CAPAStatus.CLOSED,
        CAPAStatus.INEFFECTIVE,
        CAPAStatus.CANCELLED,
    },
    CAPAStatus.CLOSED: set(),
    CAPAStatus.INEFFECTIVE: {CAPAStatus.INITIATED, CAPAStatus.CANCELLED},
    CAPAStatus.CANCELLED: set(),
}


class QualityServiceError(DomainError):
    def __init__(self, message: str, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


class QualityService:
    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    async def create_deviation(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )

        dev_type = payload.type
        dev_category = getattr(payload, "category", dev_type)
        impact_safety = getattr(payload, "impact_safety", False)
        impact_data = getattr(payload, "impact_data", False)
        impact_compliance = getattr(payload, "impact_compliance", False)
        source_system = getattr(payload, "source_system", "MANUAL")
        source_ref = getattr(payload, "source_reference_id", None)

        dev = self.repo.create_deviation_entity(
            study_id=payload.study_id,
            site_id=payload.site_id,
            title=payload.title,
            description=payload.description,
            severity=payload.severity,
            status=DeviationStatus.REPORTED,
            type=dev_type,
            category=dev_category,
            is_protocol_violation=payload.is_protocol_violation,
            impact_safety=impact_safety,
            impact_data=impact_data,
            impact_compliance=impact_compliance,
            source_system=source_system,
            source_reference_id=source_ref,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_deviation(dev)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_CREATE",
            details=f"Created deviation '{payload.title}' for study '{payload.study_id}' with status REPORTED.",
            record_id=dev.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return dev

    async def ingest_quality_event(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        """
        Automated ingestion hook for cross-service events from EDC, CTMS, eTMF with deduplication.
        """
        if not change_reason:
            change_reason = (
                f"Automated quality event ingestion from {payload.source_system}"
            )

        # Deduplicate if source_reference_id is provided
        if payload.source_reference_id:
            existing = await self.repo.get_deviations()
            for d in existing:
                if (
                    d.source_system == payload.source_system
                    and d.source_reference_id == payload.source_reference_id
                    and d.study_id == payload.study_id
                ):
                    return d

        dev = self.repo.create_deviation_entity(
            study_id=payload.study_id,
            site_id=payload.site_id,
            title=payload.title,
            description=payload.description,
            severity=payload.severity,
            status=DeviationStatus.REPORTED,
            type=payload.type,
            category=getattr(payload, "category", payload.type),
            is_protocol_violation=payload.is_protocol_violation,
            impact_safety=getattr(payload, "impact_safety", False),
            impact_data=getattr(payload, "impact_data", False),
            impact_compliance=getattr(payload, "impact_compliance", False),
            source_system=payload.source_system,
            source_reference_id=payload.source_reference_id,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_deviation(dev)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_INGEST",
            details=f"Ingested automated deviation from {payload.source_system} (Ref: {payload.source_reference_id}).",
            record_id=dev.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return dev

    async def list_deviations(
        self,
        study_id: str | None,
        site_id: str | None,
        status: DeviationStatus | None,
        severity: DeviationSeverity | None = None,
        dev_type: DeviationType | None = None,
        user_id: str = "system",
        user_role: str = "auditor",
    ) -> Sequence[Any]:
        deviations = await self.repo.get_deviations()
        filtered = []
        for d in deviations:
            if study_id and d.study_id != study_id:
                continue
            if site_id and d.site_id != site_id:
                continue
            if status and d.status != status:
                continue
            if severity and d.severity != severity:
                continue
            if dev_type and d.type != dev_type:
                continue
            filtered.append(d)

        filters = f"study_id={study_id}, site_id={site_id}, status={status}, severity={severity}"
        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_LIST",
            details=f"Listed deviations matching criteria: {filters}.",
        )
        await self.repo.save_audit_log(log)
        return filtered

    async def view_deviation(self, dev_id: str, user_id: str, user_role: str) -> Any:
        dev = await self.repo.get_deviation_by_id(dev_id)
        if not dev:
            raise QualityServiceError("Deviation not found", status_code=404)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_VIEW",
            details=f"Viewed deviation ID: {dev_id}.",
            record_id=dev_id,
        )
        await self.repo.save_audit_log(log)
        return dev

    async def create_or_update_rca(
        self,
        dev_id: str,
        payload: Any,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )

        dev = await self.repo.get_deviation_by_id(dev_id)
        if not dev:
            raise QualityServiceError("Parent deviation not found", status_code=404)

        rca = await self.repo.get_rca_by_deviation_id(dev_id)
        action = "RCA_CREATE"
        methodology = getattr(payload, "methodology", RCAMethodology.FIVE_WHYS)
        five_whys = getattr(payload, "five_whys_chain", None)
        fishbone = getattr(payload, "fishbone_categories", None)
        factors = getattr(payload, "contributing_factors", None)

        if rca:
            action = "RCA_UPDATE"
            if (
                getattr(payload, "version_index", None) is not None
                and rca.version_index != payload.version_index
            ):
                raise QualityServiceError(
                    f"Version conflict: The RCA has been modified by another process. Current version: {rca.version_index}.",
                    status_code=409,
                )
            rca.methodology = methodology
            rca.investigation_details = payload.investigation_details
            rca.root_cause_summary = payload.root_cause_summary
            if five_whys is not None:
                rca.five_whys_chain = five_whys
            if fishbone is not None:
                rca.fishbone_categories = fishbone
            if factors is not None:
                rca.contributing_factors = factors
            rca.version_index += 1
            rca.reason_for_change = change_reason
        else:
            rca = self.repo.create_rca_entity(
                deviation_id=dev_id,
                methodology=methodology,
                investigation_details=payload.investigation_details,
                root_cause_summary=payload.root_cause_summary,
                five_whys_chain=five_whys,
                fishbone_categories=fishbone,
                contributing_factors=factors,
                study_id=dev.study_id,
                site_id=dev.site_id,
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason,
            )
            await self.repo.save_rca(rca)

        if dev.status != DeviationStatus.RCA_COMPLETE:
            dev.status = DeviationStatus.RCA_COMPLETE
            dev.version_index += 1
            dev.reason_for_change = f"Progressed status to RCA_COMPLETE via {action}"
            log_dev = self.repo.create_audit_log_entity(
                user_id=user_id,
                user_role=user_role,
                action="DEVIATION_UPDATE",
                details=f"Updated deviation '{dev.title}' (ID: {dev.id}) status to RCA_COMPLETE.",
                record_id=dev.id,
                change_reason=change_reason,
            )
            await self.repo.save_audit_log(log_dev)

        log_rca = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action=action,
            details=f"Performed {action} for deviation ID: {dev_id}.",
            record_id=rca.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log_rca)
        return rca

    async def create_capa(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )

        dev = await self.repo.get_deviation_by_id(payload.deviation_id)
        if not dev:
            study_id = getattr(payload, "study_id", None)
            title = getattr(payload, "title", None)
            if study_id or title:
                dev = self.repo.create_deviation_entity(
                    id=payload.deviation_id,
                    study_id=study_id or "STUDY-UNKNOWN",
                    site_id=getattr(payload, "site_id", None) or "SITE-UNKNOWN",
                    title=title or f"Escalated Deviation {payload.deviation_id}",
                    description=getattr(payload, "description", None)
                    or f"Escalated deviation {payload.deviation_id}",
                    severity=getattr(payload, "severity", None)
                    or DeviationSeverity.MAJOR,
                    status=DeviationStatus.REPORTED,
                    type=DeviationType.PROTOCOL_PROCEDURE,
                    source_system="CTMS",
                    source_reference_id=payload.deviation_id,
                    created_by=user_id,
                    version_index=1,
                    reason_for_change=change_reason,
                )
                await self.repo.save_deviation(dev)
            else:
                raise QualityServiceError(
                    f"Parent deviation with ID '{payload.deviation_id}' not found.",
                    status_code=422,
                )

        if dev.status in (DeviationStatus.CLOSED, DeviationStatus.RESOLVED):
            raise QualityServiceError(
                f"Cannot create CAPA for a settled or closed deviation (current status: {dev.status}).",
                status_code=422,
            )

        if payload.rca_id:
            rca = await self.repo.get_rca_by_id(payload.rca_id)
            if not rca:
                raise QualityServiceError(
                    f"RCA with ID '{payload.rca_id}' not found.",
                    status_code=422,
                )
            if rca.deviation_id != payload.deviation_id:
                raise QualityServiceError(
                    f"RCA ID '{payload.rca_id}' is not linked to deviation ID '{payload.deviation_id}'.",
                    status_code=422,
                )

        risk_level = getattr(payload, "risk_level", "MEDIUM")
        lead_investigator = getattr(payload, "lead_investigator_id", None)
        effectiveness_days = getattr(payload, "effectiveness_interval_days", 30)
        finding_id = getattr(payload, "audit_finding_id", None)

        capa = self.repo.create_capa_entity(
            deviation_id=payload.deviation_id,
            rca_id=payload.rca_id,
            capa_type=payload.capa_type,
            action_plan=payload.action_plan,
            status=CAPAStatus.INITIATED,
            preventive_measures=payload.preventive_measures,
            risk_level=risk_level,
            lead_investigator_id=lead_investigator,
            target_completion_date=payload.target_completion_date,
            effectiveness_interval_days=effectiveness_days,
            audit_finding_id=finding_id,
            study_id=dev.study_id,
            site_id=dev.site_id,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_capa(capa)

        if dev.status != DeviationStatus.CAPA_INITIATED:
            dev.status = DeviationStatus.CAPA_INITIATED
            dev.version_index += 1
            dev.reason_for_change = (
                "Progressed status to CAPA_INITIATED via CAPA creation"
            )
            log_dev = self.repo.create_audit_log_entity(
                user_id=user_id,
                user_role=user_role,
                action="DEVIATION_UPDATE",
                details=f"Updated deviation '{dev.title}' (ID: {dev.id}) status to CAPA_INITIATED.",
                record_id=dev.id,
                change_reason=change_reason,
            )
            await self.repo.save_audit_log(log_dev)

        log_capa = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_CREATE",
            details=f"Created CAPA (ID: {capa.id}) linked to deviation ID '{payload.deviation_id}' with status INITIATED.",
            record_id=capa.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log_capa)

        outbox_event = self.repo.create_outbox_entity(
            event_type="CAPA_STAGE_TRANSITION",
            payload={
                "capa_id": capa.id,
                "deviation_id": capa.deviation_id,
                "capa_status": capa.status.value
                if hasattr(capa.status, "value")
                else str(capa.status),
                "target_ctms_status": STAGE_MAPPING.get(capa.status, "CAPA_ESCALATED"),
                "study_id": capa.study_id,
                "site_id": capa.site_id,
                "user_id": user_id,
                "user_role": user_role,
                "change_reason": change_reason,
                "version_index": capa.version_index,
            },
            status="PENDING",
            correlation_id=f"capa-stage-{capa.id}-{capa.version_index}-{uuid.uuid4().hex[:8]}",
            created_by=user_id,
            reason_for_change=change_reason,
        )
        await self.repo.save_outbox_event(outbox_event)

        return capa

    async def transition_capa(
        self,
        capa_id: str,
        to_status: CAPAStatus,
        version_index: int | None,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )

        capa = await self.repo.get_capa_by_id(capa_id)
        if not capa:
            raise QualityServiceError(
                f"CAPA record with ID '{capa_id}' not found.",
                status_code=404,
            )

        current_status = capa.status

        if version_index is not None and capa.version_index != version_index:
            raise QualityServiceError(
                f"Version conflict: The CAPA has been modified by another process. Current version: {capa.version_index}.",
                status_code=409,
            )

        if current_status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
            raise QualityServiceError(
                f"Transitions out of terminal state '{current_status}' are irreversible and strictly prohibited.",
                status_code=422,
            )

        allowed_targets = CAPA_TRANSITIONS.get(current_status, set())
        if to_status not in allowed_targets:
            raise QualityServiceError(
                f"Invalid transition from state '{current_status}' to '{to_status}'. Allowed targets: {[s.value for s in allowed_targets]}.",
                status_code=422,
            )

        # Stage-gate check: Before moving to IMPLEMENTATION_VERIFIED, check action items
        if to_status == CAPAStatus.IMPLEMENTATION_VERIFIED:
            items = await self.repo.get_action_items_by_capa(capa_id)
            if items:
                incomplete = [
                    i
                    for i in items
                    if i.status
                    not in (ActionItemStatus.COMPLETED, ActionItemStatus.CANCELLED)
                ]
                if incomplete:
                    raise QualityServiceError(
                        f"Cannot verify implementation: {len(incomplete)} action items remain incomplete.",
                        status_code=422,
                    )
            capa.actual_completion_date = datetime.now()

        capa.status = to_status
        capa.version_index += 1
        capa.reason_for_change = change_reason
        await self.repo.save_capa(capa)

        if to_status == CAPAStatus.CLOSED:
            dev = capa.deviation
            if dev and dev.status != DeviationStatus.CLOSED:
                dev.status = DeviationStatus.CLOSED
                dev.version_index += 1
                dev.reason_for_change = "Settled and closed parent deviation because linked CAPA was closed."
                await self.repo.save_deviation(dev)

                log_dev = self.repo.create_audit_log_entity(
                    user_id=user_id,
                    user_role=user_role,
                    action="DEVIATION_UPDATE",
                    details=f"Settled and closed parent deviation (ID: {dev.id}) following CAPA closure.",
                    record_id=dev.id,
                    change_reason=change_reason,
                )
                await self.repo.save_audit_log(log_dev)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_TRANSITION",
            details=f"Transitioned CAPA (ID: {capa.id}) status from '{current_status}' to '{to_status}'.",
            record_id=capa.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)

        outbox_event = self.repo.create_outbox_entity(
            event_type="CAPA_STAGE_TRANSITION",
            payload={
                "capa_id": capa.id,
                "deviation_id": capa.deviation_id,
                "capa_status": capa.status.value
                if hasattr(capa.status, "value")
                else str(capa.status),
                "target_ctms_status": STAGE_MAPPING.get(capa.status, "UNDER_REVIEW"),
                "study_id": capa.study_id,
                "site_id": capa.site_id,
                "user_id": user_id,
                "user_role": user_role,
                "change_reason": change_reason,
                "version_index": capa.version_index,
            },
            status="PENDING",
            correlation_id=f"capa-stage-{capa.id}-{capa.version_index}-{uuid.uuid4().hex[:8]}",
            created_by=user_id,
            reason_for_change=change_reason,
        )
        await self.repo.save_outbox_event(outbox_event)

        return capa

    async def update_capa(
        self,
        capa_id: str,
        payload: Any,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )

        capa = await self.repo.get_capa_by_id(capa_id)
        if not capa:
            raise QualityServiceError(
                f"CAPA record with ID '{capa_id}' not found.",
                status_code=404,
            )

        if (
            payload.version_index is not None
            and capa.version_index != payload.version_index
        ):
            raise QualityServiceError(
                f"Version conflict: The CAPA has been modified by another process. Current version: {capa.version_index}.",
                status_code=409,
            )

        if capa.status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
            raise QualityServiceError(
                f"Cannot update CAPA record because it is in terminal state '{capa.status}'.",
                status_code=422,
            )

        if payload.action_plan is not None:
            capa.action_plan = payload.action_plan
        if payload.preventive_measures is not None:
            capa.preventive_measures = payload.preventive_measures
        if payload.target_completion_date is not None:
            capa.target_completion_date = payload.target_completion_date
        if getattr(payload, "risk_level", None) is not None:
            capa.risk_level = payload.risk_level
        if getattr(payload, "effectiveness_interval_days", None) is not None:
            capa.effectiveness_interval_days = payload.effectiveness_interval_days

        capa.version_index += 1
        capa.reason_for_change = change_reason
        await self.repo.save_capa(capa)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_UPDATE",
            details=f"Updated CAPA record details (ID: {capa.id}).",
            record_id=capa.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return capa

    # --- Sub-Action Item Management ---

    async def create_action_item(
        self,
        capa_id: str,
        payload: Any,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )
        capa = await self.repo.get_capa_by_id(capa_id)
        if not capa:
            raise QualityServiceError("CAPA record not found", status_code=404)
        if capa.status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
            raise QualityServiceError(
                "Cannot add action item to terminal CAPA", status_code=422
            )

        item = self.repo.create_action_item_entity(
            capa_id=capa_id,
            title=payload.title,
            description=payload.description,
            action_type=getattr(payload, "action_type", "CORRECTIVE"),
            assigned_to=payload.assigned_to,
            due_date=payload.due_date,
            status=ActionItemStatus.OPEN,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_action_item(item)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_ACTION_ITEM_CREATE",
            details=f"Created action item '{payload.title}' on CAPA {capa_id}.",
            record_id=item.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return item

    async def update_action_item_status(
        self,
        item_id: str,
        status: ActionItemStatus,
        evidence_url: str | None,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )
        # Find item across all capas
        capas = await self.repo.get_capas()
        target_item = None
        for c in capas:
            c_items = await self.repo.get_action_items_by_capa(c.id)
            for it in c_items:
                if it.id == item_id:
                    target_item = it
                    break
            if target_item:
                break

        if not target_item:
            raise QualityServiceError("Action item not found", status_code=404)

        target_item.status = status
        if evidence_url:
            target_item.evidence_url = evidence_url
        if status == ActionItemStatus.COMPLETED:
            target_item.completed_at = datetime.now()
        target_item.version_index += 1
        target_item.reason_for_change = change_reason
        await self.repo.save_action_item(target_item)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_ACTION_ITEM_UPDATE",
            details=f"Updated action item {item_id} status to '{status}'.",
            record_id=item_id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return target_item

    # --- Effectiveness Checks ---

    async def record_effectiveness_evaluation(
        self,
        capa_id: str,
        payload: Any,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise QualityServiceError(
                "Missing change justification reason", status_code=403
            )
        capa = await self.repo.get_capa_by_id(capa_id)
        if not capa:
            raise QualityServiceError("CAPA not found", status_code=404)

        check = self.repo.create_effectiveness_check_entity(
            capa_id=capa_id,
            planned_date=payload.planned_date,
            executed_date=datetime.now(),
            metric_evaluated=payload.metric_evaluated,
            baseline_value=payload.baseline_value,
            target_value=payload.target_value,
            actual_value=payload.actual_value,
            outcome=payload.outcome,
            evaluator_id=user_id,
            comments=getattr(payload, "comments", None),
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_effectiveness_check(check)

        capa.effectiveness_outcome = payload.outcome
        capa.effectiveness_review_date = datetime.now()
        if payload.outcome == EffectivenessOutcome.INEFFECTIVE:
            capa.recurrence_detected = True
        capa.version_index += 1
        capa.reason_for_change = f"Evaluated effectiveness outcome: {payload.outcome}"
        await self.repo.save_capa(capa)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_EFFECTIVENESS_EVALUATED",
            details=f"Evaluated effectiveness for CAPA {capa_id}: Outcome = {payload.outcome}.",
            record_id=check.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return check

    async def list_audit_logs(self, user_id: str, user_role: str) -> Sequence[Any]:
        logs = await self.repo.get_audit_logs()
        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_LOG_LIST",
            details="Listed quality audit logs.",
        )
        await self.repo.save_audit_log(log)
        return logs
