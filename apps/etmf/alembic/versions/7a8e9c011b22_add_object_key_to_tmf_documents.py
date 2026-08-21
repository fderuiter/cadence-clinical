"""add_object_key_to_tmf_documents

Revision ID: 7a8e9c011b22
Revises: 6f491802984e
Create Date: 2026-08-21 12:00:00.000000

"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

# revision identifiers, used by Alembic.
revision: str = "7a8e9c011b22"  # pragma: allowlist secret
down_revision: str | None = "6f491802984e"  # pragma: allowlist secret
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("tmf_documents") as batch_op:
        batch_op.add_column(
            sa.Column("object_key", sa.String(length=500), nullable=True),
        )
        batch_op.create_index(
            batch_op.f("ix_tmf_documents_object_key"),
            ["object_key"],
            unique=False,
        )
        # Alter content column to nullable for post-migration state
        batch_op.alter_column(
            "content",
            existing_type=sa.String(),
            nullable=True,
        )


def downgrade() -> None:
    with op.batch_alter_table("tmf_documents") as batch_op:
        batch_op.alter_column(
            "content",
            existing_type=sa.String(),
            nullable=False,
        )
        batch_op.drop_index(batch_op.f("ix_tmf_documents_object_key"))
        batch_op.drop_column("object_key")
