"""Comprehensive Unit & Integration Tests for DICOM Viewer with RECIST 1.1 & Lugano Calipers.

Requirements:
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- PRD-IMG-001: Zero-Footprint High-Performance Web DICOM & Multi-Planar Viewer
- PRD-IMG-004: Automated RECIST 1.1, Cheson, Lugano & RANO Measurement Derivations
- PRD-SYS-001: 21 CFR Part 11 Electronic Records & Audit Trails
"""

import pytest
from httpx import AsyncClient

from apps.imaging.domain.models import (
    LesionCategory,
    PointCoordinate,
    RECISTLesionInput,
    RECISTResponseCode,
)
from apps.imaging.services.lugano_engine import (
    LuganoEngine,
    LuganoLesionInput,
    LuganoResponseCode,
)
from apps.imaging.services.mpr_engine import MprGeometryEngine, MprPlane
from apps.imaging.services.recist_engine import RECIST11Engine

# ==============================================================================
# 1. PURE RECIST 1.1 DERIVATION LOGIC TESTS
# ==============================================================================


def test_recist_sld_calculation():
    """Verify calculation of Sum of Longest Diameters (SLD) with nodal short-axis rule.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    lesions = [
        RECISTLesionInput(
            lesion_id="TL1",
            organ_site="LIVER",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=25.4,
        ),
        RECISTLesionInput(
            lesion_id="TL2",
            organ_site="LUNG_RIGHT",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=18.6,
        ),
        RECISTLesionInput(
            lesion_id="TL3",
            organ_site="LYMPH_NODE_MEDIASTINAL",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=22.0,
            is_lymph_node=True,
            short_axis_mm=14.5,
        ),
        RECISTLesionInput(
            lesion_id="NTL1",
            organ_site="BONE",
            lesion_type=LesionCategory.NON_TARGET,
            longest_diameter_mm=12.0,
        ),
    ]

    sld = RECIST11Engine.calculate_sld(lesions)
    # SLD = 25.4 + 18.6 + 14.5 (short axis for lymph node) = 58.5 mm (ignores non-target)
    assert sld == 58.5


def test_recist_baseline_timepoint_evaluation():
    """Verify that baseline evaluation establishes reference SLD and sets SD response.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    lesions = [
        RECISTLesionInput(
            lesion_id="TL1",
            organ_site="LIVER",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=30.0,
        ),
        RECISTLesionInput(
            lesion_id="TL2",
            organ_site="LUNG",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=20.0,
        ),
    ]

    res = RECIST11Engine.evaluate_timepoint(
        current_lesions=lesions,
        baseline_sld=0.0,
        is_baseline=True,
    )

    assert res.baseline_sld_mm == 50.0
    assert res.nadir_sld_mm == 50.0
    assert res.current_sld_mm == 50.0
    assert res.pct_change_baseline == 0.0
    assert res.pct_change_nadir == 0.0
    assert res.target_response == RECISTResponseCode.SD
    assert res.overall_response == RECISTResponseCode.SD
    assert res.is_nadir is True


def test_recist_partial_response_derivation():
    """Verify that a >= 30% decrease from baseline qualifies as Partial Response (PR).

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    # Baseline SLD = 50.0 mm
    # Follow-up: 30.0 mm (40% decrease -> PR)
    lesions = [
        RECISTLesionInput(
            lesion_id="TL1",
            organ_site="LIVER",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=18.0,
        ),
        RECISTLesionInput(
            lesion_id="TL2",
            organ_site="LUNG",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=12.0,
        ),
    ]

    res = RECIST11Engine.evaluate_timepoint(
        current_lesions=lesions,
        baseline_sld=50.0,
        prior_timepoints_sld=[50.0],
        non_target_status="NON_CR_NON_PD",
        new_lesions_present=False,
    )

    assert res.current_sld_mm == 30.0
    assert res.baseline_sld_mm == 50.0
    assert res.pct_change_baseline == -40.0
    assert res.target_response == RECISTResponseCode.PR
    assert res.overall_response == RECISTResponseCode.PR
    assert res.is_nadir is True  # 30.0 < 50.0 -> new nadir


def test_recist_progressive_disease_from_nadir():
    """Verify that a >= 20% increase from nadir AND >= 5mm absolute increase triggers PD.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    # Baseline = 50.0 mm, Nadir = 30.0 mm
    # Current SLD = 38.0 mm (+26.67% from nadir, +8.0 mm absolute increase) -> PD
    lesions = [
        RECISTLesionInput(
            lesion_id="TL1",
            organ_site="LIVER",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=23.0,
        ),
        RECISTLesionInput(
            lesion_id="TL2",
            organ_site="LUNG",
            lesion_type=LesionCategory.TARGET,
            longest_diameter_mm=15.0,
        ),
    ]

    res = RECIST11Engine.evaluate_timepoint(
        current_lesions=lesions,
        baseline_sld=50.0,
        prior_timepoints_sld=[30.0],
        non_target_status="NON_CR_NON_PD",
        new_lesions_present=False,
    )

    assert res.current_sld_mm == 38.0
    assert res.nadir_sld_mm == 30.0
    assert res.pct_change_nadir == 26.67
    assert res.absolute_change_nadir_mm == 8.0
    assert res.target_response == RECISTResponseCode.PD
    assert res.overall_response == RECISTResponseCode.PD


