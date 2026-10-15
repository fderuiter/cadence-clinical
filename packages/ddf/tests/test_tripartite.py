"""Unit tests for Tripartite eCRF Schema models, UAT Simulation, and Patient Migration Matrix.

@req:PRD-CRF-001
@req:PRD-CRF-004
@req:PRD-CRF-009
@req:PRD-CRF-012
@req:PRD-SYS-001
"""



from packages.ddf.tripartite import (
    AstEditCheck,
    CdiscMetadata,
    ClinicalDataType,
    ConstraintDefinition,
    DataSchema,
    FieldWidgetConfig,
    GridSection,
    LogicSchema,
    MigrationDirective,
    MigrationMode,
    PatientMigrationMatrix,
    TripartiteFormDefinition,
    UiSchema,
    VariableDefinition,
    WidgetType,
    execute_patient_migration,
    generate_protocol_synthetic_control_arm,
    generate_synthetic_payloads,
)
from packages.engine_core.rules.evaluator import RuleEvaluator


def test_tripartite_schema_instantiation() -> None:
    """Verifies that TripartiteFormDefinition, DataSchema, UiSchema, and LogicSchema models instantiate and serialize cleanly.

    @req:PRD-CRF-001
    @req:PRD-CRF-004
    """
    sys_var = VariableDefinition(
        id="SYSBP",
        name="Systolic Blood Pressure",
        data_type=ClinicalDataType.INTEGER,
        cdisc=CdiscMetadata(domain="VS", variable_name="VSORRES", oid="IT.VS.SYSBP", ucum_unit="mm[Hg]"),
        constraints=ConstraintDefinition(required=True, min_value=70.0, max_value=210.0),
    )
    dia_var = VariableDefinition(
        id="DIABP",
        name="Diastolic Blood Pressure",
        data_type=ClinicalDataType.INTEGER,
        cdisc=CdiscMetadata(domain="VS", variable_name="VSORRES", oid="IT.VS.DIABP", ucum_unit="mm[Hg]"),
        constraints=ConstraintDefinition(required=True, min_value=40.0, max_value=130.0),
    )

    data_schema = DataSchema(
        form_id="FORM-VS-01",
        name="Vital Signs eCRF",
        cdisc_form_oid="FORM.VS",
        variables=[sys_var, dia_var],
    )

    ui_schema = UiSchema(
        form_id="FORM-VS-01",
        sections=[
            GridSection(
                id="SEC-VS-MAIN",
                title="Vital Signs Data Entry",
                columns=12,
                widgets=[
                    FieldWidgetConfig(variable_id="SYSBP", widget=WidgetType.NUMBER_INPUT, grid_column_span=6),
                    FieldWidgetConfig(variable_id="DIABP", widget=WidgetType.NUMBER_INPUT, grid_column_span=6),
                ],
            )
        ],
    )

    edit_check = AstEditCheck(
        id="RULE-VS-001",
        name="Systolic Greater Than Diastolic Check",
        ast_expression={
            "<=": [
                {"field_ref": {"field_id": "SYSBP"}},
                {"field_ref": {"field_id": "DIABP"}},
            ]
        },
        severity="HARD",
        query_message="Systolic blood pressure must be strictly greater than diastolic blood pressure.",
        target_variable_ids=["SYSBP", "DIABP"],
    )

    logic_schema = LogicSchema(
        form_id="FORM-VS-01",
        edit_checks=[edit_check],
    )

    form_def = TripartiteFormDefinition(
        form_id="FORM-VS-01",
        protocol_id="PROT-CARDIO-101",
        version_index=1,
        version_tag="1.0.0",
        data_schema=data_schema,
        ui_schema=ui_schema,
        logic_schema=logic_schema,
        reason_for_change="Initial Vital Signs eCRF baseline release",
    )

    # Test JSON serialization & deserialization
    json_str = form_def.model_dump_json()
    assert "FORM-VS-01" in json_str
    assert "SYSBP" in json_str

    deserialized = TripartiteFormDefinition.model_validate_json(json_str)
    assert deserialized.form_id == "FORM-VS-01"
    assert deserialized.data_schema.variables[0].id == "SYSBP"
    assert deserialized.logic_schema.edit_checks[0].id == "RULE-VS-001"


def test_ai_gateway_synthesizes_500_boundary_payloads() -> None:
    """Verifies that 500 boundary payloads are synthesized targeting rule boundaries and nulls.

    @req:PRD-CRF-009
    """
    data_schema = DataSchema(
        form_id="FORM-LAB-01",
        name="Laboratory Panel eCRF",
        cdisc_form_oid="FORM.LB",
        variables=[
            VariableDefinition(
                id="ALT",
                name="Alanine Aminotransferase",
                data_type=ClinicalDataType.FLOAT,
                cdisc=CdiscMetadata(domain="LB", variable_name="LBORRES", oid="IT.LB.ALT", ucum_unit="U/L"),
                constraints=ConstraintDefinition(required=True, min_value=5.0, max_value=45.0),
            ),
            VariableDefinition(
                id="AST",
                name="Aspartate Aminotransferase",
                data_type=ClinicalDataType.FLOAT,
                cdisc=CdiscMetadata(domain="LB", variable_name="LBORRES", oid="IT.LB.AST", ucum_unit="U/L"),
                constraints=ConstraintDefinition(required=True, min_value=5.0, max_value=40.0),
            ),
        ],
    )

    payloads = generate_synthetic_payloads(data_schema, count=500)
    assert len(payloads) == 500
    assert payloads[0].payload_id == "payload-001"
    assert payloads[499].payload_id == "payload-500"

    # Verify boundary targeting exists (below min, min, max, above max, nulls)
    scenarios = [p.test_scenario for p in payloads]
    assert any("Min Value" in s for s in scenarios)
    assert any("Max Value" in s for s in scenarios)
    assert any("Null" in s for s in scenarios)


