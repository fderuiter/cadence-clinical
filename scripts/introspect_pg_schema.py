#!/usr/bin/env python3
"""
PostgreSQL Introspection Engine for GxP-Compliant Schema Synchronization.

This script runs out-of-band during CI/CD or development builds to connect to a
PostgreSQL instance, introspect public clinical tables, and generate corresponding
type-safe TypeScript interfaces.

Compliance Guardrails:
1. Environment Isolation: Introspection is strictly blocked on production databases.
2. Compliance Filtering: Generates types ONLY for public/clinical tables. Internal
   and compliance-only tables (e.g., audit logs, seals, outboxes) are excluded.
"""

import argparse
import os
import sys
from pathlib import Path
from urllib.parse import urlparse

# ---------------------------------------------------------------------------
# GxP Compliance Excluded Tables (Internal/Audit Tables)
# ---------------------------------------------------------------------------
EXCLUDED_TABLES = {
    "audit_logs",
    "audit_ledger_seals",
    "integration_outbox",
    "processed_offline_batches",
    "synced_batch_idempotency_keys",
    "tmf_audit_logs",
    "interop_audit_logs",
    "ctms_audit_logs",
    "quality_audit_logs",
    "isf_audit_logs",
    "notification_audit_logs",
    "consent_audit_logs",
    "org_audit_logs",
    "safety_audit_logs",
    "ticket_audit_logs",
    "translation_jobs",
    "pending_predecessor_checks",
}


def check_production_guardrail(db_url: str) -> None:
    """Enforces environment isolation to prevent production database connections."""
    app_env = os.getenv("APP_ENV", "development").lower()
    if app_env == "production":
        raise PermissionError(
            "GxP Guardrail Violation: Schema introspection is strictly prohibited in production environments."
        )

    parsed_url = urlparse(db_url)
    host = (parsed_url.hostname or "").lower()
    if any(keyword in host for keyword in ["prod", "live", "production"]):
        raise PermissionError(
            "GxP Guardrail Violation: Schema introspection cannot target a production or live database instance."
        )


def snake_to_pascal(name: str) -> str:
    """Converts a snake_case table name into a clean PascalCase interface name."""
    special_cases = {
        "sdv_sign_offs": "SDVSignOff",
        "tsdv_configs": "TSDVConfig",
        "meddra_terms": "MedDRATerm",
        "whodrug_records": "WHODrugRecord",
        "lab_test_master": "LabTestMasterLegacy",
    }
    if name in special_cases:
        return special_cases[name]

    parts = name.split("_")
    pascal = "".join(part.capitalize() for part in parts)

    # Singularize
    if pascal.endswith("s") and not pascal.endswith("ss") and not pascal.endswith("is"):
        pascal = pascal[:-3] + "y" if pascal.endswith("ies") else pascal[:-1]
    return pascal


def map_column_type(col_type) -> str:
    """Maps a SQLAlchemy column type to its TypeScript primitive counterpart."""
    from sqlalchemy.sql import sqltypes

    if isinstance(
        col_type,
        (
            sqltypes.Integer,
            sqltypes.SmallInteger,
            sqltypes.BigInteger,
            sqltypes.Numeric,
            sqltypes.Float,
            sqltypes.REAL,
            sqltypes.DECIMAL,
            sqltypes.DOUBLE_PRECISION,
        ),
    ):
        return "number"
    if isinstance(col_type, sqltypes.Boolean):
        return "boolean"
    if isinstance(
        col_type, (sqltypes.DateTime, sqltypes.Date, sqltypes.Time, sqltypes.TIMESTAMP)
    ):
        return "string"  # Dates are serialized as ISO-8601 strings in JSON
    if isinstance(col_type, (sqltypes.JSON, sqltypes.NullType)):
        return "Record<string, any>"
    if isinstance(col_type, sqltypes.Enum):
        if getattr(col_type, "enums", None):
            return " | ".join(f'"{v}"' for v in col_type.enums)
        return "string"
    return "string"


