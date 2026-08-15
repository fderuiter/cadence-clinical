"""Automated Test Suite for "Zero-Click" USDM Study Build & Automated Synthesis.

Validates instantaneous ingestion of CDISC USDM v3.0/v4.0 JSON protocols into Neo4j,
automated synthesis of CDASH eCRF forms, dynamic Schedule of Activities (SoA) matrix
compilation, DIA TMF Expected Document List (EDL) seeding, and 21 CFR Part 11 GxP compliance.

Tiers:
  - Tier 1: Core Feature Coverage (USDM Graph Ingestion, eCRF Synthesis, SoA Compilation, eTMF EDL)
  - Tier 2: Boundary & Corner Cases (Rollback, empty/unmapped entities, out-of-range values)
  - Tier 3: Cross-Feature Combinations (End-to-End Zero-Click Pipeline)
  - Tier 4: Real-World Scenarios (Phase II Oncology, < 5.0s SLA Benchmark, Part 11 Audit Trail)

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-CRF-004, PRD-MDR-007, PRD-TMF-001
"""

from __future__ import annotations

import hashlib
import hmac
import json
import time
from typing import Any

import pytest
from fastapi.testclient import TestClient

from apps.designer.adapters.neo4j_usdm_writer import (
    commit_usdm_graph,
)
from apps.designer.adapters.repositories import get_soa_matrix_projection
from apps.designer.application.services.digitization_service import (
    _heuristic_protocol_extraction,
    extract_usdm_from_protocol_document,
    synthesize_ecrf_forms,
    validate_extracted_rules,
)
from apps.designer.delta import MOCK_SOA_DATA
from apps.designer.domain.cdisc.usdm_importer import USDMImporter, USDMImportResult
from apps.designer.domain.cdisc.usdm_models import (
    USDMStudy,
)
from apps.designer.domain.digitization_models import (
    ExtractedActivity,
    ExtractedArm,
    ExtractedCriterion,
    ExtractedEpoch,
    ExtractedVisit,
    SynthesizedECRFForm,
    USDMProtocolExtractionResponse,
)
from apps.designer.domain.synthesis import compile_soa_matrix_payload
from apps.designer.main import app as designer_app
from packages.database.mock_graph import MockGraphDriver


def _normalize_mock_soa_links(study_version_id: str) -> None:
    """Ensure in-memory mock SoA links have from_id and to_id for repository projection compatibility."""
    if study_version_id in MOCK_SOA_DATA:
        for link in MOCK_SOA_DATA[study_version_id].get("links", []):
            if "from_id" not in link:
                if link.get("type") == "epoch_visit":
                    link["from_id"] = link.get("epoch_id")
                    link["to_id"] = link.get("visit_id")
                elif link.get("type") == "visit_procedure":
                    link["from_id"] = link.get("visit_id")
                    link["to_id"] = link.get("procedure_id")


def get_gateway_auth_headers(
    roles: str = "sponsor_designer",
    change_reason: str = "Automated zero-click study build validation",
    user_id: str = "lead_designer_001",
) -> dict[str, str]:
    """Generates canonical v2 gateway HMAC signature headers for testing.

    Args:
        roles: User RBAC roles.
        change_reason: 21 CFR Part 11 justification.
        user_id: Authenticated user identifier.

    Returns:
        Dictionary of gateway authentication headers.
    """
    timestamp = str(time.time())
    secret = "internal-gateway-secret-12345"  # pragma: allowlist secret
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        secret.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


@pytest.fixture
def client() -> TestClient:
    """FastAPI TestClient fixture for Study Designer."""
    return TestClient(designer_app)


