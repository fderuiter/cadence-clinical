import functools
import inspect
import json
import uuid
from collections.abc import AsyncGenerator, Callable
from contextlib import asynccontextmanager
from datetime import datetime
from typing import Any

from fastapi import FastAPI
from sqlalchemy import JSON, Boolean, DateTime, Integer, String, event, func
from sqlalchemy.exc import IntegrityError, NoResultFound, SQLAlchemyError
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.orm import Mapped, Session, mapped_column

from packages.database.datetime_helpers import UTCDateTime as UTCDateTime
from packages.database.graph import (
    GraphDatabaseManager as GraphDatabaseManager,
)
from packages.database.graph import (
    validate_cypher_query as validate_cypher_query,
)
from packages.database.graph import (
    with_transaction_retry as with_transaction_retry,
)
from packages.hexagonal import (
    DatabaseError,
    EntityAlreadyExistsError,
    EntityNotFoundError,
)


# Custom compliance exceptions
class ComplianceError(PermissionError):
    """Raised when writing to a locked trial, site, visit, subject, or form."""

    pass


class AuditJustificationError(ValueError):
    """Raised when an audited record is created, modified, or deleted without a valid justification."""

    pass


_TRIAL_LOCK_CHECKERS: list[Callable[[], bool]] = []
_SITE_LOCK_CHECKERS: list[Callable[[str], bool]] = []
_VISIT_LOCK_CHECKERS: list[Callable[[str], bool]] = []
_SUBJECT_LOCK_CHECKERS: list[Callable[[str], bool]] = []
_FORM_LOCK_CHECKERS: list[Callable[[str], bool]] = []


def register_trial_lock_checker(checker: Callable[[], bool]) -> None:
    """Register a callback that checks if a trial-wide lock is active."""
    _TRIAL_LOCK_CHECKERS.append(checker)


def register_site_lock_checker(checker: Callable[[str], bool]) -> None:
    """Register a callback that checks if a site-level lock is active."""
    _SITE_LOCK_CHECKERS.append(checker)


def register_visit_lock_checker(checker: Callable[[str], bool]) -> None:
    """Register a callback that checks if a visit-level lock is active."""
    _VISIT_LOCK_CHECKERS.append(checker)


def register_subject_lock_checker(checker: Callable[[str], bool]) -> None:
    """Register a callback that checks if a subject-level lock is active."""
    _SUBJECT_LOCK_CHECKERS.append(checker)


def register_form_lock_checker(checker: Callable[[str], bool]) -> None:
    """Register a callback that checks if a form-level lock is active."""
    _FORM_LOCK_CHECKERS.append(checker)


def is_trial_locked() -> bool:
    """Check if any registered lock checker is active."""
    return any(checker() for checker in _TRIAL_LOCK_CHECKERS)


def is_site_locked(site_id: str) -> bool:
    """Check if any registered site lock checker is active."""
    return any(checker(site_id) for checker in _SITE_LOCK_CHECKERS)


def is_visit_locked(visit_id: str) -> bool:
    """Check if any registered visit lock checker is active."""
    return any(checker(visit_id) for checker in _VISIT_LOCK_CHECKERS)


def is_subject_locked(subject_id: str) -> bool:
    """Check if any registered subject lock checker is active."""
    return any(checker(subject_id) for checker in _SUBJECT_LOCK_CHECKERS)


def is_form_locked(form_id: str) -> bool:
    """Check if any registered form lock checker is active."""
    return any(checker(form_id) for checker in _FORM_LOCK_CHECKERS)


