from apps.quality.models import QualityAuditLog
from packages.database import RelationalDatabaseManager, register_audit_hooks


class QualityDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the Quality & CAPA service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="Quality")


db_manager = QualityDatabaseManager()


# Register automated audit hooks with optional skip-list bypass
register_audit_hooks(db_manager, QualityAuditLog, skip_list=[])
