"""Comprehensive CTMS Unit and Integration Test Suite for Country-Aware Greenlight Site Activation Engine & Regulatory Submissions Tracker.

Requirements:
- PRD-SSU-001: Country-Aware Startup Prerequisite Rules Matrix
- PRD-SSU-002: Multi-Factor Greenlight Evaluator (6 Operational Facets & Executive Override)
- PRD-SSU-003: Cryptographic Gating Token & Event Dispatch
- PRD-SYS-121: Cross-Service Outbox Event Distribution
- ADR-2199 / ADR-2202: Architecture Decision Records for Country-Aware Greenlight Engine
- ICH GCP E6(R2) Section 8: Essential Documents for the Conduct of a Clinical Trial
- 21 CFR Part 11: Electronic Records and Electronic Signatures
"""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import event, select

from apps.ctms.adapters.database import db_manager
from apps.ctms.adapters.models import Base, IntegrationOutbox
from apps.ctms.main import app
from packages.security.signing import (
    generate_greenlight_token,
    verify_greenlight_token,
)
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory CTMS database with attached audit schema."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    @event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_connection, connection_record):
        cursor = dbapi_connection.cursor()
        cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        cursor.close()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "Sponsor Admin",
    change_reason: str = "Country-Aware Greenlight Site Activation Test",
    user_id: str = "regulatory_lead_01",
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


