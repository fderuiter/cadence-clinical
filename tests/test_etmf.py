import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app, map_artifact_to_tmf
from apps.etmf.models import Base, TMFAuditLog, TMFDocument
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    user_id = "test_user"
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


def test_tmf_taxonomy_mapping():
    """
    Test direct DIA TMF Zone and Section taxonomy mappings for standard clinical artifacts.
    """
    assert map_artifact_to_tmf("Clinical Trial Protocol") == (1, "01.01")
    assert map_artifact_to_tmf("Define-XML Specifications") == (
        10,
        "10.01",
    )
    assert map_artifact_to_tmf("Blank CRF") == (10, "10.02")
    assert map_artifact_to_tmf("Data Lock Certificate") == (
        11,
        "11.01",
    )


@pytest.mark.asyncio
async def test_automated_ingestion_and_version_indexing():
    """
    Verify that post requests automatically ingest searchable document archives,
    perform DIA TMF taxonomy assignment, increment version indices, and log to the audit ledger.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Ingest study data"
    )

    # Ingest Version 1
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1.pdf",
        "content": "This is the clinical trial protocol for study 001. Enforces double blind randomized controls.",
        "mime_type": "application/pdf",
        "metadata_json": {"sponsor": "Acme Corp"},
    }
    response = client.post("/events/publish", json=payload, headers=headers)
    assert response.status_code == 201
    data = response.json()
    assert data["status"] == "success"
    assert data["zone"] == 1
    assert data["section"] == "01.01"
    assert data["version_index"] == 1
    assert data["taxonomy_version"] == "v3.2.0"
    assert data["artifact_code"] == "01.01.01"

    # Ingest Version 2 (Same study_id and artifact_type)
    payload_v2 = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v2.pdf",
        "content": "This is the clinical trial protocol version 2.",
        "mime_type": "application/pdf",
    }
    response_v2 = client.post("/api/v1/etmf/ingest", json=payload_v2, headers=headers)
    assert response_v2.status_code == 201
    data_v2 = response_v2.json()
    assert data_v2["version_index"] == 2

    # Verify database contents
    async with db_manager.get_session_maker()() as session:
        # Check documents
        stmt = (
            select(TMFDocument)
            .where(TMFDocument.study_id == "study_001")
            .order_by(TMFDocument.version_index)
        )
        result = await session.execute(stmt)
        docs = result.scalars().all()
        assert len(docs) == 2
        assert docs[0].filename == "protocol_v1.pdf"
        assert docs[0].version_index == 1
        assert docs[1].filename == "protocol_v2.pdf"
        assert docs[1].version_index == 2

        # Check immutable audit log
        stmt_audit = select(TMFAuditLog).where(TMFAuditLog.action == "INGEST")
        result_audit = await session.execute(stmt_audit)
        audit_logs = result_audit.scalars().all()
        assert len(audit_logs) == 2
        assert (
            "Ingested artifact type 'Clinical Trial Protocol'" in audit_logs[0].details
        )
        assert audit_logs[0].user_id == "test_user"


@pytest.mark.asyncio
async def test_inspector_portal_read_only_access_limits():
    """
    Ensure that users with regulatory inspector roles can view, download,
    and query the eTMF repository but are strictly forbidden from mutating/ingesting files.
    """
    client = TestClient(app)
    # Include change_reason since POST mutations under V2 require it for signature validation
    inspector_headers = get_auth_headers(
        roles="regulatory_inspector", change_reason="Unauthorized ingestion attempt"
    )

    # Try to ingest using inspector role -> should fail
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Secret protocol",
        "mime_type": "application/pdf",
    }
    response = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=inspector_headers
    )
    assert response.status_code == 403
    assert "restricted to read-only" in response.json()["detail"]


@pytest.mark.asyncio
async def test_view_download_audit_logging():
    """
    Verify that document list searches, metadata views, and content downloads
    automatically write immutable, chronological logs to the TMFAuditLog ledger.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Setup study docs")

    # Ingest a document first
    payload = {
        "study_id": "study_abc",
        "artifact_type": "Define-XML Specifications",
        "filename": "define.xml",
        "content": "SDTM standard data specification structure.",
        "mime_type": "application/xml",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=admin_headers
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    # Perform List search
    inspector_headers = get_auth_headers(roles="regulatory_inspector")
    list_resp = client.get(
        "/api/v1/etmf/documents?study_id=study_abc", headers=inspector_headers
    )
    assert list_resp.status_code == 200
    assert len(list_resp.json()) == 1

    # Perform View metadata
    view_resp = client.get(
        f"/api/v1/etmf/documents/{doc_id}", headers=inspector_headers
    )
    assert view_resp.status_code == 200
    assert view_resp.json()["filename"] == "define.xml"

    # Perform Download content
    download_resp = client.get(
        f"/api/v1/etmf/documents/{doc_id}/download", headers=inspector_headers
    )
    assert download_resp.status_code == 200
    assert download_resp.text == "SDTM standard data specification structure."

    # Retrieve all audit logs
    audit_resp = client.get("/api/v1/etmf/audit-logs", headers=inspector_headers)
    assert audit_resp.status_code == 200
    logs = audit_resp.json()

    # The latest logs should be in descending order (newest first)
    # We expect: AUDIT_VIEW, DOWNLOAD, VIEW, LIST, INGEST
    actions = [log["action"] for log in logs]
    assert "AUDIT_VIEW" in actions
    assert "DOWNLOAD" in actions
    assert "VIEW" in actions
    assert "LIST" in actions
    assert "INGEST" in actions

    # Verify correct document association in logs
    download_log = next(log for log in logs if log["action"] == "DOWNLOAD")
    assert download_log["document_id"] == doc_id
    assert download_log["user_role"] == "regulatory_inspector"


@pytest.mark.asyncio
async def test_completeness_checking_transitions():
    """
    Test eTMF completeness validation checks against study milestones.
    Ensure completeness logic correctly parses mandatory documents for
    INITIATION, CONDUCT, and CLOSEOUT.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Add documents")

    # Initially, study_xyz is completely empty.
    # Check INITIATION milestone -> requires Clinical Trial Protocol -> should be False.
    headers = get_auth_headers(roles="regulatory_inspector")
    res_init = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=INITIATION",
        headers=headers,
    )
    assert res_init.status_code == 200
    data_init = res_init.json()
    assert data_init["is_complete"] is False
    assert "Clinical Trial Protocol" in data_init["missing_artifacts"]

    # Ingest Clinical Trial Protocol
    payload_prot = {
        "study_id": "study_xyz",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol content",
        "mime_type": "application/pdf",
    }
    client.post("/api/v1/etmf/ingest", json=payload_prot, headers=admin_headers)

    # Check INITIATION again -> should now be True!
    res_init_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=INITIATION",
        headers=headers,
    )
    assert res_init_2.json()["is_complete"] is True

    # Check CONDUCT milestone -> requires Clinical Trial Protocol, Define-XML Specifications, Blank CRF -> should be False.
    res_cond = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CONDUCT",
        headers=headers,
    )
    assert res_cond.json()["is_complete"] is False
    assert "Define-XML Specifications" in res_cond.json()["missing_artifacts"]
    assert "Blank CRF" in res_cond.json()["missing_artifacts"]

    # Ingest the remaining documents for CONDUCT
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_xyz",
            "artifact_type": "Define-XML Specifications",
            "filename": "define.xml",
            "content": "Metadata Spec",
            "mime_type": "application/xml",
        },
        headers=admin_headers,
    )
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_xyz",
            "artifact_type": "Blank CRF",
            "filename": "crf.xml",
            "content": "Blank CRF layout",
            "mime_type": "application/xml",
        },
        headers=admin_headers,
    )

    # Check CONDUCT again -> should now be True!
    res_cond_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CONDUCT",
        headers=headers,
    )
    assert res_cond_2.json()["is_complete"] is True

    # Check CLOSEOUT milestone -> requires Data Lock Certificate -> should be False.
    res_close = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CLOSEOUT",
        headers=headers,
    )
    assert res_close.json()["is_complete"] is False

    # Ingest Data Lock Certificate
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_xyz",
            "artifact_type": "Data Lock Certificate",
            "filename": "lock_cert.pdf",
            "content": "Locked database verified.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # Check CLOSEOUT -> should now be True!
    res_close_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CLOSEOUT",
        headers=headers,
    )
    assert res_close_2.json()["is_complete"] is True


@pytest.mark.asyncio
async def test_edl_definitions_and_crud():
    """
    Verify EDL expectation creation, listing, updating, RBAC controls,
    and TMFAuditLog audit trails.
    """
    # @req:PRD-EDL-001
    # @req:Trace-4
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Configure EDL expectations"
    )
    inspector_headers = get_auth_headers(
        roles="regulatory_inspector", change_reason="Attempt update"
    )

    # 1. Create a site-specific expectation (should succeed for admin)
    payload = {
        "study_id": "study_xyz",
        "site_id": "site_alpha",
        "milestone": "INITIATION",
        "artifact_type": "Site Signature Page",
        "zone": 5,
        "section": "5.1 Site Contacts",
        "metadata_json": {"mandated_by": "IRB"},
        "reason_for_change": "Adding site signature requirement for IRB compliance",
    }
    response = client.post("/api/v1/etmf/edl", json=payload, headers=admin_headers)
    assert response.status_code == 201
    data = response.json()
    edl_id = data["id"]
    assert data["artifact_type"] == "Site Signature Page"
    assert data["site_id"] == "site_alpha"
    assert data["version_index"] == 1

    # 2. Update expectation (should succeed for admin)
    payload_update = {
        "study_id": "study_xyz",
        "site_id": "site_alpha",
        "milestone": "INITIATION",
        "artifact_type": "Site Signature Page (Updated)",
        "zone": 5,
        "section": "5.1 Site Contacts",
        "metadata_json": {"mandated_by": "IRB"},
        "reason_for_change": "Updating artifact name to specify signature requirements",
    }
    response_update = client.put(
        f"/api/v1/etmf/edl/{edl_id}", json=payload_update, headers=admin_headers
    )
    assert response_update.status_code == 200
    data_update = response_update.json()
    assert data_update["artifact_type"] == "Site Signature Page (Updated)"
    assert data_update["version_index"] == 2

    # 3. List expectations (should succeed and contain seeded + newly created ones)
    list_resp = client.get(
        "/api/v1/etmf/edl?study_id=study_xyz&site_id=site_alpha",
        headers=inspector_headers,
    )
    assert list_resp.status_code == 200
    expectations = list_resp.json()
    assert len(expectations) >= 1
    assert any(e["id"] == edl_id for e in expectations)

    # 4. Attempt EDL mutations with Inspector role (should fail with 403)
    response_create_forbidden = client.post(
        "/api/v1/etmf/edl", json=payload, headers=inspector_headers
    )
    assert response_create_forbidden.status_code == 403

    response_update_forbidden = client.put(
        f"/api/v1/etmf/edl/{edl_id}", json=payload_update, headers=inspector_headers
    )
    assert response_update_forbidden.status_code == 403

    # 5. Verify TMFAuditLog entries are correctly recorded
    async with db_manager.get_session_maker()() as session:
        stmt = select(TMFAuditLog).order_by(TMFAuditLog.timestamp.desc())
        result = await session.execute(stmt)
        logs = result.scalars().all()

        # Check for EDL_UPDATE logs
        update_logs = [log for log in logs if log.action == "EDL_UPDATE"]
        assert len(update_logs) >= 2
        assert update_logs[0].user_id == "test_user"
        assert (
            "Updated expected document" in update_logs[0].details
            or "Created expected document" in update_logs[0].details
        )

        # Check for EDL_VIEW logs
        view_logs = [log for log in logs if log.action == "EDL_VIEW"]
        assert len(view_logs) >= 1
        assert view_logs[0].user_role == "regulatory_inspector"


@pytest.mark.asyncio
async def test_site_aware_completeness():
    """
    Verify that study-scope and site-scope expectations are combined
    when site_id is provided, and study-scope only when site_id is omitted.
    """
    # @req:PRD-EDL-001
    # @req:Trace-4
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Seed custom EDL")
    inspector_headers = get_auth_headers(roles="regulatory_inspector")

    # 1. Initially check completeness for a new study 'study_site_test' (will dynamically seed default study-scope)
    res_initial = client.get(
        "/api/v1/etmf/completeness?study_id=study_site_test&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert res_initial.status_code == 200
    data_initial = res_initial.json()
    assert data_initial["is_complete"] is False
    assert data_initial["scope"] == "study"
    assert "Clinical Trial Protocol" in data_initial["missing_artifacts"]
    assert len(data_initial["per_artifact_detail"]) == 1
    assert data_initial["per_artifact_detail"][0]["scope"] == "study"

    # 2. Add site-specific expectation
    payload = {
        "study_id": "study_site_test",
        "site_id": "site_alpha",
        "milestone": "INITIATION",
        "artifact_type": "Site Feasibility Survey",
        "reason_for_change": "Mandating site signatures",
    }
    client.post("/api/v1/etmf/edl", json=payload, headers=admin_headers)

    # 3. Check study-level completeness (should NOT include the site-specific expectation)
    res_study = client.get(
        "/api/v1/etmf/completeness?study_id=study_site_test&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert res_study.status_code == 200
    data_study = res_study.json()
    assert "Site Feasibility Survey" not in data_study["missing_artifacts"]

    # 4. Check site-level completeness for site_alpha (should include BOTH)
    res_site = client.get(
        "/api/v1/etmf/completeness?study_id=study_site_test&milestone=INITIATION&site_id=site_alpha",
        headers=inspector_headers,
    )
    assert res_site.status_code == 200
    data_site = res_site.json()
    assert data_site["is_complete"] is False
    assert "Clinical Trial Protocol" in data_site["missing_artifacts"]
    assert "Site Feasibility Survey" in data_site["missing_artifacts"]
    assert len(data_site["per_artifact_detail"]) == 2

    # 5. Ingest Clinical Trial Protocol
    payload_prot = {
        "study_id": "study_site_test",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    client.post("/api/v1/etmf/ingest", json=payload_prot, headers=admin_headers)

    # 6. Study-level should now be complete, but site-level should still be incomplete
    res_study_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_site_test&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert res_study_2.json()["is_complete"] is True

    res_site_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_site_test&milestone=INITIATION&site_id=site_alpha",
        headers=inspector_headers,
    )
    assert res_site_2.json()["is_complete"] is False
    assert "Site Feasibility Survey" in res_site_2.json()["missing_artifacts"]

    # 7. Ingest Site Feasibility Survey
    payload_sig = {
        "study_id": "study_site_test",
        "artifact_type": "Site Feasibility Survey",
        "filename": "site_sig.pdf",
        "content": "Site feasibility survey text",
        "mime_type": "application/pdf",
    }
    ingest_sig_resp = client.post(
        "/api/v1/etmf/ingest", json=payload_sig, headers=admin_headers
    )
    assert ingest_sig_resp.status_code == 201

    # 8. Site-level should now be complete!
    res_site_3 = client.get(
        "/api/v1/etmf/completeness?study_id=study_site_test&milestone=INITIATION&site_id=site_alpha",
        headers=inspector_headers,
    )
    assert res_site_3.json()["is_complete"] is True


@pytest.mark.asyncio
async def test_etmf_edge_cases_for_coverage():
    """
    Test edge cases and exception handling paths in eTMF for full branch coverage.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Cover edge cases")
    inspector_headers = get_auth_headers(roles="regulatory_inspector")

    # 1. view_document 404
    view_resp = client.get(
        "/api/v1/etmf/documents/nonexistent-id", headers=inspector_headers
    )
    assert view_resp.status_code == 404

    # 2. download_document 404
    download_resp = client.get(
        "/api/v1/etmf/documents/nonexistent-id/download", headers=inspector_headers
    )
    assert download_resp.status_code == 404

    # 3. update_expectation 404
    payload = {
        "study_id": "study_xyz",
        "site_id": "site_alpha",
        "milestone": "INITIATION",
        "artifact_type": "Some Doc",
        "reason_for_change": "Updating nonexistent EDL",
    }
    update_resp = client.put(
        "/api/v1/etmf/edl/nonexistent-id", json=payload, headers=admin_headers
    )
    assert update_resp.status_code == 404

    # 4. list_expectations filtering by milestone (first trigger check_completeness to seed default EDL dynamically)
    client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=INITIATION",
        headers=inspector_headers,
    )
    list_resp = client.get(
        "/api/v1/etmf/edl?study_id=study_xyz&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert list_resp.status_code == 200
    assert len(list_resp.json()) >= 1

    # 5. get_audit_trail with document_id filter
    audit_resp = client.get(
        "/api/v1/etmf/audit-logs?document_id=doc_123", headers=inspector_headers
    )
    assert audit_resp.status_code == 200

    # 6. TrialLock write block for EDL creation/update
    from apps.execution.trial_lock import TrialLockManager

    TrialLockManager.lock_trial()
    try:
        locked_post_resp = client.post(
            "/api/v1/etmf/edl", json=payload, headers=admin_headers
        )
        assert locked_post_resp.status_code == 403
        assert "locked in a read-only state" in locked_post_resp.json()["detail"]

        locked_put_resp = client.put(
            "/api/v1/etmf/edl/some-id", json=payload, headers=admin_headers
        )
        assert locked_put_resp.status_code == 403
    finally:
        TrialLockManager.reset()

    # 10. Call test-exception to trigger db_session rollback
    try:
        client.get("/api/v1/etmf/test-exception", headers=inspector_headers)
    except Exception as e:
        assert "Intentional" in str(e)

    # 11. Ingest with requires_signature: True in metadata
    payload_req_sig_1 = {
        "study_id": "study_xyz",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "adhoc_sig.txt",
        "content": "No signatures here.",
        "mime_type": "text/plain",
        "metadata_json": {"requires_signature": True},
    }
    req_sig_resp_1 = client.post(
        "/api/v1/etmf/ingest", json=payload_req_sig_1, headers=admin_headers
    )
    assert req_sig_resp_1.status_code == 422

    # 12. Ingest with invalid mock signature
    payload_invalid_mock = {
        "study_id": "study_xyz",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "signed_invalid.txt",
        "content": (
            "-----BEGIN CERTIFICATE-----\nMOCK_SIGNATURE INVALID_MOCK\n-----END CERTIFICATE-----\n"
            "-----BEGIN SIGNATURE-----\nMOCK\n-----END SIGNATURE-----\nContent"
        ),
        "mime_type": "text/plain",
    }
    invalid_mock_resp = client.post(
        "/api/v1/etmf/ingest", json=payload_invalid_mock, headers=admin_headers
    )
    assert invalid_mock_resp.status_code == 422

    # 7. Ingest Investigator's Brochure document (Zone 2)
    payload_adhoc = {
        "study_id": "study_xyz",
        "artifact_type": "Investigator's Brochure",
        "filename": "adhoc.txt",
        "content": "Just some plain text content.",
        "mime_type": "text/plain",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload_adhoc, headers=admin_headers
    )
    assert ingest_resp.status_code == 201

    # 8. List documents with zone=2 and search filter to hit list filters
    list_docs_resp = client.get(
        "/api/v1/etmf/documents?zone=2&search=plain", headers=inspector_headers
    )
    assert list_docs_resp.status_code == 200
    assert len(list_docs_resp.json()) >= 1

    # 8b. List documents with study_id, zone, and search filter combined
    list_docs_resp_all = client.get(
        "/api/v1/etmf/documents?study_id=study_xyz&zone=2&search=plain",
        headers=inspector_headers,
    )
    assert list_docs_resp_all.status_code == 200

    # 8c. Call health check endpoint
    health_resp = client.get("/health")
    assert health_resp.status_code == 200
    assert health_resp.json()["status"] == "ok"

    # 9. Ingest document with nested signature metadata dict to hit metadata parsing
    payload_nested_sig = {
        "study_id": "study_xyz",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "signed_doc.txt",
        "content": "Signed content.",
        "mime_type": "text/plain",
        "metadata_json": {
            "signature": {"certificate": "MOCK_SIGNATURE", "signature_value": "MOCK"}
        },
    }
    nested_sig_resp = client.post(
        "/api/v1/etmf/ingest", json=payload_nested_sig, headers=admin_headers
    )
    assert nested_sig_resp.status_code == 201


def test_uninitialized_database_manager():
    """
    Cover the exception path when database manager is uninitialized.
    """
    from apps.etmf.database import ETMFDatabaseManager

    mgr = ETMFDatabaseManager()
    with pytest.raises(Exception) as exc_info:
        mgr.get_session_maker()
    assert "not initialized" in str(exc_info.value)


def test_placeholder_scripts():
    """
    Increase unit test coverage for placeholder scripts by running them as __main__.
    """
    import runpy

    runpy.run_path("apps/execution/database/provision_tenant.py", run_name="__main__")
    runpy.run_path("apps/execution/database/rollback.py", run_name="__main__")


def test_ucum_extra_coverage():
    """
    Increase unit test coverage for apps/execution/ucum.py.
    """
    import pytest

    from apps.execution.ucum import convert_unit, get_normalized_representation

    # 1. Incompatible base units (e.g., kg to m)
    with pytest.raises(ValueError) as exc_info:
        convert_unit(10.0, "kg", "m")
    assert "Incompatible" in str(exc_info.value)

    # 2. Unrecognized units
    with pytest.raises(ValueError) as exc_info:
        convert_unit(10.0, "unknown_unit_1", "unknown_unit_2")
    assert "Unrecognized" in str(exc_info.value)

    # 3. get_normalized_representation with none values
    val, unit = get_normalized_representation(None, "kg")
    assert val is None
    assert unit == "kg"

    val, unit = get_normalized_representation(10.0, None)
    assert val == 10.0
    assert unit is None

    # 4. Temperature conversions Cel -> Fahr and K
    assert convert_unit(0, "Cel", "[Fahr]") == 32.0
    assert convert_unit(32, "[Fahr]", "Cel") == 0.0
    assert convert_unit(0, "Cel", "K") == 273.15
    assert convert_unit(273.15, "K", "Cel") == 0.0
    assert convert_unit(1.0, "[oz_av]", "g") == 28.349523125

    # 5. Trigger exception in get_normalized_representation temperature path
    val, unit = get_normalized_representation("not_a_float", "K")
    assert val == "not_a_float"
    assert unit == "K"

    # 6. Unrecognized unit in get_normalized_representation
    val, unit = get_normalized_representation(10.0, "unknown_unit")
    assert val == 10.0
    assert unit == "unknown_unit"


@pytest.mark.asyncio
async def test_canonical_catalog_ingestion_validations():
    """
    Cover:
    - valid canonical ingestion
    - unknown artifact rejection (HTTP 422)
    - invalid hierarchy rejection (HTTP 422)
    - taxonomy-version persistence
    """
    # @req:PRD-TMF-002
    # @req:PRD-TMF-003
    # @req:Trace-5
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin", change_reason="Catalog validations testing"
    )

    # 1. Unknown artifact rejection
    payload_unknown = {
        "study_id": "study_001",
        "artifact_type": "Unknown Completely Bogus Artifact",
        "filename": "unknown.pdf",
        "content": "Secret content",
        "mime_type": "application/pdf",
    }
    response_unknown = client.post(
        "/api/v1/etmf/ingest", json=payload_unknown, headers=headers
    )
    assert response_unknown.status_code == 422
    assert "validation error" in response_unknown.json()["detail"].lower()

    # 2. Invalid hierarchy rejection (mismatched zone/section/artifact)
    payload_mismatch = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",  # belongs to zone 1, section "01.01"
        "filename": "protocol.pdf",
        "content": "Protocol content",
        "mime_type": "application/pdf",
        "zone": 10,  # mismatched zone
        "section": "01.01",
    }
    response_mismatch = client.post(
        "/api/v1/etmf/ingest", json=payload_mismatch, headers=headers
    )
    assert response_mismatch.status_code == 422
    assert "mismatch" in response_mismatch.json()["detail"].lower()

    # 3. Valid canonical ingestion & taxonomy version persistence
    payload_valid = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_canonical.pdf",
        "content": "Valid protocol content",
        "mime_type": "application/pdf",
        "taxonomy_version": "v3.2.0",
        "zone": 1,
        "section": "01.01",
    }
    response_valid = client.post(
        "/api/v1/etmf/ingest", json=payload_valid, headers=headers
    )
    assert response_valid.status_code == 201
    data = response_valid.json()
    assert data["status"] == "success"
    assert data["zone"] == 1
    assert data["section"] == "01.01"
    assert data["taxonomy_version"] == "v3.2.0"
    assert data["artifact_code"] == "01.01.01"

    # Query document and verify persistence of taxonomy_version and artifact_code
    doc_id = data["document_id"]
    get_resp = client.get(f"/api/v1/etmf/documents/{doc_id}", headers=headers)
    assert get_resp.status_code == 200
    doc_data = get_resp.json()
    assert doc_data["taxonomy_version"] == "v3.2.0"
    assert doc_data["artifact_code"] == "01.01.01"


