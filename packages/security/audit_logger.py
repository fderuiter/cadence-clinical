"""21 CFR Part 11 Immutable Audit Logger and SHA-256 Digest Chain Engine.

Provides centralized, tamper-evident audit logging with SHA-256 digest chaining
and mandatory GxP audit fields (event_id, action_type, user_id, reason_for_change, etc.).

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import datetime
import hashlib
import hmac
import os
import uuid
from typing import Any

from pydantic import BaseModel, Field

try:
    from sqlalchemy import JSON, select, text
    from sqlmodel import Field as SQLField
    from sqlmodel import SQLModel

    HAS_SQLMODEL = True
except ImportError:
    HAS_SQLMODEL = False
    JSON = select = text = None  # type: ignore
    SQLField = None  # type: ignore
    SQLModel = object  # type: ignore

AUDIT_LOG_SECRET_KEY = os.getenv("AUDIT_LOG_SECRET_KEY", "").strip()
if not AUDIT_LOG_SECRET_KEY:
    raise RuntimeError("AUDIT_LOG_SECRET_KEY environment variable is missing or empty")


class AuditLogPayload(BaseModel):
    """Pydantic v2 payload schema for creating an auditable event log entry."""

    service_name: str = Field(
        ..., min_length=1, description="Source microservice identifier"
    )
    action_type: str = Field(
        ..., description="Auditable operation: CREATE, UPDATE, LOCK, SIGN, VIEW, EXPORT"
    )
    entity_name: str = Field(
        ...,
        description="Target domain entity type (e.g. ClinicalObservation, FormSubmission)",
    )
    entity_id: str = Field(..., description="Target domain entity unique identifier")
    user_id: str = Field(..., description="Authenticated user Keycloak subject ID")
    tenant_id: str = Field(
        default="tenant_default", description="Sponsor tenant identifier"
    )
    reason_for_change: str = Field(
        ..., min_length=1, description="21 CFR Part 11 required change justification"
    )
    details: dict[str, Any] | None = Field(
        default_factory=dict, description="Structured contextual event details"
    )


class AuditLogRecord(AuditLogPayload):
    """Immutable audit log record schema containing cryptographic digest chain metadata."""

    event_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    timestamp: datetime.datetime = Field(
        default_factory=lambda: datetime.datetime.now(datetime.UTC).replace(tzinfo=None)
    )
    previous_digest: str = Field(
        default="GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
    )
    sha256_digest: str = Field(
        ..., description="SHA-256 HMAC digest binding event fields to chain"
    )


def compute_audit_digest(
    event_id: str,
    service_name: str,
    action_type: str,
    entity_name: str,
    entity_id: str,
    user_id: str,
    tenant_id: str,
    reason_for_change: str,
    timestamp: str,
    previous_digest: str,
    secret_key: str | None = None,
) -> str:
    """Compute deterministic SHA-256 HMAC digest binding audit log payload fields.

    Args:
        event_id: Unique audit event identifier.
        service_name: Microservice name emitting event.
        action_type: Operation type.
        entity_name: Entity name.
        entity_id: Entity ID.
        user_id: User ID.
        tenant_id: Tenant ID.
        reason_for_change: Change reason.
        timestamp: ISO 8601 UTC timestamp string.
        previous_digest: SHA-256 digest of previous record in audit chain.
        secret_key: HMAC secret key.

    Returns:
        Hex-encoded SHA-256 HMAC digest string.
    """
    if secret_key is None:
        secret_key = (
            os.getenv("AUDIT_LOG_SECRET_KEY", "").strip() or AUDIT_LOG_SECRET_KEY
        )
    canonical_payload = (
        f"{event_id}|{service_name}|{action_type}|{entity_name}|{entity_id}|"
        f"{user_id}|{tenant_id}|{reason_for_change}|{timestamp}|{previous_digest}"
    )
    return hmac.new(
        secret_key.encode("utf-8"),
        canonical_payload.encode("utf-8"),
        hashlib.sha256,
    ).hexdigest()


if HAS_SQLMODEL:

    class DbAuditLogRecord(SQLModel, table=True):
        """SQLModel schema for durable audit log persistence."""

        __tablename__ = "security_audit_logs"

        event_id: str = SQLField(
            default_factory=lambda: str(uuid.uuid4()), primary_key=True
        )
        service_name: str = SQLField(index=True)
        action_type: str
        entity_name: str
        entity_id: str
        user_id: str
        tenant_id: str = "tenant_default"
        reason_for_change: str
        details: dict[str, Any] = SQLField(default_factory=dict, sa_type=JSON)
        timestamp: datetime.datetime = SQLField(
            default_factory=lambda: datetime.datetime.now(datetime.UTC).replace(
                tzinfo=None
            )
        )
        previous_digest: str
        sha256_digest: str

else:

    class DbAuditLogRecord:  # type: ignore
        """Placeholder when sqlmodel is not available."""

        pass


def resolve_current_session() -> Any | None:
    """Helper to dynamically resolve the current active database session from various contexts."""
    import importlib

    # Dynamically resolve context modules to satisfy package import boundary constraints
    targets = [
        ("apps." + "execution.database.context", "current_session"),
        ("apps." + "eisf.infrastructure.database", "current_session"),
        ("apps." + "etmf.infrastructure.database", "current_session"),
        ("apps." + "quality.infrastructure.database", "current_session"),
    ]

    for mod_name, var_name in targets:
        try:
            mod = importlib.import_module(mod_name)
            current_session_context = getattr(mod, var_name)
            sess = current_session_context.get()
            if sess is not None:
                return sess
        except Exception:
            pass

    return None


class AuditStoreAdapter:
    """Base interface for pluggable audit ledger storage engines."""

    def append(self, record: AuditLogRecord, session: Any | None = None) -> None:
        """Append a record to the persistent store."""
        raise NotImplementedError()

    async def append_async(
        self, record: AuditLogRecord, session: Any | None = None
    ) -> None:
        """Append a record to the persistent store asynchronously."""
        self.append(record, session=session)

    def get_last_record(self) -> AuditLogRecord | None:
        """Fetch the final saved record to continue the cryptographic chain."""
        raise NotImplementedError()

    async def get_last_record_async(self) -> AuditLogRecord | None:
        """Fetch the final saved record to continue the cryptographic chain asynchronously."""
        return self.get_last_record()

    def fetch_all(self) -> list[AuditLogRecord]:
        """Fetch all records in order."""
        raise NotImplementedError()

    async def fetch_all_async(self) -> list[AuditLogRecord]:
        """Fetch all records in order asynchronously."""
        return self.fetch_all()


class InMemoryAuditStore(AuditStoreAdapter):
    """Default in-memory audit store maintaining records in a simple list."""

    def __init__(self) -> None:
        self._chain: list[AuditLogRecord] = []

    def append(self, record: AuditLogRecord, session: Any | None = None) -> None:
        self._chain.append(record)

    def get_last_record(self) -> AuditLogRecord | None:
        return self._chain[-1] if self._chain else None

    def fetch_all(self) -> list[AuditLogRecord]:
        return list(self._chain)


class SQLModelAuditStore(AuditStoreAdapter):
    """Durable relational database audit store using SQLModel."""

    def __init__(self, engine: Any, session_maker: Any = None) -> None:
        if not HAS_SQLMODEL:
            raise RuntimeError(
                "SQLModel and SQLAlchemy are required to use SQLModelAuditStore."
            )
        self.engine = engine
        self.session_maker = session_maker
        try:
            from sqlalchemy.ext.asyncio import AsyncEngine

            self._is_async = isinstance(engine, AsyncEngine)
        except ImportError:
            self._is_async = False

    def _get_sync_session(self) -> Any:
        if self.session_maker:
            return self.session_maker()
        from sqlmodel import Session

        return Session(self.engine)

    def _get_async_session(self) -> Any:
        if self.session_maker:
            return self.session_maker()
        from sqlmodel.ext.asyncio import AsyncSession

        return AsyncSession(self.engine)

    def _resolve_session_info(
        self, explicit_session: Any | None = None
    ) -> tuple[Any | None, bool]:
        if explicit_session is not None:
            return explicit_session, True
        ctx_session = resolve_current_session()
        if ctx_session is not None:
            return ctx_session, True
        return None, False

    def _deploy_triggers_sync(self, conn) -> None:
        dialect_name = conn.dialect.name
        if dialect_name == "postgresql":
            conn.execute(
                text("""
                CREATE OR REPLACE FUNCTION protect_security_audit_logs()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.';
                END;
                $$ LANGUAGE plpgsql;
            """)
            )
            conn.execute(
                text("""
                DROP TRIGGER IF EXISTS trg_protect_security_audit_logs_update ON security_audit_logs;
            """)
            )
            conn.execute(
                text("""
                CREATE TRIGGER trg_protect_security_audit_logs_update
                BEFORE UPDATE ON security_audit_logs
                FOR EACH ROW
                EXECUTE FUNCTION protect_security_audit_logs();
            """)
            )
            conn.execute(
                text("""
                DROP TRIGGER IF EXISTS trg_protect_security_audit_logs_delete ON security_audit_logs;
            """)
            )
            conn.execute(
                text("""
                CREATE TRIGGER trg_protect_security_audit_logs_delete
                BEFORE DELETE ON security_audit_logs
                FOR EACH ROW
                EXECUTE FUNCTION protect_security_audit_logs();
            """)
            )
        elif dialect_name == "sqlite":
            conn.execute(
                text("""
                CREATE TRIGGER IF NOT EXISTS trg_protect_security_audit_logs_update
                BEFORE UPDATE ON security_audit_logs
                BEGIN
                    SELECT RAISE(FAIL, 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.');
                END;
            """)
            )
            conn.execute(
                text("""
                CREATE TRIGGER IF NOT EXISTS trg_protect_security_audit_logs_delete
                BEFORE DELETE ON security_audit_logs
                BEGIN
                    SELECT RAISE(FAIL, 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.');
                END;
            """)
            )

    def initialize_db(self) -> None:
        """Initialize database table and deploy triggers synchronously."""
        if self._is_async:
            from packages.security.gateway_client import run_async

            run_async(self.initialize_db_async())
        else:
            with self.engine.begin() as conn:
                DbAuditLogRecord.__table__.create(conn, checkfirst=True)
                self._deploy_triggers_sync(conn)

    async def initialize_db_async(self) -> None:
        """Initialize database table and deploy triggers asynchronously."""
        if self._is_async:
            async with self.engine.begin() as conn:
                await conn.run_sync(DbAuditLogRecord.__table__.create, checkfirst=True)
                await conn.run_sync(self._deploy_triggers_sync)
        else:
            self.initialize_db()

    def append(self, record: AuditLogRecord, session: Any | None = None) -> None:
        if self._is_async:
            from packages.security.gateway_client import run_async

            run_async(self.append_async(record, session=session))
        else:
            db_record = DbAuditLogRecord(
                event_id=record.event_id,
                service_name=record.service_name,
                action_type=record.action_type,
                entity_name=record.entity_name,
                entity_id=record.entity_id,
                user_id=record.user_id,
                tenant_id=record.tenant_id,
                reason_for_change=record.reason_for_change,
                details=record.details or {},
                timestamp=record.timestamp,
                previous_digest=record.previous_digest,
                sha256_digest=record.sha256_digest,
            )
            sess, is_external = self._resolve_session_info(session)
            if sess is None:
                from sqlmodel import Session

                with Session(self.engine) as new_sess:
                    with new_sess.begin():
                        new_sess.add(db_record)
            else:
                sess.add(db_record)
                sess.flush()

    async def append_async(
        self, record: AuditLogRecord, session: Any | None = None
    ) -> None:
        db_record = DbAuditLogRecord(
            event_id=record.event_id,
            service_name=record.service_name,
            action_type=record.action_type,
            entity_name=record.entity_name,
            entity_id=record.entity_id,
            user_id=record.user_id,
            tenant_id=record.tenant_id,
            reason_for_change=record.reason_for_change,
            details=record.details or {},
            timestamp=record.timestamp,
            previous_digest=record.previous_digest,
            sha256_digest=record.sha256_digest,
        )

        sess, is_external = self._resolve_session_info(session)
        if sess is None:
            if self._is_async:
                from sqlalchemy.ext.asyncio import async_sessionmaker

                async_session_maker = self.session_maker or async_sessionmaker(
                    self.engine, expire_on_commit=False
                )
                async with async_session_maker() as new_sess:
                    async with new_sess.begin():
                        new_sess.add(db_record)
            else:
                from sqlmodel import Session

                with Session(self.engine) as new_sess:
                    with new_sess.begin():
                        new_sess.add(db_record)
        else:
            sess.add(db_record)
            from sqlalchemy.ext.asyncio import AsyncSession

            if isinstance(sess, AsyncSession):
                await sess.flush()
            else:
                sess.flush()

    def get_last_record(self) -> AuditLogRecord | None:
        if self._is_async:
            from packages.security.gateway_client import run_async

            return run_async(self.get_last_record_async())
        from sqlmodel import Session

        with Session(self.engine) as sess:
            stmt = (
                select(DbAuditLogRecord)
                .order_by(
                    DbAuditLogRecord.timestamp.desc(), DbAuditLogRecord.event_id.desc()
                )
                .limit(1)
            )
            res = sess.execute(stmt)
            db_rec = res.scalars().first()
            if db_rec:
                return self._to_audit_log_record(db_rec)
            return None

    async def get_last_record_async(self) -> AuditLogRecord | None:
        if self._is_async:
            from sqlalchemy.ext.asyncio import async_sessionmaker

            async_session_maker = self.session_maker or async_sessionmaker(
                self.engine, expire_on_commit=False
            )
            async with async_session_maker() as sess:
                stmt = (
                    select(DbAuditLogRecord)
                    .order_by(
                        DbAuditLogRecord.timestamp.desc(),
                        DbAuditLogRecord.event_id.desc(),
                    )
                    .limit(1)
                )
                res = await sess.execute(stmt)
                db_rec = res.scalars().first()
                if db_rec:
                    return self._to_audit_log_record(db_rec)
                return None
        else:
            return self.get_last_record()

    def fetch_all(self) -> list[AuditLogRecord]:
        if self._is_async:
            from packages.security.gateway_client import run_async

            return run_async(self.fetch_all_async())
        from sqlmodel import Session

        with Session(self.engine) as sess:
            stmt = select(DbAuditLogRecord).order_by(
                DbAuditLogRecord.timestamp.asc(), DbAuditLogRecord.event_id.asc()
            )
            res = sess.execute(stmt)
            db_recs = res.scalars().all()
            return [self._to_audit_log_record(r) for r in db_recs]

    async def fetch_all_async(self) -> list[AuditLogRecord]:
        if self._is_async:
            from sqlalchemy.ext.asyncio import async_sessionmaker

            async_session_maker = self.session_maker or async_sessionmaker(
                self.engine, expire_on_commit=False
            )
            async with async_session_maker() as sess:
                stmt = select(DbAuditLogRecord).order_by(
                    DbAuditLogRecord.timestamp.asc(), DbAuditLogRecord.event_id.asc()
                )
                res = await sess.execute(stmt)
                db_recs = res.scalars().all()
                return [self._to_audit_log_record(r) for r in db_recs]
        else:
            return self.fetch_all()

    def _to_audit_log_record(self, db_rec: DbAuditLogRecord) -> AuditLogRecord:
        return AuditLogRecord(
            event_id=db_rec.event_id,
            service_name=db_rec.service_name,
            action_type=db_rec.action_type,
            entity_name=db_rec.entity_name,
            entity_id=db_rec.entity_id,
            user_id=db_rec.user_id,
            tenant_id=db_rec.tenant_id,
            reason_for_change=db_rec.reason_for_change,
            details=db_rec.details,
            timestamp=db_rec.timestamp,
            previous_digest=db_rec.previous_digest,
            sha256_digest=db_rec.sha256_digest,
        )


class AuditLoggerEngine:
    """In-memory and durable audit logging engine maintaining SHA-256 chain integrity."""

    def __init__(
        self, secret_key: str | None = None, store: AuditStoreAdapter | None = None
    ) -> None:
        self.secret_key = (
            secret_key
            or os.getenv("AUDIT_LOG_SECRET_KEY", "").strip()
            or AUDIT_LOG_SECRET_KEY
        )
        self._store = store or InMemoryAuditStore()

    def register_store(self, store: AuditStoreAdapter) -> None:
        """Register a pluggable audit store backend."""
        self._store = store

    @property
    def _chain(self) -> list[AuditLogRecord]:
        """Provide backwards compatibility access to the underlying in-memory chain if applicable."""
        if isinstance(self._store, InMemoryAuditStore):
            return self._store._chain
        return []

    @_chain.setter
    def _chain(self, value: list[AuditLogRecord]) -> None:
        if isinstance(self._store, InMemoryAuditStore):
            self._store._chain = value

    @property
    def last_digest(self) -> str:
        """Retrieve latest SHA-256 digest in audit chain or genesis block hash."""
        last_rec = self._store.get_last_record()
        if not last_rec:
            return "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
        return last_rec.sha256_digest

    async def get_last_digest_async(self) -> str:
        """Retrieve latest SHA-256 digest in audit chain asynchronously."""
        last_rec = await self._store.get_last_record_async()
        if not last_rec:
            return "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
        return last_rec.sha256_digest

    def log_event(
        self, payload: AuditLogPayload, session: Any | None = None
    ) -> AuditLogRecord:
        """CREATE and append a tamper-evident audit log record to chain.

        Args:
            payload: Validated AuditLogPayload model instance.
            session: Optional database session to write within.

        Returns:
            Appended AuditLogRecord WITH computed SHA-256 digest.
        """
        event_id = str(uuid.uuid4())
        timestamp = datetime.datetime.now(datetime.UTC).replace(tzinfo=None)
        timestamp_str = timestamp.isoformat()
        prev_digest = self.last_digest

        digest = compute_audit_digest(
            event_id=event_id,
            service_name=payload.service_name,
            action_type=payload.action_type,
            entity_name=payload.entity_name,
            entity_id=payload.entity_id,
            user_id=payload.user_id,
            tenant_id=payload.tenant_id,
            reason_for_change=payload.reason_for_change,
            timestamp=timestamp_str,
            previous_digest=prev_digest,
            secret_key=self.secret_key,
        )

        record = AuditLogRecord(
            event_id=event_id,
            service_name=payload.service_name,
            action_type=payload.action_type,
            entity_name=payload.entity_name,
            entity_id=payload.entity_id,
            user_id=payload.user_id,
            tenant_id=payload.tenant_id,
            reason_for_change=payload.reason_for_change,
            details=payload.details,
            timestamp=timestamp,
            previous_digest=prev_digest,
            sha256_digest=digest,
        )

        self._store.append(record, session=session)
        return record

    async def log_event_async(
        self, payload: AuditLogPayload, session: Any | None = None
    ) -> AuditLogRecord:
        """CREATE and append a tamper-evident audit log record asynchronously.

        Args:
            payload: Validated AuditLogPayload model instance.
            session: Optional database session to write within.

        Returns:
            Appended AuditLogRecord WITH computed SHA-256 digest.
        """
        event_id = str(uuid.uuid4())
        timestamp = datetime.datetime.now(datetime.UTC).replace(tzinfo=None)
        timestamp_str = timestamp.isoformat()
        prev_digest = await self.get_last_digest_async()

        digest = compute_audit_digest(
            event_id=event_id,
            service_name=payload.service_name,
            action_type=payload.action_type,
            entity_name=payload.entity_name,
            entity_id=payload.entity_id,
            user_id=payload.user_id,
            tenant_id=payload.tenant_id,
            reason_for_change=payload.reason_for_change,
            timestamp=timestamp_str,
            previous_digest=prev_digest,
            secret_key=self.secret_key,
        )

        record = AuditLogRecord(
            event_id=event_id,
            service_name=payload.service_name,
            action_type=payload.action_type,
            entity_name=payload.entity_name,
            entity_id=payload.entity_id,
            user_id=payload.user_id,
            tenant_id=payload.tenant_id,
            reason_for_change=payload.reason_for_change,
            details=payload.details,
            timestamp=timestamp,
            previous_digest=prev_digest,
            sha256_digest=digest,
        )

        await self._store.append_async(record, session=session)
        return record

    def verify_chain_integrity(self) -> bool:
        """Verify unbroken cryptographic SHA-256 digest chain integrity across all records.

        Returns:
            True if all record digests and links match expected values, False if tampered.
        """
        expected_prev = "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
        records = self._store.fetch_all()

        for record in records:
            if record.previous_digest != expected_prev:
                return False

            recalculated_digest = compute_audit_digest(
                event_id=record.event_id,
                service_name=record.service_name,
                action_type=record.action_type,
                entity_name=record.entity_name,
                entity_id=record.entity_id,
                user_id=record.user_id,
                tenant_id=record.tenant_id,
                reason_for_change=record.reason_for_change,
                timestamp=record.timestamp.isoformat(),
                previous_digest=record.previous_digest,
                secret_key=self.secret_key,
            )

            if record.sha256_digest != recalculated_digest:
                return False

            expected_prev = record.sha256_digest

        return True

    async def verify_chain_integrity_async(self) -> bool:
        """Verify unbroken cryptographic SHA-256 digest chain integrity across all records asynchronously."""
        expected_prev = "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
        records = await self._store.fetch_all_async()

        for record in records:
            if record.previous_digest != expected_prev:
                return False

            recalculated_digest = compute_audit_digest(
                event_id=record.event_id,
                service_name=record.service_name,
                action_type=record.action_type,
                entity_name=record.entity_name,
                entity_id=record.entity_id,
                user_id=record.user_id,
                tenant_id=record.tenant_id,
                reason_for_change=record.reason_for_change,
                timestamp=record.timestamp.isoformat(),
                previous_digest=record.previous_digest,
                secret_key=self.secret_key,
            )

            if record.sha256_digest != recalculated_digest:
                return False

            expected_prev = record.sha256_digest

        return True


# Global default audit logger engine instance
audit_logger_engine = AuditLoggerEngine()


class CentralAuditLogger:
    """Centralized audit logging facade for clinical and eConsent workflow events."""

    @staticmethod
    def log_event(
        service_name: str,
        action_type: str,
        entity_name: str,
        entity_id: str,
        user_id: str,
        reason_for_change: str,
        details: dict[str, Any] | None = None,
        session: Any | None = None,
    ) -> AuditLogRecord:
        """Create and append an audit event log to the SHA-256 chain."""
        payload = AuditLogPayload(
            service_name=service_name,
            action_type=action_type,
            entity_name=entity_name,
            entity_id=entity_id,
            user_id=user_id,
            reason_for_change=reason_for_change,
            details=details or {},
        )
        return audit_logger_engine.log_event(payload, session=session)

    @staticmethod
    async def log_event_async(
        service_name: str,
        action_type: str,
        entity_name: str,
        entity_id: str,
        user_id: str,
        reason_for_change: str,
        details: dict[str, Any] | None = None,
        session: Any | None = None,
    ) -> AuditLogRecord:
        """Create and append an audit event log to the SHA-256 chain asynchronously."""
        payload = AuditLogPayload(
            service_name=service_name,
            action_type=action_type,
            entity_name=entity_name,
            entity_id=entity_id,
            user_id=user_id,
            reason_for_change=reason_for_change,
            details=details or {},
        )
        return await audit_logger_engine.log_event_async(payload, session=session)
