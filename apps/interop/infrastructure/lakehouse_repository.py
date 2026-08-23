"""SQLAlchemy implementation of the LakehouseRepositoryPort.

Requirements: PRD-CDR-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import uuid
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.domain.lakehouse_models import (
    CanonicalClinicalRecord,
    ClinicalSnapshot,
    DataStreamType,
    LakehouseMetrics,
    LockMilestoneType,
    SnapshotFilterCriteria,
)
from apps.interop.domain.ports import LakehouseRepositoryPort
from apps.interop.infrastructure.lakehouse_models import (
    LakehouseAuditLog,
    LakehouseCanonicalRecord,
    LakehouseSnapshot,
    LakehouseSnapshotRecordLink,
)


class LakehouseRepository(LakehouseRepositoryPort):
    """Concrete repository implementing Lakehouse persistence with SQLAlchemy."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> Any | None:
        """Get snapshot by ID (fulfilling RepositoryPort interface)."""
        return await self.get_snapshot_by_id(entity_id)

    async def save(self, entity: Any) -> Any:
        """Generic save (fulfilling RepositoryPort interface)."""
        if isinstance(entity, ClinicalSnapshot):
            return await self.save_snapshot(entity, [])
        return entity

    async def save_canonical_records(
        self, records: list[CanonicalClinicalRecord]
    ) -> list[CanonicalClinicalRecord]:
        for rec in records:
            db_record = LakehouseCanonicalRecord(
                id=rec.record_id,
                study_id=rec.study_id,
                site_id=rec.site_id,
                subject_id=rec.subject_id,
                stream_type=rec.stream_type.value,
                domain=rec.domain,
                item_code=rec.item_code,
                item_name=rec.item_name,
                observation_datetime=rec.observation_datetime,
                numeric_value=rec.numeric_value,
                text_value=rec.text_value,
                unit=rec.unit,
                reference_low=rec.reference_low,
                reference_high=rec.reference_high,
                abnormal_flag=rec.abnormal_flag,
                is_unblinded=rec.is_unblinded,
                raw_payload=rec.raw_payload,
                normalized_attributes=rec.normalized_attributes,
                data_hash=rec.data_hash,
                created_at=rec.created_at,
            )
            self.session.add(db_record)
        await self.session.flush()
        return records

    async def get_records(
        self, study_id: str, filter_criteria: SnapshotFilterCriteria | None = None
    ) -> list[CanonicalClinicalRecord]:
        stmt = select(LakehouseCanonicalRecord).where(
            LakehouseCanonicalRecord.study_id == study_id
        )
        if filter_criteria:
            if filter_criteria.cutoff_datetime:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.observation_datetime
                    <= filter_criteria.cutoff_datetime
                )
            if filter_criteria.subject_ids:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.subject_id.in_(filter_criteria.subject_ids)
                )
            if filter_criteria.site_ids:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.site_id.in_(filter_criteria.site_ids)
                )
            if filter_criteria.stream_types:
                stream_vals = [s.value for s in filter_criteria.stream_types]
                stmt = stmt.where(LakehouseCanonicalRecord.stream_type.in_(stream_vals))
            if filter_criteria.domains:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.domain.in_(filter_criteria.domains)
                )

        stmt = stmt.order_by(LakehouseCanonicalRecord.observation_datetime.asc())
        db_records = (await self.session.execute(stmt)).scalars().all()

        return [
            CanonicalClinicalRecord(
                record_id=r.id,
                study_id=r.study_id,
                site_id=r.site_id,
                subject_id=r.subject_id,
                stream_type=DataStreamType(r.stream_type),
                domain=r.domain,
                item_code=r.item_code,
                item_name=r.item_name,
                observation_datetime=r.observation_datetime,
                numeric_value=r.numeric_value,
                text_value=r.text_value,
                unit=r.unit,
                reference_low=r.reference_low,
                reference_high=r.reference_high,
                abnormal_flag=r.abnormal_flag,
                is_unblinded=r.is_unblinded,
                raw_payload=r.raw_payload,
                normalized_attributes=r.normalized_attributes,
                data_hash=r.data_hash,
                created_at=r.created_at,
            )
            for r in db_records
        ]

    async def save_snapshot(
        self, snapshot: ClinicalSnapshot, record_ids: list[str]
    ) -> ClinicalSnapshot:
        db_snapshot = LakehouseSnapshot(
            id=snapshot.snapshot_id,
            study_id=snapshot.study_id,
            version_tag=snapshot.version_tag,
            milestone=snapshot.milestone.value,
            lock_id=snapshot.lock_id,
            record_count=snapshot.record_count,
            stream_counts=snapshot.stream_counts,
            domain_counts=snapshot.domain_counts,
            sha256_digest=snapshot.sha256_digest,
            created_at=snapshot.created_at,
            created_by=snapshot.created_by,
            reason_for_snapshot=snapshot.reason_for_snapshot,
            is_locked=snapshot.is_locked,
            blinded_record_count=snapshot.blinded_record_count,
            unblinded_record_count=snapshot.unblinded_record_count,
            snapshot_manifest=snapshot.snapshot_manifest,
        )
        self.session.add(db_snapshot)
        await self.session.flush()

        for rec_id in record_ids:
            link = LakehouseSnapshotRecordLink(
                id=str(uuid.uuid4()),
                snapshot_id=snapshot.snapshot_id,
                record_id=rec_id,
                record_hash="",
                is_unblinded=False,
            )
            self.session.add(link)
        await self.session.flush()
        return snapshot

    async def get_snapshot_by_version(
        self, study_id: str, version_tag: str
    ) -> ClinicalSnapshot | None:
        stmt = select(LakehouseSnapshot).where(
            LakehouseSnapshot.study_id == study_id,
            LakehouseSnapshot.version_tag == version_tag,
        )
        db_snap = (await self.session.execute(stmt)).scalars().first()
        if not db_snap:
            return None
        return self._to_domain_snapshot(db_snap)

    async def get_snapshot_by_id(self, snapshot_id: str) -> ClinicalSnapshot | None:
        stmt = select(LakehouseSnapshot).where(LakehouseSnapshot.id == snapshot_id)
        db_snap = (await self.session.execute(stmt)).scalars().first()
        if not db_snap:
            return None
        return self._to_domain_snapshot(db_snap)

    async def list_snapshots(
        self, study_id: str, milestone: str | None = None
    ) -> list[ClinicalSnapshot]:
        stmt = select(LakehouseSnapshot).where(LakehouseSnapshot.study_id == study_id)
        if milestone:
            stmt = stmt.where(LakehouseSnapshot.milestone == milestone)
        stmt = stmt.order_by(LakehouseSnapshot.created_at.desc())
        db_snaps = (await self.session.execute(stmt)).scalars().all()
        return [self._to_domain_snapshot(s) for s in db_snaps]

    async def get_snapshot_records(
        self, snapshot_id: str, filter_criteria: SnapshotFilterCriteria | None = None
    ) -> tuple[list[CanonicalClinicalRecord], ClinicalSnapshot | None]:
        snap = await self.get_snapshot_by_id(snapshot_id)
        if not snap:
            return [], None

        stmt = (
            select(LakehouseCanonicalRecord)
            .join(
                LakehouseSnapshotRecordLink,
                LakehouseCanonicalRecord.id == LakehouseSnapshotRecordLink.record_id,
            )
            .where(LakehouseSnapshotRecordLink.snapshot_id == snapshot_id)
        )
        if filter_criteria:
            if filter_criteria.subject_ids:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.subject_id.in_(filter_criteria.subject_ids)
                )
            if filter_criteria.site_ids:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.site_id.in_(filter_criteria.site_ids)
                )
            if filter_criteria.stream_types:
                stream_vals = [s.value for s in filter_criteria.stream_types]
                stmt = stmt.where(LakehouseCanonicalRecord.stream_type.in_(stream_vals))
            if filter_criteria.domains:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.domain.in_(filter_criteria.domains)
                )

        stmt = stmt.order_by(LakehouseCanonicalRecord.observation_datetime.asc())
        db_records = (await self.session.execute(stmt)).scalars().all()

        records = [
            CanonicalClinicalRecord(
                record_id=r.id,
                study_id=r.study_id,
                site_id=r.site_id,
                subject_id=r.subject_id,
                stream_type=DataStreamType(r.stream_type),
                domain=r.domain,
                item_code=r.item_code,
                item_name=r.item_name,
                observation_datetime=r.observation_datetime,
                numeric_value=r.numeric_value,
                text_value=r.text_value,
                unit=r.unit,
                reference_low=r.reference_low,
                reference_high=r.reference_high,
                abnormal_flag=r.abnormal_flag,
                is_unblinded=r.is_unblinded,
                raw_payload=r.raw_payload,
                normalized_attributes=r.normalized_attributes,
                data_hash=r.data_hash,
                created_at=r.created_at,
            )
            for r in db_records
        ]
        return records, snap

    async def save_audit_log(
        self,
        audit_id: str,
        timestamp: Any,
        user_id: str,
        user_role: str,
        action: str,
        study_id: str,
        snapshot_id: str | None,
        details: dict[str, Any],
        reason_for_change: str | None,
        sha256_seal: str,
    ) -> None:
        log = LakehouseAuditLog(
            id=audit_id,
            timestamp=timestamp,
            user_id=user_id,
            user_role=user_role,
            action=action,
            study_id=study_id,
            snapshot_id=snapshot_id,
            details=details,
            reason_for_change=reason_for_change,
            sha256_seal=sha256_seal,
        )
        self.session.add(log)
        await self.session.flush()

    async def get_audit_trail(self, snapshot_id: str) -> list[dict[str, Any]]:
        stmt = (
            select(LakehouseAuditLog)
            .where(LakehouseAuditLog.snapshot_id == snapshot_id)
            .order_by(LakehouseAuditLog.timestamp.desc())
        )
        logs = (await self.session.execute(stmt)).scalars().all()
        return [
            {
                "id": log.id,
                "timestamp": log.timestamp.isoformat(),
                "user_id": log.user_id,
                "user_role": log.user_role,
                "action": log.action,
                "study_id": log.study_id,
                "snapshot_id": log.snapshot_id,
                "details": log.details,
                "reason_for_change": log.reason_for_change,
                "sha256_seal": log.sha256_seal,
            }
            for log in logs
        ]

    async def get_metrics(self, study_id: str) -> LakehouseMetrics:
        stmt_records = select(LakehouseCanonicalRecord).where(
            LakehouseCanonicalRecord.study_id == study_id
        )
        records = (await self.session.execute(stmt_records)).scalars().all()

        total_records = len(records)
        stream_breakdown: dict[str, int] = {}
        domain_breakdown: dict[str, int] = {}
        subjects: set[str] = set()
        sites: set[str] = set()
        blinded_count = 0
        unblinded_count = 0
        latest_ingested = None

        for r in records:
            stream_breakdown[r.stream_type] = stream_breakdown.get(r.stream_type, 0) + 1
            domain_breakdown[r.domain] = domain_breakdown.get(r.domain, 0) + 1
            subjects.add(r.subject_id)
            if r.site_id:
                sites.add(r.site_id)
            if r.is_unblinded:
                unblinded_count += 1
            else:
                blinded_count += 1
            if latest_ingested is None or r.created_at > latest_ingested:
                latest_ingested = r.created_at

        stmt_snaps = (
            select(LakehouseSnapshot)
            .where(LakehouseSnapshot.study_id == study_id)
            .order_by(LakehouseSnapshot.created_at.desc())
        )
        snaps = (await self.session.execute(stmt_snaps)).scalars().all()
        latest_snap_digest = snaps[0].sha256_digest if snaps else None

        return LakehouseMetrics(
            study_id=study_id,
            total_records=total_records,
            total_snapshots=len(snaps),
            stream_breakdown=stream_breakdown,
            domain_breakdown=domain_breakdown,
            subject_count=len(subjects),
            site_count=len(sites),
            blinded_record_count=blinded_count,
            unblinded_record_count=unblinded_count,
            latest_snapshot_digest=latest_snap_digest,
            last_ingested_at=latest_ingested,
        )

    def _to_domain_snapshot(self, db_snap: LakehouseSnapshot) -> ClinicalSnapshot:
        return ClinicalSnapshot(
            snapshot_id=db_snap.id,
            study_id=db_snap.study_id,
            version_tag=db_snap.version_tag,
            milestone=LockMilestoneType(db_snap.milestone),
            lock_id=db_snap.lock_id,
            record_count=db_snap.record_count,
            stream_counts=db_snap.stream_counts,
            domain_counts=db_snap.domain_counts,
            sha256_digest=db_snap.sha256_digest,
            created_at=db_snap.created_at,
            created_by=db_snap.created_by,
            reason_for_snapshot=db_snap.reason_for_snapshot,
            is_locked=db_snap.is_locked,
            blinded_record_count=db_snap.blinded_record_count,
            unblinded_record_count=db_snap.unblinded_record_count,
            snapshot_manifest=db_snap.snapshot_manifest,
        )
