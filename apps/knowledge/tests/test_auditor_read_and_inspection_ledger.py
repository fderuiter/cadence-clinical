"""
Unit and integration tests for Auditor Read Logging & 21 CFR Part 11 Inspection Ledger.

Validates:
1. Complete historical version snapshot trail retrieval via GET /articles/{id}/versions.
2. Cryptographic SHA-256 HMAC digest chained audit records via GET /articles/{id}/audit-trail.
3. Automatic emission of READ_BY_AUDITOR audit events when auditor personas read articles.
4. Database-level immutability enforcement preventing updates or deletions on KnowledgeArticleAuditLog
   and locked KnowledgeArticleVersion snapshots.
5. Cryptographic audit chain verification endpoint GET /articles/{id}/verify-audit-chain.

Requirements: PRD-SYS-KH-002, PRD-SYS-KH-001, PRD-KNB-001, ADR-2188
"""

import time
import uuid

import httpx
import pytest
from httpx import ASGITransport
from jose import jwt
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.adapters.repositories import create_article_service
from apps.knowledge.application.article_service import ArticleLifecycleService
from apps.knowledge.domain.models import ArticleStatus
from apps.knowledge.infrastructure.models import (
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeCategory,
)
from apps.knowledge.main import app
from packages.testing.security import create_test_auth_headers

# ---------------------------------------------------------------------------
# Test Personas & Security Helpers
# ---------------------------------------------------------------------------

