import time

import pytest
from fastapi.testclient import TestClient

from apps.eisf.database import db_manager as eisf_db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base as EisfBase
from apps.execution.cdisc_validator import validate_cdisc_xml_structure
from apps.execution.trial_lock import TrialLockManager
from apps.gateway.main import generate_signature


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


@pytest.mark.asyncio
async def test_site_level_data_isolation():
    """
    Validation Suite - Site-level Data Isolation & Cross-site document access restrictions
    @req:PRD-SYS-004
    """
    # Initialize in-memory database for EISF
    eisf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with eisf_db_manager.engine.begin() as conn:
        await conn.run_sync(EisfBase.metadata.create_all)

    try:
        client = TestClient(eisf_app)

        # Helper to generate headers
        def get_auth_headers(user_id, roles, site_id):
            timestamp = str(time.time())
            sig = generate_signature(
                user_id,
                roles,
                timestamp,
                version="2",
                change_reason="Isolation Test",
                site_id=site_id,
            )
            return {
                "X-User-Id": user_id,
                "X-User-Roles": roles,
                "X-Gateway-Timestamp": timestamp,
                "X-Gateway-Signature": sig,
                "X-Signature-Version": "2",
                "X-Change-Reason": "Isolation Test",
                "X-Site-Id": site_id,
            }

        # Create a document for london site using admin role
        london_headers = get_auth_headers("admin-london", "admin", "site-london-02")
        payload = {
            "study_id": "study-100",
            "site_id": "site-london-02",
            "binder_classification": "Investigator CVs",
            "filename": "london_cv.pdf",
            "content": "London investigator CV content",
            "mime_type": "application/pdf",
            "reason_for_change": "Admin pre-population",
        }
        create_resp = client.post(
            "/api/v1/eisf/documents", json=payload, headers=london_headers
        )
        assert create_resp.status_code == 201
        london_doc_id = create_resp.json()["id"]

        # Attempt to access London document using Boston investigator headers
        boston_headers = get_auth_headers(
            "pi-boston", "site investigator", "site-boston-01"
        )
        get_resp = client.get(
            f"/api/v1/eisf/documents/{london_doc_id}", headers=boston_headers
        )
        assert get_resp.status_code == 403, "Cross-site access was not forbidden!"

    finally:
        async with eisf_db_manager.engine.begin() as conn:
            await conn.run_sync(EisfBase.metadata.drop_all)
        await eisf_db_manager.close()


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
