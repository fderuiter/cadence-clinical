"""Integration and Unit Tests for Blinded Dual-Reader Charter & Tie-Breaker Adjudication.

Requirements:
- ADR-2199
- PRD-IMG-003: Independent Review Charter (IRC) & Blinded Dual-Reader Workflow
- PRD-IMG-005: Adjudication Tie-Breaker Committee Escalation & Electronic Sign-off
- PRD-IMG-006: Direct EDC eCRF Measurement Binding & SDTM TU/TR/RS Auto-Population
- PRD-SYS-001: 21 CFR Part 11 Audit Trail & Step-up Electronic Signatures
"""

import os
import time

import pytest
from httpx import AsyncClient
from jose import jwt

from apps.imaging.domain.models import (
    AdjudicationWorkflowStatus,
    DiscordanceReason,
    LesionMeasurement,
    OncologicCriteria,
    ReaderAssessment,
    ReaderRole,
    RECISTResponseCode,
)
from apps.imaging.services.adjudication import (
    DualReaderAdjudicationSession,
    ImagingAdjudicationEngine,
)
from packages.testing.security import create_test_auth_headers


def _generate_sig_token(
    user_id: str = "chair_dr_adjudicator",
    action: str = "sign-off",
    secret: bytes | None = None,
) -> str:
    """Generate a 21 CFR Part 11 compliant step-up signature token."""
    secret_bytes = (
        secret
        or os.getenv(
            "GATEWAY_SECRET", "test-gateway-secret-12345"
        ).encode()  # pragma: allowlist secret
    )
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "roles": ["Adjudication Chair"],
        "iat": time.time(),
        "exp": time.time() + 300.0,
    }
    return jwt.encode(payload, secret_bytes.decode(), algorithm="HS256")


def _make_headers(
    user_id: str = "imaging.crc.user",
    role: str = "Site Investigator / CRC",
    reader_role: str | None = None,
    sig_token: str | None = None,
) -> dict[str, str]:
    """Builds valid HMAC-signed gateway headers for testing."""
    secret_bytes = os.getenv(
        "GATEWAY_SECRET", "test-gateway-secret-12345"
    ).encode()  # pragma: allowlist secret
    h = create_test_auth_headers(
        user_id=user_id,
        roles=[role],
        tenant_id="tenant_clinical_test",
        change_reason="Dual-reader adjudication test",
        secret=secret_bytes,
        sig_token=sig_token,
    )
    if reader_role:
        h["X-Reader-Role"] = reader_role
    return h


# =========================================================================
# Unit Tests: Domain Engine Blinding, Discordance, and SDTM/EDC Derivations
# =========================================================================


