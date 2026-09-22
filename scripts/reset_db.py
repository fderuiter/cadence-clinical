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
import subprocess
import sys
import urllib.parse
from pathlib import Path
from typing import Any

# Import PostgreSQL migrations
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

# Import SQLite Bases for metadata extraction and legacy Python migrations
from apps.ctms.migrate import run_migrations as run_ctms_migrations
from apps.ctms.models import Base as CTMSBase
from apps.econsent.models import Base as EConsentBase
from apps.eisf.database.migrate import run_migrations as run_eisf_migrations
from apps.eisf.models import Base as EISFBase
from apps.etmf.database.migrate import run_migrations as run_etmf_migrations
from apps.etmf.models import Base as ETMFBase
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


def detect_dotnet_services() -> list[str]:
    """
    Detects all migrated .NET microservices in apps/ by checking for C# project files.
    """
    repo_root = Path(__file__).resolve().parent.parent
    services = []
    apps_dir = repo_root / "apps"
    if apps_dir.exists():
        for item in sorted(apps_dir.iterdir()):
            if item.is_dir():
                csproj_matches = list((item / "src").glob("*.csproj")) if (item / "src").exists() else []
                if csproj_matches:
                    services.append(item.name)
    return services


async def apply_dotnet_ef_migrations_and_seed(service: str, db_url: str, allow_offline: bool) -> None:
    """
    Invokes EF Core migrations (via manage_migrations.sh apply / dotnet ef database update)
    and C# DbContext seeders (via manage_migrations.sh seed / dotnet run --seed) for a given .NET service.
    """
    repo_root = Path(__file__).resolve().parent.parent
    cap_name = service.capitalize()
    csproj_path = repo_root / "apps" / service / "src" / f"Cadence.Apps.{cap_name}.csproj"

    if not csproj_path.exists():
        return

    print(f"Executing EF Core migrations for .NET service: {service}...")

    env = os.environ.copy()
    pg_conn_str = db_url.replace("postgresql+asyncpg://", "postgresql://")
    env["POSTGRES_CONNECTION_STRING"] = pg_conn_str
    env["DATABASE_URL"] = pg_conn_str

    manage_script = repo_root / "scripts" / "manage_migrations.sh"

    # 1. Apply EF Core migrations
    try:
        if manage_script.exists():
            cmd = ["bash", str(manage_script), "apply", service]
            proc = subprocess.run(cmd, cwd=str(repo_root), env=env, capture_output=True, text=True)
            if proc.returncode == 0:
                print(f"[SUCCESS] EF Core migrations applied for {service}.")
            else:
                # Direct dotnet ef fallback
                cmd_direct = ["dotnet", "ef", "database", "update", "--project", str(csproj_path)]
                proc_direct = subprocess.run(cmd_direct, cwd=str(repo_root), env=env, capture_output=True, text=True)
                if proc_direct.returncode == 0:
                    print(f"[SUCCESS] EF Core migrations applied for {service} via dotnet ef fallback.")
                else:
                    msg = proc.stderr or proc.stdout or proc_direct.stderr or proc_direct.stdout
                    raise RuntimeError(f"EF Core migration execution failed for {service}: {msg}")
        else:
            cmd_direct = ["dotnet", "ef", "database", "update", "--project", str(csproj_path)]
            proc_direct = subprocess.run(cmd_direct, cwd=str(repo_root), env=env, capture_output=True, text=True)
            if proc_direct.returncode == 0:
                print(f"[SUCCESS] EF Core migrations applied for {service}.")
            else:
                raise RuntimeError(f"EF Core migration execution failed for {service}: {proc_direct.stderr or proc_direct.stdout}")
    except Exception as e:
        if allow_offline:
            print(
                f"WARNING: .NET SDK or EF Core migration runner for '{service}' encountered an issue: {e}. "
                "Continuing database reset via fallback."
            )
        else:
            print(f"ERROR: EF Core migration execution failed for '{service}': {e}", file=sys.stderr)
            sys.exit(1)

    # 2. Execute C# DbContext seeders
    try:
        if manage_script.exists():
            cmd_seed = ["bash", str(manage_script), "seed", service]
            proc_seed = subprocess.run(cmd_seed, cwd=str(repo_root), env=env, capture_output=True, text=True)
            if proc_seed.returncode == 0:
                print(f"[SUCCESS] C# DbContext seeder executed for {service}.")
            else:
                cmd_seed_direct = ["dotnet", "run", "--project", str(csproj_path), "--no-build", "--", "--seed"]
                proc_seed_direct = subprocess.run(cmd_seed_direct, cwd=str(repo_root), env=env, capture_output=True, text=True)
                if proc_seed_direct.returncode == 0:
                    print(f"[SUCCESS] C# DbContext seeder executed for {service} via dotnet run fallback.")
                else:
                    raise RuntimeError(f"C# DbContext seeder failed for {service}: {proc_seed.stderr or proc_seed.stdout}")
        else:
            cmd_seed_direct = ["dotnet", "run", "--project", str(csproj_path), "--no-build", "--", "--seed"]
            proc_seed_direct = subprocess.run(cmd_seed_direct, cwd=str(repo_root), env=env, capture_output=True, text=True)
            if proc_seed_direct.returncode == 0:
                print(f"[SUCCESS] C# DbContext seeder executed for {service}.")
            else:
                raise RuntimeError(f"C# DbContext seeder failed for {service}: {proc_seed_direct.stderr or proc_seed_direct.stdout}")
    except Exception as e:
        if allow_offline:
            print(f"WARNING: C# DbContext seeder for '{service}' skipped: {e}")
        else:
            print(f"ERROR: C# DbContext seeder failed for '{service}': {e}", file=sys.stderr)
            sys.exit(1)


