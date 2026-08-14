"""Test suite for Central & Local Lab Batch Ingestion Pipeline.

Verifies:
- CSV/TSV delimited parsing with auto-delimitation and header aliasing
- HL7 v2.x (ORU^R01) message parsing (MSH, PID, PV1, OBR, OBX)
- HL7 FHIR Observation resource JSON & Bundle parsing
- Demographic age/sex-stratified normal reference range evaluation
- UCUM unit normalization & database catalog unit conversions
- Automated discrepancy query creation (OUT_OF_RANGE_WARNING)
- Potential SAE critical threshold alerts (POTENTIAL_SAE_CRITICAL)
- REST endpoints (/api/v1/execution/labs/ingest & /api/v1/execution/labs/batch-status)

Requirements:
- @req:PRD-LAB-001
- @req:PRD-MDR-001
- @req:PRD-QRY-001
- @req:Trace-1
- @req:Trace-15
"""

import hashlib
import hmac
import json
import os
import time
from datetime import datetime

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    LabReferenceRange,
    LabTestMaster,
    LabUnitConversion,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app
from apps.execution.services.lab_ingestion_service import (
    LabIngestionService,
    parse_fhir_payload,
    parse_hl7_v2_payload,
)

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # nosec B105: mock test fallback


def get_auth_headers(
    user_id: str = "crc_user",
    roles: str = "crc,investigator",
    change_reason: str = "Laboratory batch ingestion test",
) -> dict[str, str]:
    """Generate Gateway signature-compliant authentication headers."""
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_csv_batch_ingestion_success():
    """Verify delimited CSV ingestion creates ClinicalObservation and discrepancy queries.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-LAB-01"
    async with db_manager.get_session_maker()() as session:
        # 1. Seed Subject
        subj = ClinicalSubject(
            subject_id="SUBJ-001",
            study_id=study_id,
            site_id="SITE-101",
            encrypted_demographics=encrypt_demographics(
                {"gender": "M", "birthdate": "1990-05-15"}
            ),
        )
        session.add(subj)

        # 2. Seed LabReferenceRange
        ref_range = LabReferenceRange(
            study_id=study_id,
            test_code="WBC",
            test_name="White Blood Cell Count",
            source="CENTRAL",
            unit="10^9/L",
            normalized_unit="10^9/L",
            sex_applicability="M",
            age_low=18.0,
            age_high=65.0,
            low_bound=4.0,
            high_bound=10.0,
            critical_low=2.0,
            critical_high=20.0,
        )
        session.add(ref_range)
        await session.commit()

    csv_data = """Subject ID,Test Code,Test Name,Value,Unit,Collection Date,Lab Source
