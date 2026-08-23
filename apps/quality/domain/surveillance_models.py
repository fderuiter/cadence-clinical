"""Domain Models for Autonomous Quality Surveillance & Sentinel Orchestration.

Requirements:
- PRD-CSM-001: Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis
- PRD-CSM-002: Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection
- PRD-CSM-003: Calendar & Timestamp Pattern Clustering
- PRD-CSM-004: Duplicate Patient Record & Anatomical Fabrication Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2213: Autonomous Cross-Domain Quality Surveillance Runner Architecture
"""

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field

from apps.quality.domain.models import RiskTier


class SiteSamplePowerStatus(StrEnum):
    SUFFICIENT_POWER = "SUFFICIENT_POWER"
    SAMPLE_GATED_INSUFFICIENT_POWER = "SAMPLE_GATED_INSUFFICIENT_POWER"


class HarvestedSurveillanceBundle(BaseModel):
    """Normalized aggregate container encapsulating multi-site demographic profiles,

    longitudinal observation series, audit trail timestamps, and medical image hashes.
    """

    study_id: str
    subjects: list[dict[str, Any]] = Field(default_factory=list)
    site_observations: dict[str, list[dict[str, float] | float]] = Field(
        default_factory=dict
    )
    audit_events: list[dict[str, Any]] = Field(default_factory=list)
    image_attachments: list[dict[str, Any]] = Field(default_factory=list)
    site_sample_counts: dict[str, int] = Field(default_factory=dict)
    metadata: dict[str, Any] = Field(default_factory=dict)


class SiteSurveillanceSummary(BaseModel):
    """Comprehensive surveillance and risk profile for an individual clinical site."""

    site_id: str
    sample_size: int = 0
    observation_count: int = 0
    power_status: SiteSamplePowerStatus = SiteSamplePowerStatus.SUFFICIENT_POWER
    is_sample_gated: bool = False
    composite_risk_score: float = 0.0
    risk_tier: RiskTier = RiskTier.LOW
    anomalies_detected: list[str] = Field(default_factory=list)
    deviations_created: list[str] = Field(default_factory=list)
    kri_alerts_triggered: list[str] = Field(default_factory=list)
    details: dict[str, Any] = Field(default_factory=dict)


class QualitySurveillanceSummary(BaseModel):
    """Executive surveillance report envelope emitted by QualitySurveillanceRunner."""

    run_id: str
    study_id: str
    run_name: str
    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))
    total_sites: int = 0
    gated_sites_count: int = 0
    analyzed_sites_count: int = 0
    total_subjects: int = 0
    total_observations: int = 0
    total_images_scanned: int = 0
    total_events_scanned: int = 0
    anomalies_count: int = 0
    deviations_dispatched_count: int = 0
    status: str = "COMPLETED"
    site_summaries: list[SiteSurveillanceSummary] = Field(default_factory=list)
    findings: list[dict[str, Any]] = Field(default_factory=list)
    deviations: list[dict[str, Any]] = Field(default_factory=list)
    forensics_run_id: str | None = None
    sentinel_run_id: str | None = None
