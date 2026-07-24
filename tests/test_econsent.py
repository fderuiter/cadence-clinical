import time
from datetime import datetime, timezone

import pytest
import pytest_asyncio
from audit import AuditFields
from fastapi.testclient import TestClient
from pydantic import ValidationError
from sqlalchemy import select

from apps.econsent.database import EConsentDatabaseManager, db_manager
from apps.econsent.main import ConsentDocumentCreate, app
from apps.econsent.models import (
    Base,
    ConsentAuditLog,
    ConsentClause,
    ConsentDocument,
    ConsentTemplate,
)
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_econsent_db():
    """
    Setup in-memory eConsent database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "consent_test_user",
    roles: str = "investigator",
    change_reason: str = "eConsent initial creation",
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for eConsent testing.
    """
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


def test_econsent_health_check():
    """
    Verify health check of independent eConsent service is publicly accessible
    and bypasses GatewayAuthMiddleware checks.
    """
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["service"] == "econsent"


@pytest.mark.asyncio
async def test_econsent_database_schema_creation():
    """
    Verify that eConsent tables are properly created and can be queried.
    """
    async with db_manager.get_session_maker()() as session:
        docs_res = await session.execute(select(ConsentDocument))
        audit_res = await session.execute(select(ConsentAuditLog))

        assert docs_res.scalars().all() == []
        assert audit_res.scalars().all() == []


def test_uninitialized_database_manager_econsent():
    """
    Ensure the eConsent database manager raises an exception when accessed before initialization.
    """
    mgr = EConsentDatabaseManager()
    with pytest.raises(Exception) as exc_info:
        mgr.get_session_maker()
    assert "eConsent database session manager is not initialized" in str(exc_info.value)


@pytest.mark.asyncio
async def test_database_url_override_and_init(monkeypatch):
    """
    Verify that database lifecycle supports ECONSENT_DATABASE_URL override.
    """
    monkeypatch.setenv("ECONSENT_DATABASE_URL", "sqlite+aiosqlite:///:memory:")
    mgr = EConsentDatabaseManager()
    mgr.init_db()  # Should pick up ECONSENT_DATABASE_URL from env
    assert mgr.engine is not None
    assert mgr.session_maker is not None
    await mgr.close()


def test_shared_audit_fields_validation():
    """
    Verify that AuditFields can be successfully instantiated with valid fields,
    and validates reason_for_change appropriately.
    """
    # Happy Path
    audit = AuditFields(
        created_by="user_456",
        reason_for_change="Initial creation",
    )
    assert audit.created_by == "user_456"
    assert audit.reason_for_change == "Initial creation"
    assert audit.version_index == 1
    assert isinstance(audit.created_at, datetime)

    # Check timezone is UTC
    assert abs((datetime.now(timezone.utc) - audit.created_at).total_seconds()) < 5

    # Validation: empty/blank reason
    with pytest.raises(ValidationError) as excinfo:
        AuditFields(
            created_by="user_456",
            reason_for_change="   ",
        )
    assert "Reason for change cannot be empty or consist only of whitespace." in str(
        excinfo.value
    )


def test_econsent_pydantic_schemas():
    """
    Verify that eConsent API schemas inherit from and enforce the shared AuditFields base.
    """
    # 1. Test ConsentDocumentCreate validation
    with pytest.raises(ValidationError) as excinfo:
        ConsentDocumentCreate(
            created_by="user_test",
            reason_for_change=" ",  # invalid
            study_id="study-01",
            site_id="site-01",
            document_name="Consent Form V1",
            content="Consent terms and details.",
        )
    assert "Reason for change cannot be empty" in str(excinfo.value)

    # Valid schema creation
    create_schema = ConsentDocumentCreate(
        created_by="user_test",
        reason_for_change="Legitimate creation of consent template",
        study_id="study-01",
        site_id="site-01",
        document_name="Consent Form V1",
        content="Consent terms and details.",
    )
    assert create_schema.study_id == "study-01"
    assert create_schema.reason_for_change == "Legitimate creation of consent template"


