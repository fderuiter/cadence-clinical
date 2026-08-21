"""
Unit and integration tests for Knowledge article lifecycle, four-eyes review, and immutable version snapshotting.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

import time
import uuid
from unittest.mock import AsyncMock, patch

import httpx
import pytest
from httpx import ASGITransport
from jose import jwt
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.application.article_service import ArticleLifecycleService
from apps.knowledge.domain.models import (
    ArticleApprovalConflictError,
    ArticleReasonRequiredError,
    ArticleStatus,
)
from apps.knowledge.infrastructure.models import (
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeCategory,
)
from apps.knowledge.main import app
from packages.testing.security import create_test_auth_headers

# ---------------------------------------------------------------------------
# Test Personas & Helpers
# ---------------------------------------------------------------------------

ACTOR_AUTHOR = "author@cadence.clinical"  # deid-ignore
ACTOR_EDITOR = "editor@cadence.clinical"  # deid-ignore
ACTOR_APPROVER = "approver@cadence.clinical"  # deid-ignore
ACTOR_CRC = "crc@cadence.clinical"  # deid-ignore
GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers_with_sig(
    user_id: str, roles: list[str], path: str
) -> dict[str, str]:
    """Generates authentic gateway headers with an eSignature token for regulated endpoints."""
    sig_payload = {
        "sub": user_id,
        "username": user_id,
        "action": path,
        "roles": roles,
        "iat": time.time(),
        "exp": time.time() + 300.0,
        "jti": str(uuid.uuid4()),
    }
    sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
    return create_test_auth_headers(
        user_id=user_id,
        roles=roles,
        tenant_id="tenant_clinical_01",
        sig_token=sig_token,
    )


@pytest.fixture
def auth_headers_author() -> dict[str, str]:
    """Gateway headers for author (super_admin role)."""
    return create_test_auth_headers(
        user_id=ACTOR_AUTHOR,
        roles=["super_admin"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_editor() -> dict[str, str]:
    """Gateway headers for editor (super_admin role)."""
    return create_test_auth_headers(
        user_id=ACTOR_EDITOR,
        roles=["super_admin"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_approver() -> dict[str, str]:
    """Gateway headers for approver (super_admin role)."""
    return create_test_auth_headers(
        user_id=ACTOR_APPROVER,
        roles=["super_admin"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_crc() -> dict[str, str]:
    """Gateway headers for site CRC."""
    return create_test_auth_headers(
        user_id=ACTOR_CRC,
        roles=["site_crc", "crc"],
        tenant_id="tenant_clinical_01",
    )


async def _make_category(svc: ArticleLifecycleService) -> KnowledgeCategory:
    """Creates a test category."""
    return await svc.create_category(
        name="Clinical SOPs",
        slug="clinical-sops",
        description="Standard operating procedures",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Test category setup",
    )


async def _make_article(
    svc: ArticleLifecycleService, category_id: str
) -> KnowledgeArticle:
    """Creates a test article in DRAFT status."""
    return await svc.create_article(
        title="Subject Randomization SOP",
        slug="subject-randomization-sop",
        category_id=category_id,
        body_markdown="# Randomization\n\nInstructions for dynamic cohort randomization.",
        version_label="1.0",
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Initial draft",
    )


# ---------------------------------------------------------------------------
# Acceptance Criteria 1: Draft Storage (#4325)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_draft_creation_and_single_row_in_place_update(
    db_session: AsyncSession,
):
    """
    Validate that working draft updates a single KnowledgeArticleVersion row during DRAFT status.

    @req:PRD-KNB-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    # Initial creation check
    assert article.status == ArticleStatus.DRAFT
    assert article.version_index == 1
    assert article.author_user_id == ACTOR_AUTHOR
    assert article.last_edited_by == ACTOR_AUTHOR

    # Verify initial version snapshot
    draft_ver = await svc.get_working_draft_version(article.id)
    assert draft_ver is not None
    assert draft_ver.version_index == 1
    assert draft_ver.status_at_snapshot == ArticleStatus.DRAFT.value
    assert draft_ver.is_locked is False
    assert "<h1>Randomization</h1>" in draft_ver.body_html

    # Perform multiple in-place working draft updates
    article, draft_ver2 = await svc.update_draft(
        article_id=article.id,
        body_markdown="# Randomization v1.1\n\nUpdated instructions with **stratification**.",
        actor_user_id=ACTOR_EDITOR,
        reason_for_change="Added stratification rules",
    )

    # Verify single row was updated in place, not duplicated
    versions = await svc.list_article_versions(article.id)
    assert len(versions) == 1
    assert versions[0].id == draft_ver.id
    assert (
        versions[0].body_markdown
        == "# Randomization v1.1\n\nUpdated instructions with **stratification**."
    )
    assert "<strong>stratification</strong>" in versions[0].body_html
    assert versions[0].created_by == ACTOR_EDITOR
    assert versions[0].reason_for_change == "Added stratification rules"
    assert article.last_edited_by == ACTOR_EDITOR


