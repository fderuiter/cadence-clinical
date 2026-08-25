"""Tests for Targeted Schema Provenance and Post-Synthesis Faithfulness Scoring.

Requirements: PRD-CRF-004, PRD-DDF-001, PRD-SYS-001, PRD-MDR-007
"""

from __future__ import annotations

import time

import pytest

from apps.designer.application.services.digitization_service import (
    _heuristic_protocol_extraction,
    synthesize_ecrf_forms,
)
from apps.designer.domain.digitization_dag_models import (
    DAGJobStatusResponse,
    DigitizationJobStatus,
)
from apps.designer.domain.digitization_models import (
    CommitUSDMResponse,
    ExtractedActivity,
    ExtractedVisit,
    FaithfulnessVerificationMetadata,
    SynthesizedECRFForm,
    USDMProtocolExtractionResponse,
)
from apps.designer.domain.protocol_render.models import SoACellView
from apps.designer.domain.synthesis.crf_synthesizer import (
    CRFSynthesizer,
    SynthesizedField,
)
from apps.designer.domain.synthesis.faithfulness import (
    evaluate_synthesis_faithfulness,
)
from apps.designer.domain.synthesis.soa_compiler import (
    SoACompiler,
    compile_soa_matrix_payload,
)


def test_ecrf_form_and_field_schema_provenance_citation_fields() -> None:
    """Verify that eCRF form and field schema models include optional section_number and page_number citation metadata fields.

    @req:PRD-CRF-004
    """
    field = SynthesizedField(
        field_id="VS_SYSBP",
        label="Systolic Blood Pressure (mmHg)",
        data_type="integer",
        cdash_variable="VS.SYSBP",
        section_number="5.1",
        page_number=12,
        citation_marker="[Protocol v1.0, Section 5.1, Page 12]",
    )
    assert field.section_number == "5.1"
    assert field.page_number == 12
    assert field.citation_marker == "[Protocol v1.0, Section 5.1, Page 12]"

    form = SynthesizedECRFForm(
        form_id="FORM_VS",
        form_name="Vital Signs eCRF",
        cdash_domain="VS",
        items=[field.model_dump()],
        rules=[],
        section_number="5.1",
        page_number=12,
        citation_marker="[Protocol v1.0, Section 5.1, Page 12]",
    )
    assert form.section_number == "5.1"
    assert form.page_number == 12
    assert form.citation_marker == "[Protocol v1.0, Section 5.1, Page 12]"
    assert form.items[0]["section_number"] == "5.1"
    assert form.items[0]["page_number"] == 12


@pytest.mark.asyncio
async def test_soa_matrix_cell_view_coordinates() -> None:
    """Verify Schedule of Activities matrix cell views contain section and page coordinate attributes.

    @req:PRD-MDR-007
    """
    cell = SoACellView(
        activity_id="act_1",
        encounter_id="visit_1",
        epoch_id="epoch_1",
        is_applicable=True,
        section_number="6.1",
        page_number=18,
        citation_marker="[Protocol v1.0, Section 6.1, Page 18]",
    )
    assert cell.section_number == "6.1"
    assert cell.page_number == 18
    assert cell.citation_marker == "[Protocol v1.0, Section 6.1, Page 18]"

    # Test SoACompiler compilation attaches coordinates across cells
    extraction = USDMProtocolExtractionResponse(
        study_title="Oncology Trial",
        protocol_id="ONC-2026-001",
        phase="PHASE_II",
        therapeutic_area="Oncology",
        visits=[
            ExtractedVisit(
                visit_name="Baseline",
                epoch_name="Treatment Epoch",
                target_day=1,
            )
        ],
        activities=[
            ExtractedActivity(
                activity_name="Vital Signs",
                cdash_domain="VS",
                section_number="5.1",
                page_number=12,
                assigned_visit_names=["Baseline"],
            )
        ],
        confidence_score=0.95,
    )
    compiler = SoACompiler()
    matrix = await compiler.compile(extraction)
    assert len(matrix["rows"]) > 0
    row_cells = matrix["rows"][0]["cells"]
    assert len(row_cells) > 0
    assert row_cells[0]["section_number"] == "5.1"
    assert row_cells[0]["page_number"] == 12
    assert "[Protocol v1.0, Section 5.1, Page 12]" in row_cells[0]["citation_marker"]


