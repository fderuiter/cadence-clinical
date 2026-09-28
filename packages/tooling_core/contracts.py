"""Pydantic contracts for developer tooling operations.

Requirements: PRD-SYS-049, ADR-2190
"""

from __future__ import annotations

from typing import Any, TypeVar

from pydantic import BaseModel, Field

T = TypeVar("T")


class CommandEnvelope[T](BaseModel):
    """Unified execution envelope for CLI JSON output and MCP tool responses."""

    success: bool = Field(..., description="Overall execution status")
    exit_code: int = Field(0, description="Standard POSIX exit code")
    summary: dict[str, Any] = Field(
        default_factory=dict, description="Concise metric dictionary"
    )
    remediation: str | None = Field(
        None, description="Actionable terminal command to remediate errors"
    )
    zoom_token: str | None = Field(
        None, description="Progressive disclosure token to inspect deep logs/traces"
    )
    data: T | None = Field(None, description="Typed result payload")


class DoctorDiagnoseRequest(BaseModel):
    auto_heal: bool = Field(
        False, description="Auto-initialize missing SQLite schemas or configs"
    )
    summary: bool = Field(
        True, description="Return concise summary instead of full logs"
    )


class DoctorDiagnoseResponse(BaseModel):
    python_version: str
    sqlite_ok: bool
    postgres_ok: bool
    neo4j_ok: bool
    issues: list[str] = Field(default_factory=list)
    remediations: list[str] = Field(default_factory=list)


class SentinelRunRequest(BaseModel):
    gate: str | None = Field(None, description="Optional specific gate to execute")
    parallel: bool = Field(True, description="Execute sentinel gates concurrently")
    summary: bool = Field(True, description="Return concise summary metrics")


class SentinelRunResponse(BaseModel):
    passed_count: int
    failed_count: int
    total_count: int
    gates: list[dict[str, Any]] = Field(default_factory=list)


class FastTestRequest(BaseModel):
    subsystem: str | None = Field(None, description="Subsystem or service path filter")
    failed_first: bool = Field(False, description="Run previously failing tests first")
    summary: bool = Field(True, description="Return concise summary metrics")


class FastTestResponse(BaseModel):
    passed: int
    failed: int
    duration_seconds: float
    command: list[str]
    failed_tests: list[str] = Field(default_factory=list)


class SeedScenarioRequest(BaseModel):
    tier: str = Field("standard", description="Seed tier (smoke, standard, full)")
    scenario: str = Field("default", description="Preset scenario identifier")
    dry_run: bool = Field(
        False, description="Preview seeding operations without writing"
    )


class SeedScenarioResponse(BaseModel):
    tier: str
    scenario: str
    entities_seeded: dict[str, int] = Field(default_factory=dict)
    duration_seconds: float


class ZoomInspectRequest(BaseModel):
    zoom_token: str = Field(
        ..., description="Progressive disclosure token emitted by a previous tool call"
    )
    offset: int = Field(0, description="Line offset for pagination")
    limit: int = Field(100, description="Max lines to return")


class ZoomInspectResponse(BaseModel):
    zoom_token: str
    entity_type: str
    total_lines: int
    lines: list[str] = Field(default_factory=list)
    has_more: bool = False


class UsdmGraphInspectRequest(BaseModel):
    protocol_id: str | None = Field(
        None, description="Optional study protocol identifier"
    )
    entity_type: str | None = Field(
        None,
        description="Optional entity label filter (Study, StudyArm, StudyEpoch, Activity, ScheduleTimeline)",
    )
    depth: int = Field(2, description="Traversal depth limit from root study node")
    summary: bool = Field(
        True, description="Return concise entity counts and structure summary"
    )


class UsdmGraphInspectResponse(BaseModel):
    protocol_id: str
    node_count: int
    relationship_count: int
    entities: dict[str, list[dict[str, Any]]] = Field(default_factory=dict)
    depth: int
    duration_seconds: float


class SdtmPipelineValidateRequest(BaseModel):
    domain: str | None = Field(
        None, description="Optional SDTM domain code filter (DM, AE, LB, VS, CM)"
    )
    strict_cdisc: bool = Field(
        True, description="Enforce strict CDISC Conformance Rules"
    )
    summary: bool = Field(True, description="Return concise conformance metric summary")


class SdtmPipelineValidateResponse(BaseModel):
    domains_evaluated: list[str] = Field(default_factory=list)
    valid_count: int = 0
    warning_count: int = 0
    error_count: int = 0
    violations: list[dict[str, Any]] = Field(default_factory=list)
    duration_seconds: float


class ClinicalEventStreamRequest(BaseModel):
    event_type: str | None = Field(
        None,
        description="Optional clinical event filter (SubjectStateChanged, FormSubmitted, LabValueIngested, PaymentTriggered)",
    )
    limit: int = Field(20, description="Maximum number of recent events to retrieve")
    replay: bool = Field(
        False, description="Replay queried events through registered mock listeners"
    )


class ClinicalEventStreamResponse(BaseModel):
    total_events: int
    returned_events: int
    events: list[dict[str, Any]] = Field(default_factory=list)
    replayed_count: int = 0
    duration_seconds: float


class SyntheticPatientGenerateRequest(BaseModel):
    protocol_id: str = Field(
        "default", description="Study protocol context for participant schedule"
    )
    scenario: str = Field(
        "standard",
        description="Clinical scenario (standard, oncology_dlt, gene_therapy_coc, cardiac_cec)",
    )
    count: int = Field(1, description="Number of synthetic subjects to synthesize")
    dry_run: bool = Field(
        False, description="Simulate generation without writing to database fixtures"
    )


class SyntheticPatientGenerateResponse(BaseModel):
    protocol_id: str
    scenario: str
    generated_count: int
    subjects: list[dict[str, Any]] = Field(default_factory=list)
    is_synthetic: bool = True
    duration_seconds: float
