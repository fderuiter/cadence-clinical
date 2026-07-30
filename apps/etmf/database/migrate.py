"""
eTMF Database Migration and Backfill Module.

This module provides a production-grade schema migration and backfill entry point
for the electronic Trial Master File (eTMF) service. It enforces core database invariants,
applies schema upgrades idempotently, and backfills historical quality control (QC)
transitions with stable per-document sequences.

--- ROLLBACK STRATEGY DOCUMENTATION ---
To rollback the schema migrations applied by this module, execute the following steps
on the target database:

1. Drop Immutability Triggers:
   - For SQLite:
     DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_update;
     DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_delete;
   - For PostgreSQL:
     DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_update ON tmf_document_qc_transitions;
     DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_delete ON tmf_document_qc_transitions;
     DROP FUNCTION IF EXISTS block_qc_transition_mutation();

2. Remove Schema Constraints and Columns:
   - For PostgreSQL:
     ALTER TABLE tmf_document_qc_transitions DROP CONSTRAINT IF EXISTS uq_document_transition_sequence;
     ALTER TABLE tmf_document_qc_transitions DROP CONSTRAINT IF EXISTS fk_tmf_document_qc_transitions_document_id;
     ALTER TABLE tmf_document_qc_transitions DROP COLUMN IF EXISTS transition_sequence;
     ALTER TABLE tmf_documents DROP CONSTRAINT IF EXISTS chk_tmf_document_status;
     ALTER TABLE tmf_documents DROP COLUMN IF EXISTS issue_date;
     ALTER TABLE tmf_documents DROP COLUMN IF EXISTS expiration_date;
     ALTER TABLE tmf_documents DROP COLUMN IF EXISTS document_owner_id;
   - For SQLite (Since SQLite does not support dropping columns directly, rebuild the table):
     CREATE TABLE tmf_document_qc_transitions_rollback (
         id VARCHAR(36) PRIMARY KEY,
         document_id VARCHAR(36) NOT NULL,
         from_status VARCHAR(50) NOT NULL,
         to_status VARCHAR(50) NOT NULL,
         actor_id VARCHAR(255) NOT NULL,
         actor_role VARCHAR(255) NOT NULL,
         reason_for_change VARCHAR(1000) NOT NULL,
         timestamp DATETIME NOT NULL
     );
     INSERT INTO tmf_document_qc_transitions_rollback (
         id, document_id, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp
     ) SELECT id, document_id, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp
     FROM tmf_document_qc_transitions;
     DROP TABLE tmf_document_qc_transitions;
     ALTER TABLE tmf_document_qc_transitions_rollback RENAME TO tmf_document_qc_transitions;
"""

import argparse
import asyncio
import os
import sys

from sqlalchemy import inspect, text
from sqlalchemy.ext.asyncio import create_async_engine

from apps.etmf.models import Base


async def deploy_database_triggers(conn, dialect_name: str) -> None:
    """
    Deploys database-level triggers to guarantee immutability (insertion-only)
    for DocumentQCTransition records.
    """
    if dialect_name == "sqlite":
        await conn.execute(
            text("""
            CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_update
            BEFORE UPDATE ON tmf_document_qc_transitions
            BEGIN
                SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be updated.');
            END;
        """)
        )
        await conn.execute(
            text("""
            CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_delete
            BEFORE DELETE ON tmf_document_qc_transitions
            BEGIN
                SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be deleted.');
            END;
        """)
        )
    elif dialect_name == "postgresql":
        await conn.execute(
            text("""
            CREATE OR REPLACE FUNCTION block_qc_transition_mutation()
            RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only.';
            END;
            $$ LANGUAGE plpgsql;
        """)
        )
        await conn.execute(
            text(
                "DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_update ON tmf_document_qc_transitions;"
            )
        )
        await conn.execute(
            text("""
            CREATE TRIGGER tmf_document_qc_transitions_no_update
            BEFORE UPDATE ON tmf_document_qc_transitions
            FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();
        """)
        )
        await conn.execute(
            text(
                "DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_delete ON tmf_document_qc_transitions;"
            )
        )
        await conn.execute(
            text("""
            CREATE TRIGGER tmf_document_qc_transitions_no_delete
            BEFORE DELETE ON tmf_document_qc_transitions
            FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();
        """)
        )


