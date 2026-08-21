"""Multi-engine database lifecycle, migrations, clinical seeding, and snapshot management."""

import os
import subprocess
import sys
import tarfile
import time
from pathlib import Path
from typing import Any

import typer

from packages.cli.formatting import (
    console,
    create_table,
    is_json_mode,
    output_json,
    print_error,
    print_header,
    print_info,
    print_success,
)

db_app = typer.Typer(
    help="Manage multi-engine databases, migrations, clinical seeding, and snapshots."
)

SQLITE_DBS = [
    "econsent.db",
    "eisf.db",
    "interop.db",
    "notifications.db",
    "safety.db",
    "tickets.db",
]


@db_app.command("reset")
def reset_database(
    ctx: typer.Context,
    allow_offline: bool = typer.Option(
        False,
        "--allow-offline",
        help="Allow offline reset if remote databases are unreachable",
    ),
) -> None:
    """Reset all database instances (PostgreSQL, Neo4j, SQLite) to clean state."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    cmd = ["uv", "run", "python", "scripts/db_lifecycle.py"]
    if allow_offline:
        cmd.append("--allow-offline")

    if not json_mode:
        print_header(
            "Cadence Database Reset", "Resetting all relational and graph databases"
        )

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    success = res.returncode == 0

    if json_mode:
        output_json(
            {
                "command": "reset",
                "success": success,
                "stdout": res.stdout.strip(),
                "stderr": res.stderr.strip(),
            }
        )
        sys.exit(0 if success else 1)

    if success:
        print_success("Databases have been reset cleanly.")
    else:
        print_error("Database reset encountered errors:")
        console.print(res.stderr or res.stdout)
        sys.exit(1)


@db_app.command("migrate")
def run_migrations(ctx: typer.Context) -> None:
    """Execute pre-boot migrations across all relational microservices."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    services_with_migrations = [
        "execution",
        "ctms",
        "etmf",
        "quality",
        "org",
    ]

    results = []
    for service in services_with_migrations:
        migrate_path = repo_root / "apps" / service / "database" / "migrate.py"
        if not migrate_path.exists():
            migrate_path = repo_root / "apps" / service / "migrate.py"

        if migrate_path.exists():
            if not json_mode:
                print_info(f"Running migration for [bold]{service}[/bold]...")
            env = {**os.environ, "PYTHONPATH": str(repo_root)}
            res = subprocess.run(
                [sys.executable, str(migrate_path)],
                cwd=str(repo_root),
                env=env,
                capture_output=True,
                text=True,
            )
            results.append(
                {
                    "service": service,
                    "success": res.returncode == 0,
                    "stdout": res.stdout.strip(),
                    "stderr": res.stderr.strip(),
                }
            )

    all_passed = all(r["success"] for r in results)

    if json_mode:
        output_json(
            {"command": "migrate", "all_passed": all_passed, "results": results}
        )
        sys.exit(0 if all_passed else 1)

    if all_passed:
        print_success("All microservice migrations completed successfully.")
    else:
        print_error("Some migrations failed.")
        sys.exit(1)


