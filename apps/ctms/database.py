from apps.ctms.models import CTMSAuditLog
from packages.database import RelationalDatabaseManager, register_audit_hooks

db_manager = RelationalDatabaseManager(service_name="CTMS")

# Register automated audit hooks with optional skip-list bypass
register_audit_hooks(db_manager, CTMSAuditLog, skip_list=[])
