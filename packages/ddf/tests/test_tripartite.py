"""Unit tests for Tripartite JSON Schema Architecture and Visual Metadata IDE Contracts."""

from packages.ddf.tripartite import (
    AstEditCheck,
    CdiscMetadata,
    ClinicalDataType,
    ConstraintDefinition,
    CrossVisitDependency,
    DataSchema,
    DerivedMathRule,
    FieldWidgetConfig,
    GridSection,
    LogicSchema,
    MigrationDirective,
    MigrationMode,
    PatientMigrationMatrix,
    SyntheticPayload,
    TripartiteFormDefinition,
    UatEvidenceReport,
    UiSchema,
    VariableDefinition,
    ViewportMode,
    WidgetType,
)


def test_tripartite_schema_instantiation():
    """Validate construction and serialization of complete Tripartite form definition.

    @req:PRD-CRF-001
    @req:PRD-CRF-004
    """
    data_schema = DataSchema(
        form_id="FORM-VS",
        name="Vital Signs Panel",
        cdisc_form_oid="FORM.VS",
        variables=[
            VariableDefinition(
                id="VS_SYSBP",
                name="Systolic Blood Pressure",
                data_type=ClinicalDataType.INTEGER,
                cdisc=CdiscMetadata(
                    domain="VS",
                    variable_name="SYSBP",
                    oid="IT.VS.SYSBP",
                    ucum_unit="mmHg",
                ),
                constraints=ConstraintDefinition(
                    required=True,
                    min_value=40.0,
                    max_value=250.0,
                ),
            ),
            VariableDefinition(
                id="VS_DIABP",
                name="Diastolic Blood Pressure",
                data_type=ClinicalDataType.INTEGER,
                cdisc=CdiscMetadata(
                    domain="VS",
                    variable_name="DIABP",
                    oid="IT.VS.DIABP",
                    ucum_unit="mmHg",
                ),
                constraints=ConstraintDefinition(
                    required=True,
                    min_value=30.0,
                    max_value=150.0,
                ),
            ),
        ],
    )

    ui_schema = UiSchema(
        form_id="FORM-VS",
        default_viewport=ViewportMode.DESKTOP_EDC,
        sections=[
            GridSection(
                id="SEC-BP",
                title="Blood Pressure Measurements",
                columns=12,
                widgets=[
                    FieldWidgetConfig(
                        variable_id="VS_SYSBP",
                        widget=WidgetType.NUMBER_INPUT,
                        grid_column_span=6,
                        help_text="Measured in sitting position",
                    ),
                    FieldWidgetConfig(
                        variable_id="VS_DIABP",
                        widget=WidgetType.NUMBER_INPUT,
                        grid_column_span=6,
                    ),
                ],
            )
        ],
    )

    logic_schema = LogicSchema(
        form_id="FORM-VS",
        edit_checks=[
            AstEditCheck(
                id="CHK-BP-01",
                name="Diastolic must be lower than Systolic",
                ast_expression={">": [{"var": "VS_SYSBP"}, {"var": "VS_DIABP"}]},
                severity="HARD",
                query_message="Systolic blood pressure must exceed diastolic blood pressure.",
                target_variable_ids=["VS_SYSBP", "VS_DIABP"],
            )
        ],
        derived_rules=[
            DerivedMathRule(
                id="DER-MAP",
                target_variable_id="VS_MAP",
                formula_expression="VS_DIABP + (1/3) * (VS_SYSBP - VS_DIABP)",
                input_variable_ids=["VS_SYSBP", "VS_DIABP"],
            )
        ],
        cross_visit_dependencies=[
            CrossVisitDependency(
                id="XVD-01",
                trigger_visit_id="VISIT-01",
                trigger_variable_id="VS_SYSBP",
                trigger_condition={">=": [{"var": "VS_SYSBP"}, 180]},
                target_visit_ids_to_spawn=["VISIT-UNSCHEDULED-HYPERTENSION"],
            )
        ],
    )

    tripartite_form = TripartiteFormDefinition(
        form_id="FORM-VS",
        protocol_id="PROT-101",
        version_index=1,
        version_tag="1.0.0",
        data_schema=data_schema,
        ui_schema=ui_schema,
        logic_schema=logic_schema,
    )

    assert tripartite_form.data_schema.form_id == "FORM-VS"
    assert len(tripartite_form.data_schema.variables) == 2
    assert tripartite_form.ui_schema.sections[0].widgets[0].grid_column_span == 6
    assert len(tripartite_form.logic_schema.edit_checks) == 1
    assert tripartite_form.logic_schema.derived_rules[0].target_variable_id == "VS_MAP"


def test_patient_migration_matrix_directives():
    """Validate in-flight patient migration matrix prospective and retrospective directives.

    @req:PRD-CRF-012
    """
    matrix = PatientMigrationMatrix(
        protocol_id="PROT-101",
        from_version_tag="1.0.0",
        to_version_tag="2.0.0",
        directives=[
            MigrationDirective(
                variable_id="VS_PULSE_OX",
                action="ADD",
                migration_mode=MigrationMode.PROSPECTIVE,
            ),
            MigrationDirective(
                variable_id="VS_WEIGHT",
                action="MODIFY_CONSTRAINT",
                migration_mode=MigrationMode.RETROSPECTIVE,
                backfill_query_message="Please verify weight for amendment 2.0 recalculations.",
            ),
        ],
    )

    assert matrix.from_version_tag == "1.0.0"
    assert matrix.to_version_tag == "2.0.0"
    assert len(matrix.directives) == 2
    assert matrix.directives[0].migration_mode == MigrationMode.PROSPECTIVE
    assert matrix.directives[1].migration_mode == MigrationMode.RETROSPECTIVE


def test_uat_simulation_and_evidence_report():
    """Validate synthetic UAT payload structure and 21 CFR Part 11 evidence reporting.

    @req:PRD-CRF-009
    @req:PRD-SYS-001
    """
    payload = SyntheticPayload(
        payload_id="SYNTH-001",
        test_scenario="Severe Hypertensive Crisis Boundary",
        data={"VS_SYSBP": 220, "VS_DIABP": 130},
        expected_rule_outcomes={"CHK-BP-01": True},
    )
    assert payload.payload_id == "SYNTH-001"

    report = UatEvidenceReport(
        report_id="UAT-REP-2026-001",
        form_id="FORM-VS",
        protocol_id="PROT-101",
        total_scenarios_evaluated=120,
        scenarios_passed=120,
        scenarios_failed=0,
        rule_coverage_percentage=100.0,
        execution_duration_ms=45.2,
        gxp_compliance_verified=True,
        sha256_evidence_digest="a6b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8",
    )
    assert report.total_scenarios_evaluated == 120
    assert report.rule_coverage_percentage == 100.0
    assert report.gxp_compliance_verified is True
