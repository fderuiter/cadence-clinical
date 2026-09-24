"""FastAPI Router for Study Protocol Training Curricula, Interactive Quizzes, Part 11 Electronic Signatures, and Qualification Gating.

Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
"""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, status
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.adapters.training_models import (
    TrainingAssignment,
    TrainingCompletionCertificate,
    TrainingCurriculum,
)
from apps.knowledge.domain.training_models import (
    QualificationGateResult,
)
from apps.knowledge.services.training_service import (
    training_service_instance,
)
from packages.security.rbac import Principal, get_principal

logger = logging.getLogger("training-router")

router = APIRouter(prefix="/api/v1/training", tags=["Study Training & Gating"])


# ---------------------------------------------------------------------------
# DTOs (Pydantic v2 Strict)
# ---------------------------------------------------------------------------


class GenerateAmendmentCurriculumRequest(BaseModel):
    """Payload to generate a delta-training curriculum from protocol amendment snapshots."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Unique clinical study identifier")
    protocol_version: str = Field(
        ..., description="Amended protocol version string (e.g. '2.0')"
    )
    baseline_snapshot: dict[str, Any] = Field(
        ..., description="Baseline USDM protocol snapshot"
    )
    amended_snapshot: dict[str, Any] = Field(
        ..., description="Amended USDM protocol snapshot"
    )
    target_roles: list[str] | None = Field(
        default=None, description="Optional target roles list"
    )
    site_assignments: list[dict[str, str]] | None = Field(
        default=None,
        description="Optional list of site personnel dictionaries to auto-assign",
    )
    reason_for_change: str = Field(
        default="Generate delta-training curriculum for protocol amendment",
        description="21 CFR Part 11 justification",
    )


class TrainingModuleSummary(BaseModel):
    """DTO summarizing a training module."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    curriculum_id: str
    title: str
    module_type: str
    content_text: str
    video_url: str | None = None
    document_reference: str | None = None
    order_index: int
    min_reading_time_seconds: int
    quiz_questions: list[dict[str, Any]] = Field(default_factory=list)


class TrainingCurriculumResponse(BaseModel):
    """Response payload for a training curriculum."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    protocol_version: str
    title: str
    description: str
    target_roles: list[str]
    is_mandatory: bool
    passing_score: float
    min_reading_time_seconds: int
    is_active: bool
    modules: list[TrainingModuleSummary] = Field(default_factory=list)
    created_at: datetime
    created_by: str


class AssignCurriculumRequest(BaseModel):
    """Payload to assign a curriculum to a site user."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Clinical study ID")
    site_id: str = Field(..., description="Clinical site ID")
    user_id: str = Field(..., description="Participant user ID")
    user_name: str = Field(..., description="Participant legal printed name")
    user_role: str = Field(
        default="Principal Investigator", description="Participant clinical role"
    )
    reason_for_change: str = Field(
        default="Assign mandatory training curriculum",
        description="GxP reason for change",
    )


