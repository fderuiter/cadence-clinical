"""Add pdf_rendition_key, conversion_status, and conversion_error columns to file_records

Revision ID: 0006_add_auto_format_conversion_fields
Revises: 0005_add_file_approval_and_signature_manifestation
Create Date: 2026-08-22 15:00:00.000000  # deid-ignore

"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

# revision identifiers, used by Alembic.
revision: str = "0006_add_auto_format_conversion_fields"  # pragma: allowlist secret
down_revision: str | None = "0005_add_file_approval_and_signature_manifestation"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # Add auto format conversion fields to file_records
    op.add_column(
        "file_records",
        sa.Column("pdf_rendition_key", sa.String(length=1000), nullable=True),
    )
    op.add_column(
        "file_records",
        sa.Column(
            "conversion_status",
            sa.String(length=50),
            server_default="NOT_REQUIRED",
            nullable=False,
        ),
    )
    op.add_column(
        "file_records",
        sa.Column("conversion_error", sa.String(length=2000), nullable=True),
    )

    op.create_index(
        op.f("ix_file_records_conversion_status"),
        "file_records",
        ["conversion_status"],
        unique=False,
    )


def downgrade() -> None:
    op.drop_index(op.f("ix_file_records_conversion_status"), table_name="file_records")

    op.drop_column("file_records", "conversion_error")
    op.drop_column("file_records", "conversion_status")
    op.drop_column("file_records", "pdf_rendition_key")