def test_gateway_auth_middleware_denials():
    """
    Verify that GatewayAuthMiddleware blocks requests with missing or invalid headers.
    """
    client = TestClient(app)

    # No headers on mutation -> 403 Forbidden
    response = client.post("/api/v1/econsent/documents", json={})
    assert response.status_code == 403
    assert "Missing gateway authentication headers" in response.json()["detail"]

    # No headers on GET -> 401 Unauthorized
    response = client.get("/api/v1/econsent/documents/some-uuid")
    assert response.status_code == 401
    assert "Missing gateway authentication headers" in response.json()["detail"]

    # Invalid signature -> 403 Forbidden on mutation
    headers = {
        "X-User-Id": "test_user",
        "X-User-Roles": "investigator",
        "X-Gateway-Timestamp": str(time.time()),
        "X-Gateway-Signature": "bad-sig",
        "X-Signature-Version": "2",
        "X-Change-Reason": "Some change",
    }
    response = client.post("/api/v1/econsent/documents", json={}, headers=headers)
    assert response.status_code == 403
    assert "Invalid gateway signature" in response.json()["detail"]


def test_econsent_document_lifecycle_and_audit_context():
    """
    Verify that calling eConsent document endpoints with valid gateway signatures
    successfully populates the request audit context, saves documents with exact audit fields,
    and appends correct Part 11 entries to the immutable ConsentAuditLog.
    """
    client = TestClient(app)

    # 1. Create a document
    doc_payload = {
        "created_by": "test_author",
        "reason_for_change": "Initial creation reason",
        "version_index": 1,
        "study_id": "study-xyz",
        "site_id": "site-999",
        "document_name": "Informed Consent Form V2.0",
        "content": "I hereby consent to participate in this clinical investigation.",
    }

    headers = get_auth_headers(
        user_id="consent_creator",
        roles="Grants Manager",
        change_reason="Deploying new consent form version",
    )

    response = client.post(
        "/api/v1/econsent/documents", json=doc_payload, headers=headers
    )
    assert response.status_code == 201
    created_data = response.json()

    assert created_data["id"] is not None
    assert created_data["study_id"] == "study-xyz"
    assert created_data["site_id"] == "site-999"
    assert created_data["document_name"] == "Informed Consent Form V2.0"
    assert created_data["content"].startswith("I hereby consent")

    # Created_by and reason_for_change are extracted from the gateway headers
    assert created_data["created_by"] == "consent_creator"
    assert created_data["reason_for_change"] == "Deploying new consent form version"
    assert created_data["version_index"] == 1

    doc_id = created_data["id"]

    # 2. Retrieve/View the document
    view_headers = get_auth_headers(
        user_id="consent_viewer",
        roles="Monitor",
        change_reason="Audit verification read",
    )
    response = client.get(f"/api/v1/econsent/documents/{doc_id}", headers=view_headers)
    assert response.status_code == 200
    retrieved_data = response.json()
    assert retrieved_data["id"] == doc_id
    assert retrieved_data["document_name"] == "Informed Consent Form V2.0"

    # 3. Verify Database and Part 11 Audit Trail Entries
    async def verify_db():
        async with db_manager.get_session_maker()() as session:
            # Check document state in DB
            stmt = select(ConsentDocument).where(ConsentDocument.id == doc_id)
            doc_res = await session.execute(stmt)
            db_doc = doc_res.scalar_one()
            assert db_doc.created_by == "consent_creator"
            assert db_doc.reason_for_change == "Deploying new consent form version"

            # Check audit logs in chronological order
            audit_stmt = select(ConsentAuditLog).order_by(
                ConsentAuditLog.timestamp.asc()
            )
            audit_res = await session.execute(audit_stmt)
            logs = audit_res.scalars().all()

            assert len(logs) == 2

            # Log 1: CREATE_DOCUMENT
            create_log = logs[0]
            assert create_log.actor_id == "consent_creator"
            assert create_log.actor_role == "Grants Manager"
            assert create_log.action == "CREATE_DOCUMENT"
            assert create_log.document_id == doc_id
            assert "Created consent document" in create_log.details
            assert create_log.reason_for_change == "Deploying new consent form version"

            # Log 2: VIEW_DOCUMENT
            view_log = logs[1]
            assert view_log.actor_id == "consent_viewer"
            assert view_log.actor_role == "Monitor"
            assert view_log.action == "VIEW_DOCUMENT"
            assert view_log.document_id == doc_id
            assert "Viewed consent document" in view_log.details
            assert view_log.reason_for_change == "Audit verification read"

    # Run the verification block
    import asyncio

    asyncio.run(verify_db())


