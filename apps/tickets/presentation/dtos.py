"""
Pydantic DTO schemas for Tickets presentation layer.
"""

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from apps.tickets.domain.models import TicketCategory, TicketPriority, TicketStatus


class TicketCreate(BaseModel):
    """Pydantic schema for creating a support ticket."""

    title: str = Field(..., description="Title of the support ticket")
    description: str = Field(..., description="Detailed description of the issue")
    category: TicketCategory = Field(
        TicketCategory.OTHER, description="Category of the ticket"
    )
    priority: TicketPriority = Field(
        TicketPriority.LOW, description="Priority level of the ticket"
    )
    reporter: str | None = Field(None, description="Reporter of the ticket")
    assignee_user: str | None = Field(None, description="Assigned user")
    assignee_role: str | None = Field(None, description="Assigned role")
    org_id: str | None = Field(None, description="Scope organization ID")
    site_id: str | None = Field(None, description="Scope site ID")
    study_id: str | None = Field(None, description="Scope study ID")
    related_entity_type: str | None = Field(None, description="Related entity type")
    related_entity_id: str | None = Field(None, description="Related entity ID")
    due_date: datetime | None = Field(None, description="Optional due date")


class TicketUpdate(BaseModel):
    """Pydantic schema for updating an existing support ticket."""

    title: str | None = Field(None, description="Updated title")
    description: str | None = Field(None, description="Updated description")
    category: TicketCategory | None = Field(None, description="Updated category")
    priority: TicketPriority | None = Field(None, description="Updated priority")
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
    reporter: str
    assignee_user: str | None = None
    assignee_role: str | None = None
    org_id: str | None = None
    site_id: str | None = None
    study_id: str | None = None
    related_entity_type: str | None = None
    related_entity_id: str | None = None
    due_date: str | None = None
    is_deleted: bool
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CommentCreate(BaseModel):
    """Pydantic schema for creating a ticket comment."""

    body: str = Field(..., description="The comment body text")


class CommentResponse(BaseModel):
    """Pydantic schema for returning ticket comment details."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    ticket_id: str
    body: str
    created_at: str
    created_by: str
    reason_for_change: str
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