SUBJ-001,WBC,White Blood Cell Count,5.5,10^9/L,2026-08-10 09:30:00,CENTRAL
SUBJ-001,WBC,White Blood Cell Count,14.2,10^9/L,2026-08-11 10:00:00,CENTRAL
"""

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_data,
            format="csv",
            study_id=study_id,
            site_id="SITE-101",
        )

        assert result.status == "COMPLETED"
        assert result.total_processed == 2
        assert result.ingested_count == 2
        assert result.out_of_range_count == 1
        assert result.critical_alerts == 0
        assert result.queries_raised == 1

        # Verify persisted observations
        stmt_obs = (
            select(ClinicalObservation)
            .where(ClinicalObservation.study_id == study_id)
            .order_by(ClinicalObservation.observation_date.asc())
        )
        res_obs = await session.execute(stmt_obs)
        observations = res_obs.scalars().all()
        assert len(observations) == 2

        # First observation (Normal)
        assert observations[0].value == 5.5
        assert observations[0].lab_indicator == "NORMAL"
        assert observations[0].lab_out_of_range is False

        # Second observation (High)
        assert observations[1].value == 14.2
        assert observations[1].lab_indicator == "HIGH"
        assert observations[1].lab_out_of_range is True

        # Verify generated ClinicalQuery
        stmt_q = select(ClinicalQuery).where(ClinicalQuery.study_id == study_id)
        res_q = await session.execute(stmt_q)
        queries = res_q.scalars().all()
        assert len(queries) == 1
        assert queries[0].subject_id == "SUBJ-001"
        assert queries[0].test_code == "WBC"
        assert queries[0].query_type == "OUT_OF_RANGE_WARNING"
        assert queries[0].priority == "HIGH"
        assert queries[0].status == "OPEN"
        assert queries[0].observation_id == observations[1].id


@pytest.mark.asyncio
async def test_hl7_batch_ingestion_oru_r01():
    """Verify parsing and ingestion of HL7 v2.x ORU^R01 messages with OBX segments.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-HL7-01"
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-HL7-001",
            study_id=study_id,
            site_id="SITE-201",
            encrypted_demographics=encrypt_demographics(
                {"gender": "F", "birthdate": "1985-02-20"}
            ),
        )
        session.add(subj)

        ref_range = LabReferenceRange(
            study_id=study_id,
            test_code="HGB",
            test_name="Hemoglobin",
            source="CENTRAL",
            unit="g/dL",
            normalized_unit="g/dL",
            sex_applicability="F",
            age_low=18.0,
            age_high=80.0,
            low_bound=12.0,
            high_bound=16.0,
            critical_low=7.0,
            critical_high=20.0,
        )
        session.add(ref_range)
        await session.commit()

    hl7_message = (
        "MSH|^~\\&|CENTRAL_LAB|SITE-201|CADENCE_EDC|SPONSOR|20260813120000||ORU^R01|MSG001|P|2.5\r"
        "PID|1||SUBJ-HL7-001^^^MRN||DOE^JANE||19850220|F\r"
        "PV1|1|O||||||||||||||||VISIT-01\r"
        "OBR|1|ORD1001|LAB1001|CBC^Complete Blood Count|||20260813090000||||||||||||CENTRAL\r"
        "OBX|1|NM|HGB^Hemoglobin^LN||13.5|g/dL|12.0-16.0|N|||F|||20260813090000\r"
        "OBX|2|NM|HGB^Hemoglobin^LN||6.5|g/dL|12.0-16.0|LL|||F|||20260813140000\r"
    )

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=hl7_message,
            format="hl7",
            study_id=study_id,
            site_id="SITE-201",
        )

        assert result.status == "COMPLETED"
        assert result.total_processed == 2
        assert result.ingested_count == 2
        assert result.out_of_range_count == 1
        assert result.critical_alerts == 1
        assert result.queries_raised == 1

        # Verify critical SAE query created
        stmt_q = select(ClinicalQuery).where(ClinicalQuery.study_id == study_id)
        res_q = await session.execute(stmt_q)
        queries = res_q.scalars().all()
        assert len(queries) == 1
        assert queries[0].query_type == "POTENTIAL_SAE_CRITICAL"
        assert queries[0].priority == "CRITICAL"
        assert queries[0].status == "OPEN"


