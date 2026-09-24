"""FastAPI Router for the Validated Containerized Statistical Computing Environment (SCE).

Endpoints:
- POST /api/v1/interop/sce/programs: Create/register version-controlled statistical program (R/Python)
- GET  /api/v1/interop/sce/programs: List statistical programs for a study
- GET  /api/v1/interop/sce/programs/{program_id}: Retrieve program definition
- POST /api/v1/interop/sce/validate-lockfile: Validate package lockfile integrity (uv.lock / renv.lock)
- POST /api/v1/interop/sce/lock-tokens: Issue database lock authorization token for unblinded analysis
- GET  /api/v1/interop/sce/lock-tokens: List active authorization tokens for a study
- POST /api/v1/interop/sce/lock-tokens/{token_id}/revoke: Revoke an authorization token
- POST /api/v1/interop/sce/execute: Execute statistical program within isolated sandbox
- GET  /api/v1/interop/sce/runs: List execution runs for a study
- GET  /api/v1/interop/sce/runs/{run_id}: Retrieve execution run manifest and details
- POST /api/v1/interop/sce/runs/{run_id}/verify: Cryptographically verify execution run manifest seal
- GET  /api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}: Retrieve artifact details

Requirements: PRD-CDR-004, PRD-CDR-005, PRD-CDR-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, Request, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.adapters.database import db_manager
from apps.interop.adapters.lakehouse_repository import LakehouseRepository
from apps.interop.application.statistical_computing_service import (
    ExecutionRunNotFoundError,
    LockfileIntegrityError,
    ProgramNotFoundError,
    StatisticalComputingService,
    UnblindedAccessDeniedError,
)
from apps.interop.presentation.sce_dtos import (
    CreateProgramRequest,
    ExecuteProgramRequest,
    ExecuteProgramResponse,
    ExecutionRunDetailResponse,
    ExecutionRunListResponse,
    IssueLockTokenRequest,
    LockTokenListResponse,
    LockTokenResponse,
    ProgramListResponse,
    ProgramResponse,
    ValidateLockfileRequest,
    ValidateLockfileResponse,
    VerifyManifestResponse,
)
from packages.database import DatabaseSessionDependency
from packages.security.trial_roles import get_normalized_request_roles

router = APIRouter(
    prefix="/api/v1/interop/sce", tags=["CDR Statistical Computing Environment"]
)
get_db_session = DatabaseSessionDependency(db_manager)


def _extract_principal_info(request: Request) -> tuple[str, str]:
    """Extract user_id and primary role from request context/headers."""
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or request.headers.get("x-user-id")
        or "biostatistician_01"
    )

    roles = get_normalized_request_roles(request)
    primary_role = roles[0] if roles else "sponsor_statistician"
    return user_id, primary_role


def _get_sce_service(session: AsyncSession) -> StatisticalComputingService:
    repository = LakehouseRepository(session)
    return StatisticalComputingService(repository)


@router.post(
    "/programs",
    response_model=ProgramResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Create or register a statistical analysis program",
)
async def create_program(
    payload: CreateProgramRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> ProgramResponse:
    """Register a version-controlled R or Python statistical program with package lockfile."""
    user_id, primary_role = _extract_principal_info(request)
    service = _get_sce_service(session)

    try:
        program = await service.create_program(
            study_id=payload.study_id,
            name=payload.name,
            title=payload.title,
            description=payload.description,
            language=payload.language,
            version=payload.version,
            source_code=payload.source_code,
            lockfile_content=payload.lockfile_content,
            lock_type=payload.lock_type,
            target_domain=payload.target_domain,
            reason_for_change=payload.reason_for_change,
            user_id=user_id,
            user_role=primary_role,
        )
        return ProgramResponse(program=program)
    except LockfileIntegrityError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail=str(exc)
        ) from exc


@router.get(
    "/programs",
    response_model=ProgramListResponse,
    summary="List statistical programs for a study",
)
async def list_programs(
    study_id: str = Query(..., description="Clinical study ID"),
    session: AsyncSession = Depends(get_db_session),
) -> ProgramListResponse:
    """List all version-controlled statistical programs for a study."""
    service = _get_sce_service(session)
    programs = await service.list_programs(study_id)
    return ProgramListResponse(
        study_id=study_id, total=len(programs), programs=programs
    )


@router.get(
    "/programs/{program_id}",
    response_model=ProgramResponse,
    summary="Retrieve statistical program by ID",
)
async def get_program(
    program_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ProgramResponse:
    """Retrieve statistical program definition."""
    service = _get_sce_service(session)
    try:
        program = await service.get_program(program_id)
        return ProgramResponse(program=program)
    except ProgramNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc


@router.post(
    "/validate-lockfile",
    response_model=ValidateLockfileResponse,
    summary="Validate package lockfile integrity and GxP qualification",
)
async def validate_lockfile(
    payload: ValidateLockfileRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ValidateLockfileResponse:
    """Parse and verify package lockfile (uv.lock / renv.lock) integrity and qualification."""
    service = _get_sce_service(session)
    result = await service.validate_lockfile(
        lock_type=payload.lock_type,
        content=payload.content,
        expected_digest=payload.expected_digest,
        strict_qualification=payload.strict_qualification,
    )
    return ValidateLockfileResponse(result=result)


@router.post(
    "/lock-tokens",
    response_model=LockTokenResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Issue database lock authorization token for unblinded analysis",
)
async def issue_lock_token(
    payload: IssueLockTokenRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> LockTokenResponse:
    """Issue a cryptographically signed database lock token granting unblinded statistical analysis access."""
    user_id, primary_role = _extract_principal_info(request)
    service = _get_sce_service(session)

    token = await service.issue_database_lock_token(
        study_id=payload.study_id,
        lock_id=payload.lock_id,
        milestone=payload.milestone,
        issued_to=payload.issued_to,
        reason=payload.reason,
        expires_in_hours=payload.expires_in_hours,
        authorized_roles=payload.authorized_roles,
        user_id=user_id,
        user_role=primary_role,
    )
    return LockTokenResponse(token=token)


@router.get(
    "/lock-tokens",
    response_model=LockTokenListResponse,
    summary="List active database lock authorization tokens",
)
async def list_lock_tokens(
    study_id: str = Query(..., description="Clinical study ID"),
    session: AsyncSession = Depends(get_db_session),
) -> LockTokenListResponse:
    """List active authorization tokens for unblinded analysis."""
    service = _get_sce_service(session)
    tokens = await service.list_database_lock_tokens(study_id)
    return LockTokenListResponse(study_id=study_id, total=len(tokens), tokens=tokens)


@router.post(
    "/lock-tokens/{token_id}/revoke",
    status_code=status.HTTP_200_OK,
    summary="Revoke an active database lock authorization token",
)
async def revoke_lock_token(
    token_id: str,
    request: Request,
    reason: str = Query("Administrative revocation", description="Revocation reason"),
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
    """Revoke an active database lock token."""
    user_id, primary_role = _extract_principal_info(request)
    service = _get_sce_service(session)
    success = await service.revoke_database_lock_token(
        token_id=token_id,
        reason=reason,
        user_id=user_id,
        user_role=primary_role,
    )
    if not success:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Database lock token '{token_id}' not found.",
        )
    return {"token_id": token_id, "revoked": True, "reason": reason}


@router.post(
    "/execute",
    response_model=ExecuteProgramResponse,
    status_code=status.HTTP_200_OK,
    summary="Execute statistical program in isolated containerized sandbox",
)
async def execute_program(
    payload: ExecuteProgramRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> ExecuteProgramResponse:
    """Execute validated R or Python script against version-pinned data snapshot with signed run manifest."""
    user_id, primary_role = _extract_principal_info(request)
    service = _get_sce_service(session)

    try:
        manifest = await service.execute_statistical_program(
            study_id=payload.study_id,
            program_id=payload.program_id,
            snapshot_id=payload.snapshot_id,
            is_unblinded=payload.is_unblinded,
            database_lock_token_id=payload.database_lock_token_id,
            sandbox_config=payload.sandbox_config,
            user_id=user_id,
            user_role=primary_role,
            reason_for_execution=payload.reason_for_execution,
        )
        return ExecuteProgramResponse(manifest=manifest)
    except ProgramNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except UnblindedAccessDeniedError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Execution error: {exc}",
        ) from exc


@router.get(
    "/runs",
    response_model=ExecutionRunListResponse,
    summary="List historical execution runs for a study",
)
async def list_execution_runs(
    study_id: str = Query(..., description="Clinical study ID"),
    program_id: str | None = Query(None, description="Optional program ID filter"),
    snapshot_id: str | None = Query(None, description="Optional snapshot ID filter"),
    session: AsyncSession = Depends(get_db_session),
) -> ExecutionRunListResponse:
    """List all historical analytical execution runs."""
    service = _get_sce_service(session)
    runs = await service.list_execution_runs(
        study_id=study_id, program_id=program_id, snapshot_id=snapshot_id
    )
    return ExecutionRunListResponse(study_id=study_id, total=len(runs), runs=runs)


@router.get(
    "/runs/{run_id}",
    response_model=ExecutionRunDetailResponse,
    summary="Retrieve execution run manifest and details",
)
async def get_execution_run(
    run_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ExecutionRunDetailResponse:
    """Retrieve details and signed manifest of an execution run."""
    service = _get_sce_service(session)
    try:
        run = await service.get_execution_run(run_id)
        return ExecutionRunDetailResponse(run=run)
    except ExecutionRunNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc


@router.post(
    "/runs/{run_id}/verify",
    response_model=VerifyManifestResponse,
    summary="Cryptographically verify execution run manifest seal",
)
async def verify_run_manifest(
    run_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> VerifyManifestResponse:
    """Assert bit-for-bit SHA-256 seal integrity and 21 CFR Part 11 electronic signature."""
    user_id, primary_role = _extract_principal_info(request)
    service = _get_sce_service(session)

    try:
        result = await service.verify_run_manifest(
            run_id=run_id, user_id=user_id, user_role=primary_role
        )
        return VerifyManifestResponse(result=result)
    except ExecutionRunNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc


@router.get(
    "/runs/{run_id}/artifacts/{artifact_id}",
    summary="Retrieve generated artifact details and preview",
)
async def get_run_artifact(
    run_id: str,
    artifact_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
    """Retrieve output artifact metadata and content preview."""
    service = _get_sce_service(session)
    try:
        run = await service.get_execution_run(run_id)
        artifact = next(
            (a for a in run.output_artifacts if a.artifact_id == artifact_id), None
        )
        if not artifact:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=f"Artifact '{artifact_id}' not found in run '{run_id}'.",
            )
        return artifact.model_dump(mode="json")
    except ExecutionRunNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