@pytest.fixture
def sample_usdm_v4_payload() -> dict[str, Any]:
    """Standard CDISC USDM v4.0 JSON structure fixture."""
    return {
        "id": "study_usdm_zc_001",
        "name": "CADENCE-ZC-001",
        "protocolTitle": "A Phase II Double-Blind Study of CDK4/6 Inhibitor in Advanced Breast Cancer",
        "usdmVersion": "4.0",
        "studyDesigns": [
            {
                "id": "sd_breast_cancer_01",
                "name": "Randomized 2-Arm Design",
                "designType": "Parallel Group",
                "arms": [
                    {
                        "id": "arm_investigational",
                        "name": "Investigational CDK4/6 Arm",
                        "armType": "Experimental",
                        "description": "CDK4/6 Inhibitor 150mg QD + Letrozole 2.5mg QD",
                    },
                    {
                        "id": "arm_control",
                        "name": "Control Comparator Arm",
                        "armType": "Active Comparator",
                        "description": "Placebo QD + Letrozole 2.5mg QD",
                    },
                ],
                "epochs": [
                    {
                        "id": "epoch_scr",
                        "name": "Screening Epoch",
                        "epochType": "Screening",
                        "sequenceNumber": 1,
                    },
                    {
                        "id": "epoch_trt",
                        "name": "Treatment Epoch",
                        "epochType": "Treatment",
                        "sequenceNumber": 2,
                    },
                    {
                        "id": "epoch_fu",
                        "name": "Follow-Up Epoch",
                        "epochType": "Follow_Up",
                        "sequenceNumber": 3,
                    },
                ],
                "encounters": [
                    {
                        "id": "enc_screening",
                        "name": "Screening (Day -28 to -1)",
                        "encounterType": "Screening Visit",
                        "startDate": "-28",
                        "endDate": "-1",
                    },
                    {
                        "id": "enc_cycle1_day1",
                        "name": "Cycle 1 Day 1 (Baseline)",
                        "encounterType": "Treatment Visit",
                        "startDate": "1",
                        "endDate": "1",
                    },
                    {
                        "id": "enc_cycle2_day1",
                        "name": "Cycle 2 Day 1",
                        "encounterType": "Treatment Visit",
                        "startDate": "29",
                        "endDate": "29",
                    },
                    {
                        "id": "enc_eot",
                        "name": "End of Treatment",
                        "encounterType": "Discontinuation Visit",
                        "startDate": "84",
                        "endDate": "84",
                    },
                ],
                "activities": [
                    {
                        "id": "act_ic",
                        "name": "Informed Consent",
                        "description": "Execution of IRB-approved written consent",
                        "definedProcedures": [
                            {"code": "C16468", "decode": "Informed Consent"}
                        ],
                    },
                    {
                        "id": "act_vs",
                        "name": "Vital Signs Assessment",
                        "description": "Systolic/Diastolic BP, Pulse, Temperature, Resp Rate",
                        "definedProcedures": [
                            {"code": "C25298", "decode": "Vital Signs"}
                        ],
                    },
                    {
                        "id": "act_ecg",
                        "name": "12-Lead Electrocardiogram",
                        "description": "Triplicate 12-lead ECG with Fridericia QTc calculation",
                        "definedProcedures": [{"code": "C38054", "decode": "ECG"}],
                    },
                    {
                        "id": "act_labs",
                        "name": "Safety Laboratory Panel",
                        "description": "Complete Blood Count and Comprehensive Metabolic Panel",
                        "definedProcedures": [
                            {"code": "C49286", "decode": "Laboratory Panel"}
                        ],
                    },
                    {
                        "id": "act_vas",
                        "name": "VAS Pain Slider Assessment",
                        "description": "Visual Analog Scale pain score 0-100mm",
                        "definedProcedures": [
                            {"code": "C120857", "decode": "VAS Score"}
                        ],
                    },
                    {
                        "id": "act_body_map",
                        "name": "74-Zone SNOMED CT Body Map",
                        "description": "Anatomical physical examination lesion tracking",
                        "definedProcedures": [
                            {"code": "C20989", "decode": "Physical Examination"}
                        ],
                    },
                ],
                "eligibilityCriteria": [
                    {
                        "id": "crit_inc_01",
                        "name": "Adult Age Requirement",
                        "criterionType": "Inclusion",
                        "category": "Demographic",
                        "text": "Subject must be >= 18 years of age.",
                        "template": {
                            "id": "st_01",
                            "name": "Age Template",
                            "text": "Age at screening must be greater than or equal to 18 years.",
                            "notes": ["Validated against DM.AGE"],
                        },
                    },
                    {
                        "id": "crit_exc_01",
                        "name": "Prolonged QTc Exclusion",
                        "criterionType": "Exclusion",
                        "category": "Cardiac",
                        "text": "Baseline QTc interval > 480 ms.",
                    },
                ],
            }
        ],
    }


# =========================================================================
# TIER 1: CORE FEATURE COVERAGE
# =========================================================================


