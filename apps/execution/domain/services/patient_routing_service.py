"""Runtime Dynamic Patient Migration Routing Service.

Applies In-Flight Patient Migration Matrices during protocol amendments:
- Prospective routing: Evaluates new fields only for subjects enrolled after amendment
- Retrospective routing: Applies fields across all active subjects with backfill queries
- Zero database DDL schema migrations or execution downtime
"""

from __future__ import annotations

from datetime import datetime
from typing import Any

from packages.ddf.tripartite import (
    MigrationMode,
    PatientMigrationMatrix,
    TripartiteFormDefinition,
)


class PatientRoutingService:
    """Evaluates dynamic schema projection per subject based on enrollment timestamp."""

    @staticmethod
    def resolve_subject_form_schema(
        subject_id: str,
        subject_enrollment_date: datetime,
        base_form: TripartiteFormDefinition,
        amended_form: TripartiteFormDefinition,
        migration_matrix: PatientMigrationMatrix,
    ) -> dict[str, Any]:
        """Project the active eCRF schema for a subject based on amendment directives."""
        is_post_amendment = subject_enrollment_date >= migration_matrix.effective_date

        if is_post_amendment:
            # Post-amendment subjects always receive the full amended form
            return {
                "subject_id": subject_id,
                "projected_version_tag": amended_form.version_tag,
                "form_definition": amended_form,
                "backfill_queries_required": [],
                "routing_mode": "POST_AMENDMENT_FULL",
            }

        # Pre-amendment subjects evaluate migration directives
        projected_vars = list(base_form.data_schema.variables)
        backfill_queries: list[dict[str, str]] = []

        amended_vars_by_id = {v.id: v for v in amended_form.data_schema.variables}

        for directive in migration_matrix.directives:
            if directive.migration_mode == MigrationMode.RETROSPECTIVE:
                if (
                    directive.action == "ADD"
                    and directive.variable_id in amended_vars_by_id
                ):
                    projected_vars.append(amended_vars_by_id[directive.variable_id])
                    if directive.backfill_query_message:
                        backfill_queries.append(
                            {
                                "variable_id": directive.variable_id,
                                "query_message": directive.backfill_query_message,
                            }
                        )
                elif directive.action == "MODIFY_CONSTRAINT":
                    # Update variable constraint in-place
                    for i, var in enumerate(projected_vars):
                        if var.id == directive.variable_id:
                            projected_vars[i] = amended_vars_by_id[
                                directive.variable_id
                            ]
                            if directive.backfill_query_message:
                                backfill_queries.append(
                                    {
                                        "variable_id": directive.variable_id,
                                        "query_message": directive.backfill_query_message,
                                    }
                                )

        # Build projected data schema
        projected_data_schema = base_form.data_schema.model_copy(
            update={"variables": projected_vars}
        )
        projected_form = base_form.model_copy(
            update={
                "data_schema": projected_data_schema,
                "version_tag": f"{base_form.version_tag}-proj-{amended_form.version_tag}",
            }
        )

        return {
            "subject_id": subject_id,
            "projected_version_tag": projected_form.version_tag,
            "form_definition": projected_form,
            "backfill_queries_required": backfill_queries,
            "routing_mode": "PRE_AMENDMENT_DIRECTIVE_PROJECTION",
        }
