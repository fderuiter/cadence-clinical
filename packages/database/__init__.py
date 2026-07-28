from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator, Optional

from fastapi import FastAPI
from sqlalchemy import event
from sqlalchemy.exc import DatabaseError
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.orm import Session


class RelationalDatabaseManager:
    """
    Parameterized relational database manager capable of managing connection pools based on distinct microservice configurations.
    """

    def __init__(self, service_name: str) -> None:
        self.service_name = service_name
        self.engine: Any = None
        self.session_maker: Optional[async_sessionmaker[AsyncSession]] = None

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        self.engine = create_async_engine(database_url, **kwargs)

        @event.listens_for(self.engine.sync_engine, "connect")
        def set_sqlite_pragma(dbapi_connection, connection_record):
            """Enable SQLite foreign key support on connect event."""
            # If using sqlite, ensure foreign keys are enabled (if dialect is sqlite)
            cursor = dbapi_connection.cursor()
            try:
                cursor.execute("PRAGMA foreign_keys=ON")
            except Exception:
                pass
            finally:
                cursor.close()

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

    async def __call__(self) -> AsyncGenerator[AsyncSession, None]:
        session_maker = self.db_manager.get_session_maker()
        async with session_maker() as session:
            try:
                yield session
                await session.commit()
            except Exception:
                await session.rollback()
                raise


def get_relational_db_lifespan(
    db_manager: RelationalDatabaseManager,
    database_url: str,
    base_metadata: Optional[Any] = None,
    startup_hooks: Optional[list] = None,
    shutdown_hooks: Optional[list] = None,
    **kwargs: Any,
) -> Any:
    """
    Unified application lifecycle wrapper that automatically handles database connection
    setup and local migrations (on SQLite), and supports parameterized callback hooks for
    executing service-specific startup and shutdown tasks.
    """

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
        # Initialize database engine and session maker
        db_manager.init_db(database_url, **kwargs)

        # Run local migrations if using sqlite
        if database_url.startswith("sqlite") and base_metadata is not None:
            async with db_manager.engine.begin() as conn:
                await conn.run_sync(base_metadata.create_all)

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


def get_primary_key(obj):
    try:
        from sqlalchemy import inspect

        mapper = inspect(obj).mapper
        pk_cols = mapper.primary_key
        if not pk_cols:
            return "unknown"
        return str(getattr(obj, pk_cols[0].name))
    except Exception:
        return "unknown"


