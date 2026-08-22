import os

import pytest
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

from apps.etmf.database.migrate import run_migrations


@pytest.mark.asyncio
async def test_etmf_triggers_immutability(tmp_path):
    """
    Verify that eTMF database-level triggers enforce:
    1. Absolute immutability on finalized QC transitions (no update, no delete).
    2. Absolute immutability on document records (no deletes).
    """
    db_file = str(tmp_path / "triggers_compliance_test.sqlite")
    db_url = f"sqlite+aiosqlite:///{db_file}"

    try:
        # Run migrations to deploy triggers
        await run_migrations(db_url)

        engine = create_async_engine(db_url)
        async with engine.begin() as conn:
            # Insert a test document
            await conn.execute(
                text("""
                INSERT INTO tmf_documents (
                    id, study_id, zone, section, artifact_type, filename, content, mime_type,
                    created_at, created_by, version_index, status, taxonomy_version, artifact_code, approval_status, is_redacted
                ) VALUES (
                    'doc_1', 'study_1', 1, 'section_1', 'type_1', 'file.pdf', 'content', 'application/pdf',
                    '2026-08-17 00:00:00', 'user_1', 1, 'DRAFT', 'v3.2.0', '01.01.01', 'PENDING', 0
                );
            """)
            )

            # Insert a transition
            await conn.execute(
                text("""
                INSERT INTO tmf_document_qc_transitions (
                    id, document_id, transition_sequence, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp
                ) VALUES (
                    'trans_1', 'doc_1', 1, 'DRAFT', 'TECHNICAL_QC', 'user_1', 'actor_1', 'justification_1', '2026-08-17 00:00:00'
                );
            """)
            )

        # 1. Verify tmf_document_qc_transitions cannot be updated
        async with engine.begin() as conn:
            with pytest.raises(Exception) as exc_info:
                await conn.execute(
                    text(
                        "UPDATE tmf_document_qc_transitions SET to_status = 'CLINICAL_QC' WHERE id = 'trans_1';"
                    )
                )
            assert "IMMUTABILITY_VIOLATION" in str(exc_info.value)

        # 2. Verify tmf_document_qc_transitions cannot be deleted
        async with engine.begin() as conn:
            with pytest.raises(Exception) as exc_info:
                await conn.execute(
                    text(
                        "DELETE FROM tmf_document_qc_transitions WHERE id = 'trans_1';"
                    )
                )
            assert "IMMUTABILITY_VIOLATION" in str(exc_info.value)

        # 3. Verify tmf_documents cannot be deleted
        async with engine.begin() as conn:
            with pytest.raises(Exception) as exc_info:
                await conn.execute(
                    text("DELETE FROM tmf_documents WHERE id = 'doc_1';")
                )
            assert "IMMUTABILITY_VIOLATION" in str(exc_info.value)

        await engine.dispose()
    finally:
        if os.path.exists(db_file):
            os.remove(db_file)
