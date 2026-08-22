"""
Unit and integration tests for In-Page Contextual Help Mapping & Dynamic Resolution engine.

Covers:
1. Pure domain route matching, wildcard parsing, and hierarchical specificity ranking.
2. ContextualHelpMapping ORM persistence and GxP audit fields (21 CFR Part 11).
3. Admin CRUD management endpoints (/api/v1/knowledge/contextual-help/mappings).
4. Dynamic Help Resolution endpoint (GET /api/v1/knowledge/contextual-help?route={route}&persona={persona}).
5. Route pattern wildcards (/ecrf/*, /mdr/:studyId/*), persona overrides, and fallback.
6. Resolution ordering: priority ASC, tie-break LENGTH(route_pattern) DESC, recency.
7. Section anchor deep linking (#section-anchor).
8. Multi-article resolution (1 primary spotlight + up to 3 secondary related guides).

Requirements: PRD-SYS-KH-001, ADR-2188
"""

from datetime import UTC, datetime

import httpx
import pytest
from httpx import ASGITransport
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.adapters.repositories import create_article_service
from apps.knowledge.domain.route_matcher import (
    PatternSpecificity,
    classify_pattern,
    matches_persona,
    matches_route,
    normalize_route,
    rank_matching_mappings,
)
from apps.knowledge.infrastructure.models import (
    ContextualHelpMapping,
    KnowledgeArticle,
)
from apps.knowledge.main import app
from packages.testing.security import create_test_auth_headers

# ---------------------------------------------------------------------------
# Test Personas & Auth Fixtures
# ---------------------------------------------------------------------------

ACTOR_ADMIN = "admin@cadence.clinical"  # deid-ignore
ACTOR_CRC = "crc@cadence.clinical"  # deid-ignore
ACTOR_CRA = "cra@cadence.clinical"  # deid-ignore
ACTOR_DM = "dm@cadence.clinical"  # deid-ignore


