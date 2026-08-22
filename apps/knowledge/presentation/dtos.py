"""
Pydantic v2 DTOs for the Knowledge & Support Hub REST API.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

import json
from datetime import datetime
from typing import Any

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
    tags: list[str] | str | None = None
    reason_for_change: str = Field(..., min_length=1, max_length=1000)


class ArticleUpdate(BaseModel):
    """Request body for updating a working draft KnowledgeArticle (PUT /articles/{id})."""

    title: str | None = Field(default=None, min_length=1, max_length=500)
    slug: str | None = Field(
        default=None, min_length=1, max_length=255, pattern=r"^[a-z0-9-]+$"
    )
    category_id: str | None = None
    body_markdown: str = Field(..., min_length=1)
    tags: list[str] | str | None = None
    reason_for_change: str | None = Field(default=None, max_length=1000)


class ArticleDraftSave(BaseModel):
    """Request body for saving an updated draft body (PUT/PATCH /articles/{id}/draft)."""

    body_markdown: str = Field(..., min_length=1)
    title: str | None = Field(default=None, min_length=1, max_length=500)
    slug: str | None = Field(
        default=None, min_length=1, max_length=255, pattern=r"^[a-z0-9-]+$"
    )
    category_id: str | None = None
    tags: list[str] | str | None = None
    reason_for_change: str | None = Field(default=None, max_length=1000)


class ArticleSubmitReviewRequest(BaseModel):
    """Request body for submitting an article for review."""

    reason_for_change: str | None = Field(default=None, max_length=1000)


class ArticleApproveRequest(BaseModel):
    """Request body for approving an article."""

    reason_for_change: str = Field(..., min_length=1, max_length=1000)


class ArticleRejectRequest(BaseModel):
    """Request body for rejecting an article."""

    reason_for_change: str | None = Field(default=None, max_length=1000)


class ArticlePublishRequest(BaseModel):
    """Request body for publishing an approved article."""

    reason_for_change: str = Field(..., min_length=1, max_length=1000)
    version_label: str | None = Field(default=None, max_length=50)


class ArticleTransitionRequest(BaseModel):
    """Request body for performing a generic state machine transition on an article."""

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
    is_locked: bool = False
    created_at: datetime
    created_by: str
    reason_for_change: str

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
    current_published_version_id: str | None = None
    tags: list[str] | None = None
    author_user_id: str
    last_edited_by: str | None
    approved_by: str | None
    body_markdown: str | None = None
    body_html: str | None = None
    is_deleted: bool
    created_at: datetime
    created_by: str
    reason_for_change: str

    model_config = {"from_attributes": True}

    @field_validator("tags", mode="before")
    @classmethod
    def parse_tags(cls, v: Any) -> list[str] | None:
        if v is None:
            return None
        if isinstance(v, list):
            return [str(item) for item in v]
        if isinstance(v, str):
            v_str = v.strip()
            if not v_str:
                return None
            if v_str.startswith("[") and v_str.endswith("]"):
                try:
                    parsed = json.loads(v_str)
                    if isinstance(parsed, list):
                        return [str(item) for item in parsed]
                except Exception:
                    pass
            return [t.strip() for t in v_str.split(",") if t.strip()]
        return None


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
    section_anchor: str | None = Field(default=None, max_length=255)
    priority: int = Field(default=100, ge=1, le=9999)
    is_active: bool = True
    reason_for_change: str = Field(..., min_length=1, max_length=1000)


class ContextualHelpMappingUpdate(BaseModel):
    """Request body for updating an existing contextual help mapping."""

    route_pattern: str | None = Field(default=None, min_length=1, max_length=500)
    persona: str | None = Field(default=None, max_length=100)
    article_id: str | None = None
    section_anchor: str | None = Field(default=None, max_length=255)
    priority: int | None = Field(default=None, ge=1, le=9999)
    is_active: bool | None = None
    reason_for_change: str = Field(..., min_length=1, max_length=1000)


class ContextualHelpMappingResponse(BaseModel):
    """Response schema for a ContextualHelpMapping."""

    id: str
    route_pattern: str
    persona: str | None
    article_id: str
    section_anchor: str | None = None
    priority: int
    is_active: bool = True
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    model_config = {"from_attributes": True}


class ContextualHelpResolutionResponse(BaseModel):
    """Dynamic resolution response for in-page contextual help."""

    matched_mapping: ContextualHelpMappingResponse | None = None
    primary_article: ArticleResponse | None = None
    primary_version: ArticleVersionResponse | None = None
    section_anchor: str | None = None
    related_articles: list[ArticleResponse] = Field(default_factory=list)
    # Backward-compatibility aliases
    article: ArticleResponse | None = None
    version: ArticleVersionResponse | None = None


# Alias for backwards compatibility
ContextualHelpLookupResponse = ContextualHelpResolutionResponse


__all__ = [
    "ArticleApproveRequest",
    "ArticleCreate",
    "ArticleDraftSave",
    "ArticlePublishRequest",
    "ArticleRejectRequest",
    "ArticleResponse",
    "ArticleSubmitReviewRequest",
    "ArticleTransitionRequest",
    "ArticleUpdate",
    "ArticleVersionResponse",
    "AuditLogResponse",
    "CategoryCreate",
    "CategoryResponse",
    "ContextualHelpLookupResponse",
    "ContextualHelpMappingCreate",
    "ContextualHelpMappingResponse",
    "ContextualHelpMappingUpdate",
    "ContextualHelpResolutionResponse",
]
