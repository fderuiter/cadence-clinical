#!/usr/bin/env python3
"""
Programmatic Multi-Database CLI Tool.

Wipes, resets, migrates, and seeds the PostgreSQL, Neo4j, and SQLite instances
of the Cadence Clinical environment concurrently. Includes strict safety guards
to prevent accidental execution against production database environments.
"""

import argparse
import asyncio
import os
import sys
import urllib.parse
from typing import Any, Optional

from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

# Import SQLite Bases for metadata extraction
from apps.ctms.models import Base as CTMSBase
from apps.econsent.models import Base as EConsentBase
from apps.eisf.models import Base as EISFBase
from apps.etmf.models import Base as ETMFBase

# Import PostgreSQL migrations
from apps.execution.database.migrate import run_migrations
from apps.interop.models import Base as InteropBase
from apps.notifications.models import Base as NotificationsBase
from apps.org.models import Base as OrgBase
from apps.quality.models import Base as QualityBase
from apps.safety.models import Base as SafetyBase
from apps.tickets.models import Base as TicketsBase

# Standard developer mock studies to seed into Neo4j
MOCK_STUDIES_NEO4J = [
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


def validate_local_only(name: str, url: Optional[str]) -> None:
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


async def reset_neo4j(uri: str, user: str, password: str, allow_offline: bool) -> None:
    """
    Wipes all nodes and relationships in Neo4j, then recreates standard developer mocks
    using the active connection driver.

    Args:
        uri: Bolt/Neo4j server URI.
        user: Username.
        password: Password.
        allow_offline: If True, warnings will be printed instead of crashing if database is unreachable.
    """
    print(f"Purging and resetting Neo4j graph database using connection: {uri}...")
    try:
        from neo4j import AsyncGraphDatabase

        async with AsyncGraphDatabase.driver(uri, auth=(user, password)) as driver:
            # Purge all nodes and relationships
            async with driver.session() as session:
                await session.run("MATCH (n) DETACH DELETE n")
                print("Neo4j graph purged successfully.")

                # Recreate standard developer mocks
                print("Recreating standard developer mock studies in Neo4j...")
                for study in MOCK_STUDIES_NEO4J:
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
                        title=study["title"],
                        desc=study["desc"],
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
                        version_id=study["version_id"],
                    )
                print("Neo4j standard developer mocks successfully seeded.")
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
    Drops all tables inside an SQLite database and re-applies metadata structure.

    Args:
        name: Logical microservice name.
        url: SQLite database connection string.
        metadata: SQLAlchemy metadata containing tables to re-create.
        allow_offline: If True, warnings will be printed instead of crashing if database is unreachable.
    """
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

            # Reapply schema using metadata
            await conn.run_sync(metadata.create_all)

        await engine.dispose()
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
        from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

        from apps.etmf.main import seed_default_edl

        engine = create_async_engine(etmf_url, echo=False)
        session_maker = async_sessionmaker(
            bind=engine, class_=AsyncSession, expire_on_commit=False
        )

        async with session_maker() as session:
            for study_id in [
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
    args = parser.parse_args()

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

    # 3. Concurrent Multi-Database Purging & Schema Migration Setup
    tasks = []

    # PostgreSQL task
    tasks.append(reset_postgres(postgres_url, args.allow_offline))

    # Neo4j task
    tasks.append(reset_neo4j(neo4j_uri, neo4j_user, neo4j_password, args.allow_offline))

    # SQLite tasks
    for db_name, (db_url, metadata) in sqlite_databases.items():
        tasks.append(reset_sqlite_db(db_name, db_url, metadata, args.allow_offline))

    # Execute purging and schema re-creation concurrently
    print("Executing multi-database schema baseline operations concurrently...")
    await asyncio.gather(*tasks)

    # 4. Seeding Default Data (EDLs)
    # This task depends on the schema being fully migrated beforehand
    etmf_url = sqlite_databases["eTMF"][0]
    await seed_sqlite_edl(etmf_url, args.allow_offline)

    print(
        "\nSUCCESS: All targeted database instances purged, migrated, and seeded successfully with zero container restarts."
    )
    sys.exit(0)


if __name__ == "__main__":
    asyncio.run(main())
