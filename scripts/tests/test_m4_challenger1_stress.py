"""Adversarial Stress & Boundary Verification Suite for Milestone M4 (ACL Implementation).

Focuses on Execution Service ACLs and CTMS Service ACLs:
1. apps/execution/src/domain/acl/designer_eligibility_dto.py
2. apps/execution/src/domain/acl/protocol_version_ref_dto.py
3. apps/execution/src/domain/acl/usdm_validation_dto.py
4. apps/ctms/src/domain/acl/document_renderer_dto.py
5. apps/ctms/src/domain/acl/sync_engine_dto.py
"""

from datetime import UTC, datetime

import pytest
from pydantic import ValidationError

from apps.ctms.domain.acl.document_renderer_dto import (
    CTMSDocumentRendererACL,
    DocumentRenderRequestDTO,
    DocumentRenderResponseDTO,
)
from apps.ctms.domain.acl.sync_engine_dto import (
    CTMSSignatureValidationError,
    CTMSSyncMetadataDTO,
    CTMSSyncRecordDTO,
    get_ctms_signature_payload,
    reconcile_ctms_records,
    verify_ctms_record_signature,
)
from apps.execution.domain.acl.designer_eligibility_dto import (
    DesignerEligibilityCriterionDTO,
    DesignerExpressionNodeDTO,
    FieldReferenceDTO,
    evaluate_eligibility_dto,
    evaluate_node_dto,
)
from apps.execution.domain.acl.protocol_version_ref_dto import (
    ProtocolVersionRefDTO,
    ProtocolVersionStatusEnum,
)
from apps.execution.domain.acl.usdm_validation_dto import (
    normalize_usdm_payload,
    validate_usdm_payload,
)
from packages.security.signing import generate_canonical_signature

# ============================================================================
# 1. EXECUTION SERVICE ACL: Designer Eligibility DTO Stress & Boundary Tests
# ============================================================================


def test_field_reference_dto_parsing():
    """Verify FieldReferenceDTO auto-parses raw reference string patterns."""
    f1 = FieldReferenceDTO(raw_reference="eCRF.DM.AGE")
    assert f1.domain == "DM"
    assert f1.variable == "AGE"

    # Alias / Dict fallback
    f2 = FieldReferenceDTO.model_validate({"field_id": "eCRF.VS.SYSBP"})
    assert f2.raw_reference == "eCRF.VS.SYSBP"
    assert f2.domain == "VS"
    assert f2.variable == "SYSBP"

    # Non-eCRF reference format
    f3 = FieldReferenceDTO(raw_reference="CUSTOM_VAR")
    assert f3.domain == ""
    assert f3.variable == ""


def test_designer_eligibility_criterion_alias_sync():
    """Verify legacy alias fields and primary fields stay in sync."""
    data = {
        "criterion_id": "INC_001",
        "criterion_type": "inclusion",
        "description": "Patient age must be >= 18",
        "dsl_source": "eCRF.DM.AGE >= 18",
        "condition": {
            "type": "comparison",
            "operator": ">=",
            "operands": [
                {"type": "field_ref", "field_ref": {"raw_reference": "eCRF.DM.AGE"}},
                {"type": "constant", "value": 18},
            ],
        },
    }
    dto = DesignerEligibilityCriterionDTO.model_validate(data)

    assert dto.id == "INC_001"
    assert dto.criterion_id == "INC_001"
    assert dto.human_readable_text == "Patient age must be >= 18"
    assert dto.description == "Patient age must be >= 18"
    assert dto.dsl_expression_string == "eCRF.DM.AGE >= 18"
    assert dto.dsl_source == "eCRF.DM.AGE >= 18"
    assert dto.structured_expression_tree is not None
    assert dto.condition is not None
    assert isinstance(
        dto.structured_expression_tree.operands[0].field_ref, FieldReferenceDTO
    )


