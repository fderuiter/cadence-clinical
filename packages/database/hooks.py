import json
from datetime import datetime
from typing import Any, Optional

from sqlalchemy import event, inspect
from sqlalchemy.orm import Session
from sqlalchemy.orm.attributes import get_history

from packages.database.mixins import AuditMixin
from packages.security.context import (
    current_change_reason,
    current_ip_address,
    current_site_id,
    current_timestamp,
    current_unblinded_access,
    current_user_id,
)


def get_primary_key(obj: Any) -> str:
    try:
        mapper = inspect(obj).mapper
        pk_cols = mapper.primary_key
        if not pk_cols:
            return "unknown"
        return str(getattr(obj, pk_cols[0].name))
    except Exception:
        return "unknown"


def get_app_prefix(cls: Any) -> str:
    module_name = getattr(cls, "__module__", "")
    parts = module_name.split(".")
    if len(parts) >= 2:
        return ".".join(parts[:2])
    return parts[0] if parts else ""


def register_audit_hooks(
    target: Any,
    audit_log_class: Any,
    skip_list: Optional[list[str]] = None,
) -> None:
    """
    Configurable SQLAlchemy session hook factory.
    Automates mutation logging, change tracking, and prevents hard-deletes of clinical data.
    """
    if skip_list is None:
        skip_list = []
    else:
        # Create a copy so we don't mutate the passed list
        skip_list = list(skip_list)

    if hasattr(audit_log_class, "__tablename__"):
        skip_list.append(audit_log_class.__tablename__)

    app_prefix = get_app_prefix(audit_log_class)

    def receive_before_flush(
        session: Session, flush_context: Any, instances: Any
    ) -> None:
        if not session.is_modified:
            # Check for deleted instances even if not modified (in case of only deletes)
            if not session.deleted:
                return

        # 1. Prevent hard deletes of protected clinical data or audit logs
        for obj in session.deleted:
            if get_app_prefix(obj.__class__) != app_prefix:
                continue
            if session.info.get("bypass_delete_protection"):
                continue
            if isinstance(obj, audit_log_class):
                raise ValueError(
                    f"Deletion of {obj.__class__.__name__} is strictly forbidden to comply with 21 CFR Part 11."
                )
            if isinstance(obj, AuditMixin) or hasattr(obj, "version_index"):
                raise ValueError(
                    f"Hard deletion of {obj.__class__.__name__} is forbidden. Use soft deletes or other compliant archival mechanisms."
                )

        # Resolve contextual fields dynamically
        user_id = session.info.get("user_id") or current_user_id.get() or "system"
        user_role = session.info.get("user_role") or "system"
        reason = (
            session.info.get("change_reason")
            or current_change_reason.get()
            or "system_operation"
        )
        ip_address = (
            session.info.get("ip_address") or current_ip_address.get() or "127.0.0.1"
        )
        timestamp = session.info.get("timestamp") or current_timestamp.get()
        if timestamp is None:
            timestamp = datetime.utcnow()

        # Dynamic GxP site-isolation and blinding scope context
        site_id = session.info.get("site_id") or current_site_id.get()
        unblinded_access = (
            session.info.get("unblinded_access")
            if session.info.get("unblinded_access") is not None
            else current_unblinded_access.get()
        )

        audit_logs = []

        # 2. Track Inserts
        for obj in session.new:
            if get_app_prefix(obj.__class__) != app_prefix:
                continue
            if not hasattr(obj, "__tablename__") or obj.__tablename__ in skip_list:
                continue

            new_values = {}
            mapper = inspect(obj).mapper
            for attr in mapper.column_attrs:
                val = getattr(obj, attr.key)
                if isinstance(val, datetime):
                    val = val.isoformat()
                new_values[attr.key] = val

            kwargs = {}
            if hasattr(audit_log_class, "table_name"):
                kwargs["table_name"] = obj.__tablename__
            if hasattr(audit_log_class, "record_id"):
                kwargs["record_id"] = get_primary_key(obj) or "pending"
            if hasattr(audit_log_class, "action"):
                kwargs["action"] = "INSERT"
            if hasattr(audit_log_class, "user_id"):
                kwargs["user_id"] = user_id
            if hasattr(audit_log_class, "user_role"):
                kwargs["user_role"] = user_role
            if hasattr(audit_log_class, "old_values"):
                kwargs["old_values"] = None
            if hasattr(audit_log_class, "new_values"):
                kwargs["new_values"] = new_values
            if hasattr(audit_log_class, "change_reason"):
                kwargs["change_reason"] = reason
            if hasattr(audit_log_class, "timestamp"):
                kwargs["timestamp"] = timestamp
            if hasattr(audit_log_class, "site_id"):
                kwargs["site_id"] = site_id
            if hasattr(audit_log_class, "unblinded_access"):
                kwargs["unblinded_access"] = unblinded_access
            if hasattr(audit_log_class, "ip_address"):
                kwargs["ip_address"] = ip_address
            if hasattr(audit_log_class, "details"):
                kwargs["details"] = json.dumps(
                    {
                        "table_name": obj.__tablename__,
                        "record_id": get_primary_key(obj) or "pending",
                        "action": "INSERT",
                        "old_values": None,
                        "new_values": new_values,
                        "change_reason": reason,
                        "site_id": site_id,
                        "unblinded_access": unblinded_access,
                        "ip_address": ip_address,
                    }
                )

            audit_logs.append(audit_log_class(**kwargs))

        # 3. Track Updates
        for obj in session.dirty:
            if get_app_prefix(obj.__class__) != app_prefix:
                continue
            if not hasattr(obj, "__tablename__") or obj.__tablename__ in skip_list:
                continue
            if not session.is_modified(obj, include_collections=False):
                continue

            old_values = {}
            new_values = {}
            mapper = inspect(obj).mapper
            for attr in mapper.column_attrs:
                history = get_history(obj, attr.key)
                if history.has_changes():
                    old_val = (
                        history.deleted[0]
                        if history.deleted
                        else getattr(obj, attr.key)
                    )
                    new_val = (
                        history.added[0] if history.added else getattr(obj, attr.key)
                    )

                    if old_val != new_val:
                        if isinstance(old_val, datetime):
                            old_val = old_val.isoformat()
                        if isinstance(new_val, datetime):
                            new_val = new_val.isoformat()
                        old_values[attr.key] = old_val
                        new_values[attr.key] = new_val

            if old_values or new_values:
                # Check for soft delete
                action = "UPDATE"
                if (
                    getattr(obj, "is_deleted", False) is True
                    and old_values.get("is_deleted") is False
                ):
                    action = "DELETE"

                # Increment version index
                if hasattr(obj, "version_index") and "version_index" not in new_values:
                    obj.version_index = (obj.version_index or 0) + 1
                    new_values["version_index"] = obj.version_index

                kwargs = {}
                if hasattr(audit_log_class, "table_name"):
                    kwargs["table_name"] = obj.__tablename__
                if hasattr(audit_log_class, "record_id"):
                    kwargs["record_id"] = get_primary_key(obj)
                if hasattr(audit_log_class, "action"):
                    kwargs["action"] = action
                if hasattr(audit_log_class, "user_id"):
                    kwargs["user_id"] = user_id
                if hasattr(audit_log_class, "user_role"):
                    kwargs["user_role"] = user_role
                if hasattr(audit_log_class, "old_values"):
                    kwargs["old_values"] = old_values
                if hasattr(audit_log_class, "new_values"):
                    kwargs["new_values"] = new_values
                if hasattr(audit_log_class, "change_reason"):
                    kwargs["change_reason"] = reason
                if hasattr(audit_log_class, "timestamp"):
                    kwargs["timestamp"] = timestamp
                if hasattr(audit_log_class, "site_id"):
                    kwargs["site_id"] = site_id
                if hasattr(audit_log_class, "unblinded_access"):
                    kwargs["unblinded_access"] = unblinded_access
                if hasattr(audit_log_class, "ip_address"):
                    kwargs["ip_address"] = ip_address
                if hasattr(audit_log_class, "details"):
                    kwargs["details"] = json.dumps(
                        {
                            "table_name": obj.__tablename__,
                            "record_id": get_primary_key(obj),
                            "action": action,
                            "old_values": old_values,
                            "new_values": new_values,
                            "change_reason": reason,
                            "site_id": site_id,
                            "unblinded_access": unblinded_access,
                            "ip_address": ip_address,
                        }
                    )

                audit_logs.append(audit_log_class(**kwargs))

        if audit_logs:
            session.add_all(audit_logs)

    # Register globally on the SQLAlchemy Session class to capture flushes from any session maker
    event.listen(Session, "before_flush", receive_before_flush)


setup_audit_hooks = register_audit_hooks
