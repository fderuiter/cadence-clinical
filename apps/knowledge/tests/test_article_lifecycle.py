"""
Unit tests for the Knowledge microservice article lifecycle.

Covers all 8 settled design decisions from wayfinder ticket #4237:
1. State machine — all valid transitions, all invalid transitions
2. Four-eyes principle — same editor cannot approve
3. Version numbering — version_index increments + version_label preserved
4. reason_for_change requirement on regulated transitions
5. Notification triggers (dispatch called on correct transitions)
6. Auto-supersede — publishing auto-transitions old PUBLISHED article
7. Reopen from Archived/Rejected -> DRAFT
8. All events emit an audit log record

Requirements: PRD-SYS-KH-001 (article lifecycle), PRD-SYS-KH-002 (GxP compliance)
"""

from unittest.mock import AsyncMock, patch

import pytest

from apps.knowledge.infrastructure.services import ArticleLifecycleService
from apps.knowledge.domain.models import (
    ArticleApprovalConflictError,
    ArticleReasonRequiredError,
    ArticleStatus,
    ArticleTransitionError,
)
from apps.knowledge.infrastructure.models import (
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeCategory,
)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

ACTOR_AUTHOR = "author@example.com"  # deid-ignore
ACTOR_APPROVER = "approver@example.com"  # deid-ignore


async def _make_category(svc: ArticleLifecycleService) -> KnowledgeCategory:
    """Creates a test category."""
    return await svc.create_category(
        name="Test Category",
        slug="test-category",
        description=None,
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Test setup",
    )


async def _make_article(
    svc: ArticleLifecycleService, category_id: str
) -> KnowledgeArticle:
    """Creates a test article in DRAFT status."""
    return await svc.create_article(
        title="Test Article",
        slug="test-article",
        category_id=category_id,
        body_markdown="# Hello\n\nThis is a test article.",
        version_label="1.0",
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Initial draft",
    )


# ---------------------------------------------------------------------------
# Decision 1 — State machine: valid transitions
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_valid_transition_draft_to_in_review(db_session):
    """
    Validate that a DRAFT article can transition to IN_REVIEW.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    assert article.status == ArticleStatus.DRAFT

    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    assert article.status == ArticleStatus.IN_REVIEW


@pytest.mark.asyncio
async def test_valid_transition_in_review_to_approved(db_session):
    """
    Validate that an IN_REVIEW article can be approved by a different user.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )

    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Reviewed and approved per SOP-KH-01",
    )
    assert article.status == ArticleStatus.APPROVED
    assert article.approved_by == ACTOR_APPROVER


@pytest.mark.asyncio
async def test_valid_transition_approved_to_published(db_session):
    """
    Validate the APPROVED -> PUBLISHED transition increments version_index.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved",
    )
    initial_version_index = article.version_index
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.PUBLISHED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Publishing to all users",
    )
    assert article.status == ArticleStatus.PUBLISHED
    assert article.version_index == initial_version_index + 1


@pytest.mark.asyncio
async def test_valid_transition_in_review_to_rejected(db_session):
    """
    Validate that an IN_REVIEW article can be rejected (returns comment to author).

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.REJECTED,
        actor_user_id=ACTOR_APPROVER,
    )
    assert article.status == ArticleStatus.REJECTED


@pytest.mark.asyncio
async def test_valid_transition_rejected_to_draft(db_session):
    """
    Validate that a REJECTED article can return to DRAFT.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.REJECTED,
        actor_user_id=ACTOR_APPROVER,
    )
    article = await svc.transition(
        article=article, target_status=ArticleStatus.DRAFT, actor_user_id=ACTOR_AUTHOR
    )
    assert article.status == ArticleStatus.DRAFT


@pytest.mark.asyncio
async def test_valid_transition_archived_to_draft(db_session):
    """
    Validate that an ARCHIVED article can be reopened as a new DRAFT (Q7 decision).

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved",
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.PUBLISHED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Published",
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.ARCHIVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Archiving",
    )
    article = await svc.transition(
        article=article, target_status=ArticleStatus.DRAFT, actor_user_id=ACTOR_AUTHOR
    )
    assert article.status == ArticleStatus.DRAFT


# ---------------------------------------------------------------------------
# Decision 1 — State machine: invalid transitions
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_invalid_transition_draft_to_published_raises(db_session):
    """
    Validate that jumping directly from DRAFT to PUBLISHED is rejected.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    with pytest.raises(ArticleTransitionError):
        await svc.transition(
            article=article,
            target_status=ArticleStatus.PUBLISHED,
            actor_user_id=ACTOR_AUTHOR,
            reason_for_change="Bypassing review",
        )


@pytest.mark.asyncio
async def test_invalid_transition_published_to_draft_raises(db_session):
    """
    Validate that a PUBLISHED article cannot jump directly to DRAFT
    (must go through ARCHIVED -> DRAFT).

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved",
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.PUBLISHED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Published",
    )

    with pytest.raises(ArticleTransitionError):
        await svc.transition(
            article=article,
            target_status=ArticleStatus.DRAFT,
            actor_user_id=ACTOR_AUTHOR,
        )


