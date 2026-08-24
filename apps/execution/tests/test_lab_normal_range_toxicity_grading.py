"""Comprehensive Test Suite for Central & Local Lab Normal Range Governance
with Automated CTCAE/DAIDS Toxicity Grading, LOINC Conversions, and Investigator AE Staging.

Verifies:
1. CDISC LAB Standard v1.0.1 XML ODM and JSON ingestion.
2. Manual Local Lab batch entry.
3. Reference range governance with age, sex, and ethnicity specificity scoring.
4. Local lab certification (CLIA/CAP) tracking and uncalibrated range safeguards.
5. LOINC dictionary standard mapping and mathematical unit conversions (mg/dL <-> umol/L, conventional <-> SI).
6. Real-time toxicity grading across CTCAE v5.0 and DAIDS v2.1 (Hepatic, Renal, Hematology, Electrolytes, Metabolic).
7. Hy's Law DILI alert compound assessment.
8. Acute baseline delta checks (>= 50% shift).
9. Automated Grade 3/4 AE Trigger Workflow: drafting of staged FormSubmission (AE eCRF), StagedAdverseEventReview, and critical ClinicalQuery.
10. Investigator Review Queue and Actions (Confirm, Dismiss, Escalate SAE) with 21 CFR Part 11 electronic signatures and audit trails.

Requirements:
- @req:PRD-LAB-001
- @req:PRD-PVG-001
- @req:PRD-SYS-001
- @req:PRD-SYS-123
- @req:ADR-2199
- @req:Trace-1
- @req:Trace-15
"""

import os
from datetime import UTC, datetime, timedelta

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalQuery,
    ClinicalSubject,
    FormSubmission,
    LabReferenceRange,
    LocalLabCertification,
    StagedAdverseEventReview,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.lab_ranges import (
    convert_lab_unit,
    get_loinc_catalog,
    resolve_loinc_mapping,
    select_reference_range,
)
from apps.execution.main import app
from apps.execution.services.lab_ingestion_service import (
    LabIngestionService,
    parse_cdisc_lab_payload,
    parse_local_manual_payload,
)
from apps.execution.services.lab_toxicity_engine import (
    CTCAEGrade,
    LabToxicityEngine,
)
from packages.security.rbac_helpers import build_gateway_headers

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # nosec B105: test mock