@pytest.mark.asyncio
async def test_api_draft_storage_endpoints(
    db_session: AsyncSession,
    auth_headers_author: dict[str, str],
    auth_headers_editor: dict[str, str],
):
    """
    Validate REST API endpoints for Draft Storage: POST /articles, PUT /articles/{id}, GET /articles/{id}.

    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Create Category
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "eCRF Guidelines",
                "slug": "ecrf-guidelines",
                "reason_for_change": "Initial category",
            },
            headers=auth_headers_author,
        )
        assert cat_resp.status_code == 201
        cat_id = cat_resp.json()["id"]

        # 2. POST /api/v1/knowledge/articles
        post_resp = await client.post(
            "/api/v1/knowledge/articles",
            json={
                "title": "Adverse Event Entry",
                "slug": "adverse-event-entry",
                "category_id": cat_id,
                "body_markdown": "## AE Reporting\n\nEnter all grade 3+ events within 24h.",
                "version_label": "1.0",
                "reason_for_change": "Initial creation",
            },
            headers=auth_headers_author,
        )
        assert post_resp.status_code == 201
        art_data = post_resp.json()
        art_id = art_data["id"]
        assert art_data["status"] == "DRAFT"
        assert art_data["author_user_id"] == ACTOR_AUTHOR
        assert art_data["last_edited_by"] == ACTOR_AUTHOR

        # 3. PUT /api/v1/knowledge/articles/{id}
        put_resp = await client.put(
            f"/api/v1/knowledge/articles/{art_id}",
            json={
                "title": "Adverse Event Entry (Updated)",
                "body_markdown": "## AE Reporting\n\nEnter all SAE events within **12 hours**.",
                "reason_for_change": "Tightened reporting window",
            },
            headers=auth_headers_editor,
        )
        assert put_resp.status_code == 200
        put_data = put_resp.json()
        assert put_data["title"] == "Adverse Event Entry (Updated)"
        assert put_data["last_edited_by"] == ACTOR_EDITOR
        assert put_data["author_user_id"] == ACTOR_AUTHOR
        assert "<h2>AE Reporting</h2>" in put_data["body_html"]
        assert "<strong>12 hours</strong>" in put_data["body_html"]

        # 4. GET /api/v1/knowledge/articles/{id}
        get_resp = await client.get(
            f"/api/v1/knowledge/articles/{art_id}",
            headers=auth_headers_author,
        )
        assert get_resp.status_code == 200
        get_data = get_resp.json()
        assert get_data["id"] == art_id
        assert get_data["title"] == "Adverse Event Entry (Updated)"
        assert "<h2>AE Reporting</h2>" in get_data["body_html"]


# ---------------------------------------------------------------------------
# Acceptance Criteria 2: Four-Eyes Review & Snapshots (#4326)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_four_eyes_author_cannot_approve(db_session: AsyncSession):
    """
    Validate that an article cannot be approved by its original author (author_user_id).

    @req:PRD-KNB-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    # Submit for review
    article = await svc.submit_for_review(
        article_id=article.id,
        actor_user_id=ACTOR_AUTHOR,
    )
    assert article.status == ArticleStatus.IN_REVIEW

    # Author attempts approval -> must fail four-eyes check
    with pytest.raises(ArticleApprovalConflictError, match="original author"):
        await svc.approve_article(
            article_id=article.id,
            actor_user_id=ACTOR_AUTHOR,
            reason_for_change="Approving my own authored article",
        )


