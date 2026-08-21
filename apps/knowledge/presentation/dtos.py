"""
Pydantic v2 DTOs for the Knowledge & Support Hub REST API.

Requirements: PRD-SYS-KH-001, PRD-SYS-KH-002
"""

from datetime import datetime

from pydantic import BaseModel, Field, field_validator

from apps.knowledge.domain.models import ArticleStatus

# ---------------------------------------------------------------------------
# Category DTOs
# ---------------------------------------------------------------------------


class CategoryCreate(BaseModel):
    """Request body for creating a KnowledgeCategory."""

    name: str = Field(..., min_length=1, max_length=255)
    slug: str = Field(..., min_length=1, max_length=255, pattern=r"^[a-z0-9-]+$")
    description: str | None = None
    persona_visibility: str | None = None
    parent_id: str | None = None
    reason_for_change: str = Field(..., min_length=1, max_length=1000)


class CategoryResponse(BaseModel):
    """Response schema for a KnowledgeCategory."""

    id: str
    name: str
    slug: str
    description: str | None
    persona_visibility: str | None
    parent_id: str | None
    is_deleted: bool
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    model_config = {"from_attributes": True}


# ---------------------------------------------------------------------------
# Article DTOs
# ---------------------------------------------------------------------------


class ArticleCreate(BaseModel):
    """Request body for creating a new KnowledgeArticle (starts in DRAFT)."""

    title: str = Field(..., min_length=1, max_length=500)
    slug: str = Field(..., min_length=1, max_length=255, pattern=r"^[a-z0-9-]+$")
    category_id: str
    body_markdown: str = Field(..., min_length=1)
    version_label: str = Field(default="1.0", max_length=50)
    reason_for_change: str = Field(..., min_length=1, max_length=1000)


class ArticleDraftSave(BaseModel):
    """Request body for saving an updated draft body."""

    body_markdown: str = Field(..., min_length=1)
    reason_for_change: str | None = Field(default=None, max_length=1000)


class ArticleTransitionRequest(BaseModel):
    """Request body for performing a state machine transition on an article."""

    target_status: ArticleStatus
    reason_for_change: str | None = Field(default=None, max_length=1000)
    version_label: str | None = Field(default=None, max_length=50)

    @field_validator("target_status")
    @classmethod
    def reject_system_transitions(cls, v: ArticleStatus) -> ArticleStatus:
        """Prevent clients from directly requesting SUPERSEDED (system-only)."""
        if v == ArticleStatus.SUPERSEDED:
            raise ValueError(
                "The SUPERSEDED status is system-managed and cannot be requested directly."
            )
        return v


class ArticleVersionResponse(BaseModel):
    """Response schema for a KnowledgeArticleVersion snapshot."""

    id: str
    article_id: str
    version_index: int
    version_label: str
    status_at_snapshot: str
    body_markdown: str
    body_html: str | None
    created_at: datetime
    created_by: str

    model_config = {"from_attributes": True}


class ArticleResponse(BaseModel):
    """Response schema for a KnowledgeArticle."""

    id: str
    slug: str
    title: str
    category_id: str
    status: ArticleStatus
    version_index: int
    version_label: str
    author_user_id: str
    last_edited_by: str | None
    approved_by: str | None
    is_deleted: bool
    created_at: datetime
    created_by: str
    reason_for_change: str

    model_config = {"from_attributes": True}


# ---------------------------------------------------------------------------
# Audit Log DTOs
# ---------------------------------------------------------------------------


class AuditLogResponse(BaseModel):
    """Response schema for a KnowledgeArticleAuditLog entry."""

    id: str
    article_id: str | None
    action: str
    previous_status: str | None
    new_status: str | None
    details: str | None
    created_at: datetime
    created_by: str
    reason_for_change: str | None
    version_index: int

    model_config = {"from_attributes": True}


# ---------------------------------------------------------------------------
# Contextual Help DTOs
# ---------------------------------------------------------------------------


class ContextualHelpMappingCreate(BaseModel):
    """Request body for creating a contextual help mapping."""

    route_pattern: str = Field(..., min_length=1, max_length=500)
    persona: str | None = Field(default=None, max_length=100)
    article_id: str
    priority: int = Field(default=100, ge=1, le=9999)
    reason_for_change: str = Field(..., min_length=1, max_length=1000)


class ContextualHelpMappingResponse(BaseModel):
    """Response schema for a ContextualHelpMapping."""

    id: str
    route_pattern: str
    persona: str | None
    article_id: str
    priority: int
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    model_config = {"from_attributes": True}


class ContextualHelpLookupResponse(BaseModel):
    """Response for a contextual help panel lookup."""

    article: ArticleResponse | None
    version: ArticleVersionResponse | None
