"""
Regression test suite for biostatistical data standards export pipeline (SDTM and ADaM).
Provides comprehensive test coverage for SDTM/ADaM conversions, imputations, sequence generation,
and Dataset-JSON exports with strict traceability annotations.
"""

import hashlib
import hmac
import json
import os
import time
from datetime import datetime, date

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    BiostatExport,
    ClinicalObservation,
    ClinicalSubject,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

from apps.execution.biostat.dates import impute_partial_date, parse_partial_date, to_sas_date, to_date_obj
from apps.execution.biostat.extractors import extract_dm, extract_ae, extract_vs, extract_lb, extract_mh
from apps.execution.biostat.adae import derive_adae
from apps.execution.biostat.adsl import derive_adsl
from apps.execution.biostat.advs import derive_advs
from apps.execution.biostat.models import SUPPRecord


GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_dm", roles="Data Manager", change_reason="system_operation"
):
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
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest_asyncio.fixture
async def populate_test_data():
    """Populates mock subject and observations into the test database."""
    async with db_manager.get_session_maker()() as session:
        # Create a valid clinical subject
        demo_enc = encrypt_demographics(
            {
                "birthdate": "1990-05-15",
                "gender": "male",
                "race": "white",
                "arm": "Active Arm",
            }
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            site_id="SITE-A",
            encrypted_demographics=demo_enc,
        )
        session.add(subj)

        # Create EX observation (Exposure Start) to calculate age & TRTSDT
        ex_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="EX",
            test_code="EXSTDTC",
            test_name="Exposure Start Date",
            value_string="2020-05-15",
            observation_date=datetime.fromisoformat("2020-05-15"),
        )
        session.add(ex_obs)

        # Create AE term observation
        ae_term = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Headache",
            page_id="ae_page_1",
            observation_date=datetime.fromisoformat("2026-08-01"),
        )
        session.add(ae_term)

        # Create AE onset date
        ae_stdtc = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            test_code="AESTDTC",
            test_name="Adverse Event Onset",
            value_string="2026-08-01",
            page_id="ae_page_1",
            observation_date=datetime.fromisoformat("2026-08-01"),
        )
        session.add(ae_stdtc)

        # Create VS observation
        vs_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
            unit="mmHg",
            normalized_value=120.0,
            normalized_unit="mmHg",
            page_id="vs_page_1",
            observation_date=datetime.fromisoformat("2026-08-05"),
        )
        session.add(vs_obs)

        # Add an invalid observation to support testing validation failures
        subj_invalid = ClinicalSubject(
            subject_id="SUBJ-INVALID",
            study_id="  ",  # Blank/whitespace study ID
            site_id="SITE-A",
            encrypted_demographics=encrypt_demographics(
                {
                    "birthdate": "1990-05-15",
                    "gender": "male",
                    "race": "white",
                }
            ),
        )
        session.add(subj_invalid)

        # An observation with blank study_id to fail STUDYID validation
        invalid_ex = ClinicalObservation(
            subject_id="SUBJ-INVALID",
            study_id="  ",  # Blank/whitespace study ID
            domain="EX",
            test_code="EXSTDTC",
            test_name="Exposure Start Date",
            value_string="2020-05-15",
            observation_date=datetime.fromisoformat("2020-05-15"),
        )
        session.add(invalid_ex)

        await session.commit()


# ==============================================================================
# PURE UNIT TESTS (Compliance Derivations)
# ==============================================================================

def test_sdtm_age_derivation():
    """
    @req: SDTM-DM-AGE-01
    Verify SDTM AGE calculation under standard conditions, boundary values, leap years, and missing/invalid dates.
    """
    # Case 1: Standard computation
    # RFSTDTC = 2020-05-15, BRTHDTC = 1990-05-15 -> Exact age is 30 years
    subj = [
        {
            "subject_id": "S1",
            "study_id": "STUDY-X",
            "site_id": "SITE-A",
            "demographics": {
                "birthdate": "1990-05-15",
                "gender": "male",
                "race": "white",
            }
        }
    ]
    obs = [
        {
            "subject_id": "S1",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2020-05-15"
        }
    ]
    dm = extract_dm(subj, obs)
    assert dm[0]["AGE"] == 30

    # Case 2: One day before birthday (not 30 yet) -> should floor to 29
    obs_before = [
        {
            "subject_id": "S1",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2020-05-14"
        }
    ]
    dm_before = extract_dm(subj, obs_before)
    assert dm_before[0]["AGE"] == 29

    # Case 3: Leap Year boundaries (born Feb 29, 2000, reference is Feb 28, 2021) -> 20 years
    subj_leap = [
        {
            "subject_id": "S2",
            "study_id": "STUDY-X",
            "demographics": {
                "birthdate": "2000-02-29",
                "gender": "female",
                "race": "white",
            }
        }
    ]
    obs_leap = [
        {
            "subject_id": "S2",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2021-02-28"
        }
    ]
    dm_leap = extract_dm(subj_leap, obs_leap)
    assert dm_leap[0]["AGE"] == 20

    # Case 4: Missing or partial birthdate with insufficient precision -> None
    subj_partial = [
        {
            "subject_id": "S3",
            "study_id": "STUDY-X",
            "demographics": {
                "birthdate": "1990-UN-UN",
                "gender": "male",
                "race": "white",
            }
        }
    ]
    dm_partial = extract_dm(subj_partial, obs)
    assert dm_partial[0]["AGE"] is None