@pytest.mark.asyncio
async def test_fhir_observation_json_ingestion():
    """Verify ingestion of FHIR Observation JSON resources and Bundles.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-FHIR-01"
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-FHIR-001",
            study_id=study_id,
            site_id="SITE-301",
            encrypted_demographics=encrypt_demographics(
                {"gender": "M", "birthdate": "1975-11-10"}
            ),
        )
        session.add(subj)

        ref_range = LabReferenceRange(
            study_id=study_id,
            test_code="GLUC",
            test_name="Fasting Glucose",
            source="CENTRAL",
            unit="mg/dL",
            normalized_unit="mg/dL",
            sex_applicability="ALL",
            low_bound=70.0,
            high_bound=99.0,
            critical_low=40.0,
            critical_high=400.0,
        )
        session.add(ref_range)
        await session.commit()

    fhir_bundle = {
        "resourceType": "Bundle",
        "type": "transaction",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-glucose-01",
                    "status": "final",
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "GLUC",
                                "display": "Fasting Glucose",
                            }
                        ]
                    },
                    "subject": {"reference": "Patient/SUBJ-FHIR-001"},
                    "effectiveDateTime": "2026-08-12T08:00:00Z",
                    "valueQuantity": {
                        "value": 115.0,
                        "unit": "mg/dL",
                        "system": "http://unitsofmeasure.org",
                        "code": "mg/dL",
                    },
                    "referenceRange": [
                        {"low": {"value": 70.0}, "high": {"value": 99.0}}
                    ],
                    "interpretation": [{"coding": [{"code": "H", "display": "High"}]}],
                }
            }
        ],
    }

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=fhir_bundle,
            format="fhir",
            study_id=study_id,
            site_id="SITE-301",
        )

        assert result.status == "COMPLETED"
        assert result.total_processed == 1
        assert result.ingested_count == 1
        assert result.out_of_range_count == 1
        assert result.queries_raised == 1

        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-FHIR-001"
        )
        res_obs = await session.execute(stmt_obs)
        obs = res_obs.scalars().first()
        assert obs is not None
        assert obs.test_code == "GLUC"
        assert obs.value == 115.0
        assert obs.lab_indicator == "HIGH"
        assert obs.lab_out_of_range is True


@pytest.mark.asyncio
async def test_age_sex_stratified_reference_range_evaluation():
    """Verify that age and sex demographics determine the matched reference range.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-STRAT-01"
    async with db_manager.get_session_maker()() as session:
        # Adult Male (Age ~36)
        session.add(
            ClinicalSubject(
                subject_id="SUBJ-MALE",
                study_id=study_id,
                site_id="SITE-01",
                encrypted_demographics=encrypt_demographics(
                    {"gender": "M", "birthdate": "1990-01-01"}
                ),
            )
        )
        # Adult Female (Age ~36)
        session.add(
            ClinicalSubject(
                subject_id="SUBJ-FEMALE",
                study_id=study_id,
                site_id="SITE-01",
                encrypted_demographics=encrypt_demographics(
                    {"gender": "F", "birthdate": "1990-01-01"}
                ),
            )
        )

        # Male range: 13.5 - 17.5
        session.add(
            LabReferenceRange(
                study_id=study_id,
                test_code="HGB",
                test_name="Hemoglobin",
                source="CENTRAL",
                unit="g/dL",
                normalized_unit="g/dL",
                sex_applicability="M",
                low_bound=13.5,
                high_bound=17.5,
            )
        )
        # Female range: 12.0 - 15.5
        session.add(
            LabReferenceRange(
                study_id=study_id,
                test_code="HGB",
                test_name="Hemoglobin",
                source="CENTRAL",
                unit="g/dL",
                normalized_unit="g/dL",
                sex_applicability="F",
                low_bound=12.0,
                high_bound=15.5,
            )
        )
        await session.commit()

    # Value 13.0 is LOW for Male (13.5-17.5), but NORMAL for Female (12.0-15.5)
    csv_data = """Subject ID,Test Code,Value,Unit,Collection Date
SUBJ-MALE,HGB,13.0,g/dL,2026-08-13
SUBJ-FEMALE,HGB,13.0,g/dL,2026-08-13
"""

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_data,
            format="csv",
            study_id=study_id,
        )

        assert result.status == "COMPLETED"
        assert result.ingested_count == 2
        assert result.out_of_range_count == 1

        stmt_male = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-MALE"
        )
        res_male = await session.execute(stmt_male)
        obs_male = res_male.scalars().first()
        assert obs_male.lab_indicator == "LOW"
        assert obs_male.lab_out_of_range is True

        stmt_female = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-FEMALE"
        )
        res_female = await session.execute(stmt_female)
        obs_female = res_female.scalars().first()
        assert obs_female.lab_indicator == "NORMAL"
        assert obs_female.lab_out_of_range is False


@pytest.mark.asyncio
async def test_critical_sae_alert_trigger():
    """Verify critical boundaries (critical_low, critical_high) trigger SAE alerts.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-SAE-01"
    async with db_manager.get_session_maker()() as session:
        session.add(
            ClinicalSubject(
                subject_id="SUBJ-SAE-001",
                study_id=study_id,
                site_id="SITE-SAE",
            )
        )
        session.add(
            LabReferenceRange(
                study_id=study_id,
                test_code="POTASSIUM",
                test_name="Serum Potassium",
                source="CENTRAL",
                unit="mmol/L",
                normalized_unit="mmol/L",
                sex_applicability="ALL",
                low_bound=3.5,
                high_bound=5.0,
                critical_low=2.5,
                critical_high=6.5,
            )
        )
        await session.commit()

    # Ingest critical high potassium (7.2 > 6.5)
    csv_data = """Subject ID,Test Code,Value,Unit
