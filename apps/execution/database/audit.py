from sqlalchemy import event, inspect
from sqlalchemy.orm import Session
from sqlalchemy.orm.attributes import get_history

from apps.execution.ledger import is_safety_freeze_active

from .context import current_change_reason, current_user_id
from .models import AuditedModel, AuditLog


def get_primary_key(obj):
    mapper = inspect(obj).mapper
    pk_cols = mapper.primary_key
    if not pk_cols:
        return "unknown"
    return str(getattr(obj, pk_cols[0].name))


@event.listens_for(Session, "before_flush")
def receive_before_flush(session: Session, flush_context, instances):
    if not session.is_modified:
        return

    if is_safety_freeze_active():
        # Check if there are any non-ledger modifications
        for obj in session.new.union(session.dirty).union(session.deleted):
            if hasattr(obj, "__tablename__") and obj.__tablename__ not in (
                "audit_ledger_blocks",
                "audit_logs",
            ):
                raise RuntimeError(
                    "SAFETY FREEZE ACTIVE: Database is in read-only mode due to integrity breach."
                )

    audit_logs = []
    user_id = current_user_id.get()
    reason = current_change_reason.get()

    # Track Inserts
    for obj in session.new:
        if not hasattr(obj, "__tablename__") or obj.__tablename__ == "audit_logs":
            continue

        new_values = {}
        mapper = inspect(obj).mapper
        for attr in mapper.column_attrs:
            val = getattr(obj, attr.key)
            new_values[attr.key] = val

        audit_logs.append(
            AuditLog(
                table_name=obj.__tablename__,
                record_id=get_primary_key(obj) or "pending",
                action="INSERT",
                user_id=user_id,
                old_values=None,
                new_values=new_values,
                version_index=getattr(obj, "version", 1),
                change_reason=reason,
            )
        )

    # Track Updates
    for obj in session.dirty:
        if not hasattr(obj, "__tablename__") or obj.__tablename__ == "audit_logs":
            continue
        if not session.is_modified(obj, include_collections=False):
            continue

        old_values = {}
        new_values = {}
        mapper = inspect(obj).mapper
        for attr in mapper.column_attrs:
            history = get_history(obj, attr.key)
            if history.has_changes():
                # history.deleted has the previous value, history.added has the new value
                # but only if it changed!
                old_val = (
                    history.deleted[0] if history.deleted else getattr(obj, attr.key)
                )
                new_val = history.added[0] if history.added else getattr(obj, attr.key)

                # Verify that it actually changed
                if old_val != new_val:
                    old_values[attr.key] = old_val
                    new_values[attr.key] = new_val

        if old_values or new_values:
            # Check if this is a soft delete
            action = "UPDATE"
            if (
                getattr(obj, "is_deleted", False) is True
                and old_values.get("is_deleted") is False
            ):
                action = "DELETE"

            # Increment version index
            if hasattr(obj, "version") and "version" not in new_values:
                obj.version += 1
                new_values["version"] = obj.version

            audit_logs.append(
                AuditLog(
                    table_name=obj.__tablename__,
                    record_id=get_primary_key(obj),
                    action=action,
                    user_id=user_id,
                    old_values=old_values,
                    new_values=new_values,
                    version_index=getattr(obj, "version", 1),
                    change_reason=reason,
                )
            )

    # Prevent hard deletions
    for obj in session.deleted:
        if isinstance(obj, AuditedModel):
            raise ValueError(
                f"Hard deletion of {obj.__class__.__name__} is forbidden. Use soft deletes by setting is_deleted=True."
            )

        if not hasattr(obj, "__tablename__") or obj.__tablename__ == "audit_logs":
            continue

        # If it's another non-audited model, capture its deletion? The requirement says "all clinical records must be versioned..."
        # We can just record a DELETE action for it.
        old_values = {}
        mapper = inspect(obj).mapper
        for attr in mapper.column_attrs:
            old_values[attr.key] = getattr(obj, attr.key)

        audit_logs.append(
            AuditLog(
                table_name=obj.__tablename__,
                record_id=get_primary_key(obj),
                action="DELETE",
                user_id=user_id,
                old_values=old_values,
                new_values=None,
                version_index=getattr(obj, "version", 1),
                change_reason=reason,
            )
        )

    if audit_logs:
        session.add_all(audit_logs)
