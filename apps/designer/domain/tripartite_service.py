"""Tripartite eCRF Designer Domain Services.

Implements core domain services for the Visual Metadata IDE:
- TripartiteCompilerService: Validates & compiles Data, UI, and Logic schemas
- SmartBlocksCatalogService: Manages CDISC-native smart component blocks and inheritance
- AmendmentDiffService: Calculates structural diffs and In-Flight Migration Matrices
- UatRunnerService: Executes automated synthetic UAT and compiles 21 CFR Part 11 Evidence Reports
- PublicationAuditService: Enforces cryptographic audit sealing on published form schemas
"""

from __future__ import annotations

import hashlib
import json
import time
from datetime import UTC, datetime
from typing import Any

from packages.ddf.smart_blocks import (
    SmartBlocksCatalogService as SmartBlocksCatalogService,
)
from packages.ddf.tripartite import (
    MigrationDirective,
    MigrationMode,
    PatientMigrationMatrix,
    SyntheticPayload,
    TripartiteFormDefinition,
    UatEvidenceReport,
)
from packages.engine_core.rules.evaluator import RuleEvaluator, parse_json_logic


class TripartiteCompilerService:
    """Compiles and validates Tripartite JSON Form Definitions."""

    @staticmethod
    def compile_and_validate(form_def: TripartiteFormDefinition) -> dict[str, Any]:
        """Validate structural integrity, CDISC bindings, and rule references."""
        warnings: list[str] = []
        variable_ids = {v.id for v in form_def.data_schema.variables}

        # 1. Validate UI Schema variable references
        for section in form_def.ui_schema.sections:
            for widget in section.widgets:
                if widget.variable_id not in variable_ids:
                    warnings.append(
                        f"UI widget references undefined variable_id '{widget.variable_id}'"
                    )

        # 2. Validate Logic Schema variable references
        for check in form_def.logic_schema.edit_checks:
            for target_id in check.target_variable_ids:
                if target_id not in variable_ids:
                    warnings.append(
                        f"Edit check '{check.id}' targets undefined variable_id '{target_id}'"
                    )

        for derived in form_def.logic_schema.derived_rules:
            if derived.target_variable_id not in variable_ids:
                warnings.append(
                    f"Derived rule '{derived.id}' targets undefined variable_id '{derived.target_variable_id}'"
                )

        # 3. Compute deterministic schema SHA-256 digest
        serialized = form_def.model_dump_json()
        form_hash = hashlib.sha256(serialized.encode("utf-8")).hexdigest()

        return {
            "status": "VALID" if not warnings else "VALID_WITH_WARNINGS",
            "form_id": form_def.form_id,
            "version_tag": form_def.version_tag,
            "form_hash": f"sha256:{form_hash}",
            "variables_count": len(variable_ids),
            "edit_checks_count": len(form_def.logic_schema.edit_checks),
            "warnings": warnings,
        }


class AmendmentDiffService:
    """Calculates structural diffs and In-Flight Patient Migration Matrices."""

    @staticmethod
    def calculate_diff(
        base_form: TripartiteFormDefinition,
        amended_form: TripartiteFormDefinition,
    ) -> PatientMigrationMatrix:
        """Compare two versions of a form and generate migration directives."""
        base_vars = {v.id: v for v in base_form.data_schema.variables}
        amended_vars = {v.id: v for v in amended_form.data_schema.variables}

        directives: list[MigrationDirective] = []

        # 1. Detect Added Variables
        for var_id, var_def in amended_vars.items():
            if var_id not in base_vars:
                directives.append(
                    MigrationDirective(
                        variable_id=var_id,
                        action="ADD",
                        migration_mode=MigrationMode.PROSPECTIVE,
                        backfill_query_message=None,
                    )
                )

        # 2. Detect Removed Variables
        for var_id in base_vars:
            if var_id not in amended_vars:
                directives.append(
                    MigrationDirective(
                        variable_id=var_id,
                        action="REMOVE",
                        migration_mode=MigrationMode.PROSPECTIVE,
                    )
                )

        # 3. Detect Modified Constraints
        for var_id, a_var in amended_vars.items():
            if var_id in base_vars:
                b_var = base_vars[var_id]
                if a_var.constraints != b_var.constraints:
                    directives.append(
                        MigrationDirective(
                            variable_id=var_id,
                            action="MODIFY_CONSTRAINT",
                            migration_mode=MigrationMode.RETROSPECTIVE,
                            backfill_query_message=f"Constraint updated for {a_var.name}. Verify historical values.",
                        )
                    )

        return PatientMigrationMatrix(
            protocol_id=amended_form.protocol_id,
            from_version_tag=base_form.version_tag,
            to_version_tag=amended_form.version_tag,
            directives=directives,
            effective_date=datetime.now(UTC),
        )


