"""FastAPI Router for the Multi-Source Clinical Lakehouse and Version-Pinned Data Pooling.

Endpoints:
- POST /api/v1/interop/lakehouse/ingest: Ingest heterogeneous clinical data streams with canonical normalization
- POST /api/v1/interop/lakehouse/snapshots: Create an immutable, version-pinned snapshot pool with SHA-256 seal
- GET  /api/v1/interop/lakehouse/snapshots: List version-pinned snapshots for a study
- GET  /api/v1/interop/lakehouse/snapshots/{snapshot_id}: Retrieve snapshot metadata
- POST /api/v1/interop/lakehouse/snapshots/{snapshot_id}/query: Query observations in snapshot pool with unblinded gating
- GET  /api/v1/interop/lakehouse/snapshots/{snapshot_id}/data: Convenience GET for snapshot pool observations
- POST /api/v1/interop/lakehouse/snapshots/{snapshot_id}/verify: Verify cryptographic SHA-256 integrity
- GET  /api/v1/interop/lakehouse/snapshots/{snapshot_id}/audit-trail: Retrieve 21 CFR Part 11 audit trail
- GET  /api/v1/interop/lakehouse/metrics/{study_id}: Aggregate real-time stream metrics
- GET  /api/v1/interop/lakehouse/records: Query raw harmonized lakehouse observations

Requirements: PRD-CDR-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, Query, Request, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.application.lakehouse_service import (
    LakehouseService,
    SnapshotImmutableError,
    SnapshotNotFoundError,
)
from apps.interop.domain.lakehouse_models import (
    DataStreamType,
    MultiStreamIngestPayload,
    SnapshotFilterCriteria,
)
from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.lakehouse_repository import LakehouseRepository
from apps.interop.presentation.lakehouse_dtos import (
    CreateSnapshotRequest,
    IngestResponse,
    LakehouseMetricsResponse,
    SnapshotAuditTrailResponse,
    SnapshotDataQueryRequest,
    SnapshotDataQueryResponse,
    SnapshotListResponse,
    SnapshotResponse,
    VerifySnapshotResponse,
)
from packages.database import DatabaseSessionDependency
from packages.security.trial_roles import get_normalized_request_roles

router = APIRouter(prefix="/api/v1/interop/lakehouse", tags=["CDR Lakehouse"])
get_db_session = DatabaseSessionDependency(db_manager)

UNBLINDED_ROLES = {
    "unblinded_statistician",
    "idmc",
    "emergency_unblinder",
    "pharmacist",
    "sponsor_admin",
    "admin",
}


def _extract_principal_info(request: Request) -> tuple[str, str, bool]:
    """Extract user_id, primary role, and unblinded access status from request context/headers."""
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or request.headers.get("x-user-id")
        or "system"
    )

    roles = get_normalized_request_roles(request)
    primary_role = roles[0] if roles else "data_manager"

    # Check unblinded access permission
    unblinded_header = (
        request.headers.get("X-Unblinded-Access")
        or request.headers.get("x-unblinded-access")
        or ""
    ).lower() in ("true", "1", "yes")

    has_unblinded_role = any(r in UNBLINDED_ROLES for r in roles)
    is_unblinded_access = unblinded_header and has_unblinded_role

    return user_id, primary_role, is_unblinded_access


def _get_lakehouse_service(session: AsyncSession) -> LakehouseService:
    repository = LakehouseRepository(session)
    return LakehouseService(repository)


@router.post(
    "/ingest",
    response_model=IngestResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Ingest heterogeneous clinical stream data",
)
async def ingest_stream_data(
    payload: MultiStreamIngestPayload,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> IngestResponse:
    """Ingest and normalize heterogeneous trial streams (EDC, ePRO, Labs, Wearables, FHIR, Imaging)."""
    user_id, primary_role, _ = _extract_principal_info(request)
    service = _get_lakehouse_service(session)

    canonical_records = await service.ingest_stream_payload(
        payload=payload,
        user_id=user_id,
        user_role=primary_role,
    )

    unblinded_count = sum(1 for r in canonical_records if r.is_unblinded)
    record_ids = [r.record_id for r in canonical_records]

    return IngestResponse(
        study_id=payload.study_id,
        stream_type=payload.stream_type.value,
        records_ingested=len(canonical_records),
        unblinded_records=unblinded_count,
        record_ids=record_ids,
    )


@router.post(
    "/snapshots",
    response_model=SnapshotResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Create an immutable, version-pinned clinical data snapshot",
)
async def create_snapshot(
    payload: CreateSnapshotRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> SnapshotResponse:
    """Pool lakehouse observations into an immutable, version-pinned snapshot sealed with SHA-256."""
    user_id, primary_role, _ = _extract_principal_info(request)
    service = _get_lakehouse_service(session)

    try:
        snapshot = await service.create_version_pinned_snapshot(
            study_id=payload.study_id,
            version_tag=payload.version_tag,
            milestone=payload.milestone,
            lock_id=payload.lock_id,
            reason_for_snapshot=payload.reason_for_snapshot,
            filter_criteria=payload.filter_criteria,
            user_id=user_id,
            user_role=primary_role,
        )
    except SnapshotImmutableError as e:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail=str(e),
        ) from e

    return SnapshotResponse(
        snapshot=snapshot,
        message="Version-pinned snapshot created, locked, and sealed with SHA-256.",
    )


@router.get(
    "/snapshots",
    response_model=SnapshotListResponse,
    summary="List version-pinned snapshots for a study",
)
async def list_snapshots(
    study_id: str = Query(..., description="Study identifier"),
    milestone: str | None = Query(None, description="Optional milestone filter"),
    session: AsyncSession = Depends(get_db_session),
) -> SnapshotListResponse:
    """Retrieve all sealed, version-pinned snapshots for the specified study."""
    service = _get_lakehouse_service(session)
    snapshots = await service.list_snapshots(
        study_id=study_id,
        milestone=milestone,
    )
    return SnapshotListResponse(
        study_id=study_id,
        snapshots=snapshots,
        total=len(snapshots),
    )


@router.get(
    "/snapshots/{snapshot_id}",
    response_model=SnapshotResponse,
    summary="Retrieve snapshot metadata and seal info",
)
async def get_snapshot_details(
    snapshot_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> SnapshotResponse:
    """Fetch metadata and cryptographic digest for a specific snapshot."""
    user_id, primary_role, _ = _extract_principal_info(request)
    service = _get_lakehouse_service(session)

    try:
        snapshot = await service.get_snapshot(
            snapshot_id=snapshot_id,
            user_id=user_id,
            user_role=primary_role,
        )
    except SnapshotNotFoundError as e:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=str(e),
        ) from e

    return SnapshotResponse(
        snapshot=snapshot,
        message="Snapshot metadata retrieved successfully.",
    )


@router.post(
    "/snapshots/{snapshot_id}/query",
    response_model=SnapshotDataQueryResponse,
    summary="Query pooled records inside a version-pinned snapshot",
)
async def query_snapshot_data(
    snapshot_id: str,
    payload: SnapshotDataQueryRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> SnapshotDataQueryResponse:
    """Query observations pooled inside a snapshot, enforcing role-based unblinded masking."""
    user_id, primary_role, is_unblinded_access = _extract_principal_info(request)
    service = _get_lakehouse_service(session)

    # If payload explicitly requests unblinded access, verify caller has unblinded role
    if payload.request_unblinded and not is_unblinded_access:
        roles = get_normalized_request_roles(request)
        if any(r in UNBLINDED_ROLES for r in roles):
            is_unblinded_access = True
        else:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Unblinded snapshot query access requires unblinded role permissions.",
            )

    try:
        records, masked_count, digest = await service.query_snapshot_data(
            snapshot_id=snapshot_id,
            filter_criteria=payload.filter_criteria,
            is_unblinded_access=is_unblinded_access,
            user_id=user_id,
            user_role=primary_role,
        )
    except SnapshotNotFoundError as e:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=str(e),
        ) from e

    return SnapshotDataQueryResponse(
        snapshot_id=snapshot_id,
        sha256_digest=digest,
        total_records=len(records),
        is_masked=masked_count > 0,
        masked_count=masked_count,
        records=records,
    )


@router.get(
    "/snapshots/{snapshot_id}/data",
    response_model=SnapshotDataQueryResponse,
    summary="Fetch snapshot pool data records via GET",
)
async def get_snapshot_data(
    snapshot_id: str,
    request: Request,
    subject_id: str | None = Query(None, description="Optional subject filter"),
    domain: str | None = Query(None, description="Optional domain filter"),
    stream_type: DataStreamType | None = Query(
        None, description="Optional stream filter"
    ),
    session: AsyncSession = Depends(get_db_session),
) -> SnapshotDataQueryResponse:
    """Convenience endpoint to retrieve observations in a snapshot pool."""
    user_id, primary_role, is_unblinded_access = _extract_principal_info(request)
    service = _get_lakehouse_service(session)

    filters = SnapshotFilterCriteria(
        subject_ids=[subject_id] if subject_id else None,
        domains=[domain] if domain else None,
        stream_types=[stream_type] if stream_type else None,
    )

    try:
        records, masked_count, digest = await service.query_snapshot_data(
            snapshot_id=snapshot_id,
            filter_criteria=filters,
            is_unblinded_access=is_unblinded_access,
            user_id=user_id,
            user_role=primary_role,
        )
    except SnapshotNotFoundError as e:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=str(e),
        ) from e

    return SnapshotDataQueryResponse(
        snapshot_id=snapshot_id,
        sha256_digest=digest,
        total_records=len(records),
        is_masked=masked_count > 0,
        masked_count=masked_count,
        records=records,
    )


@router.post(
    "/snapshots/{snapshot_id}/verify",
    response_model=VerifySnapshotResponse,
    summary="Verify cryptographic SHA-256 integrity of sealed snapshot",
)
async def verify_snapshot(
    snapshot_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> VerifySnapshotResponse:
    """Recompute and verify the SHA-256 seal against all pooled records in the snapshot."""
    user_id, primary_role, _ = _extract_principal_info(request)
    service = _get_lakehouse_service(session)

    try:
        result = await service.verify_snapshot_integrity(
            snapshot_id=snapshot_id,
            user_id=user_id,
            user_role=primary_role,
        )
    except SnapshotNotFoundError as e:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=str(e),
        ) from e

    return VerifySnapshotResponse(result=result)


@router.get(
    "/snapshots/{snapshot_id}/audit-trail",
    response_model=SnapshotAuditTrailResponse,
    summary="Retrieve 21 CFR Part 11 audit log for a snapshot",
)
async def get_snapshot_audit_trail(
    snapshot_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SnapshotAuditTrailResponse:
    """Retrieve all chronological 21 CFR Part 11 audit trail entries for a snapshot."""
    service = _get_lakehouse_service(session)
    entries = await service.get_snapshot_audit_trail(
        snapshot_id=snapshot_id,
    )
    return SnapshotAuditTrailResponse(
        snapshot_id=snapshot_id,
        audit_entries=entries,
        total_entries=len(entries),
    )


@router.get(
    "/metrics/{study_id}",
    response_model=LakehouseMetricsResponse,
    summary="Retrieve real-time Lakehouse stream and snapshot metrics",
)
async def get_metrics(
    study_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> LakehouseMetricsResponse:
    """Aggregate real-time data stream, subject, site, and snapshot metrics for a study."""
    service = _get_lakehouse_service(session)
    metrics = await service.get_lakehouse_metrics(
        study_id=study_id,
    )
    return LakehouseMetricsResponse(metrics=metrics)
