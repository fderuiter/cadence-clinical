"""Empirical stress test and verification harness for Milestone M4 (Challenger 2).

Targeting:
- eTMF Service ACL (apps/etmf/src/domain/acl/protocol_version_ref.py)
- eTMF Watermark Decoupling (apps/etmf/watermark.py)
- Interop Service ACL Eligibility DTOs & Evaluator (apps/interop/src/domain/acl/eligibility_dto.py)
- Interop Service ACL ePRO Transport DTOs (apps/interop/src/domain/acl/epro_transport_dto.py)
- Cross-Service Import Isolation for eTMF and Interop
"""

import json
import re
import sys
from datetime import UTC, datetime
from pathlib import Path

import pytest
from pydantic import ValidationError

# Add root directory to sys.path
ROOT_DIR = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT_DIR))

from apps.etmf.src.domain.acl.protocol_version_ref import (  # noqa: E402
    ProtocolVersionRef,
    ProtocolVersionRefDTO,
    ProtocolVersionStatus,
    ProtocolVersionStatusDTO,
)
from apps.etmf.watermark import apply_watermark  # noqa: E402
from apps.interop.src.domain.acl.eligibility_dto import (  # noqa: E402
    EligibilityCriterion,
    EligibilityCriterionDTO,
    ExpressionNode,
    ExpressionNodeDTO,
    FieldReferenceDTO,
    evaluate_eligibility,
    parse_dsl,
)
from apps.interop.src.domain.acl.epro_transport_dto import (  # noqa: E402
    AssignmentComplianceDetail,
    AssignmentComplianceDetailDTO,
    InstrumentCreate,
    InstrumentCreateDTO,
    InstrumentResponse,
    InstrumentResponseDTO,
    SubjectAssignmentCreate,
    SubjectAssignmentCreateDTO,
    SubjectAssignmentResponse,
    SubjectAssignmentResponseDTO,
    SubjectComplianceResponse,
    SubjectComplianceResponseDTO,
)


def test_etmf_protocol_version_ref_valid():
    """Verify standard valid creation and property access for ProtocolVersionRefDTO."""
    dto = ProtocolVersionRefDTO(
        study_id="STUDY-999",
        version_tag="v3.1.0",
        version_index=3,
        status=ProtocolVersionStatusDTO.ACTIVE,
    )
    assert dto.study_id == "STUDY-999"
    assert dto.version_tag == "v3.1.0"
    assert dto.version_index == 3
    assert dto.status == ProtocolVersionStatusDTO.ACTIVE
    assert ProtocolVersionRef == ProtocolVersionRefDTO
    assert ProtocolVersionStatus == ProtocolVersionStatusDTO


def test_etmf_protocol_version_ref_string_trimming():
    """Verify leading/trailing whitespace is stripped in study_id and version_tag."""
    dto = ProtocolVersionRefDTO(
        study_id="   STUDY-XYZ   ",
        version_tag="  2.0  ",
        version_index=1,
        status="LOCKED",
    )
    assert dto.study_id == "STUDY-XYZ"
    assert dto.version_tag == "2.0"
    assert dto.status == ProtocolVersionStatusDTO.LOCKED


def test_etmf_protocol_version_ref_invalid_study_id():
    """Verify empty or whitespace-only study_id raises ValueError."""
    with pytest.raises(ValidationError) as exc:
        ProtocolVersionRefDTO(
            study_id="   ",
            version_tag="1.0",
            version_index=1,
            status=ProtocolVersionStatusDTO.DRAFT,
        )
    assert "Study ID cannot be empty" in str(exc.value)


def test_etmf_protocol_version_ref_invalid_version_tag():
    """Verify empty or whitespace-only version_tag raises ValueError."""
    with pytest.raises(ValidationError) as exc:
        ProtocolVersionRefDTO(
            study_id="STUDY-1",
            version_tag="",
            version_index=1,
            status=ProtocolVersionStatusDTO.DRAFT,
        )
    assert "Version tag cannot be empty" in str(exc.value)


def test_etmf_protocol_version_ref_invalid_version_index():
    """Verify zero or negative version_index raises ValueError."""
    with pytest.raises(ValidationError) as exc1:
        ProtocolVersionRefDTO(
            study_id="STUDY-1",
            version_tag="1.0",
            version_index=0,
            status=ProtocolVersionStatusDTO.DRAFT,
        )
    assert "positive integer >= 1" in str(exc1.value)

    with pytest.raises(ValidationError) as exc2:
        ProtocolVersionRefDTO(
            study_id="STUDY-1",
            version_tag="1.0",
            version_index=-5,
            status=ProtocolVersionStatusDTO.DRAFT,
        )
    assert "positive integer >= 1" in str(exc2.value)


def test_etmf_protocol_version_ref_invalid_status():
    """Verify invalid status value raises ValidationError."""
    with pytest.raises(ValidationError):
        ProtocolVersionRefDTO(
            study_id="STUDY-1",
            version_tag="1.0",
            version_index=1,
            status="UNKNOWN_STATUS",
        )


