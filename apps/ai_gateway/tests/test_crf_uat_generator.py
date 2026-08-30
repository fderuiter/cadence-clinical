"""Unit tests for AI Gateway Synthetic UAT Generator and Protocol-to-CRF Scaffolder."""

from apps.ai_gateway.domain.services.crf_uat_generator import CrfUatGenerator
from apps.ai_gateway.domain.services.protocol_to_crf_scaffolder import (
    ProtocolToCrfScaffolder,
)
from packages.ddf.tripartite import (
    CdiscMetadata,
    ClinicalDataType,
    ConstraintDefinition,
    DataSchema,
    LogicSchema,
    TripartiteFormDefinition,
    UiSchema,
    VariableDefinition,
)


def _create_test_form() -> TripartiteFormDefinition:
    """Create test form for UAT generator."""
    return TripartiteFormDefinition(
        form_id="FORM-VS",
        protocol_id="STUDY-101",
        data_schema=DataSchema(
            form_id="FORM-VS",
            name="Vital Signs",
            cdisc_form_oid="FORM.VS",
            variables=[
                VariableDefinition(
                    id="VS_SYSBP",
                    name="Systolic BP",
                    data_type=ClinicalDataType.INTEGER,
                    cdisc=CdiscMetadata(oid="IT.VS.SYSBP"),
                    constraints=ConstraintDefinition(min_value=50.0, max_value=250.0),
                ),
                VariableDefinition(
                    id="VS_TEMP",
                    name="Body Temp",
                    data_type=ClinicalDataType.FLOAT,
                    cdisc=CdiscMetadata(oid="IT.VS.TEMP"),
                    constraints=ConstraintDefinition(min_value=32.0, max_value=43.0),
                ),
            ],
        ),
        ui_schema=UiSchema(form_id="FORM-VS"),
        logic_schema=LogicSchema(form_id="FORM-VS"),
    )


def test_crf_uat_generator_volume_and_boundaries():
    """Verify synthetic generation produces >= 500 boundary payloads targeting min/max edges.

    @req:PRD-SYS-051
    @req:PRD-CRF-009
    """
    form = _create_test_form()
    payloads = CrfUatGenerator.generate_uat_payloads(form, target_count=500)

    assert len(payloads) >= 500
    # Check baseline presence
    assert any(p.payload_id == "SYNTH-BASELINE-001" for p in payloads)
    # Check minimum and maximum boundary test cases
    assert any("MIN" in p.payload_id for p in payloads)
    assert any("MAX" in p.payload_id for p in payloads)


def test_protocol_to_crf_scaffolding():
    """Verify AI Copilot extracts endpoints and instantiates Smart Blocks from protocol narrative.

    @req:PRD-DDF-001
    @req:PRD-SYS-051
    """
    protocol_text = """
    Section 5: Schedule of Assessments.
    At each visit, blood pressure and vital signs will be measured in sitting position.
    Adverse events and safety toxicities will be graded according to CTCAE v5.0.
    """

    result = ProtocolToCrfScaffolder.scaffold_study_from_protocol(
        protocol_title="Phase 3 Oncology Protocol",
        protocol_text=protocol_text,
    )

    assert result["status"] == "ZERO_DRAFT_SCAFFOLDED"
    assert "VS" in result["identified_domains"]
    assert "AE" in result["identified_domains"]
    assert result["forms_count"] == 2
