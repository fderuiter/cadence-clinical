"""Comprehensive unit and integration test suite for the Deep Laboratory Clinical Assessment Engine.

Tests:
1. Multi-dimensional reference range selection (Age, Sex, Site, Ethnicity).
2. LOINC mathematical unit conversions (mg/dL <-> umol/L, U/L <-> ukat/L, conventional to SI).
3. Toxicity grading across CTCAE v5.0 and DAIDS v2.1 scales.
4. Hy's Law compound evaluation for Drug-Induced Liver Injury (DILI).
5. Acute baseline delta checks (>= 50% shift).
6. Automated Adverse Event eCRF drafting, staged review generation, and discrepancy queries.
7. Local laboratory certification verification and uncalibrated range safeguards.
8. End-to-end evaluation via evaluate_laboratory_result.
9. Cohort-wide reference range recalculation.

Requirements:
- @req:PRD-LAB-001
- @req:PRD-SYS-123
- @req:ADR-2199
- @req:PRD-PVG-001
- @req:PRD-SYS-001
- @req:Trace-1
- @req:Trace-15
"""

from datetime import UTC, datetime, timedelta

import pytest
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    FormSubmission,
    LabReferenceRange,
    LabUnitConversion,
    LocalLabCertification,
    StagedAdverseEventReview,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.domain.lab_assessment_models import (
    LabAssessmentContext,
    LabAssessmentResult,
)
from apps.execution.services.lab_assessment_engine import (
    LabAssessmentEngine,
    convert_lab_unit,
    evaluate_lab_value,
    evaluate_laboratory_result,
    get_loinc_catalog,
    resolve_loinc_mapping,
    select_reference_range,
)
from apps.execution.services.lab_toxicity_engine import CTCAEGrade


@pytest.fixture(autouse=True)
async def setup_lab_assessment_db():
    """Initializes in-memory SQLite schema for lab assessment tests."""
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
async def test_loinc_catalog_and_unit_conversion():
    """Verify LOINC metadata catalog resolution and mathematical unit conversions.

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    @req:PRD-SYS-123
    @req:ADR-2199
    """
    # 1. Catalog retrieval
    catalog = get_loinc_catalog()
    assert len(catalog) > 10
    alt_entry = resolve_loinc_mapping("ALT")
    assert alt_entry is not None
    assert alt_entry["loinc_code"] == "1742-6"
    assert alt_entry["standard_unit"] == "U/L"

    # 2. LOINC standard mathematical conversion: Bilirubin mg/dL to umol/L (* 17.1)
    bili_conv = await convert_lab_unit(
        session=None,
        test_code="BILI",
        from_unit="mg/dL",
        to_unit="umol/L",
        value=2.0,
    )
    assert pytest.approx(bili_conv, rel=1e-3) == 34.2

    # 3. Serum Creatinine mg/dL to umol/L (* 88.4)
    creat_conv = await convert_lab_unit(
        session=None,
        test_code="CREAT",
        from_unit="mg/dL",
        to_unit="umol/L",
        value=1.5,
    )
    assert pytest.approx(creat_conv, rel=1e-3) == 132.6

    # 4. Custom DB conversion factor lookup
    async with db_manager.get_session_maker()() as session:
        custom_conv = LabUnitConversion(
            study_id="STUDY-CUSTOM",
            test_code="BIOMARKER_X",
            from_unit="ng/mL",
            to_unit="pmol/L",
            factor=3.1415,
            offset=0.5,
            reason_for_change="Initial setup",
        )
        session.add(custom_conv)
        await session.commit()

        val_custom = await convert_lab_unit(
            session=session,
            test_code="BIOMARKER_X",
            from_unit="ng/mL",
            to_unit="pmol/L",
            value=10.0,
        )
        assert pytest.approx(val_custom, rel=1e-3) == 31.915