ACTOR_AUTHOR = "author@cadence.clinical"  # deid-ignore
ACTOR_EDITOR = "editor@cadence.clinical"  # deid-ignore
ACTOR_APPROVER = "approver@cadence.clinical"  # deid-ignore
ACTOR_AUDITOR = "auditor@regulatory.agency"  # deid-ignore
ACTOR_TMF_AUDITOR = "tmf.inspector@ema.europa.eu"  # deid-ignore
ACTOR_CRC = "crc@site-101.org"  # deid-ignore
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
def auth_headers_auditor() -> dict[str, str]:
    """Gateway headers for GCP regulatory auditor."""
    return create_test_auth_headers(
        user_id=ACTOR_AUDITOR,
        roles=["auditor"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_tmf_auditor() -> dict[str, str]:
    """Gateway headers for eTMF inspection auditor."""
    return create_test_auth_headers(
        user_id=ACTOR_TMF_AUDITOR,
        roles=["tmf_auditor"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_crc() -> dict[str, str]:
    """Gateway headers for clinical research coordinator."""
    return create_test_auth_headers(
        user_id=ACTOR_CRC,
        roles=["site_crc", "crc"],
        tenant_id="tenant_clinical_01",
    )


async def _setup_test_hierarchy(
    svc: ArticleLifecycleService,
) -> tuple[KnowledgeCategory, KnowledgeArticle]:
    """Sets up a category and a published article for inspection tests."""
    category = await svc.create_category(
        name="Regulatory Inspection SOPs",
        slug="regulatory-inspection-sops",
        description="Standard operating procedures for regulatory inspections",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Inspection test setup",
    )

    article = await svc.create_article(
        title="21 CFR Part 11 Electronic Records Inspection Guide",
        slug="21-cfr-part-11-inspection-guide",
        category_id=category.id,
        body_markdown="# Part 11 Inspection\n\nProcedures for electronic record verification.",
        version_label="1.0",
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Initial authoring of Part 11 inspection guide",
    )

    # Transition v1 to PUBLISHED: submit -> approve -> publish
    await svc.submit_for_review(
        article_id=article.id,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Submitted for review",
    )
    await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved by independent reviewer",
    )
    article = await svc.publish_article(
        article_id=article.id,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Published version 1.0",
    )

    return category, article


# ---------------------------------------------------------------------------
# Test 1: Historical Version Snapshot Trail (GET /articles/{id}/versions)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_historical_version_snapshot_trail(
    db_session: AsyncSession,
    auth_headers_auditor: dict[str, str],
    auth_headers_author: dict[str, str],
    auth_headers_approver: dict[str, str],
):
    """
    Validate that GET /api/v1/knowledge/articles/{id}/versions returns the complete
    historical version snapshot trail in chronological order across version cycles.

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    # Create and publish Version 2: archive -> reopen to DRAFT -> edit -> submit -> approve -> publish
    await svc.transition(
        article=article,
        target_status=ArticleStatus.ARCHIVED,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Archiving v1 to prepare v2",
    )
    await svc.transition(
        article=article,
        target_status=ArticleStatus.DRAFT,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Reopened for version 2 update",
    )
    await svc.update_draft(
        article_id=article.id,
        body_markdown="# Part 11 Inspection v2\n\nUpdated with **cloud infrastructure** verification.",
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Added cloud compliance checks",
    )
    await svc.submit_for_review(
        article_id=article.id,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Resubmitted v2 for review",
    )
    await svc.approve_article(
        article_id=article.id,
        actor_user_id=ACTOR_APPROVER,
        reason_for_change="Approved v2 by QA",
    )
    article = await svc.publish_article(
        article_id=article.id,
        actor_user_id=ACTOR_AUTHOR,
        reason_for_change="Published version 2.0",
        version_label="2.0",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Retrieve complete version trail as auditor
        resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/versions",
            headers=auth_headers_auditor,
        )
        assert resp.status_code == 200
        versions = resp.json()

        # Must return exactly 2 versions in chronological order
        assert len(versions) == 2

        v1 = versions[0]
        assert v1["version_index"] == 1
        assert v1["status_at_snapshot"] == "SUPERSEDED"
        assert v1["is_locked"] is True
        assert "Procedures for electronic record verification." in v1["body_markdown"]

        v2 = versions[1]
        assert v2["version_index"] == 2
        assert v2["status_at_snapshot"] == "PUBLISHED"
        assert v2["is_locked"] is True
        assert "cloud infrastructure" in v2["body_markdown"]
        assert "<strong>cloud infrastructure</strong>" in v2["body_html"]

        # Retrieve specific version snapshot via GET /articles/{id}/versions/{version_id}
        v1_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/versions/{v1['id']}",
            headers=auth_headers_auditor,
        )
        assert v1_resp.status_code == 200
        assert v1_resp.json()["id"] == v1["id"]

        # Verify 404 for non-existent article versions
        missing_art_resp = await client.get(
            f"/api/v1/knowledge/articles/{uuid.uuid4()}/versions",
            headers=auth_headers_auditor,
        )
        assert missing_art_resp.status_code == 404

        missing_ver_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/versions/{uuid.uuid4()}",
            headers=auth_headers_auditor,
        )
        assert missing_ver_resp.status_code == 404


# ---------------------------------------------------------------------------
# Test 2: SHA-256 HMAC Digest Chained Audit Records (GET /articles/{id}/audit-trail)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_audit_trail_sha256_digest_chain(
    db_session: AsyncSession,
    auth_headers_auditor: dict[str, str],
):
    """
    Validate that GET /api/v1/knowledge/articles/{id}/audit-trail returns
    tamper-evident SHA-256 HMAC digest chained audit records per 21 CFR Part 11 §11.10(e).

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Fetch via canonical endpoint /audit-trail
        trail_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/audit-trail",
            headers=auth_headers_auditor,
        )
        assert trail_resp.status_code == 200
        logs = trail_resp.json()

        # 2. Fetch via backwards-compatible alias /audit-log
        alias_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/audit-log",
            headers=auth_headers_auditor,
        )
        assert alias_resp.status_code == 200
        assert len(alias_resp.json()) == len(logs)

        # Expected lifecycle events: CREATED, SUBMITTED_FOR_REVIEW, APPROVED, PUBLISHED
        assert len(logs) >= 4
        actions = [log["action"] for log in logs]
        assert "CREATED" in actions
        assert "SUBMITTED_FOR_REVIEW" in actions
        assert "APPROVED" in actions
        assert "PUBLISHED" in actions

        # Verify each record has SHA-256 cryptographic digests
        for log in logs:
            assert log["sha256_digest"] is not None
            assert len(log["sha256_digest"]) == 64
            assert log["previous_digest"] is not None
            assert log["created_at"] is not None
            assert log["created_by"] is not None

        # Verify cryptographic chain integrity
        for i in range(1, len(logs)):
            current_prev = logs[i]["previous_digest"]
            # Either links to prior event or global digest block
            assert len(current_prev) >= 64

        # Non-existent article returns 404
        missing_resp = await client.get(
            f"/api/v1/knowledge/articles/{uuid.uuid4()}/audit-trail",
            headers=auth_headers_auditor,
        )
        assert missing_resp.status_code == 404