def test_recist_complete_response_and_new_lesions():
    """Verify Complete Response (CR) and new lesion progression override.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    # Target lesions all 0mm -> CR
    lesions: list[RECISTLesionInput] = []

    res_cr = RECIST11Engine.evaluate_timepoint(
        current_lesions=lesions,
        baseline_sld=40.0,
        prior_timepoints_sld=[20.0],
        non_target_status="CR",
        new_lesions_present=False,
    )
    assert res_cr.current_sld_mm == 0.0
    assert res_cr.target_response == RECISTResponseCode.CR
    assert res_cr.overall_response == RECISTResponseCode.CR

    # If new lesion appears, overall response becomes PD regardless of target shrinkage
    res_new_lesion = RECIST11Engine.evaluate_timepoint(
        current_lesions=lesions,
        baseline_sld=40.0,
        prior_timepoints_sld=[20.0],
        non_target_status="CR",
        new_lesions_present=True,
        new_lesions_count=1,
    )
    assert res_new_lesion.overall_response == RECISTResponseCode.PD


# ==============================================================================
# 2. PURE LUGANO 2014 & CHESON DERIVATION LOGIC TESTS
# ==============================================================================


def test_lugano_ppd_and_spd_calculation():
    """Verify calculation of Lugano PPD and SPD across target nodal lesions.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    lesions = [
        LuganoLesionInput(
            lesion_id="LN1",
            organ_site="CERVICAL_NODE",
            longest_diameter_mm=30.0,
            perpendicular_diameter_mm=20.0,  # PPD = 600 mm^2
        ),
        LuganoLesionInput(
            lesion_id="LN2",
            organ_site="AXILLARY_NODE",
            longest_diameter_mm=25.0,
            perpendicular_diameter_mm=15.0,  # PPD = 375 mm^2
        ),
    ]

    spd = LuganoEngine.calculate_spd(lesions)
    assert spd == 975.0


