"""Local SQLite/PostgreSQL cache for CDISC terminology and codelists with auto-refresh.

Provides fast async local persistence and TTL-based cache invalidation
for CDASH, SDTM, and Controlled Terminology codelists.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import json
import logging
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Optional

from sqlmodel import Field, Session, SQLModel, create_engine, select

from cdisc.cdisc_library_client import CodelistDefinition, CodelistTerm

logger = logging.getLogger(__name__)

DEFAULT_CACHE_DB_PATH = (
    Path(__file__).resolve().parents[3]
    / "docs"
    / "CDISC"
    / "cdisc_terminology_cache.db"
)


class CdiscCodelistCacheEntry(SQLModel, table=True):
    """SQLModel for CDISC terminology codelist cache entry."""

    __tablename__ = "cdisc_codelist_cache"

    codelist_code: str = Field(primary_key=True)
    package: str = Field(index=True)
    name: str
    extensible: bool = Field(default=False)
    terms_json: str
    updated_at: str
    ttl_seconds: int = Field(default=86400)

    # GxP audit fields
    created_at: str = Field(
        default_factory=lambda: datetime.now(timezone.utc).isoformat()
    )
    created_by: str = Field(default="system")
    reason_for_change: Optional[str] = Field(default=None)
    version_index: int = Field(default=1)


class CdiscTerminologyCache:
    """Async local SQLite/PostgreSQL cache for CDISC terminology.

    Requirements: PRD-SYS-001
    """

    def __init__(
        self,
        db_path: Optional[Any] = None,
        default_ttl_seconds: int = 86400,
    ) -> None:
        """Initialize terminology cache.

        Args:
            db_path: Path or connection string for SQLite/PostgreSQL database.
            default_ttl_seconds: Cache TTL in seconds (default 24 hours).
        """
        self.db_path = db_path or DEFAULT_CACHE_DB_PATH
        self.default_ttl_seconds = default_ttl_seconds
        self._init_db()

    def _init_db(self) -> None:
        """Initialize table schema if not existing with self-healing migration."""
        db_str = str(self.db_path)
        if db_str.startswith("postgresql://") or db_str.startswith("postgres://"):
            self.engine = create_engine(db_str)
            self.is_postgres = True
        else:
            self.is_postgres = False
            if db_str == ":memory:":
                from sqlalchemy.pool import StaticPool

                self.engine = create_engine(
                    "sqlite://",
                    connect_args={"check_same_thread": False},
                    poolclass=StaticPool,
                )
            else:
                self.db_path = Path(self.db_path)
                self.db_path.parent.mkdir(parents=True, exist_ok=True)
                self.engine = create_engine(
                    f"sqlite:///{self.db_path}",
                    connect_args={"check_same_thread": False},
                )

        # Process-safe table creation retries to prevent xdist race conditions
        from sqlalchemy.exc import OperationalError

        for i in range(5):
            try:
                SQLModel.metadata.create_all(self.engine)
                break
            except OperationalError as e:
                if "locked" in str(e).lower() and i < 4:
                    time.sleep(0.1)
                else:
                    raise e

        # Self-healing schema migration: Alter table to add GxP columns if they are missing
        # This prevents crashes when reading existing legacy cdisc_terminology_cache.db
        if not self.is_postgres:
            with self.engine.begin() as conn:
                from sqlalchemy import text

                try:
                    res = conn.execute(
                        text("PRAGMA table_info(cdisc_codelist_cache)")
                    ).fetchall()
                    existing_columns = {row[1] for row in res}
                    if existing_columns:
                        # Table exists, check for missing GxP columns
                        if "created_at" not in existing_columns:
                            conn.execute(
                                text(
                                    "ALTER TABLE cdisc_codelist_cache ADD COLUMN created_at TEXT"
                                )
                            )
                        if "created_by" not in existing_columns:
                            conn.execute(
                                text(
                                    "ALTER TABLE cdisc_codelist_cache ADD COLUMN created_by TEXT DEFAULT 'system'"
                                )
                            )
                        if "reason_for_change" not in existing_columns:
                            conn.execute(
                                text(
                                    "ALTER TABLE cdisc_codelist_cache ADD COLUMN reason_for_change TEXT"
                                )
                            )
                        if "version_index" not in existing_columns:
                            conn.execute(
                                text(
                                    "ALTER TABLE cdisc_codelist_cache ADD COLUMN version_index INTEGER DEFAULT 1"
                                )
                            )
                except Exception as e:
                    logger.warning("Self-healing schema migration warning: %s", str(e))

    async def get_codelist(
        self, package: str, codelist_code: str
    ) -> Optional[CodelistDefinition]:
        """Retrieve cached codelist definition if present and unexpired.

        Args:
            package: CT package name.
            codelist_code: Codelist concept code or submission value.

        Returns:
            CodelistDefinition if found and fresh, otherwise None.
        """
        with Session(self.engine) as session:
            statement = select(CdiscCodelistCacheEntry).where(
                CdiscCodelistCacheEntry.codelist_code == codelist_code,
                CdiscCodelistCacheEntry.package == package,
            )
            row = session.exec(statement).first()
            if not row:
                return None

            updated_at = datetime.fromisoformat(row.updated_at)
            now = datetime.now(timezone.utc)

            # Ensure updated_at has timezone
            if updated_at.tzinfo is None:
                updated_at = updated_at.replace(tzinfo=timezone.utc)

            elapsed_seconds = (now - updated_at).total_seconds()
            if elapsed_seconds > row.ttl_seconds:
                logger.info(
                    "Cached codelist %s expired (%ds > %ds TTL)",
                    codelist_code,
                    elapsed_seconds,
                    row.ttl_seconds,
                )
                return None

            terms_data = json.loads(row.terms_json)
            terms = [CodelistTerm(**t) for t in terms_data]

            return CodelistDefinition(
                codelist_code=codelist_code,
                name=row.name,
                extensible=row.extensible,
                terms=terms,
            )

    async def save_codelist(
        self,
        package: str,
        codelist: CodelistDefinition,
        ttl_seconds: Optional[int] = None,
        created_by: str = "system",
        reason_for_change: Optional[str] = None,
    ) -> None:
        """Save codelist definition to local SQLite/PostgreSQL cache.

        Args:
            package: CT package name.
            codelist: CodelistDefinition instance.
            ttl_seconds: Optional TTL override in seconds.
            created_by: Authenticated user identity.
            reason_for_change: GxP change rationale.
        """
        ttl = ttl_seconds if ttl_seconds is not None else self.default_ttl_seconds
        now_str = datetime.now(timezone.utc).isoformat()
        terms_json = json.dumps(
            [t.model_dump() for t in codelist.terms], ensure_ascii=False
        )

        with Session(self.engine) as session:
            statement = select(CdiscCodelistCacheEntry).where(
                CdiscCodelistCacheEntry.codelist_code == codelist.codelist_code
            )
            existing = session.exec(statement).first()

            if existing:
                version_index = existing.version_index + 1
                created_at_str = existing.created_at
            else:
                version_index = 1
                created_at_str = now_str

            entry = CdiscCodelistCacheEntry(
                codelist_code=codelist.codelist_code,
                package=package,
                name=codelist.name,
                extensible=codelist.extensible,
                terms_json=terms_json,
                updated_at=now_str,
                ttl_seconds=ttl,
                created_at=created_at_str,
                created_by=created_by,
                reason_for_change=reason_for_change,
                version_index=version_index,
            )
            session.merge(entry)
            session.commit()

    async def is_expired(self, codelist_code: str) -> bool:
        """Check if cached codelist entry is expired.

        Args:
            codelist_code: Concept code.

        Returns:
            True if expired or missing, False if valid.
        """
        with Session(self.engine) as session:
            statement = select(CdiscCodelistCacheEntry).where(
                CdiscCodelistCacheEntry.codelist_code == codelist_code
            )
            row = session.exec(statement).first()
            if not row:
                return True

            updated_at = datetime.fromisoformat(row.updated_at)
            if updated_at.tzinfo is None:
                updated_at = updated_at.replace(tzinfo=timezone.utc)

            now = datetime.now(timezone.utc)
            return (now - updated_at).total_seconds() > row.ttl_seconds

    async def purge_expired(self) -> int:
        """Purge all expired codelist entries from database.

        Returns:
            Number of purged rows.
        """
        with Session(self.engine) as session:
            statement = select(CdiscCodelistCacheEntry)
            rows = session.exec(statement).all()
            now = datetime.now(timezone.utc)
            expired_entries = []

            for entry in rows:
                updated_at = datetime.fromisoformat(entry.updated_at)
                if updated_at.tzinfo is None:
                    updated_at = updated_at.replace(tzinfo=timezone.utc)
                if (now - updated_at).total_seconds() > entry.ttl_seconds:
                    expired_entries.append(entry)

            for entry in expired_entries:
                session.delete(entry)

            if expired_entries:
                session.commit()

            return len(expired_entries)

    async def get_cache_stats(self) -> Dict[str, Any]:
        """Get cache statistics summary.

        Returns:
            Dictionary containing total count, expired count, and database size.
        """
        with Session(self.engine) as session:
            from sqlmodel import func

            statement = select(func.count()).select_from(CdiscCodelistCacheEntry)
            total_count = session.exec(statement).one()

            db_size = 0
            if (
                not self.is_postgres
                and str(self.db_path) != ":memory:"
                and Path(self.db_path).exists()
            ):
                db_size = Path(self.db_path).stat().st_size

            return {
                "total_cached_codelists": total_count,
                "db_path": str(self.db_path),
                "db_size_bytes": db_size,
            }
