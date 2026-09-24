from sqlalchemy import Float, String
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class MigrationRule(AuditedModel):
    """Represents a protocol amendment migration rule.

    Defines non-destructive transitions such as renamed, added, and removed fields between protocol versions.
    """

    __tablename__ = "migration_rules"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    source_version: Mapped[str] = mapped_column(String(50), nullable=False)
    target_version: Mapped[str] = mapped_column(String(50), nullable=False)
    rule_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # "rename", "add", "remove"
    source_field: Mapped[str | None] = mapped_column(String(100), nullable=True)
    target_field: Mapped[str | None] = mapped_column(String(100), nullable=True)
    default_value_string: Mapped[str | None] = mapped_column(String, nullable=True)
    default_value_float: Mapped[float | None] = mapped_column(Float, nullable=True)
