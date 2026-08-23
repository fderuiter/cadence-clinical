"""Database adapter for the Site Payments microservice.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from packages.database import DatabaseSessionDependency, RelationalDatabaseManager

db_manager = RelationalDatabaseManager("payments")
get_db_session = DatabaseSessionDependency(db_manager)
