"""Local SQLite cache for CDISC terminology and codelists with auto-refresh.

Provides fast async local persistence and TTL-based cache invalidation
for CDASH, SDTM, and Controlled Terminology codelists.
"""

import json
import logging
import sqlite3
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from cdisc.cdisc_library_client import CodelistDefinition, CodelistTerm

logger = logging.getLogger(__name__)

DEFAULT_CACHE_DB_PATH = (
    Path(__file__).resolve().parents[3]
    / "docs"
    / "CDISC"
    / "cdisc_terminology_cache.db"
)


class CdiscTerminologyCache:
    """Async local SQLite cache for CDISC terminology.

    Requirements: PRD-SYS-001
    """

    def __init__(
        self,
        db_path: Path | None = None,
        default_ttl_seconds: int = 86400,
    ) -> None:
        """Initialize terminology cache.

        Args:
            db_path: Path to SQLite database file. Defaults to repo cache path or in-memory if requested.
            default_ttl_seconds: Cache TTL in seconds (default 24 hours).
        """
        self.db_path = db_path or DEFAULT_CACHE_DB_PATH
        self.default_ttl_seconds = default_ttl_seconds
        self._init_db()

    def _get_connection(self) -> sqlite3.Connection:
        """Create a sqlite3 connection."""
        if str(self.db_path) == ":memory:":
            if not hasattr(self, "_memory_conn") or self._memory_conn is None:
                self._memory_conn = sqlite3.connect(":memory:", check_same_thread=False)
            return self._memory_conn

        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        return sqlite3.connect(str(self.db_path), check_same_thread=False)

    def _init_db(self) -> None:
        """Initialize table schema if not existing."""
        conn = self._get_connection()
        try:
            with conn:
                conn.execute(
                    """
                    CREATE TABLE IF NOT EXISTS cdisc_codelist_cache (
                        codelist_code TEXT PRIMARY KEY,
                        package TEXT NOT NULL,
                        name TEXT NOT NULL,
                        extensible INTEGER NOT NULL DEFAULT 0,
                        terms_json TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        ttl_seconds INTEGER NOT NULL DEFAULT 86400
                    )
                    """
                )
                conn.execute(
                    "CREATE INDEX IF NOT EXISTS idx_codelist_package ON cdisc_codelist_cache(package)"
                )
        finally:
            if str(self.db_path) != ":memory:":
                conn.close()

    async def get_codelist(
        self, package: str, codelist_code: str
    ) -> CodelistDefinition | None:
        """Retrieve cached codelist definition if present and unexpired.

        Args:
            package: CT package name.
            codelist_code: Codelist concept code or submission value.

        Returns:
            CodelistDefinition if found and fresh, otherwise None.
        """
        conn = self._get_connection()
        try:
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT name, extensible, terms_json, updated_at, ttl_seconds
                FROM cdisc_codelist_cache
                WHERE codelist_code = ? AND package = ?
                """,
                (codelist_code, package),
            )
            row = cursor.fetchone()
            if not row:
                return None

            name, extensible, terms_json, updated_at_str, ttl_seconds = row
            updated_at = datetime.fromisoformat(updated_at_str)
            now = datetime.now(UTC)

            # Ensure updated_at has timezone
            if updated_at.tzinfo is None:
                updated_at = updated_at.replace(tzinfo=UTC)

            elapsed_seconds = (now - updated_at).total_seconds()
            if elapsed_seconds > ttl_seconds:
                logger.info(
                    "Cached codelist %s expired (%ds > %ds TTL)",
                    codelist_code,
                    elapsed_seconds,
                    ttl_seconds,
                )
                return None

            terms_data = json.loads(terms_json)
            terms = [CodelistTerm(**t) for t in terms_data]

            return CodelistDefinition(
                codelist_code=codelist_code,
                name=name,
                extensible=bool(extensible),
                terms=terms,
            )
        finally:
            if str(self.db_path) != ":memory:":
                conn.close()

    async def save_codelist(
        self,
        package: str,
        codelist: CodelistDefinition,
        ttl_seconds: int | None = None,
    ) -> None:
        """Save codelist definition to local SQLite cache.

        Args:
            package: CT package name.
            codelist: CodelistDefinition instance.
            ttl_seconds: Optional TTL override in seconds.
        """
        ttl = ttl_seconds if ttl_seconds is not None else self.default_ttl_seconds
        now_str = datetime.now(UTC).isoformat()
        terms_json = json.dumps(
            [t.model_dump() for t in codelist.terms], ensure_ascii=False
        )

        conn = self._get_connection()
        try:
            with conn:
                conn.execute(
                    """
                    INSERT OR REPLACE INTO cdisc_codelist_cache
                    (codelist_code, package, name, extensible, terms_json, updated_at, ttl_seconds)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        codelist.codelist_code,
                        package,
                        codelist.name,
                        1 if codelist.extensible else 0,
                        terms_json,
                        now_str,
                        ttl,
                    ),
                )
        finally:
            if str(self.db_path) != ":memory:":
                conn.close()

    async def is_expired(self, codelist_code: str) -> bool:
        """Check if cached codelist entry is expired.

        Args:
            codelist_code: Concept code.

        Returns:
            True if expired or missing, False if valid.
        """
        conn = self._get_connection()
        try:
            cursor = conn.cursor()
            cursor.execute(
                """
                SELECT updated_at, ttl_seconds
                FROM cdisc_codelist_cache
                WHERE codelist_code = ?
                """,
                (codelist_code,),
            )
            row = cursor.fetchone()
            if not row:
                return True

            updated_at_str, ttl_seconds = row
            updated_at = datetime.fromisoformat(updated_at_str)
            if updated_at.tzinfo is None:
                updated_at = updated_at.replace(tzinfo=UTC)

            now = datetime.now(UTC)
            return (now - updated_at).total_seconds() > ttl_seconds
        finally:
            if str(self.db_path) != ":memory:":
                conn.close()

    async def purge_expired(self) -> int:
        """Purge all expired codelist entries from database.

        Returns:
            Number of purged rows.
        """
        conn = self._get_connection()
        try:
            cursor = conn.cursor()
            cursor.execute(
                "SELECT codelist_code, updated_at, ttl_seconds FROM cdisc_codelist_cache"
            )
            rows = cursor.fetchall()
            now = datetime.now(UTC)
            expired_codes = []

            for code, updated_at_str, ttl in rows:
                updated_at = datetime.fromisoformat(updated_at_str)
                if updated_at.tzinfo is None:
                    updated_at = updated_at.replace(tzinfo=UTC)
                if (now - updated_at).total_seconds() > ttl:
                    expired_codes.append(code)

            if expired_codes:
                with conn:
                    conn.executemany(
                        "DELETE FROM cdisc_codelist_cache WHERE codelist_code = ?",
                        [(c,) for c in expired_codes],
                    )

            return len(expired_codes)
        finally:
            if str(self.db_path) != ":memory:":
                conn.close()

    async def get_cache_stats(self) -> dict[str, Any]:
        """Get cache statistics summary.

        Returns:
            Dictionary containing total count, expired count, and database size.
        """
        conn = self._get_connection()
        try:
            cursor = conn.cursor()
            cursor.execute("SELECT COUNT(*) FROM cdisc_codelist_cache")
            total_count = cursor.fetchone()[0]

            db_size = 0
            if str(self.db_path) != ":memory:" and self.db_path.exists():
                db_size = self.db_path.stat().st_size

            return {
                "total_cached_codelists": total_count,
                "db_path": str(self.db_path),
                "db_size_bytes": db_size,
            }
        finally:
            if str(self.db_path) != ":memory:":
                conn.close()
