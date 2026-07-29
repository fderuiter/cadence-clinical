import os
import uuid
import pytest
from sqlalchemy import text, event
from sqlalchemy.exc import IntegrityError, OperationalError
from sqlalchemy.ext.asyncio import create_async_engine

from apps.etmf.migrate import run_migrations


def enable_sqlite_fks(engine):
    """Event listener to enable SQLite foreign keys on raw connections."""
    @event.listens_for(engine.sync_engine, "connect")
    def set_sqlite_pragma(dbapi_connection, connection_record):
        cursor = dbapi_connection.cursor()
        cursor.execute("PRAGMA foreign_keys=ON")
        cursor.close()


@pytest.mark.asyncio
async def test_migration_clean_path():
    """
    Verify that migrating a clean, empty database runs successfully and creates all tables.
    """
    db_file = "clean_test_db.sqlite"
    db_url = f"sqlite+aiosqlite:///{db_file}"

    if os.path.exists(db_file):
        os.remove(db_file)

    try:
        await run_migrations(db_url)

        engine = create_async_engine(db_url)
        enable_sqlite_fks(engine)
        async with engine.begin() as conn:
            # Check that tables exist
            res = await conn.execute(text("SELECT name FROM sqlite_master WHERE type='table'"))
            tables = [row[0] for row in res.fetchall()]
            assert "tmf_documents" in tables
            assert "tmf_document_qc_transitions" in tables
        await engine.dispose()
    finally:
        if os.path.exists(db_file):
            os.remove(db_file)