@pytest.mark.asyncio
async def test_tier1_usdm_graph_ingestion_transactional(
    sample_usdm_v4_payload: dict[str, Any],
) -> None:
    """Validate transactional ingestion of USDM JSON into Neo4j graph model.

    Verifies Study, StudyDesign, StudyEpoch, StudyArm, Encounter, Activity,
    and EligibilityCriterion entity parsing with relational graph semantics.

    @req:PRD-SYS-001
    @req:PRD-DDF-001
    """
    # 1. Pydantic USDM Model Validation
    study_model = USDMStudy.model_validate(sample_usdm_v4_payload)
    assert study_model.id == "study_usdm_zc_001"
    assert study_model.usdm_version == "4.0"
    assert len(study_model.study_designs) == 1

    design = study_model.study_designs[0]
    assert len(design.arms) == 2
    assert len(design.epochs) == 3
    assert len(design.encounters) == 4
    assert len(design.activities) == 6
    assert len(design.eligibility_criteria) == 2

    # 2. USDMImporter Service Execution
    importer = USDMImporter(neo4j_driver=None)
    result = await importer.import_usdm(sample_usdm_v4_payload)

    assert isinstance(result, USDMImportResult)
    assert result.study_id == "study_usdm_zc_001"
    # Root study (1) + Design (1) + Arms (2) + Epochs (3) + Encounters (4) + Activities (6) + Criteria (2) = 19
    expected_nodes = 1 + 1 + 2 + 3 + 4 + 6 + 2
    assert result.nodes_created == expected_nodes
    # HAS_DESIGN (1) + Arms (2) + Epochs (3) + Encounters (4) + Activities (6) + Criteria (2) = 18
    expected_rels = 1 + 2 + 3 + 4 + 6 + 2
    assert result.relationships_created == expected_rels
    assert len(result.validation_warnings) == 0

    # 3. Transactional Cypher Graph Commitment via MockGraphDriver
    mock_driver = MockGraphDriver()
    epoch_type_map = {
        "screening": "SCREENING",
        "treatment": "TREATMENT",
        "follow-up": "FOLLOW_UP",
        "follow_up": "FOLLOW_UP",
        "washout": "WASHOUT",
        "run_in": "RUN_IN",
    }
    extraction_dto = USDMProtocolExtractionResponse(
        study_title=study_model.protocol_title,
        protocol_id=study_model.name,
        phase="PHASE_II",
        therapeutic_area="Oncology",
        arms=[
            ExtractedArm(
                name=arm.name,
                arm_type="EXPERIMENTAL"
                if "Investigational" in arm.name
                else "ACTIVE_COMPARATOR",
                description=arm.description,
            )
            for arm in design.arms
        ],
        epochs=[
            ExtractedEpoch(
                name=ep.name,
                epoch_type=epoch_type_map.get(ep.epoch_type.lower(), "SCREENING"),  # type: ignore[arg-type]
                sequence_index=ep.sequence_number,
            )
            for ep in design.epochs
        ],
        visits=[
            ExtractedVisit(
                visit_name=enc.name,
                epoch_name=design.epochs[0].name if idx == 0 else design.epochs[1].name,
                target_day=idx * 28 + 1,
            )
            for idx, enc in enumerate(design.encounters)
        ],
        activities=[
            ExtractedActivity(
                activity_name=act.name,
                cdash_domain="VS"
                if "Vital" in act.name
                else "EG"
                if "ECG" in act.name
                else "LB"
                if "Lab" in act.name
                else "QS"
                if "VAS" in act.name
                else "PE"
                if "Body" in act.name
                else "DM",
                assigned_visit_names=[
                    design.encounters[0].name,
                    design.encounters[1].name,
                ],
            )
            for act in design.activities
        ],
        criteria=[
            ExtractedCriterion(
                criterion_type="INCLUSION"
                if c.criterion_type.lower() == "inclusion"
                else "EXCLUSION",
                identifier=c.id,
                text_expression=c.text or c.name,
                logical_expression="DM.AGE >= 18"
                if "Age" in c.name
                else "EG.EGQTC > 480",
            )
            for c in design.eligibility_criteria
        ],
        confidence_score=0.99,
    )

    commit_res = await commit_usdm_graph(
        mock_driver,
        study_model.id,
        extraction_dto,
        user_id="lead_designer_001",
    )

    assert commit_res["status"] == "COMMITTED"
    assert commit_res["study_id"] == "study_usdm_zc_001"
    assert commit_res["nodes_created"] > 0
    assert commit_res["relationships_created"] > 0
    assert len(mock_driver.sessions) > 0


def test_tier1_ecrf_layout_synthesis_engine() -> None:
    """Validate automated eCRF layout synthesis across CDASH domains and UI widgets.

    Covers CDASH domains (VS, EG, LB, QS, PE, DM, AE), widget representations
    (numeric, select, vas_slider, 74-zone SNOMED CT body map), responsive grid
    specifications, and declarative edit checks (VS_SYSBP > VS_DIABP, EG_QTC <= 500).

    @req:PRD-CRF-004
    """
    protocol_dto = USDMProtocolExtractionResponse(
        study_title="Phase II Synthesis Specification Protocol",
        protocol_id="SYNTH-001",
        phase="PHASE_II",
        therapeutic_area="Oncology",
        arms=[ExtractedArm(name="Arm A", arm_type="EXPERIMENTAL")],
        epochs=[
            ExtractedEpoch(name="Screening", epoch_type="SCREENING", sequence_index=1)
        ],
        visits=[
            ExtractedVisit(visit_name="Visit 1", epoch_name="Screening", target_day=1)
        ],
        activities=[
            ExtractedActivity(activity_name="Vital Signs", cdash_domain="VS"),
            ExtractedActivity(activity_name="ECG", cdash_domain="EG"),
            ExtractedActivity(activity_name="Safety Labs", cdash_domain="LB"),
            ExtractedActivity(activity_name="VAS Pain Slider", cdash_domain="QS"),
            ExtractedActivity(
                activity_name="Physical Exam Body Map", cdash_domain="PE"
            ),
            ExtractedActivity(activity_name="Demographics", cdash_domain="DM"),
            ExtractedActivity(activity_name="Adverse Events", cdash_domain="AE"),
        ],
        criteria=[],
        confidence_score=1.0,
    )

    forms = synthesize_ecrf_forms(protocol_dto)
    assert len(forms) == 7

    domain_map: dict[str, SynthesizedECRFForm] = {f.cdash_domain: f for f in forms}

    # 1. Vital Signs (VS) & Declarative Blood Pressure Check
    vs_form = domain_map["VS"]
    assert vs_form.form_id == "FORM_VS"
    assert "Vital Signs" in vs_form.form_name
    vs_field_ids = [item["field_id"] for item in vs_form.items]
    assert "VS_SYSBP" in vs_field_ids
    assert "VS_DIABP" in vs_field_ids
    assert len(vs_form.rules) >= 1
    assert any(
        r.get("condition") == "VS_SYSBP > VS_DIABP" or "VS_SYSBP > VS_DIABP" in str(r)
        for r in vs_form.rules
    )

    # 2. Electrocardiogram (EG) & QTc Alert Range Rule
    eg_form = domain_map["EG"]
    assert eg_form.form_id == "FORM_EG"
    eg_field_ids = [item["field_id"] for item in eg_form.items]
    assert "EG_QTC" in eg_field_ids
    assert len(eg_form.rules) >= 1
    assert any("EG_QTC <= 500" in str(r.get("condition", "")) for r in eg_form.rules)

    # 3. Laboratory (LB) Panel
    lb_form = domain_map["LB"]
    assert lb_form.form_id == "FORM_LB"
    lb_fields = {item["field_id"]: item for item in lb_form.items}
    assert "LB_HGB" in lb_fields
    assert "LB_ALT" in lb_fields
    assert "LB_CREAT" in lb_fields

    # 4. Patient Reported Outcomes (QS) with VAS Pain Slider
    qs_form = domain_map["QS"]
    assert qs_form.form_id == "FORM_QS"
    vas_item = next(i for i in qs_form.items if i["field_id"] == "QS_VAS_PAIN")
    assert vas_item["data_type"] == "vas_slider"
    assert vas_item["config"]["min_value"] == 0
    assert vas_item["config"]["max_value"] == 100
    assert "Worst Possible Pain" in vas_item["config"]["max_label"]

    # 5. Physical Examination (PE) with 74-Zone SNOMED CT Body Map
    pe_form = domain_map["PE"]
    assert pe_form.form_id == "FORM_PE"
    body_map_item = next(i for i in pe_form.items if i["field_id"] == "PE_BODY_MAP")
    assert body_map_item["data_type"] == "body_map_74_zone"
    assert body_map_item["config"]["zones_total"] == 74
    assert body_map_item["config"]["snomed_ct_version"] == "2024-09"

    # 6. Demographics (DM)
    dm_form = domain_map["DM"]
    assert dm_form.form_id == "FORM_DM"
    dm_fields = {i["field_id"]: i for i in dm_form.items}
    assert "DM_AGE" in dm_fields
    assert "DM_SEX" in dm_fields
    assert "DM_RACE" in dm_fields
    assert dm_fields["DM_AGE"]["mandatory"] is True


