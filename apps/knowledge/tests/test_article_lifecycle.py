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
from apps.knowledge.adapters.repositories import create_article_service
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
from packages.security.audit_logger import audit_logger_engine
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

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
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

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
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


@pytest.mark.asyncio
async def test_create_article_with_tags_json_array(
    db_session: AsyncSession,
    auth_headers_author: dict[str, str],
    auth_headers_editor: dict[str, str],
):
    """
    Validate keywords and tags are stored as JSON array on KnowledgeArticle and returned in DTOs.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Create Category
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Tags Category",
                "slug": "tags-category",
                "reason_for_change": "Setup category",
            },
            headers=auth_headers_author,
        )
        cat_id = cat_resp.json()["id"]

        # Create Article with JSON list of tags
        post_resp = await client.post(
            "/api/v1/knowledge/articles",
            json={
                "title": "Randomization Stratification SOP",
                "slug": "rand-strat-sop",
                "category_id": cat_id,
                "body_markdown": "# Randomization\n\nStratification instructions.",
                "tags": ["randomization", "stratification", "rtsm"],
                "reason_for_change": "Initial creation with tags",
            },
            headers=auth_headers_author,
        )
        assert post_resp.status_code == 201
        data = post_resp.json()
        assert data["tags"] == ["randomization", "stratification", "rtsm"]
        art_id = data["id"]

        # Verify GET returns tags as array
        get_resp = await client.get(
            f"/api/v1/knowledge/articles/{art_id}",
            headers=auth_headers_author,
        )
        assert get_resp.status_code == 200
        get_data = get_resp.json()
        assert get_data["tags"] == ["randomization", "stratification", "rtsm"]

        # Update tags via PUT /articles/{id}
        put_resp = await client.put(
            f"/api/v1/knowledge/articles/{art_id}",
            json={
                "body_markdown": "# Randomization\n\nUpdated stratification instructions.",
                "tags": ["randomization", "adaptive-trial", "rtsm"],
                "reason_for_change": "Updated tags",
            },
            headers=auth_headers_editor,
        )
        assert put_resp.status_code == 200
        assert put_resp.json()["tags"] == [
            "randomization",
            "adaptive-trial",
            "rtsm",
        ]


@pytest.mark.asyncio
async def test_api_put_and_patch_draft_endpoints(
    db_session: AsyncSession,
    auth_headers_author: dict[str, str],
    auth_headers_editor: dict[str, str],
):
    """
    Validate PUT and PATCH /api/v1/knowledge/articles/{id}/draft endpoints update active working draft.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Create Category & Article
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Draft Endpoints Category",
                "slug": "draft-endpoints-category",
                "reason_for_change": "Setup category",
            },
            headers=auth_headers_author,
        )
        cat_id = cat_resp.json()["id"]

        art_resp = await client.post(
            "/api/v1/knowledge/articles",
            json={
                "title": "Query Management SOP",
                "slug": "query-management-sop",
                "category_id": cat_id,
                "body_markdown": "# Query SOP\n\nInitial query guidance.",
                "reason_for_change": "Initial creation",
            },
            headers=auth_headers_author,
        )
        art_id = art_resp.json()["id"]

        # 1. Update working draft via PUT /articles/{id}/draft
        put_draft_resp = await client.put(
            f"/api/v1/knowledge/articles/{art_id}/draft",
            json={
                "body_markdown": "# Query SOP\n\nUpdated query resolution workflow.",
                "tags": ["queries", "dm", "ecrf"],
                "reason_for_change": "Updated query resolution guidance via PUT",
            },
            headers=auth_headers_editor,
        )
        assert put_draft_resp.status_code == 200
        v_data = put_draft_resp.json()
        assert (
            v_data["body_markdown"]
            == "# Query SOP\n\nUpdated query resolution workflow."
        )
        assert "<h1>Query SOP</h1>" in v_data["body_html"]
        assert v_data["is_locked"] is False

        # 2. Update working draft via PATCH /articles/{id}/draft
        patch_draft_resp = await client.patch(
            f"/api/v1/knowledge/articles/{art_id}/draft",
            json={
                "body_markdown": "# Query SOP\n\nTightened 48h resolution SLA.",
                "reason_for_change": "Updated SLA via PATCH",
            },
            headers=auth_headers_editor,
        )
        assert patch_draft_resp.status_code == 200
        pv_data = patch_draft_resp.json()
        assert (
            pv_data["body_markdown"] == "# Query SOP\n\nTightened 48h resolution SLA."
        )
        assert "Tightened 48h resolution SLA" in pv_data["body_html"]

        # Verify only 1 version exists and was updated in place
        vers_resp = await client.get(
            f"/api/v1/knowledge/articles/{art_id}/versions",
            headers=auth_headers_author,
        )
        assert vers_resp.status_code == 200
        assert len(vers_resp.json()) == 1


