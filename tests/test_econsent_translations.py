import os
import time
import asyncio
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from pydantic import ValidationError
from sqlalchemy import select
from unittest.mock import patch

from apps.econsent.database import db_manager
from apps.econsent.main import app, approved_translation_cache
from apps.econsent.models import Base, ConsentAuditLog, ConsentClause, ConsentTemplate, ConsentTranslation
from localization import validate_language_code
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_econsent_db():
    """
    Setup in-memory eConsent database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    # Clear cache before each test
    approved_translation_cache.clear()
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "consent_test_user",
    roles: str = "investigator",
    change_reason: str = "eConsent translation change",
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


def test_language_code_validation():
    """Verify standard language validation under packages/core-models/localization."""
    # Valid
    assert validate_language_code("es") == "es"
    assert validate_language_code("ES") == "es"
    assert validate_language_code("  fr  ") == "fr"
    assert validate_language_code("zh") == "zh"

    # Invalid
    with pytest.raises(ValueError):
        validate_language_code("invalid")
    with pytest.raises(ValueError):
        validate_language_code("")
    with pytest.raises(ValueError):
        validate_language_code(None)


@pytest.mark.asyncio
async def test_translation_crud_and_validation():
    """Verify submitting translations, versioning, and validation on source existence."""
    client = TestClient(app)

    # 1. Create source clause
    headers = get_auth_headers(roles="Grants Manager")
    clause_payload = {
        "clause_id": "clause-risk",
        "study_id": "study-1",
        "title": "Risks",
        "text": "Initial risks description.",
        "reason_for_change": "Drafting risks",
        "created_by": "author",
    }
    res = client.post("/api/v1/econsent/clauses", json=clause_payload, headers=headers)
    assert res.status_code == 201

    # 2. Try to create translation with invalid language code
    trans_payload = {
        "source_id": "clause-risk",
        "source_type": "clause",
        "source_version_index": 1,
        "language_code": "invalid-lang",
        "translated_title": "Riesgos",
        "translated_text": "Descripción de riesgos.",
        "reason_for_change": "Creating translation",
        "created_by": "translator",
    }
    res = client.post("/api/v1/econsent/translations", json=trans_payload, headers=headers)
    assert res.status_code == 422  # Pydantic validation error

    # 3. Try to create translation for non-existent source
    trans_payload["language_code"] = "es"
    trans_payload["source_id"] = "non-existent"
    res = client.post("/api/v1/econsent/translations", json=trans_payload, headers=headers)
    assert res.status_code == 400
    assert "not found" in res.json()["detail"]

    # 4. Create valid translation (starts at version 1, status DRAFT)
    trans_payload["source_id"] = "clause-risk"
    res = client.post("/api/v1/econsent/translations", json=trans_payload, headers=headers)
    assert res.status_code == 201
    data = res.json()
    assert data["translation_id"] is not None
    assert data["version_index"] == 1
    assert data["status"] == "DRAFT"
    assert data["translated_title"] == "Riesgos"

    translation_id = data["translation_id"]

    # 5. Update translation (increments version_index)
    update_payload = {
        "source_id": "clause-risk",
        "source_type": "clause",
        "source_version_index": 1,
        "language_code": "es",
        "translated_title": "Riesgos v2",
        "translated_text": "Descripción de riesgos actualizada.",
        "reason_for_change": "Updating translation content",
        "created_by": "translator",
    }
    res = client.put(f"/api/v1/econsent/translations/{translation_id}", json=update_payload, headers=headers)
    assert res.status_code == 200
    data = res.json()
    assert data["version_index"] == 2
    assert data["status"] == "DRAFT"
    assert data["translated_title"] == "Riesgos v2"

    # 6. Verify view list (defaults to latest version)
    res = client.get("/api/v1/econsent/translations?language_code=es", headers=headers)
    assert res.status_code == 200
    items = res.json()
    assert len(items) == 1
    assert items[0]["version_index"] == 2


@pytest.mark.asyncio
async def test_translation_status_workflow_and_rbac():
    """Verify DRAFT -> IN_REVIEW -> APPROVED transitions, role permissions, and invalid rejections."""
    client = TestClient(app)

    # Setup clause
    headers = get_auth_headers(roles="Grants Manager")
    clause_payload = {
        "clause_id": "clause-workflow",
        "study_id": "study-1",
        "title": "Workflow",
        "text": "Workflow clause",
        "reason_for_change": "Drafting",
        "created_by": "author",
    }
    client.post("/api/v1/econsent/clauses", json=clause_payload, headers=headers)

    # Create translation
    trans_payload = {
        "source_id": "clause-workflow",
        "source_type": "clause",
        "source_version_index": 1,
        "language_code": "fr",
        "translated_title": "Flux",
        "translated_text": "Flux de travail",
        "reason_for_change": "Translation draft",
        "created_by": "translator",
    }
    res = client.post("/api/v1/econsent/translations", json=trans_payload, headers=headers)
    translation_id = res.json()["translation_id"]

    # 1. Invalid direct transition: DRAFT -> APPROVED is rejected
    res = client.post(
        f"/api/v1/econsent/translations/{translation_id}/transition",
        json={"status": "APPROVED", "reason_for_change": "Approving"},
        headers=headers,
    )
    assert res.status_code == 400
    assert "Invalid translation status transition" in res.json()["detail"]

    # 2. Valid transition: DRAFT -> IN_REVIEW
    res = client.post(
        f"/api/v1/econsent/translations/{translation_id}/transition",
        json={"status": "IN_REVIEW", "reason_for_change": "Sending to review"},
        headers=headers,
    )
    assert res.status_code == 200
    assert res.json()["status"] == "IN_REVIEW"

    # 3. Valid transition: IN_REVIEW -> APPROVED
    res = client.post(
        f"/api/v1/econsent/translations/{translation_id}/transition",
        json={"status": "APPROVED", "reason_for_change": "Looks great, approved!"},
        headers=headers,
    )
    assert res.status_code == 200
    assert res.json()["status"] == "APPROVED"

    # 4. Blocked write/transition for auditor personas
    headers_auditor = get_auth_headers(roles="regulatory_inspector")
    res = client.post(
        f"/api/v1/econsent/translations/{translation_id}/transition",
        json={"status": "DRAFT", "reason_for_change": "Attempting rejection"},
        headers=headers_auditor,
    )
    assert res.status_code == 403


@pytest.mark.asyncio
async def test_approved_content_retrieval_and_cache():
    """Verify only approved translations are accessible, cache hits, expiration, and stale-on-error fallback."""
    client = TestClient(app)
    headers = get_auth_headers(roles="Grants Manager")

    # 1. Seed clause and template
    # Clause
    client.post("/api/v1/econsent/clauses", json={
        "clause_id": "c1",
        "study_id": "study-1",
        "title": "Clause Title",
        "text": "Clause Text.",
        "reason_for_change": "seed",
        "created_by": "admin"
    }, headers=headers)

    # Template
    client.post("/api/v1/econsent/templates", json={
        "template_id": "t1",
        "study_id": "study-1",
        "template_name": "Consent Template",
        "protocol_version": "v1.0",
        "requires_reconsent": True,
        "clauses": ["c1"],
        "workflow_steps": [
            {"type": "comprehension_check", "question": "Ok?"},
            {"type": "signature_placeholder", "role": "subject"}
        ],
        "reason_for_change": "seed",
        "created_by": "admin"
    }, headers=headers)

    # Publish template
    client.post("/api/v1/econsent/templates/t1/publish", headers=headers)

    # 2. Before any translation, approved-content returns 404
    res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
    assert res.status_code == 404

    # 3. Create unapproved translations (status DRAFT)
    # Template Translation
    res = client.post("/api/v1/econsent/translations", json={
        "source_id": "t1",
        "source_type": "template",
        "source_version_index": 1,
        "language_code": "es",
        "translated_title": "Formulario de Consentimiento",
        "translated_text": "",
        "reason_for_change": "Spanish tpl",
        "created_by": "translator"
    }, headers=headers)
    t_trans_id = res.json()["translation_id"]

    # Clause Translation
    res = client.post("/api/v1/econsent/translations", json={
        "source_id": "c1",
        "source_type": "clause",
        "source_version_index": 1,
        "language_code": "es",
        "translated_title": "Título de Cláusula",
        "translated_text": "Texto de cláusula.",
        "reason_for_change": "Spanish clause",
        "created_by": "translator"
    }, headers=headers)
    c_trans_id = res.json()["translation_id"]

    # Retrieval still returns 404 since translations are in DRAFT
    res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
    assert res.status_code == 404

    # 4. Transition both to APPROVED
    for tid in (t_trans_id, c_trans_id):
        client.post(f"/api/v1/econsent/translations/{tid}/transition", json={
            "status": "IN_REVIEW", "reason_for_change": "Reviewing"
        }, headers=headers)
        client.post(f"/api/v1/econsent/translations/{tid}/transition", json={
            "status": "APPROVED", "reason_for_change": "Approving"
        }, headers=headers)

    # 5. Fetch approved-content (success!)
    res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
    assert res.status_code == 200
    composed = res.json()
    assert composed["template_name"] == "Formulario de Consentimiento"
    assert composed["clauses"][0]["title"] == "Título de Cláusula"
    assert composed["clauses"][0]["text"] == "Texto de cláusula."

    # Verify cache size is 1
    assert approved_translation_cache.get_status()["size"] == 1

    # 6. Verify cache hit by patching the DB composer
    with patch("apps.econsent.main.fetch_composed_translation_from_db") as mock_fetch:
        res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
        assert res.status_code == 200
        mock_fetch.assert_not_called()

    # 7. Test cache TTL and expiration behavior
    with patch.object(approved_translation_cache, "ttl", 0.05):
        # Wait for TTL to expire
        time.sleep(0.06)
        # Fetch again -> should trigger DB reload (we'll patch the fetch to verify it gets called)
        with patch("apps.econsent.main.fetch_composed_translation_from_db") as mock_fetch:
            mock_fetch.return_value = composed
            res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
            assert res.status_code == 200
            mock_fetch.assert_called_once()

    # 8. Test stale-on-error fallback
    # Populate cache, wait for TTL to expire, then throw exception on DB fetch.
    # Cache should fallback to expired entry.
    approved_translation_cache.clear()
    # Populate
    res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
    assert res.status_code == 200

    with patch.object(approved_translation_cache, "ttl", 0.01):
        time.sleep(0.02)
        # DB becomes unreachable
        with patch("apps.econsent.main.fetch_composed_translation_from_db", side_effect=Exception("DB Unreachable")):
            res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
            assert res.status_code == 200  # Returns the stale/expired entry successfully!
            assert res.json()["template_name"] == "Formulario de Consentimiento"

    # 9. Test Invalidation on updated/newly approved version
    # Verify that creating a new approved clause translation clears or invalidates the cache
    # First, update the clause translation to trigger next version
    res = client.put(f"/api/v1/econsent/translations/{c_trans_id}", json={
        "source_id": "c1",
        "source_type": "clause",
        "source_version_index": 1,
        "language_code": "es",
        "translated_title": "Título de Cláusula Nuevo",
        "translated_text": "Texto de cláusula nuevo.",
        "reason_for_change": "Update clause translation",
        "created_by": "translator"
    }, headers=headers)
    new_c_trans_version_id = res.json()["id"]

    # Transition the new version of clause translation to APPROVED
    # Draft is already created. Let's transition it.
    client.post(f"/api/v1/econsent/translations/{c_trans_id}/transition", json={
        "status": "IN_REVIEW", "reason_for_change": "Reviewing new version"
    }, headers=headers)

    # Prior to approving, the cache has size 1 (the stale/expired item is preserved)
    # Once we approve, it must clear the cache so the next request pulls the updated translation!
    client.post(f"/api/v1/econsent/translations/{c_trans_id}/transition", json={
        "status": "APPROVED", "reason_for_change": "Approving new version"
    }, headers=headers)

    # Cache should be cleared (size is 0)
    assert approved_translation_cache.get_status()["size"] == 0

    # Next fetch will retrieve the new composed version
    res = client.get("/api/v1/econsent/templates/t1/approved-content?language_code=es", headers=headers)
    assert res.status_code == 200
    assert res.json()["clauses"][0]["title"] == "Título de Cláusula Nuevo"
    assert res.json()["clauses"][0]["text"] == "Texto de cláusula nuevo."