SUBJ-SAE-001,POTASSIUM,7.2,mmol/L
"""

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_data,
            format="csv",
            study_id=study_id,
        )

        assert result.critical_alerts == 1
        assert result.out_of_range_count == 1
        assert result.queries_raised == 1

        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-SAE-001"
        )
        res_obs = await session.execute(stmt_obs)
        obs = res_obs.scalars().first()
        assert obs.lab_indicator == "HIGH HIGH"
        assert obs.lab_out_of_range is True

        stmt_q = select(ClinicalQuery).where(ClinicalQuery.observation_id == obs.id)
        res_q = await session.execute(stmt_q)
        q = res_q.scalars().first()
        assert q.query_type == "POTENTIAL_SAE_CRITICAL"
        assert q.priority == "CRITICAL"


@pytest.mark.asyncio
async def test_ucum_unit_conversion_integration():
    """Verify UCUM unit conversion and catalog conversion factor execution.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-UCUM-01"
    async with db_manager.get_session_maker()() as session:
        session.add(
            ClinicalSubject(
                subject_id="SUBJ-UCUM-01",
                study_id=study_id,
            )
        )
        # Catalog configuration: default unit is mg/dL, normalized unit is g/L
        session.add(
            LabTestMaster(
                study_id=study_id,
                test_code="CHOLESTEROL",
                test_name="Total Cholesterol",
                default_unit="mg/dL",
                normalized_unit="g/L",
            )
        )
        # Custom unit conversion formula: 1 mg/dL = 0.01 g/L
        session.add(
            LabUnitConversion(
                study_id=study_id,
                test_code="CHOLESTEROL",
                from_unit="mg/dL",
                to_unit="g/L",
                factor=0.01,
            )
        )
        # Reference range in normalized unit (g/L): 1.5 - 2.0 g/L
        session.add(
            LabReferenceRange(
                study_id=study_id,
                test_code="CHOLESTEROL",
                test_name="Total Cholesterol",
                source="CENTRAL",
                unit="g/L",
                normalized_unit="g/L",
                sex_applicability="ALL",
                low_bound=1.5,
                high_bound=2.0,
            )
        )
        await session.commit()

    # Value 180.0 mg/dL converted to 1.8 g/L (NORMAL inside 1.5 - 2.0)
    csv_data = """Subject ID,Test Code,Value,Unit
SUBJ-UCUM-01,CHOLESTEROL,180.0,mg/dL
"""

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_data,
            format="csv",
            study_id=study_id,
        )

        assert result.status == "COMPLETED"
        assert result.ingested_count == 1

        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-UCUM-01"
        )
        res_obs = await session.execute(stmt_obs)
        obs = res_obs.scalars().first()
        assert obs.value == 180.0
        assert obs.unit == "mg/dL"
        assert pytest.approx(obs.normalized_value) == 1.8
        assert obs.normalized_unit == "g/L"
        assert obs.lab_indicator == "NORMAL"
        assert obs.lab_out_of_range is False


@pytest.mark.asyncio
async def test_csv_parser_resilience_and_errors():
    """Verify delimiter detection, header normalization, and error isolation on malformed records.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-RES-01"
    # Semicolon delimited data with 1 valid row, 1 row missing test_code, 1 row missing value
    tsv_data = (
        "patient_id\tparamcd\tmeasurement\tuom\n"
        "SUBJ-999\tALT\t35.0\tU/L\n"
        "\tAST\t40.0\tU/L\n"
        "SUBJ-999\t\t50.0\tU/L\n"
        "SUBJ-999\tBILI\t\tmg/dL\n"
    )

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=tsv_data,
            format="csv",
            study_id=study_id,
        )

        assert result.status == "COMPLETED_WITH_ERRORS"
        assert result.total_processed == 4
        assert result.ingested_count == 1
        assert len(result.errors) == 3


@pytest.mark.asyncio
async def test_hl7_and_fhir_parser_resilience():
    """Verify HL7 and FHIR parsing error reporting on invalid payloads.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    # 1. HL7 missing PID
    hl7_bad = "MSH|^~\\&|LAB|SITE|EDC|SPONSOR\rOBX|1|NM|WBC||5.0|10^9/L\r"
    records, errors = parse_hl7_v2_payload(hl7_bad)
    assert len(errors) > 0

    # 2. FHIR invalid JSON
    fhir_bad = "{ invalid json string"
    records_f, errors_f = parse_fhir_payload(fhir_bad)
    assert len(errors_f) > 0

    # 3. FHIR Observation missing subject
    fhir_no_subj = {
        "resourceType": "Observation",
        "code": {"text": "WBC"},
        "valueQuantity": {"value": 5.0},
    }
    records_s, errors_s = parse_fhir_payload(fhir_no_subj)
    assert len(errors_s) > 0