class UatRunnerService:
    """Executes automated synthetic UAT simulations and compiles 21 CFR Part 11 Evidence Reports."""

    @staticmethod
    def execute_uat_suite(
        form_def: TripartiteFormDefinition,
        payloads: list[SyntheticPayload],
    ) -> UatEvidenceReport:
        """Run synthetic payloads through the rules evaluator and record pass/fail evidence."""
        start_time = time.perf_counter()
        passed_count = 0
        failed_count = 0
        rules_evaluated: set[str] = set()

        evaluator = RuleEvaluator()

        for scenario in payloads:
            scenario_passed = True
            for rule in form_def.logic_schema.edit_checks:
                rules_evaluated.add(rule.id)
                # Parse JSONLogic AST if dict
                node = (
                    parse_json_logic(rule.ast_expression)
                    if isinstance(rule.ast_expression, dict)
                    else rule.ast_expression
                )
                # Evaluate rule expression against scenario data
                result = evaluator.evaluate(node, scenario.data)
                fired = bool(result.value) if hasattr(result, "value") else bool(result)

                expected_fired = scenario.expected_rule_outcomes.get(rule.id, False)
                if fired != expected_fired:
                    scenario_passed = False

            if scenario_passed:
                passed_count += 1
            else:
                failed_count += 1

        total_rules = len(form_def.logic_schema.edit_checks)
        coverage_pct = (
            (len(rules_evaluated) / total_rules * 100.0) if total_rules > 0 else 100.0
        )
        duration_ms = (time.perf_counter() - start_time) * 1000.0

        # Construct deterministic 21 CFR Part 11 evidence digest
        digest_source = f"{form_def.form_id}:{form_def.version_tag}:{passed_count}:{failed_count}:{coverage_pct}"
        sha256_digest = hashlib.sha256(digest_source.encode("utf-8")).hexdigest()

        return UatEvidenceReport(
            report_id=f"UAT-{form_def.form_id}-{int(time.time())}",
            form_id=form_def.form_id,
            protocol_id=form_def.protocol_id,
            executed_at=datetime.now(UTC),
            total_scenarios_evaluated=len(payloads),
            scenarios_passed=passed_count,
            scenarios_failed=failed_count,
            rule_coverage_percentage=coverage_pct,
            execution_duration_ms=round(duration_ms, 2),
            gxp_compliance_verified=failed_count == 0,
            sha256_evidence_digest=sha256_digest,
        )


class PublicationAuditService:
    """Manages 21 CFR Part 11 cryptographic sealing on form publication."""

    @staticmethod
    def seal_publication(
        form_def: TripartiteFormDefinition,
        published_by: str,
        reason_for_change: str,
        step_up_signature_token: str,
    ) -> dict[str, Any]:
        """Sign and seal published form definition into immutable audit envelope."""
        if not reason_for_change.strip():
            raise ValueError("21 CFR Part 11 requires a non-empty reason_for_change.")
        if not step_up_signature_token:
            raise ValueError(
                "Step-up electronic signature token required for publication."
            )

        payload_to_sign = {
            "form_id": form_def.form_id,
            "protocol_id": form_def.protocol_id,
            "version_index": form_def.version_index,
            "version_tag": form_def.version_tag,
            "published_at": datetime.now(UTC).isoformat(),
            "published_by": published_by,
            "reason_for_change": reason_for_change,
            "signature_token_digest": hashlib.sha256(
                step_up_signature_token.encode()
            ).hexdigest(),
        }

        serialized = json.dumps(payload_to_sign, sort_keys=True)
        seal_hash = hashlib.sha256(serialized.encode("utf-8")).hexdigest()

        return {
            "status": "PUBLISHED_AND_SEALED",
            "form_id": form_def.form_id,
            "version_tag": form_def.version_tag,
            "sha256_audit_seal": f"sha256:{seal_hash}",
            "publication_manifest": payload_to_sign,
        }