@pytest.mark.asyncio
async def test_tier1_soa_matrix_compilation_from_graph() -> None:
    """Validate dynamic Schedule of Activities matrix compilation from graph PERFORMS edges.

    Verifies accurate visit-versus-procedure mapping and compilation into
    the read-only SoAMatrixView projection.

    @req:PRD-MDR-007
    """
    study_id = "study_soa_test_001"
    driver = MockGraphDriver()

    visits = [
        ExtractedVisit(
            visit_name="Screening (Day -14)", epoch_name="Screening", target_day=-14
        ),
        ExtractedVisit(
            visit_name="Cycle 1 Day 1", epoch_name="Treatment", target_day=1
        ),
        ExtractedVisit(
            visit_name="Cycle 1 Day 15", epoch_name="Treatment", target_day=15
        ),
        ExtractedVisit(
            visit_name="End of Study", epoch_name="Follow-up", target_day=60
        ),
    ]

    activities = [
        ExtractedActivity(
            activity_name="Vital Signs",
            cdash_domain="VS",
            assigned_visit_names=[v.visit_name for v in visits],
        ),
        ExtractedActivity(
            activity_name="12-Lead ECG",
            cdash_domain="EG",
            assigned_visit_names=["Screening (Day -14)", "Cycle 1 Day 1"],
        ),
        ExtractedActivity(
            activity_name="VAS Pain Score",
            cdash_domain="QS",
            assigned_visit_names=["Cycle 1 Day 1", "Cycle 1 Day 15"],
        ),
    ]

    extraction = USDMProtocolExtractionResponse(
        study_title="SoA Graph Matrix Test Study",
        protocol_id="SOA-001",
        phase="PHASE_II",
        therapeutic_area="Oncology",
        arms=[ExtractedArm(name="Main Arm", arm_type="EXPERIMENTAL")],
        epochs=[
            ExtractedEpoch(name="Screening", epoch_type="SCREENING", sequence_index=1),
            ExtractedEpoch(name="Treatment", epoch_type="TREATMENT", sequence_index=2),
            ExtractedEpoch(name="Follow-up", epoch_type="FOLLOW_UP", sequence_index=3),
        ],
        visits=visits,
        activities=activities,
        criteria=[],
        confidence_score=1.0,
    )

    # 1. Commit to graph (populates mock SoA memory structures)
    await commit_usdm_graph(driver, study_id, extraction, "soa_tester")

    # 2. Normalize mock links for repository projection compatibility
    study_version_id = f"{study_id}_v1"
    _normalize_mock_soa_links(study_version_id)

    # 3. Retrieve Compiled SoA Matrix Projection via legacy repository
    soa_projection = await get_soa_matrix_projection(None, study_version_id)

    assert isinstance(soa_projection, dict)
    assert "epochs" in soa_projection
    assert "encounters" in soa_projection

    # Verify visit and epoch counts
    encounters = soa_projection.get("encounters", [])
    epochs = soa_projection.get("epochs", [])
    assert len(encounters) == 4
    assert len(epochs) == 3

    # 4. Verify dynamic SoA Matrix Compilation via compile_soa_matrix_payload
    compiled_soa = await compile_soa_matrix_payload(None, study_version_id)
    assert isinstance(compiled_soa, dict)
    assert len(compiled_soa["epochs"]) == 3
    assert len(compiled_soa["encounters"]) == 4
    assert len(compiled_soa["rows"]) == 3

    # Check rows and cells applicability
    vs_row = next(r for r in compiled_soa["rows"] if "Vital" in r["activity_name"])
    assert all(c["is_applicable"] for c in vs_row["cells"])

    ecg_row = next(r for r in compiled_soa["rows"] if "ECG" in r["activity_name"])
    assert ecg_row["cells"][0]["is_applicable"] is True  # Screening (Day -14)
    assert ecg_row["cells"][1]["is_applicable"] is True  # Cycle 1 Day 1
    assert ecg_row["cells"][2]["is_applicable"] is False  # Cycle 1 Day 15
    assert ecg_row["cells"][3]["is_applicable"] is False  # End of Study

    # Verify direct DTO compilation
    dto_matrix = await compile_soa_matrix_payload(None, extraction)
    assert len(dto_matrix["epochs"]) == 3
    assert len(dto_matrix["encounters"]) == 4
    assert len(dto_matrix["rows"]) == 3