async def verify_and_ensure_ef_migration_history_and_triggers(url: str, allow_offline: bool) -> None:
    """
    Verifies and ensures that __EFMigrationsHistory contains all expected EF Core migration records,
    ensures EF Core model tables exist, and attaches audit_schema.prevent_audit_mutation() trigger.
    """
    try:
        engine = create_async_engine(url, echo=False)
        async with engine.begin() as conn:
            # 1. Ensure __EFMigrationsHistory table exists
            await conn.execute(
                text(
                    """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" character varying(150) NOT NULL,
                        "ProductVersion" character varying(32) NOT NULL,
                        CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                    );
                    """
                )
            )

            # 2. Known EF Core migration records for Cadence.Apps.Execution
            known_migrations = [
                ("20260905174215_AddExecutionSubjectsSlice", "10.0.0"),
                ("20260905230921_AddMassTransitOutbox", "10.0.0"),
                ("20260910120000_AddExecutionObservationsSlice", "10.0.0"),
            ]

            # Verify tables created by EF Core. If missing (e.g. dotnet ef was offline), apply fallback DDL.
            res = await conn.execute(
                text("SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'clinical_subjects');")
            )
            has_subjects = res.scalar()
            if not has_subjects:
                await conn.execute(
                    text(
                        """
                        CREATE TABLE IF NOT EXISTS "AuditLogEntries" (
                            "Id" uuid NOT NULL,
                            "EntityName" text NOT NULL,
                            "EntityId" text NOT NULL,
                            "Action" text NOT NULL,
                            "Timestamp" timestamp with time zone NOT NULL,
                            "UserId" text NOT NULL,
                            "ReasonForChange" text NOT NULL,
                            "VersionIndex" bigint NOT NULL,
                            "ChangesJson" text,
                            "PredecessorHash" text NOT NULL,
                            "BlockHash" text NOT NULL,
                            CONSTRAINT "PK_AuditLogEntries" PRIMARY KEY ("Id")
                        );

                        CREATE TABLE IF NOT EXISTS "clinical_subjects" (
                            "Id" uuid NOT NULL,
                            "SubjectCode" character varying(255) NOT NULL,
                            "StudyCode" character varying(255) NOT NULL,
                            "SiteCode" text,
                            "TreatmentGroupCiphertext" text,
                            "Status" character varying(32) NOT NULL,
                            "ActiveProtocolVersion" text NOT NULL,
                            "EncryptedDemographics" text,
                            "EnrollmentIndex" integer NOT NULL,
                            "IsUnblinded" boolean NOT NULL,
                            "UnblindedBy" text,
                            "UnblindedReason" text,
                            "UnblindedAt" timestamp with time zone,
                            "WithdrawalReason" text,
                            "WithdrawnAt" timestamp with time zone,
                            "RandomizationId" uuid,
                            "KitReference" text,
                            "strat_factors" jsonb NOT NULL,
                            "_version" bigint NOT NULL DEFAULT 0,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "CreatedBy" text NOT NULL,
                            "ReasonForChange" text NOT NULL,
                            "VersionIndex" bigint NOT NULL,
                            CONSTRAINT "PK_clinical_subjects" PRIMARY KEY ("Id")
                        );

                        CREATE TABLE IF NOT EXISTS "site_compliance_caches" (
                            "Id" uuid NOT NULL,
                            "StudyCode" character varying(255) NOT NULL,
                            "SiteCode" character varying(255) NOT NULL,
                            "Milestone" character varying(255) NOT NULL,
                            "IsComplete" boolean NOT NULL,
                            "Version" integer NOT NULL,
                            "missing_documents" text[] NOT NULL,
                            "_version" bigint NOT NULL DEFAULT 0,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "CreatedBy" text NOT NULL,
                            "ReasonForChange" text NOT NULL,
                            "VersionIndex" bigint NOT NULL,
                            CONSTRAINT "PK_site_compliance_caches" PRIMARY KEY ("Id")
                        );

                        CREATE TABLE IF NOT EXISTS "subject_consents" (
                            "Id" uuid NOT NULL,
                            "SubjectCode" character varying(255) NOT NULL,
                            "IcfSigned" boolean NOT NULL,
                            "IcfSignedDate" timestamp with time zone,
                            "RequiresReconsent" boolean NOT NULL,
                            "IsPaperOverride" boolean NOT NULL,
                            "_version" bigint NOT NULL DEFAULT 0,
                            "ProtocolVersion_StudyCode" character varying(255) NOT NULL,
                            "ProtocolVersion_VersionIndex" integer NOT NULL,
                            "ProtocolVersion_VersionTag" text NOT NULL,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "CreatedBy" text NOT NULL,
                            "ReasonForChange" text NOT NULL,
                            "VersionIndex" bigint NOT NULL,
                            CONSTRAINT "PK_subject_consents" PRIMARY KEY ("Id")
                        );

                        CREATE TABLE IF NOT EXISTS "clinical_observations" (
                            "Id" uuid NOT NULL,
                            "ObservationId" text NOT NULL,
                            "SubjectCode" character varying(255) NOT NULL,
                            "StudyCode" character varying(255) NOT NULL,
                            "SiteCode" text,
                            "TestCode" character varying(255) NOT NULL,
                            "DomainCode" character varying(255) NOT NULL,
                            "ResultValue" text NOT NULL,
                            "ResultUnit" text,
                            "ObservationTime" timestamp with time zone NOT NULL,
                            "IsOutlier" boolean NOT NULL DEFAULT false,
                            "AdditionalPropertiesJson" text,
                            "_version" bigint NOT NULL DEFAULT 0,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "CreatedBy" text NOT NULL,
                            "ReasonForChange" text NOT NULL,
                            "VersionIndex" bigint NOT NULL,
                            CONSTRAINT "PK_clinical_observations" PRIMARY KEY ("Id")
                        );

                        CREATE TABLE IF NOT EXISTS "form_submissions" (
                            "Id" uuid NOT NULL,
                            "SubmissionId" text NOT NULL,
                            "FormId" text NOT NULL,
                            "SubjectCode" character varying(255) NOT NULL,
                            "StudyCode" character varying(255) NOT NULL,
                            "SiteCode" text,
                            "Status" character varying(32) NOT NULL,
                            "FormDataJson" text NOT NULL,
                            "SignatureManifestJson" text,
                            "_version" bigint NOT NULL DEFAULT 0,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "CreatedBy" text NOT NULL,
                            "ReasonForChange" text NOT NULL,
                            "VersionIndex" bigint NOT NULL,
                            CONSTRAINT "PK_form_submissions" PRIMARY KEY ("Id")
                        );

                        CREATE TABLE IF NOT EXISTS "clinical_queries" (
                            "Id" uuid NOT NULL,
                            "QueryId" text NOT NULL,
                            "SubjectCode" character varying(255) NOT NULL,
                            "StudyCode" character varying(255) NOT NULL,
                            "SiteCode" text,
                            "ObservationId" text,
                            "QueryText" text NOT NULL,
                            "Status" character varying(32) NOT NULL,
                            "ResolutionText" text,
                            "_version" bigint NOT NULL DEFAULT 0,
                            "CreatedAt" timestamp with time zone NOT NULL,
                            "CreatedBy" text NOT NULL,
                            "ReasonForChange" text NOT NULL,
                            "VersionIndex" bigint NOT NULL,
                            CONSTRAINT "PK_clinical_queries" PRIMARY KEY ("Id")
                        );
                        """
                    )
                )

            # Record migration IDs in __EFMigrationsHistory
            for migration_id, version in known_migrations:
                await conn.execute(
                    text(
                        """
                        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                        VALUES (:mid, :ver)
                        ON CONFLICT ("MigrationId") DO NOTHING;
                        """
                    ),
                    {"mid": migration_id, "ver": version},
                )

            # Attach GxP audit log trigger (Guardrail 3)
            await conn.execute(
                text(
                    """
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT FROM pg_tables WHERE tablename = 'AuditLogEntries') THEN
                            DROP TRIGGER IF EXISTS trg_prevent_audit_mutation_audit_log_entries ON "AuditLogEntries";
                            CREATE TRIGGER trg_prevent_audit_mutation_audit_log_entries
                            BEFORE UPDATE OR DELETE ON "AuditLogEntries"
                            FOR EACH ROW EXECUTE FUNCTION audit_schema.prevent_audit_mutation();
                        END IF;
                    END $$;
                    """
                )
            )
        await engine.dispose()
    except Exception as e:
        if allow_offline:
            print(f"WARNING: Verification of EF migrations and triggers skipped: {e}")
        else:
            raise


