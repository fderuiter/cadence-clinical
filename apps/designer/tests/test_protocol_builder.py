"""Comprehensive integration test suite for USDM Protocol Builder amendment lifecycle, quality checks, and cascade.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from apps.designer.exporters.m11_exporter import M11ProtocolExporter
from apps.designer.services.artifact_cascade import ArtifactCascadeEngine
from apps.designer.services.branch_manager import ProtocolBranchManager
from apps.designer.services.quality_sentinel import ProtocolQualitySentinel


def test_protocol_builder_amendment_branch_lifecycle() -> None:
    """Validate full protocol builder lifecycle: branch, compare, audit sentinel, cascade, merge, export.

    Requirements: PRD-SYS-001
    """
    study_id = "study_builder_master_001"

    # Step 1: Baseline Protocol Payload
    base_payload = {
        "id": study_id,
        "name": "Phase II Oncology Study Baseline",
        "protocolTitle": "A Phase II Study of Compound X in Subjects with Solid Tumors",
        "usdmVersion": "3.0",
        "studyDesigns": [
            {
                "id": "design_01",
                "name": "Parallel Dose Escalation Design",
                "objectives": [
                    {"id": "obj_01", "name": "Evaluate Safety and Tolerability"}
                ],
                "encounters": [{"id": "enc_01"}, {"id": "enc_02"}],
                "activities": [{"id": "act_01", "name": "Vital Signs Assessment"}],
            }
        ],
        "eligibilityCriteria": [
            {"id": "crit_01", "criterionType": "Inclusion", "text": "Age >= 18 years"},
        ],
    }

    branch_mgr = ProtocolBranchManager()
    sentinel = ProtocolQualitySentinel()
    cascade_engine = ArtifactCascadeEngine()
    exporter = M11ProtocolExporter()

    # Step 2: Create Amendment Working Branch
    branch = branch_mgr.create_amendment_branch(
        study_id=study_id,
        branch_name="amendment-v2.0-draft",
        created_by="lead_protocol_author",
    )
    assert branch.status == "draft"

    # Step 3: Author Amendment Draft Changes (Add criterion, add activity)
    amended_payload = dict(base_payload)
    amended_payload["eligibilityCriteria"] = [
        {
            "id": "crit_01",
            "criterionType": "Inclusion",
            "text": "Age >= 18 and Age <= 75 years",
        },  # Modified
        {
            "id": "crit_02",
            "criterionType": "Exclusion",
            "text": "Prior chemotherapy within 30 days",
        },  # Added
    ]
    amended_payload["studyDesigns"][0]["activities"].append(
        {"id": "act_02", "name": "Central Lab Blood Draw"}
    )

    # Step 4: Perform Block-Level Diffing Comparison
    diff_report = branch_mgr.compare_branches(base_payload, amended_payload)
    assert diff_report.study_id == study_id
    assert diff_report.total_changes >= 2

    # Step 5: Audit Protocol Quality Sentinel & Feasibility
    quality_report = sentinel.evaluate_protocol_quality(amended_payload)
    assert quality_report.passed is True
    assert quality_report.quality_score == 100.0
    assert quality_report.patient_burden_index > 0.0

    # Step 6: Trigger Downstream eCRF & SoA Cascade
    cascade_report = cascade_engine.cascade_protocol_to_downstream(
        amended_payload, amendment_version=2
    )
    assert cascade_report.forms_created >= 3  # DM + VS + LB
    assert cascade_report.amendment_version == 2

    # Step 7: Merge Amendment Branch into Master Baseline
    merge_result = branch_mgr.merge_amendment_branch(
        branch=branch,
        change_reason="Protocol Amendment V2 approved by Institutional Review Board",
        approved_by="medical_director_01",
    )
    assert merge_result["status"] == "merged"
    assert branch.status == "merged"

    # Step 8: Export Final ICH M11 Word Document (.docx)
    docx_bytes = exporter.export_ich_m11_docx(amended_payload)
    assert isinstance(docx_bytes, bytes)
    assert len(docx_bytes) > 2000
    assert docx_bytes.startswith(b"PK\x03\x04")