def test_blinded_dual_reader_isolation_unit():
    """Verify that Reader 1 and Reader 2 views are completely blinded from each other.

    @req:PRD-IMG-003
    @req:PRD-SYS-166
    """
    session = DualReaderAdjudicationSession(
        session_id="ADJ-UNIT-001",
        study_envelope_id="ENV-901",
        study_id="STUDY-BLIND-01",
        site_id="SITE-PARIS-01",
        subject_id="SUBJ-BLIND-101",
        timepoint_name="CYCLE_2_DAY_1",
        reader_1_id="rad_paris_01",
        reader_1_name="Dr. Reader One",
        reader_2_id="rad_london_02",
        reader_2_name="Dr. Reader Two",
        adjudicator_id="chair_rad_99",
        adjudicator_name="Dr. Adjudication Chair",
    )

    r1_assessment = ReaderAssessment(
        reader_id="rad_paris_01",
        reader_name="Dr. Reader One",
        reader_role=ReaderRole.READER_1,
        timepoint_name="CYCLE_2_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LIVER",
                longest_diameter_mm=25.0,
                sop_instance_uid="sop_111",
            )
        ],
        sum_longest_diameters_mm=25.0,
        overall_response=RECISTResponseCode.PR,
    )
    session.reader_1_assessment = r1_assessment
    session.reader_1_status = "COMPLETED"

    r2_assessment = ReaderAssessment(
        reader_id="rad_london_02",
        reader_name="Dr. Reader Two",
        reader_role=ReaderRole.READER_2,
        timepoint_name="CYCLE_2_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LIVER",
                longest_diameter_mm=40.0,
                sop_instance_uid="sop_111",
            )
        ],
        sum_longest_diameters_mm=40.0,
        overall_response=RECISTResponseCode.PD,
    )
    session.reader_2_assessment = r2_assessment
    session.reader_2_status = "COMPLETED"

    # Evaluate discordance
    ImagingAdjudicationEngine.evaluate_primary_reads(
        session, r1_assessment, r2_assessment
    )
    assert session.workflow_status == AdjudicationWorkflowStatus.DISCORDANCE_DETECTED

    # 1. Reader 1 View - Must NOT leak Reader 2 measurements or discordance
    r1_view = ImagingAdjudicationEngine.get_blinded_session_view(
        session,
        requesting_user_id="rad_paris_01",
        requesting_role=ReaderRole.READER_1,
    )
    assert r1_view["site_id"] == "BLINDED_SITE"
    assert r1_view["reader_1_assessment"] is not None
    assert r1_view["reader_2_assessment"] is None
    assert r1_view["reader_2_status"] == "BLINDED"
    assert r1_view["adjudicator_assessment"] is None
    assert r1_view["discordance_reasons"] == []
    assert r1_view["is_response_discordant"] is False
    assert r1_view["final_consensus_response"] is None

    # 2. Reader 2 View - Must NOT leak Reader 1 measurements or discordance
    r2_view = ImagingAdjudicationEngine.get_blinded_session_view(
        session,
        requesting_user_id="rad_london_02",
        requesting_role=ReaderRole.READER_2,
    )
    assert r2_view["site_id"] == "BLINDED_SITE"
    assert r2_view["reader_1_assessment"] is None
    assert r2_view["reader_1_status"] == "BLINDED"
    assert r2_view["reader_2_assessment"] is not None
    assert r2_view["adjudicator_assessment"] is None
    assert r2_view["discordance_reasons"] == []

    # 3. Chair / Adjudicator View - Must see full side-by-side comparison
    chair_view = ImagingAdjudicationEngine.get_blinded_session_view(
        session,
        requesting_user_id="chair_rad_99",
        requesting_role=ReaderRole.ADJUDICATOR,
    )
    assert chair_view["site_id"] == "SITE-PARIS-01"
    assert chair_view["reader_1_assessment"] is not None
    assert chair_view["reader_2_assessment"] is not None
    assert (
        chair_view["workflow_status"]
        == AdjudicationWorkflowStatus.DISCORDANCE_DETECTED.value
    )
    assert chair_view["is_response_discordant"] is True
    assert len(chair_view["discordance_reasons"]) > 0


def test_discordance_detection_criteria_unit():
    """Verify granular discordance rules: overall response, SLD threshold, new lesions.

    @req:PRD-IMG-005
    @req:PRD-SYS-166
    """
    session = DualReaderAdjudicationSession(
        session_id="ADJ-UNIT-002",
        study_id="STUDY-002",
        subject_id="SUBJ-002",
        timepoint_name="CYCLE_4_DAY_1",
    )

    # 1. New lesion discordance
    r1 = ReaderAssessment(
        reader_id="r1",
        reader_role=ReaderRole.READER_1,
        timepoint_name="CYCLE_4_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LUNG",
                longest_diameter_mm=20.0,
                sop_instance_uid="sop_1",
            )
        ],
        sum_longest_diameters_mm=20.0,
        new_lesions_present=False,
        overall_response=RECISTResponseCode.PR,
    )

    r2 = ReaderAssessment(
        reader_id="r2",
        reader_role=ReaderRole.READER_2,
        timepoint_name="CYCLE_4_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LUNG",
                longest_diameter_mm=20.0,
                sop_instance_uid="sop_1",
            )
        ],
        sum_longest_diameters_mm=20.0,
        new_lesions_present=True,
        overall_response=RECISTResponseCode.PD,
    )

    evaluated = ImagingAdjudicationEngine.evaluate_primary_reads(session, r1, r2)
    assert evaluated.workflow_status == AdjudicationWorkflowStatus.DISCORDANCE_DETECTED
    assert evaluated.is_response_discordant is True
    assert any(
        DiscordanceReason.OVERALL_RESPONSE_MISMATCH.value in r
        for r in evaluated.discordance_reasons
    )
    assert any(
        DiscordanceReason.NEW_LESION_DISCORDANCE.value in r
        for r in evaluated.discordance_reasons
    )