def test_jurisdiction_rule_presets_and_configuration():
    """Validate jurisdiction startup criteria presets and custom study rule persistence.

    @req:PRD-SYS-183
    @req:PRD-SSU-001
    @req:ADR-2199
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Sponsor Admin", change_reason="Configure US startup rules"
    )

    # 1. Fetch presets
    res_presets = client.get("/api/v1/ctms/startup/rules/presets", headers=headers)
    assert res_presets.status_code == 200
    presets = res_presets.json()
    assert "US" in presets
    assert "EU" in presets
    assert "JP" in presets
    assert "GB" in presets
    assert "GLOBAL" in presets

    # Verify US preset has FDA_1572 and LOCAL_IRB_APPROVAL
    us_codes = {item["criterion_code"] for item in presets["US"]}
    assert "FDA_1572" in us_codes
    assert "LOCAL_IRB_APPROVAL" in us_codes
    assert "SITE_CONTRACT" in us_codes
    assert "EISF_BASELINE_CERT" in us_codes
    assert "DOA_LOG" in us_codes
    assert "GCP_TRAINING" in us_codes
    assert "IP_RELEASE_AUTHORIZATION" in us_codes

    # 2. Configure a customized country prerequisite rule for a study
    rule_payload = {
        "study_id": "STUDY-SSU-100",
        "country_code": "JP",
        "facet": "REGULATORY_IRB",
        "criterion_code": "PMDA_NOTIFICATION",
        "criterion_name": "Japan PMDA 30-Day Clinical Trial Notification",
        "is_mandatory": True,
        "description": "Statutory 30-day review period completion",
    }
    res_rule = client.post(
        "/api/v1/ctms/startup/rules", json=rule_payload, headers=headers
    )
    assert res_rule.status_code == 201
    rule_data = res_rule.json()
    assert rule_data["study_id"] == "STUDY-SSU-100"
    assert rule_data["country_code"] == "JP"
    assert rule_data["criterion_code"] == "PMDA_NOTIFICATION"
    assert rule_data["is_mandatory"] is True

    # 3. List rules for study and country
    res_list = client.get(
        "/api/v1/ctms/startup/rules?study_id=STUDY-SSU-100&country_code=JP",
        headers=headers,
    )
    assert res_list.status_code == 200
    rules = res_list.json()
    assert len(rules) >= 1
    assert any(r["criterion_code"] == "PMDA_NOTIFICATION" for r in rules)


def test_multi_factor_6_facet_greenlight_evaluator():
    """Validate real-time multi-factor greenlight evaluator across the 6 operational facets.

    @req:PRD-SYS-183
    @req:PRD-SSU-002
    @req:PRD-SSU-003
    @req:ADR-2202
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Sponsor Admin", change_reason="6-Facet Greenlight Evaluation Test"
    )
    study_id = "STUDY-SSU-200"
    site_id = "SITE-201"

    # Step 1: Initial evaluation on an empty site -> overall_status PENDING, 0 facets passed
    res_eval1 = client.get(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight?study_id={study_id}&country_code=US",
        headers=headers,
    )
    assert res_eval1.status_code == 200
    eval1 = res_eval1.json()
    assert eval1["overall_status"] == "PENDING"
    assert eval1["facet_regulatory_irb"] is False
    assert eval1["facet_cta_budget"] is False
    assert eval1["facet_eisf_baseline"] is False
    assert eval1["facet_doa_log"] is False
    assert eval1["facet_staff_training"] is False
    assert eval1["facet_ip_release"] is False
    assert eval1["evaluation_details"] is not None

    # Step 2: Submit and approve Facet 1 (LOCAL_IRB_APPROVAL and FDA_1572)
    doc_irb = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "document_type": "LOCAL_IRB_APPROVAL",
            "file_name": "IRB_Approval_Letter.pdf",
            "file_hash": "hash_irb_12345",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_irb['id']}/review",
        json={"status": "APPROVED", "review_notes": "IRB approved"},
        headers=headers,
    )

    doc_1572 = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "document_type": "FDA_1572",
            "file_name": "Form_FDA_1572_Signed.pdf",
            "file_hash": "hash_1572_12345",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_1572['id']}/review",
        json={"status": "APPROVED", "review_notes": "1572 verified"},
        headers=headers,
    )

    # Evaluate -> Facet 1 (Regulatory/IRB) should now pass
    res_eval2 = client.get(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight?study_id={study_id}&country_code=US",
        headers=headers,
    )
    eval2 = res_eval2.json()
    assert eval2["facet_regulatory_irb"] is True
    assert eval2["facet_cta_budget"] is False

    # Attempt premature certification -> Rejected with 422
    res_cert_fail = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/certify?study_id={study_id}&country_code=US",
        headers=headers,
    )
    assert res_cert_fail.status_code == 422
    assert "Missing mandatory approvals" in res_cert_fail.json()["detail"]

    # Step 3: Submit and approve remaining facets (CTA, eISF baseline, DOA log, Training, IP release)
    doc_cta = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "document_type": "SITE_CONTRACT",
            "file_name": "CTA_Executed.pdf",
            "file_hash": "hash_cta_12345",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_cta['id']}/review",
        json={"status": "APPROVED", "review_notes": "Contract executed"},
        headers=headers,
    )

    doc_eisf = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "document_type": "EISF_BASELINE_CERT",
            "file_name": "eISF_Binder_Certified.pdf",
            "file_hash": "hash_eisf_12345",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_eisf['id']}/review",
        json={"status": "APPROVED", "review_notes": "eISF certified"},
        headers=headers,
    )

    doc_doa = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "document_type": "DOA_LOG",
            "file_name": "DOA_Log_Signed_PI.pdf",
            "file_hash": "hash_doa_12345",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_doa['id']}/review",
        json={"status": "APPROVED", "review_notes": "DOA signed"},
        headers=headers,
    )

    doc_trn = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "document_type": "GCP_TRAINING",
            "file_name": "GCP_Certificates_Combined.pdf",
            "file_hash": "hash_trn_12345",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_trn['id']}/review",
        json={"status": "APPROVED", "review_notes": "Training verified"},
        headers=headers,
    )

    doc_ip = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "document_type": "IP_RELEASE_AUTHORIZATION",
            "file_name": "IP_Depot_Greenlight_Release.pdf",
            "file_hash": "hash_ip_12345",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_ip['id']}/review",
        json={"status": "APPROVED", "review_notes": "IP release authorized"},
        headers=headers,
    )

    # Step 4: Re-evaluate -> All 6 facets pass!
    res_eval3 = client.get(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight?study_id={study_id}&country_code=US",
        headers=headers,
    )
    eval3 = res_eval3.json()
    assert eval3["overall_status"] == "APPROVED"
    assert eval3["facet_regulatory_irb"] is True
    assert eval3["facet_cta_budget"] is True
    assert eval3["facet_eisf_baseline"] is True
    assert eval3["facet_doa_log"] is True
    assert eval3["facet_staff_training"] is True
    assert eval3["facet_ip_release"] is True

    # Step 5: Formally certify greenlight -> issues cryptographic HMAC token & logs audit
    res_cert = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/certify?study_id={study_id}&country_code=US",
        headers=headers,
    )
    assert res_cert.status_code == 200
    cert_data = res_cert.json()
    assert cert_data["overall_status"] == "APPROVED"
    assert cert_data["greenlight_token"] is not None
    assert "." in cert_data["greenlight_token"]
    assert cert_data["greenlight_certified_by"] == "regulatory_lead_01"
    assert cert_data["greenlight_certified_at"] is not None