@pytest.mark.asyncio
async def test_multidimensional_reference_range_selection():
    """Verify age, sex, site, and ethnicity specificity scoring and tie-breaking.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    @req:PRD-SYS-123
    @req:ADR-2199
    """
    ranges = [
        # Generic central fallback
        {
            "id": "R-GENERIC",
            "study_id": "STUDY-101",
            "test_code": "ALT",
            "normalized_unit": "U/L",
            "source": "CENTRAL",
            "sex_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 0.0,
            "high_bound": 40.0,
        },
        # Male central
        {
            "id": "R-MALE-CENTRAL",
            "study_id": "STUDY-101",
            "test_code": "ALT",
            "normalized_unit": "U/L",
            "source": "CENTRAL",
            "sex_applicability": "M",
            "age_low": None,
            "age_high": None,
            "low_bound": 0.0,
            "high_bound": 45.0,
        },
        # Female central
        {
            "id": "R-FEMALE-CENTRAL",
            "study_id": "STUDY-101",
            "test_code": "ALT",
            "normalized_unit": "U/L",
            "source": "CENTRAL",
            "sex_applicability": "F",
            "age_low": None,
            "age_high": None,
            "low_bound": 0.0,
            "high_bound": 35.0,
        },
        # Site-specific local range for SITE-01
        {
            "id": "R-SITE01-LOCAL",
            "study_id": "STUDY-101",
            "test_code": "ALT",
            "normalized_unit": "U/L",
            "source": "LOCAL",
            "site_id": "SITE-01",
            "sex_applicability": "M",
            "age_low": 18.0,
            "age_high": 65.0,
            "low_bound": 10.0,
            "high_bound": 50.0,
        },
        # Ethnicity-specific range (HISPANIC)
        {
            "id": "R-HISPANIC-FEMALE",
            "study_id": "STUDY-101",
            "test_code": "ALT",
            "normalized_unit": "U/L",
            "source": "CENTRAL",
            "sex_applicability": "F",
            "ethnicity_applicability": "HISPANIC",
            "age_low": 18.0,
            "age_high": 75.0,
            "low_bound": 5.0,
            "high_bound": 38.0,
        },
    ]

    # Exact local site match for Male 30yo at SITE-01
    matched_site = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="ALT",
        normalized_unit="U/L",
        lab_source="LOCAL",
        sex="M",
        age=30.0,
        site_id="SITE-01",
    )
    assert matched_site is not None
    assert matched_site["id"] == "R-SITE01-LOCAL"

    # Ethnicity match for Hispanic Female 40yo
    matched_eth = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="ALT",
        normalized_unit="U/L",
        lab_source="CENTRAL",
        sex="F",
        age=40.0,
        ethnicity="HISPANIC",
    )
    assert matched_eth is not None
    assert matched_eth["id"] == "R-HISPANIC-FEMALE"

    # Fallback to general male central for CENTRAL source
    matched_male = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="ALT",
        normalized_unit="U/L",
        lab_source="CENTRAL",
        sex="M",
        age=30.0,
        site_id="SITE-01",
    )
    assert matched_male is not None
    assert matched_male["id"] == "R-MALE-CENTRAL"


@pytest.mark.asyncio
async def test_evaluate_reference_range_indicators():
    """Verify normal, low, high, and critical low/high range indicators.

    @req:PRD-SYS-170
    @req:PRD-LAB-001
    @req:PRD-SYS-001
    """
    ref_range = {
        "low_bound": 10.0,
        "high_bound": 40.0,
        "critical_low": 5.0,
        "critical_high": 100.0,
    }

    # Normal: 25
    ind, out, bounds = evaluate_lab_value(25.0, ref_range)
    assert ind == "NORMAL"
    assert out is False
    assert bounds is not None

    # Low: 8
    ind_l, out_l, _ = evaluate_lab_value(8.0, ref_range)
    assert ind_l == "LOW"
    assert out_l is True

    # High: 55
    ind_h, out_h, _ = evaluate_lab_value(55.0, ref_range)
    assert ind_h == "HIGH"
    assert out_h is True

    # Critical Low: 3 (< 5.0)
    ind_cl, out_cl, _ = evaluate_lab_value(3.0, ref_range)
    assert ind_cl == "LOW LOW"
    assert out_cl is True

    # Critical High: 150 (> 100.0)
    ind_ch, out_ch, _ = evaluate_lab_value(150.0, ref_range)
    assert ind_ch == "HIGH HIGH"
    assert out_ch is True


