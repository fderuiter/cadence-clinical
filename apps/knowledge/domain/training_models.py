"""Domain models, enums, and data types for Connected Study Training and EDC Write-Gating.

Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
"""

from __future__ import annotations

import hashlib
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class TrainingModuleType(StrEnum):
    """Types of training modules in a clinical curriculum."""

    PROCEDURE_DELTA = "PROCEDURE_DELTA"
    VISIT_SCHEDULE_DELTA = "VISIT_SCHEDULE_DELTA"
    SOP_REVISION = "SOP_REVISION"
    PROTOCOL_OVERVIEW = "PROTOCOL_OVERVIEW"
    SAFETY_REPORTING = "SAFETY_REPORTING"
    DOCUMENT = "DOCUMENT"
    VIDEO = "VIDEO"


class AssignmentStatus(StrEnum):
    """Lifecycle status for a user's assigned training curriculum."""

    PENDING = "PENDING"
    IN_PROGRESS = "IN_PROGRESS"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"


class TrainingAuditAction(StrEnum):
    """Immutable audit trail actions for training operations."""

    CURRICULUM_CREATED = "CURRICULUM_CREATED"
    ASSIGNMENT_CREATED = "ASSIGNMENT_CREATED"
    PROGRESS_RECORDED = "PROGRESS_RECORDED"
    QUIZ_SUBMITTED = "QUIZ_SUBMITTED"
    SIGNATURE_CAPTURED = "SIGNATURE_CAPTURED"
    TRAINING_COMPLETED = "TRAINING_COMPLETED"
    CERTIFICATE_ROUTED_ETMF = "CERTIFICATE_ROUTED_ETMF"
    CERTIFICATE_ROUTED_EISF = "CERTIFICATE_ROUTED_EISF"
    GATING_CHECK_PASSED = "GATING_CHECK_PASSED"
    GATING_CHECK_BLOCKED = "GATING_CHECK_BLOCKED"


class QuizQuestion(BaseModel):
    """Comprehension quiz question model."""

    model_config = ConfigDict(extra="forbid")

    id: str = Field(..., description="Unique question identifier")
    question: str = Field(..., description="The question text")
    options: list[str] = Field(..., min_length=2, description="Possible answers")
    correct_option_index: int = Field(..., ge=0, description="0-indexed correct option")
    rationale: str = Field(
        default="", description="Explanatory rationale for the answer"
    )


class QuizEvaluation(BaseModel):
    """Result of evaluating a comprehension quiz attempt."""

    model_config = ConfigDict(extra="forbid")

    score_percentage: float = Field(
        ..., ge=0.0, le=1.0, description="Achieved score as a fraction (0.0 - 1.0)"
    )
    passed: bool = Field(
        ..., description="Whether score meets or exceeds the required threshold"
    )
    passing_threshold: float = Field(
        default=0.80, description="Minimum passing threshold (e.g. 0.80 for 80%)"
    )
    total_questions: int = Field(..., ge=1, description="Total number of questions")
    correct_count: int = Field(..., ge=0, description="Number of correct answers")
    feedback: list[dict[str, Any]] = Field(
        default_factory=list, description="Question-by-question review feedback"
    )


class ElectronicSignatureManifestation(BaseModel):
    """21 CFR Part 11 Electronic Signature manifestation for training completion."""

    model_config = ConfigDict(extra="forbid")

    signer_id: str = Field(..., description="User ID of signer")
    signer_name: str = Field(..., description="Full printed legal name of signer")
    signer_role: str = Field(
        ..., description="Role of signer (e.g. Principal Investigator, CRC)"
    )
    meaning: str = Field(
        default="I certify that I have reviewed, understood, and agree to execute clinical procedures in accordance with this approved protocol curriculum.",
        description="21 CFR Part 11 signature meaning/intent",
    )
    timestamp: str = Field(
        default_factory=lambda: datetime.now(UTC).isoformat(),
        description="ISO UTC signing timestamp",
    )
    signature_hash: str = Field(
        ..., description="Cryptographic SHA-256 hash of the signature payload"
    )


class QualificationGateResult(BaseModel):
    """Result of an EDC write-permission qualification check."""

    model_config = ConfigDict(extra="forbid")

    is_qualified: bool = Field(
        ..., description="True if all mandatory protocol training is verified"
    )
    user_id: str = Field(..., description="Evaluated user ID")
    study_id: str = Field(..., description="Evaluated clinical study ID")
    protocol_version: str = Field(..., description="Active protocol version tag")
    site_id: str | None = Field(default=None, description="Optional clinical site ID")
    blocked_reason: str | None = Field(
        default=None, description="Explanation when write access is gated/blocked"
    )
    pending_curricula: list[dict[str, Any]] = Field(
        default_factory=list,
        description="List of incomplete mandatory training curricula",
    )


def compute_signature_hash(
    user_id: str,
    curriculum_id: str,
    score: float,
    timestamp: str,
    meaning: str,
) -> str:
    """Compute deterministic SHA-256 hash for 21 CFR Part 11 electronic signature."""
    payload = f"{user_id}|{curriculum_id}|{score:.4f}|{timestamp}|{meaning}".encode()
    return hashlib.sha256(payload).hexdigest()


def compute_certificate_seal(
    certificate_id: str,
    user_id: str,
    study_id: str,
    protocol_version: str,
    signature_hash: str,
    completed_at: str,
) -> str:
    """Compute cryptographic SHA-256 seal for training certificate."""
    payload = f"{certificate_id}|{user_id}|{study_id}|{protocol_version}|{signature_hash}|{completed_at}".encode()
    return hashlib.sha256(payload).hexdigest()