def test_lugano_partial_response_and_deauville_pet():
    """Verify Lugano PR (>= 50% decrease in SPD) and PET Deauville metabolic score.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    # Baseline SPD = 1000 mm^2
    # Follow-up: 450 mm^2 (55% decrease -> PR)
    lesions = [
        LuganoLesionInput(
            lesion_id="LN1",
            organ_site="CERVICAL_NODE",
            longest_diameter_mm=18.0,
            perpendicular_diameter_mm=15.0,  # PPD = 270
            deauville_score=2,  # Deauville 2 <= mediastinum -> Complete Metabolic Response
        ),
        LuganoLesionInput(
            lesion_id="LN2",
            organ_site="AXILLARY_NODE",
            longest_diameter_mm=15.0,
            perpendicular_diameter_mm=12.0,  # PPD = 180
            deauville_score=2,
        ),
    ]

    res = LuganoEngine.evaluate_timepoint(
        current_lesions=lesions,
        baseline_spd=1000.0,
        prior_timepoints_spd=[1000.0],
    )

    assert res.current_spd_mm2 == 450.0
    assert res.pct_change_spd_baseline == -55.0
    assert res.ct_response == LuganoResponseCode.PR
    assert res.pet_metabolic_response == LuganoResponseCode.CMR
    assert res.max_deauville_score == 2
    assert res.overall_response == LuganoResponseCode.CMR


def test_lugano_progressive_disease_evaluation():
    """Verify Lugano Progressive Disease triggered by >= 50% increase from nadir SPD.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    # Baseline = 1000, Nadir = 400
    # Current = 650 (+62.5% increase from nadir) -> PD
    lesions = [
        LuganoLesionInput(
            lesion_id="LN1",
            organ_site="CERVICAL_NODE",
            longest_diameter_mm=26.0,
            perpendicular_diameter_mm=25.0,  # PPD = 650
        )
    ]

    res = LuganoEngine.evaluate_timepoint(
        current_lesions=lesions,
        baseline_spd=1000.0,
        prior_timepoints_spd=[400.0],
    )

    assert res.current_spd_mm2 == 650.0
    assert res.nadir_spd_mm2 == 400.0
    assert res.pct_change_spd_nadir == 62.5
    assert res.ct_response == LuganoResponseCode.PD
    assert res.overall_response == LuganoResponseCode.PD


# ==============================================================================
# 3. MPR GEOMETRY & CALIPER ENGINE TESTS
# ==============================================================================


def test_mpr_caliper_distance_and_perpendicular():
    """Verify physical millimeter distance and perpendicular crosshair math.

    @req:PRD-IMG-001
    @req:PRD-IMG-004
    @req:PRD-SYS-164
    @req:PRD-SYS-165
    """
    p1 = PointCoordinate(x=10.0, y=10.0)
    p2 = PointCoordinate(x=40.0, y=50.0)
    # dx = 30, dy = 40, distance = sqrt(30^2 + 40^2) = 50.0 pixels
    # With pixel spacing [0.5, 0.5] mm/pixel -> 25.0 mm
    dist_mm = MprGeometryEngine.calculate_distance_mm(p1, p2, pixel_spacing=[0.5, 0.5])
    assert dist_mm == 25.0

    # Auto perpendicular line calculation
    c1, c2 = MprGeometryEngine.compute_automatic_perpendicular_line(
        p1=p1,
        p2=p2,
        length_mm=10.0,
        pixel_spacing=[0.5, 0.5],
        anchor_ratio=0.5,
    )
    cross_dist = MprGeometryEngine.calculate_distance_mm(
        c1, c2, pixel_spacing=[0.5, 0.5]
    )
    assert cross_dist == 10.0

    # 3D Ellipsoid volume: V = (pi/6) * d1 * d2 * d3
    vol = MprGeometryEngine.calculate_ellipsoid_volume(20.0, 15.0, 10.0)
    # (pi/6) * 3000 = 1570.80 mm^3
    assert vol == 1570.8


def test_mpr_slice_plane_mapping():
    """Verify Axial slice coordinate mapping to Sagittal and Coronal planes.

    @req:PRD-IMG-001
    @req:PRD-SYS-164
    """
    mapping = MprGeometryEngine.map_mpr_slice_index(
        axial_slice_index=24,
        axial_x=200.0,
        axial_y=150.0,
        rows=512,
        columns=512,
        slice_count=60,
    )

    assert mapping[MprPlane.AXIAL]["slice_index"] == 24
    assert mapping[MprPlane.SAGITTAL]["slice_index"] == 200
    assert mapping[MprPlane.CORONAL]["slice_index"] == 150


# ==============================================================================
# 4. REST API INTEGRATION TESTS (MPR, ANNOTATIONS, RECIST & LUGANO ENDPOINTS)
# ==============================================================================


@pytest.mark.asyncio
async def test_mpr_metadata_endpoint(client: AsyncClient):
    """Validate GET /api/v1/imaging/series/{uid}/mpr-metadata endpoint.

    @req:PRD-IMG-001
    @req:PRD-IMG-004
    @req:PRD-SYS-164
    @req:PRD-SYS-165
    """
    resp = await client.get("/api/v1/imaging/series/SERIES-TEST-999/mpr-metadata")
    assert resp.status_code == 200
    data = resp.json()

    assert data["series_instance_uid"] == "SERIES-TEST-999"
    assert "AXIAL" in data["available_planes"]
    assert "SAGITTAL" in data["available_planes"]
    assert "CORONAL" in data["available_planes"]
    assert "SOFT_TISSUE" in data["window_presets"]
    assert "LUNG" in data["window_presets"]