@pytest.mark.asyncio
async def test_toxicity_grading_ctcae_and_daids():
    """Verify NCI-CTCAE v5.0 and DAIDS v2.1 toxicity grading calculations.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-SYS-123
    @req:PRD-PVG-001
    """
    # Hepatic ALT: ULN = 40.0 -> 240.0 is 6.0x ULN (Grade 3)
    tox_alt = LabAssessmentEngine.evaluate_toxicity(
        test_code="ALT",
        test_name="Alanine Aminotransferase",
        value=240.0,
        unit="U/L",
        ref_high=40.0,
        grading_scale="CTCAE_V5",
    )
    assert tox_alt.ctcae_grade == CTCAEGrade.GRADE_3
    assert tox_alt.is_critical_ae_trigger is True

    # Renal Creatinine: ULN = 1.0 -> 3.5 is 3.5x ULN (Grade 3)
    tox_creat = LabAssessmentEngine.evaluate_toxicity(
        test_code="CREAT",
        test_name="Serum Creatinine",
        value=3.5,
        unit="mg/dL",
        ref_high=1.0,
        grading_scale="CTCAE_V5",
    )
    assert tox_creat.ctcae_grade == CTCAEGrade.GRADE_3
    assert tox_creat.is_critical_ae_trigger is True

    # DAIDS v2.1 ALT: ULN = 40.0 -> 300.0 is 7.5x ULN (DAIDS Grade 3: 5.0 - 10.0x ULN)
    tox_daids = LabAssessmentEngine.evaluate_toxicity(
        test_code="ALT",
        test_name="Alanine Aminotransferase",
        value=300.0,
        unit="U/L",
        ref_high=40.0,
        grading_scale="DAIDS_V2_1",
    )
    assert tox_daids.ctcae_grade == CTCAEGrade.GRADE_3
    assert "DAIDS Grade 3" in tox_daids.toxicity_description


@pytest.mark.asyncio
async def test_hys_law_compound_dili_assessment():
    """Verify Hy's Law Drug-Induced Liver Injury compound assessment criteria.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-SYS-123
    @req:PRD-PVG-001
    """
    # Criteria: ALT > 3x ULN (e.g. 150 vs 35), Total Bili > 2x ULN (e.g. 3.0 vs 1.2), ALKP < 2x ULN (e.g. 100 vs 120)
    is_dili, msg = LabAssessmentEngine.check_hys_law(
        alt_val=150.0,
        ast_val=120.0,
        bili_val=3.0,
        alkp_val=100.0,
        alt_uln=35.0,
        ast_uln=35.0,
        bili_uln=1.2,
        alkp_uln=120.0,
    )
    assert is_dili is True
    assert "Hy's Law DILI Alert" in msg

    # Cholestatic elevation (ALKP > 2x ULN) should negate Hy's Law
    is_dili_cholestatic, _ = LabAssessmentEngine.check_hys_law(
        alt_val=150.0,
        ast_val=120.0,
        bili_val=3.0,
        alkp_val=300.0,  # > 2x ULN
        alt_uln=35.0,
        ast_uln=35.0,
        bili_uln=1.2,
        alkp_uln=120.0,
    )
    assert is_dili_cholestatic is False


@pytest.mark.asyncio
async def test_acute_delta_check():
    """Verify acute baseline delta calculation (>= 50% deviation with Grade >= 2).

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-LAB-001
    @req:PRD-SYS-123
    """
    # Baseline 1.0 -> Value 2.5 (+150% shift) with Grade 3 -> Delta Alert True
    is_delta, delta_pct = LabAssessmentEngine.check_delta(
        value=2.5,
        baseline_value=1.0,
        grade=3,
    )
    assert is_delta is True
    assert delta_pct == 150.0

    # Baseline 1.0 -> Value 1.2 (+20% shift) -> Delta Alert False
    is_delta_low, delta_pct_low = LabAssessmentEngine.check_delta(
        value=1.2,
        baseline_value=1.0,
        grade=2,
    )
    assert is_delta_low is False
    assert delta_pct_low == 20.0