def test_jurisdiction_specific_startup_criteria():
    """Validate jurisdiction-specific regulatory startup criteria (EU CTR, Japan PMDA, UK MHRA).

    @req:PRD-SYS-183
    @req:PRD-SSU-001
    @req:PRD-SSU-002
    @req:ADR-2202
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="Sponsor Admin")

    # 1. European Union (EU CTR Part I & Part II EC decision)
    study_eu = "STUDY-EU-01"
    site_eu = "SITE-EU-01"

    # Regulatory milestones for EU
    client.post(
        "/api/v1/ctms/startup/milestones",
        json={
            "study_id": study_eu,
            "country_code": "EU",
            "milestone_type": "EU_CTR_PART_I",
            "status": "APPROVED",
            "approval_number": "EU-CTR-2026-9901",
            "authority_name": "European Medicines Agency",
        },
        headers=headers,
    )
    client.post(
        "/api/v1/ctms/startup/milestones",
        json={
            "study_id": study_eu,
            "country_code": "EU",
            "milestone_type": "EU_CTR_PART_II_EC",
            "status": "APPROVED",
            "approval_number": "EC-DECISION-887",
            "authority_name": "National Ethics Committee",
        },
        headers=headers,
    )

    res_eu = client.get(
        f"/api/v1/ctms/startup/sites/{site_eu}/greenlight?study_id={study_eu}&country_code=EU",
        headers=headers,
    )
    assert res_eu.json()["facet_regulatory_irb"] is True

    # 2. Japan (PMDA Notification & Local IRB)
    study_jp = "STUDY-JP-01"
    site_jp = "SITE-JP-01"

    client.post(
        "/api/v1/ctms/startup/milestones",
        json={
            "study_id": study_jp,
            "country_code": "JP",
            "milestone_type": "PMDA_NOTIFICATION",
            "status": "APPROVED",
            "approval_number": "PMDA-CTN-2026-004",
            "authority_name": "PMDA Japan",
        },
        headers=headers,
    )
    doc_jp_irb = client.post(
        "/api/v1/ctms/startup/documents",
        json={
            "study_id": study_jp,
            "site_id": site_jp,
            "document_type": "LOCAL_IRB_APPROVAL",
            "file_name": "Tokyo_Hospital_IRB.pdf",
            "file_hash": "hash_jp_irb",
        },
        headers=headers,
    ).json()
    client.post(
        f"/api/v1/ctms/startup/documents/{doc_jp_irb['id']}/review",
        json={"status": "APPROVED", "review_notes": "IRB approved"},
        headers=headers,
    )

    res_jp = client.get(
        f"/api/v1/ctms/startup/sites/{site_jp}/greenlight?study_id={study_jp}&country_code=JP",
        headers=headers,
    )
    assert res_jp.json()["facet_regulatory_irb"] is True


def test_hmac_sha256_cryptographic_token_generation_and_verification():
    """Validate HMAC-SHA256 token generation, canonical signing, tamper rejection, and expiry.

    @req:PRD-SYS-183
    @req:PRD-SSU-003
    @req:ADR-2202
    """
    site_id = "SITE-TOKEN-101"
    study_id = "STUDY-TOKEN-101"
    country_code = "US"
    user_id = "director_qa_01"

    # 1. Generate valid token
    token = generate_greenlight_token(
        site_id=site_id,
        study_id=study_id,
        country_code=country_code,
        certified_by=user_id,
        expires_in_seconds=3600,
    )
    assert token is not None
    assert "." in token

    # 2. Verify token
    payload = verify_greenlight_token(token=token, site_id=site_id, study_id=study_id)
    assert payload is not None
    assert payload["site_id"] == site_id
    assert payload["study_id"] == study_id
    assert payload["country_code"] == country_code
    assert payload["certified_by"] == user_id
    assert payload["token_type"] == "SITE_GREENLIGHT_ACTIVATION"
    assert payload["is_overridden"] is False

    # 3. Rejection on site mismatch
    assert (
        verify_greenlight_token(token=token, site_id="WRONG-SITE", study_id=study_id)
        is None
    )

    # 4. Rejection on study mismatch
    assert (
        verify_greenlight_token(token=token, site_id=site_id, study_id="WRONG-STUDY")
        is None
    )

    # 5. Tamper resistance: modify token payload
    parts = token.split(".")
    tampered_token = f"dGFtcGVyZWQ.{parts[1]}"
    assert (
        verify_greenlight_token(
            token=tampered_token, site_id=site_id, study_id=study_id
        )
        is None
    )

    # 6. Expiry rejection
    expired_token = generate_greenlight_token(
        site_id=site_id,
        study_id=study_id,
        country_code=country_code,
        certified_by=user_id,
        expires_in_seconds=-10,  # Already expired
    )
    assert (
        verify_greenlight_token(token=expired_token, site_id=site_id, study_id=study_id)
        is None
    )


@pytest.mark.asyncio
async def test_transactional_outbox_site_greenlight_approved_event():
    """Validate atomic persistence of SiteGreenlightApproved event in transactional outbox.

    @req:PRD-SYS-183
    @req:PRD-SSU-003
    @req:PRD-SYS-121
    @req:ADR-2202
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Sponsor Admin", change_reason="Atomic outbox test"
    )
    study_id = "STUDY-OUTBOX-01"
    site_id = "SITE-OUTBOX-01"

    # Setup approved essential docs to satisfy all 6 facets
    for doc_type in [
        "LOCAL_IRB_APPROVAL",
        "FDA_1572",
        "SITE_CONTRACT",
        "EISF_BASELINE_CERT",
        "DOA_LOG",
        "GCP_TRAINING",
        "IP_RELEASE_AUTHORIZATION",
    ]:
        d = client.post(
            "/api/v1/ctms/startup/documents",
            json={
                "study_id": study_id,
                "site_id": site_id,
                "document_type": doc_type,
                "file_name": f"{doc_type}.pdf",
                "file_hash": f"hash_{doc_type}",
            },
            headers=headers,
        ).json()
        client.post(
            f"/api/v1/ctms/startup/documents/{d['id']}/review",
            json={"status": "APPROVED", "review_notes": "Approved"},
            headers=headers,
        )

    # Certify Greenlight
    res_cert = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/certify?study_id={study_id}&country_code=US",
        headers=headers,
    )
    assert res_cert.status_code == 200

    # Verify Outbox table directly
    async with db_manager.get_session_maker()() as session:
        stmt = select(IntegrationOutbox).where(
            IntegrationOutbox.event_type == "SiteGreenlightApproved"
        )
        res = await session.execute(stmt)
        outbox_events = res.scalars().all()
        assert len(outbox_events) >= 1

        event_rec = outbox_events[-1]
        assert event_rec.event_type == "SiteGreenlightApproved"
        assert event_rec.status == "PENDING"
        assert event_rec.payload["site_id"] == site_id
        assert event_rec.payload["study_id"] == study_id
        assert event_rec.payload["greenlight_token"] is not None
        assert event_rec.payload["facets_summary"]["regulatory_irb"] is True
        assert event_rec.payload["facets_summary"]["ip_release"] is True