@pytest.mark.asyncio
async def test_api_ingest_json_endpoint():
    """Verify POST /api/v1/execution/labs/ingest with JSON payload.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-API-01"
    headers = get_auth_headers(roles="crc", change_reason="Batch lab test via API")

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        payload = {
            "format": "csv",
            "study_id": study_id,
            "site_id": "SITE-01",
            "payload": "subject_id,test_code,value,unit\nSUBJ-API-1,WBC,6.2,10^9/L\n",
        }

        res = await client.post(
            "/api/v1/execution/labs/ingest",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 200
        data = res.json()
        assert data["status"] == "COMPLETED"
        assert data["ingested_count"] == 1
        assert data["batch_id"] is not None

        # Query batch status by ID
        batch_id = data["batch_id"]
        status_res = await client.get(
            f"/api/v1/execution/labs/batch-status/{batch_id}",
            headers=headers,
        )
        assert status_res.status_code == 200
        assert status_res.json()["batch_id"] == batch_id


@pytest.mark.asyncio
async def test_api_ingest_multipart_file_endpoint():
    """Verify POST /api/v1/execution/labs/ingest with multipart/form-data file upload.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    headers = get_auth_headers(roles="data_manager", change_reason="File upload test")

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        file_content = b"subject_id,test_code,value,unit\nSUBJ-FILE-1,ALB,4.2,g/dL\n"
        files = {
            "file": ("labs.csv", file_content, "text/csv"),
        }
        data = {
            "format": "csv",
            "study_id": "STUDY-FILE-01",
            "site_id": "SITE-FILE-01",
            "reason_for_change": "CSV file upload",
        }

        res = await client.post(
            "/api/v1/execution/labs/ingest",
            data=data,
            files=files,
            headers=headers,
        )
        assert res.status_code == 200
        res_data = res.json()
        assert res_data["status"] == "COMPLETED"
        assert res_data["ingested_count"] == 1


@pytest.mark.asyncio
async def test_api_batch_status_list_and_not_found():
    """Verify GET /api/v1/execution/labs/batch-status querying and 404 behavior.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    headers = get_auth_headers(roles="investigator")

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # List all batches
        res = await client.get(
            "/api/v1/execution/labs/batch-status",
            headers=headers,
        )
        assert res.status_code == 200
        assert isinstance(res.json(), list)

        # 404 on non-existent batch
        bad_res = await client.get(
            "/api/v1/execution/labs/batch-status/non-existent-batch-id",
            headers=headers,
        )
        assert bad_res.status_code == 404


@pytest.mark.asyncio
async def test_parse_helpers_unit_coverage():
    """Verify parser helper functions handle diverse date, numeric, and range formats.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    from apps.execution.services.lab_ingestion_service import (
        _parse_iso_or_clinical_date,
        _parse_numeric_value,
        _parse_reference_range_bounds,
    )

    # Date parsing tests
    assert _parse_iso_or_clinical_date(None) is None
    assert _parse_iso_or_clinical_date("") is None
    assert _parse_iso_or_clinical_date("2026-08-13T12:30:00Z") == datetime(
        2026, 8, 13, 12, 30, 0
    )
    assert _parse_iso_or_clinical_date("2026/08/13 14:00:00") == datetime(
        2026, 8, 13, 14, 0, 0
    )
    assert _parse_iso_or_clinical_date("13-08-2026 15:30:00") == datetime(
        2026, 8, 13, 15, 30, 0
    )
    assert _parse_iso_or_clinical_date("13/08/2026 16:45:00") == datetime(
        2026, 8, 13, 16, 45, 0
    )
    assert _parse_iso_or_clinical_date("08/13/2026 17:00:00") == datetime(
        2026, 8, 13, 17, 0, 0
    )
    assert _parse_iso_or_clinical_date("20260813180000") == datetime(
        2026, 8, 13, 18, 0, 0
    )
    assert _parse_iso_or_clinical_date("20260813") == datetime(2026, 8, 13, 0, 0, 0)
    assert _parse_iso_or_clinical_date("not-a-valid-date") is None

    # Numeric value parsing tests
    assert _parse_numeric_value(None) == (None, None)
    assert _parse_numeric_value("") == (None, None)
    assert _parse_numeric_value(42) == (42.0, "42")
    assert _parse_numeric_value(3.14) == (3.14, "3.14")
    assert _parse_numeric_value("< 0.05") == (0.05, "< 0.05")
    assert _parse_numeric_value(">= 100") == (100.0, ">= 100")
    assert _parse_numeric_value("Negative") == (None, "Negative")

    # Reference range bounds parsing tests
    assert _parse_reference_range_bounds(None) == (None, None)
    assert _parse_reference_range_bounds("") == (None, None)
    assert _parse_reference_range_bounds("4.0-11.0") == (4.0, 11.0)
    assert _parse_reference_range_bounds("10 to 40") == (10.0, 40.0)
    assert _parse_reference_range_bounds("< 200") == (None, 200.0)
    assert _parse_reference_range_bounds("> 60") == (60.0, None)
    assert _parse_reference_range_bounds("non-numeric") == (None, None)