@pytest.mark.asyncio
async def test_local_lab_certification_and_uncalibrated_safeguard():
    """Verify local laboratory accreditation verification and uncalibrated safeguard alerts.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    @req:PRD-LAB-001
    @req:Trace-15
    """
    async with db_manager.get_session_maker()() as session:
        now = datetime.now(UTC).replace(tzinfo=None)

        # Active certification
        cert_active = LocalLabCertification(
            study_id="STUDY-101",
            site_id="SITE-ACTIVE",
            lab_name="Metropolis Local Lab",
            certification_number="CLIA-998877",
            accreditation_body="CAP",
            effective_date=now - timedelta(days=100),
            expiration_date=now + timedelta(days=200),
            status="ACTIVE",
            is_accredited=True,
            reason_for_change="Annual accreditation",
        )
        # Expired certification
        cert_expired = LocalLabCertification(
            study_id="STUDY-101",
            site_id="SITE-EXPIRED",
            lab_name="Old Town Lab",
            certification_number="CLIA-112233",
            accreditation_body="CLIA",
            effective_date=now - timedelta(days=400),
            expiration_date=now - timedelta(days=30),
            status="ACTIVE",
            is_accredited=True,
            reason_for_change="Expired setup",
        )
        session.add_all([cert_active, cert_expired])
        await session.commit()

        # Check Active Site
        res_valid = await LabAssessmentEngine.verify_local_lab_certification(
            study_id="STUDY-101",
            site_id="SITE-ACTIVE",
            obs_date=now,
            session=session,
        )
        assert res_valid.is_valid is True
        assert res_valid.uncalibrated_safeguard_triggered is False

        # Check Expired Site
        res_exp = await LabAssessmentEngine.verify_local_lab_certification(
            study_id="STUDY-101",
            site_id="SITE-EXPIRED",
            obs_date=now,
            session=session,
        )
        assert res_exp.is_valid is False
        assert res_exp.uncalibrated_safeguard_triggered is True
        assert res_exp.status == "EXPIRED"

        # Check Missing Site
        res_miss = await LabAssessmentEngine.verify_local_lab_certification(
            study_id="STUDY-101",
            site_id="SITE-UNKNOWN",
            obs_date=now,
            session=session,
        )
        assert res_miss.is_valid is False
        assert res_miss.uncalibrated_safeguard_triggered is True
        assert res_miss.status == "MISSING"


