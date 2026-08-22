"""
Pydantic DTO schemas for Tickets presentation layer.
"""

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

from apps.tickets.domain.models import (
    CommentVisibility,
    GxPSeverity,
    ResolutionCode,
    RootCauseCategory,
    TicketCategory,
    TicketPriority,
    TicketStatus,
)


class TicketCreate(BaseModel):
    """Pydantic schema for creating a support or clinical operations ticket."""

    title: str = Field(..., description="Title of the support ticket")
    description: str = Field(..., description="Detailed description of the issue")
    category: TicketCategory = Field(
        TicketCategory.OTHER, description="Category of the ticket"
    )
    priority: TicketPriority = Field(
        TicketPriority.LOW, description="Priority level of the ticket"
    )
    gxp_severity: GxPSeverity = Field(
        GxPSeverity.NOT_APPLICABLE, description="GxP compliance severity impact"
    )
    reporter: str | None = Field(None, description="Reporter of the ticket")
    assignee_user: str | None = Field(None, description="Assigned user")
    assignee_role: str | None = Field(None, description="Assigned role")
    org_id: str | None = Field(None, description="Scope organization ID")
    site_id: str | None = Field(None, description="Scope site ID")
    study_id: str | None = Field(None, description="Scope study ID")
    related_entity_type: str | None = Field(None, description="Related entity type")
    related_entity_id: str | None = Field(None, description="Related entity ID")
    context_payload: str | None = Field(
        None, description="Serialized contextual metadata JSON"
    )
    due_date: datetime | None = Field(None, description="Optional due date")
    custom_sla_hours: int | None = Field(
        None, description="Optional study-specific SLA target override in hours"
    )
    workflow_type: str | None = Field(None, description="Workflow type")
    action_type: str | None = Field(None, description="Action type")
    signature_action: str | None = Field(None, description="Signature action")


class TicketUpdate(BaseModel):
    """Pydantic schema for updating an existing support ticket."""

    title: str | None = Field(None, description="Updated title")
    description: str | None = Field(None, description="Updated description")
    category: TicketCategory | None = Field(None, description="Updated category")
    priority: TicketPriority | None = Field(None, description="Updated priority")
    gxp_severity: GxPSeverity | None = Field(None, description="Updated GxP severity")
    status: TicketStatus | None = Field(None, description="Updated status")
    assignee_user: str | None = Field(None, description="Updated assigned user")
    assignee_role: str | None = Field(None, description="Updated assigned role")
    org_id: str | None = Field(None, description="Updated organization scope")
    site_id: str | None = Field(None, description="Updated site scope")
    study_id: str | None = Field(None, description="Updated study scope")
    related_entity_type: str | None = Field(
        None, description="Updated related entity type"
    )
    related_entity_id: str | None = Field(None, description="Updated related entity ID")
    context_payload: str | None = Field(None, description="Updated contextual payload")
    due_date: datetime | None = Field(None, description="Updated due date")
    is_deleted: bool | None = Field(None, description="Soft delete state")
    version_index: int | None = Field(
        None, description="Expected version index for optimistic locking"
    )


class TicketAssignPayload(BaseModel):
    """Pydantic schema for assigning a support ticket."""

    assignee_user: str | None = Field(None, description="Username of the assignee")
    assignee_role: str | None = Field(None, description="Role-based routing target")
    version_index: int = Field(
        ..., description="Expected version index for optimistic locking"
    )


class TicketTransitionPayload(BaseModel):
    """Pydantic schema for transitioning support ticket lifecycle status."""

    status: TicketStatus = Field(
        ..., description="Target status for the lifecycle transition"
    )
    version_index: int = Field(
        ..., description="Expected version index for optimistic locking"
    )
    root_cause_category: RootCauseCategory | None = Field(
        None,
        description="RCA 5-Whys classification (mandatory for Major/Critical closure)",
    )
    root_cause_summary: str | None = Field(
        None, description="Detailed root cause explanation"
    )
    resolution_code: ResolutionCode | None = Field(
        None, description="Formal resolution outcome code"
    )
    signature_token: str | None = Field(
        None, description="21 CFR Part 11 signature token"
    )
    signature_meaning: str | None = Field(
        None, description="Electronic signature justification meaning"
    )


class TicketSignaturePayload(BaseModel):
    """Pydantic schema for capturing a 21 CFR Part 11 Electronic Signature."""

    signature_token: str = Field(..., description="Cryptographic signature token")
    meaning: str = Field(..., description="Signature meaning statement")
    version_index: int = Field(
        ..., description="Expected version index for optimistic locking"
    )