@pytest.mark.asyncio
async def test_fhir_diverse_structures_and_types():
    """Verify FHIR parsing for valueString, valueInteger, valueCodeableConcept, and arrays.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-FHIR-DIV"
    fhir_list = [
        {
            "resourceType": "Observation",
            "subject": {"display": "SUBJ-FHIR-STR"},
            "code": "URINE_COLOR",
            "valueString": "Straw Yellow",
            "effectiveInstant": "2026-08-13T10:00:00Z",
        },
        {
            "resourceType": "Observation",
            "subject": {"identifier": {"value": "SUBJ-FHIR-INT"}},
            "code": {"text": "PULSE"},
            "valueInteger": 72,
            "effectivePeriod": {"start": "2026-08-13T10:15:00Z"},
        },
        {
            "resourceType": "Observation",
            "subject": "Patient/SUBJ-FHIR-CC",
            "code": {"coding": [{"code": "BLOOD_TYPE", "display": "ABO and Rh group"}]},
            "valueCodeableConcept": {"text": "O Positive"},
            "encounter": {"reference": "Encounter/ENC-01"},
        },
    ]

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=fhir_list,
            format="fhir",
            study_id=study_id,
        )

        assert result.status == "COMPLETED"
        assert result.ingested_count == 3

        stmt_obs = (
            select(ClinicalObservation)
            .where(ClinicalObservation.study_id == study_id)
            .order_by(ClinicalObservation.subject_id.asc())
        )
        res_obs = await session.execute(stmt_obs)
        observations = res_obs.scalars().all()
        assert len(observations) == 3

        assert observations[0].subject_id == "SUBJ-FHIR-CC"
        assert observations[0].value_string == "O Positive"
        assert observations[0].visit_id == "ENC-01"

        assert observations[1].subject_id == "SUBJ-FHIR-INT"
        assert observations[1].value == 72.0

        assert observations[2].subject_id == "SUBJ-FHIR-STR"
        assert observations[2].value_string == "Straw Yellow"


@pytest.mark.asyncio
async def test_hl7_diverse_segments_and_abnormal_flags():
    """Verify HL7 v2 parsing for various abnormal flags (A, N, LL, HH) and byte payloads.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_id = "STUDY-HL7-FLAGS"
    hl7_text = (
        "MSH|^~\\&|LOCAL_LAB|SITE-LOCAL|||20260813||ORU^R01|101|P|2.5\n\n"
        "PID|1||SUBJ-FLAGS-1|||||M\n"
        "OBR|1|||PANEL|||20260813100000||||||||||||LOCAL\n"
        "OBX|1|ST|TEST_A^Analyte A||POS|units||A|||F\n"
        "OBX|2|NM|TEST_HH^Analyte HH||150.0|mg/dL|50-100|HH|||F\n"
        "OBX|3|NM|TEST_N^Analyte Normal||75.0|mg/dL|50-100|N|||F\n"
    )

    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=hl7_text.encode("utf-8"),
            format="hl7",
            study_id=study_id,
        )

        assert result.status == "COMPLETED"
        assert result.ingested_count == 3
        assert result.critical_alerts == 1
        assert result.queries_raised == 2

        stmt_obs = (
            select(ClinicalObservation)
            .where(ClinicalObservation.study_id == study_id)
            .order_by(ClinicalObservation.test_code.asc())
        )
        res_obs = await session.execute(stmt_obs)
        obs_list = res_obs.scalars().all()
        assert len(obs_list) == 3

        assert obs_list[0].test_code == "TEST_A"
        assert obs_list[0].lab_indicator == "ABNORMAL"
        assert obs_list[0].lab_out_of_range is True

        assert obs_list[1].test_code == "TEST_HH"
        assert obs_list[1].lab_indicator == "HIGH HIGH"
        assert obs_list[1].lab_out_of_range is True

        assert obs_list[2].test_code == "TEST_N"
        assert obs_list[2].lab_indicator == "NORMAL"
        assert obs_list[2].lab_out_of_range is False