def get_auth_headers(
    user_id: str = "investigator_user",
    roles: str = "site_investigator,investigator,crc,data_manager,cra",
    change_reason: str = "Testing Lab Governance & Toxicity Grading",
) -> dict[str, str]:
    """Generate Gateway signature-compliant authentication headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
    )


@pytest_asyncio.fixture(autouse=True)
async def setup_lab_governance_db():
    """Ensure in-memory database schema is clean and synchronized."""
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


# ============================================================================
# 1. CDISC LAB Standard v1.0.1 and Manual Local Lab Parsers
# ============================================================================


def test_cdisc_lab_xml_odm_parser():
    """Verify CDISC LAB v1.0.1 XML ODM payload parsing.

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    """
    xml_payload = """<?xml version="1.0" encoding="UTF-8"?>
    <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Transactional" FileOID="LB_001">
      <ClinicalData StudyOID="STUDY-101">
        <SubjectData SubjectKey="SUBJ-101" SiteOID="SITE-001">
          <StudyEventData StudyEventOID="VISIT-01">
            <FormData FormOID="LB_FORM">
              <ItemGroupData ItemGroupOID="LB">
                <ItemData ItemOID="LBTESTCD" Value="ALT"/>
                <ItemData ItemOID="LBTEST" Value="Alanine Aminotransferase"/>
                <ItemData ItemOID="LBORRES" Value="145.0"/>
                <ItemData ItemOID="LBORRESU" Value="U/L"/>
                <ItemData ItemOID="LBORNRLO" Value="10.0"/>
                <ItemData ItemOID="LBORNRHI" Value="35.0"/>
                <ItemData ItemOID="LBDTC" Value="2026-05-10T09:30:00"/>
                <ItemData ItemOID="LBNRIND" Value="HIGH"/>
              </ItemGroupData>
            </FormData>
          </StudyEventData>
        </SubjectData>
      </ClinicalData>
    </ODM>
    """
    records, errors = parse_cdisc_lab_payload(xml_payload, default_study_id="STUDY-101")
    assert len(errors) == 0
    assert len(records) == 1
    rec = records[0]
    assert rec.subject_id == "SUBJ-101"
    assert rec.site_id == "SITE-001"
    assert rec.visit_id == "VISIT-01"
    assert rec.test_code == "ALT"
    assert rec.test_name == "Alanine Aminotransferase"
    assert rec.value == 145.0
    assert rec.unit == "U/L"
    assert rec.reference_range_low == 10.0
    assert rec.reference_range_high == 35.0
    assert rec.raw_abnormal_flag == "HIGH"


def test_cdisc_lab_json_parser():
    """Verify CDISC LAB JSON format parsing.

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    """
    json_payload = {
        "records": [
            {
                "USUBJID": "SUBJ-202",
                "STUDYID": "STUDY-101",
                "SITEID": "SITE-002",
                "VISIT": "WEEK-4",
                "LBTESTCD": "CREAT",
                "LBTEST": "Serum Creatinine",
                "LBORRES": 2.4,
                "LBORRESU": "mg/dL",
                "LBORNRLO": 0.6,
                "LBORNRHI": 1.2,
                "LBDTC": "2026-06-01T14:00:00",
                "LBNRIND": "HIGH",
            }
        ]
    }
    records, errors = parse_cdisc_lab_payload(json_payload)
    assert len(errors) == 0
    assert len(records) == 1
    rec = records[0]
    assert rec.subject_id == "SUBJ-202"
    assert rec.test_code == "CREAT"
    assert rec.value == 2.4
    assert rec.unit == "mg/dL"


def test_local_manual_lab_parser():
    """Verify manual local lab batch entry parsing.

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    """
    manual_payload = {
        "site_id": "SITE-LOCAL-99",
        "study_id": "STUDY-101",
        "records": [
            {
                "subject_id": "SUBJ-303",
                "visit_id": "UNSCHEDULED",
                "test_code": "POTASSIUM",
                "test_name": "Potassium",
                "value": 6.8,
                "unit": "mmol/L",
                "reference_range_low": 3.5,
                "reference_range_high": 5.0,
                "critical_high": 6.0,
                "observation_date": "2026-06-15T11:20:00",
            }
        ],
    }
    records, errors = parse_local_manual_payload(manual_payload)
    assert len(errors) == 0
    assert len(records) == 1
    rec = records[0]
    assert rec.subject_id == "SUBJ-303"
    assert rec.lab_source == "LOCAL"
    assert rec.site_id == "SITE-LOCAL-99"
    assert rec.value == 6.8


# ============================================================================
# 2. Reference Range Specificity Scoring with Ethnicity
# ============================================================================


def test_reference_range_ethnicity_specificity_matching():
    """Verify reference range selection specificity with sex, age, and ethnicity.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    """
    ranges = [
        # Generic range: ALL sex, ALL age, ALL ethnicity
        {
            "id": "range-gen",
            "study_id": "STUDY-101",
            "test_code": "CREAT",
            "normalized_unit": "umol/L",
            "source": "CENTRAL",
            "sex_applicability": "ALL",
            "ethnicity_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 50.0,
            "high_bound": 100.0,
            "is_deleted": False,
        },
        # Sex-specific range: Male, ALL age, ALL ethnicity
        {
            "id": "range-male",
            "study_id": "STUDY-101",
            "test_code": "CREAT",
            "normalized_unit": "umol/L",
            "source": "CENTRAL",
            "sex_applicability": "M",
            "ethnicity_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 60.0,
            "high_bound": 110.0,
            "is_deleted": False,
        },
        # Sex + Ethnicity specific range: Male, Hispanic, ALL age
        {
            "id": "range-male-hispanic",
            "study_id": "STUDY-101",
            "test_code": "CREAT",
            "normalized_unit": "umol/L",
            "source": "CENTRAL",
            "sex_applicability": "M",
            "ethnicity_applicability": "HISPANIC",
            "age_low": None,
            "age_high": None,
            "low_bound": 65.0,
            "high_bound": 115.0,
            "is_deleted": False,
        },
        # Sex + Age + Ethnicity specific range: Male, 18-65, Asian
        {
            "id": "range-male-asian-adult",
            "study_id": "STUDY-101",
            "test_code": "CREAT",
            "normalized_unit": "umol/L",
            "source": "CENTRAL",
            "sex_applicability": "M",
            "ethnicity_applicability": "ASIAN",
            "age_low": 18.0,
            "age_high": 65.0,
            "low_bound": 55.0,
            "high_bound": 95.0,
            "is_deleted": False,
        },
    ]

    # Subject 1: Male, Age 35, Hispanic -> matches range-male-hispanic (Score: Sex=2, Ethnicity=2)
    m1 = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="CREAT",
        normalized_unit="umol/L",
        lab_source="CENTRAL",
        sex="M",
        age=35.0,
        ethnicity="HISPANIC",
    )
    assert m1 is not None
    assert m1["id"] == "range-male-hispanic"

    # Subject 2: Male, Age 30, Asian -> matches range-male-asian-adult (Score: Sex=2, Ethnicity=2, Age=3)
    m2 = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="CREAT",
        normalized_unit="umol/L",
        lab_source="CENTRAL",
        sex="M",
        age=30.0,
        ethnicity="ASIAN",
    )
    assert m2 is not None
    assert m2["id"] == "range-male-asian-adult"

    # Subject 3: Male, Age 40, Caucasian (no Caucasian specific range) -> falls back to range-male
    m3 = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="CREAT",
        normalized_unit="umol/L",
        lab_source="CENTRAL",
        sex="M",
        age=40.0,
        ethnicity="CAUCASIAN",
    )
    assert m3 is not None
    assert m3["id"] == "range-male"

    # Subject 4: Female -> falls back to range-gen
    m4 = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="CREAT",
        normalized_unit="umol/L",
        lab_source="CENTRAL",
        sex="F",
        age=28.0,
        ethnicity="HISPANIC",
    )
    assert m4 is not None
    assert m4["id"] == "range-gen"


# ============================================================================
# 3. LOINC Mapping and Standard Mathematical Unit Conversions
# ============================================================================


@pytest.mark.asyncio
async def test_loinc_catalog_and_mathematical_conversions():
    """Verify LOINC catalog standard resolution and mathematical unit conversions.

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    """
    # Check catalog items
    catalog = get_loinc_catalog()
    assert len(catalog) >= 10
    alt_loinc = resolve_loinc_mapping("ALT")
    assert alt_loinc is not None
    assert alt_loinc["loinc_code"] == "1742-6"

    creat_loinc = resolve_loinc_mapping("CREAT")
    assert creat_loinc is not None
    assert creat_loinc["loinc_code"] == "2160-0"

    # Test mathematical unit conversions without DB
    # 1. Creatinine: 1.0 mg/dL -> 88.4 umol/L
    c_creat = await convert_lab_unit(None, "CREAT", "mg/dL", "umol/L", 1.0)
    assert round(c_creat, 2) == 88.40

    # 2. Total Bilirubin: 2.0 mg/dL -> 34.2 umol/L (17.1 factor)
    c_bili = await convert_lab_unit(None, "BILI", "mg/dL", "umol/L", 2.0)
    assert round(c_bili, 2) == 34.20

    # 3. Fasting Glucose: 100 mg/dL -> 5.55 mmol/L (0.0555 factor)
    c_gluc = await convert_lab_unit(None, "GLUCOSE", "mg/dL", "mmol/L", 100.0)
    assert round(c_gluc, 2) == 5.55

    # 4. Hemoglobin: 14.0 g/dL -> 140.0 g/L
    c_hgb = await convert_lab_unit(None, "HEMOGLOBIN", "g/dL", "g/L", 14.0)
    assert c_hgb == 140.0


# ============================================================================
# 4. Toxicity Grading Engine: CTCAE v5.0, DAIDS, Hy's Law & Delta Checks
# ============================================================================


def test_ctcae_v5_and_daids_toxicity_grading():
    """Verify CTCAE v5.0 and DAIDS v2.1 toxicity evaluation across analytes.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    """
    # Hepatic ALT
    res_alt_g3 = LabToxicityEngine.evaluate_observation(
        test_code="ALT",
        test_name="ALT",
        value=200.0,
        unit="U/L",
        ref_high=35.0,
        grading_scale="CTCAE_V5",
    )
    assert res_alt_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert res_alt_g3.is_critical_ae_trigger is True

    # Renal Creatinine CTCAE
    res_creat_g4 = LabToxicityEngine.evaluate_observation(
        test_code="CREAT",
        test_name="Creatinine",
        value=8.0,
        unit="mg/dL",
        ref_high=1.2,
        grading_scale="CTCAE_V5",
    )
    assert res_creat_g4.ctcae_grade == CTCAEGrade.GRADE_4
    assert res_creat_g4.is_critical_ae_trigger is True

    # Hematology Platelets (Thrombocytopenia Grade 3: <50.0 x 10^9/L)
    res_plt_g3 = LabToxicityEngine.evaluate_observation(
        test_code="PLT",
        test_name="Platelets",
        value=35.0,
        unit="10^9/L",
        ref_low=150.0,
        grading_scale="CTCAE_V5",
    )
    assert res_plt_g3.ctcae_grade == CTCAEGrade.GRADE_3
    assert res_plt_g3.is_critical_ae_trigger is True

    # Hematology Hemoglobin (Severe Anemia Grade 3: <8.0 g/dL)
    res_hgb_g3 = LabToxicityEngine.evaluate_observation(
        test_code="HGB",
        test_name="Hemoglobin",
        value=7.2,
        unit="g/dL",
        ref_low=12.0,
        grading_scale="CTCAE_V5",
    )
    assert res_hgb_g3.ctcae_grade == CTCAEGrade.GRADE_3

    # Electrolytes Potassium Hyperkalemia Grade 4 (>7.0 mmol/L)
    res_k_g4 = LabToxicityEngine.evaluate_observation(
        test_code="POTASSIUM",
        test_name="Potassium",
        value=7.4,
        unit="mmol/L",
        ref_high=5.0,
        grading_scale="CTCAE_V5",
    )
    assert res_k_g4.ctcae_grade == CTCAEGrade.GRADE_4
    assert res_k_g4.is_critical_ae_trigger is True

    # Electrolytes Sodium Hyponatremia Grade 4 (<120 mmol/L)
    res_na_g4 = LabToxicityEngine.evaluate_observation(
        test_code="SODIUM",
        test_name="Sodium",
        value=115.0,
        unit="mmol/L",
        ref_low=135.0,
        grading_scale="CTCAE_V5",
    )
    assert res_na_g4.ctcae_grade == CTCAEGrade.GRADE_4

    # Metabolic Glucose Hyperglycemia Grade 3 (>250 mg/dL)
    res_gluc_g3 = LabToxicityEngine.evaluate_observation(
        test_code="GLUCOSE",
        test_name="Glucose",
        value=320.0,
        unit="mg/dL",
        ref_high=100.0,
        grading_scale="CTCAE_V5",
    )
    assert res_gluc_g3.ctcae_grade == CTCAEGrade.GRADE_3

    # DAIDS v2.1 scale test: ALT Grade 2 (2.5 - <5.0x ULN)
    res_daids_g2 = LabToxicityEngine.evaluate_observation(
        test_code="ALT",
        test_name="ALT",
        value=105.0,
        unit="U/L",
        ref_high=35.0,
        grading_scale="DAIDS_V2_1",
    )
    assert res_daids_g2.ctcae_grade == CTCAEGrade.GRADE_2
    assert "DAIDS Grade 2" in res_daids_g2.toxicity_description


def test_hys_law_dili_detection():
    """Verify Hy's Law compound alert for drug-induced liver injury.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-PVG-001
    """
    # Condition: ALT > 3x ULN (150 > 35*3=105), Total Bilirubin > 2x ULN (3.0 > 1.2*2=2.4), ALKP < 2x ULN (110 < 120*2=240)
    res = LabToxicityEngine.evaluate_observation(
        test_code="ALT",
        test_name="Alanine Aminotransferase",
        value=150.0,
        unit="U/L",
        ref_high=35.0,
        alt_co_analyte=150.0,
        bili_co_analyte=3.0,
        alkp_co_analyte=110.0,
    )

    assert res.is_hys_law_warning is True
    assert res.is_critical_ae_trigger is True
    assert "Hy's Law DILI Alert" in res.toxicity_description


def test_acute_delta_check_alert():
    """Verify acute baseline delta deviation alert (>=50% increase from baseline).

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    """
    res = LabToxicityEngine.evaluate_observation(
        test_code="CREAT",
        test_name="Creatinine",
        value=2.2,
        unit="mg/dL",
        ref_high=1.2,
        baseline_value=1.0,
    )

    assert res.is_delta_check_alert is True
    assert res.delta_percentage == 120.0


# ============================================================================
# 5. Local Lab Certification & Uncalibrated Safeguards
# ============================================================================


@pytest.mark.asyncio
async def test_local_lab_certification_safeguard():
    """Verify local lab certification tracking and uncalibrated safeguard trigger on expired cert.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    """
    async with db_manager.get_session_maker()() as session:
        # Create subject
        subj = ClinicalSubject(
            subject_id="SUBJ-SAFE-1",
            study_id="STUDY-101",
            site_id="SITE-EXPIRED-LAB",
            screening_number="SCR-001",
            encrypted_demographics=encrypt_demographics({"gender": "M", "age": 45}),
        )
        session.add(subj)

        # Create expired local lab certification
        expired_cert = LocalLabCertification(
            study_id="STUDY-101",
            site_id="SITE-EXPIRED-LAB",
            lab_name="Expired Local Laboratory Inc",
            certification_number="CLIA-EXPIRED-999",
            accreditation_body="CLIA",
            effective_date=datetime(2020, 1, 1),
            expiration_date=datetime(2021, 1, 1),  # Expired
            status="EXPIRED",
            is_accredited=True,
            reason_for_change="Initial setup",
        )
        session.add(expired_cert)
        await session.commit()

        # Ingest local lab observation for this site
        local_payload = {
            "site_id": "SITE-EXPIRED-LAB",
            "study_id": "STUDY-101",
            "records": [
                {
                    "subject_id": "SUBJ-SAFE-1",
                    "test_code": "ALT",
                    "value": 45.0,
                    "unit": "U/L",
                    "observation_date": "2026-05-15T10:00:00",
                }
            ],
        }

        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=local_payload,
            format="local_manual",
            study_id="STUDY-101",
            site_id="SITE-EXPIRED-LAB",
            lab_source="LOCAL",
        )

        assert result.status == "COMPLETED"
        assert result.uncalibrated_safeguards == 1
        assert result.queries_raised >= 1

        # Check safeguard query in database
        stmt_q = select(ClinicalQuery).where(
            ClinicalQuery.query_type == "UNCALIBRATED_LOCAL_LAB_WARNING",
            ClinicalQuery.site_id == "SITE-EXPIRED-LAB",
        )
        res_q = await session.execute(stmt_q)
        query = res_q.scalars().first()
        assert query is not None
        assert "Uncalibrated" in query.message or "expired" in query.message


# ============================================================================
# 6. Automated Grade 3/4 AE Trigger Workflow & Investigator Actions
# ============================================================================


@pytest.mark.asyncio
async def test_grade_3_4_ae_trigger_and_investigator_action_workflow():
    """Verify automated AE eCRF drafting, staged review creation, query creation,
    and Investigator Confirm/Dismiss action with 21 CFR Part 11 electronic signature.

    @req:PRD-SYS-171
    @req:PRD-LBM-003
    @req:PRD-PVG-001
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Setup subject and reference range
        subj = ClinicalSubject(
            subject_id="SUBJ-TOX-99",
            study_id="STUDY-101",
            site_id="SITE-001",
            screening_number="SCR-099",
            encrypted_demographics=encrypt_demographics({"gender": "M", "age": 50}),
        )
        session.add(subj)

        range_alt = LabReferenceRange(
            study_id="STUDY-101",
            test_code="ALT",
            test_name="Alanine Aminotransferase",
            source="CENTRAL",
            unit="U/L",
            normalized_unit="U/L",
            sex_applicability="ALL",
            low_bound=10.0,
            high_bound=35.0,
            critical_high=150.0,
            reason_for_change="Study range standard",
        )
        session.add(range_alt)
        await session.commit()

        # 2. Ingest observation causing Grade 3 ALT Toxicity (300 U/L > 5x ULN)
        csv_payload = (
            "subject_id,study_id,site_id,visit_id,test_code,test_name,value,unit,observation_date\n"
            "SUBJ-TOX-99,STUDY-101,SITE-001,VISIT-02,ALT,Alanine Aminotransferase,300.0,U/L,2026-06-01T10:00:00"
        )

        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_payload,
            format="csv",
            study_id="STUDY-101",
            site_id="SITE-001",
            lab_source="CENTRAL",
        )

        assert result.status == "COMPLETED"
        assert result.ingested_count == 1
        assert result.staged_aes_count == 1
        assert result.toxicity_grades.get("Grade 3") == 1

        # 3. Verify drafted FormSubmission (AE eCRF)
        stmt_form = select(FormSubmission).where(
            FormSubmission.subject_id == "SUBJ-TOX-99",
            FormSubmission.form_id == "AE",
        )
        res_form = await session.execute(stmt_form)
        draft_ae = res_form.scalars().first()
        assert draft_ae is not None
        assert draft_ae.status == "DRAFT"
        assert (
            draft_ae.payload.get("staging_status") == "STAGED_FOR_INVESTIGATOR_REVIEW"
        )
        assert draft_ae.payload.get("aetoxgr") == "Grade 3"

        # 4. Verify StagedAdverseEventReview record
        stmt_rev = select(StagedAdverseEventReview).where(
            StagedAdverseEventReview.subject_id == "SUBJ-TOX-99"
        )
        res_rev = await session.execute(stmt_rev)
        review = res_rev.scalars().first()
        assert review is not None
        assert review.ctcae_grade == 3
        assert review.review_status == "STAGED_FOR_INVESTIGATOR_REVIEW"
        assert review.form_submission_id == draft_ae.id
        assert review.query_id is not None

        # 5. Verify Critical ClinicalQuery raised
        stmt_q = select(ClinicalQuery).where(ClinicalQuery.id == review.query_id)
        res_q = await session.execute(stmt_q)
        query = res_q.scalars().first()
        assert query is not None
        assert query.query_type in ("GRADE_3_4_AE_TRIGGER", "POTENTIAL_SAE_CRITICAL")
        assert query.priority == "CRITICAL"
        assert query.status == "OPEN"

    # 6. Test Investigator Review Action via FastAPI REST API
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://testserver",
    ) as client:
        headers = get_auth_headers(
            user_id="dr_smith_investigator",
            roles="site_investigator,investigator",
            change_reason="Investigator confirmed clinical significance of Grade 3 ALT toxicity",
        )

        # GET review queue
        res_queue = await client.get(
            "/api/v1/execution/labs/toxicity/review-queue?study_id=STUDY-101",
            headers=headers,
        )
        assert res_queue.status_code == 200
        queue_items = res_queue.json()
        assert len(queue_items) == 1
        review_id = queue_items[0]["id"]

        # POST action: CONFIRM as AE
        action_payload = {
            "action": "CONFIRM",
            "investigator_notes": "Clinically significant acute hepatitis confirmed. Staged AE eCRF promoted to formal adverse event.",
            "reason_for_change": "21 CFR Part 11 Investigator AE confirmation",
        }
        res_action = await client.post(
            f"/api/v1/execution/labs/toxicity/review/{review_id}/action",
            json=action_payload,
            headers=headers,
        )
        assert res_action.status_code == 200
        action_res = res_action.json()
        assert action_res["review_status"] == "CONFIRMED_AS_AE"
        assert action_res["action_taken"] == "CONFIRM"

        # Verify DB state after Investigator confirmation
        async with db_manager.get_session_maker()() as session:
            stmt_post_rev = select(StagedAdverseEventReview).where(
                StagedAdverseEventReview.id == review_id
            )
            res_post_rev = await session.execute(stmt_post_rev)
            rev_updated = res_post_rev.scalars().first()
            assert rev_updated.review_status == "CONFIRMED_AS_AE"
            assert rev_updated.reviewed_by == "dr_smith_investigator"
            assert rev_updated.electronic_signature_manifest is not None
            assert (
                rev_updated.electronic_signature_manifest["signer_id"]
                == "dr_smith_investigator"
            )

            # FormSubmission promoted to SUBMITTED
            stmt_post_form = select(FormSubmission).where(
                FormSubmission.id == draft_ae.id
            )
            res_post_form = await session.execute(stmt_post_form)
            form_updated = res_post_form.scalars().first()
            assert form_updated.status == "SUBMITTED"
            assert form_updated.payload["staging_status"] == "CONFIRMED_AS_AE"
            assert form_updated.payload["confirmed_by"] == "dr_smith_investigator"
            assert (
                "Clinically significant"
                in form_updated.payload["investigator_confirmation_notes"]
            )