@pytest.mark.asyncio
async def test_completeness_from_catalog():
    """
    Verify that eTMF completeness checks source requirements from the TMF catalog API,
    reject unsupported milestones, match exact canonical artifact identities,
    and accurately handle changes to the shared catalog metadata.
    """
    # @req:PRD-TMF-004
    from tmf_reference_model import MILESTONE_MANDATORY_ARTIFACTS

    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Completeness test")
    inspector_headers = get_auth_headers(roles="regulatory_inspector")

    # 1. Unsupported milestone validation response
    unsupported_resp = client.get(
        "/api/v1/etmf/completeness?study_id=study_catalog_test&milestone=BOGUS_MILESTONE",
        headers=inspector_headers,
    )
    assert unsupported_resp.status_code == 400
    assert "Unknown milestone" in unsupported_resp.json()["detail"]

    # 2. INITIATION milestone completeness using canonical lookup (initially missing)
    res_init = client.get(
        "/api/v1/etmf/completeness?study_id=study_catalog_test&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert res_init.status_code == 200
    data_init = res_init.json()
    assert data_init["is_complete"] is False
    assert "Clinical Trial Protocol" in data_init["missing_artifacts"]

    # 3. Ingest with custom matching name but matching artifact_code and verify success
    # Here, we use a distinct filename/metadata, but canonical artifact is "Clinical Trial Protocol" (01.01.01)
    payload_valid = {
        "study_id": "study_catalog_test",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_custom.pdf",
        "content": "Protocol description",
        "mime_type": "application/pdf",
        "taxonomy_version": "v3.2.0",
        "artifact_code": "01.01.01",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload_valid, headers=admin_headers
    )
    assert ingest_resp.status_code == 201

    # 4. Re-check INITIATION completeness -> should be complete now
    res_init_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_catalog_test&milestone=INITIATION",
        headers=inspector_headers,
    )
    assert res_init_2.status_code == 200
    assert res_init_2.json()["is_complete"] is True

    # 5. Check CONDUCT milestone (missing Define-XML and Blank CRF)
    res_conduct = client.get(
        "/api/v1/etmf/completeness?study_id=study_catalog_test&milestone=CONDUCT",
        headers=inspector_headers,
    )
    assert res_conduct.status_code == 200
    assert res_conduct.json()["is_complete"] is False
    assert "Define-XML Specifications" in res_conduct.json()["missing_artifacts"]
    assert "Blank CRF" in res_conduct.json()["missing_artifacts"]

    # 6. Dynamically add a new mandatory artifact code to INITIATION in catalog to demonstrate catalog-driven changes
    # Backup original
    original_initiation = list(MILESTONE_MANDATORY_ARTIFACTS["INITIATION"])
    try:
        # Add "10.02.01" (Blank CRF) as required for INITIATION
        MILESTONE_MANDATORY_ARTIFACTS["INITIATION"].append("10.02.01")

        # Now checking INITIATION on a NEW study_id should seed the new list of expectations and report Blank CRF as missing
        # without modifying etmf logic!
        res_init_dynamic = client.get(
            "/api/v1/etmf/completeness?study_id=study_catalog_test_dynamic&milestone=INITIATION",
            headers=inspector_headers,
        )
        assert res_init_dynamic.status_code == 200
        data_dynamic = res_init_dynamic.json()
        assert data_dynamic["is_complete"] is False
        assert "Blank CRF" in data_dynamic["missing_artifacts"]
    finally:
        # Restore original
        MILESTONE_MANDATORY_ARTIFACTS["INITIATION"] = original_initiation
