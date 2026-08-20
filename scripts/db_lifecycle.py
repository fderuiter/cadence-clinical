#!/usr/bin/env python3
"""
Unified Multi-Database Lifecycle and Seeding CLI Script.

Wipes, resets, migrates, and seeds PostgreSQL, Neo4j, and SQLite instances
concurrently for the Cadence Clinical environment. Features YAML-driven baseline
clinical trial data seeding and strict safety guardrails.
"""

import argparse
import asyncio
import os
import sys
import urllib.parse
from pathlib import Path
from typing import Any

# Set default fallback secrets for local CLI environment if not already set
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "dev-audit-log-secret-key-placeholder-32-bytes"
)
os.environ.setdefault("INBOUND_EMAIL_HMAC_SECRET", "dev-inbound-email-hmac-secret")

import yaml
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.ctms.migrate import run_migrations as run_ctms_migrations
from apps.ctms.models import Base as CTMSBase
from apps.econsent.models import Base as EConsentBase
from apps.eisf.database.migrate import run_migrations as run_eisf_migrations
from apps.eisf.models import Base as EISFBase
from apps.etmf.database.migrate import run_migrations as run_etmf_migrations
from apps.etmf.models import Base as ETMFBase
from apps.execution.database.migrate import run_migrations
from apps.interop.models import Base as InteropBase
from apps.notifications.models import Base as NotificationsBase
from apps.org.models import Base as OrgBase
from apps.quality.migrate import run_migrations as run_quality_migrations
from apps.quality.models import Base as QualityBase
from apps.safety.models import Base as SafetyBase
from apps.tickets.models import Base as TicketsBase

# Standard developer mock studies to seed into Neo4j
MOCK_STUDIES_NEO4J = [
    {
        "id": "CADENCE-101",
        "title": "Phase II Multi-Center Study of Cadence-X in Advanced Solid Tumors",
        "desc": "A Phase II randomized, double-blind clinical trial evaluating safety and efficacy.",
        "version_id": "ver_cadence_101",
    },
    {
        "id": "study_1",
        "title": "Oncology Phase II",
        "desc": "A study for solid tumors.",
        "version_id": "ver_study_1",
    },
    {
        "id": "study_001",
        "title": "Cardiovascular Phase III",
        "desc": "Cardiovascular study",
        "version_id": "ver_study_001",
    },
    {
        "id": "study_abc",
        "title": "Immunology Phase I",
        "desc": "Immunology study",
        "version_id": "ver_study_abc",
    },
    {
        "id": "study_xyz",
        "title": "Neurology Phase II",
        "desc": "Neurology study",
        "version_id": "ver_study_xyz",
    },
    {
        "id": "study_123",
        "title": "Pediatric Vaccine Trial",
        "desc": "Pediatric trial",
        "version_id": "ver_study_123",
    },
    {
        "id": "study_111",
        "title": "Rare Disease Study",
        "desc": "Rare disease study",
        "version_id": "ver_study_111",
    },
]


def validate_local_only(name: str, url: str | None) -> None:
    """
    Validates that a connection string points strictly to a local development environment.
    Aborts immediately if any production keyword or non-local host is detected.

    Args:
        name: The logical name of the database connection (e.g. 'Postgres', 'Neo4j').
        url: The database connection URL string.
    """
    if not url:
        return

    lower_url = url.lower()
    prod_keywords = [
        "production",
        "prod",
        "live",
        "secure",
        "aws",
        "rds",
        "azure",
        "gcp",
        "cloud",
    ]
    for keyword in prod_keywords:
        if keyword in lower_url:
            print(
                f"ERROR: Safety Guardrail Violation: Production/Non-local keyword '{keyword}' "
                f"detected in connection string for {name}. Aborting execution immediately.",
                file=sys.stderr,
            )
            sys.exit(1)

    if "://" in url:
        try:
            scheme, remainder = url.split("://", 1)
            if scheme.startswith("sqlite"):
                # SQLite is inherently a local/file-based database
                return

            # Parse hostname using a temporary parse scheme
            parsed = urllib.parse.urlparse(f"http://{remainder}")
            host = parsed.hostname
            if host:
                local_hosts = {
                    "localhost",
                    "127.0.0.1",
                    "0.0.0.0",  # nosec B104
                    "postgres",
                    "neo4j",
                    "db",
                    "host.docker.internal",
                }
                if host not in local_hosts and not host.endswith(".local"):
                    print(
                        f"ERROR: Safety Guardrail Violation: Database host '{host}' "
                        f"for {name} is non-local. Only local/development host environments are allowed.",
                        file=sys.stderr,
                    )
                    sys.exit(1)
        except Exception as e:
            print(
                f"ERROR: Failed to parse or validate connection URL for {name}: {e}. Aborting.",
                file=sys.stderr,
            )
            sys.exit(1)