def test_etmf_protocol_version_ref_json_serialization():
    """Verify JSON serialization and deserialization roundtrip."""
    dto = ProtocolVersionRefDTO(
        study_id="STUDY-555",
        version_tag="v1.0",
        version_index=2,
        status=ProtocolVersionStatusDTO.PUBLISHED,
    )
    json_str = dto.model_dump_json()
    reconstructed = ProtocolVersionRefDTO.model_validate_json(json_str)
    assert reconstructed == dto


def test_etmf_watermark_decoupling():
    """Verify eTMF watermark functions without external dependencies and handles all format types."""
    # JSON watermarking
    json_input = json.dumps({"document_id": "DOC-101", "title": "Protocol Synopses"})
    watermarked_json = apply_watermark(
        json_input, "application/json", "USER-123", "Auditor"
    )
    parsed = json.loads(watermarked_json)
    assert "_watermark" in parsed
    assert parsed["_watermark"]["accessed_by"] == "USER-123"
    assert parsed["_watermark"]["role"] == "Auditor"
    assert parsed["document_id"] == "DOC-101"

    # XML watermarking
    xml_input = "<protocol><title>Test</title></protocol>"
    watermarked_xml = apply_watermark(xml_input, "text/xml", "USER-456", "Monitor")
    assert (
        "<!-- CONFIDENTIAL — Auditor Copy | Access by: USER-456 (Monitor)"
        in watermarked_xml
    )

    # CSV watermarking
    csv_input = "subject_id,age,gender\nSUBJ-01,45,M"
    watermarked_csv = apply_watermark(csv_input, "text/csv", "USER-789", "DataManager")
    assert (
        "# CONFIDENTIAL — Auditor Copy | Access by: USER-789 (DataManager)"
        in watermarked_csv
    )

    # Bytes input & output consistency
    raw_bytes = b"Raw text document content"
    watermarked_bytes = apply_watermark(raw_bytes, "text/plain", "USER-000", "Admin")
    assert isinstance(watermarked_bytes, bytes)
    assert (
        b"CONFIDENTIAL \xe2\x80\x94 Auditor Copy" in watermarked_bytes
        or b"CONFIDENTIAL" in watermarked_bytes
    )


def test_interop_field_reference_dto():
    """Verify FieldReferenceDTO parsing and validation rules."""
    ref = FieldReferenceDTO(raw_reference="eCRF.DM.AGE", domain="DM", variable="AGE")
    assert ref.raw_reference == "eCRF.DM.AGE"
    assert ref.domain == "DM"
    assert ref.variable == "AGE"

    # Malformed reference
    with pytest.raises(ValidationError):
        FieldReferenceDTO(raw_reference="INVALID.REF", domain="DM", variable="AGE")

    # Domain / variable mismatch with raw_reference
    with pytest.raises(ValidationError):
        FieldReferenceDTO(raw_reference="eCRF.DM.AGE", domain="VS", variable="SYSBP")


def test_interop_expression_node_dto_validation():
    """Verify ExpressionNodeDTO structural validations."""
    # Field ref node missing field_ref
    with pytest.raises(ValidationError) as exc1:
        ExpressionNodeDTO(type="field_ref")
    assert "Field reference node must provide field_ref" in str(exc1.value)

    # Invalid comparison operator
    with pytest.raises(ValidationError) as exc2:
        ExpressionNodeDTO(
            type="comparison",
            operator="INVALID_OP",
            operands=[
                ExpressionNodeDTO(type="constant", value=1),
                ExpressionNodeDTO(type="constant", value=2),
            ],
        )
    assert "Invalid comparison operator" in str(exc2.value)

    # Comparison node with wrong operand count
    with pytest.raises(ValidationError) as exc3:
        ExpressionNodeDTO(
            type="comparison",
            operator="==",
            operands=[ExpressionNodeDTO(type="constant", value=1)],
        )
    assert "requires 2 operands" in str(exc3.value)