def generate_typescript_schemas(db_url: str, output_path: str) -> bool:
    """Offline schema generator that reads Base.metadata.tables to produce clean TS types."""
    # Ensure production guardrails are checked
    check_production_guardrail(db_url)

    # Set up necessary environment variables for offline model imports
    import os

    os.environ.setdefault("TERMINOLOGY_OFFLINE", "true")
    os.environ.setdefault("ALLOW_MOCK_SIGNATURES", "1")
    os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
    os.environ.setdefault("SIGNING_SECRET", "designer-amendment-secure-key-12345")
    os.environ.setdefault(
        "AUDIT_LOG_SECRET_KEY", "test-gxp-audit-secret-key-placeholder-abc"
    )
    os.environ.setdefault(
        "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
    )

    try:
        from apps.execution.database.models import Base as exec_Base
    except Exception as e:
        import sys

        print(f"Error importing execution Base: {e}", file=sys.stderr)
        return False

    try:
        from apps.ctms.models import Base as ctms_Base
    except Exception as e:
        import sys

        print(f"Error importing CTMS Base: {e}", file=sys.stderr)
        return False

    try:
        from apps.eisf.models import (
            Base as eisf_Base,
        )
        from apps.eisf.models import (
            EISFDocumentRecord,
            EISFSectionTaxonomy,
        )
    except Exception as e:
        import sys

        print(f"Error importing eISF Base: {e}", file=sys.stderr)
        return False

    ts_output = [
        "/* tslint:disable */",
        "/* eslint-disable */",
        "/**",
        " * Auto-generated TypeScript interfaces representing the GxP clinical database models.",
        " * Excludes all internal compliance-only tables and audit logs.",
        " * Generated by scripts/introspect_pg_schema.py.",
        " */",
        "",
    ]

    table_count = 0

    # Consolidate all metadata tables across services
    consolidated_tables = {}
    for table in exec_Base.metadata.tables.values():
        consolidated_tables[table.name] = table
    for table in ctms_Base.metadata.tables.values():
        consolidated_tables[table.name] = table
    for table in eisf_Base.metadata.tables.values():
        consolidated_tables[table.name] = table
    if (
        hasattr(EISFDocumentRecord, "__table__")
        and EISFDocumentRecord.__table__ is not None
    ):
        consolidated_tables[EISFDocumentRecord.__table__.name] = (
            EISFDocumentRecord.__table__
        )
    if (
        hasattr(EISFSectionTaxonomy, "__table__")
        and EISFSectionTaxonomy.__table__ is not None
    ):
        consolidated_tables[EISFSectionTaxonomy.__table__.name] = (
            EISFSectionTaxonomy.__table__
        )
    try:
        from sqlmodel import SQLModel

        for table in SQLModel.metadata.tables.values():
            consolidated_tables[table.name] = table
    except Exception:
        pass

    # Identify test-only tables dynamically to prevent test pollution in parallel suites
    prod_tables = set()
    test_tables = set()
    for base in [exec_Base, ctms_Base, eisf_Base]:
        if hasattr(base, "registry") and base.registry:
            for mapper in base.registry.mappers:
                if mapper.local_table is not None:
                    module_name = getattr(mapper.class_, "__module__", "")
                    parts = module_name.split(".")
                    if any(
                        k in parts
                        for k in ("test", "tests", "mock", "conftest", "fakes")
                    ):
                        test_tables.add(mapper.local_table.name)
                    else:
                        prod_tables.add(mapper.local_table.name)

    test_tables = test_tables - prod_tables

    # Iterate over sorted, consolidated tables
    for table in sorted(consolidated_tables.values(), key=lambda t: t.name):
        table_name = table.name
        name_lower = table_name.lower()
        if (
            name_lower in EXCLUDED_TABLES
            or table_name in test_tables
            or "audit" in name_lower
            or "seal" in name_lower
            or "outbox" in name_lower
            or "idempotency" in name_lower
            or "processed_offline" in name_lower
        ):
            print(f"  [COMPLIANCE EXCLUDED] Skipped table '{table_name}'")
            continue

        interface_name = snake_to_pascal(table_name)
        ts_output.append(f"export interface {interface_name} {{")

        for col in table.columns:
            col_name = col.name
            col_type = col.type
            col_nullable = col.nullable

            ts_type = map_column_type(col_type)
            optional_suffix = "?" if col_nullable else ""
            null_suffix = " | null" if col_nullable else ""

            ts_output.append(f"  {col_name}{optional_suffix}: {ts_type}{null_suffix};")

        ts_output.append("}")
        ts_output.append("")
        table_count += 1
        print(f"  [EXPORTED] Table '{table_name}' -> interface '{interface_name}'")

    if table_count == 0:
        print(
            "Warning: No clinical tables were found in metadata. Output file was not written."
        )
        return False

    # Create target directories
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "w") as f:
        f.write("\n".join(ts_output).rstrip() + "\n")

    print(
        f"\n[SUCCESS] Successfully generated TS schemas offline. TypeScript definitions exported to {output_path}"
    )
    return True


def main() -> None:
    repo_root = Path(__file__).resolve().parent.parent
    default_output = str(repo_root / "apps" / "web" / "src" / "types" / "db_schemas.ts")

    parser = argparse.ArgumentParser(
        description="Out-of-band PostgreSQL DB Introspection Engine"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        help="Target database URL for introspection",
    )
    parser.add_argument(
        "--output-file",
        type=str,
        default=default_output,
        help="Path where generated TypeScript interfaces will be saved",
    )
    args = parser.parse_args()

    success = generate_typescript_schemas(args.db_url, args.output_file)
    if not success:
        sys.exit(1)
    sys.exit(0)


if __name__ == "__main__":
    main()