@event.listens_for(Session, "before_flush")
def receive_before_flush(session: Session, flush_context, instances):
    """
    Standard GxP pre-flush listener that intercepts session flushes to:
    1. Reject modifications if trial is frozen/locked.
    2. Reject modifications if specific site/visit/subject/form locks are active.
    3. Reject creations or updates/deletions of audited/clinical data lacking valid justifications.
    """
    if not session.is_modified:
        return

    # Check if there are any non-excluded modifications
    non_excluded_objs = []
    for obj in list(session.new) + list(session.dirty) + list(session.deleted):
        tablename = getattr(obj, "__tablename__", "")
        # fmt: off
        if tablename in {
            "audit_logs", "audit_ledger_seals", "data_locks", "integration_outbox", "safety_audit_logs", "ticket_audit_logs",
            "org_audit_logs", "audit_schema.audit_logs", "audit_schema.audit_ledger_seals", "notifications",
            "notification_records", "notification_audit_logs", "notification_deliveries", "notifications_audit_logs",
            "tmf_documents", "tmf_audit_logs", "tmf_seals", "tmf_expected_documents", "tmf_document_qc_transitions",
            "tmf_document_expiration_alert_states", "epro_submissions", "epro_defeated_submissions",
            "epro_quarantined_submissions", "interop_messages", "interop_audit_logs", "instruments",
            "subject_assignments", "subject_notifications", "ctms_studies", "ctms_audit_logs",
            "ctms_monitoring_visits", "ctms_monitoring_visit_findings", "ctms_generated_letters",
            "ctms_recruitment_records", "ctms_site_milestones", "ctms_cra_allocations", "ctms_investigator_grants",
            "ctms_budget_line_items", "ctms_payment_milestones", "ctms_investigator_payables", "ctms_clinical_queries",
            "ctms_defeated_monitoring_visits", "quality_deviations", "quality_root_cause_analyses",
            "quality_capa_records", "quality_audit_logs", "isf_documents", "isf_audit_logs", "consent_documents",
            "consent_audit_logs", "consent_clauses", "safety_cases", "safety_export_jobs", "tickets",
            "ticket_comments", "ticket_attachments", "org_training_logs",
        }:
        # fmt: on
            continue
        non_excluded_objs.append(obj)

    if not non_excluded_objs:
        return

    # Check for active trial lock / freeze
    if is_trial_locked():
        raise ComplianceError(
            "Trial is currently locked in a read-only state due to a security violation."
        )

    for obj in non_excluded_objs:
        tablename = getattr(obj, "__tablename__", "")

        # Site level locks
        site_id = getattr(obj, "site_id", None) or getattr(obj, "site", None)
        if site_id is not None:
            if is_site_locked(str(site_id)):
                raise ComplianceError(
                    f"Site {site_id} is currently locked in a read-only state."
                )

        # Visit level locks
        visit_id = getattr(obj, "visit_id", None) or getattr(obj, "visit", None)
        if visit_id is not None:
            if is_visit_locked(str(visit_id)):
                raise ComplianceError(
                    f"Visit {visit_id} is currently locked in a read-only state."
                )

        # Subject level locks
        subject_id = getattr(obj, "subject_id", None) or getattr(obj, "subject", None)
        if subject_id is not None:
            if is_subject_locked(str(subject_id)):
                raise ComplianceError(
                    f"Subject {subject_id} is currently locked in a read-only state."
                )

        # Form level locks
        form_id = getattr(obj, "form_id", None) or getattr(obj, "form", None)
        if form_id is not None:
            if is_form_locked(str(form_id)):
                raise ComplianceError(
                    f"Form {form_id} is currently locked in a read-only state."
                )

        # GxP Change Justification Check (Criterion 2)
        # Any change/creation/deletion on an audited clinical record requires a non-empty, non-blank reason.
        is_audited = False
        classname = obj.__class__.__name__
        if (
            "Audited" in classname
            or "clinical" in tablename.lower()
            or hasattr(obj, "reason_for_change")
            or hasattr(obj, "change_reason")
        ):
            is_audited = True

        if is_audited:
            # Try to fetch reason
            reason = getattr(obj, "reason_for_change", None) or getattr(
                obj, "change_reason", None
            )
            if not reason:
                try:
                    from packages.security.context import current_change_reason

                    reason = current_change_reason.get()
                except ImportError:
                    pass

            if not reason or not isinstance(reason, str) or not reason.strip():
                raise AuditJustificationError(
                    "GxP change reason is required. Reason for change cannot be empty or consist only of whitespace."
                )


