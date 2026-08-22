"""Pydantic domain models for Protocol Amendment Ripple-Effect Analysis and Multi-Domain Ticket Dispatch.

Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field

from apps.designer.domain.cdisc.branch_models import (
    EntityDiff,
    MigrationDirective,
)


class DomainQueue(StrEnum):
    """Target clinical and operational domain queues for ticket routing."""

    DATA_CAPTURE_ECRF = "DATA_CAPTURE_ECRF"
    SUBJECT_MANAGEMENT_RTSM = "SUBJECT_MANAGEMENT_RTSM"
    REGULATORY_COMPLIANCE = "REGULATORY_COMPLIANCE"
    SITE_OPERATIONS = "SITE_OPERATIONS"
    CLINICAL_OPERATIONS = "CLINICAL_OPERATIONS"


class DataCaptureEcrfImpact(BaseModel):
    """Manifest of impact on electronic data capture, visit schedules, and eCRF forms."""

    affected_forms_count: int = Field(
        0, description="Total count of affected eCRF forms"
    )
    added_forms: list[str] = Field(
        default_factory=list, description="Names/keys of newly added forms"
    )
    modified_forms: list[str] = Field(
        default_factory=list, description="Names/keys of modified forms"
    )
    removed_forms: list[str] = Field(
        default_factory=list, description="Names/keys of deprecated/removed forms"
    )
    affected_visits: list[str] = Field(
        default_factory=list,
        description="Names of visits with changed data capture requirements",
    )
    new_cdash_fields: list[str] = Field(
        default_factory=list, description="New CDASH domain variables required"
    )
    rule_modifications_count: int = Field(
        0, description="Number of validation rules and edit checks affected"
    )
    estimated_build_hours: float = Field(
        0.0, description="Estimated data management build hours"
    )
    action_items: list[str] = Field(
        default_factory=list, description="Step-by-step action plan for Data Management"
    )


class SubjectManagementRtsmImpact(BaseModel):
    """Manifest of impact on randomization, trial supply management (RTSM), and cohort dosing."""

    cohort_adjustments_count: int = Field(
        0, description="Number of dosing cohorts or arms adjusted"
    )
    affected_arms: list[str] = Field(
        default_factory=list, description="Names of affected study arms"
    )
    dosing_changes: list[dict[str, Any]] = Field(
        default_factory=list, description="Specific dosing regimen modifications"
    )
    visit_window_adjustments: list[dict[str, Any]] = Field(
        default_factory=list, description="Visit calculation window (+/- days) changes"
    )
    requires_kit_reallocation: bool = Field(
        False,
        description="Whether investigational product kit packaging or supply must be reallocated",
    )
    randomization_ratio_changed: bool = Field(
        False, description="Whether randomization stratifications or ratios changed"
    )
    action_items: list[str] = Field(
        default_factory=list, description="Step-by-step action plan for RTSM & Supply"
    )


class RegulatoryComplianceImpact(BaseModel):
    """Manifest of impact on safety, ethics, and regulatory compliance."""

    safety_risk_level: str = Field(
        "LOW", description="Safety risk classification: LOW, MEDIUM, HIGH, CRITICAL"
    )
    requires_reconsent: bool = Field(
        False, description="Whether subject re-consent is mandated (PRD-SUB-007)"
    )
    is_substantial_amendment: bool = Field(
        False, description="Whether amendment is substantial vs administrative"
    )
    icf_version_upgrade: str = Field(
        "", description="Target Informed Consent Form (ICF) version tag"
    )
    irb_iec_submission_type: str = Field(
        "NOTIFICATION",
        description="Ethics submission pathway: EXPEDITED, FULL_COMMITTEE, NOTIFICATION",
    )
    affected_subject_cohorts: list[str] = Field(
        default_factory=list, description="Cohorts subject to regulatory gating"
    )
    flagged_active_subjects: list[str] = Field(
        default_factory=list,
        description="IDs of in-flight active subjects requiring re-consent",
    )
    action_items: list[str] = Field(
        default_factory=list, description="Step-by-step action plan for Regulatory & QA"
    )


class ReConsentGatingPlan(BaseModel):
    """In-flight subject re-consent gating determination and cohort impact."""

    gating_mandated: bool = Field(
        False, description="Whether in-flight visit progression must be gated"
    )
    affected_cohort: str = Field(
        "ACTIVE", description="Subject population: ACTIVE, SCREENING, ENROLLED, ALL"
    )
    flagged_subject_count: int = Field(
        0,
        description="Total active subjects requiring re-consent before visit progression",
    )
    flagged_subject_ids: list[str] = Field(
        default_factory=list,
        description="Explicit list of subject IDs requiring re-consent",
    )
    justification: str = Field(
        "", description="Clinical rationale for re-consent requirement"
    )


class OperationalTicketBlueprint(BaseModel):
    """Structured actionable ticket blueprint ready for dispatch to apps/tickets."""

    domain_queue: DomainQueue = Field(
        ..., description="Target domain queue for role-based triage"
    )
    title: str = Field(..., description="Action-oriented ticket title")
    description: str = Field(..., description="Comprehensive problem & change context")
    category: str = Field(
        "CHANGE_REQUEST", description="Tickets category: CHANGE_REQUEST, CLINICAL, etc."
    )
    priority: str = Field(
        "HIGH", description="Priority level: LOW, MEDIUM, HIGH, CRITICAL"
    )
    gxp_severity: str = Field(
        "MAJOR",
        description="GxP compliance severity: NOT_APPLICABLE, MINOR, MAJOR, CRITICAL",
    )
    assignee_role: str = Field(
        ..., description="Default operational role responsible for ticket resolution"
    )
    action_plan: list[str] = Field(
        default_factory=list, description="Sequential pre-filled action items"
    )
    due_date_offset_days: int | None = Field(
        None, description="Suggested completion SLA in days from amendment creation"
    )
    context_payload: dict[str, Any] = Field(
        default_factory=dict,
        description="Structured metadata payload for context linking",
    )


class NarrativeDelta(BaseModel):
    """Semantic comparison between protocol narrative sections."""

    section_id: str = Field(..., description="Protocol section key or identifier")
    section_title: str = Field(..., description="Section title")
    change_type: str = Field(
        ..., description="Change type: ADDED, MODIFIED, REMOVED, UNCHANGED"
    )
    old_text: str | None = Field(None, description="Baseline section text")
    new_text: str | None = Field(None, description="Amended section text")
    delta_summary: str = Field(
        ..., description="Human-readable summary of textual changes"
    )
    safety_risk_impact: bool = Field(
        False, description="Whether changes alter safety profile or adverse event risks"
    )


class ProtocolImpactAssessment(BaseModel):
    """Consolidated Protocol Amendment Ripple-Effect Impact Assessment."""

    assessment_id: str = Field(..., description="Unique assessment identifier")
    study_id: str = Field(..., description="Target study identifier")
    base_version: str = Field(..., description="Baseline protocol version tag")
    amended_version: str = Field(..., description="Amended protocol version tag")
    amendment_type: str = Field(
        "minor", description="Amendment classification: major or minor"
    )
    is_substantial: bool = Field(
        False, description="Whether amendment is substantial vs administrative"
    )
    requires_reconsent: bool = Field(
        False, description="Whether mandatory subject re-consent is mandated"
    )
    patient_burden_delta: float = Field(
        0.0, description="Calculated patient burden score change"
    )
    estimated_cost_usd: float = Field(
        0.0, description="Estimated total operational cost delta in USD"
    )
    executive_summary: str = Field(
        ..., description="Executive narrative summary of the ripple effect analysis"
    )
    graph_deltas: list[EntityDiff] = Field(
        default_factory=list, description="USDM Graph entity diffs"
    )
    soa_deltas: list[EntityDiff] = Field(
        default_factory=list, description="Schedule of Activities matrix diffs"
    )
    narrative_deltas: list[NarrativeDelta] = Field(
        default_factory=list, description="Narrative section textual diffs"
    )
    data_capture_ecrf: DataCaptureEcrfImpact = Field(
        ..., description="eCRF & Visit data capture domain manifest"
    )
    subject_management_rtsm: SubjectManagementRtsmImpact = Field(
        ..., description="RTSM & Dosing domain manifest"
    )
    regulatory_compliance: RegulatoryComplianceImpact = Field(
        ..., description="Regulatory & Safety compliance domain manifest"
    )
    reconsent_gating_plan: ReConsentGatingPlan = Field(
        ..., description="Subject re-consent gating enforcement plan"
    )
    operational_tickets: list[OperationalTicketBlueprint] = Field(
        default_factory=list, description="Domain-routed operational ticket blueprints"
    )
    migration_directives: list[MigrationDirective] = Field(
        default_factory=list, description="In-flight migration directives"
    )
    created_at: str = Field(..., description="UTC ISO timestamp of assessment")


class RippleAnalysisRequest(BaseModel):
    """Payload to request ripple effect impact analysis between protocol versions."""

    study_id: str = Field(..., description="Target study ID")
    base_version_tag: str = Field("1.0.0", description="Base protocol version")
    amended_version_tag: str = Field("2.0.0", description="Amended protocol version")
    amendment_type: str = Field("minor", description="'major' or 'minor'")
    requires_reconsent: bool | None = Field(
        None, description="Explicit override for re-consent requirement"
    )
    base_payload: dict[str, Any] | None = Field(
        None, description="Explicit base protocol payload dictionary"
    )
    draft_payload: dict[str, Any] | None = Field(
        None, description="Explicit amended protocol payload dictionary"
    )
    active_subject_ids: list[str] | None = Field(
        None,
        description="Optional explicit active cohort subject IDs to evaluate for gating",
    )


class RippleAnalysisResponse(BaseModel):
    """Response containing complete ripple-effect impact assessment."""

    impact_assessment: ProtocolImpactAssessment = Field(
        ..., description="Detailed protocol impact assessment"
    )


class TicketDispatchRequest(BaseModel):
    """Payload to dispatch operational tickets for an amendment."""

    study_id: str = Field(..., description="Target study ID")
    impact_assessment: ProtocolImpactAssessment | None = Field(
        None, description="Optional pre-calculated impact assessment"
    )
    base_version_tag: str | None = Field(None, description="Base protocol version")
    amended_version_tag: str | None = Field(
        None, description="Amended protocol version"
    )
    base_payload: dict[str, Any] | None = Field(None, description="Base payload")
    draft_payload: dict[str, Any] | None = Field(None, description="Amended payload")
    selected_domain_queues: list[DomainQueue] | None = Field(
        None, description="Optional subset of domain queues to dispatch"
    )


class DispatchedTicketInfo(BaseModel):
    """Summary of a successfully dispatched operational ticket."""

    ticket_id: str = Field(..., description="Created ticket ID")
    reference: str = Field(
        ..., description="Sequential ticket reference (e.g. TKT-00101)"
    )
    domain_queue: DomainQueue = Field(..., description="Domain queue")
    title: str = Field(..., description="Ticket title")
    priority: str = Field(..., description="Priority")
    status: str = Field(..., description="Initial status (OPEN)")
    assignee_role: str = Field(..., description="Assigned role")


class TicketDispatchResponse(BaseModel):
    """Response after dispatching operational tickets to apps/tickets."""

    study_id: str = Field(..., description="Target study ID")
    assessment_id: str = Field(..., description="Associated assessment ID")
    total_dispatched: int = Field(0, description="Total tickets created")
    dispatched_tickets: list[DispatchedTicketInfo] = Field(
        default_factory=list, description="List of created ticket details"
    )
    message: str = Field(..., description="Confirmation message")