def test_sdtm_sequence_assignment():
    """
    @req: SDTM-SEQ-01
    Verify monotonic increasing sequence index per subject, ordered chronologically.
    """
    subjects = [{"subject_id": "SUBJ-X", "study_id": "STUDY-Y", "site_id": "SITE-A"}]
    observations = [
        # Visit 3: Day 10 (chronologically 3rd, but out of order in the list)
        {
            "subject_id": "SUBJ-X",
            "domain": "VS",
            "page_id": "vs_page_3",
            "test_code": "SYSBP",
            "value": 110.0,
            "observation_date": "2026-08-10T08:00:00",
        },
        # Visit 1: Day 1 (chronologically 1st)
        {
            "subject_id": "SUBJ-X",
            "domain": "VS",
            "page_id": "vs_page_1",
            "test_code": "SYSBP",
            "value": 120.0,
            "observation_date": "2026-08-01T08:00:00",
        },
        # Visit 2: Day 5 (chronologically 2nd)
        {
            "subject_id": "SUBJ-X",
            "domain": "VS",
            "page_id": "vs_page_2",
            "test_code": "SYSBP",
            "value": 115.0,
            "observation_date": "2026-08-05T08:00:00",
        },
    ]

    vs, _ = extract_vs(subjects, observations)
    assert len(vs) == 3
    # Check chronological ordering and sequence assignment
    # 2026-08-01 -> seq 1
    assert vs[0]["VSSEQ"] == 1
    assert vs[0]["VSORRES"] == 120.0

    # 2026-08-05 -> seq 2
    assert vs[1]["VSSEQ"] == 2
    assert vs[1]["VSORRES"] == 115.0

    # 2026-08-10 -> seq 3
    assert vs[2]["VSSEQ"] == 3
    assert vs[2]["VSORRES"] == 110.0


def test_sdtm_supplemental_qualifiers():
    """
    @req: SDTM-SUPP-01
    Verify mapping and structure of supplemental qualifiers (SUPP-- domains) for non-standard fields.
    """
    # Create SUPPRecord directly and test its fields and row output
    supp = SUPPRecord(
        STUDYID="STUDY-123",
        RDOMAIN="AE",
        USUBJID="STUDY-123-SITE-A-001",
        IDVAR="AESEQ",
        IDVARVAL="1",
        QNAM="AETREAT",
        QLABEL="Treatment for AE",
        QVAL="Ibuprofen",
    )
    assert supp.QEVAL == ""

    # Verify the converted row contains all fields in specified order
    ordered_vars = ["STUDYID", "RDOMAIN", "USUBJID", "IDVAR", "IDVARVAL", "QNAM", "QLABEL", "QVAL", "QEVAL"]
    row = supp.to_row(ordered_vars)
    assert row == ["STUDY-123", "AE", "STUDY-123-SITE-A-001", "AESEQ", "1", "AETREAT", "Treatment for AE", "Ibuprofen", ""]