def map_database_exceptions(func: Callable) -> Callable:
    """Decorator to translate SQLAlchemy database errors to clean domain exceptions."""

    if inspect.iscoroutinefunction(func):

        @functools.wraps(func)
        async def async_wrapper(*args: Any, **kwargs: Any) -> Any:
            try:
                return await func(*args, **kwargs)
            except NoResultFound as e:
                raise EntityNotFoundError(f"Requested entity not found: {e}") from e
            except IntegrityError as e:
                raise EntityAlreadyExistsError(
                    f"Database constraint violation or duplicate key: {e}"
                ) from e
            except SQLAlchemyError as e:
                raise DatabaseError(f"Database operational or schema error: {e}") from e

        return async_wrapper

    @functools.wraps(func)
    def sync_wrapper(*args: Any, **kwargs: Any) -> Any:
        try:
            return func(*args, **kwargs)
        except NoResultFound as e:
            raise EntityNotFoundError(f"Requested entity not found: {e}") from e
        except IntegrityError as e:
            raise EntityAlreadyExistsError(
                f"Database constraint violation or duplicate key: {e}"
            ) from e
        except SQLAlchemyError as e:
            raise DatabaseError(f"Database operational or schema error: {e}") from e

    return sync_wrapper


class RelationalDatabaseManager:
    """
    Parameterized relational database manager capable of managing connection pools based on distinct microservice configurations.
    """

    def __init__(self, service_name: str) -> None:
        self.service_name = service_name
        self.engine: Any = None
        self.session_maker: async_sessionmaker[AsyncSession] | None = None

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        if self.engine is not None:
            return
        engine_options = {}

        # Standardize JSON serialization/deserialization across SQLite and PostgreSQL
        engine_options["json_serializer"] = lambda obj: json.dumps(
            obj, ensure_ascii=False
        )
        engine_options["json_deserializer"] = json.loads

        if database_url.startswith("sqlite"):
            self.engine = create_async_engine(
                database_url, **{**engine_options, **kwargs}
            )

            @event.listens_for(self.engine.sync_engine, "connect")
            def set_sqlite_pragma(dbapi_connection, connection_record):
                """Enable SQLite foreign key support on connect event."""
                cursor = dbapi_connection.cursor()
                try:
                    cursor.execute("PRAGMA foreign_keys=ON")
                    cursor.execute("PRAGMA busy_timeout=30000")  # deid-ignore
                except Exception:
                    pass
                finally:
                    cursor.close()
        else:
            # PostgreSQL connection pool and other options
            pg_options = {
                "pool_size": 10,
                "max_overflow": 20,
                "pool_timeout": 30,
                "pool_pre_ping": True,
            }
            # Merge with kwargs, letting kwargs override
            for k, v in pg_options.items():
                if k not in kwargs:
                    kwargs[k] = v
            self.engine = create_async_engine(
                database_url, **{**engine_options, **kwargs}
            )

        self.session_maker = async_sessionmaker(
            bind=self.engine, class_=AsyncSession, expire_on_commit=False
        )

    async def close(self) -> None:
        if self.engine:
            await self.engine.dispose()
            self.engine = None
            self.session_maker = None

    def get_session_maker(self) -> async_sessionmaker[AsyncSession]:
        if not self.session_maker:
            raise Exception(
                f"{self.service_name} database session manager is not initialized."
            )
        return self.session_maker


class DatabaseSessionDependency:
    """
    Standardized FastAPI route dependency helper that manages database session lifespans,
    automatically committing on success or rolling back on failure.
    """

    def __init__(self, db_manager: RelationalDatabaseManager) -> None:
        self.db_manager = db_manager

    async def __call__(self) -> AsyncGenerator[AsyncSession]:
        from sqlalchemy import text

        session_maker = self.db_manager.get_session_maker()
        async with session_maker() as session:
            try:
                # Propagate context variables into database session if context variables exist
                try:
                    if session.bind and session.bind.dialect.name == "postgresql":
                        from packages.security.context import (
                            current_change_reason,
                            current_user_id,
                        )

                        user_id = current_user_id.get()
                        reason = current_change_reason.get()
                        if user_id:
                            await session.execute(
                                text(
                                    "SELECT set_config('cadence.current_user_id', :user_id, true);"
                                ),
                                {"user_id": user_id},
                            )
                        if reason:
                            await session.execute(
                                text(
                                    "SELECT set_config('cadence.current_change_reason', :reason, true);"
                                ),
                                {"reason": reason},
                            )
                        await session.execute(
                            text(
                                "SELECT set_config('cadence.app_writing', 'true', true);"
                            )
                        )
                except Exception:
                    pass

                yield session
                await session.commit()
            except Exception:
                await session.rollback()
                raise