# ============================================================================
# 7. Local Lab Certification Verification REST Endpoints
# ============================================================================


@pytest.mark.asyncio
async def test_local_lab_certification_rest_endpoints():
    """Verify Local Lab Certification REST API CRUD and verification endpoints.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app),
        base_url="http://testserver",
    ) as client:
        headers = get_auth_headers(
            user_id="cra_lead",
            roles="cra,data_manager",
            change_reason="Registering CAP accredited laboratory",
        )

        # 1. Create certification
        now = datetime.now(UTC)
        cert_payload = {
            "study_id": "STUDY-101",
            "site_id": "SITE-005",
            "lab_name": "Metro Pathology Laboratory",
            "certification_number": "CAP-778899",
            "accreditation_body": "CAP",
            "effective_date": (now - timedelta(days=30)).isoformat(),
            "expiration_date": (now + timedelta(days=335)).isoformat(),
            "director_name": "Dr. Alice Morgan, MD",
            "is_accredited": True,
            "notes": "Annual CAP inspection passed with zero deficiencies.",
            "reason_for_change": "Initial local lab registration",
        }

        res_create = await client.post(
            "/api/v1/execution/labs/certifications",
            json=cert_payload,
            headers=headers,
        )
        assert res_create.status_code == 201
        created = res_create.json()
        assert created["certification_number"] == "CAP-778899"
        assert created["status"] == "ACTIVE"
        assert created["id"] is not None

        # 2. List certifications
        res_list = await client.get(
            "/api/v1/execution/labs/certifications?study_id=STUDY-101",
            headers=headers,
        )
        assert res_list.status_code == 200
        items = res_list.json()
        assert len(items) >= 1

        # 3. Verify certification validity
        res_verify = await client.post(
            "/api/v1/execution/labs/certifications/verify",
            json={"study_id": "STUDY-101", "site_id": "SITE-005"},
            headers=headers,
        )
        assert res_verify.status_code == 200
        verify_data = res_verify.json()
        assert verify_data["is_valid"] is True
        assert verify_data["status"] == "ACTIVE"
        assert verify_data["uncalibrated_safeguard_triggered"] is False
        assert verify_data["days_until_expiration"] > 300

        # 4. LOINC Catalog REST Endpoint
        res_loinc = await client.get(
            "/api/v1/execution/labs/loinc-catalog",
            headers=headers,
        )
        assert res_loinc.status_code == 200
        loinc_data = res_loinc.json()
        assert loinc_data["total_count"] >= 10

        # 5. Unit Conversion REST Endpoint
        res_conv = await client.post(
            "/api/v1/execution/labs/convert-unit",
            json={
                "test_code": "CREAT",
                "from_unit": "mg/dL",
                "to_unit": "umol/L",
                "value": 1.5,
            },
            headers=headers,
        )
        assert res_conv.status_code == 200
        conv_res = res_conv.json()
        assert round(conv_res["converted_value"], 2) == 132.60
