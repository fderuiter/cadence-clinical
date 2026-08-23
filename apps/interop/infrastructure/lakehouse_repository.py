"""SQLAlchemy implementation of the LakehouseRepositoryPort.

Supports Lakehouse multi-stream storage, version-pinned snapshots,
and Containerized Statistical Computing Environment (SCE) artifacts.

Requirements: PRD-CDR-001, PRD-CDR-004, PRD-CDR-005, PRD-CDR-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import UTC
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
from apps.interop.domain.sce_models import (
    DatabaseLockAuthToken,
    ElectronicSignatureBlock,
    ExecutionLanguage,
    ExecutionRunManifest,
    ExecutionStatus,
    OutputArtifact,
    PackageLockType,
    StatisticalProgram,
)
from apps.interop.infrastructure.lakehouse_models import (
    LakehouseAuditLog,
    LakehouseCanonicalRecord,
    LakehouseDatabaseLockToken,
    LakehouseExecutionRun,
    LakehouseSnapshot,
    LakehouseSnapshotRecordLink,
    LakehouseStatisticalProgram,
)


class LakehouseRepository(LakehouseRepositoryPort):
    """Concrete repository implementing Lakehouse and SCE persistence with SQLAlchemy."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, entity_id: str) -> Any | None:
        """Get snapshot by ID (fulfilling RepositoryPort interface)."""
        return await self.get_snapshot_by_id(entity_id)

    async def save(self, entity: Any) -> Any:
        """Generic save (fulfilling RepositoryPort interface)."""
        if isinstance(entity, ClinicalSnapshot):
            return await self.save_snapshot(entity, [])
        if isinstance(entity, StatisticalProgram):
            return await self.save_program(entity)
        if isinstance(entity, ExecutionRunManifest):
            return await self.save_execution_run(entity)
        if isinstance(entity, DatabaseLockAuthToken):
            return await self.save_lock_token(entity)
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
                stream_type=rec.stream_type.value
                if hasattr(rec.stream_type, "value")
                else str(rec.stream_type),
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
                stream_vals = [
                    s.value if hasattr(s, "value") else str(s)
                    for s in filter_criteria.stream_types
                ]
                stmt = stmt.where(LakehouseCanonicalRecord.stream_type.in_(stream_vals))
            if filter_criteria.domains:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.domain.in_(filter_criteria.domains)
                )

        stmt = stmt.order_by(
            LakehouseCanonicalRecord.observation_datetime.asc(),
            LakehouseCanonicalRecord.subject_id.asc(),
        )

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
        db_snap = LakehouseSnapshot(
            id=snapshot.snapshot_id,
            study_id=snapshot.study_id,
            version_tag=snapshot.version_tag,
            milestone=snapshot.milestone.value
            if hasattr(snapshot.milestone, "value")
            else str(snapshot.milestone),
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
        self.session.add(db_snap)
        await self.session.flush()

        # Link associated records
        for rec_id in record_ids:
            link = LakehouseSnapshotRecordLink(
                snapshot_id=snapshot.snapshot_id,
                record_id=rec_id,
                record_hash="HASH_LINKED",
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
        res = (await self.session.execute(stmt)).scalar_one_or_none()
        if not res:
            return None
        return self._to_domain_snapshot(res)

    async def get_snapshot_by_id(self, snapshot_id: str) -> ClinicalSnapshot | None:
        stmt = select(LakehouseSnapshot).where(LakehouseSnapshot.id == snapshot_id)
        res = (await self.session.execute(stmt)).scalar_one_or_none()
        if not res:
            return None
        return self._to_domain_snapshot(res)

    async def list_snapshots(
        self, study_id: str, milestone: str | None = None
    ) -> list[ClinicalSnapshot]:
        stmt = select(LakehouseSnapshot).where(LakehouseSnapshot.study_id == study_id)
        if milestone:
            stmt = stmt.where(LakehouseSnapshot.milestone == milestone)
        stmt = stmt.order_by(LakehouseSnapshot.created_at.desc())

        snaps = (await self.session.execute(stmt)).scalars().all()
        return [self._to_domain_snapshot(s) for s in snaps]

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
                LakehouseSnapshotRecordLink.record_id == LakehouseCanonicalRecord.id,
            )
            .where(LakehouseSnapshotRecordLink.snapshot_id == snapshot_id)
        )

        if filter_criteria:
            if filter_criteria.subject_ids:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.subject_id.in_(filter_criteria.subject_ids)
                )
            if filter_criteria.domains:
                stmt = stmt.where(
                    LakehouseCanonicalRecord.domain.in_(filter_criteria.domains)
                )

        stmt = stmt.order_by(
            LakehouseCanonicalRecord.observation_datetime.asc(),
            LakehouseCanonicalRecord.subject_id.asc(),
        )

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

    async def get_audit_logs(
        self, study_id: str | None = None, snapshot_id: str | None = None
    ) -> list[dict[str, Any]]:
        """Retrieve audit logs filtered by study_id and/or snapshot_id."""
        stmt = select(LakehouseAuditLog).order_by(LakehouseAuditLog.timestamp.desc())
        if study_id:
            stmt = stmt.where(LakehouseAuditLog.study_id == study_id)
        if snapshot_id:
            stmt = stmt.where(LakehouseAuditLog.snapshot_id == snapshot_id)
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

    async def get_audit_trail(
        self, snapshot_id: str | None = None, study_id: str | None = None
    ) -> list[dict[str, Any]]:
        target_id = snapshot_id or study_id
        if not target_id:
            stmt = select(LakehouseAuditLog).order_by(
                LakehouseAuditLog.timestamp.desc()
            )
            logs = (await self.session.execute(stmt)).scalars().all()
        else:
            stmt = (
                select(LakehouseAuditLog)
                .where(
                    (LakehouseAuditLog.snapshot_id == target_id)
                    | (LakehouseAuditLog.study_id == target_id)
                )
                .order_by(LakehouseAuditLog.timestamp.desc())
            )
            logs = (await self.session.execute(stmt)).scalars().all()
            if not logs and snapshot_id:
                snap = await self.get_snapshot_by_id(snapshot_id)
                if snap:
                    stmt2 = (
                        select(LakehouseAuditLog)
                        .where(
                            (LakehouseAuditLog.snapshot_id == snapshot_id)
                            | (LakehouseAuditLog.study_id == snap.study_id)
                        )
                        .order_by(LakehouseAuditLog.timestamp.desc())
                    )
                    logs = (await self.session.execute(stmt2)).scalars().all()
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

    # =========================================================================
    # Statistical Computing Environment (SCE) Persistence Implementations
    # =========================================================================

    async def save_program(self, program: StatisticalProgram) -> StatisticalProgram:
        """Persist or update statistical program definition."""
        stmt = select(LakehouseStatisticalProgram).where(
            LakehouseStatisticalProgram.id == program.program_id
        )
        existing = (await self.session.execute(stmt)).scalar_one_or_none()

        if existing:
            existing.name = program.name
            existing.title = program.title
            existing.description = program.description
            existing.language = program.language.value
            existing.version = program.version
            existing.source_code = program.source_code
            existing.code_hash = program.code_hash
            existing.lockfile_content = program.lockfile_content
            existing.lock_type = program.lock_type.value
            existing.lock_digest = program.lock_digest
            existing.target_domain = program.target_domain
            existing.reason_for_change = program.reason_for_change
        else:
            db_prog = LakehouseStatisticalProgram(
                id=program.program_id,
                study_id=program.study_id,
                name=program.name,
                title=program.title,
                description=program.description,
                language=program.language.value,
                version=program.version,
                source_code=program.source_code,
                code_hash=program.code_hash,
                lockfile_content=program.lockfile_content,
                lock_type=program.lock_type.value,
                lock_digest=program.lock_digest,
                target_domain=program.target_domain,
                created_at=program.created_at,
                created_by=program.created_by,
                reason_for_change=program.reason_for_change,
            )
            self.session.add(db_prog)

        await self.session.flush()
        return program

    async def get_program_by_id(self, program_id: str) -> StatisticalProgram | None:
        """Retrieve statistical program by ID."""
        stmt = select(LakehouseStatisticalProgram).where(
            LakehouseStatisticalProgram.id == program_id
        )
        row = (await self.session.execute(stmt)).scalar_one_or_none()
        if not row:
            return None
        return self._to_domain_program(row)

    async def get_program_by_name(
        self, study_id: str, name: str
    ) -> StatisticalProgram | None:
        """Retrieve statistical program by study and filename."""
        stmt = select(LakehouseStatisticalProgram).where(
            LakehouseStatisticalProgram.study_id == study_id,
            LakehouseStatisticalProgram.name == name,
        )
        row = (await self.session.execute(stmt)).scalar_one_or_none()
        if not row:
            return None
        return self._to_domain_program(row)

    async def list_programs(self, study_id: str) -> list[StatisticalProgram]:
        """List statistical programs for a study."""
        stmt = (
            select(LakehouseStatisticalProgram)
            .where(LakehouseStatisticalProgram.study_id == study_id)
            .order_by(LakehouseStatisticalProgram.created_at.desc())
        )
        rows = (await self.session.execute(stmt)).scalars().all()
        return [self._to_domain_program(r) for r in rows]

    async def save_execution_run(
        self, manifest: ExecutionRunManifest
    ) -> ExecutionRunManifest:
        """Persist execution run record with signed manifest and output digests."""
        artifacts_json = [a.model_dump(mode="json") for a in manifest.output_artifacts]
        signed_manifest_json = manifest.model_dump(mode="json")
        sig_block_json = manifest.signature_block.model_dump(mode="json")

        db_run = LakehouseExecutionRun(
            id=manifest.run_id,
            program_id=manifest.program_id,
            study_id=manifest.study_id,
            snapshot_id=manifest.snapshot_id,
            status=manifest.status.value,
            language=manifest.language.value,
            script_code_digest=manifest.script_code_digest,
            snapshot_digest=manifest.snapshot_digest,
            lockfile_digest=manifest.lockfile_digest,
            container_image_digest=manifest.container_image_digest,
            started_at=manifest.execution_start,
            completed_at=manifest.execution_end,
            duration_ms=manifest.duration_ms,
            exit_code=manifest.exit_code,
            stdout_capture=manifest.stdout_capture,
            stderr_capture=manifest.stderr_capture,
            is_unblinded_run=manifest.is_unblinded_run,
            database_lock_token_id=manifest.database_lock_token_id,
            executed_by=manifest.executed_by,
            executed_by_role=manifest.executed_by_role,
            artifacts_manifest=artifacts_json,
            output_digest=manifest.output_artifacts_digest,
            signed_manifest=signed_manifest_json,
            manifest_sha256_seal=manifest.manifest_sha256_seal,
            signature_block=sig_block_json,
        )
        self.session.add(db_run)
        await self.session.flush()
        return manifest

    async def get_execution_run_by_id(self, run_id: str) -> ExecutionRunManifest | None:
        """Retrieve execution run manifest by run ID."""
        stmt = select(LakehouseExecutionRun).where(LakehouseExecutionRun.id == run_id)
        row = (await self.session.execute(stmt)).scalar_one_or_none()
        if not row:
            return None
        return self._to_domain_run(row)

    async def list_execution_runs(
        self,
        study_id: str,
        program_id: str | None = None,
        snapshot_id: str | None = None,
    ) -> list[ExecutionRunManifest]:
        """List execution runs for a study filtered by program or snapshot."""
        stmt = select(LakehouseExecutionRun).where(
            LakehouseExecutionRun.study_id == study_id
        )
        if program_id:
            stmt = stmt.where(LakehouseExecutionRun.program_id == program_id)
        if snapshot_id:
            stmt = stmt.where(LakehouseExecutionRun.snapshot_id == snapshot_id)

        stmt = stmt.order_by(LakehouseExecutionRun.started_at.desc())
        rows = (await self.session.execute(stmt)).scalars().all()
        return [self._to_domain_run(r) for r in rows]

    async def save_lock_token(
        self, token: DatabaseLockAuthToken
    ) -> DatabaseLockAuthToken:
        """Persist a database lock authorization token."""
        db_token = LakehouseDatabaseLockToken(
            id=token.token_id,
            study_id=token.study_id,
            lock_id=token.lock_id,
            milestone=token.milestone,
            authorized_roles=token.authorized_roles,
            issued_to=token.issued_to,
            issued_at=token.issued_at,
            expires_at=token.expires_at,
            is_revoked=token.is_revoked,
            signature_hash=token.signature_hash,
            reason=token.reason,
        )
        self.session.add(db_token)
        await self.session.flush()
        return token

    async def get_lock_token_by_id(self, token_id: str) -> DatabaseLockAuthToken | None:
        """Retrieve database lock authorization token by ID."""
        stmt = select(LakehouseDatabaseLockToken).where(
            LakehouseDatabaseLockToken.id == token_id
        )
        row = (await self.session.execute(stmt)).scalar_one_or_none()
        if not row:
            return None
        issued_at = (
            row.issued_at.replace(tzinfo=UTC)
            if row.issued_at.tzinfo is None
            else row.issued_at
        )
        expires_at = (
            row.expires_at.replace(tzinfo=UTC)
            if row.expires_at.tzinfo is None
            else row.expires_at
        )
        return DatabaseLockAuthToken(
            token_id=row.id,
            study_id=row.study_id,
            lock_id=row.lock_id,
            milestone=row.milestone,
            issued_to=row.issued_to,
            authorized_roles=row.authorized_roles,
            issued_at=issued_at,
            expires_at=expires_at,
            signature_hash=row.signature_hash,
            is_revoked=row.is_revoked,
            reason=row.reason,
        )

    async def revoke_lock_token(self, token_id: str) -> bool:
        """Revoke an active database lock authorization token."""
        stmt = select(LakehouseDatabaseLockToken).where(
            LakehouseDatabaseLockToken.id == token_id
        )
        row = (await self.session.execute(stmt)).scalar_one_or_none()
        if not row:
            return False
        row.is_revoked = True
        await self.session.flush()
        return True

    async def list_lock_tokens(self, study_id: str) -> list[DatabaseLockAuthToken]:
        """List active authorization tokens for a study."""
        stmt = (
            select(LakehouseDatabaseLockToken)
            .where(
                LakehouseDatabaseLockToken.study_id == study_id,
                LakehouseDatabaseLockToken.is_revoked.is_(False),
            )
            .order_by(LakehouseDatabaseLockToken.issued_at.desc())
        )
        rows = (await self.session.execute(stmt)).scalars().all()
        return [
            DatabaseLockAuthToken(
                token_id=r.id,
                study_id=r.study_id,
                lock_id=r.lock_id,
                milestone=r.milestone,
                issued_to=r.issued_to,
                authorized_roles=r.authorized_roles,
                issued_at=r.issued_at.replace(tzinfo=UTC)
                if r.issued_at.tzinfo is None
                else r.issued_at,
                expires_at=r.expires_at.replace(tzinfo=UTC)
                if r.expires_at.tzinfo is None
                else r.expires_at,
                signature_hash=r.signature_hash,
                is_revoked=r.is_revoked,
                reason=r.reason,
            )
            for r in rows
        ]

    # Helper mappers
    def _to_domain_snapshot(self, db_snap: LakehouseSnapshot) -> ClinicalSnapshot:
        created_at = (
            db_snap.created_at.replace(tzinfo=UTC)
            if db_snap.created_at.tzinfo is None
            else db_snap.created_at
        )
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
            created_at=created_at,
            created_by=db_snap.created_by,
            reason_for_snapshot=db_snap.reason_for_snapshot,
            is_locked=db_snap.is_locked,
            blinded_record_count=db_snap.blinded_record_count,
            unblinded_record_count=db_snap.unblinded_record_count,
            snapshot_manifest=db_snap.snapshot_manifest,
        )

    def _to_domain_program(
        self, db_prog: LakehouseStatisticalProgram
    ) -> StatisticalProgram:
        created_at = (
            db_prog.created_at.replace(tzinfo=UTC)
            if db_prog.created_at.tzinfo is None
            else db_prog.created_at
        )
        return StatisticalProgram(
            program_id=db_prog.id,
            study_id=db_prog.study_id,
            name=db_prog.name,
            title=db_prog.title,
            description=db_prog.description,
            language=ExecutionLanguage(db_prog.language),
            version=db_prog.version,
            source_code=db_prog.source_code,
            code_hash=db_prog.code_hash,
            lockfile_content=db_prog.lockfile_content,
            lock_type=PackageLockType(db_prog.lock_type),
            lock_digest=db_prog.lock_digest,
            target_domain=db_prog.target_domain,
            created_at=created_at,
            created_by=db_prog.created_by,
            reason_for_change=db_prog.reason_for_change,
        )

    def _to_domain_run(self, db_run: LakehouseExecutionRun) -> ExecutionRunManifest:
        # Reconstruct OutputArtifacts
        raw_arts = db_run.artifacts_manifest or []
        artifacts = [OutputArtifact(**a) for a in raw_arts]
        sig_block = ElectronicSignatureBlock(**db_run.signature_block)

        exec_start = (
            db_run.started_at.replace(tzinfo=UTC)
            if db_run.started_at.tzinfo is None
            else db_run.started_at
        )
        exec_end = (
            db_run.completed_at.replace(tzinfo=UTC)
            if db_run.completed_at.tzinfo is None
            else db_run.completed_at
        )

        return ExecutionRunManifest(
            manifest_id=db_run.signed_manifest.get(
                "manifest_id", f"MAN-{db_run.id[:8]}"
            ),
            run_id=db_run.id,
            study_id=db_run.study_id,
            program_id=db_run.program_id,
            program_name=db_run.signed_manifest.get("program_name", "script"),
            program_version=db_run.signed_manifest.get("program_version", "1.0.0"),
            snapshot_id=db_run.snapshot_id,
            snapshot_version_tag=db_run.signed_manifest.get(
                "snapshot_version_tag", "v1.0"
            ),
            snapshot_digest=db_run.snapshot_digest,
            language=ExecutionLanguage(db_run.language),
            script_code_digest=db_run.script_code_digest,
            lockfile_digest=db_run.lockfile_digest,
            container_image_digest=db_run.container_image_digest,
            execution_start=exec_start,
            execution_end=exec_end,
            duration_ms=db_run.duration_ms,
            exit_code=db_run.exit_code,
            status=ExecutionStatus(db_run.status),
            stdout_capture=db_run.stdout_capture,
            stderr_capture=db_run.stderr_capture,
            output_artifacts=artifacts,
            output_artifacts_digest=db_run.output_digest,
            is_unblinded_run=db_run.is_unblinded_run,
            database_lock_token_id=db_run.database_lock_token_id,
            executed_by=db_run.executed_by,
            executed_by_role=db_run.executed_by_role,
            signature_block=sig_block,
            manifest_sha256_seal=db_run.manifest_sha256_seal,
        )
