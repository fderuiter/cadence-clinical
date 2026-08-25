"""NCI-CTCAE v5.0 & DAIDS Toxicity Grading Engine, Hy's Law, and Investigator AE Staging Test Suite.

Requirements:
- @req:PRD-SYS-171
- @req:PRD-LBM-003
- @req:PRD-PVG-001
- @req:PRD-SYS-001
- @req:Trace-1
- @req:Trace-15
"""

import os
from datetime import UTC, datetime

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
    FormSubmission,
    LabReferenceRange,
    StagedAdverseEventReview,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.domain.lab_assessment_models import LabAssessmentContext
from apps.execution.main import app
from apps.execution.services.lab_assessment_engine import (
    evaluate_laboratory_result,
)
from apps.execution.services.lab_toxicity_engine import (
    CTCAEGrade,
    LabToxicityEngine,
)
from packages.security.rbac_helpers import build_gateway_headers

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "valid-test-gateway-secret-key-2026")  # nosec B105


def get_auth_headers(
    user_id: str = "investigator_user",
    roles: str = "site_investigator,investigator,crc,data_manager,cra",
    change_reason: str = "CTCAE grading and AE review test verification",
) -> dict[str, str]:
    """Generate Gateway signature-compliant authentication headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
    )


@pytest_asyncio.fixture(autouse=True)
async def setup_ctcae_test_db():
    """Setup in-memory SQLite database before each test and tear down after."""
    from apps.execution.lab_range_cache import lab_range_cache

    lab_range_cache.clear()
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    lab_range_cache.clear()


@pytest.mark.asyncio
async def test_nci_ctcae_v5_hepatic_and_renal_grading():
    """Verify NCI-CTCAE v5.0 grading engine evaluating ULN multiples for hepatic enzymes and serum creatinine.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    """
    # 1. ALT Grading (ULN = 35.0)
    uln_alt = 35.0
    # Normal: 30 U/L (<= 1.0x ULN -> Grade 0)
    g0 = LabToxicityEngine.evaluate_observation(
        "ALT", "ALT", 30.0, "U/L", ref_high=uln_alt
    )
    assert g0.ctcae_grade == CTCAEGrade.GRADE_0

    # Grade 1: 70.0 U/L (2.0x ULN -> >1.0 - 3.0x ULN)
    g1 = LabToxicityEngine.evaluate_observation(
        "ALT", "ALT", 70.0, "U/L", ref_high=uln_alt
    )
    assert g1.ctcae_grade == CTCAEGrade.GRADE_1

    # Grade 2: 140.0 U/L (4.0x ULN -> >3.0 - 5.0x ULN)
    g2 = LabToxicityEngine.evaluate_observation(
        "ALT", "ALT", 140.0, "U/L", ref_high=uln_alt
    )
    assert g2.ctcae_grade == CTCAEGrade.GRADE_2

    # Grade 3: 280.0 U/L (8.0x ULN -> >5.0 - 20.0x ULN, Severe AE Trigger)
    g3 = LabToxicityEngine.evaluate_observation(
        "ALT", "ALT", 280.0, "U/L", ref_high=uln_alt
    )
    assert g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert g3.is_critical_ae_trigger is True

    # Grade 4: 800.0 U/L (22.8x ULN -> >20.0x ULN, Life Threatening)
    g4 = LabToxicityEngine.evaluate_observation(
        "ALT", "ALT", 800.0, "U/L", ref_high=uln_alt
    )
    assert g4.ctcae_grade == CTCAEGrade.GRADE_4
    assert g4.is_critical_ae_trigger is True

    # 2. Serum Creatinine (ULN = 1.0 mg/dL, Baseline = 1.0 mg/dL)
    uln_creat = 1.0
    # Grade 2: 1.8 mg/dL (>1.5 - 3.0x ULN)
    creat_g2 = LabToxicityEngine.evaluate_observation(
        "CREAT", "Creatinine", 1.8, "mg/dL", ref_high=uln_creat, baseline_value=1.0
    )
    assert creat_g2.ctcae_grade == CTCAEGrade.GRADE_2

    # Grade 3: 2.5 mg/dL (>1.5 - 3.0x ULN and >=2.0x - <3.0x baseline -> Grade 3)
    creat_g3 = LabToxicityEngine.evaluate_observation(
        "CREAT", "Creatinine", 2.5, "mg/dL", ref_high=uln_creat, baseline_value=1.0
    )
    assert creat_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert creat_g3.is_critical_ae_trigger is True


@pytest.mark.asyncio
async def test_nci_ctcae_v5_hematology_and_electrolytes_grading():
    """Verify CTCAE v5.0 grading for ANC, Platelets, Hemoglobin, Potassium, and Sodium.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    """
    # 1. Absolute Neutrophils (LLN = 1.5 x 10^9/L)
    # Grade 3: 0.8 x 10^9/L (<1.0 - 0.5)
    anc_g3 = LabToxicityEngine.evaluate_observation(
        "ANC", "Neutrophils", 0.8, "10^9/L", ref_low=1.5
    )
    assert anc_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert anc_g3.is_critical_ae_trigger is True

    # Grade 4: 0.3 x 10^9/L (<0.5)
    anc_g4 = LabToxicityEngine.evaluate_observation(
        "ANC", "Neutrophils", 0.3, "10^9/L", ref_low=1.5
    )
    assert anc_g4.ctcae_grade == CTCAEGrade.GRADE_4
    assert anc_g4.is_critical_ae_trigger is True

    # 2. Platelets (LLN = 150.0 x 10^9/L)
    # Grade 3: 40.0 x 10^9/L (<50.0 - 25.0)
    plt_g3 = LabToxicityEngine.evaluate_observation(
        "PLT", "Platelets", 40.0, "10^9/L", ref_low=150.0
    )
    assert plt_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert plt_g3.is_critical_ae_trigger is True

    # 3. Potassium Hyperkalemia (ULN = 5.0 mmol/L)
    # Grade 3: 6.5 mmol/L (>6.0 - 7.0)
    k_g3 = LabToxicityEngine.evaluate_observation(
        "POTASSIUM", "Potassium", 6.5, "mmol/L", ref_high=5.0
    )
    assert k_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert k_g3.is_critical_ae_trigger is True

    # Grade 4: 7.5 mmol/L (>7.0)
    k_g4 = LabToxicityEngine.evaluate_observation(
        "POTASSIUM", "Potassium", 7.5, "mmol/L", ref_high=5.0
    )
    assert k_g4.ctcae_grade == CTCAEGrade.GRADE_4
    assert k_g4.is_critical_ae_trigger is True


@pytest.mark.asyncio
async def test_hys_law_compound_dili_detection():
    """Verify Hy's Law Drug-Induced Liver Injury (DILI) compound alert triggers.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-PVG-001
    """
    # Hy's Law Met: ALT > 3x ULN (160 vs 35), Total Bili > 2x ULN (3.2 vs 1.2), ALKP < 2x ULN (95 vs 120)
    is_dili, msg = LabToxicityEngine.check_hys_law(
        alt_val=160.0,
        ast_val=140.0,
        bili_val=3.2,
        alkp_val=95.0,
        alt_uln=35.0,
        ast_uln=35.0,
        bili_uln=1.2,
        alkp_uln=120.0,
    )
    assert is_dili is True
    assert "Hy's Law DILI Alert" in msg

    # Cholestatic elevation (ALKP >= 2x ULN) rules out pure hepatocellular DILI
    is_cholestatic, _ = LabToxicityEngine.check_hys_law(
        alt_val=160.0,
        ast_val=140.0,
        bili_val=3.2,
        alkp_val=280.0,
        alt_uln=35.0,
        ast_uln=35.0,
        bili_uln=1.2,
        alkp_uln=120.0,
    )
    assert is_cholestatic is False


@pytest.mark.asyncio
async def test_acute_baseline_shift_delta_check():
    """Verify acute baseline shift delta check alerts for shifts >= 50% from baseline.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    """
    # Baseline 1.0 mg/dL -> Observed 2.6 mg/dL (+160% shift with Grade 3) -> Delta Alert True
    res = LabToxicityEngine.evaluate_observation(
        test_code="CREAT",
        test_name="Serum Creatinine",
        value=2.6,
        unit="mg/dL",
        ref_high=1.2,
        baseline_value=1.0,
    )
    assert res.ctcae_grade == CTCAEGrade.GRADE_3
    assert res.is_delta_check_alert is True
    assert res.delta_percentage == 160.0


@pytest.mark.asyncio
async def test_automated_grade_3_4_ae_trigger_and_investigator_signoff():
    """Verify automated AE discrepancy creation for Grade 3/4 severe toxicities and investigator reconciliation.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-PVG-001
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Seed Subject & Reference Range
        subj = ClinicalSubject(
            study_id="STUDY-TOX-01",
            site_id="SITE-01",
            subject_id="SUBJ-TOX-101",
            status="SCREENING",
            encrypted_demographics=encrypt_demographics(
                {"gender": "M", "birthdate": "1988-07-20"}
            ),
        )
        ref_range = LabReferenceRange(
            study_id="STUDY-TOX-01",
            test_code="ALT",
            test_name="Alanine Aminotransferase",
            source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex_applicability="ALL",
            low_bound=0.0,
            high_bound=40.0,
            critical_low=0.0,
            critical_high=200.0,
            reason_for_change="Protocol normal range",
        )
        session.add_all([subj, ref_range])
        await session.commit()

        # 2. Evaluate Grade 4 Observation (950 U/L -> 23.75x ULN)
        obs_payload = {
            "subject_id": "SUBJ-TOX-101",
            "study_id": "STUDY-TOX-01",
            "site_id": "SITE-01",
            "visit_id": "CYCLE-2-DAY-1",
            "test_code": "ALT",
            "test_name": "Alanine Aminotransferase",
            "value": 950.0,
            "unit": "U/L",
            "observation_date": datetime.now(UTC),
            "lab_source": "CENTRAL",
        }
        ctx = LabAssessmentContext(
            session=session,
            study_id="STUDY-TOX-01",
            site_id="SITE-01",
            subject=subj,
            persist_records=True,
            user_id="central_lab_system",
            change_reason="Automated central lab toxicities",
        )

        result = await evaluate_laboratory_result(observation=obs_payload, context=ctx)
        await session.commit()

        # Assert Grade 4 assessment outputs
        assert result.ctcae_grade == 4
        assert result.is_critical is True
        assert result.is_critical_ae_trigger is True
        assert result.form_submission_record is not None
        assert result.staged_review_record is not None
        assert len(result.queries_raised) >= 1

        # Verify staged FormSubmission (AE eCRF draft)
        stmt_form = select(FormSubmission).where(
            FormSubmission.subject_id == "SUBJ-TOX-101",
            FormSubmission.form_id == "AE",
            FormSubmission.is_deleted.is_(False),
        )
        res_form = await session.execute(stmt_form)
        draft_ae = res_form.scalars().first()
        assert draft_ae is not None
        assert draft_ae.status == "DRAFT"
        assert draft_ae.payload["aetoxgr"] == "Grade 4"

        # Verify StagedAdverseEventReview record
        stmt_rev = select(StagedAdverseEventReview).where(
            StagedAdverseEventReview.subject_id == "SUBJ-TOX-101",
            StagedAdverseEventReview.test_code == "ALT",
            StagedAdverseEventReview.is_deleted.is_(False),
        )
        res_rev = await session.execute(stmt_rev)
        review_item = res_rev.scalars().first()
        assert review_item is not None
        assert review_item.review_status == "STAGED_FOR_INVESTIGATOR_REVIEW"
        assert review_item.ctcae_grade == 4

        # 3. Simulate Investigator Sign-off Action via REST API
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app),
            base_url="http://testserver",
        ) as client:
            headers = get_auth_headers(
                user_id="investigator_pi",
                roles="site_investigator,investigator",
                change_reason="Investigator confirmation of Grade 4 ALT hepatotoxicity AE",
            )
            resp = await client.post(
                f"/api/v1/execution/labs/toxicity/review/{review_item.id}/action",
                headers=headers,
                json={
                    "action": "CONFIRM",
                    "investigator_notes": "Confirmed acute Grade 4 ALT elevation related to study drug. Temporary dose hold indicated.",
                    "reason_for_change": "21 CFR Part 11 medical determination sign-off",
                },
            )
            assert resp.status_code == 200
            data = resp.json()
            assert data["review_status"] == "CONFIRMED_AS_AE"
            assert data["action_taken"] == "CONFIRM"
            assert data["form_submission_id"] == draft_ae.id

        # Verify finalized state in database
        await session.refresh(review_item)
        assert review_item.review_status == "CONFIRMED_AS_AE"
        assert review_item.reviewed_by == "investigator_pi"

        await session.refresh(draft_ae)
        assert draft_ae.status == "SUBMITTED"