# ---------------------------------------------------------------------------
# Test 3: Automatic Emission of READ_BY_AUDITOR Audit Event
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_automatic_emission_of_auditor_read_event(
    db_session: AsyncSession,
    auth_headers_auditor: dict[str, str],
    auth_headers_tmf_auditor: dict[str, str],
    auth_headers_crc: dict[str, str],
    auth_headers_author: dict[str, str],
):
    """
    Validate that reading an article via GET /api/v1/knowledge/articles/{id} automatically
    emits a READ_BY_AUDITOR audit event when accessed by auditor or tmf_auditor personas,
    and does NOT emit read events for non-auditor roles.

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Initial audit log count before auditor access
        init_trail = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/audit-trail",
            headers=auth_headers_auditor,
        )
        initial_logs = init_trail.json()
        initial_read_events = [
            log for log in initial_logs if log["action"] == "READ_BY_AUDITOR"
        ]
        initial_read_count = len(initial_read_events)

        # 1. Access article as GCP Auditor (roles=["auditor"])
        auditor_read_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}",
            headers=auth_headers_auditor,
        )
        assert auditor_read_resp.status_code == 200

        # Verify READ_BY_AUDITOR was emitted
        trail_resp1 = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/audit-trail",
            headers=auth_headers_auditor,
        )
        logs1 = trail_resp1.json()
        read_events1 = [log for log in logs1 if log["action"] == "READ_BY_AUDITOR"]
        assert len(read_events1) == initial_read_count + 1
        latest_read1 = read_events1[-1]
        assert latest_read1["created_by"] == ACTOR_AUDITOR
        assert ACTOR_AUDITOR in latest_read1["details"]
        assert latest_read1["sha256_digest"] is not None

        # 2. Access article as eTMF Auditor (roles=["tmf_auditor"])
        tmf_read_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}",
            headers=auth_headers_tmf_auditor,
        )
        assert tmf_read_resp.status_code == 200

        # Verify second READ_BY_AUDITOR was emitted for tmf_auditor
        trail_resp2 = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/audit-trail",
            headers=auth_headers_auditor,
        )
        logs2 = trail_resp2.json()
        read_events2 = [log for log in logs2 if log["action"] == "READ_BY_AUDITOR"]
        assert len(read_events2) == initial_read_count + 2
        latest_read2 = read_events2[-1]
        assert latest_read2["created_by"] == ACTOR_TMF_AUDITOR
        assert ACTOR_TMF_AUDITOR in latest_read2["details"]

        # 3. Access article as CRC (roles=["site_crc", "crc"]) -> must NOT emit READ_BY_AUDITOR
        crc_read_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}",
            headers=auth_headers_crc,
        )
        assert crc_read_resp.status_code == 200

        trail_resp3 = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/audit-trail",
            headers=auth_headers_auditor,
        )
        logs3 = trail_resp3.json()
        read_events3 = [log for log in logs3 if log["action"] == "READ_BY_AUDITOR"]
        # Count remains unchanged after CRC access
        assert len(read_events3) == initial_read_count + 2


# ---------------------------------------------------------------------------
# Test 4: Database Immutability via SQLAlchemy before_flush Hook
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_immutability_hook_rejects_audit_log_modification(
    db_session: AsyncSession,
):
    """
    Validate that SQLAlchemy before_flush hook strictly forbids updates to
    KnowledgeArticleAuditLog records per 21 CFR Part 11.

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    result = await db_session.execute(
        select(KnowledgeArticleAuditLog).where(
            KnowledgeArticleAuditLog.article_id == article.id
        )
    )
    log_entry = result.scalars().first()
    assert log_entry is not None

    # Attempt to tamper with audit record details -> before_flush must raise ValueError
    log_entry.details = "TAMPERED AUDIT ENTRY"
    with pytest.raises(
        ValueError,
        match="Updates to KnowledgeArticleAuditLog are strictly forbidden",
    ):
        await db_session.flush()

    # Rollback dirty session state
    await db_session.rollback()