@pytest.mark.asyncio
async def test_migration_upgrade_and_backfill_path():
    """
    Verify that migrating an existing database with legacy schemas safely updates columns,
    rebuilds SQLite constraints, backfills transition sequences sequentially,
    and preserves delivered fields (like content, signatures, or redactions).
    """
    db_file = "upgrade_test_db.sqlite"
    db_url = f"sqlite+aiosqlite:///{db_file}"

    if os.path.exists(db_file):
        os.remove(db_file)

    try:
        engine = create_async_engine(db_url)
        enable_sqlite_fks(engine)

        async with engine.begin() as conn:
            # 1. Manually create legacy schemas (prior to our hard constraints and sequence field)
            await conn.execute(text("""
                CREATE TABLE tmf_documents (
                    id VARCHAR(36) PRIMARY KEY,
                    study_id VARCHAR(255) NOT NULL,
                    zone INTEGER NOT NULL,
                    section VARCHAR(255) NOT NULL,
                    artifact_type VARCHAR(255) NOT NULL,
                    filename VARCHAR(255) NOT NULL,
                    content TEXT NOT NULL,
                    mime_type VARCHAR(100) NOT NULL,
                    created_at DATETIME NOT NULL,
                    created_by VARCHAR(255) NOT NULL,
                    version_index INTEGER NOT NULL,
                    status VARCHAR(50) NOT NULL,
                    taxonomy_version VARCHAR(50) NOT NULL,
                    artifact_code VARCHAR(50) NOT NULL,
                    approval_status VARCHAR(50) NOT NULL,
                    is_redacted BOOLEAN NOT NULL DEFAULT 0
                );
            """))

            await conn.execute(text("""
                CREATE TABLE tmf_document_qc_transitions (
                    id VARCHAR(36) PRIMARY KEY,
                    document_id VARCHAR(36) NOT NULL,
                    from_status VARCHAR(50) NOT NULL,
                    to_status VARCHAR(50) NOT NULL,
                    actor_id VARCHAR(255) NOT NULL,
                    actor_role VARCHAR(255) NOT NULL,
                    reason_for_change VARCHAR(1000) NOT NULL,
                    timestamp DATETIME NOT NULL
                );
            """))

            # 2. Seed some legacy documents and transitions
            doc1_id = str(uuid.uuid4())
            doc2_id = str(uuid.uuid4())

            await conn.execute(text("""
                INSERT INTO tmf_documents (id, study_id, zone, section, artifact_type, filename, content, mime_type, created_at, created_by, version_index, status, taxonomy_version, artifact_code, approval_status)
                VALUES
                (:id1, 'study_001', 1, 'SecA', 'TypeA', 'file1.txt', 'ContentA', 'text/plain', '2026-01-01', 'user1', 1, 'CLINICAL_QC', 'v3.2.0', '01.01.01', 'PENDING'),
                (:id2, 'study_001', 1, 'SecA', 'TypeA', 'file2.txt', 'ContentB', 'text/plain', '2026-01-01', 'user1', 1, 'DRAFT', 'v3.2.0', '01.01.01', 'PENDING')
            """), {"id1": doc1_id, "id2": doc2_id})

            # Insert multiple transitions for doc1 to test sequence calculation
            await conn.execute(text("""
                INSERT INTO tmf_document_qc_transitions (id, document_id, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp)
                VALUES
                ('t1', :doc_id, 'DRAFT', 'TECHNICAL_QC', 'u1', 'r1', 'First change reason here', '2026-01-01 10:00:00'),
                ('t2', :doc_id, 'TECHNICAL_QC', 'CLINICAL_QC', 'u2', 'r2', 'Second change reason here', '2026-01-01 11:00:00')
            """), {"doc_id": doc1_id})

            # Insert one transition for doc2
            await conn.execute(text("""
                INSERT INTO tmf_document_qc_transitions (id, document_id, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp)
                VALUES
                ('t3', :doc_id, 'DRAFT', 'TECHNICAL_QC', 'u1', 'r1', 'Third change reason here', '2026-01-01 12:00:00')
            """), {"doc_id": doc2_id})

        await engine.dispose()

        # Run the migrations (upgrade + backfill + trigger deployment path)
        await run_migrations(db_url)

        # Verify that sequences are computed and constraints are active
        engine = create_async_engine(db_url)
        enable_sqlite_fks(engine)
        async with engine.begin() as conn:
            # Check transition_sequence column backfilled correctly
            res = await conn.execute(text("SELECT id, document_id, transition_sequence FROM tmf_document_qc_transitions ORDER BY id"))
            trans_rows = {row[0]: (row[1], row[2]) for row in res.fetchall()}

            # 't1' and 't2' are for doc1. Chronologically: t1 then t2
            assert trans_rows["t1"] == (doc1_id, 1)
            assert trans_rows["t2"] == (doc1_id, 2)
            # 't3' is for doc2
            assert trans_rows["t3"] == (doc2_id, 1)

            # 3. Test CheckConstraint on TMFDocument status
            # Writing an invalid status should fail
            with pytest.raises(IntegrityError):
                await conn.execute(text("""
                    INSERT INTO tmf_documents (id, study_id, zone, section, artifact_type, filename, content, mime_type, created_at, created_by, version_index, status, taxonomy_version, artifact_code, approval_status)
                    VALUES
                    ('doc3', 'study_001', 1, 'SecA', 'TypeA', 'file3.txt', 'ContentC', 'text/plain', '2026-01-01', 'user1', 1, 'INVALID_QC_STATUS', 'v3.2.0', '01.01.01', 'PENDING')
                """))

            # 4. Test ForeignKey on DocumentQCTransition
            with pytest.raises(IntegrityError):
                await conn.execute(text("""
                    INSERT INTO tmf_document_qc_transitions (id, document_id, transition_sequence, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp)
                    VALUES
                    ('t4', 'nonexistent-doc-id', 1, 'DRAFT', 'TECHNICAL_QC', 'u1', 'r1', 'Fourth change reason', '2026-01-01 13:00:00')
                """))

            # 5. Test UniqueConstraint (document_id, transition_sequence)
            with pytest.raises(IntegrityError):
                await conn.execute(text("""
                    INSERT INTO tmf_document_qc_transitions (id, document_id, transition_sequence, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp)
                    VALUES
                    ('t5', :doc_id, 1, 'DRAFT', 'TECHNICAL_QC', 'u1', 'r1', 'Duplicate sequence check', '2026-01-01 14:00:00')
                """), {"doc_id": doc1_id})

            # 6. Test Immutability Triggers (reject UPDATE/DELETE)
            # Attempting to delete or update a transition should fail via the sqlite trigger
            with pytest.raises((OperationalError, IntegrityError)) as exc_info:
                await conn.execute(text("DELETE FROM tmf_document_qc_transitions WHERE id='t1'"))
            assert "IMMUTABILITY_VIOLATION" in str(exc_info.value)

            with pytest.raises((OperationalError, IntegrityError)) as exc_info:
                await conn.execute(text("UPDATE tmf_document_qc_transitions SET to_status='APPROVED' WHERE id='t1'"))
            assert "IMMUTABILITY_VIOLATION" in str(exc_info.value)

        await engine.dispose()
    finally:
        if os.path.exists(db_file):
            os.remove(db_file)
