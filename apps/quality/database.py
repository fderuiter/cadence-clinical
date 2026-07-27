from apps.quality.models import QualityAuditLog
from packages.database import RelationalDatabaseManager, register_audit_hooks

db_manager = RelationalDatabaseManager(service_name="Quality")

# Register automated audit hooks with optional skip-list bypass
register_audit_hooks(db_manager, QualityAuditLog, skip_list=[])
