"""Add virus and malware scanning fields to file_records table

Revision ID: 0003_add_virus_scanning_fields
Revises: 0002_add_video_transcoding_and_playback_audit
Create Date: 2026-08-22 14:15:00.000000  # deid-ignore

"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

# revision identifiers, used by Alembic.
revision: str = "0003_add_virus_scanning_fields"  # pragma: allowlist secret
down_revision: str | None = "0002_add_video_transcoding_and_playback_audit"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column(
        "file_records",
        sa.Column(
            "status",
            sa.String(length=50),
            nullable=False,
            server_default="PENDING_SCAN",
        ),
    )
    op.add_column(
        "file_records",
        sa.Column(
            "scan_status",
            sa.String(length=50),
            nullable=False,
            server_default="PENDING",
        ),
    )
    op.add_column(
        "file_records",
        sa.Column("scan_engine", sa.String(length=100), nullable=True),
    )
    op.add_column(
        "file_records",
        sa.Column("scan_completed_at", sa.DateTime(), nullable=True),
    )
    op.add_column(
        "file_records",
        sa.Column("scan_threat_name", sa.String(length=255), nullable=True),
    )
    op.create_index(
        op.f("ix_file_records_status"),
        "file_records",
        ["status"],
        unique=False,
    )
    op.create_index(
        op.f("ix_file_records_scan_status"),
        "file_records",
        ["scan_status"],
        unique=False,
    )


def downgrade() -> None:
    op.drop_index(op.f("ix_file_records_scan_status"), table_name="file_records")
    op.drop_index(op.f("ix_file_records_status"), table_name="file_records")
    op.drop_column("file_records", "scan_threat_name")
    op.drop_column("file_records", "scan_completed_at")
    op.drop_column("file_records", "scan_engine")
    op.drop_column("file_records", "scan_status")
    op.drop_column("file_records", "status")