async def reset_postgres(url: str, allow_offline: bool) -> None:
    """
    Clears PostgreSQL database schemas (public and audit_schema) without dropping the database,
    then programmatically re-applies schemas, migrations, and mandatory triggers.

    Args:
        url: PostgreSQL connection URL.
        allow_offline: If True, warnings will be printed instead of crashing if database is unreachable.
    """
    print(f"Purging and resetting PostgreSQL database using connection: {url}...")
    try:
        engine = create_async_engine(url, echo=False)
        async with engine.begin() as conn:
            # Drop schemas recursively to remove all tables, types, and triggers
            await conn.execute(text("DROP SCHEMA IF EXISTS public CASCADE;"))
            await conn.execute(text("CREATE SCHEMA public;"))
            await conn.execute(text("DROP SCHEMA IF EXISTS audit_schema CASCADE;"))
        await engine.dispose()

        # Re-apply migrations, tables, and triggers
        await run_migrations(url)
        await run_etmf_migrations(url)
        await run_ctms_migrations(url)
        await run_quality_migrations(url)
        await run_eisf_migrations(url)
        print(
            "PostgreSQL database schemas, migrations, and triggers successfully re-applied."
        )
    except Exception as e:
        if allow_offline:
            print(
                f"WARNING: PostgreSQL is currently unreachable or offline. Skipping reset. ({e})"
            )
        else:
            print(f"ERROR: PostgreSQL reset failed: {e}", file=sys.stderr)
            sys.exit(1)


async def reset_neo4j(
    uri: str,
    user: str,
    password: str,
    allow_offline: bool,
    seed_studies: list[dict[str, Any]] | None = None,
) -> None:
    """
    Wipes all nodes and relationships in Neo4j, then recreates standard developer mocks
    using the active connection driver.

    Args:
        uri: Bolt/Neo4j server URI.
        user: Username.
        password: Password.
        allow_offline: If True, warnings will be printed instead of crashing if database is unreachable.
        seed_studies: Optional list of study dicts to seed.
    """
    print(f"Purging and resetting Neo4j graph database using connection: {uri}...")
    try:
        from neo4j import AsyncGraphDatabase

        studies_to_seed = seed_studies or MOCK_STUDIES_NEO4J

        async with AsyncGraphDatabase.driver(uri, auth=(user, password)) as driver:
            # Purge all nodes and relationships
            async with driver.session() as session:
                await session.run("MATCH (n) DETACH DELETE n")
                print("Neo4j graph purged successfully.")

                # Recreate standard developer mocks and YAML baseline study
                print("Recreating developer mock studies in Neo4j...")
                for study in studies_to_seed:
                    # Create Study root node
                    await session.run(
                        "MERGE (s:Study {id: $study_id}) RETURN s.id",
                        study_id=study["id"],
                    )
                    # Create StudyProperties node and link
                    await session.run(
                        """
                        MATCH (s:Study {id: $study_id})
                        CREATE (sp:StudyProperties {
                            title: $title,
                            desc: $desc
                        })
                        CREATE (s)-[:HAS_PROPERTIES]->(sp)
                        """,
                        study_id=study["id"],
                        title=study.get("title", ""),
                        desc=study.get("desc", study.get("description", "")),
                    )
                    # Create StudyVersion node and link
                    await session.run(
                        """
                        MATCH (s:Study {id: $study_id})
                        CREATE (sv:StudyVersion {
                            id: $version_id,
                            version_tag: "1.0",
                            status: "DRAFT",
                            version_index: 1,
                            created_at: datetime(),
                            created_by: "system"
                        })
                        CREATE (s)-[:HAS_VERSION]->(sv)
                        """,
                        study_id=study["id"],
                        version_id=study.get(
                            "version_id", f"ver_{study['id'].lower()}"
                        ),
                    )
                print("Neo4j graph nodes successfully seeded.")
    except Exception as e:
        if allow_offline:
            print(
                f"WARNING: Neo4j is currently unreachable or offline. Skipping reset. ({e})"
            )
        else:
            print(f"ERROR: Neo4j reset failed: {e}", file=sys.stderr)
            sys.exit(1)