def test_kleene_3_valued_logic_evaluation():
    """Verify Kleene 3-valued logic handling for indeterminate (missing/null) values."""
    # Field missing in context -> indeterminate
    node_ref = DesignerExpressionNodeDTO(
        type="field_ref", field_ref=FieldReferenceDTO(raw_reference="eCRF.DM.AGE")
    )
    res1 = evaluate_node_dto(node_ref, {})
    assert res1.is_indeterminate is True
    assert "missing or null" in res1.explanation

    # Comparison with missing field -> indeterminate comparison
    comp_node = DesignerExpressionNodeDTO(
        type="comparison",
        operator=">=",
        operands=[node_ref, DesignerExpressionNodeDTO(type="constant", value=18)],
    )
    res_comp = evaluate_node_dto(comp_node, {})
    assert res_comp.is_indeterminate is True

    # Kleene AND short-circuiting: False AND Indeterminate -> False
    false_node = DesignerExpressionNodeDTO(type="constant", value=False)
    and_node_short = DesignerExpressionNodeDTO(
        type="logical", operator="and", operands=[false_node, comp_node]
    )
    res_and_short = evaluate_node_dto(and_node_short, {})
    assert res_and_short.is_indeterminate is False
    assert res_and_short.value is False

    # Kleene AND without short-circuiting: True AND Indeterminate -> Indeterminate
    true_node = DesignerExpressionNodeDTO(type="constant", value=True)
    and_node_indet = DesignerExpressionNodeDTO(
        type="logical", operator="and", operands=[true_node, comp_node]
    )
    res_and_indet = evaluate_node_dto(and_node_indet, {})
    assert res_and_indet.is_indeterminate is True

    # Kleene OR short-circuiting: True OR Indeterminate -> True
    or_node_short = DesignerExpressionNodeDTO(
        type="logical", operator="or", operands=[true_node, comp_node]
    )
    res_or_short = evaluate_node_dto(or_node_short, {})
    assert res_or_short.is_indeterminate is False
    assert res_or_short.value is True

    # Kleene NOT on Indeterminate -> Indeterminate
    not_node_indet = DesignerExpressionNodeDTO(
        type="logical", operator="not", operands=[comp_node]
    )
    res_not = evaluate_node_dto(not_node_indet, {})
    assert res_not.is_indeterminate is True


def test_aggregate_eligibility_evaluation_scenarios():
    """Verify aggregate eligibility decision outcomes across met, failed, and indeterminate criteria."""
    inc1 = DesignerEligibilityCriterionDTO(
        id="INC_1",
        criterion_type="inclusion",
        expected_outcome=True,
        structured_expression_tree=DesignerExpressionNodeDTO(
            type="comparison",
            operator=">=",
            operands=[
                {"type": "field_ref", "field_ref": {"raw_reference": "eCRF.DM.AGE"}},
                {"type": "constant", "value": 18},
            ],
        ),
    )
    inc2 = DesignerEligibilityCriterionDTO(
        id="INC_2",
        criterion_type="inclusion",
        expected_outcome=True,
        structured_expression_tree=DesignerExpressionNodeDTO(
            type="comparison",
            operator="==",
            operands=[
                {"type": "field_ref", "field_ref": {"raw_reference": "eCRF.DM.GENDER"}},
                {"type": "constant", "value": "F"},
            ],
        ),
    )

    # 1. All criteria met
    eval_all_met = evaluate_eligibility_dto(
        [inc1, inc2], {"eCRF.DM.AGE": 25, "eCRF.DM.GENDER": "F"}
    )
    assert eval_all_met.eligible is True
    assert len(eval_all_met.failed_criteria) == 0
    assert len(eval_all_met.indeterminate_criteria) == 0

    # 2. One criterion failed
    eval_one_failed = evaluate_eligibility_dto(
        [inc1, inc2], {"eCRF.DM.AGE": 16, "eCRF.DM.GENDER": "F"}
    )
    assert eval_one_failed.eligible is False
    assert eval_one_failed.failed_criteria == ["INC_1"]

    # 3. One criterion indeterminate (missing GENDER), no failures -> eligible is None
    eval_indet = evaluate_eligibility_dto([inc1, inc2], {"eCRF.DM.AGE": 25})
    assert eval_indet.eligible is None
    assert eval_indet.indeterminate_criteria == ["INC_2"]


# ============================================================================
# 2. EXECUTION SERVICE ACL: Protocol Version Ref DTO Stress Tests
# ============================================================================