class CrossAppEventCreate(BaseModel):
    """Pydantic schema for cross-app automated ticket ingestion from sibling microservices."""

    event_type: str = Field(
        ..., description="Event type, e.g. PROTOCOL_DEVIATION, DATA_QUERY, SAFETY_ALERT"
    )
    title: str = Field(..., description="Ticket title")
    description: str = Field(..., description="Ticket description")
    category: TicketCategory = Field(
        TicketCategory.PROTOCOL_DEVIATION, description="Ticket category"
    )
    priority: TicketPriority = Field(TicketPriority.HIGH, description="Priority level")
    gxp_severity: GxPSeverity = Field(
        GxPSeverity.MAJOR, description="GxP Severity rating"
    )
    source_service: str = Field(
        ..., description="Source microservice, e.g. execution, safety, ctms, quality"
    )
    study_id: str | None = Field(None, description="Study scope ID")
    site_id: str | None = Field(None, description="Site scope ID")
    related_entity_type: str = Field(
        ..., description="Linked entity type (e.g. Subject, Form, AE, CAPA)"
    )
    related_entity_id: str = Field(..., description="Linked entity primary key")
    context_payload: dict[str, Any] | None = Field(
        None, description="Structured contextual JSON payload"
    )


class TicketResponse(BaseModel):
    """Pydantic schema for returning support ticket details."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    reference: str
    title: str
    description: str
    category: str
    priority: str
    status: str
    gxp_severity: str = "NOT_APPLICABLE"
    root_cause_category: str | None = None
    root_cause_summary: str | None = None
    resolution_code: str | None = None
    reporter: str
    assignee_user: str | None = None
    assignee_role: str | None = None
    org_id: str | None = None
    site_id: str | None = None
    study_id: str | None = None
    related_entity_type: str | None = None
    related_entity_id: str | None = None
    context_payload: str | None = None
    due_date: str | None = None
    sla_target_at: str | None = None
    sla_paused_at: str | None = None
    sla_total_paused_seconds: int = 0
    sla_breached: bool = False
    sla_amber_warned: bool = False
    signature_token: str | None = None
    signature_meaning: str | None = None
    signature_timestamp: str | None = None
    signature_user: str | None = None
    is_deleted: bool
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CommentCreate(BaseModel):
    """Pydantic schema for creating a ticket comment."""

    body: str = Field(..., description="The comment body text")
    visibility: CommentVisibility = Field(
        CommentVisibility.PUBLIC, description="Visibility boundary for comment"
    )


class CommentResponse(BaseModel):
    """Pydantic schema for returning ticket comment details."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    ticket_id: str
    body: str
    visibility: str = "PUBLIC"
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class AttachmentResponse(BaseModel):
    """Pydantic schema for returning ticket attachment metadata."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    ticket_id: str
    filename: str
    file_size_bytes: int
    mime_type: str
    storage_uri: str
    sha256_hash: str
    uploaded_by: str
    uploaded_at: str
    deid_scrubbed: bool
    version_index: int


class TicketAuditLogResponse(BaseModel):
    """Pydantic schema for returning ticket audit logs."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    ticket_id: str | None = None
    created_at: str
    created_by: str
    reason_for_change: str | None = None
    version_index: int
    action: str
    details: str
    record_id: str | None = None


class PaginatedTicketAuditLogResponse(BaseModel):
    """Paginated representation of ticket audit trail logs."""

    items: list[TicketAuditLogResponse]
    total_count: int
    limit: int
    offset: int
    has_more: bool
    next_page: str | None = None
    next_cursor: str | None = None


class SettingDiffEntry(BaseModel):
    """Schema representing a configuration setting difference entry."""

    setting_key: str
    old_value: str
    new_value: str
    data_type: str = ""


class RegulatoryRiskAssessment(BaseModel):
    """Schema representing a clinical and regulatory risk assessment for a setting change."""

    risk_level: Literal["HIGH_RISK", "MEDIUM_RISK", "LOW_RISK"]
    affected_gxp_clauses: list[str]
    requires_qa_signoff: bool
    summary: str
    risk_summary: str


class TicketKPISummaryResponse(BaseModel):
    """Schema representing aggregated clinical KPI and KRI metrics."""

    total_tickets: int
    active_tickets: int
    open_tickets: int
    in_progress_tickets: int
    waiting_tickets: int
    resolved_tickets: int
    closed_tickets: int
    critical_deviations: int
    sla_breaches: int
    sla_amber_warnings: int
    sla_compliance_rate: float
    mean_time_to_resolution_hours: float
    category_distribution: dict[str, int]
    severity_distribution: dict[str, int]
    rca_distribution: dict[str, int]
    site_distribution: dict[str, int]


class RAGTriageResponse(BaseModel):
    """Schema representing the outcome of Grounded Protocol RAG Support Ticket Triage."""

    ticket_id: str
    rag_status: str
    faithfulness_score: float
    is_grounded: bool
    draft_answer: str | None = None
    citations: list[dict[str, Any]] = Field(default_factory=list)
    routed_to_role: str | None = None
    routing_reason: str
    latency_ms: float = 0.0


class RAGPreviewRequest(BaseModel):
    """Schema for requesting a read-only grounded RAG triage preview."""

    query: str = Field(..., min_length=3, description="Support inquiry text")
    study_id: str = Field(..., description="Study scope identifier")
    protocol_version: str | None = Field(
        None, description="Optional protocol version filter"
    )
    top_k: int = Field(5, ge=1, le=20, description="Max candidate chunks to evaluate")