@pytest.mark.asyncio
async def test_update_draft_on_non_draft_article_fails(
    db_session: AsyncSession,
    auth_headers_author: dict[str, str],
    auth_headers_editor: dict[str, str],
):
    """
    Validate that updating a draft on an article in non-DRAFT status raises 409 Conflict.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Review State Category",
                "slug": "review-state-category",
                "reason_for_change": "Setup category",
            },
            headers=auth_headers_author,
        )
        cat_id = cat_resp.json()["id"]

        art_resp = await client.post(
            "/api/v1/knowledge/articles",
            json={
                "title": "In Review SOP",
                "slug": "in-review-sop",
                "category_id": cat_id,
                "body_markdown": "# In Review Content",
                "reason_for_change": "Initial creation",
            },
            headers=auth_headers_author,
        )
        art_id = art_resp.json()["id"]

        # Transition to IN_REVIEW
        await client.post(
            f"/api/v1/knowledge/articles/{art_id}/submit-review",
            headers=auth_headers_author,
        )

        # Attempting draft update on IN_REVIEW article must fail with 409
        put_resp = await client.put(
            f"/api/v1/knowledge/articles/{art_id}/draft",
            json={
                "body_markdown": "# Tampered Draft Content",
                "reason_for_change": "Attempting edit while under review",
            },
            headers=auth_headers_editor,
        )
        assert put_resp.status_code == 409
        assert "must be in DRAFT status" in put_resp.json()["detail"]


@pytest.mark.asyncio
async def test_gxp_audit_ledger_created_and_draft_saved_events(
    db_session: AsyncSession,
):
    """
    Validate that GxP audit ledger records CREATED and DRAFT_SAVED events with user ID, timestamp, and details.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
    cat = await _make_category(svc)

    # 1. Create Article -> records CREATED audit log
    article = await svc.create_article(
        title="Consent Verification Protocol",
        slug="consent-verification-protocol",
        category_id=cat.id,
        body_markdown="# Informed Consent\n\nVerify patient signatures.",
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Initial consent procedure authoring",
    )

    # 2. Update Draft -> records DRAFT_SAVED audit log
    await svc.update_draft(
        article_id=article.id,
        body_markdown="# Informed Consent\n\nVerify patient signatures and re-consent flags.",
        actor_user_id=ACTOR_EDITOR,
        reason_for_change="Added re-consent instructions",
    )

    result = await db_session.execute(
        select(KnowledgeArticleAuditLog)
        .where(KnowledgeArticleAuditLog.article_id == article.id)
        .order_by(KnowledgeArticleAuditLog.created_at.asc())
    )
    logs = result.scalars().all()
    assert len(logs) == 2

    # Verify CREATED audit log
    created_log = logs[0]
    assert created_log.action == "CREATED"
    assert created_log.previous_status is None
    assert created_log.new_status == "DRAFT"
    assert created_log.created_by == ACTOR_AUTHOR
    assert created_log.reason_for_change == "Initial consent procedure authoring"
    assert "Consent Verification Protocol" in created_log.details
    assert created_log.created_at is not None

    # Verify DRAFT_SAVED audit log
    draft_log = logs[1]
    assert draft_log.action == "DRAFT_SAVED"
    assert draft_log.previous_status == "DRAFT"
    assert draft_log.new_status == "DRAFT"
    assert draft_log.created_by == ACTOR_EDITOR
    assert draft_log.reason_for_change == "Added re-consent instructions"
    assert ACTOR_EDITOR in draft_log.details
    assert draft_log.created_at is not None