def test_protocol_version_ref_dto_validation():
    """Verify strict validation rules on study_id, version_tag, version_index, and status enum."""
    valid_dto = ProtocolVersionRefDTO(
        study_id="STUDY-101",
        version_tag="v1.0.0",
        version_index=1,
        status=ProtocolVersionStatusEnum.ACTIVE,
    )
    assert valid_dto.study_id == "STUDY-101"
    assert valid_dto.version_index == 1

    # Empty / whitespace study_id
    with pytest.raises(ValueError, match="Study ID cannot be empty"):
        ProtocolVersionRefDTO(
            study_id="   ",
            version_tag="v1.0",
            version_index=1,
            status=ProtocolVersionStatusEnum.ACTIVE,
        )

    # Empty / whitespace version_tag
    with pytest.raises(ValueError, match="Version tag cannot be empty"):
        ProtocolVersionRefDTO(
            study_id="STUDY-101",
            version_tag="",
            version_index=1,
            status=ProtocolVersionStatusEnum.ACTIVE,
        )

    # Zero or negative version_index
    with pytest.raises(ValueError, match="Version index must be a positive integer"):
        ProtocolVersionRefDTO(
            study_id="STUDY-101",
            version_tag="v1.0",
            version_index=0,
            status=ProtocolVersionStatusEnum.ACTIVE,
        )

    # Invalid status enum string
    with pytest.raises(ValidationError):
        ProtocolVersionRefDTO(
            study_id="STUDY-101",
            version_tag="v1.0",
            version_index=1,
            status="NON_EXISTENT_STATUS",  # type: ignore
        )


# ============================================================================
# 3. EXECUTION SERVICE ACL: USDM Validation DTO Stress Tests
# ============================================================================


def test_usdm_validation_dto_and_parser():
    """Verify USDM version resolution, normalization, and duplicate ID detection."""
    # USDM v3 payload detection
    v3_json = '{"usdmVersion": "3.0.0", "id": "STUDY-001", "name": "Trial 1"}'
    v3_res = validate_usdm_payload(v3_json)
    assert v3_res.version == "v3"
    assert v3_res.format == "JSON"
    assert v3_res.validity is True

    # USDM v2 payload detection
    v2_json = '{"id": "STUDY-002", "name": "Trial 2"}'
    v2_res = validate_usdm_payload(v2_json)
    assert v2_res.version == "v2"
    assert v2_res.validity is True

    # Duplicate physical ID detection across nested structures
    dup_json = """
    {
        "id": "STUDY-003",
        "name": "Trial 3",
        "sub_element": {"id": "STUDY-003"}
    }
    """
    dup_res = validate_usdm_payload(dup_json)
    assert dup_res.validity is False
    assert any("Duplicate physical ID" in err.reason for err in dup_res.errors)

    # Normalization helper mapping study_id to id
    norm = normalize_usdm_payload({"study_id": "STUDY-999", "name": "Test Study"})
    assert norm["id"] == "STUDY-999"


# ============================================================================
# 4. CTMS SERVICE ACL: Document Renderer DTO & Fallback Stream Tests
# ============================================================================


def test_ctms_document_renderer_fallback():
    """Verify CTMS ACL PDF renderer handles requests and generates valid PDF header."""
    renderer = CTMSDocumentRendererACL()
    req = DocumentRenderRequestDTO(
        html_content="<h1>DOA Log</h1><p>Site 101 Delegation Log</p>",
        document_title="DOA Site 101 Log",
    )
    res = renderer.render_pdf(req)

    assert isinstance(res, DocumentRenderResponseDTO)
    assert res.pdf_bytes.startswith(b"%PDF-")
    assert res.content_type == "application/pdf"
    assert res.filename == "DOA_Site_101_Log.pdf"


# ============================================================================
# 5. CTMS SERVICE ACL: Sync Engine DTO, Signature Verification & Reconciliation
# ============================================================================


def test_ctms_sync_signature_verification_and_tampering():
    """Verify HMAC signature creation, verification, and tamper detection."""
    secret = b"super-secret-ctms-key-32-bytes!!"
    t0 = datetime.now(UTC)

    meta = CTMSSyncMetadataDTO(
        timestamps={"status": t0, "role": t0},
        modified_by="user_dr_smith",
    )
    rec = CTMSSyncRecordDTO(
        deduplication_key="STUDY-1:SITE-101:STAFF-42",
        data={"status": "APPROVED", "role": "Principal Investigator"},
        metadata=meta,
    )

    # Generate valid signature
    payload_dict = get_ctms_signature_payload(rec)
    sig = generate_canonical_signature(payload_dict, secret)
    rec.metadata.signature = sig

    # 1. Valid signature passes
    assert verify_ctms_record_signature(rec, secret) is True

    # 2. Tampered data payload fails signature verification
    tampered_rec = CTMSSyncRecordDTO(
        deduplication_key=rec.deduplication_key,
        data={"status": "APPROVED", "role": "Sub-Investigator"},  # Tampered role
        metadata=rec.metadata,
    )
    assert verify_ctms_record_signature(tampered_rec, secret) is False

    # 3. Missing signature fails
    unsigned_rec = CTMSSyncRecordDTO(
        deduplication_key=rec.deduplication_key,
        data=rec.data,
        metadata=CTMSSyncMetadataDTO(
            timestamps={"status": t0}, modified_by="user_dr_smith"
        ),
    )
    assert verify_ctms_record_signature(unsigned_rec, secret) is False


