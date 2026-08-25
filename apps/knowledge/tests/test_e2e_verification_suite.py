"""
End-to-End Multi-Persona GxP Verification Suite for Cadence Knowledge & Support Hub.

Validates:
1. Complete Multi-Persona Clinical Lifecycle: Category hierarchy creation, working draft authoring,
   four-eyes review controls, version snapshot immutability, and auto-supersede on publication.
2. In-Page Contextual Help Dynamic Resolution: Exact route, parameterized, wildcard matching,
   hierarchical specificity scoring, and unmapped route graceful fallback.
3. Grounded Protocol & SOP Ingestion: Section-aware chunk extraction, dense vector embeddings,
   semantic cosine similarity search, and verbatim citation marker generation.
4. 21 CFR Part 11 Regulatory Compliance: Auditor read logging (READ_BY_AUDITOR), SHA-256 HMAC
   audit trail verification, and database-level immutability enforcement.
5. Role-Based Access Control: Persona-level category scoping, search visibility filtering, and
   authorization gate protection on administrative endpoints.

Requirements: PRD-SYS-KH-001, PRD-SYS-KH-002, PRD-KNB-001, PRD-KNB-002, PRD-TCK-005, PRD-SYS-050, ADR-2188, ADR-2192
"""

from __future__ import annotations

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
from apps.knowledge.infrastructure.models import (
    KnowledgeArticleAuditLog,
)
from apps.knowledge.main import app
from packages.testing.security import create_test_auth_headers

# ---------------------------------------------------------------------------
# Test Personas & Security Helpers
# ---------------------------------------------------------------------------

GATEWAY_SECRET = "valid-test-gateway-secret-key-2026"  # pragma: allowlist secret

PERSONA_SUPER_ADMIN = "admin@cadence.clinical"  # deid-ignore
PERSONA_SPONSOR_DESIGNER = "designer@sponsor.pharma"  # deid-ignore
PERSONA_SITE_CRC = "crc@site-101.hospital.org"  # deid-ignore
PERSONA_CRA_MONITOR = "cra@cro-partner.com"  # deid-ignore
PERSONA_DATA_MANAGER = "dm@sponsor.pharma"  # deid-ignore
PERSONA_AUDITOR = "inspector@fda.hhs.gov"  # deid-ignore


def get_auth_headers(
    user_id: str, roles: list[str], path: str | None = None
) -> dict[str, str]:
    """Generates authentic gateway headers with an optional single-use eSignature token."""
    sig_token = None
    if path:
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


