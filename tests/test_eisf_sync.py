from datetime import datetime, timedelta, timezone

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.eisf.database import db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base, ISFAuditLog, ISFDocument
from tests.test_eisf_api import get_eisf_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db_for_sync():
    """
    Setup in-memory eISF database for testing sync.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest_asyncio.fixture
def mock_etmf_propagation(monkeypatch):
    """
    Mock httpx AsyncClient post to capture and verify eTMF propagation.
    """
    calls = []

    async def mock_post(self_client, url, *args, **kwargs):
        calls.append(
            {"url": url, "json": kwargs.get("json"), "headers": kwargs.get("headers")}
        )
        import httpx

        return httpx.Response(201, json={"status": "success"})

    import httpx

    monkeypatch.setattr(httpx.AsyncClient, "post", mock_post)
    return calls


@pytest.mark.asyncio
async def test_eisf_sync_creation_and_etmf_propagation(mock_etmf_propagation) -> None:
    """
    Test that a NEW sync item is successfully created, increments created_count,
    writes a SYNC audit log, and propagates to eTMF if not eTMF-originated.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    payload = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Investigator CV",
                "filename": "cv_smith.pdf",
                "content": "Dr. Smith CV content",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "CLIENT_WINS",
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["status"] == "success"
    assert data["processed_count"] == 1
    assert data["created_count"] == 1
    assert data["updated_count"] == 0
    assert data["ignored_count"] == 0

    # Verify document is in eISF db
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFDocument).where(ISFDocument.study_id == "study-100")
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 1
        doc = docs[0]
        assert doc.filename == "cv_smith.pdf"
        assert doc.version_index == 1
        assert doc.sync_status == "SYNCED"
        assert doc.correlation_key is not None

        # Verify SYNC audit log is written
        stmt_audit = select(ISFAuditLog).where(ISFAuditLog.action == "SYNC")
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()
        assert len(audit_logs) == 1
        assert "Created new document" in audit_logs[0].details

    # Verify eTMF propagation happened
    assert len(mock_etmf_propagation) == 1
    prop = mock_etmf_propagation[0]
    assert prop["url"] == "http://localhost:8003/api/v1/etmf/ingest"
    assert prop["json"]["study_id"] == "study-100"
    assert prop["json"]["content"] == "Dr. Smith CV content"
    assert prop["headers"]["X-User-Id"] == "eisf_sync_service"


@pytest.mark.asyncio
async def test_eisf_sync_echo_loop_prevention(mock_etmf_propagation) -> None:
    """
    Test that sync items with source_system="eTMF" are stored locally in eISF,
    but are NOT propagated back to eTMF (echo loop prevention).
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    payload = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "FDA Form 1572",
                "filename": "1572_form.pdf",
                "content": "FDA Form 1572 content",
                "mime_type": "application/pdf",
                "source_system": "eTMF",  # eTMF originated record
                "conflict_policy": "CLIENT_WINS",
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["created_count"] == 1

    # Verify stored with source_system="eTMF" and sync_status="SYNCED"
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFDocument).where(
            ISFDocument.binder_classification == "FDA Form 1572"
        )
        res = await session.execute(stmt)
        doc = res.scalars().one()
        assert doc.source_system == "eTMF"
        assert doc.sync_status == "SYNCED"

    # Verify NO eTMF propagation calls were made (prevent echo loop)
    assert len(mock_etmf_propagation) == 0


@pytest.mark.asyncio
async def test_eisf_sync_exact_duplicate_ignored(mock_etmf_propagation) -> None:
    """
    Test that exact duplicates (same correlation_key and same checksum) are ignored
    without incrementing version indexes or triggering eTMF propagation.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    # 1. Ingest initial document
    payload_init = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Investigator CV",
        "filename": "cv_smith.pdf",
        "content": "Dr. Smith CV content",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial CV filing",
    }
    init_resp = client.post(
        "/api/v1/eisf/documents", json=payload_init, headers=headers
    )
    assert init_resp.status_code == 201
    doc_id = init_resp.json()["id"]

    # 2. Sync same document content (exact duplicate)
    payload_sync = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Investigator CV",
                "filename": "cv_smith.pdf",
                "content": "Dr. Smith CV content",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "CLIENT_WINS",
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["processed_count"] == 1
    assert data["created_count"] == 0
    assert data["updated_count"] == 0
    assert data["ignored_count"] == 1

    # Verify no new versions are created in the database
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFDocument).where(ISFDocument.study_id == "study-100")
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 1
        assert docs[0].id == doc_id
        assert docs[0].version_index == 1

    # Verify no eTMF propagation calls happened for the duplicate
    assert len(mock_etmf_propagation) == 0


