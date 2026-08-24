"""Central & Local Laboratory Governance and Unit Conversion Test Suite.

Requirements:
- @req:PRD-SYS-170
- @req:PRD-LBM-001
- @req:PRD-LBM-002
- @req:PRD-SYS-001
- @req:Trace-1
- @req:Trace-15
"""

from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
    LabReferenceRange,
    LocalLabCertification,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.services.lab_assessment_engine import (
    LabAssessmentEngine,
    convert_lab_unit,
    get_loinc_catalog,
    resolve_loinc_mapping,
    select_reference_range,
)
from apps.execution.services.lab_ingestion_service import (
    LabIngestionService,
    parse_cdisc_lab_payload,
    parse_csv_payload,
    parse_fhir_payload,
    parse_hl7_v2_payload,
)
from packages.security.rbac_helpers import build_gateway_headers


def get_auth_headers(
    user_id: str = "lab_governance_user",
    roles: str = "site_investigator,investigator,crc,data_manager,cra",
    change_reason: str = "Laboratory governance test verification",
) -> dict[str, str]:
    """Generate Gateway signature-compliant authentication headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
    )


@pytest_asyncio.fixture(autouse=True)
async def setup_lab_governance_test_db():
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
async def test_central_lab_multi_format_ingestion_parsers():
    """Verify central laboratory batch ingestion across CSV, HL7 v2.x, FHIR JSON, and CDISC LAB.

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    """
    # 1. Delimited CSV parsing
    csv_payload = (
        "SubjectID,Visit,TestCode,TestName,Value,Unit,CollectionDate,LabSource\n"
        "SUBJ-101,V1,ALT,Alanine Aminotransferase,32.5,U/L,2026-08-01 08:30:00,CENTRAL\n"
        "SUBJ-101,V1,CREAT,Serum Creatinine,1.1,mg/dL,2026-08-01 08:30:00,CENTRAL\n"
    )
    csv_records, csv_errs = parse_csv_payload(csv_payload, default_study_id="STUDY-101")
    assert len(csv_errs) == 0
    assert len(csv_records) == 2
    assert csv_records[0].test_code == "ALT"
    assert csv_records[0].value == 32.5
    assert csv_records[1].test_code == "CREAT"

    # 2. HL7 v2.x (ORU^R01) parsing
    hl7_payload = (
        "MSH|^~\\&|CENTRAL_LAB|HOSPITAL|CADENCE|TRIAL|20260801090000||ORU^R01|MSG001|P|2.5\r"
        "PID|1||SUBJ-202^^^STUDY-101||DOE^JOHN||19850615|M\r"
        "PV1|1|O||||||||||||||||VISIT-01\r"
        "OBR|1||ORD001|1742-6^Alanine Aminotransferase^LN|||20260801083000||||||||||||CENTRAL\r"
        "OBX|1|NM|ALT^Alanine Aminotransferase||45.0|U/L|10-40|H|||F|||20260801083000\r"
    )
    hl7_records, hl7_errs = parse_hl7_v2_payload(
        hl7_payload, default_study_id="STUDY-101"
    )
    assert len(hl7_errs) == 0
    assert len(hl7_records) == 1
    assert hl7_records[0].subject_id == "SUBJ-202"
    assert hl7_records[0].test_code == "ALT"
    assert hl7_records[0].value == 45.0
    assert hl7_records[0].raw_abnormal_flag == "H"

    # 3. HL7 FHIR Observation JSON parsing
    fhir_payload = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "subject": {"reference": "Patient/SUBJ-303"},
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "2160-0",
                                "display": "Creatinine",
                            }
                        ]
                    },
                    "valueQuantity": {"value": 1.4, "unit": "mg/dL", "code": "mg/dL"},
                    "effectiveDateTime": "2026-08-01T10:00:00Z",
                    "referenceRange": [{"low": {"value": 0.6}, "high": {"value": 1.2}}],
                }
            }
        ],
    }
    fhir_records, fhir_errs = parse_fhir_payload(
        fhir_payload, default_study_id="STUDY-101"
    )
    assert len(fhir_errs) == 0
    assert len(fhir_records) == 1
    assert fhir_records[0].subject_id == "SUBJ-303"
    assert fhir_records[0].test_code == "2160-0"
    assert fhir_records[0].value == 1.4

    # 4. CDISC LAB Standard v1.0.1 JSON parsing
    cdisc_payload = {
        "study_id": "STUDY-101",
        "records": [
            {
                "subject_id": "SUBJ-404",
                "visit_id": "CYCLE-1",
                "test_code": "BILI",
                "test_name": "Total Bilirubin",
                "value": 1.8,
                "unit": "mg/dL",
                "reference_range_low": 0.2,
                "reference_range_high": 1.2,
                "observation_date": "2026-08-01T11:00:00Z",
                "lab_source": "CENTRAL",
            }
        ],
    }
    cdisc_records, cdisc_errs = parse_cdisc_lab_payload(cdisc_payload)
    assert len(cdisc_errs) == 0
    assert len(cdisc_records) == 1
    assert cdisc_records[0].test_code == "BILI"
    assert cdisc_records[0].value == 1.8


@pytest.mark.asyncio
async def test_loinc_standard_conversions_conventional_to_si():
    """Verify standard LOINC catalog mapping and mathematical unit conversions (conventional to SI units).

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    """
    # 1. LOINC Catalog Introspection
    catalog = get_loinc_catalog()
    test_codes = [item["test_code"] for item in catalog]
    assert "ALT" in test_codes
    assert "CREAT" in test_codes
    assert "BILI" in test_codes
    assert "GLUCOSE" in test_codes
    assert "HEMOGLOBIN" in test_codes

    alt_meta = resolve_loinc_mapping("ALT")
    assert alt_meta is not None
    assert alt_meta["loinc_code"] == "1742-6"

    # 2. Total Bilirubin: mg/dL to umol/L (factor 17.1)
    bili_si = await convert_lab_unit(
        session=None,
        test_code="BILI",
        from_unit="mg/dL",
        to_unit="umol/L",
        value=2.0,
    )
    assert pytest.approx(bili_si, rel=1e-3) == 34.2

    # 3. Serum Creatinine: mg/dL to umol/L (factor 88.4)
    creat_si = await convert_lab_unit(
        session=None,
        test_code="CREAT",
        from_unit="mg/dL",
        to_unit="umol/L",
        value=1.5,
    )
    assert pytest.approx(creat_si, rel=1e-3) == 132.6

    # 4. Fasting Glucose: mg/dL to mmol/L (factor 0.0555)
    gluc_si = await convert_lab_unit(
        session=None,
        test_code="GLUCOSE",
        from_unit="mg/dL",
        to_unit="mmol/L",
        value=180.0,
    )
    assert pytest.approx(gluc_si, rel=1e-2) == 9.99

    # 5. Hemoglobin: g/dL to g/L (factor 10.0)
    hgb_si = await convert_lab_unit(
        session=None,
        test_code="HEMOGLOBIN",
        from_unit="g/dL",
        to_unit="g/L",
        value=14.5,
    )
    assert pytest.approx(hgb_si, rel=1e-3) == 145.0


@pytest.mark.asyncio
async def test_reference_range_specificity_matching_and_tiebreaking():
    """Verify age, sex, ethnicity, and site specificity scoring and tie-breaking for reference ranges.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    """
    ranges = [
        # Broad central range
        {
            "id": "RANGE-GLOBAL-MALE",
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
        # Local hospital range for SITE-01
        {
            "id": "RANGE-SITE01-LOCAL",
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
    ]

    # Exact match for Local Site-01
    matched_local = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="ALT",
        normalized_unit="U/L",
        lab_source="LOCAL",
        sex="M",
        age=35.0,
        site_id="SITE-01",
    )
    assert matched_local is not None
    assert matched_local["id"] == "RANGE-SITE01-LOCAL"

    # Fallback for Central Lab
    matched_central = select_reference_range(
        ranges=ranges,
        study_id="STUDY-101",
        test_code="ALT",
        normalized_unit="U/L",
        lab_source="CENTRAL",
        sex="M",
        age=35.0,
        site_id="SITE-01",
    )
    assert matched_central is not None
    assert matched_central["id"] == "RANGE-GLOBAL-MALE"


@pytest.mark.asyncio
async def test_local_lab_certification_tracking_and_uncalibrated_safeguard():
    """Verify local laboratory CLIA/CAP certification tracking and uncalibrated safeguard alerts.

    @req:PRD-SYS-170
    @req:PRD-LBM-002
    @req:Trace-15
    """
    async with db_manager.get_session_maker()() as session:
        now = datetime.now(UTC).replace(tzinfo=None)

        # Active certification
        cert_active = LocalLabCertification(
            study_id="STUDY-101",
            site_id="SITE-CALIBRATED",
            lab_name="St. Jude Hospital Clinical Pathology",
            certification_number="CLIA-88776655",
            accreditation_body="CAP",
            effective_date=now - timedelta(days=60),
            expiration_date=now + timedelta(days=300),
            status="ACTIVE",
            is_accredited=True,
            reason_for_change="Annual accreditation",
        )
        # Expired certification
        cert_expired = LocalLabCertification(
            study_id="STUDY-101",
            site_id="SITE-EXPIRED",
            lab_name="County Hospital Lab",
            certification_number="CLIA-11223344",
            accreditation_body="CLIA",
            effective_date=now - timedelta(days=400),
            expiration_date=now - timedelta(days=15),
            status="ACTIVE",
            is_accredited=True,
            reason_for_change="Expired accreditation",
        )
        session.add_all([cert_active, cert_expired])
        await session.commit()

        # Check calibrated site
        cal_res = await LabAssessmentEngine.verify_local_lab_certification(
            study_id="STUDY-101",
            site_id="SITE-CALIBRATED",
            obs_date=now,
            session=session,
        )
        assert cal_res.is_valid is True
        assert cal_res.uncalibrated_safeguard_triggered is False

        # Check expired site
        exp_res = await LabAssessmentEngine.verify_local_lab_certification(
            study_id="STUDY-101",
            site_id="SITE-EXPIRED",
            obs_date=now,
            session=session,
        )
        assert exp_res.is_valid is False
        assert exp_res.uncalibrated_safeguard_triggered is True
        assert exp_res.status == "EXPIRED"

        # Check missing certification site
        miss_res = await LabAssessmentEngine.verify_local_lab_certification(
            study_id="STUDY-101",
            site_id="SITE-UNKNOWN",
            obs_date=now,
            session=session,
        )
        assert miss_res.is_valid is False
        assert miss_res.uncalibrated_safeguard_triggered is True
        assert miss_res.status == "MISSING"


@pytest.mark.asyncio
async def test_lab_batch_ingestion_service_end_to_end():
    """Verify batch ingestion pipeline execution with automated ClinicalObservation creation and safeguards.

    @req:PRD-SYS-170
    @req:PRD-LBM-001
    @req:PRD-SYS-001
    @req:Trace-1
    """
    async with db_manager.get_session_maker()() as session:
        # Seed Subject
        subj = ClinicalSubject(
            study_id="STUDY-E2E-INGEST",
            site_id="SITE-101",
            subject_id="SUBJ-INGEST-01",
            status="SCREENING",
            encrypted_demographics=encrypt_demographics(
                {"gender": "F", "birthdate": "1992-03-10"}
            ),
        )
        # Seed Reference Range for Platelets
        ref_plt = LabReferenceRange(
            study_id="STUDY-E2E-INGEST",
            test_code="PLT",
            test_name="Platelet Count",
            source="CENTRAL",
            unit="10^9/L",
            normalized_unit="10^9/L",
            sex_applicability="ALL",
            low_bound=150.0,
            high_bound=450.0,
            critical_low=50.0,
            critical_high=1000.0,
            reason_for_change="Hematology normal bounds",
        )
        session.add_all([subj, ref_plt])
        await session.commit()

        # Ingest CSV Batch
        csv_payload = (
            "SubjectID,Visit,TestCode,TestName,Value,Unit,CollectionDate,LabSource\n"
            "SUBJ-INGEST-01,V1,PLT,Platelet Count,180.0,10^9/L,2026-08-10 09:00:00,CENTRAL\n"
            "SUBJ-INGEST-01,V2,PLT,Platelet Count,45.0,10^9/L,2026-08-15 09:00:00,CENTRAL\n"
        )

        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_payload,
            format="csv",
            study_id="STUDY-E2E-INGEST",
            site_id="SITE-101",
            user_id="ingest_operator",
            change_reason="Batch platelet monitoring",
        )
        await session.commit()

        assert result.status == "COMPLETED"
        assert result.total_processed == 2
        assert result.ingested_count == 2
        assert result.out_of_range_count == 1
        assert (
            result.staged_aes_count == 1
        )  # 45.0 < 50.0 is Grade 3 Severe Thrombocytopenia
        assert result.queries_raised >= 1
