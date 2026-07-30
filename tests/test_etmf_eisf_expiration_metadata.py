import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import inspect

from apps.eisf.database import db_manager as eisf_db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base as EISFBase
from apps.etmf.database import db_manager as etmf_db_manager
from apps.etmf.main import app as etmf_app
from apps.etmf.models import Base as ETMFBase
from apps.gateway.main import generate_signature
from packages.security.rbac import (
    ROLE_AUDITOR_CANONICAL,
    ROLE_CRA_CANONICAL,
    ROLE_CRC,
    ROLE_INVESTIGATOR,
    ROLE_SPONSOR_DM,
    ROLE_SYSADMIN,
    Principal,
    has_permission,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_test_databases():
    """Setup in-memory SQLite databases for eTMF and eISF testing."""
    etmf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(ETMFBase.metadata.create_all)

    eisf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with eisf_db_manager.engine.begin() as conn:
        await conn.run_sync(EISFBase.metadata.create_all)

    yield

    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(ETMFBase.metadata.drop_all)
    await etmf_db_manager.close()

    async with eisf_db_manager.engine.begin() as conn:
        await conn.run_sync(EISFBase.metadata.drop_all)
    await eisf_db_manager.close()


def get_auth_headers(
    user_id: str = "test_user",
    roles: str = "admin",
    site_id: str = None,
    change_reason: str = "Authorized change for testing",
) -> dict:
    """Helper to generate valid gateway V2 signed headers for testing."""
    timestamp = str(time.time())
    sig = generate_signature(
        user_id,
        roles,
        timestamp,
        version="2",
        change_reason=change_reason,
        site_id=site_id,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    return headers


# ==========================================
# 1. RBAC / Permission Declarative Tests
# ==========================================


def test_manage_expiration_rbac_permissions() -> None:
    """Verify that only authorized roles have the manage_expiration permission."""
    # Authorized roles
    assert (
        has_permission(
            Principal(user_id="p1", roles=[ROLE_SPONSOR_DM]),
            "etmf_document:manage_expiration",
        )
        is True
    )
    assert (
        has_permission(
            Principal(user_id="p2", roles=[ROLE_SYSADMIN]),
            "etmf_document:manage_expiration",
        )
        is True
    )
    assert (
        has_permission(
            Principal(user_id="p3", roles=["admin"]), "etmf_document:manage_expiration"
        )
        is True
    )
    assert (
        has_permission(
            Principal(user_id="p4", roles=["system"]), "etmf_document:manage_expiration"
        )
        is True
    )

    # Unauthorized roles
    assert (
        has_permission(
            Principal(user_id="p5", roles=[ROLE_CRC]), "etmf_document:manage_expiration"
        )
        is False
    )
    assert (
        has_permission(
            Principal(user_id="p6", roles=[ROLE_INVESTIGATOR]),
            "etmf_document:manage_expiration",
        )
        is False
    )
    assert (
        has_permission(
            Principal(user_id="p7", roles=[ROLE_CRA_CANONICAL]),
            "etmf_document:manage_expiration",
        )
        is False
    )
    assert (
        has_permission(
            Principal(user_id="p8", roles=[ROLE_AUDITOR_CANONICAL]),
            "etmf_document:manage_expiration",
        )
        is False
    )


# ==========================================
# 2. Schema Date Sequence Validation Tests (422)
# ==========================================


def test_etmf_ingest_date_validation_rejected() -> None:
    """Verify that eTMF Ingest with invalid date range is rejected with 422."""
    client = TestClient(etmf_app)
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol content",
        "mime_type": "application/pdf",
        "issue_date": "2026-12-31",
        "expiration_date": "2026-01-01",  # issue_date > expiration_date
    }
    resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=get_auth_headers(roles="admin")
    )
    assert resp.status_code == 422
    assert "issue_date cannot be later than expiration_date" in resp.text


def test_etmf_expiration_update_date_validation_rejected() -> None:
    """Verify that eTMF Expiration PUT with invalid date range is rejected with 422."""
    client = TestClient(etmf_app)
    payload = {
        "issue_date": "2026-12-31",
        "expiration_date": "2026-01-01",
    }
    resp = client.put(
        "/api/v1/etmf/documents/doc123/expiration",
        json=payload,
        headers=get_auth_headers(roles="admin"),
    )
    assert resp.status_code == 422
    assert "issue_date cannot be later than expiration_date" in resp.text


