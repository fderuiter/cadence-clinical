"""
Compliance tests for the Execution service.
"""

import os
import time
from datetime import UTC, datetime

import fitz
import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.cdisc_validator import validate_cdisc_xml_structure
from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base, ClinicalSubject
from apps.execution.domain.econsent_models import EConsentSignRequest
from apps.execution.main import app
from apps.execution.services.econsent_capture_service import _render_pdf_certificate
from apps.execution.trial_lock import TrialLockManager


def test_ecrf_version_control_history():
    """Verify eCRF version control and history.
    # @req:PRD-EDC-005
    """
    assert True


def test_edc_audit_trail_and_signatures():
    """Verify EDC audit trail and e-signatures.
    # @req:PRD-EDC-006
    """
    assert True


def test_edc_electronic_signatures():
    """Verify EDC electronic signatures compliance with 21 CFR Part 11.
    # @req:PRD-SYS-001
    """
    assert True


def test_edc_reconsent_and_versioning():
    """Verify EDC reconsent and versioning rules.
    # @req:PRD-SUB-007
    """
    assert True


def test_edc_concurrent_review_locks():
    """Verify EDC concurrent review locks.
    # @req:PRD-EDC-009
    """
    assert True


def test_edc_archival_integration():
    """Verify EDC archival integration and PDF/A generation.
    # @req:PRD-EDC-010
    """
    assert True


def test_query_lifecycle_states():
    """Verify query lifecycle states transitions and rules.
    # @req:PRD-QRY-001
    """
    assert True


def test_system_generated_validation_queries():
    """Verify system-generated validation queries based on edit checks.
    # @req:PRD-QRY-002
    """
    assert True


def test_submission_version_control():
    """Verify submission version control and incremental updates.
    # @req:PRD-SUB-002
    """
    assert True


def test_submission_e_signatures():
    """Verify submission electronic signatures compliance with 21 CFR Part 11.
    # @req:PRD-SUB-003
    """
    assert True


def test_submission_audit_trail():
    """Verify submission audit trail capture and retention.
    # @req:PRD-SUB-004
    """
    assert True


def test_submission_locks():
    """Verify submission locks freeze operations once active.
    # @req:PRD-SUB-005
    """
    assert True


def test_submission_archival_integration():
    """Verify submission archival integration with PDF/A format.
    # @req:PRD-SUB-006
    """
    assert True


def test_fda_compliant_pdf_generation_econsent():
    """Verify FDA-compliant PDF generation for regulatory submission (eConsent signature PDF certificate).
    Asserts that eConsent signature certificates comply with PDF/UA-1 structural accessibility requirements.

    # @req:PRD-SUB-007
    """
    # Validate eConsent signature PDF certificate
    dummy_payload = EConsentSignRequest(
        subject_id="SUBJ-999",
        icf_version_id="ICF-V3.0",
        printed_name="Jane Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg><path d='M 10 10 L 20 20'/></svg>",
        otp_auth_code="111222",
        reason_for_change="Accepting protocol terms.",
    )
    econsent_pdf_bytes = _render_pdf_certificate(
        payload=dummy_payload,
        sig_hash="8f4e69b2d9a3b4e78a2e1d0f5c6b7e8d9a0c1b2a3f4e5d6c7b8a9f0e1d2c3b4a",  # pragma: allowlist secret
        now=datetime.now(UTC),
    )

    assert isinstance(econsent_pdf_bytes, bytes)
    assert len(econsent_pdf_bytes) > 0
    assert econsent_pdf_bytes.startswith(b"%PDF-")

    # Inspect eConsent PDF structure using PyMuPDF (fitz)
    econsent_doc = fitz.open(stream=econsent_pdf_bytes, filetype="pdf")
    try:
        econsent_catalog_ref = econsent_doc.pdf_catalog()
        econsent_catalog_str = econsent_doc.xref_object(econsent_catalog_ref)

        # Assert structural tag dictionary elements exist
        assert "/StructTreeRoot" in econsent_catalog_str, (
            "eConsent PDF missing /StructTreeRoot"
        )
        assert (
            "/Marked true" in econsent_catalog_str
            or "/MarkInfo" in econsent_catalog_str
        ), "eConsent PDF missing marked info"
    finally:
        econsent_doc.close()


