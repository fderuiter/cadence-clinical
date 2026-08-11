"""add_integration_outbox

Revision ID: 6f491802984e
Revises: f7ebdb42c09c
Create Date: 2026-08-11 22:53:56.393611

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '6f491802984e'
down_revision: Union[str, None] = 'f7ebdb42c09c'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "integration_outbox",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("event_type", sa.String(length=255), nullable=False),
        sa.Column("payload", sa.JSON(), nullable=False),
        sa.Column("status", sa.String(length=50), nullable=False, server_default="PENDING"),
        sa.Column("attempts", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("last_error", sa.String(), nullable=True),
        sa.Column("next_retry_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("completed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("retry_eligible", sa.Boolean(), nullable=False, server_default="true"),
        sa.Column("correlation_id", sa.String(length=255), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        sa.Column("created_by", sa.String(length=255), nullable=False),
        sa.Column("reason_for_change", sa.String(length=1000), nullable=False),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        op.f("ix_integration_outbox_event_type"),
        "integration_outbox",
        ["event_type"],
        unique=False,
    )
    op.create_index(
        op.f("ix_integration_outbox_status"),
        "integration_outbox",
        ["status"],
        unique=False,
    )
    op.create_index(
        op.f("ix_integration_outbox_correlation_id"),
        "integration_outbox",
        ["correlation_id"],
        unique=True,
    )


def downgrade() -> None:
    op.drop_index(op.f("ix_integration_outbox_correlation_id"), table_name="integration_outbox")
    op.drop_index(op.f("ix_integration_outbox_status"), table_name="integration_outbox")
    op.drop_index(op.f("ix_integration_outbox_event_type"), table_name="integration_outbox")
    op.drop_table("integration_outbox")