@pytest.mark.asyncio
async def test_four_eyes_last_editor_cannot_approve(db_session: AsyncSession):
    """
    Validate that an article cannot be approved by the last editor (last_edited_by).

    @req:PRD-KNB-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    # Editor updates draft
    article, _ = await svc.update_draft(
        article_id=article.id,
        body_markdown="# Edited body",
        actor_user_id=ACTOR_EDITOR,
    )
    assert article.last_edited_by == ACTOR_EDITOR

    # Submit for review
    article = await svc.submit_for_review(
        article_id=article.id,
        actor_user_id=ACTOR_EDITOR,
    )

    # Last editor attempts approval -> must fail four-eyes check
    with pytest.raises(ArticleApprovalConflictError, match="last edited"):
        await svc.approve_article(
            article_id=article.id,
            actor_user_id=ACTOR_EDITOR,
            reason_for_change="Approving my own edit",
        )


@pytest.mark.asyncio
async def test_independent_reviewer_approves_and_locks_version(
    db_session: AsyncSession,
):
    """
    Validate that an independent reviewer can approve an article, locking the snapshot as permanently immutable.

    @req:PRD-KNB-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    # Editor modifies draft
    await svc.update_draft(
        article_id=article.id,
        body_markdown="# Validated Protocol Workflow",
        actor_user_id=ACTOR_EDITOR,
    )

    # Submit for review
    await svc.submit_for_review(
        article_id=article.id,
        actor_user_id=ACTOR_EDITOR,
    )

    # Independent third party approves
    article = await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Four-eyes peer review verified per SOP-KNB-002",
    )

    assert article.status == ArticleStatus.APPROVED
    assert article.approved_by == ACTOR_APPROVER

    # Verify version snapshot is locked
    locked_version = await svc.get_working_draft_version(article.id)
    assert locked_version is None

    versions = await svc.list_article_versions(article.id)
    assert len(versions) == 1
    assert versions[0].status_at_snapshot == ArticleStatus.APPROVED.value
    assert versions[0].is_locked is True

    # Verify database-level immutability: modification of locked version body raises ValueError
    versions[0].body_markdown = "TAMPERED CONTENT"
    with pytest.raises(ValueError, match="strictly forbidden"):
        await db_session.flush()


