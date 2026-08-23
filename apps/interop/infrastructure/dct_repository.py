"""Repository implementation for DCT Telehealth, DTP Logistics, and Pediatric Assent.

Provides async persistence and retrieval with strict GxP 21 CFR Part 11 auditing.
Requirements: PRD-DCT-003, PRD-DCT-004, PRD-DCT-005, PRD-DCT-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.domain.dct_models import DCTDashboardMetrics
from apps.interop.domain.ports import IDCTRepositoryPort
from apps.interop.infrastructure.dct_models import (
    DCTAuditLogRecord,
    DTPShipmentRecord,
    InHomeVisitRecord,
    PediatricAssentRecord,
    TelehealthSessionRecord,
)


class DCTRepository(IDCTRepositoryPort):
    """Async SQLAlchemy repository for DCT operations."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> Any | None:
        return await self.get_telehealth_session_by_id(entity_id)

    async def save(self, entity: Any) -> Any:
        self.session.add(entity)
        await self.session.commit()
        await self.session.refresh(entity)
        return entity

    # =========================================================================
    # Telehealth Sessions
    # =========================================================================

    async def create_telehealth_session(
        self, record: TelehealthSessionRecord | dict[str, Any]
    ) -> TelehealthSessionRecord:
        """Persist a new WebRTC telehealth video consultation session."""
        if isinstance(record, dict):
            record = TelehealthSessionRecord(**record)
        self.session.add(record)
        await self.session.commit()
        await self.session.refresh(record)
        return record

    async def get_telehealth_session_by_id(
        self, session_id: str
    ) -> TelehealthSessionRecord | None:
        """Retrieve telehealth session by internal primary key."""
        stmt = select(TelehealthSessionRecord).where(
            TelehealthSessionRecord.id == session_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_telehealth_session_by_room(
        self, room_id: str
    ) -> TelehealthSessionRecord | None:
        """Retrieve telehealth session by room identifier."""
        stmt = select(TelehealthSessionRecord).where(
            TelehealthSessionRecord.room_id == room_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_telehealth_sessions(
        self, study_id: str | None = None, subject_id: str | None = None
    ) -> list[TelehealthSessionRecord]:
        """List telehealth sessions matching study or subject filters."""
        stmt = select(TelehealthSessionRecord)
        if study_id:
            stmt = stmt.where(TelehealthSessionRecord.study_id == study_id)
        if subject_id:
            stmt = stmt.where(TelehealthSessionRecord.subject_id == subject_id)
        stmt = stmt.order_by(TelehealthSessionRecord.scheduled_start_utc.desc())
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def update_telehealth_session(
        self, record: TelehealthSessionRecord
    ) -> TelehealthSessionRecord:
        """Update existing telehealth session record."""
        record.version_index += 1
        await self.session.commit()
        await self.session.refresh(record)
        return record

    # =========================================================================
    # Tiered Pediatric Assent
    # =========================================================================

    async def create_pediatric_assent(
        self, record: PediatricAssentRecord | dict[str, Any]
    ) -> PediatricAssentRecord:
        """Persist a tiered pediatric assent and LAR consent manifest."""
        if isinstance(record, dict):
            record = PediatricAssentRecord(**record)
        self.session.add(record)
        await self.session.commit()
        await self.session.refresh(record)
        return record

    async def get_pediatric_assent_by_id(
        self, assent_id: str
    ) -> PediatricAssentRecord | None:
        """Retrieve pediatric assent by assent identifier."""
        stmt = select(PediatricAssentRecord).where(
            (PediatricAssentRecord.assent_id == assent_id)
            | (PediatricAssentRecord.id == assent_id)
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_pediatric_assents(
        self, study_id: str | None = None, subject_id: str | None = None
    ) -> list[PediatricAssentRecord]:
        """List pediatric assent records matching filters."""
        stmt = select(PediatricAssentRecord)
        if study_id:
            stmt = stmt.where(PediatricAssentRecord.study_id == study_id)
        if subject_id:
            stmt = stmt.where(PediatricAssentRecord.subject_id == subject_id)
        stmt = stmt.order_by(PediatricAssentRecord.created_at.desc())
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # =========================================================================
    # Direct-to-Patient (DTP) Logistics
    # =========================================================================

    async def create_dtp_shipment(
        self, record: DTPShipmentRecord | dict[str, Any]
    ) -> DTPShipmentRecord:
        """Persist a new Direct-to-Patient courier shipment order."""
        if isinstance(record, dict):
            record = DTPShipmentRecord(**record)
        self.session.add(record)
        await self.session.commit()
        await self.session.refresh(record)
        return record

    async def get_dtp_shipment_by_tracking(
        self, tracking_number: str
    ) -> DTPShipmentRecord | None:
        """Retrieve DTP shipment by tracking number."""
        stmt = select(DTPShipmentRecord).where(
            DTPShipmentRecord.tracking_number == tracking_number
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_dtp_shipment_by_id(
        self, shipment_id: str
    ) -> DTPShipmentRecord | None:
        """Retrieve DTP shipment by shipment ID or primary key."""
        stmt = select(DTPShipmentRecord).where(
            (DTPShipmentRecord.shipment_id == shipment_id)
            | (DTPShipmentRecord.id == shipment_id)
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_dtp_shipments(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        subject_pseudonym_id: str | None = None,
    ) -> list[DTPShipmentRecord]:
        """List DTP shipments matching study, site, or subject."""
        stmt = select(DTPShipmentRecord)
        if study_id:
            stmt = stmt.where(DTPShipmentRecord.study_id == study_id)
        if site_id:
            stmt = stmt.where(DTPShipmentRecord.site_id == site_id)
        if subject_pseudonym_id:
            stmt = stmt.where(
                DTPShipmentRecord.subject_pseudonym_id == subject_pseudonym_id
            )
        stmt = stmt.order_by(DTPShipmentRecord.created_at.desc())
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def update_dtp_shipment(self, record: DTPShipmentRecord) -> DTPShipmentRecord:
        """Update DTP shipment status, temperature telemetry, or custody handoffs."""
        record.version_index += 1
        await self.session.commit()
        await self.session.refresh(record)
        return record

    # =========================================================================
    # In-Home Healthcare Nurse Visits
    # =========================================================================

    async def create_in_home_visit(
        self, record: InHomeVisitRecord | dict[str, Any]
    ) -> InHomeVisitRecord:
        """Persist a scheduled decentralized in-home nurse visit."""
        if isinstance(record, dict):
            record = InHomeVisitRecord(**record)
        self.session.add(record)
        await self.session.commit()
        await self.session.refresh(record)
        return record

    async def get_in_home_visit_by_id(self, visit_id: str) -> InHomeVisitRecord | None:
        """Retrieve in-home visit by visit ID or primary key."""
        stmt = select(InHomeVisitRecord).where(
            (InHomeVisitRecord.visit_id == visit_id)
            | (InHomeVisitRecord.id == visit_id)
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_in_home_visits(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        nurse_id: str | None = None,
        subject_id: str | None = None,
    ) -> list[InHomeVisitRecord]:
        """List in-home visiting nurse assignments matching filters."""
        stmt = select(InHomeVisitRecord)
        if study_id:
            stmt = stmt.where(InHomeVisitRecord.study_id == study_id)
        if site_id:
            stmt = stmt.where(InHomeVisitRecord.site_id == site_id)
        if nurse_id:
            stmt = stmt.where(InHomeVisitRecord.nurse_id == nurse_id)
        if subject_id:
            stmt = stmt.where(InHomeVisitRecord.subject_id == subject_id)
        stmt = stmt.order_by(InHomeVisitRecord.scheduled_start_utc.asc())
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def update_in_home_visit(
        self, record: InHomeVisitRecord
    ) -> InHomeVisitRecord:
        """Update in-home visit status, procedures, or mobile eCRF payload."""
        record.version_index += 1
        await self.session.commit()
        await self.session.refresh(record)
        return record

    # =========================================================================
    # GxP Audit Trails & Dashboard Metrics
    # =========================================================================

    async def write_dct_audit_log(
        self,
        study_id: str,
        action: str,
        entity_type: str,
        entity_id: str,
        user_id: str,
        user_role: str,
        change_reason: str,
        details: dict[str, Any],
    ) -> DCTAuditLogRecord:
        """Write an immutable GxP 21 CFR Part 11 audit log entry."""
        audit_entry = DCTAuditLogRecord(
            study_id=study_id,
            action=action,
            entity_type=entity_type,
            entity_id=entity_id,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
            details=details,
        )
        self.session.add(audit_entry)
        await self.session.commit()
        await self.session.refresh(audit_entry)
        return audit_entry

    async def get_dct_metrics(self, study_id: str) -> DCTDashboardMetrics:
        """Compute aggregated DCT operational metrics for study."""
        sessions = await self.list_telehealth_sessions(study_id=study_id)
        active_rooms = sum(
            1 for s in sessions if s.status in ("IN_PROGRESS", "WAITING_ROOM")
        )

        shipments = await self.list_dtp_shipments(study_id=study_id)
        active_shipments = sum(
            1
            for sh in shipments
            if sh.status in ("COURIER_DISPATCHED", "IN_TRANSIT", "OUT_FOR_DELIVERY")
        )
        excursions = sum(
            1
            for sh in shipments
            if sh.excursion_severity != "NONE" or sh.status == "TEMP_EXCURSION_HOLD"
        )

        visits = await self.list_in_home_visits(study_id=study_id)
        scheduled_visits = sum(
            1 for v in visits if v.status in ("SCHEDULED", "CONFIRMED", "EN_ROUTE")
        )
        completed_visits = sum(1 for v in visits if v.status == "VISIT_COMPLETED")
        pending_offline = sum(1 for v in visits if v.sync_status == "PENDING_SYNC")

        assents = await self.list_pediatric_assents(study_id=study_id)

        return DCTDashboardMetrics(
            total_telehealth_sessions=len(sessions),
            active_video_rooms=active_rooms,
            total_dtp_shipments=len(shipments),
            active_cold_chain_shipments=active_shipments,
            temperature_excursion_alerts=excursions,
            scheduled_nurse_visits=scheduled_visits,
            completed_home_visits=completed_visits,
            pending_offline_syncs=pending_offline,
            pediatric_assents_completed=len(assents),
        )

    get_dashboard_metrics = get_dct_metrics
