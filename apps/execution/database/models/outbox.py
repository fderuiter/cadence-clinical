from packages.database import IntegrationOutboxMixin

from .audit import Base


class IntegrationOutbox(Base, IntegrationOutboxMixin):
    """Concrete integration outbox table for the Execution service."""

    __tablename__ = "integration_outbox"