def test_edc_ecrf_binding_and_sdtm_derivation_unit():
    """Verify EDC eCRF tumor forms and CDISC SDTM TU/TR/RS domain exports.

    @req:PRD-IMG-006
    @req:PRD-SYS-165
    @req:PRD-SYS-166
    """
    session = DualReaderAdjudicationSession(
        session_id="ADJ-UNIT-003",
        study_id="PROTOCOL-ONC-301",
        site_id="SITE-101",
        subject_id="SUBJ-888",
        timepoint_name="CYCLE_2_DAY_1",
        assessment_criteria=OncologicCriteria.RECIST_1_1,
    )

    r1 = ReaderAssessment(
        reader_id="rad_01",
        reader_role=ReaderRole.READER_1,
        timepoint_name="CYCLE_2_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LIVER",
                longest_diameter_mm=20.0,
                short_axis_mm=15.0,
                slice_location=-45.0,
                sop_instance_uid="sop_liver_01",
            ),
            LesionMeasurement(
                lesion_id="TL2",
                organ_site="RIGHT_LUNG",
                longest_diameter_mm=10.0,
                short_axis_mm=8.0,
                slice_location=12.5,
                sop_instance_uid="sop_lung_02",
            ),
        ],
        sum_longest_diameters_mm=30.0,
        overall_response=RECISTResponseCode.PR,
    )

    r2 = ReaderAssessment(
        reader_id="rad_02",
        reader_role=ReaderRole.READER_2,
        timepoint_name="CYCLE_2_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LIVER",
                longest_diameter_mm=22.0,
                sop_instance_uid="sop_liver_01",
            ),
            LesionMeasurement(
                lesion_id="TL2",
                organ_site="RIGHT_LUNG",
                longest_diameter_mm=10.0,
                sop_instance_uid="sop_lung_02",
            ),
        ],
        sum_longest_diameters_mm=32.0,
        overall_response=RECISTResponseCode.PR,
    )

    # Concordant evaluation
    concordant_sess = ImagingAdjudicationEngine.evaluate_primary_reads(session, r1, r2)
    assert (
        concordant_sess.workflow_status == AdjudicationWorkflowStatus.CONSENSUS_REACHED
    )
    assert concordant_sess.final_consensus_sld_mm == 31.0

    # 1. EDC eCRF Binding
    edc_binding = ImagingAdjudicationEngine.generate_edc_ecrf_binding(concordant_sess)
    assert edc_binding["form_id"] == "eCRF_TUMOR_ASSESSMENT"
    assert edc_binding["subject_id"] == "SUBJ-888"
    assert edc_binding["overall_response"] == "PR"
    assert edc_binding["sum_longest_diameters_mm"] == 31.0
    assert len(edc_binding["target_lesions"]) == 2
    assert edc_binding["manifest_sha256"] != ""

    # 2. CDISC SDTM TU / TR / RS Datasets
    sdtm_pkg = ImagingAdjudicationEngine.generate_sdtm_tu_tr_rs(concordant_sess)

    # SDTM TU
    assert len(sdtm_pkg["TU"]) == 2
    tu_0 = sdtm_pkg["TU"][0]
    assert tu_0["DOMAIN"] == "TU"
    assert tu_0["USUBJID"] == "PROTOCOL-ONC-301-SUBJ-888"
    assert tu_0["TUTESTCD"] == "TUMIDENT"
    assert tu_0["TULNKID"] == "TL1"
    assert tu_0["TULOC"] == "LIVER"
    assert tu_0["TUEVAL"] == "INDEPENDENT ASSESSOR"

    # SDTM TR
    assert len(sdtm_pkg["TR"]) == 3  # 2 lesions + 1 SUMDIR summary
    tr_sum = next(r for r in sdtm_pkg["TR"] if r["TRTESTCD"] == "SUMDIR")
    assert tr_sum["TRTEST"] == "Sum of Longest Diameters"
    assert tr_sum["TRSTRESN"] == 31.0
    assert tr_sum["TRORRESU"] == "mm"

    # SDTM RS
    assert len(sdtm_pkg["RS"]) == 1
    rs_0 = sdtm_pkg["RS"][0]
    assert rs_0["DOMAIN"] == "RS"
    assert rs_0["RSTESTCD"] == "OVRLRESP"
    assert rs_0["RSORRES"] == "PR"
    assert rs_0["RSMETHOD"] == "RECIST 1.1"
    assert rs_0["RSEVALSTAT"] == "FINAL"


