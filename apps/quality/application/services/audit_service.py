import hashlib
import json
from collections.abc import Sequence
from datetime import datetime
from typing import Any

from apps.quality.domain.models import (
    AuditStatus,
    AuditType,
    CAPAStatus,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    FindingSeverity,
)
from apps.quality.domain.ports import QualityRepositoryPort
from packages.hexagonal import DomainError


class AuditServiceError(DomainError):
    def __init__(self, message: str, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


class ClinicalAuditService:
    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    # --- Audit Engagements ---

    async def create_audit(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise AuditServiceError(
                "Missing change justification reason", status_code=403
            )

        audit = self.repo.create_audit_entity(
            audit_number=payload.audit_number,
            study_id=payload.study_id,
            site_id=payload.site_id,
            vendor_name=getattr(payload, "vendor_name", None),
            audit_type=getattr(payload, "audit_type", AuditType.SITE_AUDIT),
            lead_auditor=payload.lead_auditor,
            planned_start_date=payload.planned_start_date,
            planned_end_date=payload.planned_end_date,
            status=AuditStatus.PLANNED,
            scope_summary=payload.scope_summary,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_audit(audit)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_CREATE",
            details=f"Created clinical audit {payload.audit_number} for study '{payload.study_id}'.",
            record_id=audit.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return audit

    async def list_audits(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        audit_type: str | None = None,
        status: str | None = None,
        user_id: str = "system",
        user_role: str = "auditor",
    ) -> Sequence[Any]:
        all_audits = await self.repo.get_audits()
        filtered = []
        for a in all_audits:
            if study_id and a.study_id != study_id:
                continue
            if site_id and a.site_id != site_id:
                continue
            if audit_type and a.audit_type != audit_type:
                continue
            if status and a.status != status:
                continue
            filtered.append(a)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_LIST",
            details=f"Listed audits (study={study_id}, site={site_id}, status={status}).",
        )
        await self.repo.save_audit_log(log)
        return filtered

    async def get_audit_by_id(self, audit_id: str, user_id: str, user_role: str) -> Any:
        audit = await self.repo.get_audit_by_id(audit_id)
        if not audit:
            raise AuditServiceError(
                f"Audit with ID '{audit_id}' not found.", status_code=404
            )

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_VIEW",
            details=f"Viewed audit ID: {audit_id} ({audit.audit_number}).",
            record_id=audit_id,
        )
        await self.repo.save_audit_log(log)
        return audit

    async def update_audit_status(
        self,
        audit_id: str,
        status: AuditStatus,
        actual_start_date: datetime | None,
        actual_end_date: datetime | None,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise AuditServiceError(
                "Missing change justification reason", status_code=403
            )

        audit = await self.repo.get_audit_by_id(audit_id)
        if not audit:
            raise AuditServiceError(
                f"Audit with ID '{audit_id}' not found.", status_code=404
            )

        audit.status = status
        if actual_start_date:
            audit.actual_start_date = actual_start_date
        if actual_end_date:
            audit.actual_end_date = actual_end_date
        audit.version_index += 1
        audit.reason_for_change = change_reason
        await self.repo.save_audit(audit)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_STATUS_UPDATE",
            details=f"Updated audit {audit_id} status to '{status}'.",
            record_id=audit_id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return audit

    # --- Findings ---

    async def create_finding(
        self,
        audit_id: str,
        payload: Any,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise AuditServiceError(
                "Missing change justification reason", status_code=403
            )

        audit = await self.repo.get_audit_by_id(audit_id)
        if not audit:
            raise AuditServiceError(
                f"Parent audit '{audit_id}' not found.", status_code=404
            )

        finding = self.repo.create_audit_finding_entity(
            audit_id=audit_id,
            finding_number=payload.finding_number,
            severity=getattr(payload, "severity", FindingSeverity.MAJOR),
            category=payload.category,
            condition=payload.condition,
            criteria=payload.criteria,
            cause=payload.cause,
            effect=payload.effect,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_audit_finding(finding)

        if audit.status == AuditStatus.IN_PROGRESS:
            audit.status = AuditStatus.FINDINGS_REPORTED
            await self.repo.save_audit(audit)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_FINDING_CREATE",
            details=f"Logged audit finding {payload.finding_number} ({finding.severity}) on audit {audit_id}.",
            record_id=finding.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return finding

    async def list_findings_by_audit(self, audit_id: str) -> Sequence[Any]:
        return await self.repo.get_findings_by_audit(audit_id)

    async def promote_finding_to_capa(
        self,
        finding_id: str,
        action_plan: str,
        preventive_measures: str | None,
        target_completion_date: datetime | None,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        """
        1-Click promotion of a Critical/Major audit finding into a formal CAPA record.
        """
        if not change_reason:
            raise AuditServiceError(
                "Missing change justification reason", status_code=403
            )

        finding = await self.repo.get_finding_by_id(finding_id)
        if not finding:
            raise AuditServiceError(
                f"Audit finding '{finding_id}' not found.", status_code=404
            )

        if finding.capa_id:
            raise AuditServiceError(
                f"Finding '{finding_id}' is already linked to CAPA '{finding.capa_id}'.",
                status_code=409,
            )

        audit = await self.repo.get_audit_by_id(finding.audit_id)
        study_id = audit.study_id if audit else "STUDY-UNKNOWN"
        site_id = audit.site_id if audit else None

        # 1. Create a corresponding deviation for traceability
        dev_severity = (
            DeviationSeverity.CRITICAL
            if finding.severity == FindingSeverity.CRITICAL
            else DeviationSeverity.MAJOR
        )
        deviation = self.repo.create_deviation_entity(
            study_id=study_id,
            site_id=site_id,
            title=f"Audit Finding {finding.finding_number}: {finding.condition[:100]}",
            description=f"Condition: {finding.condition}\nCriteria: {finding.criteria}\nCause: {finding.cause}\nEffect: {finding.effect}",
            severity=dev_severity,
            status=DeviationStatus.CAPA_INITIATED,
            type=DeviationType.GCP_COMPLIANCE,
            category=finding.category,
            is_protocol_violation=True,
            impact_safety=(finding.severity == FindingSeverity.CRITICAL),
            impact_data=True,
            impact_compliance=True,
            source_system="CLINICAL_AUDIT",
            source_reference_id=finding.finding_number,
            created_by=user_id,
            version_index=1,
            reason_for_change=f"Automated deviation created via 1-click promotion from Audit finding {finding.finding_number}",
        )
        await self.repo.save_deviation(deviation)

        # 2. Create the linked CAPA
        capa = self.repo.create_capa_entity(
            deviation_id=deviation.id,
            rca_id=None,
            capa_type="BOTH",
            action_plan=action_plan,
            status=CAPAStatus.INITIATED,
            preventive_measures=preventive_measures,
            risk_level="HIGH"
            if finding.severity == FindingSeverity.CRITICAL
            else "MEDIUM",
            target_completion_date=target_completion_date,
            audit_finding_id=finding_id,
            study_id=study_id,
            site_id=site_id,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_capa(capa)

        finding.capa_id = capa.id
        finding.version_index += 1
        finding.reason_for_change = f"Promoted to CAPA {capa.id}"
        await self.repo.save_audit_finding(finding)

        if audit and audit.status != AuditStatus.CAPA_PENDING:
            audit.status = AuditStatus.CAPA_PENDING
            await self.repo.save_audit(audit)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="AUDIT_FINDING_PROMOTED_TO_CAPA",
            details=f"Promoted finding {finding.finding_number} to CAPA {capa.id} (Deviation {deviation.id}).",
            record_id=capa.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return capa

    # --- 1-Click Inspection Readiness Dossier ---

    async def compile_inspection_readiness_dossier(
        self, study_id: str, user_id: str, user_role: str
    ) -> dict[str, Any]:
        """
        Compiles an on-demand, cryptographically hashed inspection readiness dossier
        aggregating deviations, RCAs, CAPAs, RBQM profiles, and clinical audit records.
        """
        all_devs = await self.repo.get_deviations()
        study_devs = [d for d in all_devs if d.study_id == study_id]

        all_capas = await self.repo.get_capas()
        study_capas = [c for c in all_capas if c.study_id == study_id]

        all_audits = await self.repo.get_audits()
        study_audits = [a for a in all_audits if a.study_id == study_id]

        profiles = await self.repo.get_site_risk_profiles(study_id)
        qtls = await self.repo.get_qtls(study_id)
        breaches = await self.repo.get_qtl_breaches(study_id)

        # Compute summary metrics
        total_devs = len(study_devs)
        critical_devs = sum(
            1 for d in study_devs if d.severity == DeviationSeverity.CRITICAL
        )
        closed_capas = sum(1 for c in study_capas if c.status == CAPAStatus.CLOSED)
        capa_closure_rate = (
            round((closed_capas / len(study_capas)) * 100, 1) if study_capas else 100.0
        )

        dossier_content = {
            "study_id": study_id,
            "compilation_timestamp": datetime.now().isoformat(),
            "compiled_by": user_id,
            "summary_statistics": {
                "total_deviations": total_devs,
                "critical_deviations_count": critical_devs,
                "total_capas": len(study_capas),
                "closed_capas": closed_capas,
                "capa_closure_rate_pct": capa_closure_rate,
                "audits_completed": sum(
                    1 for a in study_audits if a.status == AuditStatus.COMPLETED
                ),
                "active_site_risk_profiles": len(profiles),
                "qtl_breaches_count": len(breaches),
            },
            "deviations": [
                {
                    "id": d.id,
                    "site_id": d.site_id,
                    "title": d.title,
                    "severity": d.severity,
                    "status": d.status,
                    "type": d.type,
                    "is_protocol_violation": d.is_protocol_violation,
                    "created_at": d.created_at.isoformat(),
                }
                for d in study_devs
            ],
            "capas": [
                {
                    "id": c.id,
                    "deviation_id": c.deviation_id,
                    "status": c.status,
                    "action_plan": c.action_plan,
                    "effectiveness_outcome": c.effectiveness_outcome,
                    "recurrence_detected": c.recurrence_detected,
                }
                for c in study_capas
            ],
            "audits": [
                {
                    "id": a.id,
                    "audit_number": a.audit_number,
                    "audit_type": a.audit_type,
                    "status": a.status,
                    "findings_count": len(getattr(a, "findings", [])),
                }
                for a in study_audits
            ],
            "qtl_status": [
                {
                    "parameter_name": q.parameter_name,
                    "tolerance_limit": f"{q.tolerance_limit}{q.unit}",
                    "is_breached": q.is_breached,
                    "breach_count": q.breach_count,
                }
                for q in qtls
            ],
        }

        # Deterministic SHA-256 seal for tamper evidence
        serialized = json.dumps(dossier_content, sort_keys=True)
        merkle_seal = hashlib.sha256(serialized.encode("utf-8")).hexdigest()
        dossier_content["cryptographic_tamper_seal"] = merkle_seal

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="INSPECTION_DOSSIER_COMPILED",
            details=f"Compiled GxP Inspection Readiness Dossier for study '{study_id}'. Seal: {merkle_seal[:16]}...",
            merkle_hash=merkle_seal,
        )
        await self.repo.save_audit_log(log)

        return dossier_content