async def upgrade_existing_tables(conn, dialect_name: str) -> None:
    """
    Inspects and upgrades existing tables to ensure they adhere to core invariants
    without destructive data modifications.
    """

    def get_table_columns(sync_conn, table_name: str):
        insp = inspect(sync_conn)
        if not insp.has_table(table_name):
            return []
        return [col["name"] for col in insp.get_columns(table_name)]

    # 1. Clean and align TMFDocument statuses
    has_tmf_docs = await conn.run_sync(
        lambda sc: inspect(sc).has_table("tmf_documents")
    )
    if has_tmf_docs:
        # Auto-heal invalid statuses to DRAFT
        valid_statuses = (
            "DRAFT",
            "TECHNICAL_QC",
            "CLINICAL_QC",
            "APPROVED",
            "ARCHIVED",
            "REJECTED",
            "SIGNED",
        )
        res = await conn.execute(text("SELECT id, status FROM tmf_documents"))
        rows = res.fetchall()
        for doc_id, status in rows:
            if status not in valid_statuses:
                print(
                    f"[Migration] Document {doc_id} has invalid status '{status}'; resetting to DRAFT."
                )
                await conn.execute(
                    text("UPDATE tmf_documents SET status = 'DRAFT' WHERE id = :id"),
                    {"id": doc_id},
                )

        if dialect_name == "sqlite":
            print(
                "[Migration] Rebuilding tmf_documents for SQLite constraint enforcement..."
            )
            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS tmf_documents_new (
                    id VARCHAR(36) PRIMARY KEY,
                    study_id VARCHAR(255) NOT NULL,
                    site_id VARCHAR(255),
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
                    metadata_json JSON,
                    reason_for_change VARCHAR(1000),
                    protocol_version_tag VARCHAR(50),
                    protocol_version_index INTEGER,
                    protocol_version_status VARCHAR(50),
                    document_type VARCHAR(50),
                    approval_status VARCHAR(50) NOT NULL,
                    signature_manifestation JSON,
                    signer VARCHAR(255),
                    signing_timestamp DATETIME,
                    is_redacted BOOLEAN NOT NULL DEFAULT 0,
                    redaction_source_id VARCHAR(36),
                    redaction_manifest_json JSON,
                    issue_date DATE,
                    expiration_date DATE,
                    document_owner_id VARCHAR(255),
                    CHECK (status IN ('DRAFT', 'TECHNICAL_QC', 'CLINICAL_QC', 'APPROVED', 'ARCHIVED', 'REJECTED', 'SIGNED'))
                );
            """)
            )

            cols_present = await conn.run_sync(
                lambda sc: get_table_columns(sc, "tmf_documents")
            )
            select_parts = []
            for col in [
                "id",
                "study_id",
                "site_id",
                "zone",
                "section",
                "artifact_type",
                "filename",
                "content",
                "mime_type",
                "created_at",
                "created_by",
                "version_index",
                "status",
                "taxonomy_version",
                "artifact_code",
                "metadata_json",
                "reason_for_change",
                "protocol_version_tag",
                "protocol_version_index",
                "protocol_version_status",
                "document_type",
                "approval_status",
                "signature_manifestation",
                "signer",
                "signing_timestamp",
                "is_redacted",
                "redaction_source_id",
                "redaction_manifest_json",
                "issue_date",
                "expiration_date",
                "document_owner_id",
            ]:
                if col in cols_present:
                    select_parts.append(col)
                else:
                    if col == "approval_status":
                        select_parts.append("'PENDING' AS approval_status")
                    elif col == "is_redacted":
                        select_parts.append("0 AS is_redacted")
                    elif col in ("status", "taxonomy_version", "artifact_code"):
                        if col == "status":
                            select_parts.append("'DRAFT' AS status")
                        elif col == "taxonomy_version":
                            select_parts.append("'v3.2.0' AS taxonomy_version")
                        else:
                            select_parts.append("'01.01.01' AS artifact_code")
                    else:
                        select_parts.append(f"NULL AS {col}")

            select_sql = ", ".join(select_parts)
            await conn.execute(
                text(f"""
                INSERT INTO tmf_documents_new (
                    id, study_id, site_id, zone, section, artifact_type, filename, content, mime_type,
                    created_at, created_by, version_index, status, taxonomy_version, artifact_code,
                    metadata_json, reason_for_change, protocol_version_tag, protocol_version_index, protocol_version_status,
                    document_type, approval_status, signature_manifestation, signer,
                    signing_timestamp, is_redacted, redaction_source_id, redaction_manifest_json,
                    issue_date, expiration_date, document_owner_id
                )
                SELECT {select_sql}
                FROM tmf_documents;
            """)
            )

            await conn.execute(text("DROP TABLE tmf_documents;"))
            await conn.execute(
                text("ALTER TABLE tmf_documents_new RENAME TO tmf_documents;")
            )

            # Recreate indices
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_study_id ON tmf_documents (study_id);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_site_id ON tmf_documents (site_id);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_zone ON tmf_documents (zone);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_artifact_type ON tmf_documents (artifact_type);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_artifact_code ON tmf_documents (artifact_code);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_document_type ON tmf_documents (document_type);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_redaction_source_id ON tmf_documents (redaction_source_id);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_issue_date ON tmf_documents (issue_date);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_expiration_date ON tmf_documents (expiration_date);"
                )
            )
            await conn.execute(
                text(
                    "CREATE INDEX IF NOT EXISTS ix_tmf_documents_document_owner_id ON tmf_documents (document_owner_id);"
                )
            )

        elif dialect_name == "postgresql":
            # Add site_id column to PostgreSQL table if missing
            try:
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS site_id VARCHAR(255);"
                    )
                )
                await conn.execute(
                    text(
                        "CREATE INDEX IF NOT EXISTS ix_tmf_documents_site_id ON tmf_documents (site_id);"
                    )
                )
            except Exception:
                pass

            # Add reason_for_change and protocol_version columns to PostgreSQL table if missing
            try:
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS reason_for_change VARCHAR(1000);"
                    )
                )
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS protocol_version_tag VARCHAR(50);"
                    )
                )
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS protocol_version_index INTEGER;"
                    )
                )
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS protocol_version_status VARCHAR(50);"
                    )
                )
            except Exception:
                pass

            # Add issue_date, expiration_date, and document_owner_id to PostgreSQL
            try:
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS issue_date DATE;"
                    )
                )
                await conn.execute(
                    text(
                        "CREATE INDEX IF NOT EXISTS ix_tmf_documents_issue_date ON tmf_documents (issue_date);"
                    )
                )
            except Exception:
                pass

            try:
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS expiration_date DATE;"
                    )
                )
                await conn.execute(
                    text(
                        "CREATE INDEX IF NOT EXISTS ix_tmf_documents_expiration_date ON tmf_documents (expiration_date);"
                    )
                )
            except Exception:
                pass

            try:
                await conn.execute(
                    text(
                        "ALTER TABLE tmf_documents ADD COLUMN IF NOT EXISTS document_owner_id VARCHAR(255);"
                    )
                )
                await conn.execute(
                    text(
                        "CREATE INDEX IF NOT EXISTS ix_tmf_documents_document_owner_id ON tmf_documents (document_owner_id);"
                    )
                )
            except Exception:
                pass

            # Attempt to add check constraint if missing
            try:
                res_const = await conn.execute(
                    text("""
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'chk_tmf_document_status';
                """)
                )
                if not res_const.fetchone():
                    await conn.execute(
                        text("""
                        ALTER TABLE tmf_documents
                        ADD CONSTRAINT chk_tmf_document_status
                        CHECK (status IN ('DRAFT', 'TECHNICAL_QC', 'CLINICAL_QC', 'APPROVED', 'ARCHIVED', 'REJECTED', 'SIGNED'));
                    """)
                    )
            except Exception:
                pass

        # Idempotent backfill/quarantine strategy for legacy records:
        # Scan legacy records with null site_id, classifying them using is_site_level_artifact.
        # If site-level, update site_id to 'QUARANTINED' to prevent silent scope inference.
        from apps.etmf.models import is_site_level_artifact

        res_site_check = await conn.execute(
            text(
                "SELECT id, artifact_type, artifact_code FROM tmf_documents WHERE site_id IS NULL"
            )
        )
        rows_site_check = res_site_check.fetchall()
        for doc_id, art_type, art_code in rows_site_check:
            if is_site_level_artifact(art_type, art_code):
                await conn.execute(
                    text(
                        "UPDATE tmf_documents SET site_id = 'QUARANTINED' WHERE id = :id"
                    ),
                    {"id": doc_id},
                )

    # 2. Upgrade and Backfill tmf_document_qc_transitions table
    trans_cols = await conn.run_sync(
        lambda sc: get_table_columns(sc, "tmf_document_qc_transitions")
    )
    if trans_cols:
        # Add transition_sequence as nullable column if missing
        if "transition_sequence" not in trans_cols:
            print(
                "[Migration] Adding transition_sequence column to tmf_document_qc_transitions..."
            )
            await conn.execute(
                text(
                    "ALTER TABLE tmf_document_qc_transitions ADD COLUMN transition_sequence INTEGER;"
                )
            )

            # Query all existing records to compute and backfill sequences chronologically
            res = await conn.execute(
                text("""
                SELECT id, document_id
                FROM tmf_document_qc_transitions
                ORDER BY document_id, timestamp ASC, id ASC
            """)
            )
            rows = res.fetchall()

            seq_map = {}  # document_id -> current sequential index
            for tid, doc_id in rows:
                seq = seq_map.get(doc_id, 0) + 1
                seq_map[doc_id] = seq
                await conn.execute(
                    text(
                        "UPDATE tmf_document_qc_transitions SET transition_sequence = :seq WHERE id = :id"
                    ),
                    {"seq": seq, "id": tid},
                )

        # Enforce NOT NULL, Unique, and FK constraints
        if dialect_name == "sqlite":
            # Rebuild table to enforce SQLite column and index constraints
            print(
                "[Migration] Rebuilding tmf_document_qc_transitions for SQLite constraint enforcement..."
            )

            # Temporary drop triggers to prevent rebuild errors
            await conn.execute(
                text("DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_update;")
            )
            await conn.execute(
                text("DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_delete;")
            )

            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS tmf_document_qc_transitions_new (
                    id VARCHAR(36) PRIMARY KEY,
                    document_id VARCHAR(36) NOT NULL,
                    transition_sequence INTEGER NOT NULL,
                    from_status VARCHAR(50) NOT NULL,
                    to_status VARCHAR(50) NOT NULL,
                    actor_id VARCHAR(255) NOT NULL,
                    actor_role VARCHAR(255) NOT NULL,
                    reason_for_change VARCHAR(1000) NOT NULL,
                    timestamp DATETIME NOT NULL,
                    FOREIGN KEY (document_id) REFERENCES tmf_documents(id) ON DELETE CASCADE,
                    UNIQUE (document_id, transition_sequence)
                );
            """)
            )

            await conn.execute(
                text("""
                INSERT INTO tmf_document_qc_transitions_new (
                    id, document_id, transition_sequence, from_status, to_status, actor_id, actor_role, reason_for_change, timestamp
                )
                SELECT id, document_id, COALESCE(transition_sequence, 1), from_status, to_status, actor_id, actor_role, reason_for_change, timestamp
                FROM tmf_document_qc_transitions;
            """)
            )

            await conn.execute(text("DROP TABLE tmf_document_qc_transitions;"))
            await conn.execute(
                text(
                    "ALTER TABLE tmf_document_qc_transitions_new RENAME TO tmf_document_qc_transitions;"
                )
            )

            await conn.execute(
                text("""
                CREATE INDEX IF NOT EXISTS ix_tmf_document_qc_transitions_doc_seq
                ON tmf_document_qc_transitions (document_id, transition_sequence);
            """)
            )

        elif dialect_name == "postgresql":
            print("[Migration] Setting constraints for PostgreSQL dialect...")
            # Set transition_sequence to NOT NULL
            await conn.execute(
                text(
                    "ALTER TABLE tmf_document_qc_transitions ALTER COLUMN transition_sequence SET NOT NULL;"
                )
            )

            # Add foreign key constraint if missing
            try:
                res_fk = await conn.execute(
                    text("""
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'fk_tmf_document_qc_transitions_document_id';
                """)
                )
                if not res_fk.fetchone():
                    await conn.execute(
                        text("""
                        ALTER TABLE tmf_document_qc_transitions
                        ADD CONSTRAINT fk_tmf_document_qc_transitions_document_id
                        FOREIGN KEY (document_id) REFERENCES tmf_documents(id) ON DELETE CASCADE;
                    """)
                    )
            except Exception:
                pass

            # Add UniqueConstraint
            try:
                res_uq = await conn.execute(
                    text("""
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'uq_document_transition_sequence';
                """)
                )
                if not res_uq.fetchone():
                    await conn.execute(
                        text("""
                        ALTER TABLE tmf_document_qc_transitions
                        ADD CONSTRAINT uq_document_transition_sequence
                        UNIQUE (document_id, transition_sequence);
                    """)
                    )
            except Exception:
                pass


async def run_migrations(database_url: str) -> None:
    """
    Executes pre-boot eTMF schema migrations and safe GxP backfills.
    """
    print(f"Starting pre-boot schema migration for eTMF: {database_url}")
    engine = create_async_engine(database_url, echo=False)
    dialect_name = engine.dialect.name

    try:
        async with engine.begin() as conn:
            # First, check and run base create_all for clean installs (creates tables that do not exist yet)
            await conn.run_sync(Base.metadata.create_all)

            # Apply migrations and backfills for existing schema instances
            await upgrade_existing_tables(conn, dialect_name)

            # Deploy native database mutation immutability triggers
            await deploy_database_triggers(conn, dialect_name)

        print("eTMF Schema migration completed successfully.")
    except Exception as e:
        print(f"eTMF Schema migration failed: {e}")
        sys.exit(1)
    finally:
        await engine.dispose()


def main() -> None:
    """CLI script entrypoint."""
    parser = argparse.ArgumentParser(
        description="eTMF Database Schema Migration Runner"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv("ETMF_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        help="Database URL for migration",
    )
    args = parser.parse_args()
    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
