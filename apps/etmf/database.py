from packages.database import RelationalDatabaseManager, register_audit_hooks


class ETMFDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the eTMF service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="eTMF")


db_manager = ETMFDatabaseManager()

from apps.etmf.models import TMFAuditLog

# Register automated audit hooks with optional skip-list bypass
register_audit_hooks(db_manager, TMFAuditLog, skip_list=[])