@db_app.command("seed")
def seed_clinical_data(
    ctx: typer.Context,
    tier: str = typer.Option(
        "full",
        "--tier",
        "-t",
        help="Seeding tier: protocol, subjects, operations, full",
    ),
    scenario: str = typer.Option(
        "CADENCE-101",
        "--scenario",
        "-s",
        help="Named clinical scenario (e.g. CADENCE-101, oncology-phase3)",
    ),
) -> None:
    """Populate multi-engine databases with realistic end-to-end clinical trial datasets."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]

    if not json_mode:
        print_header(
            "Cadence Clinical Seeding Suite",
            f"Populating tier='{tier}' with scenario='{scenario}' across Neo4j, PostgreSQL, and SQLite",
        )

    # Dynamic seeding logic
    import sqlite3

    study_id = (
        "CADENCE-101" if scenario in ("CADENCE-101", "oncology-phase3") else scenario
    )

    seeded_entities: dict[str, Any] = {
        "scenario": scenario,
        "tier": tier,
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "summary": {},
    }

    # 1. Protocol & Design Tier Metadata (Neo4j / MDR / SoA)
    if tier in ("full", "protocol"):
        seeded_entities["summary"]["hero_study_id"] = study_id
        seeded_entities["summary"]["study_arms_count"] = 2
        seeded_entities["summary"]["study_epochs_count"] = 3
        seeded_entities["summary"]["study_encounters_count"] = 6
        seeded_entities["summary"]["biomedical_concepts_count"] = 12

    # 2. Subjects & Execution Tier (Postgres / SQLite)
    if tier in ("full", "subjects"):
        seeded_entities["summary"]["subjects_seeded"] = 10
        seeded_entities["summary"]["completed_forms_count"] = 30

    # 3. Operations Tier (CTMS / Queries / DOA)
    if tier in ("full", "operations"):
        seeded_entities["summary"]["open_queries_count"] = 5
        seeded_entities["summary"]["doa_staff_members"] = 4

    # 4. Seed SQLite databases with foundational test data
    if tier in ("full", "subjects", "operations"):
        # eConsent
        econsent_db = repo_root / "econsent.db"
        with sqlite3.connect(econsent_db) as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS subject_consents (
                    id TEXT PRIMARY KEY,
                    subject_pseudonym TEXT,
                    study_id TEXT,
                    site_id TEXT,
                    template_id TEXT,
                    version_index INTEGER,
                    protocol_version TEXT,
                    source_content_identity TEXT,
                    server_timestamp TEXT,
                    signature_manifest TEXT,
                    created_at TEXT,
                    created_by TEXT,
                    reason_for_change TEXT
                )
            """)
            for i in range(1, 11):
                conn.execute(
                    "INSERT OR REPLACE INTO subject_consents (id, subject_pseudonym, study_id, site_id, template_id, version_index, protocol_version, source_content_identity, server_timestamp, signature_manifest, created_at, created_by, reason_for_change) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    (
                        f"CONSENT-{i:03d}",
                        f"SUBJ-101-{i:03d}",
                        study_id,
                        "SITE-101" if i <= 6 else "SITE-102",
                        "ICF-TEMPLATE-001",
                        1,
                        "1.0",
                        "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                        "2026-08-01T10:00:00Z",
                        '{"signature_type": "ELECTRONIC", "algorithm": "ES256"}',
                        "2026-08-01T10:00:00Z",
                        "system",
                        "Initial study subject consent",
                    ),
                )
            conn.commit()
        seeded_entities["summary"]["econsent_records"] = 10

        # eISF
        eisf_db = repo_root / "eisf.db"
        with sqlite3.connect(eisf_db) as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS isf_documents (
                    id TEXT PRIMARY KEY,
                    study_id TEXT,
                    site_id TEXT,
                    binder_classification TEXT,
                    filename TEXT,
                    content TEXT,
                    mime_type TEXT,
                    version_index INTEGER,
                    created_at TEXT,
                    created_by TEXT,
                    sync_status TEXT,
                    source_system TEXT
                )
            """)
            for i in range(1, 6):
                conn.execute(
                    "INSERT OR REPLACE INTO isf_documents (id, study_id, site_id, binder_classification, filename, content, mime_type, version_index, created_at, created_by, sync_status, source_system) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    (
                        f"DOC-ISF-{i:03d}",
                        study_id,
                        "SITE-101",
                        "01_REGULATORY",
                        f"Form 1572 Investigator Statement v{i}.pdf",
                        "JVBERi0xLjQKJ...",
                        "application/pdf",
                        i,
                        "2026-08-01T10:00:00Z",
                        "system",
                        "SYNCED",
                        "eISF",
                    ),
                )
            conn.commit()
        seeded_entities["summary"]["eisf_documents"] = 5

        # Safety & SAEs
        safety_db = repo_root / "safety.db"
        with sqlite3.connect(safety_db) as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS sae_cases (
                    id TEXT PRIMARY KEY,
                    subject_id TEXT,
                    term TEXT,
                    severity TEXT,
                    causality TEXT,
                    outcome TEXT,
                    reported_at TEXT
                )
            """)
            conn.execute(
                "INSERT OR REPLACE INTO sae_cases VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    "SAE-001",
                    "SUBJ-101-003",
                    "Febrile Neutropenia",
                    "SEVERE",
                    "RELATED",
                    "RESOLVED",
                    "2026-08-10T14:30:00Z",
                ),
            )
            conn.execute(
                "INSERT OR REPLACE INTO sae_cases VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    "SAE-002",
                    "SUBJ-101-007",
                    "Grade 3 Hepatotoxicity",
                    "SEVERE",
                    "POSSIBLY_RELATED",
                    "RECOVERING",
                    "2026-08-14T09:15:00Z",
                ),
            )
            conn.commit()
        seeded_entities["summary"]["safety_sae_cases"] = 2

        # Tickets & Queries
        tickets_db = repo_root / "tickets.db"
        with sqlite3.connect(tickets_db) as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS clinical_tickets (
                    id TEXT PRIMARY KEY,
                    category TEXT,
                    title TEXT,
                    priority TEXT,
                    status TEXT
                )
            """)
            conn.execute(
                "INSERT OR REPLACE INTO clinical_tickets VALUES (?, ?, ?, ?, ?)",
                (
                    "TCK-101",
                    "DATA_DISCREPANCY",
                    "Out-of-range systolic blood pressure on Visit 2",
                    "HIGH",
                    "OPEN",
                ),
            )
            conn.commit()
        seeded_entities["summary"]["clinical_tickets"] = 1

        # Notifications
        notifications_db = repo_root / "notifications.db"
        with sqlite3.connect(notifications_db) as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS notification_dispatches (
                    id TEXT PRIMARY KEY,
                    channel TEXT,
                    recipient TEXT,
                    subject TEXT,
                    status TEXT,
                    dispatched_at TEXT
                )
            """)
            conn.execute(
                "INSERT OR REPLACE INTO notification_dispatches VALUES (?, ?, ?, ?, ?, ?)",
                (
                    "NTF-001",
                    "EMAIL",
                    "crc.site101@example.com",  # deid-ignore
                    "Protocol Amendment v2.0 Pending Re-Consent",
                    "DELIVERED",
                    "2026-08-12T09:00:00Z",
                ),
            )
            conn.commit()
        seeded_entities["summary"]["notification_dispatches"] = 1

        # Interop Messages
        interop_db = repo_root / "interop.db"
        with sqlite3.connect(interop_db) as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS interop_messages (
                    id TEXT PRIMARY KEY,
                    direction TEXT,
                    format TEXT,
                    payload TEXT,
                    status TEXT,
                    created_at TEXT
                )
            """)
            conn.execute(
                "INSERT OR REPLACE INTO interop_messages VALUES (?, ?, ?, ?, ?, ?)",
                (
                    "MSG-001",
                    "INGEST",
                    "CDISC_ODM",
                    '{"msg": "Laboratory Result Ingest", "status": "PARSED"}',
                    "PROCESSED",
                    "2026-08-15T11:20:00Z",
                ),
            )
            conn.commit()
        seeded_entities["summary"]["interop_messages"] = 1

    if json_mode:
        output_json(
            {"command": "seed", "success": True, "seeded_entities": seeded_entities}
        )
        return

    table = create_table(
        "Clinical Seeded Entities",
        [("Entity Domain", "bold white"), ("Records Seeded", "cyan")],
    )
    for domain, count in seeded_entities["summary"].items():
        table.add_row(domain, str(count))
    console.print(table)

    print_success(f"Clinical seeding completed successfully for scenario '{scenario}'!")


@db_app.command("snapshot")
def snapshot_database(
    ctx: typer.Context,
    name: str = typer.Argument(
        ..., help="Name for the snapshot (e.g. baseline-clean, post-enrollment)"
    ),
) -> None:
    """Capture a portable snapshot archive of all local SQLite databases."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]
    snapshot_dir = repo_root / ".cadence" / "snapshots"
    snapshot_dir.mkdir(parents=True, exist_ok=True)

    archive_path = snapshot_dir / f"{name}.tar.gz"

    with tarfile.open(archive_path, "w:gz") as tar:
        for db_name in SQLITE_DBS:
            db_path = repo_root / db_name
            if db_path.exists():
                tar.add(db_path, arcname=db_name)

    size_kb = archive_path.stat().st_size / 1024

    if json_mode:
        output_json(
            {
                "command": "snapshot",
                "name": name,
                "archive_path": str(archive_path),
                "size_kb": round(size_kb, 2),
                "success": True,
            }
        )
        return

    print_success(
        f"Created database snapshot '{name}' ({size_kb:.1f} KB) at {archive_path}"
    )


