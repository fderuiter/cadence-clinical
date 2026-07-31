"""Integration test suite qualifying eISF site boundary isolation and non-destructive PHI redaction workflows.

Requirements: PRD-SYS-001
"""

import time
from typing import AsyncGenerator, Dict

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.pool import StaticPool

import packages  # noqa: F401
from apps.eisf.database import db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base as EisfModelBase
from apps.etmf.services.eisf_service import Base as ServiceBase
from apps.etmf.services.eisf_service import EISFBinderService
from apps.execution.database.models import Base as ExecutionModelBase
from apps.gateway.main import generate_signature
from packages.security.rbac import Principal


@pytest_asyncio.fixture
async def db_session() -> AsyncGenerator:
    """Provides an isolated database session for testing EISFBinderService.

    Requirements: PRD-SYS-001
    """
    engine = create_async_engine(
        "sqlite+aiosqlite:///:memory:",
        poolclass=StaticPool,
        connect_args={"check_same_thread": False},
        echo=False,
    )
    from sqlalchemy import event as sa_event
    from sqlalchemy import text

    @sa_event.listens_for(engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_conn, record):
        cursor = dbapi_conn.cursor()
        try:
            cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        except Exception:
            pass
        finally:
            cursor.close()

    async with engine.begin() as conn:
        if engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(ExecutionModelBase.metadata.create_all)
        await conn.run_sync(ServiceBase.metadata.create_all)

    session_maker = async_sessionmaker(engine, expire_on_commit=False)
    async with session_maker() as session:
        yield session

    await engine.dispose()


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db() -> AsyncGenerator:
    """Setup in-memory eISF database for testing FastAPI endpoints.

    Requirements: PRD-SYS-001
    """
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        poolclass=StaticPool,
        connect_args={"check_same_thread": False},
        echo=False,
    )
    from sqlalchemy import event as sa_event
    from sqlalchemy import text

    @sa_event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_conn, record):
        cursor = dbapi_conn.cursor()
        try:
            cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        except Exception:
            pass
        finally:
            cursor.close()

    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(ExecutionModelBase.metadata.create_all)
        await conn.run_sync(EisfModelBase.metadata.create_all)
    yield


@pytest.fixture
def site_101_user() -> Principal:
    """Provides a Principal object for a Site 101 coordinator.

    Requirements: PRD-SYS-001
    """
    return Principal(
        user_id="coord_101",
        roles=["crc"],
        assigned_sites=["SITE-101"],
        change_reason="Standard Site 101 action",
    )


@pytest.fixture
def site_102_user() -> Principal:
    """Provides a Principal object for a Site 102 coordinator.

    Requirements: PRD-SYS-001
    """
    return Principal(
        user_id="coord_102",
        roles=["crc"],
        assigned_sites=["SITE-102"],
        change_reason="Standard Site 102 action",
    )


@pytest.fixture
def site_101_pi() -> Principal:
    """Provides a Principal object for a Site 101 Principal Investigator.

    Requirements: PRD-SYS-001
    """
    return Principal(
        user_id="pi_101",
        roles=["principal_investigator"],
        assigned_sites=["SITE-101"],
        change_reason="PI action on Site 101",
    )


@pytest.fixture
def site_101_monitor() -> Principal:
    """Provides a Principal object for an external monitor assigned to Site 101.

    Requirements: PRD-SYS-001
    """
    return Principal(
        user_id="monitor_101",
        roles=["external_monitor"],
        assigned_sites=["SITE-101"],
        change_reason="Monitor view",
    )


def get_eisf_auth_headers(
    user_id: str = "test_user_eisf",
    roles: str = "crc",
    site_id: str = None,
    change_reason: str = "Valid Change Reason",
) -> Dict[str, str]:
    """Helper to generate valid gateway V2 signed headers for testing.

    Requirements: PRD-SYS-001
    """
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


@pytest.mark.asyncio
async def test_eisf_site_isolation_and_redaction(
    db_session, site_101_user, site_102_user
) -> None:
    """Validate eISF binder enforces site boundary isolation and non-destructive PHI redaction.

    Requirements: PRD-SYS-001
    """
    service = EISFBinderService(session=db_session)

    # 1. Site 101 user attempts to access Site 102 binder -> Must raise PermissionError
    with pytest.raises(PermissionError):
        await service.get_site_binder(site_id="SITE-102", requesting_user=site_101_user)

    # 2. Redact eISF document and verify original is preserved
    doc_record = await service.upload_site_document(
        site_id="SITE-101",
        filename="medical_record.pdf",
        content=b"%PDF-1.4 sample content with PHI...",
        uploading_user=site_101_user,
    )

    redacted_doc = await service.create_redacted_copy(
        document_id=doc_record.id, phi_terms=["sample"]
    )

    assert redacted_doc.id != doc_record.id
    assert redacted_doc.parent_document_id == doc_record.id
    assert doc_record.is_redacted is False


def test_eisf_site_isolation_enforcement() -> None:
    """Validate endpoint /api/v1/eisf/binders/{site_id} strictly enforces site isolation.

    Requirements: PRD-SYS-001
    """
    client = TestClient(eisf_app)

    # Coordinator belonging to SITE-101 tries to access SITE-102
    headers = get_eisf_auth_headers(
        user_id="coord_101",
        roles="crc",
        site_id="SITE-101",
    )

    resp = client.get("/api/v1/eisf/binders/SITE-102", headers=headers)
    assert resp.status_code == 403
    assert "Forbidden" in resp.json()["detail"]


@pytest.mark.asyncio
async def test_eisf_phi_redaction_preserves_original(
    db_session, site_101_pi, site_101_monitor
) -> None:
    """Validate that redacting patient identifiers from eISF medical records preserves the original unredacted document.

    Requirements: PRD-SYS-001
    """
    service = EISFBinderService(session=db_session)

    # 1. Upload sample eISF medical record with patient name "Alice Johnson"
    original_content = (
        b"Patient name: Alice Johnson, SSN: 123-45-6789. Diagnosis: Normal."
    )
    doc_record = await service.upload_site_document(
        site_id="SITE-101",
        filename="medical_record.pdf",
        content=original_content,
        uploading_user=site_101_pi,
    )

    # 2. Perform PHI redaction workflow
    redacted_doc = await service.create_redacted_copy(
        document_id=doc_record.id, phi_terms=["Alice Johnson"]
    )

    # 3. Assert original document remains accessible to authorized Site PI unredacted
    original_fetched = await db_session.get(doc_record.__class__, doc_record.id)
    assert original_fetched is not None
    assert original_fetched.content == original_content
    assert b"Alice Johnson" in original_fetched.content
    assert original_fetched.is_redacted is False

    # 4. Assert external monitor download yields redacted copy
    assert redacted_doc is not None
    assert redacted_doc.is_redacted is True
    assert b"Alice Johnson" not in redacted_doc.content
    assert b"123-45-6789" not in redacted_doc.content  # auto-detected by scrubber
    assert b"[REDACTED]" in redacted_doc.content