# ---------------------------------------------------------------------------
# End-to-End Multi-Persona Verification Test Suite
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_e2e_golden_path_article_lifecycle_and_four_eyes(
    db_session: AsyncSession,
) -> None:
    """
    End-to-End Golden Path: Category taxonomy creation, article drafting, four-eyes review
    enforcement, version locking, publication, fast lookups, and second-version auto-supersede.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    @req:PRD-KNB-002
    @req:PRD-SYS-050
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    transport = ASGITransport(app=app)

    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        # Step 1: super_admin creates Category Hierarchy
        admin_headers = get_auth_headers(
            PERSONA_SUPER_ADMIN, ["super_admin"], "/api/v1/knowledge/categories"
        )
        cat_payload = {
            "name": "Clinical Monitoring SOPs",
            "slug": "clinical-monitoring-sops",
            "description": "Standard operating procedures for CRA monitoring and SDV.",
            "persona_visibility": "cra_monitor,site_crc,data_manager",
            "reason_for_change": "Initial category taxonomy creation for monitoring SOPs",
        }
        resp = await client.post(
            "/api/v1/knowledge/categories", json=cat_payload, headers=admin_headers
        )
        assert resp.status_code == 201, resp.text
        category = resp.json()
        category_id = category["id"]

        # Step 2: super_admin creates Knowledge Article Draft
        art_payload = {
            "title": "Source Data Verification (SDV) Standard Operating Procedure",
            "slug": "sdv-sop-001",
            "category_id": category_id,
            "body_markdown": "# SDV SOP\n\n## 1. Scope\n\nSource Data Verification guidelines for Phase III trials.",
            "version_label": "v1.0-draft",
            "tags": ["SDV", "Monitoring", "SOP", "GxP"],
            "reason_for_change": "Initial authoring of SDV SOP draft",
        }
        art_headers = get_auth_headers(
            PERSONA_SUPER_ADMIN, ["super_admin"], "/api/v1/knowledge/articles"
        )
        resp = await client.post(
            "/api/v1/knowledge/articles", json=art_payload, headers=art_headers
        )
        assert resp.status_code == 201, resp.text
        article = resp.json()
        article_id = article["id"]
        assert article["status"] == "DRAFT"
        assert article["version_index"] == 1

        # Step 3: super_admin updates working draft with expanded guidelines
        draft_update_payload = {
            "title": "Source Data Verification (SDV) Standard Operating Procedure",
            "body_markdown": "# SDV SOP v1.0\n\n## 1. Scope\n\nComprehensive guidelines.\n\n## 2. 100% SDV Triggers\n\nInformed consent and primary efficacy endpoints.",
            "tags": ["SDV", "Monitoring", "SOP", "GxP", "eCRF"],
            "reason_for_change": "Add section 2 regarding 100% SDV triggers",
        }
        update_headers = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}",
        )
        resp = await client.put(
            f"/api/v1/knowledge/articles/{article_id}",
            json=draft_update_payload,
            headers=update_headers,
        )
        assert resp.status_code == 200, resp.text
        updated_art = resp.json()
        assert "100% SDV Triggers" in updated_art["body_markdown"]
        assert "<h2" in updated_art["body_html"]

        # Step 4: super_admin submits working draft for peer review
        submit_headers = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/submit-review",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/submit-review",
            json={
                "reason_for_change": "Draft complete and ready for formal medical monitoring review"
            },
            headers=submit_headers,
        )
        assert resp.status_code == 200, resp.text
        in_review_art = resp.json()
        assert in_review_art["status"] == "IN_REVIEW"

        # Step 5: Four-Eyes Principle Enforcement — author (super_admin) cannot approve own article
        author_approve_headers = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/approve",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/approve",
            json={
                "reason_for_change": "Author self-approval attempt should be rejected"
            },
            headers=author_approve_headers,
        )
        assert resp.status_code == 403, (
            "Four-eyes principle must block the author/editor from approving"
        )

        # Step 6: Independent reviewer (sponsor_designer) approves the article
        reviewer_approve_headers = get_auth_headers(
            PERSONA_SPONSOR_DESIGNER,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/approve",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/approve",
            json={
                "reason_for_change": "Reviewed and validated SDV triggers against protocol risk assessment"
            },
            headers=reviewer_approve_headers,
        )
        assert resp.status_code == 200, resp.text
        approved_art = resp.json()
        assert approved_art["status"] == "APPROVED"
        assert approved_art["approved_by"] == PERSONA_SPONSOR_DESIGNER

        # Step 7: super_admin publishes the approved article (APPROVED -> PUBLISHED)
        publish_headers = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/publish",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/publish",
            json={
                "reason_for_change": "Publish v1.0 for site monitors and clinical teams",
                "version_label": "v1.0",
            },
            headers=publish_headers,
        )
        assert resp.status_code == 200, resp.text
        pub_art = resp.json()
        assert pub_art["status"] == "PUBLISHED"
        assert pub_art["current_published_version_id"] is not None
        v1_published_version_id = pub_art["current_published_version_id"]

        # Step 8: Fast single-query published article lookup by slug and ID
        read_headers = get_auth_headers(PERSONA_SITE_CRC, ["site_crc"])
        resp = await client.get(
            "/api/v1/knowledge/articles/sdv-sop-001", headers=read_headers
        )
        assert resp.status_code == 200, resp.text
        fetched_art = resp.json()
        assert fetched_art["id"] == article_id
        assert fetched_art["status"] == "PUBLISHED"
        assert "<h2" in fetched_art["body_html"]

        # Step 9: Version N+1 amendment and Auto-Supersede workflow
        # Cycle through PUBLISHED -> ARCHIVED -> DRAFT to begin Version 2
        transition_headers_archive = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/transition",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/transition",
            json={
                "target_status": "ARCHIVED",
                "reason_for_change": "Archiving v1.0 to initiate Version 2 revision",
            },
            headers=transition_headers_archive,
        )
        assert resp.status_code == 200, resp.text

        transition_headers_draft = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/transition",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/transition",
            json={
                "target_status": "DRAFT",
                "reason_for_change": "Initiate Version 2 draft for remote monitoring amendments",
                "version_label": "v2.0-draft",
            },
            headers=transition_headers_draft,
        )
        assert resp.status_code == 200, resp.text
        v2_draft = resp.json()
        assert v2_draft["status"] == "DRAFT"
        assert v2_draft["version_index"] == 2

        # Update Version 2 draft content
        update_headers_v2 = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}",
        )
        resp = await client.put(
            f"/api/v1/knowledge/articles/{article_id}",
            json={
                "title": "Source Data Verification (SDV) Standard Operating Procedure - Rev 2",
                "body_markdown": "# SDV SOP v2.0\n\n## 1. Scope\n\nUpdated for decentralized and hybrid trials.\n\n## 2. Remote SDV\n\nSecure VPN portal guidelines.",
                "tags": ["SDV", "Monitoring", "SOP", "Remote", "GxP"],
                "reason_for_change": "Include decentralized and remote SDV specifications",
            },
            headers=update_headers_v2,
        )
        assert resp.status_code == 200, resp.text

        # Submit Version 2 for review
        submit_headers_v2 = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/submit-review",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/submit-review",
            json={"reason_for_change": "Version 2 draft ready for QA sign-off"},
            headers=submit_headers_v2,
        )
        assert resp.status_code == 200, resp.text

        # Independent approval of Version 2 (fresh single-use eSignature token)
        reviewer_approve_headers_v2 = get_auth_headers(
            PERSONA_SPONSOR_DESIGNER,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/approve",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/approve",
            json={
                "reason_for_change": "Approved remote monitoring protocol integration"
            },
            headers=reviewer_approve_headers_v2,
        )
        assert resp.status_code == 200, resp.text

        # Publish Version 2 -> should auto-supersede Version 1 (fresh single-use eSignature token)
        publish_headers_v2 = get_auth_headers(
            PERSONA_SUPER_ADMIN,
            ["super_admin"],
            f"/api/v1/knowledge/articles/{article_id}/publish",
        )
        resp = await client.post(
            f"/api/v1/knowledge/articles/{article_id}/publish",
            json={
                "reason_for_change": "Promote Version 2.0 to authoritative active status",
                "version_label": "v2.0",
            },
            headers=publish_headers_v2,
        )
        assert resp.status_code == 200, resp.text
        v2_pub = resp.json()
        assert v2_pub["status"] == "PUBLISHED"
        assert v2_pub["current_published_version_id"] != v1_published_version_id

        # Verify historical version snapshots
        resp = await client.get(
            f"/api/v1/knowledge/articles/{article_id}/versions",
            headers=admin_headers,
        )
        assert resp.status_code == 200, resp.text
        versions = resp.json()
        assert len(versions) == 2
        v1_snap = next(v for v in versions if v["version_index"] == 1)
        v2_snap = next(v for v in versions if v["version_index"] == 2)
        assert v1_snap["status_at_snapshot"] == "SUPERSEDED"
        assert v2_snap["status_at_snapshot"] == "PUBLISHED"


@pytest.mark.asyncio
async def test_e2e_contextual_help_dynamic_resolution_and_fallback(
    db_session: AsyncSession,
) -> None:
    """
    End-to-End Contextual Help: Route pattern matching (/ecrf/*, /mdr/:studyId/*),
    persona filtering, hierarchical specificity ranking, and unmapped fallback.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    transport = ASGITransport(app=app)

    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        admin_headers = get_auth_headers(
            PERSONA_SUPER_ADMIN, ["super_admin"], "/api/v1/knowledge"
        )

        # 1. Create category
        cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Clinical Operations In-App Guidance",
                "slug": "clin-ops-guidance",
                "description": "In-app SOPs for eCRF and MDR screens",
                "persona_visibility": "site_crc,cra_monitor,sponsor_designer",
                "reason_for_change": "Category for in-page contextual help articles",
            },
            headers=admin_headers,
        )
        assert cat_resp.status_code == 201, cat_resp.text
        cat_id = cat_resp.json()["id"]

        # 2. Create and publish 3 distinct guidance articles
        svc = create_article_service(db_session)

        # Article A: General eCRF Data Entry SOP (Wildcard /ecrf/* for site_crc)
        art_a = await svc.create_article(
            title="eCRF Subject Visit Data Entry SOP",
            slug="ecrf-data-entry-sop",
            category_id=cat_id,
            body_markdown="# eCRF Data Entry\n\nGuidelines for subject enrollment and form completion.",
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Create eCRF SOP",
        )
        art_a = await svc.submit_for_review(
            article_id=art_a.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Submit for review",
        )
        art_a = await svc.approve_article(
            article_id=art_a.id,
            actor_user_id=PERSONA_SPONSOR_DESIGNER,
            reason_for_change="Approved by Designer",
        )
        art_a = await svc.publish_article(
            article_id=art_a.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Publish eCRF SOP",
        )

        # Article B: Query Management SOP (Exact match /ecrf/site-101/queries for cra_monitor)
        art_b = await svc.create_article(
            title="Query Discrepancy Resolution & CRA Monitoring SOP",
            slug="query-resolution-sop",
            category_id=cat_id,
            body_markdown="# Query Resolution\n\nHow CRAs issue and close clinical queries.",
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Create Query SOP",
        )
        art_b = await svc.submit_for_review(
            article_id=art_b.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Submit for review",
        )
        art_b = await svc.approve_article(
            article_id=art_b.id,
            actor_user_id=PERSONA_SPONSOR_DESIGNER,
            reason_for_change="Approved by Designer",
        )
        art_b = await svc.publish_article(
            article_id=art_b.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Publish Query SOP",
        )

        # Article C: MDR Study Authoring SOP (Parameterized /mdr/:studyId/*)
        art_c = await svc.create_article(
            title="MDR Protocol & CRF Designer Guide",
            slug="mdr-designer-guide",
            category_id=cat_id,
            body_markdown="# MDR Designer Guide\n\nProtocol and CRF authoring instructions.",
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Create MDR Guide",
        )
        art_c = await svc.submit_for_review(
            article_id=art_c.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Submit for review",
        )
        art_c = await svc.approve_article(
            article_id=art_c.id,
            actor_user_id=PERSONA_SPONSOR_DESIGNER,
            reason_for_change="Approved by Designer",
        )
        art_c = await svc.publish_article(
            article_id=art_c.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Publish MDR Guide",
        )

        # 3. super_admin configures Contextual Help Mappings
        # Mapping 1: Wildcard /ecrf/* -> eCRF Data Entry SOP (priority 10, persona=site_crc)
        resp1 = await client.post(
            "/api/v1/knowledge/contextual-help/mappings",
            json={
                "route_pattern": "/ecrf/*",
                "persona": "site_crc",
                "article_id": art_a.id,
                "priority": 10,
                "section_anchor": "#ecrf-data-entry",
                "is_active": True,
                "reason_for_change": "Map wildcard /ecrf/* to general data entry SOP",
            },
            headers=admin_headers,
        )
        assert resp1.status_code == 201

        # Mapping 2: Specific route /ecrf/:siteId/queries -> Query Resolution SOP (priority 5, persona=cra_monitor)
        resp2 = await client.post(
            "/api/v1/knowledge/contextual-help/mappings",
            json={
                "route_pattern": "/ecrf/:siteId/queries",
                "persona": "cra_monitor",
                "article_id": art_b.id,
                "priority": 5,
                "section_anchor": "#query-resolution",
                "is_active": True,
                "reason_for_change": "Map queries route to query resolution SOP",
            },
            headers=admin_headers,
        )
        assert resp2.status_code == 201

        # Mapping 3: Parameterized /mdr/:studyId/* -> MDR Guide (priority 15, universal persona=None)
        resp3 = await client.post(
            "/api/v1/knowledge/contextual-help/mappings",
            json={
                "route_pattern": "/mdr/:studyId/*",
                "persona": None,
                "article_id": art_c.id,
                "priority": 15,
                "section_anchor": "#mdr-designer-guide",
                "is_active": True,
                "reason_for_change": "Map MDR screens to MDR designer guide",
            },
            headers=admin_headers,
        )
        assert resp3.status_code == 201

        # 4. Test dynamic resolution for site_crc navigating /ecrf/site-101/subjects
        crc_headers = get_auth_headers(PERSONA_SITE_CRC, ["site_crc"])
        resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/site-101/subjects&persona=site_crc",
            headers=crc_headers,
        )
        assert resp.status_code == 200, resp.text
        res = resp.json()
        assert res["primary_article"] is not None
        assert res["primary_article"]["id"] == art_a.id
        assert res["section_anchor"] == "#ecrf-data-entry"
        assert "<h1" in res["primary_article"]["body_html"]

        # 5. Test dynamic resolution for cra_monitor navigating /ecrf/site-101/queries
        cra_headers = get_auth_headers(PERSONA_CRA_MONITOR, ["cra_monitor"])
        resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/ecrf/site-101/queries&persona=cra_monitor",
            headers=cra_headers,
        )
        assert resp.status_code == 200, resp.text
        res = resp.json()
        assert res["primary_article"] is not None
        assert res["primary_article"]["id"] == art_b.id
        assert res["section_anchor"] == "#query-resolution"

        # 6. Test dynamic resolution for sponsor_designer navigating /mdr/CDNC-301/crf-builder
        designer_headers = get_auth_headers(
            PERSONA_SPONSOR_DESIGNER, ["sponsor_designer"]
        )
        resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/mdr/CDNC-301/crf-builder&persona=sponsor_designer",
            headers=designer_headers,
        )
        assert resp.status_code == 200, resp.text
        res = resp.json()
        assert res["primary_article"] is not None
        assert res["primary_article"]["id"] == art_c.id

        # 7. Test unmapped route fallback (/analytics/dashboard)
        resp = await client.get(
            "/api/v1/knowledge/contextual-help?route=/analytics/dashboard&persona=data_manager",
            headers=admin_headers,
        )
        assert resp.status_code == 200, resp.text
        res = resp.json()
        assert res["primary_article"] is None
        assert res["matched_mapping"] is None
        assert res["related_articles"] == []


@pytest.mark.asyncio
async def test_e2e_grounded_protocol_ingestion_and_vector_search(
    db_session: AsyncSession,
) -> None:
    """
    End-to-End Grounded Protocol & SOP Ingestion: Multi-page document parsing,
    section coordinate extraction, dense vector semantic embedding, cosine similarity search,
    and verbatim citation marker generation.

    @req:PRD-TCK-005
    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    transport = ASGITransport(app=app)

    # Sample protocol document with explicit section coordinates
    protocol_text = """
--- Page 1 ---
Section 1.0 Protocol Title and Study Synopsis
This Phase III randomized, double-blind trial investigates Cadence-2026 for advanced solid tumors.
The study will evaluate safety, tolerability, and overall response rate.

--- Page 2 ---
Section 4.1 Patient Inclusion Criteria
Patients must meet all of the following criteria:
1. Age >= 18 years at the time of signing informed consent.
2. Histologically confirmed solid tumor refractory to standard therapies.
3. ECOG Performance Status of 0 or 1.
4. Adequate bone marrow and organ function.

--- Page 3 ---
Section 6.2 Serious Adverse Event (SAE) Reporting
All SAEs must be reported to the Sponsor Medical Monitor within 24 hours of site awareness.
Initial reports must include subject ID, onset date, severity, causality, and outcome.
Follow-up information must be submitted within 7 calendar days.
"""

    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        designer_headers = get_auth_headers(
            PERSONA_SPONSOR_DESIGNER, ["sponsor_designer"]
        )

        # 1. Ingest Protocol Document
        ingest_payload = {
            "study_id": "STUDY-ONCOLOGY-301",
            "protocol_version": "v3.0",
            "raw_text": protocol_text,
            "filename": "protocol_oncology_v3.0.txt",
            "document_type": "PROTOCOL",
            "is_approved": True,
            "reason_for_change": "Ingest approved oncology protocol v3.0 for RAG support triage",
        }
        resp = await client.post(
            "/api/v1/knowledge/protocols/ingest",
            json=ingest_payload,
            headers=designer_headers,
        )
        assert resp.status_code == 201, resp.text
        chunks = resp.json()
        assert len(chunks) >= 3

        # Verify citation markers
        inclusion_chunk = next(
            (c for c in chunks if c.get("section_number") == "4.1"), None
        )
        assert inclusion_chunk is not None
        assert "Inclusion Criteria" in inclusion_chunk["section_title"]
        assert (
            inclusion_chunk["citation_marker"] == "[Protocol v3.0, Section 4.1, Page 2]"
        )

        sae_chunk = next((c for c in chunks if c.get("section_number") == "6.2"), None)
        assert sae_chunk is not None
        assert sae_chunk["citation_marker"] == "[Protocol v3.0, Section 6.2, Page 3]"

        # 2. Clinical CRC queries semantic search for inclusion criteria
        crc_headers = get_auth_headers(PERSONA_SITE_CRC, ["site_crc"])
        search_payload = {
            "query": "What are the patient inclusion criteria for age and ECOG performance status?",
            "study_id": "STUDY-ONCOLOGY-301",
            "protocol_version": "v3.0",
            "only_approved": True,
            "top_k": 3,
        }
        search_resp = await client.post(
            "/api/v1/knowledge/protocols/search",
            json=search_payload,
            headers=crc_headers,
        )
        assert search_resp.status_code == 200, search_resp.text
        search_results = search_resp.json()
        assert len(search_results) > 0
        match_41 = next(
            (r for r in search_results if r.get("section_number") == "4.1"), None
        )
        assert match_41 is not None
        assert "Patients must meet" in match_41["chunk_text"]
        assert match_41["similarity_score"] > 0.2
        assert match_41["citation_marker"] == "[Protocol v3.0, Section 4.1, Page 2]"

        # 3. CRA queries semantic search for SAE reporting timelines
        cra_headers = get_auth_headers(PERSONA_CRA_MONITOR, ["cra_monitor"])
        sae_query_payload = {
            "query": "How quickly must a serious adverse event SAE be reported to the medical monitor?",
            "study_id": "STUDY-ONCOLOGY-301",
            "protocol_version": "v3.0",
            "only_approved": True,
            "top_k": 3,
        }
        sae_resp = await client.post(
            "/api/v1/knowledge/protocols/search",
            json=sae_query_payload,
            headers=cra_headers,
        )
        assert sae_resp.status_code == 200, sae_resp.text
        sae_results = sae_resp.json()
        assert len(sae_results) > 0
        sae_match = sae_results[0]
        assert sae_match["section_number"] == "6.2"
        assert "within 24 hours" in sae_match["chunk_text"]

        # 4. Direct Chunk Retrieval by ID
        chunk_id = match_41["chunk_id"]
        detail_resp = await client.get(
            f"/api/v1/knowledge/protocols/chunks/{chunk_id}",
            headers=crc_headers,
        )
        assert detail_resp.status_code == 200, detail_resp.text
        detail = detail_resp.json()
        assert detail["chunk_id"] == chunk_id
        assert detail["section_number"] == "4.1"
        assert detail["is_approved"] is True


@pytest.mark.asyncio
async def test_e2e_auditor_read_logging_and_cryptographic_chain_verification(
    db_session: AsyncSession,
) -> None:
    """
    End-to-End Regulatory Audit & Immutability: Automatic emission of READ_BY_AUDITOR
    events when auditor personas inspect articles, verification of the SHA-256 HMAC
    digest chain, and database-level immutability guarding against tampering.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    transport = ASGITransport(app=app)

    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        svc = create_article_service(db_session)

        # 1. Setup Category & Published Article
        cat = await svc.create_category(
            name="Regulatory Audit Guidance",
            slug="reg-audit-guidance",
            description="SOPs subject to 21 CFR Part 11 regulatory inspection.",
            persona_visibility="auditor,super_admin",
            parent_id=None,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Setup category for audit inspection tests",
        )

        art = await svc.create_article(
            title="Trial Master File (eTMF) Archival Procedure",
            slug="etmf-archival-sop",
            category_id=cat.id,
            body_markdown="# eTMF Archival\n\nLong-term electronic record retention guidelines.",
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Draft initial eTMF archival SOP",
        )
        art = await svc.submit_for_review(
            article_id=art.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Submit for review",
        )
        art = await svc.approve_article(
            article_id=art.id,
            actor_user_id=PERSONA_SPONSOR_DESIGNER,
            reason_for_change="Approved by Designer",
        )
        art = await svc.publish_article(
            article_id=art.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Publish eTMF Archival SOP",
        )

        # 2. Regulatory Auditor reads the published article
        auditor_headers = get_auth_headers(PERSONA_AUDITOR, ["auditor"])
        resp = await client.get(
            f"/api/v1/knowledge/articles/{art.id}", headers=auditor_headers
        )
        assert resp.status_code == 200, resp.text
        assert resp.json()["title"] == "Trial Master File (eTMF) Archival Procedure"

        # 3. Inspect Audit Trail via GET /articles/{id}/audit-trail
        resp = await client.get(
            f"/api/v1/knowledge/articles/{art.id}/audit-trail",
            headers=auditor_headers,
        )
        assert resp.status_code == 200, resp.text
        audit_trail = resp.json()
        actions = [log["action"] for log in audit_trail]

        # Verify full sequential lifecycle captured + READ_BY_AUDITOR
        assert "CREATED" in actions
        assert "SUBMITTED_FOR_REVIEW" in actions
        assert "APPROVED" in actions
        assert "PUBLISHED" in actions
        assert "READ_BY_AUDITOR" in actions

        # Verify auditor read event details
        auditor_event = next(
            log for log in audit_trail if log["action"] == "READ_BY_AUDITOR"
        )
        assert auditor_event["created_by"] == PERSONA_AUDITOR

        # 4. Cryptographically Verify SHA-256 HMAC Digest Chain
        resp = await client.get(
            f"/api/v1/knowledge/articles/{art.id}/verify-audit-chain",
            headers=auditor_headers,
        )
        assert resp.status_code == 200, resp.text
        chain_verification = resp.json()
        assert chain_verification["is_valid"] is True
        assert chain_verification["total_records"] == len(audit_trail)
        assert chain_verification["tamper_detected"] is False

        # 5. Database-level Immutability Enforcement
        # Attempting to delete or update an audit record must raise ValueError via ORM session hook
        raw_log = (
            (
                await db_session.execute(
                    select(KnowledgeArticleAuditLog).where(
                        KnowledgeArticleAuditLog.article_id == art.id
                    )
                )
            )
            .scalars()
            .first()
        )
        assert raw_log is not None

        raw_log.reason_for_change = "Tampering attempt"
        with pytest.raises(ValueError, match="strictly forbidden"):
            await db_session.flush()

        await db_session.rollback()


@pytest.mark.asyncio
async def test_e2e_persona_scoped_category_visibility_and_fulltext_search(
    db_session: AsyncSession,
) -> None:
    """
    End-to-End Persona Scoping & Full-Text Search: Scoped category listing by caller role,
    multi-keyword full-text search with highlighted snippets, and RBAC endpoint gating.

    @req:PRD-SYS-KH-001
    @req:PRD-SYS-KH-002
    @req:PRD-KNB-001
    @req:PRD-KNB-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session
    transport = ASGITransport(app=app)

    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        svc = create_article_service(db_session)

        # 1. Create 3 Scoped Categories
        cat_crc = await svc.create_category(
            name="CRC Site Procedures",
            slug="crc-site-procedures",
            description="Site staff only",
            persona_visibility="site_crc",
            parent_id=None,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Category for site CRC",
        )
        cat_cra = await svc.create_category(
            name="CRA Monitoring Guides",
            slug="cra-monitoring-guides",
            description="CRA monitors only",
            persona_visibility="cra_monitor",
            parent_id=None,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Category for CRA monitors",
        )
        await svc.create_category(
            name="Universal Platform Policies",
            slug="universal-policies",
            description="All platform users",
            persona_visibility=None,  # Universal
            parent_id=None,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Category for universal policies",
        )

        # 2. Create Published Articles in Scoped Categories
        # Article 1 in CRC category
        art1 = await svc.create_article(
            title="Investigational Product (IP) Dispensation Protocol",
            slug="ip-dispensation-protocol",
            category_id=cat_crc.id,
            body_markdown="# IP Dispensation\n\nMaintain accurate accountability logs in temperature-controlled storage.",
            tags=["Pharmacy", "IP", "Dispensation", "Storage"],
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Create IP article",
        )
        art1 = await svc.submit_for_review(
            article_id=art1.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Review",
        )
        art1 = await svc.approve_article(
            article_id=art1.id,
            actor_user_id=PERSONA_SPONSOR_DESIGNER,
            reason_for_change="Approve",
        )
        await svc.publish_article(
            article_id=art1.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Publish",
        )

        # Article 2 in CRA category
        art2 = await svc.create_article(
            title="Site Closeout and IP Reconciliation Procedure",
            slug="site-closeout-ip-reconciliation",
            category_id=cat_cra.id,
            body_markdown="# Closeout\n\nPerform final IP reconciliation and return all unused drug kits.",
            tags=["Closeout", "IP", "Reconciliation", "Monitoring"],
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Create Closeout article",
        )
        art2 = await svc.submit_for_review(
            article_id=art2.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Review",
        )
        art2 = await svc.approve_article(
            article_id=art2.id,
            actor_user_id=PERSONA_SPONSOR_DESIGNER,
            reason_for_change="Approve",
        )
        await svc.publish_article(
            article_id=art2.id,
            actor_user_id=PERSONA_SUPER_ADMIN,
            reason_for_change="Publish",
        )

        # 3. Test Persona Scoping on Category Listing
        # site_crc should see CRC category and Universal category, but NOT CRA category
        crc_headers = get_auth_headers(PERSONA_SITE_CRC, ["site_crc"])
        resp = await client.get("/api/v1/knowledge/categories", headers=crc_headers)
        assert resp.status_code == 200, resp.text
        crc_categories = [c["slug"] for c in resp.json()]
        assert "crc-site-procedures" in crc_categories
        assert "universal-policies" in crc_categories
        assert "cra-monitoring-guides" not in crc_categories

        # cra_monitor should see CRA category and Universal category, but NOT CRC category
        cra_headers = get_auth_headers(PERSONA_CRA_MONITOR, ["cra_monitor"])
        resp = await client.get("/api/v1/knowledge/categories", headers=cra_headers)
        assert resp.status_code == 200, resp.text
        cra_categories = [c["slug"] for c in resp.json()]
        assert "cra-monitoring-guides" in cra_categories
        assert "universal-policies" in cra_categories
        assert "crc-site-procedures" not in cra_categories

        # 4. Test Persona-Filtered Full-Text Search
        # Search query "Dispensation" by site_crc -> returns only CRC article
        resp = await client.get(
            "/api/v1/knowledge/search?q=Dispensation", headers=crc_headers
        )
        assert resp.status_code == 200, resp.text
        search_res = resp.json()
        assert len(search_res) == 1
        assert search_res[0]["slug"] == "ip-dispensation-protocol"
        assert "<mark>" in search_res[0]["highlighted_snippet"]

        # Search query "Reconciliation" by site_crc -> returns 0 results because it belongs to CRA-only category
        resp = await client.get(
            "/api/v1/knowledge/search?q=Reconciliation", headers=crc_headers
        )
        assert resp.status_code == 200, resp.text
        assert len(resp.json()) == 0

        # Search query "Reconciliation" by cra_monitor -> returns CRA article
        resp = await client.get(
            "/api/v1/knowledge/search?q=Reconciliation", headers=cra_headers
        )
        assert resp.status_code == 200, resp.text
        assert len(resp.json()) == 1
        assert resp.json()[0]["slug"] == "site-closeout-ip-reconciliation"

        # 5. Non-admin personas blocked from admin mutation endpoints (e.g. creating categories)
        unauth_cat_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Unauthorized Cat",
                "slug": "unauth-cat",
                "description": "Should fail",
                "reason_for_change": "Unauthorized creation",
            },
            headers=crc_headers,
        )
        assert unauth_cat_resp.status_code == 403
