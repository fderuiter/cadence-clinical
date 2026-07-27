from apps.etmf.models import TMFAuditLog
from packages.database import RelationalDatabaseManager, register_audit_hooks

db_manager = RelationalDatabaseManager(service_name="eTMF")

# Register automated audit hooks with optional skip-list bypass
register_audit_hooks(db_manager, TMFAuditLog, skip_list=[])