# =========================================================================
# Integration Tests: End-to-End Dual-Reader Workflow via REST API
# =========================================================================


@pytest.mark.asyncio
async def test_dual_reader_routing_and_concordance_api(client: AsyncClient):
    """End-to-end test of dual-reader session creation, independent submissions, and automatic consensus.

    @req:PRD-IMG-003
    @req:PRD-IMG-005
    @req:PRD-SYS-166
    """
    # 1. Create Dual-Reader Adjudication Session
    create_payload = {
        "study_id": "TRIAL-SOLID-101",
        "site_id": "SITE-NYC-01",
        "subject_id": "SUBJ-ONC-777",
        "timepoint_name": "CYCLE_2_DAY_1",
        "assessment_criteria": "RECIST_1_1",
        "reader_1_id": "radiologist_01",
        "reader_1_name": "Dr. Sarah Miller",
        "reader_2_id": "radiologist_02",
        "reader_2_name": "Dr. James Wilson",
        "adjudicator_id": "chair_dr_smith",
        "adjudicator_name": "Dr. Arthur Smith (Chair)",
    }

    create_res = await client.post(
        "/api/v1/imaging/adjudication/sessions",
        json=create_payload,
        headers=_make_headers(user_id="study_coordinator_99"),
    )
    assert create_res.status_code == 201, create_res.text
    session_data = create_res.json()
    session_id = session_data["session_id"]
    assert session_data["workflow_status"] == "PENDING_PRIMARY_READS"

    # 2. Reader 1 Submits Blinded Assessment
    r1_payload = {
        "reader_id": "radiologist_01",
        "reader_name": "Dr. Sarah Miller",
        "reader_role": "READER_1",
        "target_lesions": [
            {
                "lesion_id": "TL1",
                "organ_site": "LIVER",
                "longest_diameter_mm": 24.0,
                "sop_instance_uid": "sop_001",
            }
        ],
        "sum_longest_diameters_mm": 24.0,
        "overall_response": "PR",
        "assessment_notes": "Significant shrinkage in segment IV target lesion.",
    }

    r1_res = await client.post(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/assessments",
        json=r1_payload,
        headers=_make_headers(user_id="radiologist_01", reader_role="READER_1"),
    )
    assert r1_res.status_code == 200, r1_res.text
    r1_data = r1_res.json()
    assert r1_data["reader_1_status"] == "COMPLETED"
    assert r1_data["reader_2_status"] == "BLINDED"
    assert r1_data["reader_2_assessment"] is None

    # 3. Reader 2 Queries Session (Before Submitting) - Must be blinded to Reader 1
    r2_query = await client.get(
        f"/api/v1/imaging/adjudication/sessions/{session_id}",
        headers=_make_headers(user_id="radiologist_02", reader_role="READER_2"),
    )
    assert r2_query.status_code == 200
    r2_view = r2_query.json()
    assert r2_view["reader_1_status"] == "BLINDED"
    assert r2_view["reader_1_assessment"] is None
    assert r2_view["reader_2_status"] == "PENDING"

    # 4. Reader 2 Submits Concordant Assessment (PR, 26mm SLD -> |24-26|/26 = 7.7% < 20%)
    r2_payload = {
        "reader_id": "radiologist_02",
        "reader_name": "Dr. James Wilson",
        "reader_role": "READER_2",
        "target_lesions": [
            {
                "lesion_id": "TL1",
                "organ_site": "LIVER",
                "longest_diameter_mm": 26.0,
                "sop_instance_uid": "sop_001",
            }
        ],
        "sum_longest_diameters_mm": 26.0,
        "overall_response": "PR",
        "assessment_notes": "Good response to chemo-immunotherapy.",
    }

    r2_res = await client.post(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/assessments",
        json=r2_payload,
        headers=_make_headers(user_id="radiologist_02", reader_role="READER_2"),
    )
    assert r2_res.status_code == 200, r2_res.text

    # 5. Adjudication Chair Queries Session - Auto-Consensus Reached
    chair_query = await client.get(
        f"/api/v1/imaging/adjudication/sessions/{session_id}",
        headers=_make_headers(user_id="chair_dr_smith", reader_role="ADJUDICATOR"),
    )
    assert chair_query.status_code == 200
    chair_data = chair_query.json()
    assert chair_data["workflow_status"] == "CONSENSUS_REACHED"
    assert chair_data["final_consensus_response"] == "PR"
    assert chair_data["final_consensus_sld_mm"] == 25.0
    assert chair_data["binding_reader"] == "CONSENSUS"
    assert chair_data["locked_by"] == "system_concordance_evaluator"
    assert chair_data["session_digest"] != ""


