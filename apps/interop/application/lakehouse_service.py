"""Application service orchestrating the Multi-Source Clinical Lakehouse and Version-Pinned Snapshots.

Coordinates:
- Heterogeneous data stream ingestion with canonical normalization
- Deterministic SHA-256 cryptographic snapshot sealing and milestone linkage
- Snapshot immutability protection
- Bit-for-bit integrity verification
- Strict role-based unblinded data partitioning and field masking
- 21 CFR Part 11 immutable audit logging

Pure application layer adhering to Hexagonal Architecture (no direct database or framework dependencies).

Requirements: PRD-CDR-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import hashlib
import json
import uuid
from datetime import UTC, datetime
from typing import Any

from apps.interop.domain.lakehouse_models import (
    CanonicalClinicalRecord,
    ClinicalSnapshot,
    DataStreamType,
    LakehouseAction,
    LakehouseMetrics,
    LockMilestoneType,
    MultiStreamIngestPayload,
    SnapshotFilterCriteria,
    SnapshotVerificationResult,
)
from apps.interop.domain.lakehouse_normalizer import (
    compute_snapshot_pool_digest,
    normalize_central_lab,
    normalize_edc_ecrf,
    normalize_ehr_fhir,
    normalize_epro_ecoa,
    normalize_iot_wearable,
    normalize_local_lab,
    normalize_medical_imaging,
    partition_and_filter_records,
)
from apps.interop.domain.ports import LakehouseRepositoryPort


class SnapshotImmutableError(Exception):
    """Raised when attempting to modify, overwrite, or delete an immutable sealed snapshot."""

    pass


class SnapshotNotFoundError(Exception):
    """Raised when a requested snapshot cannot be found."""

    pass


class LakehouseService:
    """Core application service orchestrating lakehouse ingestion, snapshot pooling, and audit logging."""

    def __init__(self, repository: LakehouseRepositoryPort) -> None:
        self.repository = repository

    async def write_audit_log(
        self,
        user_id: str,
        user_role: str,
        action: LakehouseAction,
        study_id: str,
        snapshot_id: str | None,
        details: dict[str, Any],
        reason_for_change: str | None = None,
    ) -> None:
        """Write an immutable entry to the 21 CFR Part 11 Lakehouse audit trail."""
        timestamp = datetime.now(UTC)
        seal_payload = {
            "timestamp": timestamp.isoformat(),
            "user_id": user_id,
            "user_role": user_role,
            "action": action.value,
            "study_id": study_id,
            "snapshot_id": snapshot_id,
            "details": details,
        }
        canonical_str = json.dumps(seal_payload, sort_keys=True, separators=(",", ":"))
        sha256_seal = hashlib.sha256(canonical_str.encode("utf-8")).hexdigest()

        await self.repository.save_audit_log(
            audit_id=str(uuid.uuid4()),
            timestamp=timestamp,
            user_id=user_id,
            user_role=user_role,
            action=action.value,
            study_id=study_id,
            snapshot_id=snapshot_id,
            details=details,
            reason_for_change=reason_for_change,
            sha256_seal=sha256_seal,
        )

    async def ingest_stream_payload(
        self,
        payload: MultiStreamIngestPayload,
        user_id: str,
        user_role: str,
    ) -> list[CanonicalClinicalRecord]:
        """Normalize and ingest a heterogeneous multi-stream payload into the Lakehouse repository."""
        canonical_records: list[CanonicalClinicalRecord] = []
        study_id = payload.study_id

        # 1. EDC eCRF stream
        if payload.stream_type == DataStreamType.EDC_ECRF and payload.edc_items:
            for item in payload.edc_items:
                canonical_records.append(normalize_edc_ecrf(study_id, item))

        # 2. ePRO / eCOA stream
        elif payload.stream_type == DataStreamType.EPRO_ECOA and payload.epro_items:
            for item in payload.epro_items:
                canonical_records.extend(normalize_epro_ecoa(study_id, item))

        # 3. Central Diagnostic Lab stream
        elif (
            payload.stream_type == DataStreamType.CENTRAL_LAB
            and payload.central_lab_items
        ):
            for item in payload.central_lab_items:
                canonical_records.append(normalize_central_lab(study_id, item))

        # 4. Local Lab stream
        elif (
            payload.stream_type == DataStreamType.LOCAL_LAB and payload.local_lab_items
        ):
            for item in payload.local_lab_items:
                canonical_records.append(normalize_local_lab(study_id, item))

        # 5. Connected Sensors & Wearables stream
        elif (
            payload.stream_type == DataStreamType.IOT_WEARABLE
            and payload.wearable_items
        ):
            for item in payload.wearable_items:
                canonical_records.extend(normalize_iot_wearable(study_id, item))

        # 6. EHR HL7 FHIR stream
        elif payload.stream_type == DataStreamType.EHR_FHIR and payload.fhir_items:
            for item in payload.fhir_items:
                canonical_records.append(normalize_ehr_fhir(study_id, item))

        # 7. Blinded Medical Imaging Adjudication stream
        elif (
            payload.stream_type == DataStreamType.MEDICAL_IMAGING
            and payload.imaging_items
        ):
            for item in payload.imaging_items:
                canonical_records.extend(normalize_medical_imaging(study_id, item))

        # Persist to repository
        await self.repository.save_canonical_records(canonical_records)

        # Audit log ingestion
        await self.write_audit_log(
            user_id=user_id,
            user_role=user_role,
            action=LakehouseAction.INGEST
            if len(canonical_records) == 1
            else LakehouseAction.BATCH_INGEST,
            study_id=study_id,
            snapshot_id=None,
            details={
                "stream_type": payload.stream_type.value,
                "ingested_count": len(canonical_records),
                "unblinded_count": sum(1 for r in canonical_records if r.is_unblinded),
            },
            reason_for_change=payload.reason_for_ingest,
        )

        return canonical_records

    async def create_version_pinned_snapshot(
        self,
        study_id: str,
        version_tag: str,
        milestone: LockMilestoneType,
        lock_id: str | None,
        reason_for_snapshot: str,
        filter_criteria: SnapshotFilterCriteria | None,
        user_id: str,
        user_role: str,
    ) -> ClinicalSnapshot:
        """Pool harmonized clinical records and create an immutable, version-pinned snapshot sealed with SHA-256."""
        # 1. Check for duplicate version tag
        existing = await self.repository.get_snapshot_by_version(study_id, version_tag)
        if existing:
            raise SnapshotImmutableError(
                f"Snapshot with version tag '{version_tag}' already exists for study '{study_id}' and is immutable."
            )

        # 2. Query canonical records matching study and optional filters
        domain_records = await self.repository.get_records(study_id, filter_criteria)

        # 3. Calculate statistics
        stream_counts: dict[str, int] = {}
        domain_counts: dict[str, int] = {}
        blinded_count = 0
        unblinded_count = 0

        for r in domain_records:
            stream_counts[r.stream_type.value] = (
                stream_counts.get(r.stream_type.value, 0) + 1
            )
            domain_counts[r.domain] = domain_counts.get(r.domain, 0) + 1
            if r.is_unblinded:
                unblinded_count += 1
            else:
                blinded_count += 1

        # 4. Compute deterministic SHA-256 digest sealing snapshot pool
        sha256_digest = compute_snapshot_pool_digest(
            study_id=study_id,
            version_tag=version_tag,
            milestone=milestone.value,
            records=domain_records,
        )

        snapshot_id = f"SNAP-{uuid.uuid4().hex[:12].upper()}"
        created_at = datetime.now(UTC)

        manifest = {
            "snapshot_id": snapshot_id,
            "study_id": study_id,
            "version_tag": version_tag,
            "milestone": milestone.value,
            "lock_id": lock_id,
            "record_count": len(domain_records),
            "sha256_digest": sha256_digest,
            "created_by": user_id,
            "created_at": created_at.isoformat(),
            "filter_criteria": filter_criteria.model_dump(mode="json")
            if filter_criteria
            else None,
        }

        snapshot_domain = ClinicalSnapshot(
            snapshot_id=snapshot_id,
            study_id=study_id,
            version_tag=version_tag,
            milestone=milestone,
            lock_id=lock_id,
            record_count=len(domain_records),
            stream_counts=stream_counts,
            domain_counts=domain_counts,
            sha256_digest=sha256_digest,
            created_at=created_at,
            created_by=user_id,
            reason_for_snapshot=reason_for_snapshot,
            is_locked=True,
            blinded_record_count=blinded_count,
            unblinded_record_count=unblinded_count,
            snapshot_manifest=manifest,
        )

        # 5. Persist Snapshot and M:N Record Links
        record_ids = [r.record_id for r in domain_records]
        await self.repository.save_snapshot(snapshot_domain, record_ids)

        # 6. Audit log snapshot creation
        await self.write_audit_log(
            user_id=user_id,
            user_role=user_role,
            action=LakehouseAction.SNAPSHOT_CREATE,
            study_id=study_id,
            snapshot_id=snapshot_id,
            details={
                "version_tag": version_tag,
                "milestone": milestone.value,
                "lock_id": lock_id,
                "record_count": len(domain_records),
                "sha256_digest": sha256_digest,
                "blinded_record_count": blinded_count,
                "unblinded_record_count": unblinded_count,
            },
            reason_for_change=reason_for_snapshot,
        )

        return snapshot_domain

    async def get_snapshot(
        self,
        snapshot_id: str,
        user_id: str,
        user_role: str,
    ) -> ClinicalSnapshot:
        """Retrieve snapshot metadata and log query audit."""
        snapshot = await self.repository.get_snapshot_by_id(snapshot_id)
        if not snapshot:
            raise SnapshotNotFoundError(f"Snapshot '{snapshot_id}' not found.")

        # Log query
        await self.write_audit_log(
            user_id=user_id,
            user_role=user_role,
            action=LakehouseAction.SNAPSHOT_QUERY,
            study_id=snapshot.study_id,
            snapshot_id=snapshot_id,
            details={"action": "get_snapshot_metadata"},
        )

        return snapshot

    async def list_snapshots(
        self,
        study_id: str,
        milestone: str | None = None,
    ) -> list[ClinicalSnapshot]:
        """List all version-pinned snapshots for a given study."""
        return await self.repository.list_snapshots(study_id, milestone)

    async def query_snapshot_data(
        self,
        snapshot_id: str,
        filter_criteria: SnapshotFilterCriteria | None,
        is_unblinded_access: bool,
        user_id: str,
        user_role: str,
    ) -> tuple[list[CanonicalClinicalRecord], int, str]:
        """Query observations pooled inside a version-pinned snapshot with unblinded role gating and masking."""
        domain_records, snapshot = await self.repository.get_snapshot_records(
            snapshot_id, filter_criteria
        )
        if not snapshot:
            raise SnapshotNotFoundError(f"Snapshot '{snapshot_id}' not found.")

        # Enforce blinding partition and field-level masking
        filtered_records, masked_count = partition_and_filter_records(
            domain_records, is_unblinded_access=is_unblinded_access
        )

        # Audit query
        audit_action = (
            LakehouseAction.UNBLINDED_ACCESS
            if is_unblinded_access
            else LakehouseAction.SNAPSHOT_QUERY
        )
        await self.write_audit_log(
            user_id=user_id,
            user_role=user_role,
            action=audit_action,
            study_id=snapshot.study_id,
            snapshot_id=snapshot_id,
            details={
                "requested_unblinded": is_unblinded_access,
                "total_returned": len(filtered_records),
                "masked_count": masked_count,
            },
        )

        return filtered_records, masked_count, snapshot.sha256_digest

    async def verify_snapshot_integrity(
        self,
        snapshot_id: str,
        user_id: str,
        user_role: str,
    ) -> SnapshotVerificationResult:
        """Verify the cryptographic SHA-256 digest integrity of a historical version-pinned snapshot."""
        domain_records, snapshot = await self.repository.get_snapshot_records(
            snapshot_id, None
        )
        if not snapshot:
            raise SnapshotNotFoundError(f"Snapshot '{snapshot_id}' not found.")

        computed_digest = compute_snapshot_pool_digest(
            study_id=snapshot.study_id,
            version_tag=snapshot.version_tag,
            milestone=snapshot.milestone.value,
            records=domain_records,
        )

        is_valid = computed_digest == snapshot.sha256_digest
        msg = (
            "Cryptographic SHA-256 seal verified successfully. Dataset pool is untampered and 100% bit-for-bit reproducible."
            if is_valid
            else "CRITICAL INTEGRITY FAILURE: Computed SHA-256 digest does NOT match the sealed snapshot digest."
        )

        # Audit verification
        await self.write_audit_log(
            user_id=user_id,
            user_role=user_role,
            action=LakehouseAction.SNAPSHOT_VERIFY,
            study_id=snapshot.study_id,
            snapshot_id=snapshot_id,
            details={
                "is_valid": is_valid,
                "stored_digest": snapshot.sha256_digest,
                "computed_digest": computed_digest,
                "record_count": len(domain_records),
            },
        )

        return SnapshotVerificationResult(
            snapshot_id=snapshot_id,
            version_tag=snapshot.version_tag,
            stored_digest=snapshot.sha256_digest,
            computed_digest=computed_digest,
            is_valid=is_valid,
            record_count=len(domain_records),
            verified_at=datetime.now(UTC),
            verified_by=user_id,
            verification_message=msg,
        )

    async def get_lakehouse_metrics(
        self,
        study_id: str,
    ) -> LakehouseMetrics:
        """Aggregate real-time metrics across all data streams and snapshot pools for a study."""
        return await self.repository.get_metrics(study_id)

    async def get_snapshot_audit_trail(
        self,
        snapshot_id: str,
    ) -> list[dict[str, Any]]:
        """Retrieve all 21 CFR Part 11 audit log entries associated with a specific snapshot."""
        return await self.repository.get_audit_trail(snapshot_id)