def test_partial_date_imputation_detailed():
    """
    @req: SDTM-IMPUTE-01
    Verify partial date imputation rules across directions (START, END) including boundaries, leap years, and leap days.
    """
    # Case 1: START direction - Year and Month known, Day missing
    assert impute_partial_date("2026-08-UN", direction="START") == "2026-08-01"
    # Case 2: START direction - Year and Month match Treatment Start Month -> Imputed to TRTSDT
    assert impute_partial_date("2026-08-UN", direction="START", treatment_start_date="2026-08-15") == "2026-08-15"
    # Case 3: START direction - Year only known
    assert impute_partial_date("2026-UN-UN", direction="START") == "2026-01-01"
    # Case 4: START direction - Year matches Treatment Start Year -> Imputed to TRTSDT
    assert impute_partial_date("2026-UN-UN", direction="START", treatment_start_date="2026-08-15") == "2026-08-15"

    # Case 5: END direction - Year and Month known (non-leap year Feb) -> Feb 28
    assert impute_partial_date("2026-02-UN", direction="END") == "2026-02-28"
    # Case 6: END direction - Year and Month known (leap year Feb) -> Feb 29
    assert impute_partial_date("2024-02-UN", direction="END") == "2024-02-29"
    # Case 7: END direction - Year only known -> Dec 31
    assert impute_partial_date("2026-UN-UN", direction="END") == "2026-12-31"
    # Case 8: END direction - Capped by End of Study Date
    assert impute_partial_date("2026-UN-UN", direction="END", end_of_study_date="2026-06-15") == "2026-06-15"


def test_adae_trtemfl_logic():
    """
    @req: ADAM-ADAE-TRTEMFL-01
    Verify Treatment-Emergent AE Flag (TRTEMFL) under various safety window rules and missing date thresholds.
    """
    trtsdt = to_sas_date("2026-08-10")
    trtedt = to_sas_date("2026-08-20")

    adsl = [{"USUBJID": "SUBJ-01", "TRTSDT": trtsdt, "TRTEDT": trtedt}]

    # Case 1: AE starts before treatment -> N
    ae_pre = [{"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-09"}]
    assert derive_adae(adsl, ae_pre)[0]["TRTEMFL"] == "N"

    # Case 2: AE starts exactly on treatment start -> Y
    ae_onset = [{"USUBJID": "SUBJ-01", "AESTDTC": "2026-08-10"}]
    assert derive_adae(adsl, ae_onset)[0]["TRTEMFL"] == "Y"

    # Case 3: AE starts inside 30-day post-treatment safety window (TRTEDT + 30 days = 2026-09-19) -> Y
    ae_window = [{"USUBJID": "SUBJ-01", "AESTDTC": "2026-09-19"}]
    assert derive_adae(adsl, ae_window)[0]["TRTEMFL"] == "Y"

    # Case 4: AE starts outside safety window (TRTEDT + 31 days) -> N
    ae_out = [{"USUBJID": "SUBJ-01", "AESTDTC": "2026-09-20"}]
    assert derive_adae(adsl, ae_out)[0]["TRTEMFL"] == "N"

    # Case 5: Missing AE start date -> N
    ae_miss = [{"USUBJID": "SUBJ-01", "AESTDTC": ""}]
    assert derive_adae(adsl, ae_miss)[0]["TRTEMFL"] == "N"

    # Case 6: Ongoing treatment (TRTEDT is None) and AE onset after TRTSDT -> Y
    adsl_ongoing = [{"USUBJID": "SUBJ-01", "TRTSDT": trtsdt, "TRTEDT": None}]
    ae_late = [{"USUBJID": "SUBJ-01", "AESTDTC": "2026-12-01"}]
    assert derive_adae(adsl_ongoing, ae_late)[0]["TRTEMFL"] == "Y"