def test_ctms_sync_reconciliation_strategies():
    """Verify CLIENT_WINS, SERVER_WINS, and MERGE (Last-Write-Wins) reconciliation logic."""
    secret = b"ctms-reconcile-secret-key-12345"
    t_old = datetime(2026, 1, 1, 12, 0, 0, tzinfo=UTC)
    t_new = datetime(2026, 1, 2, 12, 0, 0, tzinfo=UTC)

    server_data = {"phone": "555-0100", "email": "old@site.org"}
    server_meta = CTMSSyncMetadataDTO(
        timestamps={"phone": t_old, "email": t_old},
        modified_by="server",
    )

    incoming_meta = CTMSSyncMetadataDTO(
        timestamps={"phone": t_old, "email": t_new},  # Email updated later
        modified_by="client_device_1",
    )
    incoming_rec = CTMSSyncRecordDTO(
        deduplication_key="STAFF-101",
        data={"phone": "555-9999", "email": "new@site.org"},
        metadata=incoming_meta,
    )

    # Add signature
    payload_dict = get_ctms_signature_payload(incoming_rec)
    incoming_rec.metadata.signature = generate_canonical_signature(payload_dict, secret)

    # 1. CLIENT_WINS Strategy
    res_client = reconcile_ctms_records(
        server_data, server_meta, incoming_rec, "CLIENT_WINS", secret=secret
    )
    assert res_client.status == "UPDATED_CLIENT_WINS"
    assert res_client.data == incoming_rec.data

    # 2. SERVER_WINS Strategy
    res_server = reconcile_ctms_records(
        server_data, server_meta, incoming_rec, "SERVER_WINS", secret=secret
    )
    assert res_server.status == "IGNORED_SERVER_WINS"
    assert res_server.data == server_data

    # 3. MERGE Strategy (LWW)
    # phone has t_old on both. Tie-break via modified_by: 'client_device_1' < 'server' -> server wins phone ('555-0100')
    # email has t_new vs t_old (t_new > t_old -> client wins email 'new@site.org')
    res_merge = reconcile_ctms_records(
        server_data, server_meta, incoming_rec, "MERGE", secret=secret
    )
    assert res_merge.status == "MERGED"
    assert res_merge.data["email"] == "new@site.org"
    assert (
        res_merge.data["phone"] == "555-0100"
    )  # 'server' > 'client_device_1' lexicographically

    # Test tie-break when client modified_by is lexicographically greater than server ('z_client' > 'server')
    incoming_meta_z = CTMSSyncMetadataDTO(
        timestamps={"phone": t_old, "email": t_new},
        modified_by="z_client",
    )
    incoming_rec_z = CTMSSyncRecordDTO(
        deduplication_key="STAFF-101",
        data={"phone": "555-9999", "email": "new@site.org"},
        metadata=incoming_meta_z,
    )
    payload_dict_z = get_ctms_signature_payload(incoming_rec_z)
    incoming_rec_z.metadata.signature = generate_canonical_signature(
        payload_dict_z, secret
    )

    res_merge_z = reconcile_ctms_records(
        server_data, server_meta, incoming_rec_z, "MERGE", secret=secret
    )
    assert (
        res_merge_z.data["phone"] == "555-9999"
    )  # 'z_client' > 'server' -> client wins tie-break

    # Dict item access backward compatibility (__getitem__)
    assert res_merge["status"] == "MERGED"
    assert res_merge["data"] == res_merge.data


def test_ctms_sync_reconciliation_signature_enforcement_errors():
    """Verify CTMSSignatureValidationError is raised on missing secret or bad signature when required."""
    rec = CTMSSyncRecordDTO(
        deduplication_key="KEY-1",
        data={"a": 1},
        metadata=CTMSSyncMetadataDTO(modified_by="client", signature="invalid_sig"),
    )

    with pytest.raises(CTMSSignatureValidationError, match="A secret must be provided"):
        reconcile_ctms_records(
            {}, None, rec, "CLIENT_WINS", secret=None, require_signature=True
        )

    with pytest.raises(CTMSSignatureValidationError, match="Invalid signature"):
        reconcile_ctms_records(
            {}, None, rec, "CLIENT_WINS", secret=b"secret", require_signature=True
        )