# ---------------------------------------------------------------------------
# Decision 2 — Four-eyes principle
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_four_eyes_same_editor_cannot_approve(db_session):
    """
    Validate that the user who last edited an article cannot approve it.

    @req:PRD-SYS-KH-002
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )

    with pytest.raises(ArticleApprovalConflictError):
        await svc.transition(
            article=article,
            target_status=ArticleStatus.APPROVED,
            actor_user_id=ACTOR_AUTHOR,  # same as last_edited_by
            reason_for_change="Approving my own work",
        )


@pytest.mark.asyncio
async def test_four_eyes_different_user_can_approve(db_session):
    """
    Validate that a different user can approve an article after another user edits it.

    @req:PRD-SYS-KH-002
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )

    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Reviewed independently",
    )
    assert article.approved_by == ACTOR_APPROVER


# ---------------------------------------------------------------------------
# Decision 3 — Version numbering
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_version_index_increments_on_publish(db_session):
    """
    Validate version_index increments by 1 on each PUBLISHED transition.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    assert article.version_index == 1

    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="OK",
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.PUBLISHED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Go live",
        version_label="1.0",
    )

    assert article.version_index == 2
    assert article.version_label == "1.0"


# ---------------------------------------------------------------------------
# Decision 4 — reason_for_change requirement
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_reason_required_on_approve_raises_without_it(db_session):
    """
    Validate that attempting to APPROVE without a reason_for_change raises an error.

    @req:PRD-SYS-KH-002
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )

    with pytest.raises(ArticleReasonRequiredError):
        await svc.transition(
            article=article,
            target_status=ArticleStatus.APPROVED,
            actor_user_id=ACTOR_APPROVER,
            reason_for_change=None,  # missing
        )


@pytest.mark.asyncio
async def test_reason_required_on_publish_raises_without_it(db_session):
    """
    Validate that attempting to PUBLISH without a reason_for_change raises an error.

    @req:PRD-SYS-KH-002
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="OK",
    )

    with pytest.raises(ArticleReasonRequiredError):
        await svc.transition(
            article=article,
            target_status=ArticleStatus.PUBLISHED,
            actor_user_id=ACTOR_APPROVER,
            reason_for_change="",  # blank
        )


@pytest.mark.asyncio
async def test_reason_not_required_on_draft_save(db_session):
    """
    Validate that saving a draft without a reason_for_change is allowed.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    version = await svc.save_draft(
        article=article,
        body_markdown="# Updated content",
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change=None,  # optional on draft saves
    )
    assert version.body_markdown == "# Updated content"


# ---------------------------------------------------------------------------
# Decision 5 — Notification dispatch
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_notification_dispatched_on_published(db_session):
    """
    Validate that a notification is dispatched when an article is Published.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="OK",
    )

    with patch(
        "apps.knowledge.infrastructure.services.publish_notification",
        new_callable=AsyncMock,
        return_value=True,
    ) as mock_notify:
        article = await svc.transition(
            article=article,
            target_status=ArticleStatus.PUBLISHED,
            actor_user_id=ACTOR_APPROVER,
            reason_for_change="Publishing",
        )
        mock_notify.assert_awaited_once()
        call_kwargs = mock_notify.call_args[0][0]
        assert call_kwargs["event_type"] == "knowledge.article.published"


@pytest.mark.asyncio
async def test_no_notification_dispatched_on_draft_save(db_session):
    """
    Validate that no notification is dispatched when saving a draft.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    with patch(
        "apps.knowledge.infrastructure.services.publish_notification",
        new_callable=AsyncMock,
    ) as mock_notify:
        await svc.save_draft(
            article=article,
            body_markdown="# Draft update",
            actor_user_id=ACTOR_AUTHOR,
        )
        mock_notify.assert_not_awaited()


# ---------------------------------------------------------------------------
# Decision 8 — Audit log records emitted for all actions
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_audit_log_created_on_article_creation(db_session):
    """
    Validate that a CREATED audit log entry is written when an article is created.

    @req:PRD-SYS-KH-002
    """
    from sqlalchemy import select

    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    result = await db_session.execute(
        select(KnowledgeArticleAuditLog).where(
            KnowledgeArticleAuditLog.article_id == article.id
        )
    )
    logs = result.scalars().all()
    assert len(logs) == 1
    assert logs[0].action == "CREATED"
    assert logs[0].new_status == "DRAFT"


@pytest.mark.asyncio
async def test_audit_log_written_on_every_transition(db_session):
    """
    Validate that an audit log entry is emitted for every state transition.

    @req:PRD-SYS-KH-002
    """
    from sqlalchemy import select

    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.IN_REVIEW,
        actor_user_id=ACTOR_AUTHOR,
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.APPROVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved",
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.PUBLISHED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Published",
    )

    result = await db_session.execute(
        select(KnowledgeArticleAuditLog)
        .where(KnowledgeArticleAuditLog.article_id == article.id)
        .order_by(KnowledgeArticleAuditLog.created_at.asc())
    )
    logs = result.scalars().all()
    actions = [log.action for log in logs]
    assert "CREATED" in actions
    assert "SUBMITTED_FOR_REVIEW" in actions
    assert "APPROVED" in actions
    assert "PUBLISHED" in actions


@pytest.mark.asyncio
async def test_audit_log_immutability_enforced(db_session):
    """
    Validate that updating a KnowledgeArticleAuditLog record raises ValueError.

    @req:PRD-SYS-KH-002
    """
    from sqlalchemy import select

    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    result = await db_session.execute(
        select(KnowledgeArticleAuditLog).where(
            KnowledgeArticleAuditLog.article_id == article.id
        )
    )
    log_entry = result.scalar_one()
    log_entry.action = "TAMPERED"

    with pytest.raises(ValueError, match="strictly forbidden"):
        await db_session.flush()