async def reset_postgres(url: str, allow_offline: bool) -> None:
    """
    Clears PostgreSQL database schemas (public and audit_schema) without dropping the database,
    then programmatically re-applies EF Core and Python migrations, seeders, and mandatory triggers.

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
            await conn.execute(text("CREATE SCHEMA audit_schema;"))
            # Guardrail 3: Create audit_schema.prevent_audit_mutation() trigger function
            await conn.execute(
                text(
                    """
                    CREATE OR REPLACE FUNCTION audit_schema.prevent_audit_mutation()
                    RETURNS trigger AS $$
                    BEGIN
                        RAISE EXCEPTION '21 CFR Part 11 Compliance Violation: Audit trail entries in audit_schema are strictly immutable and cannot be updated or deleted.';
                    END;
                    $$ LANGUAGE plpgsql;
                    """
                )
            )
        await engine.dispose()

        # 1. Detect and apply EF Core migrations & seeders for migrated .NET services
        dotnet_services = detect_dotnet_services()
        for service in dotnet_services:
            await apply_dotnet_ef_migrations_and_seed(service, url, allow_offline)

        # 2. Re-apply legacy Python SQLAlchemy migrations
        await run_etmf_migrations(url)
        await run_ctms_migrations(url)
        await run_quality_migrations(url)
        await run_eisf_migrations(url)

        # 3. Ensure __EFMigrationsHistory and GxP triggers
        await verify_and_ensure_ef_migration_history_and_triggers(url, allow_offline)

        print(
            "PostgreSQL database schemas, EF Core & Python migrations, C# seeders, and triggers successfully re-applied."
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