async def reset_sqlite_db(
    name: str, url: str, metadata: Any, allow_offline: bool
) -> None:
    """
    Drops all tables inside an SQLite or PostgreSQL database and re-applies metadata structure or migrations.

    Args:
        name: Logical microservice name.
        url: Database connection string.
        metadata: SQLAlchemy metadata containing tables to re-create.
        allow_offline: If True, warnings will be printed instead of crashing if database is unreachable.
    """
    if url.startswith(("postgres", "postgresql")):
        print(
            f"Wiping and migrating PostgreSQL database for {name} using connection: {url}..."
        )
        try:
            engine = create_async_engine(url, echo=False)
            async with engine.begin() as conn:
                await conn.execute(text("DROP SCHEMA IF EXISTS public CASCADE;"))
                await conn.execute(text("CREATE SCHEMA public;"))
                await conn.execute(text("DROP SCHEMA IF EXISTS audit_schema CASCADE;"))
            await engine.dispose()

            if name == "eTMF":
                await run_etmf_migrations(url)
            elif name == "CTMS":
                await run_ctms_migrations(url)
            elif name == "Quality":
                await run_quality_migrations(url)
            elif name == "eISF":
                await run_eisf_migrations(url)
            elif (
                name not in ("eTMF", "CTMS", "Quality", "eISF") and metadata is not None
            ):
                engine2 = create_async_engine(url, echo=False)
                async with engine2.begin() as conn:
                    await conn.run_sync(metadata.create_all)
                await engine2.dispose()

            print(f"PostgreSQL database for {name} purged and migrated successfully.")
        except Exception as e:
            if allow_offline:
                print(
                    f"WARNING: PostgreSQL database {name} could not be reset: {e}. Skipping."
                )
            else:
                print(
                    f"ERROR: PostgreSQL database reset failed for {name}: {e}",
                    file=sys.stderr,
                )
                sys.exit(1)
        return

    print(f"Wiping and migrating SQLite database for {name} using connection: {url}...")
    try:
        engine = create_async_engine(url, echo=False)
        async with engine.begin() as conn:
            # Disable foreign keys temporarily
            await conn.execute(text("PRAGMA foreign_keys = OFF;"))

            # Query all table names
            result = await conn.execute(
                text(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"
                )
            )
            tables = [row[0] for row in result.fetchall()]

            # Drop each table
            for t in tables:
                await conn.execute(text(f"DROP TABLE IF EXISTS {t};"))

            # Re-enable foreign keys
            await conn.execute(text("PRAGMA foreign_keys = ON;"))

            # Reapply schema using metadata (skip for services managed by Alembic migrations)
            if name not in ("eTMF", "CTMS", "Quality"):
                await conn.run_sync(metadata.create_all)

        await engine.dispose()

        if name == "eTMF":
            await run_etmf_migrations(url)
        elif name == "CTMS":
            await run_ctms_migrations(url)
        elif name == "Quality":
            await run_quality_migrations(url)
        elif name == "eISF":
            await run_eisf_migrations(url)

        print(f"SQLite database for {name} purged and migrated successfully.")
    except Exception as e:
        if allow_offline:
            print(f"WARNING: SQLite database {name} could not be reset: {e}. Skipping.")
        else:
            print(
                f"ERROR: SQLite database reset failed for {name}: {e}", file=sys.stderr
            )
            sys.exit(1)


