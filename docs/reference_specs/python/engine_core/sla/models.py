"""Domain Models and Schemas for the Event-Driven SLA Escalation Engine.

Provides declarative Pydantic v2 schemas for SLA policies, countdown clocks,
business calendar schedules, and multi-tier escalation policies.
"""

from __future__ import annotations

import hashlib
import json
from datetime import UTC, datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

AlertLevel = Literal["NORMAL", "WARNING", "CRITICAL", "OVERDUE"]


class BusinessHoursSchedule(BaseModel):
    """Configuration for business hours and holiday calendar calculations."""

    model_config = ConfigDict(frozen=True)

    is_24x7: bool = True
    work_start_hour: int = 9
    work_end_hour: int = 17
    work_days: list[int] = Field(
        default_factory=lambda: [0, 1, 2, 3, 4]
    )  # Monday - Friday
    holidays: list[str] = Field(default_factory=list)  # ISO dates 'YYYY-MM-DD'


class EscalationAction(BaseModel):
    """Actions triggered upon reaching an escalation threshold tier."""

    target_priority: str | None = None
    notify_roles: list[str] = Field(default_factory=list)
    webhook_channel: str | None = None
    message_template: str | None = None


class EscalationTier(BaseModel):
    """An escalation tier triggered at a specific elapsed percentage threshold."""

    tier_id: str
    name: str
    breach_threshold_pct: float = 100.0
    action: EscalationAction
    description: str | None = None


class CountdownClock(BaseModel):
    """Live computed status of a statutory or service SLA countdown clock."""

    clock_start_date: str
    clock_due_date: str
    effective_due_date: str
    total_allocated_hours: float
    total_paused_seconds: float = 0.0
    elapsed_hours: float
    elapsed_percentage: float
    remaining_hours: float
    is_overdue: bool
    alert_level: AlertLevel


class EscalationEvaluationResult(BaseModel):
    """Result of evaluating an active clock against policy escalation tiers."""

    current_alert_level: AlertLevel
    triggered_tiers: list[EscalationTier] = Field(default_factory=list)
    recommended_priority: str | None = None
    notification_roles: list[str] = Field(default_factory=list)
    audit_summary: str


class SLAPolicy(BaseModel):
    """Declarative specification for domain-wide SLA commitments and escalations."""

    policy_id: str
    name: str
    description: str | None = None
    base_hours_by_priority: dict[str, float] = Field(
        default_factory=lambda: {
            "CRITICAL": 4.0,
            "HIGH": 24.0,
            "MEDIUM": 72.0,
            "LOW": 168.0,
        }
    )
    category_multipliers: dict[str, float] = Field(default_factory=dict)
    paused_states: list[str] = Field(default_factory=list)
    warning_threshold_pct: float = 75.0
    critical_threshold_pct: float = 90.0
    schedule: BusinessHoursSchedule = Field(default_factory=BusinessHoursSchedule)
    escalation_tiers: list[EscalationTier] = Field(default_factory=list)
    version_index: int = Field(default=1, ge=1)
    sha256_checksum: str | None = None
    is_active: bool = True
    is_deleted: bool = False
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    created_by: str = "system"
    reason_for_change: str = "Initial SLA policy authoring"

    @model_validator(mode="after")
    def compute_seal(self) -> SLAPolicy:
        """Computes monotonic SHA-256 seal of the SLA policy configuration."""
        if not self.sha256_checksum:
            payload = {
                "policy_id": self.policy_id,
                "name": self.name,
                "base_hours_by_priority": self.base_hours_by_priority,
                "category_multipliers": self.category_multipliers,
                "paused_states": sorted(self.paused_states),
                "warning_threshold_pct": self.warning_threshold_pct,
                "critical_threshold_pct": self.critical_threshold_pct,
                "version_index": self.version_index,
            }
            canonical = json.dumps(payload, sort_keys=True)
            self.sha256_checksum = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
        return self