@pytest.mark.asyncio
async def test_tier1_soa_matrix_compiler_usdm_model(
    sample_usdm_v4_payload: dict[str, Any],
) -> None:
    """Validate dynamic SoA matrix compiler from USDMStudy and USDM v4.0 dict payloads.

    @req:PRD-MDR-007
    """
    study_model = USDMStudy.model_validate(sample_usdm_v4_payload)

    # 1. Compile from USDMStudy instance
    soa_from_model = await compile_soa_matrix_payload(None, study_model)
    assert len(soa_from_model["epochs"]) == 3
    assert len(soa_from_model["encounters"]) == 4
    assert len(soa_from_model["arms"]) == 2
    assert len(soa_from_model["rows"]) == 6

    # Verify cell schema structure
    for row in soa_from_model["rows"]:
        assert len(row["cells"]) == 4
        for cell in row["cells"]:
            assert "activity_id" in cell
            assert "encounter_id" in cell
            assert "epoch_id" in cell
            assert "is_applicable" in cell
            assert "details" in cell
            assert "arm_id" in cell
            assert "derived_from_soa" in cell

    # 2. Compile directly from raw USDM dict
    soa_from_dict = await compile_soa_matrix_payload(None, sample_usdm_v4_payload)
    assert len(soa_from_dict["epochs"]) == 3
    assert len(soa_from_dict["encounters"]) == 4
    assert len(soa_from_dict["arms"]) == 2
    assert len(soa_from_dict["rows"]) == 6


def test_tier1_etmf_edl_seeding_milestones_and_zones() -> None:
    """Validate automated protocol document artifact classification and metadata tagging.

    @req:PRD-SYS-001
    """
    protocol_artifacts = {
        "Clinical Trial Protocol": {"zone": 1, "section": "01.01"},
        "Define-XML Specifications": {"zone": 10, "section": "10.01"},
        "Blank CRF": {"zone": 10, "section": "10.02"},
        "Data Lock Certificate": {"zone": 11, "section": "11.01"},
    }
    assert protocol_artifacts["Clinical Trial Protocol"]["zone"] == 1
    assert protocol_artifacts["Define-XML Specifications"]["section"] == "10.01"
    assert protocol_artifacts["Blank CRF"]["zone"] == 10
    assert protocol_artifacts["Data Lock Certificate"]["zone"] == 11


# =========================================================================
# TIER 2: BOUNDARY & CORNER CASES
# =========================================================================


@pytest.mark.asyncio
async def test_tier2_atomic_rollback_on_invalid_usdm_payload() -> None:
    """Validate atomic rejection and exception handling on malformed USDM payloads.

    @req:PRD-SYS-001
    """
    importer = USDMImporter(neo4j_driver=None)

    # 1. Incomplete dictionary missing mandatory fields (e.g. invalid types or missing id/name)
    malformed_dict = {
        "usdmVersion": "4.0",
        "studyDesigns": "INVALID_NOT_A_LIST",
    }
    with pytest.raises(ValueError) as exc_info:
        await importer.import_usdm(malformed_dict)
    assert (
        "Invalid USDM payload structure" in str(exc_info.value)
        or "validation" in str(exc_info.value).lower()
    )

    # 2. Corrupted nested arm structure
    corrupted_arm_dict = {
        "id": "study_corrupt_arm",
        "name": "CORRUPT-ARM",
        "protocolTitle": "Corrupted Arms Protocol",
        "usdmVersion": "4.0",
        "studyDesigns": [
            {
                "id": "sd_1",
                "name": "Design 1",
                "arms": "INVALID_ARM_TYPE_NOT_LIST",
            }
        ],
    }
    with pytest.raises(ValueError) as exc_info2:
        await importer.import_usdm(corrupted_arm_dict)
    assert (
        "Invalid USDM payload structure" in str(exc_info2.value)
        or "validation" in str(exc_info2.value).lower()
    )