def test_standardized_citation_marker_formatting() -> None:
    """Verify that protocol synthesis formats citation markers using standardized syntax.

    @req:PRD-SYS-001
    """
    synthesizer = CRFSynthesizer()
    extracted = _heuristic_protocol_extraction("Protocol text for oncology study", "protocol.pdf")
    forms = synthesizer.synthesize(extracted)

    assert len(forms) > 0
    for form in forms:
        assert form.section_number is not None
        assert form.page_number is not None
        assert form.citation_marker is not None
        assert "[Protocol v1.0" in form.citation_marker
        assert "Section" in form.citation_marker
        assert "Page" in form.citation_marker


@pytest.mark.asyncio
async def test_post_synthesis_faithfulness_scoring_and_sla() -> None:
    """Verify post-synthesis pipeline computes faithfulness scores against 0.85 threshold within sub-5.0s SLA.

    @req:PRD-DDF-001
    """
    extracted = _heuristic_protocol_extraction(
        "Protocol Title: A Phase II Randomized Study of Drug X vs Control in Advanced Solid Tumors. "
        "Section 5.1 Vital Signs Assessment. Section 5.2 12-Lead Electrocardiogram.",
        "protocol.pdf",
    )
    forms = synthesize_ecrf_forms(extracted)
    soa_matrix = await compile_soa_matrix_payload(None, extracted)

    start_t = time.perf_counter()
    faithfulness = evaluate_synthesis_faithfulness(
        synthesized_forms=forms,
        soa_matrix=soa_matrix,
        source_text=extracted.study_title,
        protocol_version="v1.0",
    )
    elapsed = time.perf_counter() - start_t

    # SLA requirement
    assert elapsed < 5.0

    # Faithfulness score structure
    assert 0.0 <= faithfulness.faithfulness_score <= 1.0
    assert faithfulness.faithfulness_score >= 0.85
    assert faithfulness.is_faithful is True
    assert faithfulness.verification_status == "VERIFIED"
    assert faithfulness.citation_coverage >= 0.80


def test_low_faithfulness_flagging_without_workflow_blocking() -> None:
    """Verify that low faithfulness scores flag response metadata for user awareness rather than hard failing.

    @req:PRD-SYS-001
    """
    # Unrelated forms and missing citations
    unfaithful_form = SynthesizedECRFForm(
        form_id="FORM_UNGROUNDED",
        form_name="Unrelated Nonexistent Form",
        cdash_domain="XX",
        items=[
            {
                "field_id": "XX_VAL",
                "label": "Completely Unrelated Hallucinated Field",
                "section_number": None,
                "page_number": None,
                "citation_marker": None,
            }
        ],
        rules=[],
    )

    faithfulness = evaluate_synthesis_faithfulness(
        synthesized_forms=[unfaithful_form],
        soa_matrix={"rows": []},
        source_text="Specific Protocol about Diabetes",
        protocol_version="v1.0",
    )

    assert faithfulness.is_faithful is False
    assert faithfulness.verification_status == "FLAGGED_LOW_FAITHFULNESS"
    assert len(faithfulness.warnings) > 0


def test_response_payload_dtos_include_faithfulness_metadata() -> None:
    """Verify response payload DTOs return faithfulness score metrics and verification status indicators.

    @req:PRD-CRF-004
    """
    metadata = FaithfulnessVerificationMetadata(
        faithfulness_score=0.92,
        is_faithful=True,
        verification_status="VERIFIED",
        citation_coverage=1.0,
        protocol_version="v1.0",
        warnings=[],
    )

    commit_res = CommitUSDMResponse(
        study_id="study_01",
        version_id="study_01_v1",
        status="COMMITTED",
        nodes_created=10,
        relationships_created=12,
        synthesized_forms=[],
        faithfulness_metadata=metadata,
        message="Committed with verification",
    )
    assert commit_res.faithfulness_metadata is not None
    assert commit_res.faithfulness_metadata.faithfulness_score == 0.92
    assert commit_res.faithfulness_metadata.verification_status == "VERIFIED"

    job_res = DAGJobStatusResponse(
        job_id="job_01",
        study_id="study_01",
        status=DigitizationJobStatus.COMPLETED,
        current_stage=None,
        progress_pct=100,
        created_at=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        updated_at=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        checkpoints={},
        error_message=None,
        is_terminal=True,
        faithfulness_metadata=metadata,
    )
    assert job_res.faithfulness_metadata is not None
    assert job_res.faithfulness_metadata.is_faithful is True
