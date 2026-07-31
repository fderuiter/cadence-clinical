"""
eISF Database Migration Module.

This module provides a production-grade schema migration entry point for the
electronic Investigator Site File (eISF) service. It applies schema upgrades idempotently.
"""

import argparse
import asyncio
import os
import sys

from sqlalchemy import inspect, text
from sqlalchemy.ext.asyncio import create_async_engine

from apps.eisf.models import Base


async def upgrade_existing_tables(conn, dialect_name: str) -> None:
    """
    Upgrades pre-existing tables with new columns if they do not exist.
    """

    def get_table_columns(sync_conn, table_name: str):
        insp = inspect(sync_conn)
        if not insp.has_table(table_name):
            return []
        return [col["name"] for col in insp.get_columns(table_name)]

    has_isf_docs = await conn.run_sync(
        lambda sc: inspect(sc).has_table("isf_documents")
    )
    if has_isf_docs:
        cols = await conn.run_sync(lambda sc: get_table_columns(sc, "isf_documents"))

        # Idempotently add missing columns
        if "issue_date" not in cols:
            print("Adding missing column issue_date to isf_documents table...")
            await conn.execute(
                text("ALTER TABLE isf_documents ADD COLUMN issue_date DATE;")
            )
            try:
                await conn.execute(
                    text(
                        "CREATE INDEX IF NOT EXISTS ix_isf_documents_issue_date ON isf_documents (issue_date);"
                    )
                )
            except Exception:
                pass

        if "expiration_date" not in cols:
            print("Adding missing column expiration_date to isf_documents table...")
            await conn.execute(
                text("ALTER TABLE isf_documents ADD COLUMN expiration_date DATE;")
            )
            try:
                await conn.execute(
                    text(
                        "CREATE INDEX IF NOT EXISTS ix_isf_documents_expiration_date ON isf_documents (expiration_date);"
                    )
                )
            except Exception:
                pass

        if "document_owner_id" not in cols:
            print("Adding missing column document_owner_id to isf_documents table...")
            await conn.execute(
                text(
                    "ALTER TABLE isf_documents ADD COLUMN document_owner_id VARCHAR(255);"
                )
            )
            try:
                await conn.execute(
                    text(
                        "CREATE INDEX IF NOT EXISTS ix_isf_documents_document_owner_id ON isf_documents (document_owner_id);"
                    )
                )
            except Exception:
                pass


async def run_migrations(database_url: str) -> None:
    """
    Execute asynchronous pre-boot eISF schema migrations.
    """
    print(f"Starting pre-boot schema migration for eISF: {database_url}...")
    engine = create_async_engine(database_url, echo=False)
    dialect_name = engine.dialect.name

    try:
        async with engine.begin() as conn:
            # First, check and run base create_all for clean installs
            await conn.run_sync(Base.metadata.create_all)

            # Create SQLModel tables (EISFSectionTaxonomy and EISFDocumentRecord)
            from sqlmodel import SQLModel

            await conn.run_sync(SQLModel.metadata.create_all)

            # Apply migrations for existing schema instances
            await upgrade_existing_tables(conn, dialect_name)

            # Seed standard eISF section taxonomies if empty
            from sqlalchemy import select

            from apps.eisf.models import STANDARD_EISF_SECTIONS, EISFSectionTaxonomy

            res = await conn.execute(select(EISFSectionTaxonomy))
            existing_sections = res.scalars().all()
            if not existing_sections:
                print("Seeding standard eISF section taxonomies...")
                for sec in STANDARD_EISF_SECTIONS:
                    await conn.execute(
                        text(
                            "INSERT INTO eisf_section_taxonomies (section_code, section_number, title, description, is_mandatory) "
                            "VALUES (:section_code, :section_number, :title, :description, :is_mandatory);"
                        ),
                        sec,
                    )

        print("eISF Schema migration completed successfully.")
    except Exception as e:
        print(f"eISF Schema migration failed: {e}")
        sys.exit(1)
    finally:
        await engine.dispose()


def main() -> None:
    """CLI script entrypoint."""
    parser = argparse.ArgumentParser(
        description="eISF Database Schema Migration Runner"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv("EISF_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        help="Database URL for migration",
    )
    args = parser.parse_args()
    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
