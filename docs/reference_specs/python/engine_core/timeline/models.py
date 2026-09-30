"""Models and schemas for the Schedule of Activities (SoA) Vector Timeline Engine.

Governs relative visit scheduling, asymmetric window tolerances, cycle template expansions,
predecessor DAG chaining, and automated deviation classifications under 21 CFR Part 11.
"""

import hashlib
import json
from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, Field

AnchorEventType = Literal[
    "SCREENING",
    "CONSENT",
    "RANDOMIZATION",
    "FIRST_DOSE",
    "CYCLE_START",
    "PREVIOUS_VISIT",
    "STUDY_START",
]

VisitDeviationClassification = Literal[
    "WITHIN_WINDOW",
    "EARLY_VISIT",
    "LATE_VISIT",
    "OUT_OF_WINDOW_MAJOR",
    "MISSED_VISIT",
]

DeviationSeverity = Literal["NONE", "MINOR", "MAJOR"]


class VisitWindowEnvelope(BaseModel):
    """Temporal boundary envelope defining permissible calendar day deviations around target."""

    window_before_days: int = Field(
        default=0,
        ge=0,
        description="Permissible days prior to target date (e.g. -3 days)",
    )
    window_after_days: int = Field(
        default=0,
        ge=0,
        description="Permissible days after target date (e.g. +5 days)",
    )
    major_deviation_tolerance_days: int = Field(
        default=7,
        ge=0,
        description="Days beyond window threshold before classifying deviation as MAJOR protocol violation",
    )


class EncounterScheduleItem(BaseModel):
    """Specification for a single clinical encounter or visit milestone in the study timeline."""

    encounter_id: str = Field(..., description="Unique clinical encounter identifier")
    name: str = Field(
        ..., description="Human-readable encounter title (e.g. 'Cycle 1 Day 15')"
    )
    anchor_event: AnchorEventType = Field(
        default="FIRST_DOSE",
        description="Reference baseline event for calculating relative target day",
    )
    predecessor_encounter_id: str | None = Field(
        default=None,
        description="Optional predecessor visit ID when chaining dynamically to prior actual date",
    )
    target_day_offset: int = Field(
        default=0,
        description="Target offset in calendar days from the resolved anchor event date",
    )
    window: VisitWindowEnvelope = Field(default_factory=VisitWindowEnvelope)
    is_mandatory: bool = Field(
        default=True,
        description="Flag indicating if missing this visit constitutes a protocol violation",
    )
    epoch_id: str | None = Field(
        default=None, description="Optional CDISC USDM study epoch ID"
    )
    cycle_number: int | None = Field(
        default=None, description="Optional treatment cycle index"
    )


class CycleTemplateDefinition(BaseModel):
    """Specification for repeating oncology/multi-cycle treatment regimens."""

    template_id: str = Field(
        ..., description="Cycle template identifier (e.g. 'Q3W_REGIMEN')"
    )
    name: str = Field(..., description="Human-readable cycle template name")
    cycle_count: int = Field(
        ..., ge=1, description="Number of treatment cycles to generate"
    )
    cycle_length_days: int = Field(
        ...,
        ge=1,
        description="Duration in calendar days of each cycle (e.g. 21 for Q3W)",
    )
    encounters_per_cycle: list[EncounterScheduleItem] = Field(
        default_factory=list,
        description="Template encounters defined relative to each cycle's start day",
    )


class TimelineScheduleDefinition(BaseModel):
    """Immutable, version-pinned schedule specification for a clinical trial protocol."""

    study_id: str = Field(..., description="Protocol or study identifier")
    version_index: int = Field(default=1, ge=1)
    is_active: bool = Field(default=True)
    cycle_templates: list[CycleTemplateDefinition] = Field(default_factory=list)
    encounters: list[EncounterScheduleItem] = Field(default_factory=list)
    reason_for_change: str | None = Field(default=None)
    sha256_checksum: str | None = Field(
        default=None,
        description="Deterministic cryptographic checksum of the schedule definition",
    )

    def model_post_init(self, __context: Any) -> None:
        """Compute SHA-256 seal upon definition instantiation."""
        if not self.sha256_checksum:
            payload = {
                "study_id": self.study_id,
                "version_index": self.version_index,
                "cycle_templates": [t.model_dump() for t in self.cycle_templates],
                "encounters": [e.model_dump() for e in self.encounters],
            }
            raw = json.dumps(payload, sort_keys=True, default=str)
            object.__setattr__(
                self, "sha256_checksum", hashlib.sha256(raw.encode("utf-8")).hexdigest()
            )


class CalculatedVisitSchedule(BaseModel):
    """Evaluated calendar milestone projection for a subject's clinical visit."""

    encounter_id: str
    name: str
    cycle_number: int | None = None
    target_date: str = Field(..., description="ISO 8601 YYYY-MM-DD target visit date")
    window_start_date: str = Field(
        ..., description="ISO 8601 YYYY-MM-DD window opening date"
    )
    window_end_date: str = Field(
        ..., description="ISO 8601 YYYY-MM-DD window closing date"
    )
    anchor_used: AnchorEventType | str
    anchor_date: str
    is_mandatory: bool = True
    epoch_id: str | None = None


class VisitDeviationAssessment(BaseModel):
    """Audit-trailed evaluation of an actual visit completion against scheduled window."""

    encounter_id: str
    actual_date: str
    target_date: str
    window_start_date: str
    window_end_date: str
    delta_days_from_target: int
    classification: VisitDeviationClassification
    is_deviation: bool
    deviation_severity: DeviationSeverity
    rationale: str
    eval_timestamp: str = Field(default_factory=lambda: datetime.now().isoformat())