async def seed_sqlite_edl(etmf_url: str, allow_offline: bool) -> None:
    """
    Populates local eTMF database with default clinical Expected Document Lists (EDLs).

    Args:
        etmf_url: SQLite connection string for eTMF database.
        allow_offline: If True, ignore failure to connect.
    """
    print(
        f"Seeding Expected Document Lists (EDLs) to eTMF database using connection: {etmf_url}..."
    )
    try:
        from apps.etmf.main import seed_default_edl

        engine = create_async_engine(etmf_url, echo=False)
        session_maker = async_sessionmaker(
            bind=engine, class_=AsyncSession, expire_on_commit=False
        )

        async with session_maker() as session:
            for study_id in [
                "CADENCE-101",
                "study_001",
                "study_abc",
                "study_xyz",
                "study_123",
                "study_111",
            ]:
                for milestone in ["INITIATION", "CONDUCT", "CLOSEOUT"]:
                    await seed_default_edl(session, study_id, milestone)
            await session.commit()

        await engine.dispose()
        print("Expected Document Lists (EDLs) seeded successfully.")
    except Exception as e:
        if allow_offline:
            print(f"WARNING: SQLite EDL seeding could not be completed: {e}. Skipping.")
        else:
            print(f"ERROR: SQLite EDL seeding failed: {e}", file=sys.stderr)
            sys.exit(1)


def load_yaml_seed_data(seed_path: Path) -> dict[str, Any]:
    """
    Loads and parses YAML-driven baseline clinical trial data.

    Args:
        seed_path: Path to baseline clinical trial YAML file.

    Returns:
        Parsed dictionary of clinical trial seed data.
    """
    if not seed_path.exists():
        print(
            f"WARNING: Seed file {seed_path} not found. Skipping YAML baseline seeding."
        )
        return {}

    with open(seed_path, encoding="utf-8") as f:
        return yaml.safe_load(f) or {}