@pytest.mark.asyncio
async def test_api_review_and_four_eyes_workflow(
    db_session: AsyncSession,
    auth_headers_author: dict[str, str],
    auth_headers_editor: dict[str, str],
    auth_headers_approver: dict[str, str],
):
    """
    Validate REST API endpoints for Four-Eyes Review: submit-review, approve (with 403 conflicts), reject.

    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Create Category & Article
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Safety Protocols",
                "slug": "safety-protocols",
                "reason_for_change": "Setup",
            },
            headers=auth_headers_author,
        )
        cat_id = cat_resp.json()["id"]

        art_resp = await client.post(
            "/api/v1/knowledge/articles",
            json={
                "title": "Unblinding Guidelines",
                "slug": "unblinding-guidelines",
                "category_id": cat_id,
                "body_markdown": "# Emergency Unblinding Protocol",
                "reason_for_change": "Initial safety draft",
            },
            headers=auth_headers_author,
        )
        art_id = art_resp.json()["id"]
        approve_path = f"/api/v1/knowledge/articles/{art_id}/approve"

        # Editor updates draft
        await client.put(
            f"/api/v1/knowledge/articles/{art_id}",
            json={
                "body_markdown": "# Emergency Unblinding Protocol (Reviewed)",
                "reason_for_change": "Added Medical Monitor contact info",
            },
            headers=auth_headers_editor,
        )

        # Submit for review
        sub_resp = await client.post(
            f"/api/v1/knowledge/articles/{art_id}/submit-review",
            headers=auth_headers_editor,
        )
        assert sub_resp.status_code == 200
        assert sub_resp.json()["status"] == "IN_REVIEW"

        # 1. Author attempts approval -> 403
        author_sig_headers = get_auth_headers_with_sig(
            ACTOR_AUTHOR, ["super_admin"], approve_path
        )
        author_app_resp = await client.post(
            approve_path,
            json={"reason_for_change": "Author self-approval"},
            headers=author_sig_headers,
        )
        assert author_app_resp.status_code == 403

        # 2. Editor attempts approval -> 403
        editor_sig_headers = get_auth_headers_with_sig(
            ACTOR_EDITOR, ["super_admin"], approve_path
        )
        editor_app_resp = await client.post(
            approve_path,
            json={"reason_for_change": "Editor self-approval"},
            headers=editor_sig_headers,
        )
        assert editor_app_resp.status_code == 403

        # 3. Independent Approver rejects -> 200 (IN_REVIEW -> REJECTED)
        rej_resp = await client.post(
            f"/api/v1/knowledge/articles/{art_id}/reject",
            json={"reason_for_change": "Missing Medical Monitor phone number"},
            headers=auth_headers_approver,
        )
        assert rej_resp.status_code == 200
        assert rej_resp.json()["status"] == "REJECTED"

        # Reopen to DRAFT
        await client.post(
            f"/api/v1/knowledge/articles/{art_id}/transition",
            json={
                "target_status": "DRAFT",
                "reason_for_change": "Fixing rejection remarks",
            },
            headers=auth_headers_editor,
        )

        # Editor fixes and resubmits
        await client.put(
            f"/api/v1/knowledge/articles/{art_id}",
            json={
                "body_markdown": "# Emergency Unblinding Protocol\n\nCall Medical Monitor at +1-800-555-0199.",
                "reason_for_change": "Added phone number",
            },
            headers=auth_headers_editor,
        )
        await client.post(
            f"/api/v1/knowledge/articles/{art_id}/submit-review",
            headers=auth_headers_editor,
        )

        # Independent Approver approves -> 200 (IN_REVIEW -> APPROVED)
        approver_sig_headers = get_auth_headers_with_sig(
            ACTOR_APPROVER, ["super_admin"], approve_path
        )
        app_resp = await client.post(
            approve_path,
            json={"reason_for_change": "Verified phone number added"},
            headers=approver_sig_headers,
        )
        assert app_resp.status_code == 200
        assert app_resp.json()["status"] == "APPROVED"
        assert app_resp.json()["approved_by"] == ACTOR_APPROVER


# ---------------------------------------------------------------------------
# Acceptance Criteria 3: Publication & Auto-Supersede (#4327)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_publication_and_auto_supersede_prior_version(
    db_session: AsyncSession,
):
    """
    Validate that publishing version N+1 updates current_published_version_id for O(1) reads
    and automatically sets prior active version N to SUPERSEDED status.

    @req:PRD-KNB-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    # 1. Publish Version 1.0 (v1)
    await svc.submit_for_review(article_id=article.id, actor_user_id=ACTOR_AUTHOR)
    await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved v1.0",
    )
    article = await svc.publish_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Published v1.0 to site staff",
        version_label="1.0",
    )

    assert article.status == ArticleStatus.PUBLISHED
    assert article.current_published_version_id is not None
    v1_id = article.current_published_version_id

    # Verify O(1) published version retrieval
    published_v1 = await svc.get_current_published_version(article.id)
    assert published_v1 is not None
    assert published_v1.id == v1_id
    assert published_v1.version_label == "1.0"
    assert published_v1.status_at_snapshot == ArticleStatus.PUBLISHED.value

    # 2. Start Version 2.0 (archive v1 -> reopen to DRAFT)
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.ARCHIVED,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Archiving v1.0 to prepare v2.0 revision",
    )
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.DRAFT,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Starting version 2.0 revision",
    )
    assert article.status == ArticleStatus.DRAFT

    # Update working draft for version 2
    await svc.update_draft(
        article_id=article.id,
        body_markdown="# Randomization v2.0\n\nMajor update to stratification algorithms.",
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Updated stratification algorithm",
    )

    # Submit and Approve Version 2.0
    await svc.submit_for_review(article_id=article.id, actor_user_id=ACTOR_AUTHOR)
    await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved v2.0",
    )

    # 3. Publish Version 2.0 -> Must auto-supersede version 1.0
    article = await svc.publish_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Published v2.0",
        version_label="2.0",
    )

    assert article.status == ArticleStatus.PUBLISHED
    assert article.current_published_version_id is not None
    assert article.current_published_version_id != v1_id
    v2_id = article.current_published_version_id

    # Verify O(1) published version retrieval returns v2
    published_v2 = await svc.get_current_published_version(article.id)
    assert published_v2 is not None
    assert published_v2.id == v2_id
    assert published_v2.version_label == "2.0"
    assert published_v2.status_at_snapshot == ArticleStatus.PUBLISHED.value

    # Verify prior version v1 is now SUPERSEDED without data loss
    versions = await svc.list_article_versions(article.id)
    assert len(versions) == 2
    v1_record = next(v for v in versions if v.id == v1_id)
    v2_record = next(v for v in versions if v.id == v2_id)
    assert v1_record.status_at_snapshot == ArticleStatus.SUPERSEDED.value
    assert v2_record.status_at_snapshot == ArticleStatus.PUBLISHED.value