def test_eisf_creation_date_validation_rejected() -> None:
    """Verify that eISF creation with invalid date range is rejected with 422."""
    client = TestClient(eisf_app)
    payload = {
        "study_id": "study_100",
        "site_id": "site_boston",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "CV text",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial CV upload",
        "issue_date": "2026-12-31",
        "expiration_date": "2026-01-01",
    }
    resp = client.post(
        "/api/v1/eisf/documents",
        json=payload,
        headers=get_auth_headers(roles="admin", site_id="site_boston"),
    )
    assert resp.status_code == 422
    assert "issue_date cannot be later than expiration_date" in resp.text


def test_eisf_update_date_validation_rejected() -> None:
    """Verify that eISF update with invalid date range is rejected with 422."""
    client = TestClient(eisf_app)
    payload = {
        "study_id": "study_100",
        "site_id": "site_boston",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "CV text",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial CV upload",
        "issue_date": "2026-12-31",
        "expiration_date": "2026-01-01",
    }
    resp = client.put(
        "/api/v1/eisf/documents/doc123",
        json=payload,
        headers=get_auth_headers(roles="admin", site_id="site_boston"),
    )
    assert resp.status_code == 422
    assert "issue_date cannot be later than expiration_date" in resp.text


# ==========================================
# 3. Endpoint Ingestion & Creation Authorization (403 vs 200/201)
# ==========================================


def test_etmf_ingest_authorized_vs_unauthorized() -> None:
    """Verify that authorized roles can set expiration fields on ingest, while unauthorized are blocked."""
    client = TestClient(etmf_app)
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol content",
        "mime_type": "application/pdf",
        "issue_date": "2026-01-01",
        "expiration_date": "2026-12-31",
        "document_owner_id": "sponsor_user_01",
    }

    # 1. Unauthorized investigator tries to ingest with expiration fields -> 403
    resp = client.post(
        "/api/v1/etmf/ingest",
        json=payload,
        headers=get_auth_headers(roles="investigator"),
    )
    assert resp.status_code == 403

    # 2. Authorized sponsor_dm ingests -> 201 Success
    resp = client.post(
        "/api/v1/etmf/ingest",
        json=payload,
        headers=get_auth_headers(roles="sponsor_dm"),
    )
    assert resp.status_code == 201
    doc_id = resp.json()["document_id"]

    # 3. View document and assert fields persist
    view_resp = client.get(
        f"/api/v1/etmf/documents/{doc_id}", headers=get_auth_headers(roles="sponsor_dm")
    )
    assert view_resp.status_code == 200
    view_data = view_resp.json()
    assert view_data["issue_date"] == "2026-01-01"
    assert view_data["expiration_date"] == "2026-12-31"
    assert view_data["document_owner_id"] == "sponsor_user_01"


def test_eisf_create_authorized_vs_unauthorized() -> None:
    """Verify that authorized roles can set expiration fields on eISF create, while unauthorized are blocked."""
    client = TestClient(eisf_app)
    payload = {
        "study_id": "study_100",
        "site_id": "site_boston",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "CV text",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial CV upload",
        "issue_date": "2026-01-01",
        "expiration_date": "2026-12-31",
        "document_owner_id": "pi_boston_user",
    }

    # 1. Unauthorized role (e.g. cra) tries to create with expiration metadata -> 403
    # Wait, cra might have create permission? Let's check: "manage_expiration" is only for sponsor_dm and admin.
    # Yes, cra is not in manage_expiration, so setting it should fail with 403.
    resp = client.post(
        "/api/v1/eisf/documents",
        json=payload,
        headers=get_auth_headers(roles="cra", site_id="site_boston"),
    )
    assert resp.status_code == 403

    # 2. Authorized admin creates -> 201 Success
    resp = client.post(
        "/api/v1/eisf/documents",
        json=payload,
        headers=get_auth_headers(roles="admin", site_id="site_boston"),
    )
    assert resp.status_code == 201
    doc_id = resp.json()["id"]

    # 3. Retrieve document and assert fields persist
    get_resp = client.get(
        f"/api/v1/eisf/documents/{doc_id}",
        headers=get_auth_headers(roles="admin", site_id="site_boston"),
    )
    assert get_resp.status_code == 200
    get_data = get_resp.json()
    assert get_data["issue_date"] == "2026-01-01"
    assert get_data["expiration_date"] == "2026-12-31"
    assert get_data["document_owner_id"] == "pi_boston_user"


# ==========================================
# 4. Expiration Update Endpoint Tests
# ==========================================