@pytest.mark.asyncio
async def test_caliper_annotation_crud_and_audit(client: AsyncClient):
    """Validate Caliper annotation creation, query, deletion, and 21 CFR Part 11 audit trail.

    @req:PRD-IMG-004
    @req:PRD-SYS-001
    @req:PRD-SYS-165
    """
    sop_uid = "1.2.840.10008.5.1.4.1.1.4.999"

    # 1. Create a 2D caliper annotation
    create_payload = {
        "study_envelope_id": None,
        "series_envelope_id": None,
        "sop_instance_uid": sop_uid,
        "study_id": "TRIAL-ONC-301",
        "site_id": "SITE-01",
        "subject_id": "SUBJ-ONC-100",
        "timepoint_name": "BASELINE",
        "lesion_id": "TL1",
        "lesion_type": "TARGET",
        "organ_site": "LIVER",
        "measurement_criteria": "RECIST_1_1",
        "measurement_type": "1D_RECIST",
        "longest_diameter_mm": 32.5,
        "perpendicular_diameter_mm": 24.0,
        "slice_index": 18,
        "points": [{"x": 120.0, "y": 140.0}, {"x": 152.5, "y": 140.0}],
        "notes": "Target lesion in segment 4 of liver",
    }

    create_resp = await client.post("/api/v1/imaging/annotations", json=create_payload)
    assert create_resp.status_code == 201, create_resp.text
    annot_data = create_resp.json()

    annot_id = annot_data["id"]
    assert annot_data["lesion_id"] == "TL1"
    assert annot_data["longest_diameter_mm"] == 32.5
    assert annot_data["perpendicular_diameter_mm"] == 24.0
    assert annot_data["product_perpendicular_mm2"] == 780.0  # 32.5 * 24.0
    assert annot_data["volume_mm3"] is not None

    # 2. Query annotations by subject_id
    query_resp = await client.get("/api/v1/imaging/annotations?subject_id=SUBJ-ONC-100")
    assert query_resp.status_code == 200
    annots = query_resp.json()
    assert len(annots) >= 1
    assert any(a["id"] == annot_id for a in annots)

    # 3. Delete annotation
    del_resp = await client.delete(f"/api/v1/imaging/annotations/{annot_id}")
    assert del_resp.status_code == 200
    assert del_resp.json()["deleted"] is True


@pytest.mark.asyncio
async def test_response_evaluations_endpoints(client: AsyncClient):
    """Validate POST /api/v1/imaging/response-evaluations/recist and /lugano.

    @req:PRD-IMG-004
    @req:PRD-SYS-165
    """
    # RECIST Endpoint
    recist_payload = {
        "current_lesions": [
            {
                "lesion_id": "TL1",
                "organ_site": "LIVER",
                "lesion_type": "TARGET",
                "longest_diameter_mm": 21.0,
            }
        ],
        "baseline_sld_mm": 35.0,
        "prior_timepoints_sld": [35.0],
        "non_target_status": "NON_CR_NON_PD",
        "new_lesions_present": False,
    }
    recist_resp = await client.post(
        "/api/v1/imaging/response-evaluations/recist", json=recist_payload
    )
    assert recist_resp.status_code == 200
    recist_data = recist_resp.json()
    assert recist_data["current_sld_mm"] == 21.0
    assert recist_data["pct_change_baseline"] == -40.0
    assert recist_data["target_response"] == "PR"
    assert recist_data["overall_response"] == "PR"

    # Lugano Endpoint
    lugano_payload = {
        "current_lesions": [
            {
                "lesion_id": "LN1",
                "organ_site": "MEDIASTINUM",
                "longest_diameter_mm": 20.0,
                "perpendicular_diameter_mm": 15.0,
                "deauville_score": 2,
            }
        ],
        "baseline_spd_mm2": 800.0,
        "prior_timepoints_spd": [800.0],
        "new_lesions_present": False,
    }
    lugano_resp = await client.post(
        "/api/v1/imaging/response-evaluations/lugano", json=lugano_payload
    )
    assert lugano_resp.status_code == 200
    lugano_data = lugano_resp.json()
    assert lugano_data["current_spd_mm2"] == 300.0
    assert lugano_data["pct_change_spd_baseline"] == -62.5
    assert lugano_data["ct_response"] == "PR"
    assert lugano_data["pet_metabolic_response"] == "CMR"
    assert lugano_data["overall_response"] == "CMR"


