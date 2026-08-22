"""
SQLAlchemy ORM models for the Knowledge & Support Hub microservice.

Implements GxP audit fields, immutable audit log enforcement, two-tier version
snapshotting, and the article controlled-document lifecycle per 21 CFR Part 11.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

from __future__ import annotations

import uuid
from datetime import datetime

from sqlalchemy import (
    Boolean,
    DateTime,
    ForeignKey,
    Integer,
    String,
    Text,
    event,
    func,
    inspect,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column, relationship

from apps.knowledge.domain.models import ArticleStatus


class Base(DeclarativeBase):
    """Declarative base for the Knowledge microservice ORM models."""


class KnowledgeCategory(Base):
    """
    A hierarchical content category for knowledge articles.

    Supports a self-referential parent/child tree and persona-level visibility
    via a comma-separated persona_visibility field.
    """

    __tablename__ = "knowledge_categories"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    name: Mapped[str] = mapped_column(String(255), nullable=False, unique=True)
    slug: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    description: Mapped[str | None] = mapped_column(Text, nullable=True)

    # Self-referential parent category
    parent_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("knowledge_categories.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )

    # Comma-separated list of persona roles that can see articles in this category.
    # Empty/null means visible to all authenticated users.
    persona_visibility: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    # Soft delete
    is_deleted: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    articles: Mapped[list[KnowledgeArticle]] = relationship(
        back_populates="category",
        cascade="all, delete-orphan",
        foreign_keys="[KnowledgeArticle.category_id]",
    )
    parent: Mapped[KnowledgeCategory | None] = relationship(
        "KnowledgeCategory",
        remote_side="KnowledgeCategory.id",
        back_populates="children",
    )
    children: Mapped[list[KnowledgeCategory]] = relationship(
        "KnowledgeCategory",
        back_populates="parent",
    )


class KnowledgeArticle(Base):
    """
    A GxP-controlled knowledge base article with a seven-state lifecycle.

    The article record tracks lifecycle state, version metadata, and authorship.
    Immutable version snapshots are stored in KnowledgeArticleVersion.
    """

    __tablename__ = "knowledge_articles"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    slug: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    title: Mapped[str] = mapped_column(String(500), nullable=False)

    category_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("knowledge_categories.id", ondelete="RESTRICT"),
        nullable=False,
        index=True,
    )

    # Lifecycle state
    status: Mapped[ArticleStatus] = mapped_column(
        String(50), default=ArticleStatus.DRAFT, nullable=False, index=True
    )

    # Version tracking — monotonic int (GxP) + human label (e.g. "1.0")
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    version_label: Mapped[str] = mapped_column(
        String(50), default="1.0", nullable=False
    )

    # Pointer to the currently active published version for O(1) reads
    current_published_version_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey(
            "knowledge_article_versions.id",
            ondelete="SET NULL",
            use_alter=True,
            name="fk_knowledge_article_current_version",
        ),
        nullable=True,
        index=True,
    )

    # Tags for indexing and discovery (stored as JSON/comma-separated string)
    tags: Mapped[str | None] = mapped_column(Text, nullable=True)

    # Four-eyes authorship tracking
    # author_user_id: original creator; never changes.
    author_user_id: Mapped[str] = mapped_column(String(255), nullable=False)
    # last_edited_by: the user who last saved the article body.
    # The approver must differ from this field AND author_user_id (four-eyes principle).
    last_edited_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    # approved_by: recorded on APPROVED transition.
    approved_by: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # Soft delete
    is_deleted: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # 21 CFR Part 11 audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    # Relationships
    category: Mapped[KnowledgeCategory] = relationship(
        back_populates="articles", foreign_keys=[category_id]
    )
    versions: Mapped[list[KnowledgeArticleVersion]] = relationship(
        back_populates="article",
        cascade="all, delete-orphan",
        order_by="KnowledgeArticleVersion.version_index",
        foreign_keys="[KnowledgeArticleVersion.article_id]",
    )
    current_published_version: Mapped[KnowledgeArticleVersion | None] = relationship(
        "KnowledgeArticleVersion",
        foreign_keys=[current_published_version_id],
        post_update=True,
    )
    audit_logs: Mapped[list[KnowledgeArticleAuditLog]] = relationship(
        back_populates="article",
        foreign_keys="[KnowledgeArticleAuditLog.article_id]",
    )
    contextual_mappings: Mapped[list[ContextualHelpMapping]] = relationship(
        back_populates="article",
        cascade="all, delete-orphan",
        foreign_keys="[ContextualHelpMapping.article_id]",
    )


class KnowledgeArticleVersion(Base):
    """
    An immutable snapshot of a KnowledgeArticle's body at a specific version.

    Once an article reaches APPROVED status, the body content is snapshotted
    here and locked permanently (non-destructive historical retention per
    21 CFR Part 11 and ADR-2188).
    """

    __tablename__ = "knowledge_article_versions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    article_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("knowledge_articles.id", ondelete="RESTRICT"),
        nullable=False,
        index=True,
    )

    # Version metadata — mirrors the parent article at snapshot time
    version_index: Mapped[int] = mapped_column(Integer, nullable=False, index=True)
    version_label: Mapped[str] = mapped_column(String(50), nullable=False)
    status_at_snapshot: Mapped[str] = mapped_column(String(50), nullable=False)

    # Content — Markdown source; rendered HTML cached for performance
    body_markdown: Mapped[str] = mapped_column(Text, nullable=False)
    body_html: Mapped[str | None] = mapped_column(Text, nullable=True)

    # Immutability lock flag
    is_locked: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    # Relationships
    article: Mapped[KnowledgeArticle] = relationship(
        back_populates="versions", foreign_keys=[article_id]
    )


class KnowledgeArticleAuditLog(Base):
    """
    Immutable, append-only audit ledger for all lifecycle actions on KnowledgeArticle records.

    Records are protected from update or deletion by a SQLAlchemy before_flush
    session event, ensuring tamper-evidence per 21 CFR Part 11 §11.10(e).
    """

    __tablename__ = "knowledge_article_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    article_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("knowledge_articles.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )

    action: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    previous_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    new_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    details: Mapped[str | None] = mapped_column(String(2000), nullable=True)

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    article: Mapped[KnowledgeArticle | None] = relationship(
        back_populates="audit_logs", foreign_keys=[article_id]
    )


class ContextualHelpMapping(Base):
    """
    Maps a frontend route pattern and persona to a relevant KnowledgeArticle.

    Used by the contextual help panel to surface the most relevant article
    based on what the user is currently doing in the platform.
    """

    __tablename__ = "knowledge_contextual_help_mappings"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )

    # Route pattern (e.g. "/ecrf", "/mdr", "/tickets")
    route_pattern: Mapped[str] = mapped_column(String(500), nullable=False, index=True)

    # Persona role this mapping applies to; null means all personas
    persona: Mapped[str | None] = mapped_column(String(100), nullable=True, index=True)

    article_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("knowledge_articles.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )

    # Specific section anchor inside article (e.g. "#enrollment-procedure")
    section_anchor: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # Lower number = higher priority when multiple mappings match
    priority: Mapped[int] = mapped_column(Integer, default=100, nullable=False)

    # Active flag for toggling mappings without deleting
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    article: Mapped[KnowledgeArticle] = relationship(
        back_populates="contextual_mappings", foreign_keys=[article_id]
    )


class ProtocolKnowledgeChunk(Base):
    """
    A chunk of a clinical study protocol or SOP document indexed for grounded RAG.

    Stores text content and dense embeddings while retaining structural coordinates
    (protocol version, section number, section title, page number) for 21 CFR Part 11
    and ICH GCP auditability.
    """

    __tablename__ = "protocol_knowledge_chunks"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    document_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    protocol_version: Mapped[str] = mapped_column(
        String(50), nullable=False, index=True
    )
    document_type: Mapped[str] = mapped_column(
        String(50), default="PROTOCOL", nullable=False
    )

    # Structural coordinates
    section_number: Mapped[str | None] = mapped_column(String(50), nullable=True)
    section_title: Mapped[str] = mapped_column(String(500), nullable=False)
    page_number: Mapped[int] = mapped_column(Integer, nullable=False, index=True)
    chunk_index: Mapped[int] = mapped_column(Integer, default=0, nullable=False)

    # Content and dense vector embedding
    chunk_text: Mapped[str] = mapped_column(Text, nullable=False)
    embedding_json: Mapped[str | None] = mapped_column(Text, nullable=True)
    token_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)

    # GxP approval gate
    is_approved: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # GxP audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


# ---------------------------------------------------------------------------
# Immutability guards — prevents update or deletion of audit logs and locked version snapshots.
# ---------------------------------------------------------------------------


@event.listens_for(Session, "before_flush")
def prevent_audit_log_and_locked_version_modification(
    session: Session, flush_context, instances
) -> None:
    """
    Ensures that KnowledgeArticleAuditLog and locked KnowledgeArticleVersion
    records can never be updated or deleted.

    Raises:
        ValueError: On any attempt to modify or delete a locked or audit record,
            enforcing 21 CFR Part 11 immutability requirements.
    """
    for obj in session.dirty:
        if isinstance(obj, KnowledgeArticleAuditLog):
            raise ValueError(
                "Updates to KnowledgeArticleAuditLog are strictly forbidden "
                "to comply with 21 CFR Part 11."
            )
        if isinstance(obj, KnowledgeArticleVersion):
            insp = inspect(obj)
            locked_hist = insp.attrs.is_locked.history
            was_locked = (obj.is_locked and not locked_hist.has_changes()) or (
                True in locked_hist.deleted
            )
            if was_locked and (
                insp.attrs.body_markdown.history.has_changes()
                or insp.attrs.body_html.history.has_changes()
                or insp.attrs.version_index.history.has_changes()
            ):
                raise ValueError(
                    "Modifications to locked KnowledgeArticleVersion snapshots "
                    "are strictly forbidden to comply with 21 CFR Part 11."
                )

    for obj in session.deleted:
        if isinstance(obj, KnowledgeArticleAuditLog):
            raise ValueError(
                "Deletions from KnowledgeArticleAuditLog are strictly forbidden "
                "to comply with 21 CFR Part 11."
            )
        if isinstance(obj, KnowledgeArticleVersion) and obj.is_locked:
            raise ValueError(
                "Deletions of locked KnowledgeArticleVersion snapshots "
                "are strictly forbidden to comply with 21 CFR Part 11."
            )


__all__ = [
    "Base",
    "ContextualHelpMapping",
    "KnowledgeArticle",
    "KnowledgeArticleAuditLog",
    "KnowledgeArticleVersion",
    "KnowledgeCategory",
    "ProtocolKnowledgeChunk",
]
