"""Unit tests for Tripartite eCRF Designer Domain Services."""

from apps.designer.domain.tripartite_service import (
    AmendmentDiffService,
    PublicationAuditService,
    TripartiteCompilerService,
    UatRunnerService,
)
from packages.ddf.smart_blocks import SmartBlocksCatalogService
from packages.ddf.tripartite import (
    AstEditCheck,
    CdiscMetadata,
    ClinicalDataType,
    ConstraintDefinition,
    DataSchema,
    FieldWidgetConfig,
    GridSection,
    LogicSchema,
    MigrationMode,
    SyntheticPayload,
    TripartiteFormDefinition,
    UiSchema,
    VariableDefinition,
    ViewportMode,
    WidgetType,
)


def _create_sample_form() -> TripartiteFormDefinition:
    """Helper creating a sample Tripartite form."""
    data_schema = DataSchema(
        form_id="FORM-VS",
        name="Vital Signs",
        cdisc_form_oid="FORM.VS",
        variables=[
            VariableDefinition(
                id="VS_SYSBP",
                name="Systolic BP",
                data_type=ClinicalDataType.INTEGER,
                cdisc=CdiscMetadata(oid="IT.VS.SYSBP", ucum_unit="mmHg"),
                constraints=ConstraintDefinition(min_value=50.0, max_value=250.0),
            ),
            VariableDefinition(
                id="VS_DIABP",
                name="Diastolic BP",
                data_type=ClinicalDataType.INTEGER,
                cdisc=CdiscMetadata(oid="IT.VS.DIABP", ucum_unit="mmHg"),
                constraints=ConstraintDefinition(min_value=30.0, max_value=150.0),
            ),
        ],
    )
    ui_schema = UiSchema(
        form_id="FORM-VS",
        default_viewport=ViewportMode.DESKTOP_EDC,
        sections=[
            GridSection(
                id="SEC-1",
                title="Vitals",
                widgets=[
                    FieldWidgetConfig(
                        variable_id="VS_SYSBP", widget=WidgetType.NUMBER_INPUT
                    ),
                    FieldWidgetConfig(
                        variable_id="VS_DIABP", widget=WidgetType.NUMBER_INPUT
                    ),
                ],
            )
        ],
    )
    logic_schema = LogicSchema(
        form_id="FORM-VS",
        edit_checks=[
            AstEditCheck(
                id="CHK-01",
                name="Systolic > Diastolic",
                ast_expression={">": [{"var": "VS_SYSBP"}, {"var": "VS_DIABP"}]},
                severity="HARD",
                query_message="Systolic must exceed diastolic",
                target_variable_ids=["VS_SYSBP", "VS_DIABP"],
            )
        ],
    )
    return TripartiteFormDefinition(
        form_id="FORM-VS",
        protocol_id="PROT-101",
        version_index=1,
        version_tag="1.0.0",
        data_schema=data_schema,
        ui_schema=ui_schema,
        logic_schema=logic_schema,
    )


def test_tripartite_compiler_service():
    """Verify validation and SHA-256 digest calculation for Tripartite form.

    @req:PRD-CRF-001
    @req:PRD-CRF-004
    """
    form = _create_sample_form()
    result = TripartiteCompilerService.compile_and_validate(form)
    assert result["status"] == "VALID"
    assert result["form_id"] == "FORM-VS"
    assert result["form_hash"].startswith("sha256:")
    assert result["variables_count"] == 2
    assert result["edit_checks_count"] == 1


def test_smart_blocks_catalog_and_instantiation():
    """Verify Global Library Smart Blocks catalog and instantiation into Tripartite schemas.

    @req:PRD-CRF-001
    @req:PRD-MDR-001
    """
    catalog = SmartBlocksCatalogService.get_catalog()
    assert len(catalog) >= 2
    assert any(b["block_id"] == "BLOCK-VITAL-SIGNS" for b in catalog)

    vs_form = SmartBlocksCatalogService.instantiate_smart_block(
        "BLOCK-VITAL-SIGNS", "FORM-NEW-VS"
    )
    assert vs_form.form_id == "FORM-NEW-VS"
    assert len(vs_form.data_schema.variables) >= 4
    assert vs_form.data_schema.cdisc_form_oid == "FORM.VS"


def test_amendment_diff_service():
    """Verify structural diff calculation and In-Flight Patient Migration Matrix generation.

    @req:PRD-CRF-012
    """
    base_form = _create_sample_form()
    amended_form = _create_sample_form()

    # Add a new variable to amended form
    new_var = VariableDefinition(
        id="VS_PULSE",
        name="Heart Rate",
        data_type=ClinicalDataType.INTEGER,
        cdisc=CdiscMetadata(oid="IT.VS.PULSE", ucum_unit="beats/min"),
    )
    amended_form.data_schema.variables.append(new_var)
    amended_form.version_tag = "2.0.0"

    matrix = AmendmentDiffService.calculate_diff(base_form, amended_form)
    assert matrix.from_version_tag == "1.0.0"
    assert matrix.to_version_tag == "2.0.0"
    assert len(matrix.directives) == 1
    assert matrix.directives[0].variable_id == "VS_PULSE"
    assert matrix.directives[0].action == "ADD"
    assert matrix.directives[0].migration_mode == MigrationMode.PROSPECTIVE


def test_uat_runner_service():
    """Verify automated execution of synthetic UAT payloads and 21 CFR Part 11 Evidence Report.

    @req:PRD-CRF-009
    @req:PRD-SYS-001
    """
    form = _create_sample_form()
    payloads = [
        SyntheticPayload(
            payload_id="P1",
            test_scenario="Valid Vitals",
            data={"VS_SYSBP": 120, "VS_DIABP": 80},
            expected_rule_outcomes={"CHK-01": True},
        ),
        SyntheticPayload(
            payload_id="P2",
            test_scenario="Invalid Vitals (Diastolic higher)",
            data={"VS_SYSBP": 70, "VS_DIABP": 90},
            expected_rule_outcomes={"CHK-01": False},
        ),
    ]

    report = UatRunnerService.execute_uat_suite(form, payloads)
    assert report.total_scenarios_evaluated == 2
    assert report.scenarios_passed == 2
    assert report.scenarios_failed == 0
    assert report.rule_coverage_percentage == 100.0
    assert report.gxp_compliance_verified is True
    assert len(report.sha256_evidence_digest) == 64


def test_publication_audit_service():
    """Verify 21 CFR Part 11 publication sealing with reason for change and step-up signature.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    form = _create_sample_form()
    seal_res = PublicationAuditService.seal_publication(
        form_def=form,
        published_by="dr.jane",
        reason_for_change="Final Phase 3 eCRF approval",
        step_up_signature_token="sig-token-abc-123",
    )

    assert seal_res["status"] == "PUBLISHED_AND_SEALED"
    assert seal_res["form_id"] == "FORM-VS"
    assert seal_res["sha256_audit_seal"].startswith("sha256:")
    assert seal_res["publication_manifest"]["published_by"] == "dr.jane"