# ---------------------------------------------------------------------------
# Acceptance Criteria 2: Four-Eyes Review & Snapshots (#4326)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_four_eyes_author_cannot_approve(db_session: AsyncSession):
    """
    Validate that an article cannot be approved by its original author (author_user_id).

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
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

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
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

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
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

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
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


@pytest.mark.asyncio
async def test_api_generic_transition_endpoint_supporting_in_review_approved_rejected(
    db_session: AsyncSession,
    auth_headers_author: dict[str, str],
    auth_headers_editor: dict[str, str],
    auth_headers_approver: dict[str, str],
):
    """
    Validate POST /api/v1/knowledge/articles/{id}/transition supporting IN_REVIEW, APPROVED, and REJECTED.

    Tests four-eyes enforcement, mandatory reason_for_change on approval, and rejection workflow.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Setup category & article
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Regulatory Compliance Guidelines",
                "slug": "regulatory-compliance-guidelines",
                "reason_for_change": "Category setup",
            },
            headers=auth_headers_author,
        )
        assert cat_resp.status_code == 201
        cat_id = cat_resp.json()["id"]

        art_resp = await client.post(
            "/api/v1/knowledge/articles",
            json={
                "title": "21 CFR Part 11 Electronic Records SOP",
                "slug": "part-11-electronic-records-sop",
                "category_id": cat_id,
                "body_markdown": "# Electronic Records SOP\n\nMandatory digital signature guidelines.",
                "version_label": "1.0",
                "reason_for_change": "Initial compliance draft",
            },
            headers=auth_headers_author,
        )
        assert art_resp.status_code == 201
        art_id = art_resp.json()["id"]
        transition_path = f"/api/v1/knowledge/articles/{art_id}/transition"

        # 2. Transition DRAFT -> IN_REVIEW via /transition endpoint
        sub_resp = await client.post(
            transition_path,
            json={
                "target_status": "IN_REVIEW",
                "reason_for_change": "Ready for QA review",
            },
            headers=auth_headers_author,
        )
        assert sub_resp.status_code == 200
        assert sub_resp.json()["status"] == "IN_REVIEW"

        # 3. Author attempts approval -> 403 Forbidden (Four-eyes violation)
        author_sig = get_auth_headers_with_sig(
            ACTOR_AUTHOR, ["super_admin"], transition_path
        )
        author_app_resp = await client.post(
            transition_path,
            json={
                "target_status": "APPROVED",
                "reason_for_change": "Author self-approval",
            },
            headers=author_sig,
        )
        assert author_app_resp.status_code == 403

        # 4. Approver attempts approval without reason_for_change -> 422 Unprocessable Entity
        approver_sig = get_auth_headers_with_sig(
            ACTOR_APPROVER, ["super_admin"], transition_path
        )
        no_reason_resp = await client.post(
            transition_path,
            json={
                "target_status": "APPROVED",
                "reason_for_change": "",
            },
            headers=approver_sig,
        )
        assert no_reason_resp.status_code == 422

        # 5. Approver rejects article -> 200 OK (IN_REVIEW -> REJECTED)
        rej_resp = await client.post(
            transition_path,
            json={
                "target_status": "REJECTED",
                "reason_for_change": "Missing section 4.2 password aging policy",
            },
            headers=approver_sig,
        )
        assert rej_resp.status_code == 200
        assert rej_resp.json()["status"] == "REJECTED"

        # 6. Reopen REJECTED -> DRAFT
        reopen_resp = await client.post(
            transition_path,
            json={
                "target_status": "DRAFT",
                "reason_for_change": "Reopening to incorporate password aging policy",
            },
            headers=auth_headers_editor,
        )
        assert reopen_resp.status_code == 200
        assert reopen_resp.json()["status"] == "DRAFT"

        # 7. Editor updates draft and transitions DRAFT -> IN_REVIEW
        await client.put(
            f"/api/v1/knowledge/articles/{art_id}",
            json={
                "body_markdown": "# Electronic Records SOP\n\nMandatory digital signature guidelines & 90-day password aging.",
                "reason_for_change": "Added section 4.2 password aging policy",
            },
            headers=auth_headers_editor,
        )
        await client.post(
            transition_path,
            json={
                "target_status": "IN_REVIEW",
                "reason_for_change": "Resubmitting amended draft for review",
            },
            headers=auth_headers_editor,
        )

        # 8. Editor attempts approval -> 403 Forbidden (Four-eyes violation for last_edited_by)
        editor_sig = get_auth_headers_with_sig(
            ACTOR_EDITOR, ["super_admin"], transition_path
        )
        editor_app_resp = await client.post(
            transition_path,
            json={
                "target_status": "APPROVED",
                "reason_for_change": "Editor self-approval",
            },
            headers=editor_sig,
        )
        assert editor_app_resp.status_code == 403

        # 9. Independent approver approves via /transition -> 200 OK (IN_REVIEW -> APPROVED)
        app_resp = await client.post(
            transition_path,
            json={
                "target_status": "APPROVED",
                "reason_for_change": "Four-eyes peer review verified per 21 CFR Part 11",
            },
            headers=approver_sig,
        )
        assert app_resp.status_code == 200
        assert app_resp.json()["status"] == "APPROVED"
        assert app_resp.json()["approved_by"] == ACTOR_APPROVER

        # 10. Attempting direct system-only transition (e.g. SUPERSEDED) -> 422 Unprocessable Entity
        disallowed_resp = await client.post(
            transition_path,
            json={
                "target_status": "SUPERSEDED",
                "reason_for_change": "Direct client supersede attempt",
            },
            headers=approver_sig,
        )
        assert disallowed_resp.status_code == 422