@pytest.mark.asyncio
async def test_evaluate_laboratory_result_e2e_critical_ae_staging():
    """Verify end-to-end evaluation with automated Grade 4 AE eCRF drafting and query creation.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-LAB-001
    @req:PRD-PVG-001
    @req:PRD-SYS-123
    @req:ADR-2199
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Seed Subject & Reference Range
        subj = ClinicalSubject(
            study_id="STUDY-E2E",
            site_id="SITE-E2E",
            subject_id="SUBJ-9001",
            status="SCREENING",
            encrypted_demographics=encrypt_demographics(
                {"gender": "M", "birthdate": "1980-05-15", "ethnicity": "ASIAN"}
            ),
        )
        ref_range = LabReferenceRange(
            study_id="STUDY-E2E",
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
            reason_for_change="Protocol ranges",
        )
        session.add_all([subj, ref_range])
        await session.commit()

        # 2. Evaluate severe ALT observation (900 U/L -> 22.5x ULN -> Grade 4)
        raw_obs = {
            "subject_id": "SUBJ-9001",
            "study_id": "STUDY-E2E",
            "site_id": "SITE-E2E",
            "visit_id": "VISIT-02",
            "test_code": "ALT",
            "test_name": "Alanine Aminotransferase",
            "value": 900.0,
            "unit": "U/L",
            "observation_date": datetime.now(UTC),
            "lab_source": "CENTRAL",
        }

        ctx = LabAssessmentContext(
            session=session,
            study_id="STUDY-E2E",
            site_id="SITE-E2E",
            subject=subj,
            persist_records=True,
            user_id="e2e_tester",
            change_reason="Testing E2E Grade 4 AE escalation",
        )

        result: LabAssessmentResult = await evaluate_laboratory_result(
            observation=raw_obs,
            context=ctx,
        )
        await session.commit()

        # 3. Assert assessment result
        assert result.ctcae_grade == 4
        assert result.is_critical is True
        assert result.is_critical_ae_trigger is True
        assert result.indicator == "HIGH HIGH"
        assert result.out_of_range is True
        assert result.staged_ae_payload is not None
        assert result.staged_ae_payload["aeser"] == "Y"
        assert result.form_submission_record is not None
        assert result.staged_review_record is not None
        assert len(result.queries_raised) >= 1

        # 4. Verify database persistence of drafted records
        stmt_form = select(FormSubmission).where(
            FormSubmission.subject_id == "SUBJ-9001",
            FormSubmission.form_id == "AE",
        )
        res_form = await session.execute(stmt_form)
        saved_form = res_form.scalars().first()
        assert saved_form is not None
        assert saved_form.status == "DRAFT"

        stmt_review = select(StagedAdverseEventReview).where(
            StagedAdverseEventReview.subject_id == "SUBJ-9001",
            StagedAdverseEventReview.test_code == "ALT",
        )
        res_review = await session.execute(stmt_review)
        saved_review = res_review.scalars().first()
        assert saved_review is not None
        assert saved_review.ctcae_grade == 4
        assert saved_review.review_status == "STAGED_FOR_INVESTIGATOR_REVIEW"

        stmt_query = select(ClinicalQuery).where(
            ClinicalQuery.subject_id == "SUBJ-9001",
            ClinicalQuery.test_code == "ALT",
        )
        res_query = await session.execute(stmt_query)
        saved_query = res_query.scalars().first()
        assert saved_query is not None
        assert saved_query.priority == "CRITICAL"


@pytest.mark.asyncio
async def test_recalculate_cohort_range_flags():
    """Verify study-test cohort range flag recalculation and version incrementing.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    @req:PRD-LAB-001
    @req:Trace-15
    """
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            study_id="STUDY-RECALC",
            site_id="SITE-1",
            subject_id="SUBJ-RECALC-1",
            status="SCREENING",
            encrypted_demographics=encrypt_demographics(
                {"gender": "F", "birthdate": "1990-01-01"}
            ),
        )
        obs1 = ClinicalObservation(
            subject_id="SUBJ-RECALC-1",
            study_id="STUDY-RECALC",
            site_id="SITE-1",
            domain="LB",
            test_code="POTASSIUM",
            test_name="Potassium",
            value=6.5,
            normalized_value=6.5,
            normalized_unit="mmol/L",
            unit="mmol/L",
            lab_indicator=None,
            lab_out_of_range=False,
            version=1,
        )
        session.add_all([subj, obs1])
        await session.commit()

        # Add reference range for Potassium (Normal: 3.5 - 5.0, Critical High: 6.0)
        ref_k = LabReferenceRange(
            study_id="STUDY-RECALC",
            test_code="POTASSIUM",
            test_name="Potassium",
            source="CENTRAL",
            unit="mmol/L",
            normalized_unit="mmol/L",
            sex_applicability="ALL",
            low_bound=3.5,
            high_bound=5.0,
            critical_low=2.5,
            critical_high=6.0,
            reason_for_change="Electrolyte normal ranges",
        )
        session.add(ref_k)
        await session.commit()

        # Recalculate
        updated_count = await LabAssessmentEngine.recalculate_cohort_range_flags(
            session=session,
            study_id="STUDY-RECALC",
            test_code="POTASSIUM",
        )
        assert updated_count == 1

        # Verify observation was updated to HIGH HIGH
        await session.refresh(obs1)
        assert obs1.lab_indicator == "HIGH HIGH"
        assert obs1.lab_out_of_range is True
        assert obs1.version == 2
