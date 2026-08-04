from typing import Sequence, Any
from fastapi import HTTPException
import os

from ..database import transactional
from ..ports.repository import QualityRepositoryPort
from ..models import (
    Deviation,
    DeviationStatus,
    RootCauseAnalysis,
    CAPARecord,
    CAPAStatus,
    QualityAuditLog,
)


class QualityService:
    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    @transactional
    async def create_deviation(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Deviation:
        if not change_reason:
            raise HTTPException(
                status_code=403, detail="Missing change justification reason"
            )

        dev = Deviation(
            study_id=payload.study_id,
            site_id=payload.site_id,
            title=payload.title,
            description=payload.description,
            severity=payload.severity,
            status=DeviationStatus.REPORTED,
            type=payload.type,
            is_protocol_violation=payload.is_protocol_violation,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_deviation(dev)

        log = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_CREATE",
            details=f"Created deviation '{payload.title}' for study '{payload.study_id}' with status REPORTED.",
            record_id=dev.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return dev

    @transactional
    async def list_deviations(
        self,
        study_id: str | None,
        site_id: str | None,
        status: DeviationStatus | None,
        user_id: str,
        user_role: str,
    ) -> Sequence[Deviation]:
        deviations = await self.repo.get_deviations()
        filtered = []
        for d in deviations:
            if study_id and d.study_id != study_id:
                continue
            if site_id and d.site_id != site_id:
                continue
            if status and d.status != status:
                continue
            filtered.append(d)

        filters = f"study_id={study_id}, site_id={site_id}, status={status}"
        log = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_LIST",
            details=f"Listed deviations matching criteria: {filters}.",
        )
        await self.repo.save_audit_log(log)
        return filtered

    @transactional
    async def view_deviation(
        self, dev_id: str, user_id: str, user_role: str
    ) -> Deviation:
        dev = await self.repo.get_deviation_by_id(dev_id)
        if not dev:
            raise HTTPException(status_code=404, detail="Deviation not found")

        log = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_VIEW",
            details=f"Viewed deviation ID: {dev_id}.",
            record_id=dev_id,
        )
        await self.repo.save_audit_log(log)
        return dev

    @transactional
    async def create_or_update_rca(
        self, dev_id: str, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> RootCauseAnalysis:
        if not change_reason:
            raise HTTPException(
                status_code=403, detail="Missing change justification reason"
            )

        dev = await self.repo.get_deviation_by_id(dev_id)
        if not dev:
            raise HTTPException(status_code=404, detail="Parent deviation not found")

        rca = await self.repo.get_rca_by_deviation_id(dev_id)
        action = "RCA_CREATE"
        if rca:
            action = "RCA_UPDATE"
            if payload.version_index is not None and rca.version_index != payload.version_index:
                raise HTTPException(
                    status_code=409,
                    detail=f"Version conflict: The RCA has been modified by another process. Current version: {rca.version_index}.",
                )
            rca.methodology = payload.methodology
            rca.investigation_details = payload.investigation_details
            rca.root_cause_summary = payload.root_cause_summary
            rca.version_index += 1
            rca.reason_for_change = change_reason
        else:
            rca = RootCauseAnalysis(
                deviation_id=dev_id,
                methodology=payload.methodology,
                investigation_details=payload.investigation_details,
                root_cause_summary=payload.root_cause_summary,
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
            log_dev = QualityAuditLog(
                user_id=user_id,
                user_role=user_role,
                action="DEVIATION_UPDATE",
                details=f"Updated deviation '{dev.title}' (ID: {dev.id}) status to RCA_COMPLETE.",
                record_id=dev.id,
                change_reason=change_reason,
            )
            await self.repo.save_audit_log(log_dev)

        log_rca = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action=action,
            details=f"Performed {action} for deviation ID: {dev_id}.",
            record_id=rca.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log_rca)
        return rca

    @transactional
    async def create_capa(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> CAPARecord:
        if not change_reason:
            raise HTTPException(
                status_code=403, detail="Missing change justification reason"
            )

        dev = await self.repo.get_deviation_by_id(payload.deviation_id)
        if not dev:
            raise HTTPException(
                status_code=422,
                detail=f"Parent deviation with ID '{payload.deviation_id}' not found.",
            )

        if dev.status in (DeviationStatus.CLOSED, DeviationStatus.RESOLVED):
            raise HTTPException(
                status_code=422,
                detail=f"Cannot create CAPA for a settled or closed deviation (current status: {dev.status}).",
            )

        if payload.rca_id:
            rca = await self.repo.get_rca_by_id(payload.rca_id)
            if not rca:
                raise HTTPException(
                    status_code=422,
                    detail=f"RCA with ID '{payload.rca_id}' not found.",
                )
            if rca.deviation_id != payload.deviation_id:
                raise HTTPException(
                    status_code=422,
                    detail=f"RCA ID '{payload.rca_id}' is not linked to deviation ID '{payload.deviation_id}'.",
                )

        capa = CAPARecord(
            deviation_id=payload.deviation_id,
            rca_id=payload.rca_id,
            capa_type=payload.capa_type,
            action_plan=payload.action_plan,
            status=CAPAStatus.INITIATED,
            preventive_measures=payload.preventive_measures,
            target_completion_date=payload.target_completion_date,
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
            dev.reason_for_change = "Progressed status to CAPA_INITIATED via CAPA creation"
            log_dev = QualityAuditLog(
                user_id=user_id,
                user_role=user_role,
                action="DEVIATION_UPDATE",
                details=f"Updated deviation '{dev.title}' (ID: {dev.id}) status to CAPA_INITIATED.",
                record_id=dev.id,
                change_reason=change_reason,
            )
            await self.repo.save_audit_log(log_dev)

        log_capa = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_CREATE",
            details=f"Created CAPA (ID: {capa.id}) linked to deviation ID '{payload.deviation_id}' with status INITIATED.",
            record_id=capa.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log_capa)
        return capa

    @transactional
    async def transition_capa(
        self,
        capa_id: str,
        to_status: CAPAStatus,
        version_index: int | None,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> CAPARecord:
        if not change_reason:
            raise HTTPException(
                status_code=403, detail="Missing change justification reason"
            )

        capa = await self.repo.get_capa_by_id(capa_id)
        if not capa:
            raise HTTPException(
                status_code=404, detail=f"CAPA record with ID '{capa_id}' not found."
            )

        current_status = capa.status

        # 1. Validate version mismatch for optimistic concurrency
        if (
            version_index is not None
            and capa.version_index != version_index
        ):
            raise HTTPException(
                status_code=409,
                detail=f"Version conflict: The CAPA has been modified by another process. Current version: {capa.version_index}.",
            )

        # 2. Reject transition from terminal states
        if current_status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
            raise HTTPException(
                status_code=422,
                detail=f"Transitions out of terminal state '{current_status}' are irreversible and strictly prohibited.",
            )

        # 3. Validate against explicit transitions map
        from ..main import CAPA_TRANSITIONS
        allowed_targets = CAPA_TRANSITIONS.get(current_status, set())
        if to_status not in allowed_targets:
            raise HTTPException(
                status_code=422,
                detail=f"Invalid transition from state '{current_status}' to '{to_status}'. Allowed targets: {[s.value for s in allowed_targets]}.",
            )

        # 4. Transition status
        capa.status = to_status
        capa.version_index += 1
        capa.reason_for_change = change_reason
        await self.repo.save_capa(capa)

        # 5. Keep linked deviation state consistent (Settlement)
        if to_status == CAPAStatus.CLOSED:
            # Progress parent deviation to CLOSED
            dev = capa.deviation
            if dev and dev.status != DeviationStatus.CLOSED:
                dev.status = DeviationStatus.CLOSED
                dev.version_index += 1
                dev.reason_for_change = (
                    "Settled and closed parent deviation because linked CAPA was closed."
                )
                await self.repo.save_deviation(dev)
                
                log_dev = QualityAuditLog(
                    user_id=user_id,
                    user_role=user_role,
                    action="DEVIATION_UPDATE",
                    details=f"Settled and closed parent deviation (ID: {dev.id}) following CAPA closure.",
                    record_id=dev.id,
                    change_reason=change_reason,
                )
                await self.repo.save_audit_log(log_dev)

        log = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_TRANSITION",
            details=f"Transitioned CAPA (ID: {capa.id}) status from '{current_status}' to '{to_status}'.",
            record_id=capa.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return capa

    @transactional
    async def update_capa(
        self, capa_id: str, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> CAPARecord:
        if not change_reason:
            raise HTTPException(
                status_code=403, detail="Missing change justification reason"
            )

        capa = await self.repo.get_capa_by_id(capa_id)
        if not capa:
            raise HTTPException(
                status_code=404, detail=f"CAPA record with ID '{capa_id}' not found."
            )

        if payload.version_index is not None and capa.version_index != payload.version_index:
            raise HTTPException(
                status_code=409,
                detail=f"Version conflict: The CAPA has been modified by another process. Current version: {capa.version_index}.",
            )

        if capa.status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
            raise HTTPException(
                status_code=422,
                detail=f"Cannot update CAPA record because it is in terminal state '{capa.status}'.",
            )

        if payload.action_plan is not None:
            capa.action_plan = payload.action_plan
        if payload.preventive_measures is not None:
            capa.preventive_measures = payload.preventive_measures
        if payload.target_completion_date is not None:
            capa.target_completion_date = payload.target_completion_date

        capa.version_index += 1
        capa.reason_for_change = change_reason
        await self.repo.save_capa(capa)

        log = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="CAPA_UPDATE",
            details=f"Updated CAPA record details (ID: {capa.id}).",
            record_id=capa.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return capa

    @transactional
    async def list_audit_logs(self, user_id: str, user_role: str) -> Sequence[QualityAuditLog]:
        logs = await self.repo.get_audit_logs()
        log = QualityAuditLog(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_LOG_LIST",
            details="Listed quality audit logs.",
        )
        await self.repo.save_audit_log(log)
        return logs