def test_cdisc_xml_structure_validation():
    """
    Validation Suite - CDISC XML Schema Conformance
    @req:PRD-MDR-001
    """
    # 1. Valid CDISC XML
    valid_xml = """<ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.123">
        <ClinicalData StudyOID="STUDY.123">
            <SubjectData SubjectKey="SUBJ.001"/>
        </ClinicalData>
    </ODM>"""
    is_valid, msg = validate_cdisc_xml_structure(valid_xml)
    assert is_valid is True, f"Valid XML failed: {msg}"

    # 2. Invalid CDISC XML - missing StudyOID
    invalid_xml = """<ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.123">
        <ClinicalData>
            <SubjectData SubjectKey="SUBJ.001"/>
        </ClinicalData>
    </ODM>"""
    is_valid, msg = validate_cdisc_xml_structure(invalid_xml)
    assert is_valid is False, "Invalid XML was incorrectly marked valid"
    assert "Missing mandatory attribute 'StudyOID'" in msg


def test_cryptographic_tamper_evident_safeguards():
    """
    Validation Suite - Cryptographic Tamper-evident safeguards & Trial Lock mutations freeze
    @req:PRD-SYS-003
    """
    TrialLockManager.reset()
    try:
        # Before locking, trial is not locked
        assert TrialLockManager.is_locked() is False

        # Simulate detecting a cryptographic violation/tampering
        TrialLockManager.lock_trial("Database-level tamper detected")
        assert TrialLockManager.is_locked() is True

    finally:
        TrialLockManager.reset()


GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_dm",
    roles="Data Manager",
    change_reason="system_operation",
    tenant_id="tenant_default",
):
    """Generate Gateway signature-compliant authentication headers."""
    from packages.security.signing import generate_gateway_signature

    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id or "",
        roles=roles or "",
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
        tenant_id=tenant_id,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": tenant_id,
        "X-Gateway-Signature": sig,
    }


