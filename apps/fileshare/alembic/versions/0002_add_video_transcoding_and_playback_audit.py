"""Add video transcoding fields and playback audit logs table

Revision ID: 0002_add_video_transcoding_and_playback_audit
Revises: 0001_initial_fileshare_baseline
Create Date: 2026-08-22 14:00:00.000000  # deid-ignore

"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

# revision identifiers, used by Alembic.
revision: str = (
    "0002_add_video_transcoding_and_playback_audit"  # pragma: allowlist secret
)
down_revision: str | None = "0001_initial_fileshare_baseline"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # 1. Add transcoding columns to file_records table
    op.add_column(
        "file_records",
        sa.Column(
            "transcode_status",
            sa.String(length=50),
            nullable=False,
            server_default="NOT_REQUIRED",
        ),
    )
    op.add_column(
        "file_records",
        sa.Column("hls_manifest_key", sa.String(length=1000), nullable=True),
    )
    op.add_column(
        "file_records",
        sa.Column("transcode_error", sa.String(length=2000), nullable=True),
    )
    op.create_index(
        op.f("ix_file_records_transcode_status"),
        "file_records",
        ["transcode_status"],
        unique=False,
    )

    # 2. Create playback_audit_logs table
    op.create_table(
        "playback_audit_logs",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("file_record_id", sa.String(length=36), nullable=False),
        sa.Column("event", sa.String(length=50), nullable=False),
        sa.Column(
            "source", sa.String(length=50), nullable=False, server_default="internal"
        ),
        sa.Column("session_id", sa.String(length=255), nullable=False),
        sa.Column("user_id", sa.String(length=255), nullable=False),
        sa.Column(
            "timestamp", sa.DateTime(), nullable=False, server_default=sa.func.now()
        ),
        sa.Column(
            "created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()
        ),
        sa.Column("reason_for_change", sa.String(length=1000), nullable=False),
        sa.ForeignKeyConstraint(
            ["file_record_id"], ["file_records.id"], ondelete="CASCADE"
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        op.f("ix_playback_audit_logs_file_record_id"),
        "playback_audit_logs",
        ["file_record_id"],
        unique=False,
    )
    op.create_index(
        op.f("ix_playback_audit_logs_event"),
        "playback_audit_logs",
        ["event"],
        unique=False,
    )
    op.create_index(
        op.f("ix_playback_audit_logs_session_id"),
        "playback_audit_logs",
        ["session_id"],
        unique=False,
    )
    op.create_index(
        op.f("ix_playback_audit_logs_user_id"),
        "playback_audit_logs",
        ["user_id"],
        unique=False,
    )


def downgrade() -> None:
    op.drop_index(
        op.f("ix_playback_audit_logs_user_id"), table_name="playback_audit_logs"
    )
    op.drop_index(
        op.f("ix_playback_audit_logs_session_id"), table_name="playback_audit_logs"
    )
    op.drop_index(
        op.f("ix_playback_audit_logs_event"), table_name="playback_audit_logs"
    )
    op.drop_index(
        op.f("ix_playback_audit_logs_file_record_id"),
        table_name="playback_audit_logs",
    )
    op.drop_table("playback_audit_logs")

    op.drop_index(op.f("ix_file_records_transcode_status"), table_name="file_records")
    op.drop_column("file_records", "transcode_error")
    op.drop_column("file_records", "hls_manifest_key")
    op.drop_column("file_records", "transcode_status")