def test_uat_simulation_and_evidence_report() -> None:
    """Verifies evaluating 500 payloads achieves complete branch coverage and produces a 21 CFR Part 11 evidence report.

    @req:PRD-CRF-009
    @req:PRD-SYS-001
    """
    sys_var = VariableDefinition(
        id="SYSBP",
        name="Systolic BP",
        data_type=ClinicalDataType.INTEGER,
        cdisc=CdiscMetadata(domain="VS", variable_name="VSORRES", oid="IT.VS.SYSBP"),
        constraints=ConstraintDefinition(required=True, min_value=70.0, max_value=200.0),
    )
    data_schema = DataSchema(form_id="FORM-VS-01", name="VS Form", cdisc_form_oid="FORM.VS", variables=[sys_var])

    edit_check = AstEditCheck(
        id="RULE-VS-SYS",
        name="Systolic Limit Check",
        ast_expression={"<": [{"var": "SYSBP"}, 70]},
        severity="HARD",
        query_message="Systolic BP is below valid threshold",
        target_variable_ids=["SYSBP"],
    )
    logic_schema = LogicSchema(form_id="FORM-VS-01", edit_checks=[edit_check])
    form_def = TripartiteFormDefinition(
        form_id="FORM-VS-01",
        protocol_id="PROT-101",
        data_schema=data_schema,
        ui_schema=UiSchema(form_id="FORM-VS-01"),
        logic_schema=logic_schema,
    )

    payloads = generate_synthetic_payloads(form_def, count=500)
    evaluator = RuleEvaluator()
    report = evaluator.evaluate_payloads(
        form_id="FORM-VS-01",
        protocol_id="PROT-101",
        rules=[edit_check],
        payloads=payloads,
    )

    assert report.total_scenarios_evaluated == 500
    assert report.scenarios_passed + report.scenarios_failed == 500
    assert report.rule_coverage_percentage == 100.0
    assert report.gxp_compliance_verified is True
    assert len(report.sha256_evidence_digest) == 64


def test_patient_migration_matrix_directives() -> None:
    """Verifies executing protocol amendment in-flight data routing via PatientMigrationMatrix directives.

    @req:PRD-CRF-012
    """
    matrix = PatientMigrationMatrix(
        protocol_id="PROT-101",
        from_version_tag="1.0.0",
        to_version_tag="2.0.0",
        directives=[
            MigrationDirective(
                variable_id="NEW_LAB_VAL",
                action="ADD",
                migration_mode=MigrationMode.RETROSPECTIVE,
                backfill_query_message="Retrospective lab parameter missing from patient encounter",
            ),
            MigrationDirective(
                variable_id="OLD_DEPRECATED_VAL",
                action="REMOVE",
                migration_mode=MigrationMode.PROSPECTIVE,
            ),
        ],
    )

    patient_data = {
        "SYSBP": 120,
        "OLD_DEPRECATED_VAL": "legacy_val",
    }

    result = execute_patient_migration(matrix, patient_data, is_retrospective=True)
    assert result["protocol_id"] == "PROT-101"
    assert "NEW_LAB_VAL" in result["queries_raised"][0]
    assert "_archived_OLD_DEPRECATED_VAL" in result["patient_data"]
    assert result["patient_data"]["_archived_OLD_DEPRECATED_VAL"] == "legacy_val"
    assert result["is_compliant"] is False  # Raised query for missing retrospective field


def test_generate_protocol_synthetic_control_arm() -> None:
    """Verifies generating full protocol-level synthetic control arms beyond eCRF form boundary testing.

    @req:PRD-SYS-051
    """
    arm = generate_protocol_synthetic_control_arm(
        protocol_id="PROT-ONCO-202",
        rwe_cohort_profile_id="RWE-NSCLC-01",
        target_sample_size=100,
        caliper=0.20,
        seed=123,
    )

    assert arm.protocol_id == "PROT-ONCO-202"
    assert arm.arm_id == "ARM-PROT-ONCO-202-CONTROL"
    assert arm.target_sample_size == 100
    assert arm.matched_sample_size == 100
    assert arm.match_rate_percentage == 100.0
    assert len(arm.synthetic_patient_profiles) == 100

    first_pat = arm.synthetic_patient_profiles[0]
    assert first_pat.patient_id == "SYNTH-PAT-00001"
    assert first_pat.is_matched is True
    assert "DM" in first_pat.domain_observations
    assert "VS" in first_pat.domain_observations
    assert "LB" in first_pat.domain_observations
    assert "AE" in first_pat.domain_observations
    assert "Age_SMD" in arm.covariate_balance