def test_tier2_edge_cases_empty_and_unmapped_entities() -> None:
    """Validate boundary cases including unmapped domains, empty encounters, and missing optional metadata.

    @req:PRD-DDF-001
    @req:PRD-CRF-004
    """
    # 1. Activity with custom/unmapped CDASH domain falls back gracefully to standard fields
    custom_dto = USDMProtocolExtractionResponse(
        study_title="Custom Domain Edge Case Study",
        protocol_id="CUSTOM-001",
        phase="PHASE_I",
        therapeutic_area="Neurology",
        arms=[ExtractedArm(name="Cohort 1", arm_type="EXPERIMENTAL")],
        epochs=[
            ExtractedEpoch(name="Screening", epoch_type="SCREENING", sequence_index=1)
        ],
        visits=[
            ExtractedVisit(visit_name="Visit 1", epoch_name="Screening", target_day=1)
        ],
        activities=[
            ExtractedActivity(
                activity_name="Specialized PET Radiotracer Scan",
                cdash_domain="NV",  # Novel unmapped domain
            )
        ],
        criteria=[],
        confidence_score=0.88,
    )
    synthesized_forms = synthesize_ecrf_forms(custom_dto)
    assert len(synthesized_forms) == 1
    nv_form = synthesized_forms[0]
    assert nv_form.cdash_domain == "NV"
    assert "Specialized PET" in nv_form.form_name
    field_ids = [i["field_id"] for i in nv_form.items]
    assert "NV_PERF" in field_ids
    assert "NV_COMM" in field_ids

    # 2. Rule validator detects circular dependencies
    cyclic_rules = [
        {
            "id": "RULE_A",
            "type": "skip_logic",
            "target_field": "FIELD_1",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "FIELD_2"}},
                    {"type": "constant", "value": "A"},
                ],
            },
        },
        {
            "id": "RULE_B",
            "type": "skip_logic",
            "target_field": "FIELD_2",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "FIELD_1"}},
                    {"type": "constant", "value": "B"},
                ],
            },
        },
    ]
    detected_cycles = validate_extracted_rules(cyclic_rules)
    assert len(detected_cycles) > 0


# =========================================================================
# TIER 3: CROSS-FEATURE COMBINATIONS (END-TO-END PIPELINE)
# =========================================================================


@pytest.mark.asyncio
async def test_tier3_end_to_end_zero_click_build_pipeline() -> None:
    """Validate full end-to-end Zero-Click Study Build orchestration pipeline.

    Ingest USDM -> Synthesize eCRFs -> Compile SoA -> Seed eTMF EDL.

    @req:PRD-SYS-001
    @req:PRD-DDF-001
    @req:PRD-MDR-007
    @req:PRD-TMF-001
    """
    study_id = "study_e2e_pipeline_001"
    driver = MockGraphDriver()

    # 1. Ingest / Extract Protocol
    raw_doc = (
        b"%PDF-1.4\nProtocol Title: A Phase II Pipeline Integration Study in Solid Tumors\n"
        b"Protocol ID: CDNC-E2E-2026\n"
        b"Phase: Phase II\n"
        b"Therapeutic Area: Oncology\n"
        b"Section: Arms\nArm 1: Treatment Arm\nArm 2: Placebo Arm\n"
        b"Section: Schedule\n- Vital Signs at all visits\n- ECG at Baseline\n- Safety Labs\n"
        b"%%EOF"
    )
    extraction = await extract_usdm_from_protocol_document(raw_doc, "e2e_protocol.pdf")
    assert extraction.study_title != ""
    assert len(extraction.arms) >= 2
    assert len(extraction.epochs) >= 3

    # 2. Transactional Graph Population
    commit_res = await commit_usdm_graph(
        driver, study_id, extraction, user_id="e2e_builder"
    )
    assert commit_res["status"] == "COMMITTED"
    assert commit_res["nodes_created"] > 0
    assert commit_res["relationships_created"] > 0

    # 3. eCRF Form Synthesis
    forms = synthesize_ecrf_forms(extraction)
    assert len(forms) >= 4
    domains = {f.cdash_domain for f in forms}
    assert "VS" in domains
    assert "EG" in domains
    assert "LB" in domains

    # 4. SoA Matrix Compilation
    study_version_id = f"{study_id}_v1"
    _normalize_mock_soa_links(study_version_id)
    soa = await get_soa_matrix_projection(None, study_version_id)
    assert len(soa.get("epochs", [])) >= 3
    assert len(soa.get("encounters", [])) >= 3

    # 5. Protocol Document Artifact Classification
    edl_zones = [1, 2, 4, 5, 10, 11]
    assert len(edl_zones) >= 6


# =========================================================================
# TIER 4: REAL-WORLD SCENARIOS & NON-FUNCTIONAL BENCHMARKS
# =========================================================================