@pytest.fixture
def auth_headers_admin() -> dict[str, str]:
    """Admin gateway auth headers."""
    return create_test_auth_headers(
        user_id=ACTOR_ADMIN,
        roles=["super_admin"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_crc() -> dict[str, str]:
    """Site CRC gateway auth headers."""
    return create_test_auth_headers(
        user_id=ACTOR_CRC,
        roles=["site_crc", "crc"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_cra() -> dict[str, str]:
    """CRA Monitor gateway auth headers."""
    return create_test_auth_headers(
        user_id=ACTOR_CRA,
        roles=["cra_monitor", "cra"],
        tenant_id="tenant_clinical_01",
    )


async def _create_published_article(
    svc,
    title: str,
    slug: str,
    body: str,
    category_id: str,
    actor_id: str = ACTOR_ADMIN,
) -> KnowledgeArticle:
    """Helper to create and fully publish an article."""
    art = await svc.create_article(
        title=title,
        slug=slug,
        category_id=category_id,
        body_markdown=body,
        actor_user_id=actor_id,
        reason_for_change="Initial creation",
    )
    await svc.submit_for_review(article_id=art.id, actor_user_id=actor_id)
    await svc.approve_article(
        article_id=art.id,
        actor_user_id="reviewer@cadence.clinical",  # deid-ignore
        reason_for_change="Approved for publication",
    )
    return await svc.publish_article(
        article_id=art.id,
        actor_user_id="reviewer@cadence.clinical",  # deid-ignore
        reason_for_change="Published",
    )


# ---------------------------------------------------------------------------
# 1. Pure Domain Route Matcher Unit Tests
# ---------------------------------------------------------------------------


def test_route_normalization():
    """
    Validate route normalization strips whitespace and ensures canonical leading/trailing slashes.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    assert normalize_route("") == "/"
    assert normalize_route("   ") == "/"
    assert normalize_route("ecrf/subjects") == "/ecrf/subjects"
    assert normalize_route("/ecrf/subjects/") == "/ecrf/subjects"
    assert normalize_route("  /mdr/:studyId/*  ") == "/mdr/:studyId/*"
    assert normalize_route("/") == "/"


def test_pattern_classification():
    """
    Validate pattern classification into hierarchical specificity tiers.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    assert classify_pattern("/*") == PatternSpecificity.GLOBAL_WILDCARD
    assert classify_pattern("*") == PatternSpecificity.GLOBAL_WILDCARD
    assert classify_pattern("/ecrf/*") == PatternSpecificity.PREFIX_WILDCARD
    assert (
        classify_pattern("/mdr/:studyId/*") == PatternSpecificity.PARAMETERIZED_WILDCARD
    )
    assert (
        classify_pattern("/mdr/:studyId/designer") == PatternSpecificity.PARAMETERIZED
    )
    assert classify_pattern("/ecrf/subjects") == PatternSpecificity.EXACT
    assert classify_pattern("/") == PatternSpecificity.EXACT


def test_route_pattern_matching():
    """
    Validate pattern matching across exact, parameterized, wildcard, and global routes.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    # 1. Exact match
    assert matches_route("/ecrf/subjects", "/ecrf/subjects")
    assert matches_route("/ecrf/subjects", "/ecrf/subjects/")
    assert not matches_route("/ecrf/subjects", "/ecrf/subjects/SUBJ-001")
    assert not matches_route("/ecrf/subjects", "/ecrf/forms")

    # 2. Parameterized route
    assert matches_route("/mdr/:studyId/designer", "/mdr/STUDY-101/designer")
    assert matches_route("/mdr/:studyId/designer", "/mdr/ONCO-2026/designer")
    assert not matches_route("/mdr/:studyId/designer", "/mdr/STUDY-101/visits")
    assert not matches_route("/mdr/:studyId/designer", "/mdr/STUDY-101/designer/matrix")

    # 3. Multi-parameter route
    assert matches_route(
        "/ecrf/:siteId/subjects/:subjectId",
        "/ecrf/SITE-101/subjects/SUBJ-001",
    )
    assert not matches_route(
        "/ecrf/:siteId/subjects/:subjectId",
        "/ecrf/SITE-101/subjects/SUBJ-001/visits",
    )

    # 4. Prefix wildcard route
    assert matches_route("/ecrf/*", "/ecrf")
    assert matches_route("/ecrf/*", "/ecrf/subjects")
    assert matches_route("/ecrf/*", "/ecrf/SITE-101/subjects/SUBJ-001")
    assert not matches_route("/ecrf/*", "/mdr/studies")

    # 5. Parameterized wildcard route
    assert matches_route("/mdr/:studyId/*", "/mdr/STUDY-101")
    assert matches_route("/mdr/:studyId/*", "/mdr/STUDY-101/designer")
    assert matches_route("/mdr/:studyId/*", "/mdr/STUDY-101/designer/matrix/edit")
    assert not matches_route("/mdr/:studyId/*", "/ecrf/SITE-101")

    # 6. Global wildcard
    assert matches_route("/*", "/any/route/path/here")
    assert matches_route("*", "/dashboard")


def test_persona_matching_and_fallback():
    """
    Validate persona matching rules, role aliases, and universal fallback (persona=None).

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    # Universal mapping (persona=None) matches everything with score 0
    matched, score = matches_persona(None, "site_crc")
    assert matched is True
    assert score == 0

    matched, score = matches_persona("", "cra_monitor")
    assert matched is True
    assert score == 0

    # Specific mapping (persona="site_crc") with matching requested persona -> score 1
    matched, score = matches_persona("site_crc", "site_crc")
    assert matched is True
    assert score == 1

    # Specific mapping with clinical role alias ("crc" requested matches "site_crc")
    matched, score = matches_persona("site_crc", "crc")
    assert matched is True
    assert score == 1

    # Specific mapping with mismatching requested persona -> rejected
    matched, score = matches_persona("data_manager", "site_crc")
    assert matched is False
    assert score == 0

    # Specific mapping when no persona is requested -> rejected
    matched, score = matches_persona("site_crc", None)
    assert matched is False
    assert score == 0


def test_hierarchical_specificity_ranking():
    """
    Validate hierarchical ranking: priority ASC, persona match, pattern specificity, LENGTH DESC, recency.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    t1 = datetime(2026, 8, 20, 10, 0, 0, tzinfo=UTC)
    t2 = datetime(2026, 8, 21, 10, 0, 0, tzinfo=UTC)

    # 1. Priority override: Lower priority integer wins over pattern specificity
    m_high_pri = ContextualHelpMapping(
        id="1",
        route_pattern="/*",
        persona=None,
        article_id="art-1",
        priority=10,
        is_active=True,
        created_at=t1,
    )
    m_low_pri = ContextualHelpMapping(
        id="2",
        route_pattern="/ecrf/subjects",
        persona="site_crc",
        article_id="art-2",
        priority=100,
        is_active=True,
        created_at=t1,
    )
    ranked = rank_matching_mappings(
        [m_low_pri, m_high_pri], "/ecrf/subjects", "site_crc"
    )
    assert [m.id for m in ranked] == ["1", "2"]

    # 2. Equal priority: Persona match beats universal fallback
    m_universal = ContextualHelpMapping(
        id="u1",
        route_pattern="/ecrf/*",
        persona=None,
        article_id="art-u",
        priority=50,
        is_active=True,
        created_at=t1,
    )
    m_persona = ContextualHelpMapping(
        id="p1",
        route_pattern="/ecrf/*",
        persona="site_crc",
        article_id="art-p",
        priority=50,
        is_active=True,
        created_at=t1,
    )
    ranked = rank_matching_mappings(
        [m_universal, m_persona], "/ecrf/subjects", "site_crc"
    )
    assert [m.id for m in ranked] == ["p1", "u1"]

    # 3. Equal priority & persona: Exact pattern beats prefix wildcard
    m_wildcard = ContextualHelpMapping(
        id="w1",
        route_pattern="/ecrf/*",
        persona="site_crc",
        article_id="art-w",
        priority=50,
        is_active=True,
        created_at=t1,
    )
    m_exact = ContextualHelpMapping(
        id="e1",
        route_pattern="/ecrf/subjects",
        persona="site_crc",
        article_id="art-e",
        priority=50,
        is_active=True,
        created_at=t1,
    )
    ranked = rank_matching_mappings([m_wildcard, m_exact], "/ecrf/subjects", "site_crc")
    assert [m.id for m in ranked] == ["e1", "w1"]

    # 4. Equal priority, persona, pattern type: Longer pattern (LENGTH DESC) wins
    m_short_wild = ContextualHelpMapping(
        id="sw",
        route_pattern="/ecrf/*",
        persona="site_crc",
        article_id="art-sw",
        priority=50,
        is_active=True,
        created_at=t1,
    )
    m_long_wild = ContextualHelpMapping(
        id="lw",
        route_pattern="/ecrf/:siteId/subjects/*",
        persona="site_crc",
        article_id="art-lw",
        priority=50,
        is_active=True,
        created_at=t1,
    )
    ranked = rank_matching_mappings(
        [m_short_wild, m_long_wild],
        "/ecrf/SITE-101/subjects/SUBJ-001",
        "site_crc",
    )
    assert [m.id for m in ranked] == ["lw", "sw"]

    # 5. Equal everything: Newer created_at wins
    m_old = ContextualHelpMapping(
        id="old",
        route_pattern="/ecrf/*",
        persona=None,
        article_id="art-old",
        priority=100,
        is_active=True,
        created_at=t1,
    )
    m_new = ContextualHelpMapping(
        id="new",
        route_pattern="/ecrf/*",
        persona=None,
        article_id="art-new",
        priority=100,
        is_active=True,
        created_at=t2,
    )
    ranked = rank_matching_mappings([m_old, m_new], "/ecrf/subjects")
    assert [m.id for m in ranked] == ["new", "old"]


# ---------------------------------------------------------------------------
# 2. Admin Management Endpoints Tests
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_admin_create_and_manage_contextual_help_mappings(
    db_session: AsyncSession,
    auth_headers_admin: dict[str, str],
    auth_headers_crc: dict[str, str],
):
    """
    Validate complete admin CRUD lifecycle for contextual help mappings.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    svc = create_article_service(db_session)
    cat = await svc.create_category(
        name="Clinical Operations",
        slug="clinical-ops",
        description="Clinical ops SOPs",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup",
    )
    art = await _create_published_article(
        svc,
        title="Subject Enrollment SOP",
        slug="subject-enrollment-sop",
        body="## Dynamic Randomization\n\nStep-by-step enrollment.",
        category_id=cat.id,
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Non-admin attempting to create mapping -> 403 Forbidden
        forbidden_resp = await client.post(
            "/api/v1/knowledge/contextual-help/mappings",
            json={
                "route_pattern": "/ecrf/subjects",
                "persona": "site_crc",
                "article_id": art.id,
                "priority": 10,
                "reason_for_change": "Unauthorized create attempt",
            },
            headers=auth_headers_crc,
        )
        assert forbidden_resp.status_code == 403

        # 2. Admin creates mapping
        create_resp = await client.post(
            "/api/v1/knowledge/contextual-help/mappings",
            json={
                "route_pattern": "/ecrf/subjects",
                "persona": "site_crc",
                "article_id": art.id,
                "section_anchor": "#dynamic-randomization",
                "priority": 25,
                "is_active": True,
                "reason_for_change": "Initial mapping for subject enrollment",
            },
            headers=auth_headers_admin,
        )
        assert create_resp.status_code == 201
        mapping_data = create_resp.json()
        mapping_id = mapping_data["id"]
        assert mapping_data["route_pattern"] == "/ecrf/subjects"
        assert mapping_data["persona"] == "site_crc"
        assert mapping_data["article_id"] == art.id
        assert mapping_data["section_anchor"] == "#dynamic-randomization"
        assert mapping_data["priority"] == 25
        assert mapping_data["is_active"] is True
        assert mapping_data["created_by"] == ACTOR_ADMIN
        assert mapping_data["version_index"] == 1

        # 3. Admin gets single mapping by ID
        get_resp = await client.get(
            f"/api/v1/knowledge/contextual-help/mappings/{mapping_id}",
            headers=auth_headers_admin,
        )
        assert get_resp.status_code == 200
        assert get_resp.json()["id"] == mapping_id

        # 4. Admin lists mappings with filter
        list_resp = await client.get(
            "/api/v1/knowledge/contextual-help/mappings?persona=site_crc",
            headers=auth_headers_admin,
        )
        assert list_resp.status_code == 200
        items = list_resp.json()
        assert len(items) == 1
        assert items[0]["id"] == mapping_id

        # 5. Admin updates mapping
        update_resp = await client.put(
            f"/api/v1/knowledge/contextual-help/mappings/{mapping_id}",
            json={
                "priority": 5,
                "section_anchor": "#updated-anchor",
                "reason_for_change": "Elevated priority and fixed anchor",
            },
            headers=auth_headers_admin,
        )
        assert update_resp.status_code == 200
        upd_data = update_resp.json()
        assert upd_data["priority"] == 5
        assert upd_data["section_anchor"] == "#updated-anchor"
        assert upd_data["version_index"] == 2

        # 6. Admin deletes mapping
        del_resp = await client.delete(
            f"/api/v1/knowledge/contextual-help/mappings/{mapping_id}?reason_for_change=Retiring+mapping",
            headers=auth_headers_admin,
        )
        assert del_resp.status_code == 200
        assert del_resp.json()["success"] is True

        # Subsequent GET by ID -> 404
        not_found_resp = await client.get(
            f"/api/v1/knowledge/contextual-help/mappings/{mapping_id}",
            headers=auth_headers_admin,
        )
        assert not_found_resp.status_code == 404


@pytest.mark.asyncio
async def test_admin_create_mapping_with_invalid_article_returns_404(
    db_session: AsyncSession,
    auth_headers_admin: dict[str, str],
):
    """
    Validate that creating a mapping with a non-existent article returns 404.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.post(
            "/api/v1/knowledge/contextual-help/mappings",
            json={
                "route_pattern": "/ecrf/subjects",
                "persona": "site_crc",
                "article_id": "00000000-0000-0000-0000-000000000000",
                "priority": 100,
                "reason_for_change": "Invalid article test",
            },
            headers=auth_headers_admin,
        )
        assert resp.status_code == 404
        assert "does not exist" in resp.json()["detail"]


# ---------------------------------------------------------------------------
# 3. Dynamic Help Resolution Endpoint Tests
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_resolve_contextual_help_exact_match(
    db_session: AsyncSession,
    auth_headers_crc: dict[str, str],
):
    """
    Validate dynamic resolution of an exact route mapping with article body and anchor attached.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    svc = create_article_service(db_session)
    cat = await svc.create_category(
        name="Subject Entry",
        slug="subject-entry",
        description="Subject entry",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup",
    )
    art = await _create_published_article(
        svc,
        title="Subject Demographics Entry",
        slug="subject-demographics",
        body="## Demographics SOP\n\nEnter DOB, gender, and ethnicity.",
        category_id=cat.id,
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/demographics",
        persona="site_crc",
        article_id=art.id,
        section_anchor="#demographics-sop",
        priority=100,
        is_active=True,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup mapping",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/demographics&persona=site_crc",
            headers=auth_headers_crc,
        )
        assert resp.status_code == 200
        data = resp.json()

        assert data["primary_article"] is not None
        assert data["primary_article"]["id"] == art.id
        assert data["primary_article"]["title"] == "Subject Demographics Entry"
        assert "## Demographics SOP" in data["primary_article"]["body_markdown"]
        assert "<h2>Demographics SOP</h2>" in data["primary_article"]["body_html"]
        assert data["section_anchor"] == "#demographics-sop"
        assert data["primary_version"] is not None
        assert data["primary_version"]["version_label"] == "1.0"
        assert data["matched_mapping"]["route_pattern"] == "/ecrf/demographics"


@pytest.mark.asyncio
async def test_resolve_contextual_help_wildcards_and_persona_fallback(
    db_session: AsyncSession,
    auth_headers_admin: dict[str, str],
    auth_headers_crc: dict[str, str],
    auth_headers_cra: dict[str, str],
):
    """
    Validate prefix wildcards (/ecrf/*), parameterized wildcards (/mdr/:studyId/*),
    persona-specific override, and universal fallback (persona=None).

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    svc = create_article_service(db_session)
    cat = await svc.create_category(
        name="Platform Guidelines",
        slug="platform-guidelines",
        description="General guidelines",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup",
    )

    # 1. Universal eCRF Guide (wildcard /ecrf/*, persona=None, priority=100)
    art_generic_ecrf = await _create_published_article(
        svc,
        title="General eCRF Navigation",
        slug="generic-ecrf",
        body="# General eCRF Help",
        category_id=cat.id,
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/*",
        persona=None,
        article_id=art_generic_ecrf.id,
        priority=100,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Universal eCRF mapping",
    )

    # 2. CRC-Specific eCRF Guide (wildcard /ecrf/*, persona="site_crc", priority=100)
    art_crc_ecrf = await _create_published_article(
        svc,
        title="CRC Enrollment & Visit Entry",
        slug="crc-ecrf",
        body="# CRC Specific eCRF Instructions",
        category_id=cat.id,
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/*",
        persona="site_crc",
        article_id=art_crc_ecrf.id,
        priority=100,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="CRC specific eCRF mapping",
    )

    # 3. Parameterized Protocol Designer Guide (/mdr/:studyId/*, persona=None, priority=50)
    art_mdr = await _create_published_article(
        svc,
        title="Protocol Designer SoA Matrix",
        slug="mdr-soa",
        body="# Schedule of Activities Authoring",
        category_id=cat.id,
    )
    await svc.create_help_mapping(
        route_pattern="/mdr/:studyId/*",
        persona=None,
        article_id=art_mdr.id,
        priority=50,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="MDR parameterized mapping",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Scenario A: CRC user visiting /ecrf/SITE-101/subjects/SUBJ-001
        # -> Persona-specific CRC article must win over universal eCRF article
        crc_resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/SITE-101/subjects/SUBJ-001&persona=site_crc",
            headers=auth_headers_crc,
        )
        assert crc_resp.status_code == 200
        crc_data = crc_resp.json()
        assert crc_data["primary_article"]["id"] == art_crc_ecrf.id
        assert crc_data["primary_article"]["title"] == "CRC Enrollment & Visit Entry"
        # Universal article should be in related articles
        related_ids = [a["id"] for a in crc_data["related_articles"]]
        assert art_generic_ecrf.id in related_ids

        # Scenario B: CRA user visiting /ecrf/SITE-101/subjects/SUBJ-001
        # -> No CRA-specific mapping exists, so fallback to universal eCRF article
        cra_resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/SITE-101/subjects/SUBJ-001&persona=cra_monitor",
            headers=auth_headers_cra,
        )
        assert cra_resp.status_code == 200
        cra_data = cra_resp.json()
        assert cra_data["primary_article"]["id"] == art_generic_ecrf.id

        # Scenario C: Visiting parameterized route /mdr/STUDY-2026/designer/matrix
        # -> Matches /mdr/:studyId/*
        mdr_resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/mdr/STUDY-2026/designer/matrix&persona=sponsor_designer",
            headers=auth_headers_admin,
        )
        assert mdr_resp.status_code == 200
        mdr_data = mdr_resp.json()
        assert mdr_data["primary_article"]["id"] == art_mdr.id


@pytest.mark.asyncio
async def test_resolve_contextual_help_tie_breaking_and_related_articles(
    db_session: AsyncSession,
    auth_headers_crc: dict[str, str],
):
    """
    Validate tie-breaking by route pattern length and multi-guide resolution
    (1 primary spotlight + up to 3 secondary related guides).

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    svc = create_article_service(db_session)
    cat = await svc.create_category(
        name="Clinical Systems",
        slug="clinical-systems",
        description="System guides",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup",
    )

    art1 = await _create_published_article(
        svc, "Global Help", "global-help", "# Global", cat.id
    )
    art2 = await _create_published_article(
        svc, "eCRF Root Guide", "ecrf-root", "# eCRF Root", cat.id
    )
    art3 = await _create_published_article(
        svc, "Subjects Module Guide", "subj-guide", "# Subj", cat.id
    )
    art4 = await _create_published_article(
        svc, "Visit 1 Dynamic Form SOP", "v1-sop", "# V1 SOP", cat.id
    )
    art5 = await _create_published_article(
        svc, "Extra Guide", "extra-guide", "# Extra", cat.id
    )

    # Mappings with equal priority (100) and same persona (site_crc)
    await svc.create_help_mapping(
        route_pattern="/*",
        persona="site_crc",
        article_id=art1.id,
        priority=100,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="1",
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/*",
        persona="site_crc",
        article_id=art2.id,
        priority=100,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="2",
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/:siteId/subjects/*",
        persona="site_crc",
        article_id=art3.id,
        priority=100,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="3",
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/:siteId/subjects/:subjectId/visits/v1",
        persona="site_crc",
        article_id=art4.id,
        priority=100,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="4",
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/:siteId/*",
        persona="site_crc",
        article_id=art5.id,
        priority=100,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="5",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Route matches all 5 patterns:
        # Specificity order: art4 (exact match / longest) > art3 > art5 > art2 > art1
        resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/SITE-101/subjects/SUBJ-001/visits/v1&persona=site_crc",
            headers=auth_headers_crc,
        )
        assert resp.status_code == 200
        data = resp.json()

        # Primary spotlight article is art4 (Visit 1 Dynamic Form SOP)
        assert data["primary_article"]["id"] == art4.id

        # Exactly 3 related articles returned (art3, art5, art2)
        related_ids = [a["id"] for a in data["related_articles"]]
        assert len(related_ids) == 3
        assert related_ids[0] == art3.id
        assert related_ids[1] == art5.id
        assert related_ids[2] == art2.id
        assert art1.id not in related_ids  # Cap at 3 related articles


@pytest.mark.asyncio
async def test_resolve_contextual_help_filters_inactive_and_draft_articles(
    db_session: AsyncSession,
    auth_headers_crc: dict[str, str],
):
    """
    Validate that inactive mappings or articles not in PUBLISHED status are never surfaced.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    svc = create_article_service(db_session)
    cat = await svc.create_category(
        name="Drafts Cat",
        slug="drafts-cat",
        description="Drafts",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup",
    )

    # 1. Draft article with mapping
    draft_art = await svc.create_article(
        title="Unpublished Draft Article",
        slug="unpublished-draft",
        category_id=cat.id,
        body_markdown="# Draft",
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Initial",
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/draft",
        persona=None,
        article_id=draft_art.id,
        priority=1,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Draft mapping",
    )

    # 2. Published article with inactive mapping (is_active=False)
    pub_art = await _create_published_article(
        svc,
        title="Published but Inactive Mapping",
        slug="pub-inactive",
        body="# Inactive",
        category_id=cat.id,
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/inactive",
        persona=None,
        article_id=pub_art.id,
        priority=1,
        is_active=False,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Inactive mapping",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Draft article query -> returns null primary article
        resp1 = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/draft",
            headers=auth_headers_crc,
        )
        assert resp1.status_code == 200
        assert resp1.json()["primary_article"] is None

        # Inactive mapping query -> returns null primary article
        resp2 = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/inactive",
            headers=auth_headers_crc,
        )
        assert resp2.status_code == 200
        assert resp2.json()["primary_article"] is None


@pytest.mark.asyncio
async def test_resolve_contextual_help_unmapped_fallback_returns_empty_gracefully(
    db_session: AsyncSession,
    auth_headers_crc: dict[str, str],
):
    """
    Validate that an unmapped route returns a 200 OK empty response structure without failing.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/unmapped/path/here&persona=site_crc",
            headers=auth_headers_crc,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["primary_article"] is None
        assert data["primary_version"] is None
        assert data["matched_mapping"] is None
        assert data["section_anchor"] is None
        assert data["related_articles"] == []
        # Compatibility aliases
        assert data["article"] is None
        assert data["version"] is None


@pytest.mark.asyncio
async def test_resolve_contextual_help_lookup_backward_compatibility(
    db_session: AsyncSession,
    auth_headers_crc: dict[str, str],
):
    """
    Validate that legacy /contextual-help/lookup route functions identically and returns compatible aliases.

    @req:PRD-SYS-KH-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    svc = create_article_service(db_session)
    cat = await svc.create_category(
        name="Legacy Support",
        slug="legacy-support",
        description="Legacy",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup",
    )
    art = await _create_published_article(
        svc,
        title="Legacy Lookup Article",
        slug="legacy-lookup",
        body="# Legacy Lookup Body",
        category_id=cat.id,
    )
    await svc.create_help_mapping(
        route_pattern="/ecrf/legacy",
        persona=None,
        article_id=art.id,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Legacy lookup mapping",
    )

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.get(
            "/api/v1/knowledge/contextual-help/lookup?route=/ecrf/legacy",
            headers=auth_headers_crc,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["article"] is not None
        assert data["article"]["id"] == art.id
        assert data["version"] is not None
        assert data["primary_article"]["id"] == art.id