@pytest.mark.asyncio
async def test_sha256_digest_chain_audit_emission(db_session: AsyncSession):
    """
    Validate that SUBMITTED_FOR_REVIEW, APPROVED, and REJECTED lifecycle events emit to SHA-256 digest chain.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
    cat = await _make_category(svc)
    article = await _make_article(svc, cat.id)

    # Initial state
    assert audit_logger_engine.verify_chain_integrity() is True
    digest_initial = audit_logger_engine.last_digest

    # 1. Submit for review -> emits SUBMITTED_FOR_REVIEW
    article = await svc.submit_for_review(
        article_id=article.id,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Submitting for quality review",
    )
    digest_after_submit = audit_logger_engine.last_digest
    assert digest_after_submit != digest_initial
    assert audit_logger_engine.verify_chain_integrity() is True

    # 2. Reject article -> emits REJECTED
    article = await svc.reject_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Needs typo fixes in introduction",
    )
    digest_after_reject = audit_logger_engine.last_digest
    assert digest_after_reject != digest_after_submit
    assert audit_logger_engine.verify_chain_integrity() is True

    # Reopen to DRAFT and resubmit
    article = await svc.transition(
        article=article,
        target_status=ArticleStatus.DRAFT,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Addressing review comments",
    )
    article = await svc.submit_for_review(
        article_id=article.id,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Resubmitting corrected draft",
    )

    # 3. Approve article -> emits APPROVED
    digest_before_approve = audit_logger_engine.last_digest
    article = await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Verified compliant per SOP-QA-001",
    )
    digest_after_approve = audit_logger_engine.last_digest
    assert digest_after_approve != digest_before_approve
    assert audit_logger_engine.verify_chain_integrity() is True

    # Verify recent records in the audit store
    records = audit_logger_engine._store.fetch_all()
    action_types = [r.action_type for r in records if r.entity_id == article.id]
    assert "SUBMITTED_FOR_REVIEW" in action_types
    assert "REJECTED" in action_types
    assert "APPROVED" in action_types


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
    svc = create_article_service(db_session)
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
    svc = create_article_service(db_session)
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
    svc = create_article_service(db_session)
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
    svc = create_article_service(db_session)
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
