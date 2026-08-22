"""Add hold_reason column to file_records and create file_audit_logs table

Revision ID: 0004_add_hold_reason_and_audit_logs
Revises: 0003_add_virus_scanning_fields
Create Date: 2026-08-22 14:30:00.000000  # deid-ignore

"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

# revision identifiers, used by Alembic.
revision: str = "0004_add_hold_reason_and_audit_logs"  # pragma: allowlist secret
down_revision: str | None = "0003_add_virus_scanning_fields"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # 1. Add hold_reason column to file_records
    op.add_column(
        "file_records",
        sa.Column("hold_reason", sa.String(length=1000), nullable=True),
    )

    # 2. Create file_audit_logs table
    op.create_table(
        "file_audit_logs",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("file_record_id", sa.String(length=36), nullable=False),
        sa.Column("action", sa.String(length=50), nullable=False),
        sa.Column("user_id", sa.String(length=255), nullable=False),
        sa.Column(
            "timestamp", sa.DateTime(), nullable=False, server_default=sa.func.now()
        ),
        sa.Column("reason_for_change", sa.String(length=1000), nullable=False),
        sa.Column("detail", sa.JSON(), nullable=True),
        sa.ForeignKeyConstraint(
            ["file_record_id"], ["file_records.id"], ondelete="CASCADE"
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        op.f("ix_file_audit_logs_file_record_id"),
        "file_audit_logs",
        ["file_record_id"],
        unique=False,
    )
    op.create_index(
        op.f("ix_file_audit_logs_action"),
        "file_audit_logs",
        ["action"],
        unique=False,
    )
    op.create_index(
        op.f("ix_file_audit_logs_user_id"),
        "file_audit_logs",
        ["user_id"],
        unique=False,
    )
    op.create_index(
        op.f("ix_file_audit_logs_timestamp"),
        "file_audit_logs",
        ["timestamp"],
        unique=False,
    )


def downgrade() -> None:
    op.drop_index(op.f("ix_file_audit_logs_timestamp"), table_name="file_audit_logs")
    op.drop_index(op.f("ix_file_audit_logs_user_id"), table_name="file_audit_logs")
    op.drop_index(op.f("ix_file_audit_logs_action"), table_name="file_audit_logs")
    op.drop_index(
        op.f("ix_file_audit_logs_file_record_id"), table_name="file_audit_logs"
    )
    op.drop_table("file_audit_logs")

    op.drop_column("file_records", "hold_reason")