def test_econsent_get_not_found():
    """
    Verify 404 behavior for non-existent eConsent documents.
    """
    client = TestClient(app)
    headers = get_auth_headers()
    response = client.get(
        "/api/v1/econsent/documents/non-existent-uuid", headers=headers
    )
    assert response.status_code == 404
    assert "not found" in response.json()["detail"].lower()


@pytest.mark.asyncio
async def test_clause_lifecycle_and_versioning_audit():
    """
    Test versioned ICF clause creation, update, listing, retrieval,
    and verify that edits never mutate historical versions.
    """
    client = TestClient(app)

    # 1. Create a clause (version 1)
    clause_payload = {
        "clause_id": "clause-risk-disclosure",
        "study_id": "study-101",
        "title": "Risk Disclosure V1",
        "text": "These are the initial risks of the trial.",
        "reason_for_change": "Initial clause drafting",
        "created_by": "test_auth",
    }
    headers = get_auth_headers(
        user_id="designer_user",
        roles="Grants Manager",
        change_reason="Drafting risk clause",
    )

    response = client.post(
        "/api/v1/econsent/clauses", json=clause_payload, headers=headers
    )
    assert response.status_code == 201
    created_clause = response.json()
    assert created_clause["clause_id"] == "clause-risk-disclosure"
    assert created_clause["version_index"] == 1
    assert created_clause["title"] == "Risk Disclosure V1"
    assert created_clause["reason_for_change"] == "Drafting risk clause"

    # 2. Update the clause (creates version 2)
    update_payload = {
        "study_id": "study-101",
        "title": "Risk Disclosure V2",
        "text": "These are the expanded risks of the trial.",
        "reason_for_change": "Updating risks with more detail",
        "created_by": "test_auth",
    }
    headers_update = get_auth_headers(
        user_id="designer_user_2",
        roles="Grants Manager",
        change_reason="Expanded risk details",
    )

    response = client.put(
        "/api/v1/econsent/clauses/clause-risk-disclosure",
        json=update_payload,
        headers=headers_update,
    )
    assert response.status_code == 200
    updated_clause = response.json()
    assert updated_clause["clause_id"] == "clause-risk-disclosure"
    assert updated_clause["version_index"] == 2
    assert updated_clause["title"] == "Risk Disclosure V2"
    assert updated_clause["reason_for_change"] == "Expanded risk details"

    # 3. Verify immutability of historical versions in DB
    async with db_manager.get_session_maker()() as session:
        # Retrieve version 1 from DB
        stmt1 = select(ConsentClause).where(
            ConsentClause.clause_id == "clause-risk-disclosure",
            ConsentClause.version_index == 1,
        )
        res1 = await session.execute(stmt1)
        clause_v1 = res1.scalar_one()
        assert clause_v1.title == "Risk Disclosure V1"
        assert clause_v1.text == "These are the initial risks of the trial."

        # Retrieve version 2 from DB
        stmt2 = select(ConsentClause).where(
            ConsentClause.clause_id == "clause-risk-disclosure",
            ConsentClause.version_index == 2,
        )
        res2 = await session.execute(stmt2)
        clause_v2 = res2.scalar_one()
        assert clause_v2.title == "Risk Disclosure V2"
        assert clause_v2.text == "These are the expanded risks of the trial."

    # 4. List clauses (should default to latest version)
    headers_list = get_auth_headers(
        user_id="list_user", roles="Monitor", change_reason="Reviewing clause list"
    )
    response = client.get(
        "/api/v1/econsent/clauses?study_id=study-101", headers=headers_list
    )
    assert response.status_code == 200
    clauses = response.json()
    assert len(clauses) == 1
    assert clauses[0]["version_index"] == 2
    assert clauses[0]["title"] == "Risk Disclosure V2"

    # List clauses with all_versions=True
    response = client.get(
        "/api/v1/econsent/clauses?study_id=study-101&all_versions=true",
        headers=headers_list,
    )
    assert response.status_code == 200
    all_clauses = response.json()
    assert len(all_clauses) == 2

    # 5. Retrieve specific version
    headers_view = get_auth_headers(
        user_id="view_user", roles="Monitor", change_reason="View clause v1"
    )
    response = client.get(
        "/api/v1/econsent/clauses/clause-risk-disclosure?version_index=1",
        headers=headers_view,
    )
    assert response.status_code == 200
    assert response.json()["version_index"] == 1
    assert response.json()["title"] == "Risk Disclosure V1"

    # 6. Verify Part 11 Audit Trail Logs for INGEST, UPDATE, VIEW, LIST
    async with db_manager.get_session_maker()() as session:
        audit_stmt = select(ConsentAuditLog).order_by(ConsentAuditLog.timestamp.asc())
        audit_res = await session.execute(audit_stmt)
        logs = audit_res.scalars().all()

        # Check action types
        actions = [log.action for log in logs]
        assert "INGEST" in actions
        assert "UPDATE" in actions
        assert "LIST" in actions
        assert "VIEW" in actions