async def seed_baseline_clinical_data(
    seed_data: dict[str, Any],
    sqlite_databases: dict[str, tuple[str, Any]],
    allow_offline: bool,
) -> None:
    """
    Seeds parsed YAML baseline clinical trial data across SQLite microservice instances.

    Args:
        seed_data: Dictionary of parsed baseline clinical trial entities.
        sqlite_databases: Mapping of service names to (db_url, metadata).
        allow_offline: If True, ignore connection failures.
    """
    if not seed_data:
        return

    print("Seeding baseline clinical trial data from YAML configuration...")

    study_info = seed_data.get("study", {})
    study_id = study_info.get("id", "CADENCE-101")
    consents = seed_data.get("consents", [])
    documents = seed_data.get("documents", [])
    sae_cases = seed_data.get("sae_cases", [])

    # 1. eConsent Seeding
    econsent_url = sqlite_databases["eConsent"][0]
    try:
        engine = create_async_engine(econsent_url, echo=False)
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS subject_consents (
                    id VARCHAR(255) PRIMARY KEY,
                    subject_pseudonym VARCHAR(255),
                    study_id VARCHAR(255),
                    site_id VARCHAR(255),
                    template_id VARCHAR(255),
                    version_index INTEGER,
                    protocol_version VARCHAR(255),
                    source_content_identity VARCHAR(255),
                    status VARCHAR(50) NOT NULL DEFAULT 'ACTIVE',
                    server_timestamp VARCHAR(255),
                    signature_manifest TEXT,
                    created_at VARCHAR(255),
                    created_by VARCHAR(255),
                    reason_for_change VARCHAR(1000)
                );
            """)
            )
            for consent in consents:
                await conn.execute(
                    text("""
                    INSERT OR REPLACE INTO subject_consents (
                        id, subject_pseudonym, study_id, site_id, template_id,
                        version_index, protocol_version, source_content_identity, status,
                        server_timestamp, signature_manifest, created_at, created_by, reason_for_change
                    ) VALUES (
                        :id, :subject_pseudonym, :study_id, :site_id, :template_id,
                        :version_index, :protocol_version, :source_content_identity, :status,
                        :server_timestamp, :signature_manifest, :created_at, :created_by, :reason_for_change
                    );
                """),
                    {
                        "id": consent.get("id", "CONSENT-101-001"),
                        "subject_pseudonym": consent.get("subject_id", "SUBJ-101-001"),
                        "study_id": consent.get("study_id", study_id),
                        "site_id": consent.get("site_id", "SITE-101"),
                        "template_id": consent.get("template_id", "ICF-TEMPLATE-001"),
                        "version_index": consent.get("version_index", 1),
                        "protocol_version": consent.get("protocol_version", "1.0"),
                        "source_content_identity": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                        "status": consent.get("status", "ACTIVE"),
                        "server_timestamp": consent.get(
                            "signed_at", "2026-08-01T10:00:00Z"
                        ),
                        "signature_manifest": '{"signature_type": "ELECTRONIC", "algorithm": "ES256"}',
                        "created_at": "2026-08-01T10:00:00Z",
                        "created_by": "system",
                        "reason_for_change": "Initial study subject consent",
                    },
                )
        await engine.dispose()
    except Exception as e:
        if allow_offline:
            print(f"WARNING: eConsent YAML seeding skipped: {e}")
        else:
            print(f"ERROR: eConsent YAML seeding failed: {e}", file=sys.stderr)
            sys.exit(1)

    # 2. eISF Seeding
    eisf_url = sqlite_databases["eISF"][0]
    try:
        engine = create_async_engine(eisf_url, echo=False)
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS isf_documents (
                    id VARCHAR(255) PRIMARY KEY,
                    study_id VARCHAR(255),
                    site_id VARCHAR(255),
                    binder_classification VARCHAR(255),
                    filename VARCHAR(255),
                    content TEXT,
                    mime_type VARCHAR(255),
                    version_index INTEGER,
                    created_at VARCHAR(255),
                    created_by VARCHAR(255),
                    sync_status VARCHAR(255),
                    source_system VARCHAR(255)
                );
            """)
            )
            for doc in documents:
                await conn.execute(
                    text("""
                    INSERT OR REPLACE INTO isf_documents (
                        id, study_id, site_id, binder_classification, filename,
                        content, mime_type, version_index, created_at, created_by,
                        sync_status, source_system
                    ) VALUES (
                        :id, :study_id, :site_id, :binder_classification, :filename,
                        :content, :mime_type, :version_index, :created_at, :created_by,
                        :sync_status, :source_system
                    );
                """),
                    {
                        "id": doc.get("id", "DOC-TMF-001"),
                        "study_id": doc.get("study_id", study_id),
                        "site_id": doc.get("site_id", "SITE-101"),
                        "binder_classification": "01_REGULATORY",
                        "filename": doc.get("filename", "Protocol_v1.0_Signed.pdf"),
                        "content": doc.get("content", "JVBERi0xLjQKJ..."),
                        "mime_type": doc.get("mime_type", "application/pdf"),
                        "version_index": 1,
                        "created_at": "2026-08-01T10:00:00Z",
                        "created_by": doc.get("created_by", "system"),
                        "sync_status": "SYNCED",
                        "source_system": "eISF",
                    },
                )
        await engine.dispose()
    except Exception as e:
        if allow_offline:
            print(f"WARNING: eISF YAML seeding skipped: {e}")
        else:
            print(f"ERROR: eISF YAML seeding failed: {e}", file=sys.stderr)
            sys.exit(1)

    # 3. Safety Seeding
    safety_url = sqlite_databases["Safety"][0]
    try:
        engine = create_async_engine(safety_url, echo=False)
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS sae_cases (
                    id VARCHAR(255) PRIMARY KEY,
                    subject_id VARCHAR(255),
                    term VARCHAR(255),
                    severity VARCHAR(255),
                    causality VARCHAR(255),
                    outcome VARCHAR(255),
                    reported_at VARCHAR(255)
                );
            """)
            )
            for sae in sae_cases:
                await conn.execute(
                    text("""
                    INSERT OR REPLACE INTO sae_cases (
                        id, subject_id, term, severity, causality, outcome, reported_at
                    ) VALUES (
                        :id, :subject_id, :term, :severity, :causality, :outcome, :reported_at
                    );
                """),
                    {
                        "id": sae.get("id", "SAE-001"),
                        "subject_id": sae.get("subject_id", "SUBJ-101-002"),
                        "term": sae.get("term", "Febrile Neutropenia"),
                        "severity": sae.get("severity", "SEVERE"),
                        "causality": sae.get("causality", "RELATED"),
                        "outcome": sae.get("outcome", "RESOLVED"),
                        "reported_at": sae.get("reported_at", "2026-08-10T14:30:00Z"),
                    },
                )
        await engine.dispose()
    except Exception as e:
        if allow_offline:
            print(f"WARNING: Safety YAML seeding skipped: {e}")
        else:
            print(f"ERROR: Safety YAML seeding failed: {e}", file=sys.stderr)
            sys.exit(1)

    # 4. Tickets Seeding
    tickets_url = sqlite_databases["Tickets"][0]
    try:
        engine = create_async_engine(tickets_url, echo=False)
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS clinical_tickets (
                    id VARCHAR(255) PRIMARY KEY,
                    category VARCHAR(255),
                    title VARCHAR(255),
                    priority VARCHAR(255),
                    status VARCHAR(255)
                );
            """)
            )
            await conn.execute(
                text("""
                INSERT OR REPLACE INTO clinical_tickets (id, category, title, priority, status)
                VALUES (:id, :category, :title, :priority, :status);
            """),
                {
                    "id": "TCK-101",
                    "category": "DATA_DISCREPANCY",
                    "title": "Out-of-range systolic blood pressure on Visit 2",
                    "priority": "HIGH",
                    "status": "OPEN",
                },
            )
        await engine.dispose()
    except Exception as e:
        if allow_offline:
            print(f"WARNING: Tickets YAML seeding skipped: {e}")
        else:
            print(f"ERROR: Tickets YAML seeding failed: {e}", file=sys.stderr)
            sys.exit(1)

    # 5. Notifications Seeding
    notifications_url = sqlite_databases["Notifications"][0]
    try:
        engine = create_async_engine(notifications_url, echo=False)
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS notification_dispatches (
                    id VARCHAR(255) PRIMARY KEY,
                    channel VARCHAR(255),
                    recipient VARCHAR(255),
                    subject VARCHAR(255),
                    status VARCHAR(255),
                    dispatched_at VARCHAR(255)
                );
            """)
            )
            await conn.execute(
                text("""
                INSERT OR REPLACE INTO notification_dispatches (id, channel, recipient, subject, status, dispatched_at)
                VALUES (:id, :channel, :recipient, :subject, :status, :dispatched_at);
            """),
                {
                    "id": "NTF-001",
                    "channel": "EMAIL",
                    "recipient": "crc.site101@example.com",  # deid-ignore
                    "subject": "Protocol Amendment v2.0 Pending Re-Consent",
                    "status": "DELIVERED",
                    "dispatched_at": "2026-08-12T09:00:00Z",
                },
            )
        await engine.dispose()
    except Exception as e:
        if allow_offline:
            print(f"WARNING: Notifications YAML seeding skipped: {e}")
        else:
            print(f"ERROR: Notifications YAML seeding failed: {e}", file=sys.stderr)
            sys.exit(1)

    # 6. Interop Seeding
    interop_url = sqlite_databases["Interop"][0]
    try:
        engine = create_async_engine(interop_url, echo=False)
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE IF NOT EXISTS interop_messages (
                    id VARCHAR(255) PRIMARY KEY,
                    direction VARCHAR(255),
                    format VARCHAR(255),
                    payload TEXT,
                    status VARCHAR(255),
                    created_at VARCHAR(255)
                );
            """)
            )
            await conn.execute(
                text("""
                INSERT OR REPLACE INTO interop_messages (id, direction, format, payload, status, created_at)
                VALUES (:id, :direction, :format, :payload, :status, :created_at);
            """),
                {
                    "id": "MSG-001",
                    "direction": "INGEST",
                    "format": "CDISC_ODM",
                    "payload": '{"msg": "Laboratory Result Ingest", "status": "PARSED"}',
                    "status": "PROCESSED",
                    "created_at": "2026-08-15T11:20:00Z",
                },
            )
        await engine.dispose()
    except Exception as e:
        if allow_offline:
            print(f"WARNING: Interop YAML seeding skipped: {e}")
        else:
            print(f"ERROR: Interop YAML seeding failed: {e}", file=sys.stderr)
            sys.exit(1)

    print("Baseline clinical trial data successfully seeded across all databases.")