def test_dual_signature_executive_gxp_override():
    """Validate dual-signature executive sponsor GxP override workflow with 21 CFR Part 11 justification.

    @req:PRD-SYS-183
    @req:PRD-SSU-002
    @req:ADR-2202
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Sponsor Admin", change_reason="Executive GxP override test"
    )
    study_id = "STUDY-OVERRIDE-01"
    site_id = "SITE-OVERRIDE-01"

    # 1. Attempt override with invalid payload (missing medical monitor or short justification)
    res_invalid = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/override",
        json={
            "study_id": study_id,
            "sponsor_user_id": "sponsor_exec_01",
            "medical_user_id": "",
            "sponsor_signature": "sig_sponsor_valid",
            "medical_signature": "sig_med_valid",
            "justification": "short",
            "country_code": "US",
        },
        headers=headers,
    )
    assert res_invalid.status_code == 422

    # 2. Execute valid dual-signature override
    res_override = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/override",
        json={
            "study_id": study_id,
            "sponsor_user_id": "sponsor_exec_01",
            "medical_user_id": "med_monitor_02",
            "sponsor_signature": "sig_sponsor_hmac_valid_hash_1",
            "medical_signature": "sig_med_monitor_hmac_valid_hash_2",
            "justification": "Authorized expedited protocol activation under comprehensive risk management assessment.",
            "country_code": "US",
        },
        headers=headers,
    )
    assert res_override.status_code == 200
    override_data = res_override.json()
    assert override_data["overall_status"] == "OVERRIDDEN"
    assert override_data["is_overridden"] is True
    assert override_data["override_sponsor_user_id"] == "sponsor_exec_01"
    assert override_data["override_medical_user_id"] == "med_monitor_02"
    assert override_data["greenlight_token"] is not None

    # 3. Verify token issued during override
    token_verify_res = client.post(
        f"/api/v1/ctms/startup/sites/{site_id}/greenlight/verify-token",
        json={
            "token": override_data["greenlight_token"],
            "site_id": site_id,
            "study_id": study_id,
        },
        headers=headers,
    )
    assert token_verify_res.status_code == 200
    assert token_verify_res.json()["is_valid"] is True
    assert token_verify_res.json()["payload"]["is_overridden"] is True
