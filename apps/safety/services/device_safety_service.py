"""Medical Device & IVD Safety Investigation Service.

Requirements:
- PRD-SYS-126: Medical Device & IVD Safety Investigation Suite
- PRD-DEV-003: Adverse Device Effect Vigilance Deadlines & RTSM Quarantine
- PRD-DEV-004: Regulatory Incident Reports & CDISC SDTM DD/DO Exports
- ISO 14155:2020 / EU MDR 2017/745 / EU IVDR 2017/746 / 21 CFR Part 11
- ADR-2209 / ADR-2199
"""

import uuid
from datetime import UTC, datetime
from typing import Any

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.infrastructure.models import (
    DeviceSafetyInvestigation,
    write_audit_log,
)
from apps.safety.presentation.dtos import (
    DeviceSafetyInvestigationAdjudicateDTO,
    DeviceSafetyInvestigationCreateDTO,
    DeviceSafetyInvestigationResponseDTO,
    DeviceVigilanceReportResponseDTO,
)
from apps.safety.services.regulatory_audit_ledger import (
    RegulatoryAuditLedger,
)


class DeviceSafetyService:
    """Encapsulates Adverse Device Effect investigations, vigilance deadlines,

    Safety Physician adjudication sealing, and EU MDR/IVDR vigilance reporting.
    """

    @classmethod
    def map_investigation_to_dto(
        cls,
        inv: DeviceSafetyInvestigation,
    ) -> DeviceSafetyInvestigationResponseDTO:
        """Serializes a DeviceSafetyInvestigation ORM entity to a Pydantic DTO with countdown clocks."""
        now_ts = datetime.now(UTC)
        due = (
            inv.vigilance_due_date
            if inv.vigilance_due_date.tzinfo
            else inv.vigilance_due_date.replace(tzinfo=UTC)
        )
        days_remaining = (due.date() - now_ts.date()).days
        is_overdue = days_remaining < 0 and inv.status != "CLOSED"

        return DeviceSafetyInvestigationResponseDTO(
            adjudicated_at=(
                inv.adjudicated_at.isoformat() if inv.adjudicated_at else None
            ),
            capa_reference=inv.capa_reference,
            capa_required=inv.capa_required,
            case_number=inv.case_number,
            causality=inv.causality,
            classification=inv.classification,
            competent_authority_notified=inv.competent_authority_notified,
            created_at=inv.created_at.isoformat() if inv.created_at else "",
            created_by=inv.created_by,
            days_remaining=days_remaining,
            deficiency_id=inv.deficiency_id,
            device_name=inv.device_name,
            device_udi_di=inv.device_udi_di,
            esignature_manifest_id=inv.esignature_manifest_id,
            fsca_required=inv.fsca_required,
            id=inv.id,
            investigation_notes=inv.investigation_notes,
            is_overdue=is_overdue,
            is_usade=inv.is_usade,
            lot_number=inv.lot_number,
            notified_body_notified=inv.notified_body_notified,
            reason_for_change=inv.reason_for_change,
            root_cause_summary=inv.root_cause_summary,
            safety_physician_user_id=inv.safety_physician_user_id,
            serial_number=inv.serial_number,
            site_id=inv.site_id,
            status=inv.status,
            study_id=inv.study_id,
            subject_id=inv.subject_id,
            version_index=inv.version_index,
            vigilance_deadline_days=inv.vigilance_deadline_days,
            vigilance_due_date=inv.vigilance_due_date.isoformat(),
            vigilance_reporting_required=inv.vigilance_reporting_required,
        )

    @classmethod
    async def open_investigation(
        cls,
        session: AsyncSession,
        payload: DeviceSafetyInvestigationCreateDTO,
        user_id: str,
        change_reason: str | None = None,
    ) -> DeviceSafetyInvestigationResponseDTO:
        """Opens a new Adverse Device Effect safety investigation case with calculated vigilance deadline."""
        now_ts = datetime.now(UTC).replace(tzinfo=None)
        case_num = (
            payload.case_number
            or f"DEV-{payload.study_id}-{uuid.uuid4().hex[:6].upper()}"
        )
        classification = payload.classification.upper()

        deadline_days, due_date = (
            RegulatoryAuditLedger.calculate_device_vigilance_deadline(
                classification=classification,
                now_ts=now_ts,
            )
        )

        investigation = DeviceSafetyInvestigation(
            capa_reference=None,
            capa_required=False,
            case_number=case_num,
            causality=payload.causality,
            classification=classification,
            competent_authority_notified=False,
            created_at=now_ts,
            created_by=user_id,
            deficiency_id=payload.deficiency_id,
            device_name=payload.device_name,
            device_udi_di=payload.device_udi_di,
            fsca_required=False,
            investigation_notes=payload.investigation_notes,
            is_usade=payload.is_usade or classification == "USADE",
            lot_number=payload.lot_number,
            notified_body_notified=False,
            reason_for_change=payload.reason_for_change
            or change_reason
            or "Open device safety investigation",
            root_cause_summary=None,
            serial_number=payload.serial_number,
            site_id=payload.site_id,
            status="OPEN",
            study_id=payload.study_id,
            subject_id=payload.subject_id,
            version_index=1,
            vigilance_deadline_days=deadline_days,
            vigilance_due_date=due_date,
            vigilance_reporting_required=True,
        )
        session.add(investigation)
        await session.flush()

        await write_audit_log(
            action="DEVICE_SAFETY_INVESTIGATION_OPENED",
            created_by=user_id,
            details=f"Opened device safety case {case_num} ({classification}) for deficiency {payload.deficiency_id}",
            reason_for_change=payload.reason_for_change
            or change_reason
            or "Open device safety investigation",
            record_id=investigation.id,
            session=session,
        )
        await session.commit()
        return cls.map_investigation_to_dto(investigation)

    @classmethod
    async def list_investigations(
        cls,
        session: AsyncSession,
        study_id: str | None = None,
        site_id: str | None = None,
        classification: str | None = None,
        status: str | None = None,
    ) -> list[DeviceSafetyInvestigationResponseDTO]:
        """Queries and lists device safety investigation cases matching parameters."""
        stmt = select(DeviceSafetyInvestigation)
        if study_id:
            stmt = stmt.where(DeviceSafetyInvestigation.study_id == study_id)
        if site_id:
            stmt = stmt.where(DeviceSafetyInvestigation.site_id == site_id)
        if classification:
            stmt = stmt.where(
                DeviceSafetyInvestigation.classification == classification.upper()
            )
        if status:
            stmt = stmt.where(DeviceSafetyInvestigation.status == status.upper())

        stmt = stmt.order_by(DeviceSafetyInvestigation.created_at.desc())
        res = await session.execute(stmt)
        cases = res.scalars().all()
        return [cls.map_investigation_to_dto(c) for c in cases]

    @classmethod
    async def get_investigation(
        cls,
        session: AsyncSession,
        investigation_id: str,
    ) -> DeviceSafetyInvestigationResponseDTO:
        """Retrieves single device safety investigation case by ID."""
        stmt = select(DeviceSafetyInvestigation).where(
            DeviceSafetyInvestigation.id == investigation_id
        )
        res = await session.execute(stmt)
        case = res.scalars().first()
        if not case:
            raise HTTPException(
                detail=f"Device safety investigation '{investigation_id}' not found.",
                status_code=404,
            )
        return cls.map_investigation_to_dto(case)

    @classmethod
    async def adjudicate_investigation(
        cls,
        session: AsyncSession,
        investigation_id: str,
        payload: DeviceSafetyInvestigationAdjudicateDTO,
        user_id: str,
        change_reason: str | None = None,
    ) -> DeviceSafetyInvestigationResponseDTO:
        """Adjudicates Adverse Device Effect causality and SADE status with 21 CFR Part 11 signature seal."""
        stmt = select(DeviceSafetyInvestigation).where(
            DeviceSafetyInvestigation.id == investigation_id
        )
        res = await session.execute(stmt)
        case = res.scalars().first()
        if not case:
            raise HTTPException(
                detail=f"Device safety investigation '{investigation_id}' not found.",
                status_code=404,
            )

        if not payload.signature_token or len(payload.signature_token) < 5:
            raise HTTPException(
                detail="Electronic signature token is required for Safety Physician adjudication.",
                status_code=400,
            )

        now_ts = datetime.now(UTC).replace(tzinfo=None)

        # Seal adjudication via unified RegulatoryAuditLedger
        seal_manifest = RegulatoryAuditLedger.seal_device_adjudication(
            capa_required=payload.capa_required,
            case_number=case.case_number,
            causality=payload.causality.upper(),
            classification=payload.classification.upper(),
            investigation_id=case.id,
            reason_for_change=payload.reason_for_change or change_reason,
            safety_physician_user_id=user_id,
            signature_token=payload.signature_token,
        )

        case.classification = payload.classification.upper()
        case.causality = payload.causality.upper()
        case.is_usade = payload.is_usade or case.classification == "USADE"
        if payload.investigation_notes:
            case.investigation_notes = payload.investigation_notes
        if payload.root_cause_summary:
            case.root_cause_summary = payload.root_cause_summary
        case.capa_required = payload.capa_required
        case.capa_reference = payload.capa_reference
        case.fsca_required = payload.fsca_required
        case.status = "RESOLVED"
        case.safety_physician_user_id = user_id
        case.adjudicated_at = now_ts
        case.esignature_manifest_id = seal_manifest["esignature_manifest_id"]
        case.version_index += 1
        case.reason_for_change = payload.reason_for_change or change_reason

        session.add(case)
        await write_audit_log(
            action="DEVICE_SAFETY_INVESTIGATION_ADJUDICATED",
            created_by=user_id,
            details=(
                f"Adjudicated case {case.case_number}: classification={case.classification}, "
                f"causality={case.causality}, capa_required={case.capa_required}. "
                f"Seal digest: {seal_manifest['adjudication_digest'][:16]}..."
            ),
            reason_for_change=payload.reason_for_change or change_reason,
            record_id=case.id,
            session=session,
            version_index=case.version_index,
        )
        await session.commit()
        return cls.map_investigation_to_dto(case)

    @classmethod
    async def generate_vigilance_report(
        cls,
        session: AsyncSession,
        investigation_id: str,
    ) -> DeviceVigilanceReportResponseDTO:
        """Generates and seals an EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2)."""
        stmt = select(DeviceSafetyInvestigation).where(
            DeviceSafetyInvestigation.id == investigation_id
        )
        res = await session.execute(stmt)
        case = res.scalars().first()
        if not case:
            raise HTTPException(
                detail=f"Device safety investigation '{investigation_id}' not found.",
                status_code=404,
            )

        case_dict: dict[str, Any] = {
            "adjudicated_at": (
                case.adjudicated_at.isoformat() if case.adjudicated_at else None
            ),
            "capa_reference": case.capa_reference,
            "capa_required": case.capa_required,
            "case_number": case.case_number,
            "causality": case.causality,
            "classification": case.classification,
            "deficiency_id": case.deficiency_id,
            "device_name": case.device_name,
            "device_udi_di": case.device_udi_di,
            "fsca_required": case.fsca_required,
            "investigation_notes": case.investigation_notes,
            "is_usade": case.is_usade,
            "lot_number": case.lot_number,
            "root_cause_summary": case.root_cause_summary,
            "safety_physician_user_id": case.safety_physician_user_id,
            "serial_number": case.serial_number,
            "site_id": case.site_id,
            "status": case.status,
            "study_id": case.study_id,
            "subject_id": case.subject_id,
            "vigilance_deadline_days": case.vigilance_deadline_days,
            "vigilance_due_date": case.vigilance_due_date.isoformat(),
        }

        report_data = RegulatoryAuditLedger.seal_device_vigilance_report(case_dict)

        return DeviceVigilanceReportResponseDTO(
            case_number=report_data["case_number"],
            classification=report_data["classification"],
            device_name=report_data["device_name"],
            incident_summary=report_data["incident_summary"],
            lot_number=report_data["lot_number"],
            payload_json=report_data["payload_json"],
            payload_xml=report_data["payload_xml"],
            report_type=report_data["report_type"],
            reporting_authority=report_data["reporting_authority"],
            serial_number=report_data["serial_number"],
            udi_di=report_data["udi_di"],
            vigilance_deadline_days=report_data["vigilance_deadline_days"],
            vigilance_due_date=report_data["vigilance_due_date"],
        )