@pytest.mark.asyncio
async def test_discordant_dual_reads_and_step_up_adjudication_sign_off_api(
    client: AsyncClient,
):
    """End-to-end test of discordant primary reads, automatic chair escalation, and 21 CFR Part 11 sign-off.

    @req:PRD-IMG-003
    @req:PRD-IMG-005
    @req:PRD-IMG-006
    @req:PRD-SYS-001
    @req:PRD-SYS-166
    """
    # 1. Create Session
    create_payload = {
        "study_id": "TRIAL-DISCORD-202",
        "site_id": "SITE-BERLIN-02",
        "subject_id": "SUBJ-ONC-999",
        "timepoint_name": "CYCLE_4_DAY_1",
        "assessment_criteria": "RECIST_1_1",
        "reader_1_id": "radiologist_berlin_01",
        "reader_1_name": "Dr. Klaus Richter",
        "reader_2_id": "radiologist_munich_02",
        "reader_2_name": "Dr. Anna Weber",
        "adjudicator_id": "chair_dr_adjudicator",
        "adjudicator_name": "Dr. Adjudication Chair",
    }

    create_res = await client.post(
        "/api/v1/imaging/adjudication/sessions",
        json=create_payload,
        headers=_make_headers(user_id="monitor_01"),
    )
    assert create_res.status_code == 201
    session_id = create_res.json()["session_id"]

    # 2. Reader 1 Submits Partial Response (PR, 20mm SLD)
    await client.post(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/assessments",
        json={
            "reader_id": "radiologist_berlin_01",
            "reader_name": "Dr. Klaus Richter",
            "reader_role": "READER_1",
            "target_lesions": [
                {
                    "lesion_id": "TL1",
                    "organ_site": "LIVER",
                    "longest_diameter_mm": 20.0,
                    "sop_instance_uid": "sop_berlin_1",
                }
            ],
            "sum_longest_diameters_mm": 20.0,
            "overall_response": "PR",
        },
        headers=_make_headers(user_id="radiologist_berlin_01", reader_role="READER_1"),
    )

    # 3. Reader 2 Submits Progressive Disease (PD, 42mm SLD + New Lesions)
    await client.post(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/assessments",
        json={
            "reader_id": "radiologist_munich_02",
            "reader_name": "Dr. Anna Weber",
            "reader_role": "READER_2",
            "target_lesions": [
                {
                    "lesion_id": "TL1",
                    "organ_site": "LIVER",
                    "longest_diameter_mm": 42.0,
                    "sop_instance_uid": "sop_berlin_1",
                }
            ],
            "sum_longest_diameters_mm": 42.0,
            "new_lesions_present": True,
            "new_lesions_count": 1,
            "overall_response": "PD",
        },
        headers=_make_headers(user_id="radiologist_munich_02", reader_role="READER_2"),
    )

    # 4. Chair reviews escalated case
    chair_res = await client.get(
        f"/api/v1/imaging/adjudication/sessions/{session_id}",
        headers=_make_headers(
            user_id="chair_dr_adjudicator", reader_role="ADJUDICATOR"
        ),
    )
    assert chair_res.status_code == 200
    chair_data = chair_res.json()
    assert chair_data["workflow_status"] == "DISCORDANCE_DETECTED"
    assert chair_data["is_response_discordant"] is True
    assert chair_data["sld_discordance_delta"] > 0.20
    assert len(chair_data["discordance_reasons"]) >= 2
    assert chair_data["final_consensus_response"] is None

    # 5a. Chair attempts sign-off without sig_token -> 401 Re-authentication Required
    unauth_signoff = await client.post(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/sign-off",
        json={
            "signer_id": "chair_dr_adjudicator",
            "signer_name": "Dr. Arthur Chair",
            "rationale": "Sample rationale",
            "binding_reader": "READER_1",
        },
        headers=_make_headers(user_id="chair_dr_adjudicator"),
    )
    assert unauth_signoff.status_code == 401

    # 5b. Chair attempts sign-off with sig_token but empty rationale -> 400 Bad Request
    sig_token = _generate_sig_token(user_id="chair_dr_adjudicator")
    invalid_signoff = await client.post(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/sign-off",
        json={
            "signer_id": "chair_dr_adjudicator",
            "signer_name": "Dr. Arthur Chair",
            "rationale": "",  # Empty rationale
            "binding_reader": "READER_1",
        },
        headers=_make_headers(user_id="chair_dr_adjudicator", sig_token=sig_token),
    )
    assert invalid_signoff.status_code == 400

    # 6. Chair executes 21 CFR Part 11 Step-up Electronic Sign-off with fresh token
    fresh_sig_token = _generate_sig_token(user_id="chair_dr_adjudicator")
    signoff_payload = {
        "signer_id": "chair_dr_adjudicator",
        "signer_name": "Dr. Arthur Chair",
        "password": "PasswordPart11Secret!",  # pragma: allowlist secret
        "signing_reason": "ADJUDICATION_SIGN_OFF",
        "binding_reader": "ADJUDICATOR",
        "rationale": "Reviewed slice 48 axial series 2. Confirmed Reader 2 misidentified portal vein branching as new liver metastasis. Target lesion measured 22.0mm. Adjudicated final response: Partial Response (PR).",
        "adjudicator_assessment": {
            "reader_id": "chair_dr_adjudicator",
            "reader_name": "Dr. Arthur Chair",
            "reader_role": "ADJUDICATOR",
            "target_lesions": [
                {
                    "lesion_id": "TL1",
                    "organ_site": "LIVER",
                    "longest_diameter_mm": 22.0,
                    "sop_instance_uid": "sop_berlin_1",
                }
            ],
            "sum_longest_diameters_mm": 22.0,
            "new_lesions_present": False,
            "overall_response": "PR",
        },
    }

    signoff_res = await client.post(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/sign-off",
        json=signoff_payload,
        headers=_make_headers(
            user_id="chair_dr_adjudicator", sig_token=fresh_sig_token
        ),
    )
    assert signoff_res.status_code == 200, signoff_res.text
    final_data = signoff_res.json()
    assert final_data["workflow_status"] == "ADJUDICATED"
    assert final_data["final_consensus_response"] == "PR"
    assert final_data["final_consensus_sld_mm"] == 22.0
    assert final_data["binding_reader"] == "ADJUDICATOR"
    assert final_data["electronic_signature"]["step_up_verified"] is True
    assert (
        final_data["electronic_signature"]["signing_reason"] == "ADJUDICATION_SIGN_OFF"
    )
    assert final_data["locked_by"] == "chair_dr_adjudicator"

    # 7. Extract Direct EDC eCRF Form Binding
    edc_res = await client.get(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/edc-binding",
        headers=_make_headers(user_id="chair_dr_adjudicator"),
    )
    assert edc_res.status_code == 200
    edc_payload = edc_res.json()
    assert edc_payload["form_id"] == "eCRF_TUMOR_ASSESSMENT"
    assert edc_payload["overall_response"] == "PR"
    assert edc_payload["sum_longest_diameters_mm"] == 22.0
    assert edc_payload["adjudication_status"] == "ADJUDICATED"
    assert edc_payload["signed_by"] == "chair_dr_adjudicator"

    # 8. Export CDISC SDTM TU/TR/RS Datasets
    sdtm_res = await client.get(
        f"/api/v1/imaging/adjudication/sessions/{session_id}/export-sdtm",
        headers=_make_headers(user_id="chair_dr_adjudicator"),
    )
    assert sdtm_res.status_code == 200
    sdtm_payload = sdtm_res.json()
    assert sdtm_payload["study_id"] == "TRIAL-DISCORD-202"
    assert len(sdtm_payload["tu_records"]) >= 1
    assert len(sdtm_payload["tr_records"]) >= 2
    assert len(sdtm_payload["rs_records"]) == 1
    assert sdtm_payload["rs_records"][0]["RSORRES"] == "PR"
    assert sdtm_payload["total_records"] >= 4
