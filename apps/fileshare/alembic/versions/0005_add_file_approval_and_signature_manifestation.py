"""Add approval_status, signature_manifestation, and approval tracking columns to file_records

Revision ID: 0005_add_file_approval_and_signature_manifestation
Revises: 0004_add_hold_reason_and_audit_logs
Create Date: 2026-08-22 14:35:00.000000  # deid-ignore

"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

# revision identifiers, used by Alembic.
revision: str = (
    "0005_add_file_approval_and_signature_manifestation"  # pragma: allowlist secret
)
down_revision: str | None = "0004_add_hold_reason_and_audit_logs"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # Add approval status and electronic signature manifestation fields to file_records
    op.add_column(
        "file_records",
        sa.Column(
            "approval_status",
            sa.String(length=50),
            server_default="PENDING_APPROVAL",
            nullable=False,
        ),
    )
    op.add_column(
        "file_records",
        sa.Column("signature_manifestation", sa.JSON(), nullable=True),
    )
    op.add_column(
        "file_records",
        sa.Column("approved_by", sa.String(length=255), nullable=True),
    )
    op.add_column(
        "file_records",
        sa.Column("approved_at", sa.DateTime(), nullable=True),
    )
    op.add_column(
        "file_records",
        sa.Column("approval_reason", sa.String(length=1000), nullable=True),
    )

    op.create_index(
        op.f("ix_file_records_approval_status"),
        "file_records",
        ["approval_status"],
        unique=False,
    )
    op.create_index(
        op.f("ix_file_records_approved_by"),
        "file_records",
        ["approved_by"],
        unique=False,
    )


def downgrade() -> None:
    op.drop_index(op.f("ix_file_records_approved_by"), table_name="file_records")
    op.drop_index(op.f("ix_file_records_approval_status"), table_name="file_records")

    op.drop_column("file_records", "approval_reason")
    op.drop_column("file_records", "approved_at")
    op.drop_column("file_records", "approved_by")
    op.drop_column("file_records", "signature_manifestation")
    op.drop_column("file_records", "approval_status")