@pytest.mark.asyncio
async def test_recalculate_range_flags_full_coverage():
    """Verify recalculate_range_flags across modified cohort demographics and references.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    from apps.execution.lab_ranges import recalculate_range_flags

    study_id = "STUDY-RECALC-01"
    test_code = "CALCIUM"

    async with db_manager.get_session_maker()() as session:
        # Create subject and initial observation without indicator
        subj = ClinicalSubject(
            subject_id="SUBJ-RECALC",
            study_id=study_id,
            encrypted_demographics=encrypt_demographics(
                {"gender": "M", "birthdate": "1995-03-10"}
            ),
        )
        session.add(subj)

        obs = ClinicalObservation(
            subject_id="SUBJ-RECALC",
            study_id=study_id,
            domain="LB",
            test_code=test_code,
            test_name="Serum Calcium",
            value=12.5,
            unit="mg/dL",
            normalized_value=12.5,
            normalized_unit="mg/dL",
            lab_source="CENTRAL",
        )
        session.add(obs)

        ref_range = LabReferenceRange(
            study_id=study_id,
            test_code=test_code,
            test_name="Serum Calcium",
            source="CENTRAL",
            unit="mg/dL",
            normalized_unit="mg/dL",
            sex_applicability="ALL",
            low_bound=8.5,
            high_bound=10.5,
            critical_low=6.0,
            critical_high=14.0,
        )
        session.add(ref_range)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        updated_count = await recalculate_range_flags(
            session=session,
            study_id=study_id,
            test_code=test_code,
        )
        assert updated_count == 1

        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-RECALC"
        )
        res_obs = await session.execute(stmt_obs)
        obs_updated = res_obs.scalars().first()
        assert obs_updated.lab_indicator == "HIGH"
        assert obs_updated.lab_out_of_range is True

        # Test calling with empty observations
        empty_count = await recalculate_range_flags(
            session=session,
            study_id="STUDY-NON-EXISTENT",
            test_code="UNKNOWN",
        )
        assert empty_count == 0


@pytest.mark.asyncio
async def test_consent_version_stamping_on_ingestion():
    """Verify active SubjectConsent version is stamped onto ingested ClinicalObservation.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    from apps.execution.database.models import SubjectConsent

    study_id = "STUDY-CONSENT-01"
    async with db_manager.get_session_maker()() as session:
        session.add(
            ClinicalSubject(
                subject_id="SUBJ-CONSENT-01",
                study_id=study_id,
            )
        )
        session.add(
            SubjectConsent(
                subject_id="SUBJ-CONSENT-01",
                study_id=study_id,
                version_tag="v2.1",
                version_index=3,
                icf_signed=True,
            )
        )
        await session.commit()

    csv_data = """Subject ID,Test Code,Value,Unit
SUBJ-CONSENT-01,WBC,6.0,10^9/L
"""
    async with db_manager.get_session_maker()() as session:
        result = await LabIngestionService.ingest_batch(
            session=session,
            payload=csv_data,
            format="csv",
            study_id=study_id,
        )
        assert result.status == "COMPLETED"

        stmt_obs = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == "SUBJ-CONSENT-01"
        )
        res_obs = await session.execute(stmt_obs)
        obs = res_obs.scalars().first()
        assert obs.protocol_version_tag == "v2.1"
        assert obs.protocol_version_index == 3