def test_interop_dsl_parsing_and_evaluation():
    """Verify parse_dsl and evaluate_eligibility on complex inclusion/exclusion criteria."""
    dsl_1 = "eCRF.DM.AGE >= 18"
    node_1 = parse_dsl(dsl_1)
    assert node_1.type == "comparison"
    assert node_1.operator == ">="

    dsl_2 = "eCRF.VS.SYSBP < 140"
    node_2 = parse_dsl(dsl_2)

    crit_1 = EligibilityCriterionDTO(
        id="INCL-01",
        criterion_type="inclusion",
        human_readable_text="Subject age must be at least 18",
        dsl_expression_string=dsl_1,
        structured_expression_tree=node_1,
        expected_outcome=True,
    )

    crit_2 = EligibilityCriterionDTO(
        id="EXCL-01",
        criterion_type="exclusion",
        human_readable_text="Systolic blood pressure must be under 140",
        dsl_expression_string=dsl_2,
        structured_expression_tree=node_2,
        expected_outcome=True,
    )

    assert EligibilityCriterion == EligibilityCriterionDTO
    assert ExpressionNode == ExpressionNodeDTO

    # Test context 1: Eligible subject (AGE=25, SYSBP=120)
    ctx_1 = {"eCRF.DM.AGE": 25, "eCRF.VS.SYSBP": 120}
    res_1 = evaluate_eligibility([crit_1, crit_2], ctx_1)
    assert res_1.eligible is True
    assert len(res_1.failed_criteria) == 0
    assert len(res_1.indeterminate_criteria) == 0

    # Test context 2: Ineligible subject (AGE=16, SYSBP=120)
    ctx_2 = {"eCRF.DM.AGE": 16, "eCRF.VS.SYSBP": 120}
    res_2 = evaluate_eligibility([crit_1, crit_2], ctx_2)
    assert res_2.eligible is False
    assert "INCL-01" in res_2.failed_criteria

    # Test context 3: Indeterminate context (missing SYSBP)
    ctx_3 = {"eCRF.DM.AGE": 25}
    res_3 = evaluate_eligibility([crit_1, crit_2], ctx_3)
    assert res_3.eligible is None
    assert "EXCL-01" in res_3.indeterminate_criteria


def test_interop_epro_transport_dto_serialization():
    """Verify ePRO Transport DTO creation, ISO datetime parsing, and json serialization."""
    inst_create = InstrumentCreateDTO(
        study_id="STUDY-101",
        name="SF-36 Health Survey",
        description="Quality of life questionnaire",
        items={"q1": "General Health", "q2": "Physical Activity"},
        response_types={"q1": "likert", "q2": "boolean"},
        scoring_metadata={"scale": "0-100"},
        reason_for_change="Initial setup",
    )
    assert inst_create.study_id == "STUDY-101"

    now = datetime.now(UTC)
    inst_resp = InstrumentResponseDTO(
        id="INST-001",
        name="SF-36 Health Survey",
        description="Quality of life questionnaire",
        items={"q1": "General Health"},
        response_types={"q1": "likert"},
        scoring_metadata={"scale": "0-100"},
        created_at=now,
        created_by="investigator_1",
        reason_for_change="Initial version",
        version_index=1,
    )
    json_bytes = inst_resp.model_dump_json()
    deserialized = InstrumentResponseDTO.model_validate_json(json_bytes)
    assert deserialized.id == inst_resp.id
    assert deserialized.created_by == inst_resp.created_by

    # Backward compatibility aliases check
    assert InstrumentCreate == InstrumentCreateDTO
    assert InstrumentResponse == InstrumentResponseDTO
    assert SubjectAssignmentCreate == SubjectAssignmentCreateDTO
    assert SubjectAssignmentResponse == SubjectAssignmentResponseDTO
    assert AssignmentComplianceDetail == AssignmentComplianceDetailDTO
    assert SubjectComplianceResponse == SubjectComplianceResponseDTO


def test_cross_service_import_isolation():
    """Audit eTMF and Interop services for zero cross-service or core-models imports."""
    forbidden_patterns = [
        r"^\s*(from|import)\s+packages\.core_models\b",
        r"^\s*from\s+apps\.(designer|execution|ctms)\b",
    ]

    # Specific forbidden patterns for eTMF
    etmf_forbidden = forbidden_patterns + [
        r"^\s*(from|import)\s+protocol_version_ref\b",
        r"^\s*from\s+apps\.interop\b",
    ]

    # Specific forbidden patterns for Interop
    interop_forbidden = forbidden_patterns + [
        r"^\s*(from|import)\s+eligibility\b",
        r"^\s*from\s+apps\.etmf\b",
    ]

    violations = []

    # Audit eTMF
    for p in Path(ROOT_DIR / "apps/etmf").rglob("*.py"):
        if "tests" in p.parts:
            continue
        for line_no, line in enumerate(p.read_text().splitlines(), 1):
            if line.strip().startswith("#"):
                continue
            for pat in etmf_forbidden:
                if re.search(pat, line):
                    violations.append(
                        (str(p.relative_to(ROOT_DIR)), line_no, line.strip())
                    )

    # Audit Interop
    for p in Path(ROOT_DIR / "apps/interop").rglob("*.py"):
        if "tests" in p.parts:
            continue
        for line_no, line in enumerate(p.read_text().splitlines(), 1):
            if line.strip().startswith("#"):
                continue
            for pat in interop_forbidden:
                if re.search(pat, line):
                    violations.append(
                        (str(p.relative_to(ROOT_DIR)), line_no, line.strip())
                    )

    assert len(violations) == 0, (
        f"Found {len(violations)} cross-service import violations: {violations}"
    )


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