@event.listens_for(Session, "before_flush")
def centralized_before_flush(session: Session, flush_context, instances):
    if not session.is_modified:
        return

    # Dynamically import models and context variables to prevent circular imports
    try:
        from apps.ctms.models import CTMSAuditLog
    except ImportError:
        CTMSAuditLog = None

    try:
        from apps.etmf.models import TMFAuditLog
    except ImportError:
        TMFAuditLog = None

    try:
        from apps.quality.models import QualityAuditLog
    except ImportError:
        QualityAuditLog = None

    from packages.security.context import (
        current_change_reason,
        current_user_id,
        current_user_roles,
    )

    user_id = current_user_id.get()
    user_roles = current_user_roles.get()
    change_reason = current_change_reason.get()

    audit_logs_to_add = []

    # 1. Prevent hard deletions on clinical trial models
    for obj in session.deleted:
        if not hasattr(obj, "__tablename__"):
            continue
        tbl = obj.__tablename__
        if tbl in (
            "ctms_audit_logs",
            "tmf_audit_logs",
            "quality_audit_logs",
            "ctms_audit_ledger_seals",
            "tmf_audit_ledger_seals",
            "quality_audit_ledger_seals",
        ):
            raise DatabaseError(
                "Deletion of AuditLog or LedgerSeal is strictly forbidden to comply with 21 CFR Part 11.",
                None,
                None,
            )
        elif (
            tbl.startswith("ctms_")
            or tbl.startswith("tmf_")
            or tbl.startswith("quality_")
        ):
            raise DatabaseError(
                f"Hard deletion of clinical model {obj.__class__.__name__} is forbidden. Use soft deletions.",
                None,
                None,
            )

    # 2. Audit insertions
    for obj in session.new:
        if not hasattr(obj, "__tablename__"):
            continue
        tbl = obj.__tablename__
        if tbl in (
            "ctms_audit_logs",
            "tmf_audit_logs",
            "quality_audit_logs",
            "ctms_audit_ledger_seals",
            "tmf_audit_ledger_seals",
            "quality_audit_ledger_seals",
        ):
            continue

        if (
            tbl.startswith("ctms_")
            or tbl.startswith("tmf_")
            or tbl.startswith("quality_")
        ):
            # Enforce user context and change justification
            obj_user_id = user_id
            if not obj_user_id or obj_user_id == "system":
                if hasattr(obj, "created_by") and getattr(obj, "created_by"):
                    obj_user_id = getattr(obj, "created_by")

            obj_change_reason = change_reason
            if not obj_change_reason or obj_change_reason in (
                "system_operation",
                "default_reason",
                "",
            ):
                if hasattr(obj, "reason_for_change") and getattr(
                    obj, "reason_for_change"
                ):
                    obj_change_reason = getattr(obj, "reason_for_change")

            if not obj_user_id:
                raise ValueError(
                    "A valid user context is required to modify clinical trial models."
                )
            if not obj_change_reason or obj_change_reason in (
                "system_operation",
                "default_reason",
                "",
            ):
                raise ValueError(
                    "A valid change justification is required to modify clinical trial models."
                )

            pk = get_primary_key(obj)
            action = "INSERT"
            details = f"Created {obj.__class__.__name__} with ID {pk}."

            if tbl.startswith("ctms_") and CTMSAuditLog is not None:
                audit_logs_to_add.append(
                    CTMSAuditLog(
                        user_id=obj_user_id,
                        user_role=user_roles or "system",
                        action=action,
                        details=details,
                    )
                )
            elif tbl.startswith("tmf_") and TMFAuditLog is not None:
                doc_id = getattr(obj, "id", None) if tbl == "tmf_documents" else None
                audit_logs_to_add.append(
                    TMFAuditLog(
                        user_id=obj_user_id,
                        user_role=user_roles or "system",
                        action=action,
                        document_id=doc_id,
                        details=details,
                    )
                )
            elif tbl.startswith("quality_") and QualityAuditLog is not None:
                audit_logs_to_add.append(
                    QualityAuditLog(
                        user_id=obj_user_id,
                        user_role=user_roles or "system",
                        action=action,
                        details=details,
                        record_id=pk,
                        change_reason=obj_change_reason,
                    )
                )

    # 3. Audit updates
    for obj in session.dirty:
        if not hasattr(obj, "__tablename__"):
            continue
        tbl = obj.__tablename__
        if tbl in (
            "ctms_audit_logs",
            "tmf_audit_logs",
            "quality_audit_logs",
            "ctms_audit_ledger_seals",
            "tmf_audit_ledger_seals",
            "quality_audit_ledger_seals",
        ):
            continue

        # If it's not actually modified, skip
        from sqlalchemy import inspect

        try:
            is_mod = session.is_modified(obj, include_collections=False)
        except Exception:
            is_mod = True
        if not is_mod:
            continue

        if (
            tbl.startswith("ctms_")
            or tbl.startswith("tmf_")
            or tbl.startswith("quality_")
        ):
            # Enforce user context and change justification
            obj_user_id = user_id
            if not obj_user_id or obj_user_id == "system":
                if hasattr(obj, "created_by") and getattr(obj, "created_by"):
                    obj_user_id = getattr(obj, "created_by")

            obj_change_reason = change_reason
            if not obj_change_reason or obj_change_reason in (
                "system_operation",
                "default_reason",
                "",
            ):
                if hasattr(obj, "reason_for_change") and getattr(
                    obj, "reason_for_change"
                ):
                    obj_change_reason = getattr(obj, "reason_for_change")

            if not obj_user_id:
                raise ValueError(
                    "A valid user context is required to modify clinical trial models."
                )
            if not obj_change_reason or obj_change_reason in (
                "system_operation",
                "default_reason",
                "",
            ):
                raise ValueError(
                    "A valid change justification is required to modify clinical trial models."
                )

            # Increment version index automatically
            if hasattr(obj, "version_index"):
                from sqlalchemy.orm.attributes import get_history

                history_version = get_history(obj, "version_index")
                if not history_version.has_changes():
                    obj.version_index += 1

            pk = get_primary_key(obj)
            action = "UPDATE"
            if hasattr(obj, "is_deleted") and getattr(obj, "is_deleted") is True:
                action = "DELETE"
            elif hasattr(obj, "status") and getattr(obj, "status") == "DELETED":
                action = "DELETE"

            # Inspect modified columns
            changed_attrs = []
            from sqlalchemy.orm.attributes import get_history

            try:
                insp = inspect(obj)
                for attr in insp.mapper.column_attrs:
                    history = get_history(obj, attr.key)
                    if history.has_changes():
                        old_val = history.deleted[0] if history.deleted else None
                        new_val = (
                            history.added[0]
                            if history.added
                            else getattr(obj, attr.key)
                        )
                        if old_val != new_val:
                            changed_attrs.append(f"{attr.key}: {old_val} -> {new_val}")
            except Exception:
                pass

            details = f"Updated {obj.__class__.__name__} {pk}. Changes: {', '.join(changed_attrs)}"
            if len(details) > 1000:
                details = details[:997] + "..."

            if tbl.startswith("ctms_") and CTMSAuditLog is not None:
                audit_logs_to_add.append(
                    CTMSAuditLog(
                        user_id=obj_user_id,
                        user_role=user_roles or "system",
                        action=action,
                        details=details,
                    )
                )
            elif tbl.startswith("tmf_") and TMFAuditLog is not None:
                doc_id = getattr(obj, "id", None) if tbl == "tmf_documents" else None
                audit_logs_to_add.append(
                    TMFAuditLog(
                        user_id=obj_user_id,
                        user_role=user_roles or "system",
                        action=action,
                        document_id=doc_id,
                        details=details,
                    )
                )
            elif tbl.startswith("quality_") and QualityAuditLog is not None:
                audit_logs_to_add.append(
                    QualityAuditLog(
                        user_id=obj_user_id,
                        user_role=user_roles or "system",
                        action=action,
                        details=details,
                        record_id=pk,
                        change_reason=obj_change_reason,
                    )
                )

    if audit_logs_to_add:
        session.add_all(audit_logs_to_add)