@pytest.mark.asyncio
async def test_template_lifecycle_and_validation():
    """
    Test versioned ConsentTemplate creation, update, composition, and publish workflows,
    including validation of referenced clauses and workflow steps.
    """
    client = TestClient(app)

    # Pre-create a clause to reference
    clause_payload = {
        "clause_id": "clause-01",
        "study_id": "study-202",
        "title": "Required Clause",
        "text": "Some required consent disclosure.",
        "reason_for_change": "Seeding clause",
        "created_by": "test_auth",
    }
    headers = get_auth_headers(
        user_id="admin_user", roles="Grants Manager", change_reason="Seeding clause"
    )
    response = client.post(
        "/api/v1/econsent/clauses", json=clause_payload, headers=headers
    )
    assert response.status_code == 201

    # 1. Create a template (draft, version 1)
    template_payload = {
        "template_id": "template-icf",
        "study_id": "study-202",
        "template_name": "Informed Consent Form",
        "protocol_version": "v1.0",
        "requires_reconsent": True,
        "clauses": ["clause-01"],
        "workflow_steps": [
            {"type": "comprehension_check", "question": "Understood?"},
            {"type": "signature_placeholder", "role": "subject"},
        ],
        "reason_for_change": "Initial template creation",
        "created_by": "admin_user",
    }
    headers_tpl = get_auth_headers(
        user_id="designer_user",
        roles="Grants Manager",
        change_reason="Creating template draft",
    )
    response = client.post(
        "/api/v1/econsent/templates", json=template_payload, headers=headers_tpl
    )
    assert response.status_code == 201
    tpl = response.json()
    assert tpl["template_id"] == "template-icf"
    assert tpl["version_index"] == 1
    assert tpl["is_published"] is False

    # 2. Test Compose Template
    headers_compose = get_auth_headers(
        user_id="viewer_user", roles="Monitor", change_reason="Composing template"
    )
    response = client.get(
        "/api/v1/econsent/templates/template-icf/compose", headers=headers_compose
    )
    assert response.status_code == 200
    composed = response.json()
    assert composed["template_id"] == "template-icf"
    assert len(composed["clauses"]) == 1
    assert composed["clauses"][0]["clause_id"] == "clause-01"
    assert (
        composed["clauses"][0]["text"] == "Some required consent disclosure."
    )  # Fully resolved!

    # 3. Test update preserves previous version
    update_payload = {
        "study_id": "study-202",
        "template_name": "Informed Consent Form - Rev 1",
        "protocol_version": "v1.1",
        "requires_reconsent": True,
        "clauses": ["clause-01"],
        "workflow_steps": [
            {"type": "comprehension_check", "question": "Understood?"},
            {"type": "signature_placeholder", "role": "subject"},
        ],
        "reason_for_change": "Adding more details",
        "created_by": "designer_user",
    }
    headers_update = get_auth_headers(
        user_id="designer_user",
        roles="Grants Manager",
        change_reason="Template revision 1",
    )
    response = client.put(
        "/api/v1/econsent/templates/template-icf",
        json=update_payload,
        headers=headers_update,
    )
    assert response.status_code == 200
    assert response.json()["version_index"] == 2
    assert response.json()["protocol_version"] == "v1.1"

    # Verify both v1 and v2 are in DB separately
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ConsentTemplate)
            .where(ConsentTemplate.template_id == "template-icf")
            .order_by(ConsentTemplate.version_index.asc())
        )
        res = await session.execute(stmt)
        versions = res.scalars().all()
        assert len(versions) == 2
        assert versions[0].protocol_version == "v1.0"
        assert versions[1].protocol_version == "v1.1"

    # 4. Publishing Validations: referenced clauses
    invalid_tpl_payload = {
        "template_id": "template-invalid",
        "study_id": "study-202",
        "template_name": "Invalid Form",
        "protocol_version": "v1.0",
        "requires_reconsent": False,
        "clauses": ["clause-nonexistent"],  # Doesn't exist!
        "workflow_steps": [
            {"type": "comprehension_check"},
            {"type": "signature_placeholder"},
        ],
        "reason_for_change": "Invalid clauses template",
        "created_by": "designer_user",
    }
    response = client.post(
        "/api/v1/econsent/templates", json=invalid_tpl_payload, headers=headers_tpl
    )
    assert response.status_code == 201

    headers_pub = get_auth_headers(
        user_id="publisher_user",
        roles="Grants Manager",
        change_reason="Publishing template",
    )
    response = client.post(
        "/api/v1/econsent/templates/template-invalid/publish", headers=headers_pub
    )
    assert response.status_code == 400
    assert (
        "Referenced clause 'clause-nonexistent' does not exist"
        in response.json()["detail"]
    )

    # 5. Publishing Validations: missing comprehension check
    missing_comp_payload = {
        "template_id": "template-missing-comp",
        "study_id": "study-202",
        "template_name": "Missing Comp Form",
        "protocol_version": "v1.0",
        "requires_reconsent": False,
        "clauses": ["clause-01"],
        "workflow_steps": [
            {"type": "signature_placeholder"},  # Missing comprehension check
        ],
        "reason_for_change": "Missing comp template",
        "created_by": "designer_user",
    }
    response = client.post(
        "/api/v1/econsent/templates", json=missing_comp_payload, headers=headers_tpl
    )
    assert response.status_code == 201

    response = client.post(
        "/api/v1/econsent/templates/template-missing-comp/publish",
        headers=headers_pub,
    )
    assert response.status_code == 400
    assert "comprehension-check" in response.json()["detail"]

    # 6. Publishing Validations: missing signature placeholder
    missing_sig_payload = {
        "template_id": "template-missing-sig",
        "study_id": "study-202",
        "template_name": "Missing Sig Form",
        "protocol_version": "v1.0",
        "requires_reconsent": False,
        "clauses": ["clause-01"],
        "workflow_steps": [
            {"type": "comprehension_check"},  # Missing signature placeholder
        ],
        "reason_for_change": "Missing sig template",
        "created_by": "designer_user",
    }
    response = client.post(
        "/api/v1/econsent/templates", json=missing_sig_payload, headers=headers_tpl
    )
    assert response.status_code == 201

    response = client.post(
        "/api/v1/econsent/templates/template-missing-sig/publish",
        headers=headers_pub,
    )
    assert response.status_code == 400
    assert "signature placeholder" in response.json()["detail"]

    # 7. Successful publish
    response = client.post(
        "/api/v1/econsent/templates/template-icf/publish", headers=headers_pub
    )
    assert response.status_code == 200
    assert response.json()["is_published"] is True