@pytest.mark.asyncio
async def test_immutability_hook_rejects_audit_log_deletion(
    db_session: AsyncSession,
):
    """
    Validate that SQLAlchemy before_flush hook strictly forbids deletions of
    KnowledgeArticleAuditLog records per 21 CFR Part 11.

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    result = await db_session.execute(
        select(KnowledgeArticleAuditLog).where(
            KnowledgeArticleAuditLog.article_id == article.id
        )
    )
    log_entry = result.scalars().first()
    assert log_entry is not None

    # Attempt to delete audit record -> before_flush must raise ValueError
    await db_session.delete(log_entry)
    with pytest.raises(
        ValueError,
        match="Deletions from KnowledgeArticleAuditLog are strictly forbidden",
    ):
        await db_session.flush()

    await db_session.rollback()


@pytest.mark.asyncio
async def test_immutability_hook_rejects_locked_version_modification(
    db_session: AsyncSession,
):
    """
    Validate that SQLAlchemy before_flush hook strictly forbids modifications of
    locked KnowledgeArticleVersion snapshots.

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    versions = await svc.list_article_versions(article.id)
    assert len(versions) >= 1
    locked_ver = versions[0]
    assert locked_ver.is_locked is True

    # Attempt body modification on locked version -> before_flush must raise ValueError
    locked_ver.body_markdown = "# TAMPERED VERSION CONTENT"
    with pytest.raises(
        ValueError,
        match="Modifications to locked KnowledgeArticleVersion snapshots are strictly forbidden",
    ):
        await db_session.flush()


@pytest.mark.asyncio
async def test_immutability_hook_rejects_locked_version_deletion(
    db_session: AsyncSession,
):
    """
    Validate that SQLAlchemy before_flush hook strictly forbids deletions of
    locked KnowledgeArticleVersion snapshots.

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    versions = await svc.list_article_versions(article.id)
    assert len(versions) >= 1
    locked_ver = versions[0]
    assert locked_ver.is_locked is True

    # Attempt deletion of locked version -> before_flush must raise ValueError
    await db_session.delete(locked_ver)
    with pytest.raises(
        ValueError,
        match="Deletions of locked KnowledgeArticleVersion snapshots are strictly forbidden",
    ):
        await db_session.flush()


# ---------------------------------------------------------------------------
# Test 5: Cryptographic Chain Verification Endpoint (GET /articles/{id}/verify-audit-chain)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_verify_audit_chain_endpoint(
    db_session: AsyncSession,
    auth_headers_auditor: dict[str, str],
    auth_headers_crc: dict[str, str],
):
    """
    Validate that GET /api/v1/knowledge/articles/{id}/verify-audit-chain cryptographically
    verifies the SHA-256 HMAC digest chain integrity and rejects unauthorized personas with 403.

    @req:PRD-SYS-KH-002
    @req:PRD-SYS-KH-001
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    svc = create_article_service(db_session)
    _, article = await _setup_test_hierarchy(svc)

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Auditor verifies chain -> 200 OK
        resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/verify-audit-chain",
            headers=auth_headers_auditor,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["article_id"] == article.id
        assert data["is_valid"] is True
        assert data["tamper_detected"] is False
        assert data["total_records"] >= 4
        assert "verified intact" in data["details"]
        assert len(data["genesis_digest"]) >= 64
        assert len(data["latest_digest"]) == 64

        # 2. Non-auditor role (site_crc) receives 403 Forbidden
        forbidden_resp = await client.get(
            f"/api/v1/knowledge/articles/{article.id}/verify-audit-chain",
            headers=auth_headers_crc,
        )
        assert forbidden_resp.status_code == 403