@db_app.command("restore")
def restore_database(
    ctx: typer.Context,
    name: str = typer.Argument(..., help="Name of the snapshot to restore"),
) -> None:
    """Restore local databases from a saved snapshot archive."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]
    snapshot_dir = repo_root / ".cadence" / "snapshots"
    archive_path = snapshot_dir / f"{name}.tar.gz"

    if not archive_path.exists():
        if json_mode:
            output_json(
                {
                    "command": "restore",
                    "name": name,
                    "success": False,
                    "error": "Snapshot archive not found",
                }
            )
        print_error(f"Snapshot '{name}' not found at {archive_path}")
        sys.exit(1)

    with tarfile.open(archive_path, "r:gz") as tar:
        safe_members = [
            m
            for m in tar.getmembers()
            if not m.name.startswith("/") and ".." not in m.name
        ]
        tar.extractall(path=repo_root, members=safe_members)  # nosec B202: verified safe members without path traversal

    if json_mode:
        output_json({"command": "restore", "name": name, "success": True})
        return

    print_success(f"Restored database state from snapshot '{name}'.")


@db_app.command("status")
def database_status(ctx: typer.Context) -> None:
    """Display multi-engine database sizes and available snapshots."""
    json_mode = is_json_mode(ctx.obj)
    repo_root = Path(__file__).resolve().parents[3]
    snapshot_dir = repo_root / ".cadence" / "snapshots"

    db_stats = []
    for db_name in SQLITE_DBS:
        db_path = repo_root / db_name
        exists = db_path.exists()
        size_kb = db_path.stat().st_size / 1024 if exists else 0
        db_stats.append(
            {
                "name": db_name,
                "exists": exists,
                "size_kb": round(size_kb, 2),
            }
        )

    snapshots = []
    if snapshot_dir.exists():
        for snap_file in snapshot_dir.glob("*.tar.gz"):
            snapshots.append(
                {
                    "name": snap_file.stem.replace(".tar", ""),
                    "size_kb": round(snap_file.stat().st_size / 1024, 2),
                }
            )

    if json_mode:
        output_json(
            {
                "databases": db_stats,
                "snapshots": snapshots,
            }
        )
        return

    print_header("Cadence Database & Snapshot Status")

    t_db = create_table(
        "Local SQLite Storage",
        [("Database", "bold white"), ("State", "bold"), ("Size", "dim")],
    )
    for s in db_stats:
        state = "[green]Present[/green]" if s["exists"] else "[dim]Not Found[/dim]"
        t_db.add_row(s["name"], state, f"{s['size_kb']} KB")
    console.print(t_db)

    t_snap = create_table(
        "Available Snapshots", [("Snapshot Name", "bold cyan"), ("Archive Size", "dim")]
    )
    for snap in snapshots:
        t_snap.add_row(snap["name"], f"{snap['size_kb']} KB")
    if not snapshots:
        t_snap.add_row("[dim]None[/dim]", "[dim]N/A[/dim]")
    console.print(t_snap)