def test_etmf_expiration_update_authorized_vs_unauthorized() -> None:
    """Verify updating eTMF document expiration metadata is role-gated."""
    client = TestClient(etmf_app)

    # Ingest document first
    ingest_payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol content",
        "mime_type": "application/pdf",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest",
        json=ingest_payload,
        headers=get_auth_headers(roles="admin"),
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    update_payload = {
        "issue_date": "2026-02-01",
        "expiration_date": "2026-11-30",
        "document_owner_id": "owner_02",
    }

    # 1. Unauthorized Investigator tries to update -> 403
    resp = client.put(
        f"/api/v1/etmf/documents/{doc_id}/expiration",
        json=update_payload,
        headers=get_auth_headers(roles="investigator"),
    )
    assert resp.status_code == 403

    # 2. Authorized Sponsor DM updates -> 200 Success
    resp = client.put(
        f"/api/v1/etmf/documents/{doc_id}/expiration",
        json=update_payload,
        headers=get_auth_headers(roles="sponsor_dm"),
    )
    assert resp.status_code == 200
    updated_data = resp.json()
    assert updated_data["issue_date"] == "2026-02-01"
    assert updated_data["expiration_date"] == "2026-11-30"
    assert updated_data["document_owner_id"] == "owner_02"
    assert updated_data["version_index"] == 2


def test_eisf_expiration_update_authorized_vs_unauthorized() -> None:
    """Verify updating eISF document expiration metadata is role-gated."""
    client = TestClient(eisf_app)

    # Create document first as admin (without expiration metadata initially)
    payload = {
        "study_id": "study_100",
        "site_id": "site_boston",
        "binder_classification": "Investigator CV",
        "filename": "cv.pdf",
        "content": "CV text",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial CV upload",
    }
    resp = client.post(
        "/api/v1/eisf/documents",
        json=payload,
        headers=get_auth_headers(roles="admin", site_id="site_boston"),
    )
    assert resp.status_code == 201
    doc_id = resp.json()["id"]

    # Try updating to add expiration metadata
    update_payload = payload.copy()
    update_payload["issue_date"] = "2026-02-01"
    update_payload["expiration_date"] = "2026-11-30"
    update_payload["document_owner_id"] = "new_owner"

    # 1. Investigator (unauthorized to set expiration fields) tries to update -> 403
    resp = client.put(
        f"/api/v1/eisf/documents/{doc_id}",
        json=update_payload,
        headers=get_auth_headers(roles="investigator", site_id="site_boston"),
    )
    assert resp.status_code == 403

    # 2. Admin (authorized) updates -> 200 Success
    resp = client.put(
        f"/api/v1/eisf/documents/{doc_id}",
        json=update_payload,
        headers=get_auth_headers(roles="admin", site_id="site_boston"),
    )
    assert resp.status_code == 200
    updated_data = resp.json()
    assert updated_data["issue_date"] == "2026-02-01"
    assert updated_data["expiration_date"] == "2026-11-30"
    assert updated_data["document_owner_id"] == "new_owner"


# ==========================================
# 5. Database Migration Verification Tests
# ==========================================


@pytest.mark.asyncio
async def test_migration_adds_expiration_columns_idempotently() -> None:
    """Verify that eTMF/eISF database migration runners add the new columns idempotently."""
    import os

    db_file = "test_migrate.db"
    db_url = f"sqlite+aiosqlite:///{db_file}"
    if os.path.exists(db_file):
        try:
            os.remove(db_file)
        except Exception:
            pass

    try:
        # 1. Run migrations initially (creates tables with columns)
        from apps.eisf.database.migrate import run_migrations as run_eisf_m
        from apps.etmf.database.migrate import run_migrations as run_etmf_m

        await run_etmf_m(db_url)
        await run_eisf_m(db_url)

        # 2. Inspect table structures to verify columns exist
        from sqlalchemy.ext.asyncio import create_async_engine

        engine = create_async_engine(db_url)

        async with engine.connect() as conn:

            def inspect_columns(sync_conn):
                insp = inspect(sync_conn)
                tmf_cols = [c["name"] for c in insp.get_columns("tmf_documents")]
                isf_cols = [c["name"] for c in insp.get_columns("isf_documents")]
                return tmf_cols, isf_cols

            tmf_columns, isf_columns = await conn.run_sync(inspect_columns)

            assert "issue_date" in tmf_columns
            assert "expiration_date" in tmf_columns
            assert "document_owner_id" in tmf_columns

            assert "issue_date" in isf_columns
            assert "expiration_date" in isf_columns
            assert "document_owner_id" in isf_columns

        # 3. Run migrations a second time to assert idempotency
        await run_etmf_m(db_url)
        await run_eisf_m(db_url)

        async with engine.connect() as conn:
            tmf_columns_2, isf_columns_2 = await conn.run_sync(inspect_columns)
            assert tmf_columns_2 == tmf_columns
            assert isf_columns_2 == isf_columns

        await engine.dispose()
    finally:
        if os.path.exists(db_file):
            try:
                os.remove(db_file)
            except Exception:
                pass