def get_relational_db_lifespan(
    db_manager: RelationalDatabaseManager,
    database_url: str,
    base_metadata: Any | None = None,
    startup_hooks: list | None = None,
    shutdown_hooks: list | None = None,
    **kwargs: Any,
) -> Any:
    """
    Unified application lifecycle wrapper that automatically handles database connection
    setup and local migrations (on SQLite), and supports parameterized callback hooks for
    executing service-specific startup and shutdown tasks.
    """

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncGenerator[None]:
        # Initialize database engine and session maker
        db_manager.init_db(database_url, **kwargs)

        # Run local migrations if using sqlite
        if database_url.startswith("sqlite") and base_metadata is not None:
            async with db_manager.engine.begin() as conn:
                await conn.run_sync(base_metadata.create_all)
                # If this is the eTMF database with DocumentQCTransition, deploy SQLite triggers
                from sqlalchemy import inspect, text

                def has_table(sync_conn):
                    return inspect(sync_conn).has_table("tmf_document_qc_transitions")

                table_exists = await conn.run_sync(has_table)
                if table_exists:
                    await conn.execute(
                        text("""
                        CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_update
                        BEFORE UPDATE ON tmf_document_qc_transitions
                        BEGIN
                            SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be updated.');
                        END;
                    """)
                    )
                    await conn.execute(
                        text("""
                        CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_delete
                        BEFORE DELETE ON tmf_document_qc_transitions
                        BEGIN
                            SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be deleted.');
                        END;
                    """)
                    )
                    await conn.execute(
                        text("""
                        CREATE TRIGGER IF NOT EXISTS tmf_documents_no_delete
                        BEFORE DELETE ON tmf_documents
                        BEGIN
                            SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: eTMF documents are immutable and cannot be deleted.');
                        END;
                    """)
                    )

        # Run service-specific asynchronous startup tasks
        if startup_hooks:
            for hook in startup_hooks:
                await hook()

        try:
            yield
        finally:
            # Run service-specific asynchronous shutdown tasks
            if shutdown_hooks:
                for hook in shutdown_hooks:
                    await hook()
            # Clean up database engine
            await db_manager.close()

    return lifespan


def create_transactional_decorator(
    db_manager: RelationalDatabaseManager,
    current_session_var: Any,
) -> Any:
    """
    Creates a standardized transactional decorator for a specific database manager and contextvar.
    """
    import functools

    from sqlalchemy import text

    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        async def wrapper(*args, **kwargs) -> Any:
            existing_session = current_session_var.get()
            if existing_session is not None:
                # Reuse the existing session in the context (nested transaction)
                return await func(*args, **kwargs)

            session_maker = db_manager.get_session_maker()
            async with session_maker() as session:
                async with session.begin():
                    token = current_session_var.set(session)
                    try:
                        # Propagate context variables into database session if context variables exist
                        try:
                            if (
                                session.bind
                                and session.bind.dialect.name == "postgresql"
                            ):
                                from packages.security.context import (
                                    current_change_reason,
                                    current_user_id,
                                )

                                user_id = current_user_id.get()
                                reason = current_change_reason.get()
                                if user_id:
                                    await session.execute(
                                        text(
                                            "SELECT set_config('cadence.current_user_id', :user_id, true);"
                                        ),
                                        {"user_id": user_id},
                                    )
                                if reason:
                                    await session.execute(
                                        text(
                                            "SELECT set_config('cadence.current_change_reason', :reason, true);"
                                        ),
                                        {"reason": reason},
                                    )
                                await session.execute(
                                    text(
                                        "SELECT set_config('cadence.app_writing', 'true', true);"
                                    )
                                )
                        except Exception:
                            pass

                        return await func(*args, **kwargs)
                    finally:
                        current_session_var.reset(token)

        return wrapper

    return decorator


class IntegrationOutboxMixin:
    """
    Mixin defining standardized integration outbox columns.
    Complies with FDA 21 CFR Part 11 auditing and tracking constraints.
    """

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    event_type: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    payload: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False, index=True
    )
    attempts: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    last_error: Mapped[str | None] = mapped_column(String, nullable=True)
    next_retry_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )
    completed_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )
    retry_eligible: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    correlation_id: Mapped[str] = mapped_column(
        String(255), unique=True, index=True, nullable=False
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


# Expose new Graph Persistence capabilities while keeping imports resilient
