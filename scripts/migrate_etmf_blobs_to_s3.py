#!/usr/bin/env python3
"""Data Migration Script: Migrate In-Database Binary Content to Object Storage.

Iterates historical eTMF (tmf_documents) and eISF (isf_documents) rows where
object_key IS NULL, decodes the legacy _content payload, uploads to object
storage via StoragePort, and updates object_key while clearing _content.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import argparse
import asyncio
import base64
import os
import sys
from pathlib import Path

# Add repository root to sys.path
REPO_ROOT = str(Path(__file__).resolve().parent.parent)
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.eisf.infrastructure.models import ISFDocument
from apps.etmf.infrastructure.models import TMFDocument
from packages.storage.adapters.minio_adapter import MinioStorageAdapter
from packages.storage.adapters.s3_adapter import S3StorageAdapter
from packages.storage.ports.storage_port import StoragePort


def get_default_db_url() -> str:
    """Resolve the database URL from environment or fallback to default SQLite."""
    url = os.getenv("ETMF_DATABASE_URL") or os.getenv("DATABASE_URL")
    if not url:
        return "sqlite+aiosqlite:///etmf.db"
    if url.startswith("postgresql://"):
        return url.replace("postgresql://", "postgresql+asyncpg://", 1)
    if url.startswith("sqlite:///"):
        return url.replace("sqlite:///", "sqlite+aiosqlite:///", 1)
    return url


def get_storage_port() -> StoragePort:
    """Instantiate the appropriate StoragePort adapter based on environment."""
    endpoint = os.getenv("MINIO_ENDPOINT_URL") or os.getenv("STORAGE_ENDPOINT_URL")
    if endpoint and ("localhost" in endpoint or "minio" in endpoint):
        return MinioStorageAdapter()
    return S3StorageAdapter()


async def migrate_documents(
    db_url: str,
    batch_size: int = 50,
    study_id_filter: str | None = None,
    dry_run: bool = False,
    storage: StoragePort | None = None,
) -> dict[str, int]:
    """Execute idempotent migration of legacy database blobs to object storage.

    @req:PRD-DOC-001
    @req:PRD-SYS-001
    """
    engine = create_async_engine(db_url, echo=False)
    session_maker = async_sessionmaker(
        engine, expire_on_commit=False, class_=AsyncSession
    )
    storage_adapter = storage or get_storage_port()

    stats = {
        "etmf_migrated": 0,
        "etmf_skipped": 0,
        "eisf_migrated": 0,
        "eisf_skipped": 0,
        "errors": 0,
    }

    print("=" * 70)
    print("Cadence Clinical — eTMF/eISF Object Storage Data Migration")
    print("=" * 70)
    print(f"Database URL : {db_url}")
    print(
        f"Dry Run Mode : {'ENABLED (read-only)' if dry_run else 'DISABLED (will write to storage and DB)'}"
    )
    print(f"Batch Size   : {batch_size}")
    if study_id_filter:
        print(f"Study Filter : {study_id_filter}")
    print("-" * 70)

    async with session_maker() as session:
        # 1. Migrate eTMF Documents
        print("[1/2] Scanning tmf_documents for unmigrated blobs...")
        stmt = select(TMFDocument).where(
            TMFDocument.object_key.is_(None),
            TMFDocument._content.is_not(None),
        )
        if study_id_filter:
            stmt = stmt.where(TMFDocument.study_id == study_id_filter)

        res = await session.execute(stmt)
        etmf_docs = res.scalars().all()
        print(f"Found {len(etmf_docs)} unmigrated eTMF documents.")

        for i, doc in enumerate(etmf_docs, start=1):
            if not doc._content:
                stats["etmf_skipped"] += 1
                continue

            # Decode content
            mime_lower = (doc.mime_type or "").lower().strip()
            is_binary = (
                "pdf" in mime_lower
                or "wordprocessingml" in mime_lower
                or "docx" in mime_lower
                or mime_lower == "application/octet-stream"
            )

            raw_bytes: bytes
            if is_binary:
                try:
                    raw_bytes = base64.b64decode(doc._content)
                except Exception:
                    raw_bytes = doc._content.encode("utf-8", errors="surrogateescape")
            else:
                raw_bytes = doc._content.encode("utf-8", errors="surrogateescape")

            object_key = f"etmf/{doc.study_id}/{doc.id}/{doc.filename}"

            if dry_run:
                print(
                    f"  [DRY-RUN] Would upload eTMF doc {doc.id} ({doc.filename}) -> {object_key} ({len(raw_bytes)} bytes)"
                )
                stats["etmf_migrated"] += 1
            else:
                try:
                    await storage_adapter.put_object(
                        key=object_key,
                        data=raw_bytes,
                        content_type=doc.mime_type,
                        metadata={
                            "study_id": doc.study_id,
                            "doc_id": doc.id,
                            "filename": doc.filename,
                            "migrated_by": "scripts/migrate_etmf_blobs_to_s3.py",
                        },
                    )
                    doc.object_key = object_key
                    doc._content = None
                    stats["etmf_migrated"] += 1

                    if i % batch_size == 0:
                        await session.commit()
                        print(
                            f"  [PROGRESS] Migrated and committed {i}/{len(etmf_docs)} eTMF documents."
                        )
                except Exception as e:
                    print(f"  [ERROR] Failed to migrate eTMF doc {doc.id}: {e}")
                    stats["errors"] += 1

        if not dry_run and stats["etmf_migrated"] > 0:
            await session.commit()

        # 2. Migrate eISF Documents (if table exists)
        print("\n[2/2] Scanning isf_documents for unmigrated blobs...")
        try:
            stmt_isf = select(ISFDocument).where(
                ISFDocument.object_key.is_(None),
                ISFDocument.content.is_not(None),
            )
            if study_id_filter:
                stmt_isf = stmt_isf.where(ISFDocument.study_id == study_id_filter)

            res_isf = await session.execute(stmt_isf)
            isf_docs = res_isf.scalars().all()
            print(f"Found {len(isf_docs)} unmigrated eISF documents.")

            for j, isf_doc in enumerate(isf_docs, start=1):
                if not isf_doc.content:
                    stats["eisf_skipped"] += 1
                    continue

                raw_bytes = isf_doc.content.encode("utf-8", errors="surrogateescape")
                object_key = f"eisf/{isf_doc.study_id}/{isf_doc.site_id}/{isf_doc.id}/{isf_doc.filename}"

                if dry_run:
                    print(
                        f"  [DRY-RUN] Would upload eISF doc {isf_doc.id} ({isf_doc.filename}) -> {object_key}"
                    )
                    stats["eisf_migrated"] += 1
                else:
                    try:
                        await storage_adapter.put_object(
                            key=object_key,
                            data=raw_bytes,
                            content_type=isf_doc.mime_type,
                            metadata={
                                "study_id": isf_doc.study_id,
                                "site_id": isf_doc.site_id,
                                "doc_id": isf_doc.id,
                                "migrated_by": "scripts/migrate_etmf_blobs_to_s3.py",
                            },
                        )
                        isf_doc.object_key = object_key
                        isf_doc.content = None
                        stats["eisf_migrated"] += 1

                        if j % batch_size == 0:
                            await session.commit()
                            print(
                                f"  [PROGRESS] Migrated and committed {j}/{len(isf_docs)} eISF documents."
                            )
                    except Exception as e:
                        print(f"  [ERROR] Failed to migrate eISF doc {isf_doc.id}: {e}")
                        stats["errors"] += 1

            if not dry_run and stats["eisf_migrated"] > 0:
                await session.commit()
        except Exception as e:
            print(f"Note: eISF table scan skipped ({e})")

    await engine.dispose()
    print("-" * 70)
    print("Migration Summary:")
    print(f"  eTMF Documents Migrated: {stats['etmf_migrated']}")
    print(f"  eTMF Documents Skipped : {stats['etmf_skipped']}")
    print(f"  eISF Documents Migrated: {stats['eisf_migrated']}")
    print(f"  eISF Documents Skipped : {stats['eisf_skipped']}")
    print(f"  Errors Encountered     : {stats['errors']}")
    print("=" * 70)
    return stats


def main():
    parser = argparse.ArgumentParser(
        description="Migrate historical eTMF and eISF database blobs to S3/MinIO object storage."
    )
    parser.add_argument(
        "--db-url",
        default=get_default_db_url(),
        help="Database connection URL.",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=50,
        help="Transaction batch size.",
    )
    parser.add_argument(
        "--study-id",
        default=None,
        help="Optional study ID filter.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Run without modifying database or object storage.",
    )
    args = parser.parse_args()

    asyncio.run(
        migrate_documents(
            db_url=args.db_url,
            batch_size=args.batch_size,
            study_id_filter=args.study_id,
            dry_run=args.dry_run,
        )
    )


if __name__ == "__main__":
    main()