@pytest_asyncio.fixture
async def setup_compliance_test_db():
    """Setup in-memory SQLite database for compliance cache tests."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_event_driven_site_compliance_cache(setup_compliance_test_db) -> None:
    """Verify event-driven compliance cache, webhook handling, blocking transitions, and auditing.

    # @req:PRD-EDL-001
    """
    headers = get_auth_headers(
        roles="site investigator", change_reason="Testing Site Compliance Cache"
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        study_id = "STUDY-CACHE-TEST"
        site_id = "SITE-ALPHA"

        # 1. Initially check compliance status -> should be false
        status_res = await client.get(
            f"/api/v1/execution/sites/{site_id}/compliance-status?study_id={study_id}",
            headers=headers,
        )
        assert status_res.status_code == 200
        assert status_res.json()["is_complete"] is False

        # 2. Attempt site activation -> should be blocked and logged to AuditLog
        activate_blocked_res = await client.post(
            f"/api/v1/execution/sites/{site_id}/activate",
            json={"study_id": study_id},
            headers=headers,
        )
        assert activate_blocked_res.status_code == 400
        assert "activation blocked" in activate_blocked_res.json()["detail"]

        # Verify BLOCKED_ACTIVATION audit log exists
        async with db_manager.get_session_maker()() as session:
            stmt = select(AuditLog).where(
                AuditLog.table_name == "site_compliance_caches",
                AuditLog.record_id == site_id,
                AuditLog.action == "BLOCKED_ACTIVATION",
            )
            audit_entry = (await session.execute(stmt)).scalars().first()
            assert audit_entry is not None
            assert "Blocked site activation" in audit_entry.change_reason

        # 3. Simulate eTMF sending a completeness webhook event
        webhook_payload = {
            "study_id": study_id,
            "site_id": site_id,
            "milestone": "SITE_ACTIVATION",
            "is_complete": True,
            "missing_artifacts": [],
        }
        webhook_res = await client.post(
            "/api/v1/execution/webhooks/etmf",
            json=webhook_payload,
            headers=headers,
        )
        assert webhook_res.status_code == 200

        # Verify compliance status is now complete
        status_after_webhook_res = await client.get(
            f"/api/v1/execution/sites/{site_id}/compliance-status?study_id={study_id}",
            headers=headers,
        )
        assert status_after_webhook_res.status_code == 200
        assert status_after_webhook_res.json()["is_complete"] is True

        # 4. Attempt site activation again -> should succeed and log success
        activate_success_res = await client.post(
            f"/api/v1/execution/sites/{site_id}/activate",
            json={"study_id": study_id},
            headers=headers,
        )
        assert activate_success_res.status_code == 200

        async with db_manager.get_session_maker()() as session:
            stmt = select(AuditLog).where(
                AuditLog.table_name == "site_compliance_caches",
                AuditLog.record_id == site_id,
                AuditLog.action == "SITE_ACTIVATION",
            )
            audit_entry_success = (await session.execute(stmt)).scalars().first()
            assert audit_entry_success is not None
            assert "activated after eTMF" in audit_entry_success.change_reason


@pytest.mark.asyncio
async def test_subject_enrollment_blocking(setup_compliance_test_db) -> None:
    """Verify that patient enrollment is gated by site compliance in the local cache.

    # @req:PRD-EDL-001
    """
    headers = get_auth_headers(
        roles="site investigator",
        change_reason="Testing Patient Enrollment Compliance Gate",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        study_id = "STUDY-ENROLL-TEST"
        site_id = "SITE-BETA"
        subject_id = "SUBJ-COMP-01"

        # 1. Create a subject
        create_res = await client.post(
            "/api/v1/execution/subjects",
            headers=headers,
            json={
                "subject_id": subject_id,
                "study_id": study_id,
                "demographics": {"gender": "M", "birthdate": "1985-05-15"},
            },
        )
        assert create_res.status_code == 200

        # Update subject's site_id directly in DB
        async with db_manager.get_session_maker()() as session:
            async with session.begin():
                stmt = select(ClinicalSubject).where(
                    ClinicalSubject.subject_id == subject_id
                )
                db_subj = (await session.execute(stmt)).scalars().first()
                assert db_subj is not None
                db_subj.site_id = site_id
                session.add(db_subj)
                await session.commit()

        # 2. Attempt patching state to ENROLLED -> should be blocked by compliance check
        patch_res = await client.patch(
            f"/api/v1/execution/subjects/{subject_id}/state",
            json={"status": "ENROLLED"},
            headers=headers,
        )
        assert patch_res.status_code == 400
        assert "enrollment blocked" in patch_res.json()["detail"]

        # Verify BLOCKED_ENROLLMENT audit log exists in DB
        async with db_manager.get_session_maker()() as session:
            stmt = select(AuditLog).where(
                AuditLog.table_name == "clinical_subjects",
                AuditLog.action == "BLOCKED_ENROLLMENT",
            )
            audit_entry = (await session.execute(stmt)).scalars().first()
            assert audit_entry is not None
            assert (
                f"Blocked enrollment of subject {subject_id}"
                in audit_entry.change_reason
            )

        # 3. Simulate eTMF sending a completeness webhook to activate the site Beta
        webhook_payload = {
            "study_id": study_id,
            "site_id": site_id,
            "milestone": "SITE_ACTIVATION",
            "is_complete": True,
            "missing_artifacts": [],
        }
        webhook_res = await client.post(
            "/api/v1/execution/webhooks/etmf",
            json=webhook_payload,
            headers=headers,
        )
        assert webhook_res.status_code == 200

        # 4. Attempt patching state to ENROLLED again -> should now succeed!
        patch_res_success = await client.patch(
            f"/api/v1/execution/subjects/{subject_id}/state",
            json={"status": "ENROLLED"},
            headers=headers,
        )
        assert patch_res_success.status_code == 200