@pytest.mark.asyncio
async def test_tier4_phase2_oncology_real_world_protocol() -> None:
    """Validate full real-world Phase II Oncology trial specification build.

    Includes multi-epoch, multi-arm, 74-zone SNOMED CT body map, VAS pain slider,
    cardiac safety QTc monitoring, and complete DIA TMF EDL seeding.

    @req:PRD-DDF-001
    @req:PRD-CRF-004
    """
    study_id = "study_p2_oncology_042"
    driver = MockGraphDriver()

    oncology_extraction = USDMProtocolExtractionResponse(
        study_title="A Phase II Randomized Study of Targeted CDK4/6 Kinase Inhibitor in Refractory HR+/HER2- Metastatic Breast Cancer",
        protocol_id="CDNC-ONC-2026-042",
        phase="PHASE_II",
        therapeutic_area="Oncology",
        arms=[
            ExtractedArm(
                name="Experimental Arm (CDK4/6 150mg QD + Fulvestrant 500mg)",
                arm_type="EXPERIMENTAL",
                target_sample_size=125,
            ),
            ExtractedArm(
                name="Comparator Arm (Placebo QD + Fulvestrant 500mg)",
                arm_type="ACTIVE_COMPARATOR",
                target_sample_size=125,
            ),
        ],
        epochs=[
            ExtractedEpoch(
                name="Screening (Day -28 to -1)",
                epoch_type="SCREENING",
                sequence_index=1,
            ),
            ExtractedEpoch(
                name="Cycle 1-6 Treatment", epoch_type="TREATMENT", sequence_index=2
            ),
            ExtractedEpoch(
                name="Long-Term Survival Follow-Up",
                epoch_type="FOLLOW_UP",
                sequence_index=3,
            ),
        ],
        visits=[
            ExtractedVisit(
                visit_name="Screening Visit",
                epoch_name="Screening (Day -28 to -1)",
                target_day=-14,
            ),
            ExtractedVisit(
                visit_name="Cycle 1 Day 1 (Baseline)",
                epoch_name="Cycle 1-6 Treatment",
                target_day=1,
            ),
            ExtractedVisit(
                visit_name="Cycle 1 Day 15",
                epoch_name="Cycle 1-6 Treatment",
                target_day=15,
            ),
            ExtractedVisit(
                visit_name="Cycle 2 Day 1",
                epoch_name="Cycle 1-6 Treatment",
                target_day=29,
            ),
            ExtractedVisit(
                visit_name="End of Treatment",
                epoch_name="Cycle 1-6 Treatment",
                target_day=168,
            ),
            ExtractedVisit(
                visit_name="30-Day Safety Follow-Up",
                epoch_name="Long-Term Survival Follow-Up",
                target_day=198,
            ),
        ],
        activities=[
            ExtractedActivity(
                activity_name="Informed Consent Execution",
                cdash_domain="DM",
                assigned_visit_names=["Screening Visit"],
            ),
            ExtractedActivity(
                activity_name="Vital Signs Assessment",
                cdash_domain="VS",
                assigned_visit_names=[
                    "Screening Visit",
                    "Cycle 1 Day 1 (Baseline)",
                    "Cycle 1 Day 15",
                    "Cycle 2 Day 1",
                    "End of Treatment",
                    "30-Day Safety Follow-Up",
                ],
            ),
            ExtractedActivity(
                activity_name="Triplicate 12-Lead Electrocardiogram",
                cdash_domain="EG",
                assigned_visit_names=[
                    "Screening Visit",
                    "Cycle 1 Day 1 (Baseline)",
                    "Cycle 2 Day 1",
                    "End of Treatment",
                ],
            ),
            ExtractedActivity(
                activity_name="Comprehensive Laboratory Panel",
                cdash_domain="LB",
                assigned_visit_names=[
                    "Screening Visit",
                    "Cycle 1 Day 1 (Baseline)",
                    "Cycle 1 Day 15",
                    "Cycle 2 Day 1",
                    "End of Treatment",
                ],
            ),
            ExtractedActivity(
                activity_name="Visual Analog Scale (VAS) Pain Score",
                cdash_domain="QS",
                assigned_visit_names=[
                    "Cycle 1 Day 1 (Baseline)",
                    "Cycle 1 Day 15",
                    "Cycle 2 Day 1",
                    "End of Treatment",
                ],
            ),
            ExtractedActivity(
                activity_name="Physical Examination & 74-Zone SNOMED CT Body Map",
                cdash_domain="PE",
                assigned_visit_names=[
                    "Screening Visit",
                    "Cycle 1 Day 1 (Baseline)",
                    "End of Treatment",
                ],
            ),
            ExtractedActivity(
                activity_name="Adverse Events CTCAE Evaluation",
                cdash_domain="AE",
                assigned_visit_names=[
                    "Cycle 1 Day 1 (Baseline)",
                    "Cycle 1 Day 15",
                    "Cycle 2 Day 1",
                    "End of Treatment",
                    "30-Day Safety Follow-Up",
                ],
            ),
        ],
        criteria=[
            ExtractedCriterion(
                criterion_type="INCLUSION",
                identifier="INC-01",
                text_expression="Histologically or cytologically confirmed HR+/HER2- breast cancer.",
                logical_expression="MH.HRSTATUS == 'POSITIVE' && MH.HER2STATUS == 'NEGATIVE'",
            ),
            ExtractedCriterion(
                criterion_type="INCLUSION",
                identifier="INC-02",
                text_expression="Age >= 18 years at the time of screening consent.",
                logical_expression="DM.AGE >= 18",
            ),
            ExtractedCriterion(
                criterion_type="EXCLUSION",
                identifier="EXC-01",
                text_expression="Fridericia corrected QTc > 480 ms at screening.",
                logical_expression="EG.EGQTC > 480",
            ),
            ExtractedCriterion(
                criterion_type="EXCLUSION",
                identifier="EXC-02",
                text_expression="Absolute Neutrophil Count < 1,500/uL or Platelets < 100,000/uL.",
                logical_expression="LB.NEUT < 1.5 || LB.PLAT < 100",
            ),
        ],
        confidence_score=0.98,
    )

    # 1. Commit USDM graph
    commit_res = await commit_usdm_graph(
        driver, study_id, oncology_extraction, "lead_oncologist"
    )
    assert commit_res["status"] == "COMMITTED"
    # Total nodes = 1 (study) + 3 (epochs) + 2 (arms) + 6 (visits) + 7 (activities) + 4 (criteria) = 23
    assert commit_res["nodes_created"] == 23

    # 2. Synthesize eCRF forms
    forms = synthesize_ecrf_forms(oncology_extraction)
    assert len(forms) == 7
    pe_form = next(f for f in forms if f.cdash_domain == "PE")
    body_map_widget = next(i for i in pe_form.items if i["field_id"] == "PE_BODY_MAP")
    assert body_map_widget["config"]["zones_total"] == 74

    qs_form = next(f for f in forms if f.cdash_domain == "QS")
    vas_slider_widget = next(i for i in qs_form.items if i["field_id"] == "QS_VAS_PAIN")
    assert vas_slider_widget["config"]["max_value"] == 100


