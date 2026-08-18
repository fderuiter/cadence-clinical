"""Integration tests for newly added eConsent sub-routers and endpoints.

Tests export, audit trail query, granular options router, and template diffing.
"""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import (
    Base,
    ConsentAuditLog,
    ConsentClause,
    ConsentSignature,
    ConsentTemplate,
    SubjectConsent,
)
from apps.econsent.main import app
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "test_user",
    roles: str = "investigator",
    change_reason: str = "API Endpoint Test",
) -> dict:
    timestamp = str(time.time())
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


@pytest.mark.asyncio
async def test_audit_logs_endpoint() -> None:
    """Test querying 21 CFR Part 11 audit logs via the audit sub-router."""
    async with db_manager.get_session_maker()() as session:
        log1 = ConsentAuditLog(
            actor_id="user.crc",
            actor_role="crc",
            action="CREATE_DOCUMENT",
            document_id="doc-123",
            details="Document created",
            reason_for_change="Initial setup",
        )
        log2 = ConsentAuditLog(
            actor_id="user.patient",
            actor_role="patient",
            action="CAPTURE_CONSENT",
            document_id="doc-456",
            details="Consent captured",
            reason_for_change="Participant consent",
        )
        session.add(log1)
        session.add(log2)
        await session.commit()

    client = TestClient(app)
    headers = get_auth_headers()
    res = client.get("/api/v1/econsent/audit", headers=headers)
    assert res.status_code == 200
    logs = res.json()
    assert len(logs) == 2

    # Filter by document_id
    res_filtered = client.get(
        "/api/v1/econsent/audit?document_id=doc-123", headers=headers
    )
    assert res_filtered.status_code == 200
    assert len(res_filtered.json()) == 1
    assert res_filtered.json()[0]["actor_id"] == "user.crc"


@pytest.mark.asyncio
async def test_granular_options_router() -> None:
    """Test POST and GET on /api/v1/econsent/options."""
    client = TestClient(app)
    headers = get_auth_headers()
    payload = {
        "option_code": "OPT_BIOBANK_ROUTER",
        "title": "Biobank Storage",
        "description": "Store biological specimens",
        "category": "BIOBANKING",
        "is_mandatory": False,
        "default_selected": True,
        "reason_for_change": "Add biobanking option",
    }
    res_post = client.post(
        "/api/v1/econsent/options/tpl-opt-01/1",
        json=payload,
        headers=headers,
    )
    assert res_post.status_code == 201
    created = res_post.json()
    assert created["option_code"] == "OPT_BIOBANK_ROUTER"

    res_get = client.get("/api/v1/econsent/options/tpl-opt-01/1", headers=headers)
    assert res_get.status_code == 200
    opts = res_get.json()
    assert len(opts) == 1
    assert opts[0]["title"] == "Biobank Storage"


@pytest.mark.asyncio
async def test_export_and_diff_endpoints() -> None:
    """Test CDISC ODM XML export, verifiable HTML certificate, and template version diff endpoints."""
    async with db_manager.get_session_maker()() as session:
        c1 = ConsentClause(
            clause_id="c-diff-1",
            study_id="STUDY-EXP-01",
            title="Study Purpose",
            text="Evaluate drug safety.",
            version_index=1,
            created_by="designer",
            reason_for_change="v1",
        )
        c2 = ConsentClause(
            clause_id="c-diff-2",
            study_id="STUDY-EXP-01",
            title="Study Purpose and Objectives",
            text="Evaluate drug safety and adverse risk profiles extensively.",
            version_index=1,
            created_by="designer",
            reason_for_change="v2",
        )
        session.add(c1)
        session.add(c2)

        t1 = ConsentTemplate(
            template_id="tpl-exp-01",
            study_id="STUDY-EXP-01",
            template_name="Export Test ICF",
            protocol_version="1.0",
            version_index=1,
            is_published=True,
            requires_reconsent=False,
            clauses=["c-diff-1"],
            workflow_steps=[],
            created_by="designer",
            reason_for_change="v1",
        )
        t2 = ConsentTemplate(
            template_id="tpl-exp-01",
            study_id="STUDY-EXP-01",
            template_name="Export Test ICF",
            protocol_version="2.0",
            version_index=2,
            is_published=True,
            requires_reconsent=True,
            clauses=["c-diff-1", "c-diff-2"],
            workflow_steps=[],
            created_by="designer",
            reason_for_change="v2",
        )
        session.add(t1)
        session.add(t2)

        consent = SubjectConsent(
            subject_pseudonym="SUBJ-EXP-001",
            study_id="STUDY-EXP-01",
            site_id="SITE-01",
            template_id="tpl-exp-01",
            version_index=1,
            protocol_version="1.0",
            source_content_identity="source-hash",
            status="ACTIVE",
            signature_manifest={},
            created_by="patient",
            reason_for_change="Consent",
        )
        session.add(consent)

        sig = ConsentSignature(
            template_id="tpl-exp-01",
            version_index=1,
            subject_pseudonym="SUBJ-EXP-001",
            role="SUBJECT",
            signer_name="Export Subject",
            signature_data="sig_data",
            digest_sha256="abcdef123456",
            created_by="patient",
            reason_for_change="Sign",
        )
        session.add(sig)
        await session.commit()

    client = TestClient(app)
    headers = get_auth_headers()

    # 1. Test CDISC ODM Export
    res_odm = client.get(
        "/api/v1/econsent/export/cdisc-odm/STUDY-EXP-01/SUBJ-EXP-001/tpl-exp-01/1",
        headers=headers,
    )
    assert res_odm.status_code == 200
    odm_data = res_odm.json()
    assert odm_data["odm_version"] == "1.3.2"
    assert "<ODM" in odm_data["xml_content"]

    # 2. Test Verifiable HTML Certificate Export
    res_cert = client.get(
        "/api/v1/econsent/export/certificate/STUDY-EXP-01/SUBJ-EXP-001/tpl-exp-01/1",
        headers=headers,
    )
    assert res_cert.status_code == 200
    cert_data = res_cert.json()
    assert "<!DOCTYPE html>" in cert_data["html_content"]
    assert cert_data["digest_sha256"] is not None

    # 3. Test Template Version Diff Endpoint
    res_diff = client.get(
        "/api/v1/econsent/templates/tpl-exp-01/diff/1/2",
        headers=headers,
    )
    assert res_diff.status_code == 200
    diff_data = res_diff.json()
    assert diff_data["template_id"] == "tpl-exp-01"
    assert diff_data["base_version_index"] == 1
    assert diff_data["target_version_index"] == 2
    assert diff_data["total_added"] == 1
    assert diff_data["total_unchanged"] == 1
    assert diff_data["requires_reconsent"] is True
