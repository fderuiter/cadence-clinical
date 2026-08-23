"""Pydantic v2 DTO schemas for the Containerized Statistical Computing Environment (SCE) REST APIs.

Requirements: PRD-CDR-004, PRD-CDR-005, PRD-CDR-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.interop.domain.sce_models import (
    DatabaseLockAuthToken,
    ExecutionLanguage,
    ExecutionRunManifest,
    ExecutionSandboxConfig,
    PackageLockType,
    PackageLockValidationResult,
    StatisticalProgram,
)


class CreateProgramRequest(BaseModel):
    """Request payload to create or register a statistical analysis program."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Associated clinical study ID")
    name: str = Field(
        ..., min_length=1, description="Program script filename (e.g. t_14_1_demog.py)"
    )
    title: str = Field(..., min_length=1, description="Human-readable program title")
    description: str = Field(
        "", description="Detailed statistical methodology description"
    )
    language: ExecutionLanguage = Field(
        ..., description="Runtime language: PYTHON or R"
    )
    version: str = Field("1.0.0", description="Semantic version string")
    source_code: str = Field(..., min_length=1, description="Script source code")
    lockfile_content: str | None = Field(
        None, description="Raw text of uv.lock or renv.lock"
    )
    lock_type: PackageLockType = Field(
        PackageLockType.UV_LOCK, description="Lockfile format"
    )
    target_domain: str | None = Field(
        None, description="Target CDISC output domain (e.g. ADSL, TLF-14.1)"
    )
    reason_for_change: str = Field(
        "Initial program authoring", description="21 CFR Part 11 change rationale"
    )


class ProgramResponse(BaseModel):
    """Response containing statistical program definition."""

    model_config = ConfigDict(extra="forbid")

    program: StatisticalProgram = Field(..., description="Statistical program details")


class ProgramListResponse(BaseModel):
    """Response containing list of statistical programs for a study."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Target study ID")
    total: int = Field(..., ge=0, description="Total programs registered")
    programs: list[StatisticalProgram] = Field(
        default_factory=list, description="List of programs"
    )


class ValidateLockfileRequest(BaseModel):
    """Request payload to validate package lockfile integrity and GxP qualification."""

    model_config = ConfigDict(extra="forbid")

    lock_type: PackageLockType = Field(
        ..., description="Format: UV_LOCK, RENV_LOCK, or REQUIREMENTS_TXT"
    )
    content: str = Field(..., min_length=1, description="Raw text content of lockfile")
    expected_digest: str | None = Field(
        None, description="Optional SHA-256 digest to assert immutability"
    )
    strict_qualification: bool = Field(
        False, description="Enforce strict package whitelist validation"
    )


class ValidateLockfileResponse(BaseModel):
    """Response containing lockfile parsing and cryptographic verification results."""

    model_config = ConfigDict(extra="forbid")

    result: PackageLockValidationResult = Field(..., description="Verification result")


class IssueLockTokenRequest(BaseModel):
    """Request payload to issue a formal database lock authorization token for unblinded analysis."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Clinical study ID")
    lock_id: str = Field(..., min_length=1, description="Database lock reference ID")
    milestone: str = Field(
        ..., min_length=1, description="Lock milestone tier (e.g. DATABASE_LOCK)"
    )
    issued_to: str = Field(..., min_length=1, description="Authorized user ID")
    reason: str = Field(
        ..., min_length=1, description="Regulatory justification for unblinded access"
    )
    expires_in_hours: int = Field(
        24, ge=1, le=720, description="Validity duration in hours (default 24h)"
    )
    authorized_roles: list[str] | None = Field(
        None, description="Optional list of permitted unblinded roles"
    )


class LockTokenResponse(BaseModel):
    """Response containing database lock authorization token."""

    model_config = ConfigDict(extra="forbid")

    token: DatabaseLockAuthToken = Field(..., description="Signed authorization token")


class LockTokenListResponse(BaseModel):
    """Response listing active authorization tokens for a study."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study ID")
    total: int = Field(..., ge=0, description="Total active tokens")
    tokens: list[DatabaseLockAuthToken] = Field(
        default_factory=list, description="Active tokens"
    )


class ExecuteProgramRequest(BaseModel):
    """Request payload to launch an isolated, sandboxed statistical run."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., min_length=1, description="Clinical study ID")
    program_id: str = Field(
        ..., min_length=1, description="Statistical program ID to execute"
    )
    snapshot_id: str = Field(
        ..., min_length=1, description="Version-pinned data snapshot ID"
    )
    is_unblinded: bool = Field(
        False, description="Request unblinded treatment arm access"
    )
    database_lock_token_id: str | None = Field(
        None,
        description="Required formal database lock authorization token if is_unblinded is True",
    )
    sandbox_config: ExecutionSandboxConfig | None = Field(
        None, description="Optional resource bounds and sandbox settings"
    )
    reason_for_execution: str = Field(
        "Statistical analysis execution run", description="GxP execution rationale"
    )


class ExecuteProgramResponse(BaseModel):
    """Response containing executed run manifest and Part 11 electronic signature."""

    model_config = ConfigDict(extra="forbid")

    manifest: ExecutionRunManifest = Field(
        ..., description="Signed execution run manifest"
    )


class ExecutionRunListResponse(BaseModel):
    """Response listing historical execution runs for a study."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study ID")
    total: int = Field(..., ge=0, description="Total execution runs")
    runs: list[ExecutionRunManifest] = Field(
        default_factory=list, description="Historical runs"
    )


class ExecutionRunDetailResponse(BaseModel):
    """Response containing detailed execution run manifest and output artifacts."""

    model_config = ConfigDict(extra="forbid")

    run: ExecutionRunManifest = Field(..., description="Execution run manifest details")


class VerifyManifestResponse(BaseModel):
    """Response containing cryptographic manifest seal verification results."""

    model_config = ConfigDict(extra="forbid")

    result: dict[str, Any] = Field(
        ..., description="Verification summary and cryptographic checks"
    )