class TrainingAssignmentResponse(BaseModel):
    """Response payload for a user's training assignment."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    curriculum_id: str
    study_id: str
    site_id: str
    user_id: str
    user_name: str
    user_role: str
    status: str
    time_spent_seconds: int
    quiz_score: float | None = None
    attempts_count: int
    completed_at: datetime | None = None
    certificate_id: str | None = None
    created_at: datetime


class TrainingAssignmentDetailResponse(TrainingAssignmentResponse):
    """Detailed assignment payload including the parent curriculum and modules."""

    curriculum: TrainingCurriculumResponse | None = None


class RecordProgressRequest(BaseModel):
    """Payload to record time-on-page reading progress."""

    model_config = ConfigDict(extra="forbid")

    time_spent_delta_seconds: int = Field(
        ..., ge=1, description="Seconds spent in active training"
    )
    reason_for_change: str = Field(
        default="Record training time-on-page progress",
        description="GxP reason for change",
    )


class SubmitQuizRequest(BaseModel):
    """Payload for submitting comprehension quiz answers."""

    model_config = ConfigDict(extra="forbid")

    answers: dict[str, int] = Field(
        ..., description="Dictionary mapping question ID to selected 0-indexed option"
    )
    reason_for_change: str = Field(
        default="Submit comprehension quiz for qualification",
        description="GxP reason for change",
    )


class QuizEvaluationResponse(BaseModel):
    """Result of evaluating a comprehension quiz submission."""

    model_config = ConfigDict(from_attributes=True)

    score_percentage: float
    passed: bool
    passing_threshold: float
    total_questions: int
    correct_count: int
    feedback: list[dict[str, Any]] = Field(default_factory=list)


class SignTrainingRequest(BaseModel):
    """Payload for 21 CFR Part 11 digital sign-off of completed training."""

    model_config = ConfigDict(extra="forbid")

    signer_name: str = Field(..., description="Legal printed name of signer")
    signer_role: str = Field(
        ..., description="Clinical role (e.g. Principal Investigator, CRC)"
    )
    meaning: str = Field(
        default="I certify that I have reviewed, understood, and agree to execute clinical procedures in accordance with this approved protocol curriculum.",
        description="21 CFR Part 11 signature intent statement",
    )
    reason_for_change: str = Field(
        default="21 CFR Part 11 digital sign-off on training completion",
        description="GxP reason for change",
    )


class CertificateResponse(BaseModel):
    """Response payload for a verified training completion certificate."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    assignment_id: str
    curriculum_id: str
    study_id: str
    site_id: str
    user_id: str
    user_name: str
    user_role: str
    protocol_version: str
    score_achieved: float
    time_spent_seconds: int
    signature_manifestation: dict[str, Any]
    sha256_seal: str
    pdf_base64: str | None = None
    etmf_doc_id: str | None = None
    eisf_doc_id: str | None = None
    routed_to_etmf: bool
    routed_to_eisf: bool
    created_at: datetime


class VerifyGateRequest(BaseModel):
    """Request payload to verify EDC write-gating status."""

    model_config = ConfigDict(extra="forbid")

    user_id: str = Field(..., description="User ID to verify")
    study_id: str = Field(..., description="Clinical study identifier")
    protocol_version: str = Field(..., description="Active protocol version tag")
    site_id: str | None = Field(default=None, description="Optional clinical site ID")
    user_role: str | None = Field(
        default=None, description="Optional user clinical role"
    )


# ---------------------------------------------------------------------------
# API Endpoints
# ---------------------------------------------------------------------------