def get_sqlite_url(env_name: str, file_name: str) -> str:
    """
    Returns SQL connection string from environment if defined, otherwise defaults
    to local workspace path under /app directory.

    Args:
        env_name: Environment variable name.
        file_name: Default database file name under /app/.
    """
    val = os.getenv(env_name)
    if val:
        return val
    return f"sqlite+aiosqlite:////app/{file_name}"


async def main() -> None:
    """
    Parses CLI args, runs safety checks, and orchestrates multi-database cleanup and seeding concurrently.
    """
    parser = argparse.ArgumentParser(
        description="Multi-Database Programmatic CLI Reset and Seeding Tool"
    )
    parser.add_argument(
        "--allow-offline",
        action="store_true",
        help="If any targeted database is unreachable/offline, log warning instead of crashing.",
    )
    parser.add_argument(
        "--seed-file",
        type=str,
        default="data/seeds/baseline_clinical_trial.yaml",
        help="Path to YAML seed data file relative to repo root.",
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[1]
    seed_path = repo_root / args.seed_file

    # 1. Resolve Active Connection Strings
    postgres_url = os.getenv(
        "DATABASE_URL",
        "postgresql+asyncpg://cadence:cadence_password@localhost:5432/cadence_edc",  # pragma: allowlist secret
    )
    neo4j_uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    neo4j_user = os.getenv("NEO4J_USER", "neo4j")
    neo4j_password = os.getenv(
        "NEO4J_PASSWORD", "cadence_password"
    )  # pragma: allowlist secret

    sqlite_databases = {
        "eTMF": (get_sqlite_url("ETMF_DATABASE_URL", "tmf.db"), ETMFBase.metadata),
        "CTMS": (get_sqlite_url("CTMS_DATABASE_URL", "ctms.db"), CTMSBase.metadata),
        "Quality": (
            get_sqlite_url("QUALITY_DATABASE_URL", "quality.db"),
            QualityBase.metadata,
        ),
        "Interop": (
            get_sqlite_url("INTEROP_DATABASE_URL", "interop.db"),
            InteropBase.metadata,
        ),
        "Tickets": (
            get_sqlite_url("TICKETS_DATABASE_URL", "tickets.db"),
            TicketsBase.metadata,
        ),
        "Notifications": (
            get_sqlite_url("NOTIFICATIONS_DATABASE_URL", "notifications.db"),
            NotificationsBase.metadata,
        ),
        "eConsent": (
            get_sqlite_url("ECONSENT_DATABASE_URL", "econsent.db"),
            EConsentBase.metadata,
        ),
        "Safety": (
            get_sqlite_url("SAFETY_DATABASE_URL", "safety.db"),
            SafetyBase.metadata,
        ),
        "Organization": (
            get_sqlite_url("ORG_DATABASE_URL", "org.db"),
            OrgBase.metadata,
        ),
        "eISF": (get_sqlite_url("EISF_DATABASE_URL", "eisf.db"), EISFBase.metadata),
    }

    # 2. Safety Guardrails Checks
    validate_local_only("Postgres", postgres_url)
    validate_local_only("Neo4j", neo4j_uri)
    for db_name, (db_url, _) in sqlite_databases.items():
        validate_local_only(db_name, db_url)

    # 3. Load Seed Data from YAML
    seed_data = load_yaml_seed_data(seed_path)
    neo4j_studies = None
    if seed_data and "study" in seed_data:
        study = seed_data["study"]
        neo4j_studies = [
            {
                "id": study.get("id", "CADENCE-101"),
                "title": study.get("title", ""),
                "desc": study.get("description", ""),
                "version_id": study.get("version_id", "ver_cadence_101"),
            }
        ]

    # 4. Concurrent Multi-Database Purging & Schema Migration Setup
    tasks = []

    # PostgreSQL task
    tasks.append(reset_postgres(postgres_url, args.allow_offline))

    # Neo4j task
    tasks.append(
        reset_neo4j(
            neo4j_uri,
            neo4j_user,
            neo4j_password,
            args.allow_offline,
            seed_studies=neo4j_studies,
        )
    )

    # SQLite tasks
    for db_name, (db_url, metadata) in sqlite_databases.items():
        tasks.append(reset_sqlite_db(db_name, db_url, metadata, args.allow_offline))

    # Execute purging and schema re-creation concurrently
    print("Executing multi-database schema baseline operations concurrently...")
    await asyncio.gather(*tasks)

    # 5. Seeding Default Data (EDLs & YAML Baseline Data)
    etmf_url = sqlite_databases["eTMF"][0]
    await seed_sqlite_edl(etmf_url, args.allow_offline)
    await seed_baseline_clinical_data(
        seed_data, sqlite_databases, allow_offline=args.allow_offline
    )

    print(
        "\nSUCCESS: All targeted database instances purged, migrated, and seeded successfully with zero container restarts."
    )
    sys.exit(0)


if __name__ == "__main__":
    asyncio.run(main())
