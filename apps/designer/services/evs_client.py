"""Asynchronous NCI EVS client with local SQLite/PostgreSQL caching.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import logging
import os
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional

import httpx
from sqlmodel import Field, Session, SQLModel, create_engine, select

logger = logging.getLogger(__name__)


class EVSClientError(Exception):
    """Base exception for NCI EVS client errors."""

    pass


class EVSNotFoundError(EVSClientError):
    """Raised when a concept/code is not found or is invalid (e.g. 404)."""

    pass


class EVSTimeoutError(EVSClientError):
    """Raised when a request to NCI EVS times out."""

    pass


class EVSTransportError(EVSClientError):
    """Raised for connection issues, transport failures, or non-404 HTTP errors."""

    pass


class EvsConceptCacheEntry(SQLModel, table=True):
    """SQLModel for persistent caching of EVS concepts."""

    __tablename__ = "evs_concept_cache"

    code: str = Field(primary_key=True, index=True)
    decode: str
    system: str
    valid: bool = Field(default=True)
    updated_at: str
    ttl_seconds: int = Field(default=86400)

    # GxP audit fields (PRD-SYS-001)
    created_at: str = Field(
        default_factory=lambda: datetime.now(timezone.utc).isoformat()
    )
    created_by: str = Field(default="system")
    reason_for_change: Optional[str] = Field(default=None)
    version_index: int = Field(default=1)


def normalize_concept(
    concept_data: Dict[str, Any], default_system: str
) -> Dict[str, Any]:
    """Normalize EVS concept to the target concept shape: code, decode, system, plus valid."""
    code = concept_data.get("code") or ""
    # "decode" should map to the preferred name, which is "name" or "displayName" in EVS
    decode = concept_data.get("name") or concept_data.get("displayName") or ""
    # "system" maps to terminology, or default to configured terminology/system
    system = concept_data.get("terminology") or default_system
    # "valid" maps to the active status (default to True if not present)
    valid = concept_data.get("active")
    if valid is None:
        valid = True
    else:
        valid = bool(valid)

    return {
        "code": code,
        "decode": decode,
        "system": system,
        "valid": valid,
    }


class NCIEVSClient:
    """Asynchronous, configurable NCI EVS REST client for NCIt/CDISC Controlled Terminology."""

    def __init__(
        self,
        base_url: Optional[str] = None,
        terminology: Optional[str] = None,
        timeout: Optional[httpx.Timeout] = None,
        cache_db_url: Optional[str] = None,
        default_ttl_seconds: int = 86400,
    ) -> None:
        """Initialize the client.

        Args:
            base_url (str, optional): The base URL of the EVS REST API. Defaults to NCI_EVS_BASE_URL env var or a safe default.
            terminology (str, optional): The terminology source. Defaults to NCI_EVS_TERMINOLOGY env var or 'ncit'.
            timeout (httpx.Timeout, optional): Custom timeout configuration. Defaults to configured environment variables or 5.0s.
            cache_db_url (str, optional): Optional database URL for local SQLite/PostgreSQL caching.
            default_ttl_seconds (int): Default cache entry TTL in seconds.
        """
        self.base_url = (
            base_url
            or os.getenv("NCI_EVS_BASE_URL")
            or "https://api-evsrest.nci.nih.gov"
        ).rstrip("/")

        self.terminology = terminology or os.getenv("NCI_EVS_TERMINOLOGY") or "ncit"

        if timeout is not None:
            self.timeout = timeout
        else:
            connect = float(os.getenv("NCI_EVS_TIMEOUT_CONNECT", "5.0"))
            read = float(os.getenv("NCI_EVS_TIMEOUT_READ", "5.0"))
            write = float(os.getenv("NCI_EVS_TIMEOUT_WRITE", "5.0"))
            pool = float(os.getenv("NCI_EVS_TIMEOUT_POOL", "5.0"))
            self.timeout = httpx.Timeout(
                connect=connect,
                read=read,
                write=write,
                pool=pool,
            )

        self.default_ttl_seconds = default_ttl_seconds
        self.cache_db_url = cache_db_url or os.getenv("EVS_CACHE_DB_URL")
        self.engine = None
        if self.cache_db_url:
            if self.cache_db_url.startswith("sqlite"):
                if "///" in self.cache_db_url and not self.cache_db_url.startswith(
                    "sqlite:///:memory:"
                ):
                    path_str = self.cache_db_url.split("///")[1]
                    Path(path_str).parent.mkdir(parents=True, exist_ok=True)
            self.engine = create_engine(self.cache_db_url)

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

            # Self-healing schema migration for SQLite EVS cache
            if not self.cache_db_url.startswith(("postgres", "postgresql")):
                with self.engine.begin() as conn:
                    from sqlalchemy import text

                    try:
                        res = conn.execute(
                            text("PRAGMA table_info(evs_concept_cache)")
                        ).fetchall()
                        existing_columns = {row[1] for row in res}
                        if existing_columns:
                            if "created_at" not in existing_columns:
                                conn.execute(
                                    text(
                                        "ALTER TABLE evs_concept_cache ADD COLUMN created_at TEXT"
                                    )
                                )
                            if "created_by" not in existing_columns:
                                conn.execute(
                                    text(
                                        "ALTER TABLE evs_concept_cache ADD COLUMN created_by TEXT DEFAULT 'system'"
                                    )
                                )
                            if "reason_for_change" not in existing_columns:
                                conn.execute(
                                    text(
                                        "ALTER TABLE evs_concept_cache ADD COLUMN reason_for_change TEXT"
                                    )
                                )
                            if "version_index" not in existing_columns:
                                conn.execute(
                                    text(
                                        "ALTER TABLE evs_concept_cache ADD COLUMN version_index INTEGER DEFAULT 1"
                                    )
                                )
                    except Exception as e:
                        logger.warning(
                            "Self-healing EVS schema migration warning: %s", str(e)
                        )

    async def get_concept(
        self,
        code: str,
        client: Optional[httpx.AsyncClient] = None,
        use_cache: bool = True,
    ) -> Dict[str, Any]:
        """Fetch concept details by concept code.

        Args:
            code (str): The terminology concept code (e.g. C123).
            client (httpx.AsyncClient, optional): Shared client instance.
            use_cache (bool): Try reading and writing from local cache.

        Returns:
            Dict[str, Any]: The normalized concept dict.

        Raises:
            EVSNotFoundError: If the code is invalid or not found.
            EVSTimeoutError: If the request times out.
            EVSTransportError: For transport failures, connection issues, or non-404 HTTP errors.
        """
        # 1. Attempt Cache Read
        if use_cache and self.engine is not None:
            try:
                with Session(self.engine) as session:
                    statement = select(EvsConceptCacheEntry).where(
                        EvsConceptCacheEntry.code == code
                    )
                    cached = session.exec(statement).first()
                    if cached:
                        # Check expiration
                        updated_at = datetime.fromisoformat(cached.updated_at)
                        if updated_at.tzinfo is None:
                            updated_at = updated_at.replace(tzinfo=timezone.utc)
                        elapsed_seconds = (
                            datetime.now(timezone.utc) - updated_at
                        ).total_seconds()
                        if elapsed_seconds <= cached.ttl_seconds:
                            logger.info("Cache hit for EVS concept: %s", code)
                            return {
                                "code": cached.code,
                                "decode": cached.decode,
                                "system": cached.system,
                                "valid": cached.valid,
                            }
                        else:
                            logger.info("Expired EVS cache entry for %s", code)
            except Exception as e:
                logger.warning("Failed to query EVS concept cache: %s", str(e))

        # 2. Fetch from Upstream API
        url = f"{self.base_url}/api/v1/concept/{self.terminology}/{code}"

        try:
            if client is not None:
                response = await client.get(url)
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url)

            if response.status_code == 404:
                raise EVSNotFoundError(f"Concept not found or invalid: {code}")

            response.raise_for_status()

        except httpx.TimeoutException as e:
            raise EVSTimeoutError(f"EVS client request timed out: {str(e)}") from e

        except httpx.HTTPStatusError as e:
            if e.response.status_code == 404:
                raise EVSNotFoundError(f"Concept not found or invalid: {code}") from e
            elif e.response.status_code in (400, 422):
                is_invalid = False
                try:
                    body = e.response.json()
                    detail = str(body).lower()
                    if "not found" in detail or "invalid" in detail:
                        is_invalid = True
                except Exception:
                    pass
                if is_invalid:
                    raise EVSNotFoundError(
                        f"Concept not found or invalid: {code}"
                    ) from e
            raise EVSTransportError(
                f"HTTP error from EVS API: {e.response.status_code} - {e.response.text}"
            ) from e

        except httpx.RequestError as e:
            raise EVSTransportError(
                f"Transport failure contacting EVS API: {str(e)}"
            ) from e

        try:
            data = response.json()
        except Exception as e:
            raise EVSTransportError(
                f"Failed to parse EVS JSON response: {str(e)}"
            ) from e

        normalized = normalize_concept(data, self.terminology)

        # 3. Write/Update Cache
        if use_cache and self.engine is not None:
            try:
                now_str = datetime.now(timezone.utc).isoformat()
                with Session(self.engine) as session:
                    statement = select(EvsConceptCacheEntry).where(
                        EvsConceptCacheEntry.code == code
                    )
                    existing = session.exec(statement).first()
                    version_index = (existing.version_index + 1) if existing else 1

                    entry = EvsConceptCacheEntry(
                        code=normalized["code"] or code,
                        decode=normalized["decode"],
                        system=normalized["system"],
                        valid=normalized["valid"],
                        updated_at=now_str,
                        ttl_seconds=self.default_ttl_seconds,
                        created_at=existing.created_at if existing else now_str,
                        created_by="system",
                        reason_for_change="Concept caching"
                        if existing
                        else "Initial cache write",
                        version_index=version_index,
                    )
                    session.merge(entry)
                    session.commit()
            except Exception as e:
                logger.warning("Failed to update EVS concept cache: %s", str(e))

        return normalized

    async def search_concepts(
        self,
        term: str,
        client: Optional[httpx.AsyncClient] = None,
        from_record: Optional[int] = None,
        page_size: Optional[int] = None,
    ) -> List[Dict[str, Any]]:
        """Perform a text search for terminology concepts.

        Args:
            term (str): The search term.
            client (httpx.AsyncClient, optional): Shared client instance.
            from_record (int, optional): The starting record offset.
            page_size (int, optional): The maximum number of results to return.

        Returns:
            List[Dict[str, Any]]: A list of normalized concept dictionaries.

        Raises:
            EVSTimeoutError: If the request times out.
            EVSTransportError: For transport failures, connection issues, or HTTP errors.
        """
        url = f"{self.base_url}/api/v1/concept/{self.terminology}/search"
        params = {"term": term}
        if from_record is not None:
            params["fromRecord"] = str(from_record)
        if page_size is not None:
            params["pageSize"] = str(page_size)

        try:
            if client is not None:
                response = await client.get(url, params=params)
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, params=params)

            response.raise_for_status()

        except httpx.TimeoutException as e:
            raise EVSTimeoutError(f"EVS client request timed out: {str(e)}") from e

        except httpx.HTTPStatusError as e:
            raise EVSTransportError(
                f"HTTP error from EVS API: {e.response.status_code} - {e.response.text}"
            ) from e

        except httpx.RequestError as e:
            raise EVSTransportError(
                f"Transport failure contacting EVS API: {str(e)}"
            ) from e

        try:
            data = response.json()
        except Exception as e:
            raise EVSTransportError(
                f"Failed to parse EVS JSON response: {str(e)}"
            ) from e

        concepts_list = []
        if isinstance(data, list):
            concepts_list = data
        elif isinstance(data, dict):
            concepts_list = data.get("concepts") or data.get("results") or []

        results = []
        for c in concepts_list:
            if isinstance(c, dict):
                results.append(normalize_concept(c, self.terminology))

        return results