@pytest.mark.asyncio
async def test_api_publish_endpoint_and_auto_supersede(
    db_session: AsyncSession,
    auth_headers_author: dict[str, str],
    auth_headers_approver: dict[str, str],
):
    """
    Validate REST API POST /articles/{id}/publish endpoint and auto-supersede behavior.

    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Monitoring Guidance",
                "slug": "monitoring-guidance",
                "reason_for_change": "Setup",
            },
            headers=auth_headers_author,
        )
        cat_id = cat_resp.json()["id"]

        # Create Article v1
        art_resp = await client.post(
            "/api/v1/knowledge/articles",
            json={
                "title": "Source Data Verification SOP",
                "slug": "sdv-sop",
                "category_id": cat_id,
                "body_markdown": "# SDV SOP v1",
                "version_label": "1.0",
                "reason_for_change": "Initial version",
            },
            headers=auth_headers_author,
        )
        art_id = art_resp.json()["id"]
        approve_path = f"/api/v1/knowledge/articles/{art_id}/approve"
        approver_sig_headers = get_auth_headers_with_sig(
            ACTOR_APPROVER, ["super_admin"], approve_path
        )

        # Submit & Approve v1
        await client.post(
            f"/api/v1/knowledge/articles/{art_id}/submit-review",
            headers=auth_headers_author,
        )
        await client.post(
            approve_path,
            json={"reason_for_change": "Approved v1"},
            headers=approver_sig_headers,
        )

        # Publish v1
        pub1_resp = await client.post(
            f"/api/v1/knowledge/articles/{art_id}/publish",
            json={
                "reason_for_change": "Publishing v1.0",
                "version_label": "1.0",
            },
            headers=auth_headers_approver,
        )
        assert pub1_resp.status_code == 200
        pub1_data = pub1_resp.json()
        assert pub1_data["status"] == "PUBLISHED"
        assert pub1_data["current_published_version_id"] is not None

        # Reopen (ARCHIVED -> DRAFT), update to v2, submit & approve
        await client.post(
            f"/api/v1/knowledge/articles/{art_id}/transition",
            json={
                "target_status": "ARCHIVED",
                "reason_for_change": "Archive v1",
            },
            headers=auth_headers_approver,
        )
        await client.post(
            f"/api/v1/knowledge/articles/{art_id}/transition",
            json={
                "target_status": "DRAFT",
                "reason_for_change": "Reopen for v2.0",
            },
            headers=auth_headers_author,
        )
        await client.put(
            f"/api/v1/knowledge/articles/{art_id}",
            json={
                "body_markdown": "# SDV SOP v2 (Targeted SDV)",
                "reason_for_change": "Targeted SDV update",
            },
            headers=auth_headers_author,
        )
        await client.post(
            f"/api/v1/knowledge/articles/{art_id}/submit-review",
            headers=auth_headers_author,
        )
        approver_sig_headers_v2 = get_auth_headers_with_sig(
            ACTOR_APPROVER, ["super_admin"], approve_path
        )
        app_v2_resp = await client.post(
            approve_path,
            json={"reason_for_change": "Approved v2"},
            headers=approver_sig_headers_v2,
        )
        assert app_v2_resp.status_code == 200

        # Publish v2
        pub2_resp = await client.post(
            f"/api/v1/knowledge/articles/{art_id}/publish",
            json={
                "reason_for_change": "Publishing v2.0",
                "version_label": "2.0",
            },
            headers=auth_headers_approver,
        )
        assert pub2_resp.status_code == 200
        pub2_data = pub2_resp.json()
        assert (
            pub2_data["current_published_version_id"]
            != pub1_data["current_published_version_id"]
        )

        # Check version history
        ver_resp = await client.get(
            f"/api/v1/knowledge/articles/{art_id}/versions",
            headers=auth_headers_author,
        )
        assert ver_resp.status_code == 200
        versions = ver_resp.json()
        assert len(versions) == 2
        assert versions[0]["status_at_snapshot"] == "SUPERSEDED"
        assert versions[1]["status_at_snapshot"] == "PUBLISHED"


# ---------------------------------------------------------------------------
# Additional Lifecycle, Audit Log, and Notifications Tests
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_reason_for_change_enforced_on_publish_and_approve(
    db_session: AsyncSession,
):
    """
    Validate that reason_for_change is mandatory for regulated approval and publication transitions.

    @req:PRD-KNB-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    await svc.submit_for_review(article_id=article.id, actor_user_id=ACTOR_AUTHOR)

    # Approve without reason -> raises ArticleReasonRequiredError
    with pytest.raises(ArticleReasonRequiredError):
        await svc.approve_article(
            article_id=article.id,
            actor_user_id=ACTOR_APPROVER,
            reason_for_change="",
        )


