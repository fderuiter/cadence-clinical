"""Initial fileshare baseline migration

Revision ID: 0001_initial_fileshare_baseline
Revises: None
Create Date: 2026-08-21 12:00:00.000000  # deid-ignore

"""

from collections.abc import Sequence

from alembic import op
import sqlalchemy as sa

# revision identifiers, used by Alembic.
revision: str = "0001_initial_fileshare_baseline"  # pragma: allowlist secret
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    # 1. file_records table
    op.create_table(
        "file_records",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("study_id", sa.String(length=255), nullable=False),
        sa.Column("site_id", sa.String(length=255), nullable=True),
        sa.Column("filename", sa.String(length=500), nullable=False),
        sa.Column("mime_type", sa.String(length=255), nullable=False),
        sa.Column("size_bytes", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("object_key", sa.String(length=1000), nullable=False),
        sa.Column("checksum_sha256", sa.String(length=64), nullable=True),
        sa.Column("version_index", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("uploaded_by", sa.String(length=255), nullable=False),
        sa.Column("uploaded_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("is_on_hold", sa.Boolean(), nullable=False, server_default="0"),
        sa.Column("is_deleted", sa.Boolean(), nullable=False, server_default="0"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("created_by", sa.String(length=255), nullable=False),
        sa.Column("reason_for_change", sa.String(length=1000), nullable=False),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(op.f("ix_file_records_study_id"), "file_records", ["study_id"], unique=False)
    op.create_index(op.f("ix_file_records_site_id"), "file_records", ["site_id"], unique=False)
    op.create_index(op.f("ix_file_records_object_key"), "file_records", ["object_key"], unique=True)
    op.create_index(op.f("ix_file_records_checksum_sha256"), "file_records", ["checksum_sha256"], unique=False)
    op.create_index(op.f("ix_file_records_uploaded_by"), "file_records", ["uploaded_by"], unique=False)

    # 2. share_grants table
    op.create_table(
        "share_grants",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("file_record_id", sa.String(length=36), nullable=False),
        sa.Column("granted_to_user_id", sa.String(length=255), nullable=True),
        sa.Column("granted_by_user_id", sa.String(length=255), nullable=False),
        sa.Column("scope", sa.String(length=50), nullable=False, server_default="individual"),
        sa.Column("permission_level", sa.String(length=50), nullable=False, server_default="view"),
        sa.Column("expires_at", sa.DateTime(), nullable=True),
        sa.Column("revoked_at", sa.DateTime(), nullable=True),
        sa.Column("is_deleted", sa.Boolean(), nullable=False, server_default="0"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("created_by", sa.String(length=255), nullable=False),
        sa.Column("reason_for_change", sa.String(length=1000), nullable=False),
        sa.Column("version_index", sa.Integer(), nullable=False, server_default="1"),
        sa.ForeignKeyConstraint(["file_record_id"], ["file_records.id"], ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(op.f("ix_share_grants_file_record_id"), "share_grants", ["file_record_id"], unique=False)
    op.create_index(op.f("ix_share_grants_granted_to_user_id"), "share_grants", ["granted_to_user_id"], unique=False)

    # 3. guest_links table
    op.create_table(
        "guest_links",
        sa.Column("id", sa.String(length=36), nullable=False),
        sa.Column("file_record_id", sa.String(length=36), nullable=False),
        sa.Column("token_hmac", sa.String(length=128), nullable=False),
        sa.Column("expires_at", sa.DateTime(), nullable=False),
        sa.Column("created_by", sa.String(length=255), nullable=False),
        sa.Column("last_accessed_at", sa.DateTime(), nullable=True),
        sa.Column("access_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("revoked_at", sa.DateTime(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.func.now()),
        sa.Column("reason_for_change", sa.String(length=1000), nullable=False),
        sa.Column("version_index", sa.Integer(), nullable=False, server_default="1"),
        sa.ForeignKeyConstraint(["file_record_id"], ["file_records.id"], ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(op.f("ix_guest_links_file_record_id"), "guest_links", ["file_record_id"], unique=False)
    op.create_index(op.f("ix_guest_links_token_hmac"), "guest_links", ["token_hmac"], unique=True)


def downgrade() -> None:
    op.drop_index(op.f("ix_guest_links_token_hmac"), table_name="guest_links")
    op.drop_index(op.f("ix_guest_links_file_record_id"), table_name="guest_links")
    op.drop_table("guest_links")

    op.drop_index(op.f("ix_share_grants_granted_to_user_id"), table_name="share_grants")
    op.drop_index(op.f("ix_share_grants_file_record_id"), table_name="share_grants")
    op.drop_table("share_grants")

    op.drop_index(op.f("ix_file_records_uploaded_by"), table_name="file_records")
    op.drop_index(op.f("ix_file_records_checksum_sha256"), table_name="file_records")
    op.drop_index(op.f("ix_file_records_object_key"), table_name="file_records")
    op.drop_index(op.f("ix_file_records_site_id"), table_name="file_records")
    op.drop_index(op.f("ix_file_records_study_id"), table_name="file_records")
    op.drop_table("file_records")

