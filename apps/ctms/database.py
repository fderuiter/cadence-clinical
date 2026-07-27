from apps.ctms.models import CTMSAuditLog
from packages.database import RelationalDatabaseManager, register_audit_hooks


class CTMSDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the CTMS service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="CTMS")


db_manager = CTMSDatabaseManager()


# Register automated audit hooks with optional skip-list bypass
register_audit_hooks(db_manager, CTMSAuditLog, skip_list=[])