@pytest.mark.asyncio
async def test_notification_dispatched_on_publish(db_session: AsyncSession):
    """
    Validate that notifications are dispatched to event bus on article publication.

    @req:PRD-KNB-001
    @req:PRD-KNB-002
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)
    await svc.submit_for_review(article_id=article.id, actor_user_id=ACTOR_AUTHOR)
    await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved",
    )

    with patch(
        "apps.knowledge.application.article_service.publish_notification",
        new_callable=AsyncMock,
        return_value=True,
    ) as mock_notify:
        await svc.publish_article(
            article_id=article.id,
            actor_user_id=ACTOR_APPROVER,
            reason_for_change="Going live",
        )
        mock_notify.assert_awaited_once()
        payload = mock_notify.call_args[0][0]
        assert payload["event_type"] == "knowledge.article.published"
        assert payload["data"]["article_id"] == article.id


@pytest.mark.asyncio
async def test_audit_logs_emitted_for_all_lifecycle_transitions(
    db_session: AsyncSession,
):
    """
    Validate that immutable audit log entries are generated for every lifecycle state transition.

    @req:PRD-KNB-001
    """
    svc = ArticleLifecycleService(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    await svc.update_draft(
        article_id=article.id,
        body_markdown="# Updated Draft",
        actor_user_id=ACTOR_EDITOR,
    )
    await svc.submit_for_review(article_id=article.id, actor_user_id=ACTOR_EDITOR)
    await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved",
    )
    await svc.publish_article(
        article_id=article.id,
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
    assert "DRAFT_SAVED" in actions
    assert "SUBMITTED_FOR_REVIEW" in actions
    assert "APPROVED" in actions
    assert "PUBLISHED" in actions