@pytest.mark.asyncio
async def test_unsupported_format_and_empty_payload():
    """Verify handling of unsupported formats and empty payloads.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    async with db_manager.get_session_maker()() as session:
        # Unsupported format
        res_bad = await LabIngestionService.ingest_batch(
            session=session,
            payload="some data",
            format="unsupported_format_xyz",
        )
        assert res_bad.status == "FAILED"
        assert len(res_bad.errors) == 1

        # Empty payload
        res_empty = await LabIngestionService.ingest_batch(
            session=session,
            payload="",
            format="csv",
        )
        assert res_empty.status == "COMPLETED"
        assert res_empty.ingested_count == 0


@pytest.mark.asyncio
async def test_lab_ranges_specificity_and_tie_breaking():
    """Verify multi-dimensional specificity scoring and tie-breaking in select_reference_range.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    from apps.execution.lab_ranges import _get_val, select_reference_range

    # Test _get_val with synonym dictionary keys
    d = {"range_low": 3.0, "range_high": 12.0, "source": "LOCAL", "sex": "M"}
    assert _get_val(d, "low_bound") == 3.0
    assert _get_val(d, "high_bound") == 12.0
    assert _get_val(d, "lab_source") == "LOCAL"
    assert _get_val(d, "sex_applicability") == "M"

    study = "STUDY-SPEC-01"
    tcode = "WBC"
    unit = "10^9/L"

    ranges = [
        {
            "id": "central_generic",
            "study_id": study,
            "test_code": tcode,
            "normalized_unit": unit,
            "source": "CENTRAL",
            "site_id": None,
            "sex_applicability": "ALL",
            "age_low": None,
            "age_high": None,
            "low_bound": 4.0,
            "high_bound": 11.0,
        },
        {
            "id": "local_exact_site",
            "study_id": study,
            "test_code": tcode,
            "normalized_unit": unit,
            "source": "LOCAL",
            "site_id": "SITE-A",
            "sex_applicability": "M",
            "age_low": 18.0,
            "age_high": 65.0,
            "low_bound": 4.5,
            "high_bound": 10.5,
        },
        {
            "id": "local_generic_site",
            "study_id": study,
            "test_code": tcode,
            "normalized_unit": unit,
            "source": "LOCAL",
            "site_id": None,
            "sex_applicability": "ALL",
            "age_low": 18.0,
            "age_high": None,
            "low_bound": 4.2,
            "high_bound": 10.8,
        },
    ]

    # Exact local site match wins
    matched_exact = select_reference_range(
        ranges=ranges,
        study_id=study,
        test_code=tcode,
        normalized_unit=unit,
        lab_source="LOCAL",
        sex="M",
        age=30.0,
        site_id="SITE-A",
    )
    assert matched_exact is not None
    assert matched_exact["id"] == "local_exact_site"

    # Generic local site match for other site
    matched_generic = select_reference_range(
        ranges=ranges,
        study_id=study,
        test_code=tcode,
        normalized_unit=unit,
        lab_source="LOCAL",
        sex="F",
        age=30.0,
        site_id="SITE-OTHER",
    )
    assert matched_generic is not None
    assert matched_generic["id"] == "local_generic_site"

    # Central source match
    matched_central = select_reference_range(
        ranges=ranges,
        study_id=study,
        test_code=tcode,
        normalized_unit=unit,
        lab_source="CENTRAL",
        sex="U",
        age=None,
    )
    assert matched_central is not None
    assert matched_central["id"] == "central_generic"

    # Incompatible range returns None
    assert (
        select_reference_range(
            ranges=ranges,
            study_id="OTHER-STUDY",
            test_code=tcode,
            normalized_unit=unit,
            lab_source="CENTRAL",
            sex="M",
            age=30.0,
        )
        is None
    )


@pytest.mark.asyncio
async def test_batch_store_filtering_by_study():
    """Verify listing batches with study_id filter.

    @req:PRD-LAB-001
    @req:PRD-MDR-001
    @req:PRD-QRY-001
    @req:Trace-1
    @req:Trace-15
    """
    study_a = "STUDY-FILTER-A"
    study_b = "STUDY-FILTER-B"

    async with db_manager.get_session_maker()() as session:
        await LabIngestionService.ingest_batch(
            session=session,
            payload="subject_id,test_code,value\nS1,T1,10\n",
            format="csv",
            study_id=study_a,
        )
        await LabIngestionService.ingest_batch(
            session=session,
            payload="subject_id,test_code,value\nS2,T2,20\n",
            format="csv",
            study_id=study_b,
        )

    all_batches = LabIngestionService.list_batch_statuses()
    assert len(all_batches) >= 2

    study_a_batches = LabIngestionService.list_batch_statuses(study_id=study_a)
    assert all(b.study_id == study_a for b in study_a_batches)
    assert len(study_a_batches) >= 1