def test_authoring_mutations_rejected_for_auditors():
    """
    Test that write operations are rejected with HTTP 403 when the user has
    auditor, inspector, or regulatory_inspector roles.
    """
    client = TestClient(app)

    clause_payload = {
        "clause_id": "clause-forbidden",
        "study_id": "study-101",
        "title": "Forbidden Clause",
        "text": "Auditors should not be writing this.",
        "reason_for_change": "Unauthorized write",
        "created_by": "auditor_user",
    }

    # inspector role -> 403 Forbidden
    headers_inspector = get_auth_headers(
        user_id="inspector_user",
        roles="inspector",
        change_reason="Should be blocked",
    )
    response = client.post(
        "/api/v1/econsent/clauses", json=clause_payload, headers=headers_inspector
    )
    assert response.status_code == 403
    assert (
        "Auditor personas are restricted to read-only access"
        in response.json()["detail"]
    )

    # regulatory_inspector role -> 403 Forbidden
    headers_regulatory = get_auth_headers(
        user_id="reg_user",
        roles="regulatory_inspector",
        change_reason="Should be blocked",
    )
    response = client.post(
        "/api/v1/econsent/clauses", json=clause_payload, headers=headers_regulatory
    )
    assert response.status_code == 403
    assert (
        "Auditor personas are restricted to read-only access"
        in response.json()["detail"]
    )