@router.post(
    "/curricula/generate-amendment",
    response_model=TrainingCurriculumResponse,
    status_code=status.HTTP_201_CREATED,
)
async def generate_amendment_curriculum_endpoint(
    payload: GenerateAmendmentCurriculumRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> TrainingCurriculumResponse:
    """Generate a delta-training curriculum upon protocol amendment publication.

    Requirements: PRD-TRN-001
    """
    user_id = principal.user_id or "system_author"
    curriculum = await training_service_instance.generate_amendment_curriculum(
        session=session,
        study_id=payload.study_id,
        baseline_snapshot=payload.baseline_snapshot,
        amended_snapshot=payload.amended_snapshot,
        protocol_version=payload.protocol_version,
        created_by=user_id,
        reason_for_change=payload.reason_for_change,
        target_roles=payload.target_roles,
        site_assignments=payload.site_assignments,
    )
    return TrainingCurriculumResponse.model_validate(curriculum)


@router.get("/curricula", response_model=list[TrainingCurriculumResponse])
async def list_curricula_endpoint(
    study_id: str | None = Query(None, description="Study scope filter"),
    protocol_version: str | None = Query(None, description="Protocol version filter"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[TrainingCurriculumResponse]:
    """List active training curricula."""
    stmt = (
        select(TrainingCurriculum)
        .where(TrainingCurriculum.is_active.is_(True))
        .options(selectinload(TrainingCurriculum.modules))
    )
    if study_id:
        stmt = stmt.where(TrainingCurriculum.study_id == study_id)
    if protocol_version:
        stmt = stmt.where(TrainingCurriculum.protocol_version == protocol_version)

    res = await session.execute(stmt)
    curricula = res.scalars().all()
    return [TrainingCurriculumResponse.model_validate(c) for c in curricula]


@router.get("/curricula/{curriculum_id}", response_model=TrainingCurriculumResponse)
async def get_curriculum_endpoint(
    curriculum_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> TrainingCurriculumResponse:
    """Retrieve details for a single training curriculum."""
    stmt = (
        select(TrainingCurriculum)
        .where(TrainingCurriculum.id == curriculum_id)
        .options(selectinload(TrainingCurriculum.modules))
    )
    res = await session.execute(stmt)
    curriculum = res.scalar_one_or_none()
    if not curriculum:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Curriculum '{curriculum_id}' not found.",
        )
    return TrainingCurriculumResponse.model_validate(curriculum)


@router.post(
    "/curricula/{curriculum_id}/assign",
    response_model=TrainingAssignmentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def assign_curriculum_endpoint(
    curriculum_id: str,
    payload: AssignCurriculumRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> TrainingAssignmentResponse:
    """Assign a training curriculum to a site user."""
    created_by = principal.user_id or "system_admin"
    assignment = await training_service_instance.assign_curriculum(
        session=session,
        curriculum_id=curriculum_id,
        study_id=payload.study_id,
        site_id=payload.site_id,
        user_id=payload.user_id,
        user_name=payload.user_name,
        user_role=payload.user_role,
        created_by=created_by,
        reason_for_change=payload.reason_for_change,
    )
    return TrainingAssignmentResponse.model_validate(assignment)


@router.get("/assignments", response_model=list[TrainingAssignmentResponse])
async def list_assignments_endpoint(
    study_id: str | None = Query(None),
    site_id: str | None = Query(None),
    user_id: str | None = Query(None),
    status_filter: str | None = Query(None, alias="status"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> list[TrainingAssignmentResponse]:
    """List training assignments for a user or site."""
    stmt = select(TrainingAssignment)
    if study_id:
        stmt = stmt.where(TrainingAssignment.study_id == study_id)
    if site_id:
        stmt = stmt.where(TrainingAssignment.site_id == site_id)
    if user_id:
        stmt = stmt.where(TrainingAssignment.user_id == user_id)
    if status_filter:
        stmt = stmt.where(TrainingAssignment.status == status_filter)

    res = await session.execute(stmt)
    assignments = res.scalars().all()
    return [TrainingAssignmentResponse.model_validate(a) for a in assignments]


@router.get(
    "/assignments/{assignment_id}",
    response_model=TrainingAssignmentDetailResponse,
)
async def get_assignment_detail_endpoint(
    assignment_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> TrainingAssignmentDetailResponse:
    """Retrieve full details of an assignment including curriculum modules."""
    stmt = (
        select(TrainingAssignment)
        .where(TrainingAssignment.id == assignment_id)
        .options(
            selectinload(TrainingAssignment.curriculum).selectinload(
                TrainingCurriculum.modules
            )
        )
    )
    res = await session.execute(stmt)
    asn = res.scalar_one_or_none()
    if not asn:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Assignment '{assignment_id}' not found.",
        )
    return TrainingAssignmentDetailResponse.model_validate(asn)


@router.post(
    "/assignments/{assignment_id}/progress",
    response_model=TrainingAssignmentResponse,
)
async def record_progress_endpoint(
    assignment_id: str,
    payload: RecordProgressRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> TrainingAssignmentResponse:
    """Record reading duration and time-on-page tracking."""
    user_id = principal.user_id or "demo_user"
    user_role = ",".join(principal.raw_roles) if principal.raw_roles else "site_crc"
    try:
        updated = await training_service_instance.record_progress(
            session=session,
            assignment_id=assignment_id,
            user_id=user_id,
            time_spent_delta=payload.time_spent_delta_seconds,
            user_role=user_role,
            reason_for_change=payload.reason_for_change,
        )
        return TrainingAssignmentResponse.model_validate(updated)
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(e))


@router.post(
    "/assignments/{assignment_id}/submit-quiz",
    response_model=QuizEvaluationResponse,
)
async def submit_quiz_endpoint(
    assignment_id: str,
    payload: SubmitQuizRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> QuizEvaluationResponse:
    """Submit comprehension quiz answers and evaluate score (>= 80% passing).

    Requirements: PRD-TRN-003
    """
    user_id = principal.user_id or "demo_user"
    user_role = ",".join(principal.raw_roles) if principal.raw_roles else "site_crc"
    try:
        eval_result = await training_service_instance.evaluate_and_submit_quiz(
            session=session,
            assignment_id=assignment_id,
            user_id=user_id,
            answers=payload.answers,
            user_role=user_role,
            reason_for_change=payload.reason_for_change,
        )
        return QuizEvaluationResponse.model_validate(eval_result)
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))


@router.post(
    "/assignments/{assignment_id}/sign-off",
    response_model=CertificateResponse,
    status_code=status.HTTP_201_CREATED,
)
async def sign_off_training_endpoint(
    assignment_id: str,
    payload: SignTrainingRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> CertificateResponse:
    """Capture 21 CFR Part 11 electronic signature, generate sealed PDF certificate, and push to eTMF & eISF.

    Requirements: PRD-TRN-003, PRD-TRN-004
    """
    user_id = principal.user_id or "demo_user"
    try:
        cert = await training_service_instance.sign_and_complete_training(
            session=session,
            assignment_id=assignment_id,
            user_id=user_id,
            user_name=payload.signer_name,
            user_role=payload.signer_role,
            meaning=payload.meaning,
            reason_for_change=payload.reason_for_change,
        )
        return CertificateResponse.model_validate(cert)
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))


@router.get(
    "/assignments/{assignment_id}/certificate",
    response_model=CertificateResponse,
)
async def get_certificate_endpoint(
    assignment_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> CertificateResponse:
    """Retrieve completion certificate for an assignment."""
    stmt = select(TrainingCompletionCertificate).where(
        TrainingCompletionCertificate.assignment_id == assignment_id
    )
    res = await session.execute(stmt)
    cert = res.scalar_one_or_none()
    if not cert:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Certificate for assignment '{assignment_id}' not found.",
        )
    return CertificateResponse.model_validate(cert)


@router.get("/gating-status", response_model=QualificationGateResult)
async def get_gating_status_endpoint(
    user_id: str = Query(..., description="User identifier"),
    study_id: str = Query(..., description="Study identifier"),
    protocol_version: str = Query(..., description="Active protocol version"),
    site_id: str | None = Query(None, description="Optional site identifier"),
    user_role: str | None = Query(None, description="Optional clinical role"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> QualificationGateResult:
    """Check whether a user is qualified for EDC write permissions on a protocol version.

    Requirements: PRD-TRN-002
    """
    return await training_service_instance.verify_qualification_gating(
        session=session,
        user_id=user_id,
        study_id=study_id,
        protocol_version=protocol_version,
        site_id=site_id,
        user_role=user_role,
    )


@router.post("/verify-gate", response_model=QualificationGateResult)
async def verify_gate_endpoint(
    payload: VerifyGateRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> QualificationGateResult:
    """Programmatically verify qualification gate for EDC write operations.

    Requirements: PRD-TRN-002
    """
    return await training_service_instance.verify_qualification_gating(
        session=session,
        user_id=payload.user_id,
        study_id=payload.study_id,
        protocol_version=payload.protocol_version,
        site_id=payload.site_id,
        user_role=payload.user_role,
    )