def test_advs_chg_pchg_computations():
    """
    @req: ADAM-ADVS-CHG-01
    Verify ADVS Baseline comparisons CHG/PCHG calculations, handling of division-by-zero,
    and preservation (non-coercion) of missing numeric values.
    """
    adsl_records = [{"USUBJID": "SUBJ-1", "TRTSDT": 24350}]  # 2026-09-01

    # Case 1: Standard computation
    # Baseline = 100, Week 1 = 110 -> CHG = 10.0, PCHG = 10.0%
    vs_records_std = [
        {
            "USUBJID": "SUBJ-1",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 100.0,
            "VSDTC": "2026-08-31",
            "VISIT": "Screening",
            "VISITNUM": 1.0,
            "VSBLFL": "Y",
            "VSSEQ": 1,
        },
        {
            "USUBJID": "SUBJ-1",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 110.0,
            "VSDTC": "2026-09-02",
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSSEQ": 2,
        }
    ]
    advs_std = derive_advs(adsl_records, vs_records_std)
    rec_post = next(r for r in advs_std if r["VSSEQ"] == 2)
    assert rec_post["BASE"] == 100.0
    assert rec_post["CHG"] == 10.0
    assert rec_post["PCHG"] == 10.0

    # Case 2: Division-by-zero handling
    # Baseline = 0.0, Week 1 = 50.0 -> CHG = 50.0, PCHG = None
    vs_records_zero = [
        {
            "USUBJID": "SUBJ-1",
            "VSTESTCD": "PULSE",
            "VSTEST": "Pulse Rate",
            "VSSTRESN": 0.0,
            "VSDTC": "2026-08-31",
            "VISIT": "Screening",
            "VISITNUM": 1.0,
            "VSBLFL": "Y",
            "VSSEQ": 1,
        },
        {
            "USUBJID": "SUBJ-1",
            "VSTESTCD": "PULSE",
            "VSTEST": "Pulse Rate",
            "VSSTRESN": 50.0,
            "VSDTC": "2026-09-02",
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSSEQ": 2,
        }
    ]
    advs_zero = derive_advs(adsl_records, vs_records_zero)
    rec_post_zero = next(r for r in advs_zero if r["VSSEQ"] == 2)
    assert rec_post_zero["BASE"] == 0.0
    assert rec_post_zero["CHG"] == 50.0
    assert rec_post_zero["PCHG"] is None

    # Case 3: Non-coercion of missing values (AVAL is None) -> Base remains None, CHG/PCHG remains None
    vs_records_missing = [
        {
            "USUBJID": "SUBJ-1",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": None,  # Missing
            "VSDTC": "2026-08-31",
            "VISIT": "Screening",
            "VISITNUM": 1.0,
            "VSBLFL": "Y",
            "VSSEQ": 1,
        },
        {
            "USUBJID": "SUBJ-1",
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSSTRESN": 105.0,
            "VSDTC": "2026-09-02",
            "VISIT": "Week 1",
            "VISITNUM": 2.0,
            "VSSEQ": 2,
        }
    ]
    advs_missing = derive_advs(adsl_records, vs_records_missing)
    rec_post_miss = next(r for r in advs_missing if r["VSSEQ"] == 2)
    assert rec_post_miss["BASE"] is None
    assert rec_post_miss["CHG"] is None
    assert rec_post_miss["PCHG"] is None


# ==============================================================================
# INTEGRATION TESTS (API Endpoints, Authentication and Database Auditing)
# ==============================================================================

@pytest.mark.asyncio
async def test_api_sdtm_export_success(populate_test_data) -> None:
    """
    @req: API-SDTM-EXPORT-01
    Verify authenticated retrieval of SDTM export, compliant Dataset-JSON structure, and transactional DB audit logging.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="sponsor_statistician")
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001",
            headers=headers,
        )
        assert res.status_code == 200
        data = res.json()

        # Schema & structural checks
        assert data["datasetJSONVersion"] == "1.0.0"
        assert "clinicalData" in data
        assert "IG.DM" in data["clinicalData"]["itemGroupData"]

        group = data["clinicalData"]["itemGroupData"]["IG.DM"]
        assert group["records"] == 1
        assert len(group["itemData"]) == 1

        # Check DB audit ledger logging
        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(BiostatExport.export_type == "SDTM")
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert export_log.status == "SUCCESS"
            assert export_log.dataset_name == "DM"


@pytest.mark.asyncio
async def test_api_adam_export_success(populate_test_data) -> None:
    """
    @req: API-ADAM-EXPORT-01
    Verify authenticated ADaM export derivation and Dataset-JSON response.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        res = await client.get(
            "/api/v1/execution/biostat/adam/ADSL?study_id=STUDY-001",
            headers=headers,
        )
        assert res.status_code == 200
        data = res.json()

        assert data["datasetJSONVersion"] == "1.0.0"
        assert "IG.ADSL" in data["clinicalData"]["itemGroupData"]


@pytest.mark.asyncio
async def test_api_unauthenticated_export_rejection() -> None:
    """
    @req: SEC-EXPORT-AUTH-01
    Verify unauthenticated access to biostatistics export endpoints is strictly rejected with HTTP 401.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001"
        )
        assert res.status_code == 401
        assert "Missing gateway authentication headers" in res.json()["detail"]


@pytest.mark.asyncio
async def test_api_validation_failure_logging(populate_test_data) -> None:
    """
    @req: API-EXPORT-VAL-01
    Verify that validation schema violations trigger HTTP 422 and result in saved FAILED records in the audit database.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        # Empty whitespace study_id triggers validation error in the SDTM mapper
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=  ",
            headers=headers,
        )
        assert res.status_code == 422
        assert "Dataset-JSON validation failed" in res.json()["detail"]

        # Ensure database logged this as a failure
        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(
                BiostatExport.export_type == "SDTM",
                BiostatExport.status == "FAILED"
            )
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert "STUDYID is empty or missing" in export_log.error_message