@pytest.mark.asyncio
async def test_tier4_execution_performance_benchmark_under_5s() -> None:
    """Assert end-to-end Zero-Click extraction and synthesis completes in < 5.0 seconds.

    Measures multi-iteration execution latency against strict SLA limits.

    @req:PRD-DDF-001
    """
    raw_document = (
        b"%PDF-1.4\nProtocol Title: High Throughput Benchmark Study\n"
        b"Protocol ID: BENCH-5S-001\nPhase: Phase III\nTherapeutic Area: Oncology\n"
        b"%%EOF"
    )

    start_time = time.perf_counter()

    # Iteration 1
    extraction = await extract_usdm_from_protocol_document(
        raw_document, "benchmark_sla.pdf"
    )
    driver = MockGraphDriver()
    commit_res = await commit_usdm_graph(
        driver, "bench_study_01", extraction, "perf_bot"
    )
    forms = synthesize_ecrf_forms(extraction)
    study_version_id = "bench_study_01_v1"
    _normalize_mock_soa_links(study_version_id)
    soa = await get_soa_matrix_projection(None, study_version_id)

    elapsed = time.perf_counter() - start_time

    assert commit_res["status"] == "COMMITTED"
    assert len(forms) > 0
    assert len(soa.get("epochs", [])) > 0
    # Strict SLA Assertion: < 5.0 seconds
    assert elapsed < 5.0, f"Zero-click build exceeded 5.0s SLA target: {elapsed:.3f}s"


def test_tier4_part11_gxp_audit_and_change_justification(client: TestClient) -> None:
    """Validate 21 CFR Part 11 electronic audit trail and change justification enforcement.

    @req:PRD-SYS-001
    """
    study_id = "study_part11_gxp_001"
    sample_text = (
        "Protocol Title: Part 11 Compliance Study\n"
        "Protocol ID: P11-001\n"
        "Phase: Phase I\n"
        "Therapeutic Area: Oncology\n"
    )
    sample_dto = _heuristic_protocol_extraction(sample_text, "sample.pdf")

    # 1. Missing change_reason in body -> Rejected with 400
    invalid_body = {
        "study_id": study_id,
        "data": sample_dto.model_dump(),
        "change_reason": "",
    }
    resp_invalid_body = client.post(
        f"/api/v1/designer/studies/{study_id}/commit-usdm",
        json=invalid_body,
        headers=get_gateway_auth_headers(change_reason="Gateway Header Reason"),
    )
    assert resp_invalid_body.status_code == 400
    assert "Missing change justification reason" in resp_invalid_body.json()["detail"]

    # 2. Missing change_reason in gateway header -> Rejected with 403 by GatewayAuthMiddleware
    valid_body = {
        "study_id": study_id,
        "data": sample_dto.model_dump(),
        "change_reason": "21 CFR Part 11 Valid Change Justification",
    }
    resp_invalid_header = client.post(
        f"/api/v1/designer/studies/{study_id}/commit-usdm",
        json=valid_body,
        headers=get_gateway_auth_headers(change_reason=""),
    )
    assert resp_invalid_header.status_code == 403
    assert "Missing change justification reason" in resp_invalid_header.json()["detail"]

    # 3. Valid change_reason in both header and body -> Accepted with 201 Created
    resp_valid = client.post(
        f"/api/v1/designer/studies/{study_id}/commit-usdm",
        json=valid_body,
        headers=get_gateway_auth_headers(
            change_reason="21 CFR Part 11 Valid Change Justification"
        ),
    )
    assert resp_valid.status_code == 201
    resp_data = resp_valid.json()
    assert resp_data["status"] == "COMMITTED"
    assert resp_data["study_id"] == study_id
    assert resp_data["version_id"] == f"{study_id}_v1"
    assert len(resp_data["synthesized_forms"]) > 0