@pytest.mark.asyncio
async def test_eisf_sync_conflict_client_wins(mock_etmf_propagation) -> None:
    """
    Test that CLIENT_WINS conflict policy replaces the existing document with a new version index.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    # Pre-populate version 1
    payload_init = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Approved Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol Content V1",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial protocol filing",
    }
    client.post("/api/v1/eisf/documents", json=payload_init, headers=headers)

    # Sync newer version under CLIENT_WINS
    payload_sync = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Approved Protocol",
                "filename": "protocol_v2.pdf",
                "content": "Protocol Content V2",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "CLIENT_WINS",
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["updated_count"] == 1

    # Verify database state
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ISFDocument)
            .where(ISFDocument.binder_classification == "Approved Protocol")
            .order_by(ISFDocument.version_index.asc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 2
        assert docs[0].version_index == 1
        assert docs[1].version_index == 2
        assert docs[1].filename == "protocol_v2.pdf"
        assert docs[1].content == "Protocol Content V2"


@pytest.mark.asyncio
async def test_eisf_sync_conflict_server_wins(mock_etmf_propagation) -> None:
    """
    Test that SERVER_WINS conflict policy ignores the incoming document and keeps existing.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    # Pre-populate version 1
    payload_init = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Approved Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol Content V1",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial protocol filing",
    }
    client.post("/api/v1/eisf/documents", json=payload_init, headers=headers)

    # Sync newer version under SERVER_WINS
    payload_sync = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Approved Protocol",
                "filename": "protocol_v2.pdf",
                "content": "Protocol Content V2",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "SERVER_WINS",
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["ignored_count"] == 1

    # Verify database state has not changed (no version index 2 is created)
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ISFDocument)
            .where(ISFDocument.binder_classification == "Approved Protocol")
            .order_by(ISFDocument.version_index.asc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 1
        assert docs[0].version_index == 1
        assert docs[0].filename == "protocol_v1.pdf"


@pytest.mark.asyncio
async def test_eisf_sync_conflict_merge_lww_incoming_wins(
    mock_etmf_propagation,
) -> None:
    """
    Test that MERGE conflict policy with newer incoming timestamp overwrites existing content.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    # Pre-populate existing document with metadata_json
    payload_init = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Approved Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol Content V1",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial protocol filing",
        "metadata_json": {"reviewer": "Dr. Exist"},
    }
    client.post("/api/v1/eisf/documents", json=payload_init, headers=headers)

    # Get the prepopulated document created_at time
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFDocument).where(
            ISFDocument.binder_classification == "Approved Protocol"
        )
        res = await session.execute(stmt)
        doc = res.scalars().one()
        created_at_dt = doc.created_at

    # Build incoming with a much newer timestamp
    future_time = (
        (created_at_dt + timedelta(hours=2)).replace(tzinfo=timezone.utc).isoformat()
    )

    payload_sync = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Approved Protocol",
                "filename": "protocol_v2.pdf",
                "content": "Protocol Content V2",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "MERGE",
                "metadata_json": {
                    "timestamp": future_time,
                    "reviewer": "Dr. Sync",
                    "approver": "Sponsor Team",
                },
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["updated_count"] == 1

    # Verify core content and metadata are merged and updated
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ISFDocument)
            .where(ISFDocument.binder_classification == "Approved Protocol")
            .order_by(ISFDocument.version_index.desc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 2
        latest = docs[0]
        assert latest.version_index == 2
        assert (
            latest.content == "Protocol Content V2"
        )  # Overwritten because incoming timestamp is newer
        assert latest.metadata_json["reviewer"] == "Dr. Sync"  # Overwritten via LWW
        assert (
            latest.metadata_json["approver"] == "Sponsor Team"
        )  # Merged from incoming


@pytest.mark.asyncio
async def test_eisf_sync_conflict_merge_lww_existing_wins(
    mock_etmf_propagation,
) -> None:
    """
    Test that MERGE conflict policy with older incoming timestamp retains existing content,
    but still merges independent fields from the metadata.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    # Pre-populate existing document
    payload_init = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Approved Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol Content V1",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial protocol filing",
        "metadata_json": {"reviewer": "Dr. Exist"},
    }
    client.post("/api/v1/eisf/documents", json=payload_init, headers=headers)

    # Get the prepopulated document created_at time
    async with db_manager.get_session_maker()() as session:
        stmt = select(ISFDocument).where(
            ISFDocument.binder_classification == "Approved Protocol"
        )
        res = await session.execute(stmt)
        doc = res.scalars().one()
        created_at_dt = doc.created_at

    # Build incoming with a much older timestamp
    past_time = (
        (created_at_dt - timedelta(hours=2)).replace(tzinfo=timezone.utc).isoformat()
    )

    payload_sync = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Approved Protocol",
                "filename": "protocol_v2.pdf",
                "content": "Protocol Content V2",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "MERGE",
                "metadata_json": {
                    "timestamp": past_time,
                    "reviewer": "Dr. Sync",
                    "approver": "Sponsor Team",
                },
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["updated_count"] == 1  # Updated because metadata merged some new fields

    # Verify core content and metadata
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ISFDocument)
            .where(ISFDocument.binder_classification == "Approved Protocol")
            .order_by(ISFDocument.version_index.desc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 2
        latest = docs[0]
        assert latest.version_index == 2
        assert (
            latest.content == "Protocol Content V1"
        )  # Retained because existing timestamp is newer
        assert latest.metadata_json["reviewer"] == "Dr. Exist"  # Retained via LWW
        assert (
            latest.metadata_json["approver"] == "Sponsor Team"
        )  # Merged independent metadata field


@pytest.mark.asyncio
async def test_eisf_sync_conflict_merge_lexicographic_tiebreaker(
    mock_etmf_propagation,
) -> None:
    """
    Test that MERGE conflict policy with identical timestamps uses lexicographic modified_by tiebreaker.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    # Pre-populate existing document with timestamp and modified_by = "alpha"
    timestamp_iso = datetime.utcnow().replace(tzinfo=timezone.utc).isoformat()
    payload_init = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Approved Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol Content V1",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial protocol filing",
        "metadata_json": {
            "timestamp": timestamp_iso,
            "modified_by": "alpha",
            "reviewer": "Dr. Exist",
        },
    }
    client.post("/api/v1/eisf/documents", json=payload_init, headers=headers)

    # 1. Sync incoming with modified_by = "beta" (lexicographically greater -> wins)
    payload_sync_win = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Approved Protocol",
                "filename": "protocol_v2_win.pdf",
                "content": "Protocol Content V2 (Win)",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "MERGE",
                "metadata_json": {
                    "timestamp": timestamp_iso,
                    "modified_by": "beta",
                    "reviewer": "Dr. Win",
                },
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync_win, headers=headers)
    assert resp.status_code == 200
    assert resp.json()["updated_count"] == 1

    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ISFDocument)
            .where(ISFDocument.binder_classification == "Approved Protocol")
            .order_by(ISFDocument.version_index.desc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert (
            docs[0].content == "Protocol Content V2 (Win)"
        )  # Overwritten because "beta" > "alpha"
        assert docs[0].metadata_json["reviewer"] == "Dr. Win"

    # 2. Sync incoming with modified_by = "aaa" (lexicographically smaller -> loses)
    payload_sync_lose = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Approved Protocol",
                "filename": "protocol_v3_lose.pdf",
                "content": "Protocol Content V3 (Lose)",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "MERGE",
                "metadata_json": {
                    "timestamp": timestamp_iso,
                    "modified_by": "aaa",
                    "reviewer": "Dr. Lose",
                },
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync_lose, headers=headers)
    assert resp.status_code == 200
    assert (
        resp.json()["ignored_count"] == 1
    )  # Ignored because no changes made to the winning document representation


@pytest.mark.asyncio
async def test_eisf_sync_per_field_metadata_lww(mock_etmf_propagation) -> None:
    """
    Test that MERGE conflict policy handles per-field metadata timestamps correctly.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    t_base = datetime.utcnow()

    # Pre-populate existing document with per-field timestamps
    payload_init = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Approved Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol Content V1",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial protocol filing",
        "metadata_json": {
            "reviewer": "Dr. Exist",
            "approver": "Sponsor Exist",
            "timestamps": {
                "reviewer": (t_base + timedelta(hours=1))
                .replace(tzinfo=timezone.utc)
                .isoformat(),
                "approver": (t_base - timedelta(hours=1))
                .replace(tzinfo=timezone.utc)
                .isoformat(),
            },
        },
    }
    client.post("/api/v1/eisf/documents", json=payload_init, headers=headers)

    # Sync newer metadata values with custom timestamps
    payload_sync = {
        "submissions": [
            {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Approved Protocol",
                "filename": "protocol_v1.pdf",
                "content": "Protocol Content V1",
                "mime_type": "application/pdf",
                "source_system": "eISF",
                "conflict_policy": "MERGE",
                "metadata_json": {
                    "reviewer": "Dr. Incoming Newer",
                    "approver": "Sponsor Incoming Newer",
                    "timestamps": {
                        # Incoming reviewer timestamp is OLDER than existing -> will lose
                        "reviewer": (t_base - timedelta(hours=2))
                        .replace(tzinfo=timezone.utc)
                        .isoformat(),
                        # Incoming approver timestamp is NEWER than existing -> will win
                        "approver": (t_base + timedelta(hours=2))
                        .replace(tzinfo=timezone.utc)
                        .isoformat(),
                    },
                },
            }
        ]
    }

    resp = client.post("/api/v1/eisf/sync", json=payload_sync, headers=headers)
    assert resp.status_code == 200
    assert resp.json()["updated_count"] == 1

    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ISFDocument)
            .where(ISFDocument.binder_classification == "Approved Protocol")
            .order_by(ISFDocument.version_index.desc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        latest = docs[0]
        assert (
            latest.metadata_json["reviewer"] == "Dr. Exist"
        )  # Retained because existing was newer
        assert (
            latest.metadata_json["approver"] == "Sponsor Incoming Newer"
        )  # Overwritten because incoming was newer