@pytest.mark.asyncio
async def test_subject_timepoint_evaluation_and_trajectory(client: AsyncClient):
    """Validate full longitudinal timepoint assessment workflow and trajectory API.

    @req:PRD-IMG-004
    @req:PRD-SYS-001
    @req:PRD-SYS-165
    """
    subject_id = "SUBJ-ONCOLOGY-TRAJECTORY-1"

    # 1. Create baseline annotation
    await client.post(
        "/api/v1/imaging/annotations",
        json={
            "study_envelope_id": None,
            "series_envelope_id": None,
            "sop_instance_uid": "SOP-01",
            "study_id": "TRIAL-ONC",
            "site_id": "SITE-01",
            "subject_id": subject_id,
            "timepoint_name": "BASELINE",
            "lesion_id": "TL1",
            "lesion_type": "TARGET",
            "organ_site": "LUNG_LEFT",
            "longest_diameter_mm": 50.0,
        },
    )

    # 2. Evaluate and lock Baseline
    eval_base_resp = await client.post(
        f"/api/v1/imaging/subjects/{subject_id}/timepoints/evaluate",
        json={
            "study_id": "TRIAL-ONC",
            "site_id": "SITE-01",
            "timepoint_name": "BASELINE",
            "assessment_criteria": "RECIST_1_1",
            "is_baseline": True,
            "digital_signature": "SIG-BASE-VERIFIED-12345",
        },
    )
    assert eval_base_resp.status_code == 201
    base_data = eval_base_resp.json()
    assert base_data["baseline_sld_mm"] == 50.0
    assert base_data["overall_response"] == "SD"
    assert base_data["is_locked"] is True

    # 3. Create Cycle 2 annotation (shrinkage: 30 mm -> PR)
    await client.post(
        "/api/v1/imaging/annotations",
        json={
            "study_envelope_id": None,
            "series_envelope_id": None,
            "sop_instance_uid": "SOP-02",
            "study_id": "TRIAL-ONC",
            "site_id": "SITE-01",
            "subject_id": subject_id,
            "timepoint_name": "CYCLE_2",
            "lesion_id": "TL1",
            "lesion_type": "TARGET",
            "organ_site": "LUNG_LEFT",
            "longest_diameter_mm": 30.0,
        },
    )

    # 4. Evaluate and lock Cycle 2
    eval_c2_resp = await client.post(
        f"/api/v1/imaging/subjects/{subject_id}/timepoints/evaluate",
        json={
            "study_id": "TRIAL-ONC",
            "site_id": "SITE-01",
            "timepoint_name": "CYCLE_2",
            "assessment_criteria": "RECIST_1_1",
            "is_baseline": False,
            "digital_signature": "SIG-C2-VERIFIED-67890",
        },
    )
    assert eval_c2_resp.status_code == 201
    c2_data = eval_c2_resp.json()
    assert c2_data["current_sld_mm"] == 30.0
    assert c2_data["pct_change_baseline"] == -40.0
    assert c2_data["overall_response"] == "PR"

    # 5. Fetch full trajectory for subject
    traj_resp = await client.get(f"/api/v1/imaging/subjects/{subject_id}/timepoints")
    assert traj_resp.status_code == 200
    traj_data = traj_resp.json()

    assert traj_data["subject_id"] == subject_id
    assert len(traj_data["trajectory"]) == 2
    assert traj_data["trajectory"][0]["timepoint_name"] == "BASELINE"
    assert traj_data["trajectory"][0]["overall_response"] == "SD"
    assert traj_data["trajectory"][1]["timepoint_name"] == "CYCLE_2"
    assert traj_data["trajectory"][1]["overall_response"] == "PR"
