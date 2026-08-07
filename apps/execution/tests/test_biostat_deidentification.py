"""
Comprehensive test suite verifying privacy guarantees, determinism, and authorization boundaries
for SDTM/ADaM structured clinical exports de-identification and pseudonymization.
"""

import hashlib
import hmac
import json
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.biostat.deid import (
    deidentify_export_data,
    scrub_error_message,
    shift_partial_date,
)
from apps.execution.biostat.serializer import serialize_to_dataset_json
from apps.execution.biostat.validator import validate_dataset_json
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    BiostatExport,
    ClinicalObservation,
    ClinicalSubject,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_dm", roles="Data Manager", change_reason="system_operation"
):
    """Helper to generate gateway-compliant signed headers."""
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


@pytest_asyncio.fixture
async def populate_subject_data():
    """Populates valid and invalid subjects with observations for exports."""
    async with db_manager.get_session_maker()() as session:
        # Subject 1 (Valid)
        demo_enc = encrypt_demographics(
            {
                "birthdate": "1980-10-10",
                "gender": "female",
                "race": "white",
                "arm": "Active Arm",
            }
        )
        subj = ClinicalSubject(
            subject_id="SUBJ-A",
            study_id="STUDY-001",
            site_id="SITE-X",
            encrypted_demographics=demo_enc,
        )
        session.add(subj)

        # Exposure Start
        from datetime import datetime

        ex = ClinicalObservation(
            subject_id="SUBJ-A",
            study_id="STUDY-001",
            domain="EX",
            test_code="EXSTDTC",
            test_name="Exposure Start Date",
            value_string="2026-01-01",
            observation_date=datetime.fromisoformat("2026-01-01"),
        )
        session.add(ex)

        # AE records
        ae_term = ClinicalObservation(
            subject_id="SUBJ-A",
            study_id="STUDY-001",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Headache",
            page_id="ae_page_1",
            observation_date=datetime.fromisoformat("2026-01-05"),
        )
        session.add(ae_term)

        ae_stdtc = ClinicalObservation(
            subject_id="SUBJ-A",
            study_id="STUDY-001",
            domain="AE",
            test_code="AESTDTC",
            test_name="Adverse Event Onset",
            value_string="2026-01-05",
            page_id="ae_page_1",
            observation_date=datetime.fromisoformat("2026-01-05"),
        )
        session.add(ae_stdtc)

        # Supplemental ongoing observation to trigger SUPPAE record
        ae_supp = ClinicalObservation(
            subject_id="SUBJ-A",
            study_id="STUDY-001",
            domain="AE",
            test_code="AEENGRY",
            test_name="Ongoing Status",
            value_string="ONGOING",
            page_id="ae_page_1",
            observation_date=datetime.fromisoformat("2026-01-05"),
        )
        session.add(ae_supp)

        # VS observation
        vs_obs = ClinicalObservation(
            subject_id="SUBJ-A",
            study_id="STUDY-001",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=120.0,
            unit="mmHg",
            normalized_value=120.0,
            normalized_unit="mmHg",
            page_id="vs_page_1",
            observation_date=datetime.fromisoformat("2026-01-07"),
        )
        session.add(vs_obs)

        # Subject with invalid details to force a validation failure
        subj_invalid = ClinicalSubject(
            subject_id="SUBJ-BAD",
            study_id="  ",  # Blank triggers validation error
            site_id="SITE-X",
            encrypted_demographics=encrypt_demographics(
                {
                    "birthdate": "1990-01-01",
                    "gender": "male",
                    "race": "asian",
                }
            ),
        )
        session.add(subj_invalid)

        invalid_ex = ClinicalObservation(
            subject_id="SUBJ-BAD",
            study_id="  ",
            domain="EX",
            test_code="EXSTDTC",
            test_name="Exposure Start Date",
            value_string="2020-05-15",
            observation_date=datetime.fromisoformat("2020-05-15"),
        )
        session.add(invalid_ex)

        await session.commit()


# ==============================================================================
# PURE UNIT TESTS (De-identification Transform Invariants)
# ==============================================================================


def test_pseudonymization_determinism_and_hex_format():
    """Verify HMAC pseudonymization is 64-character hex format and deterministic."""
    salt = "stable-salt-12345"
    id_1 = "STUDY-001-SITE-A-SUBJ-101"
    id_2 = "STUDY-001-SITE-A-SUBJ-101"

    # Determinism check
    from packages.deid.transforms import pseudonymize_value

    p1 = pseudonymize_value(id_1, salt)
    p2 = pseudonymize_value(id_2, salt)

    assert p1 == p2
    assert len(p1) == 64
    assert all(c in "0123456789abcdef" for c in p1)


def test_source_records_are_not_mutated():
    """Verify that transformation is strictly non-mutating on source inputs."""
    salt = "secret-test-salt"
    source_records = [
        {
            "STUDYID": "S001",
            "USUBJID": "SUBJ-X",
            "SITEID": "SITE-A",
            "RFSTDTC": "2026-08-15",
        }
    ]
    source_copy = json.loads(json.dumps(source_records))

    # Apply transform
    transformed = deidentify_export_data(source_records, salt)

    # Assert source is completely untouched
    assert source_records == source_copy
    assert transformed != source_records
    assert transformed[0]["USUBJID"] != source_records[0]["USUBJID"]


def test_identical_pseudonymization_across_datasets_and_supp():
    """Verify the same identifier has identical transformed value across different domains/datasets."""
    salt = "secret-test-salt"
    subject_id = "SUBJ-A"

    records = {
        "DM": [{"STUDYID": "S001", "USUBJID": subject_id, "SUBJID": subject_id}],
        "AE": [{"STUDYID": "S001", "USUBJID": subject_id, "AESTDTC": "2026-08-15"}],
        "SUPPAE": [
            {
                "STUDYID": "S001",
                "USUBJID": subject_id,
                "RDOMAIN": "AE",
                "IDVAR": "AESEQ",
                "IDVARVAL": "1",
            }
        ],
        "ADSL": [{"STUDYID": "S001", "USUBJID": subject_id, "ARM": "Active"}],
    }

    transformed = deidentify_export_data(records, salt)

    dm_usubjid = transformed["DM"][0]["USUBJID"]
    ae_usubjid = transformed["AE"][0]["USUBJID"]
    supp_usubjid = transformed["SUPPAE"][0]["USUBJID"]
    adsl_usubjid = transformed["ADSL"][0]["USUBJID"]

    assert dm_usubjid == ae_usubjid == supp_usubjid == adsl_usubjid
    assert dm_usubjid != subject_id
    assert len(dm_usubjid) == 64


def test_date_shift_stable_and_interval_preserving():
    """Verify subject-level date shifting is stable, preserves intervals and partial date placeholders."""
    salt = "stable-salt"
    # Subject A
    r1 = {"USUBJID": "SUBJ-A", "AESTDTC": "2026-08-15", "AEENDTC": "2026-08-25"}
    # Subject B
    r2 = {"USUBJID": "SUBJ-B", "AESTDTC": "2026-08-15", "AEENDTC": "2026-08-25"}

    # Apply de-identification
    deid1 = deidentify_export_data([r1], salt)[0]
    deid2 = deidentify_export_data([r2], salt)[0]

    # Interval preservation check (2026-08-25 - 2026-08-15 = 10 days)
    # The gap between shifted dates must remain exactly 10 days
    from dateutil import parser as date_parser

    dt_start_1 = date_parser.parse(deid1["AESTDTC"])
    dt_end_1 = date_parser.parse(deid1["AEENDTC"])
    assert (dt_end_1 - dt_start_1).days == 10

    dt_start_2 = date_parser.parse(deid2["AESTDTC"])
    dt_end_2 = date_parser.parse(deid2["AEENDTC"])
    assert (dt_end_2 - dt_start_2).days == 10

    # Stable offset check: different subjects get different offsets,
    # so shifted dates shouldn't match if subject offsets differ.
    assert deid1["AESTDTC"] != deid2["AESTDTC"]


def test_partial_dates_shifted_without_fabricating_precision():
    """Verify that YYYY-MM-UN, YYYY-UN-UN, YYYY-MM, YYYY partial dates are shifted without fabricating precision."""
    # YYYY-MM-UN
    assert shift_partial_date("2026-08-UN", -10) == "2026-08-UN"
    assert shift_partial_date("2026-08-UN", 365) == "2027-08-UN"

    # YYYY-UN-UN
    assert shift_partial_date("2026-UN-UN", -30) == "2026-UN-UN"
    assert shift_partial_date("2026-UN-UN", 365) == "2027-UN-UN"

    # YYYY-MM
    assert shift_partial_date("2026-08", 365) == "2027-08"

    # YYYY
    assert shift_partial_date("2026", 365) == "2027"


def test_age_capping_thresholds():
    """Verify age values > 89 are generalized to 89, while <= 89 values are untouched."""
    salt = "salt"
    records = [
        {"USUBJID": "S1", "AGE": 95},
        {"USUBJID": "S2", "AGE": "92"},
        {"USUBJID": "S3", "AGE": 89},
        {"USUBJID": "S4", "AGE": 45},
        {"USUBJID": "S5", "AGE": "92.5"},
        {"USUBJID": "S6", "AGE": "95 years"},
        {"USUBJID": "S7", "AGE": "34 yrs"},
        {"USUBJID": "S8", "AGE": 92.5},
        {"USUBJID": "S9", "AGE": None},
        {"USUBJID": "S10", "AGE": True},
    ]

    transformed = deidentify_export_data(records, salt)

    assert transformed[0]["AGE"] == 89
    assert isinstance(transformed[0]["AGE"], int)

    assert transformed[1]["AGE"] == "89"
    assert isinstance(transformed[1]["AGE"], str)

    assert transformed[2]["AGE"] == 89
    assert transformed[3]["AGE"] == 45

    assert transformed[4]["AGE"] == "89"
    assert isinstance(transformed[4]["AGE"], str)

    assert transformed[5]["AGE"] == "89"
    assert isinstance(transformed[5]["AGE"], str)

    assert transformed[6]["AGE"] == "34 yrs"
    assert isinstance(transformed[6]["AGE"], str)

    assert transformed[7]["AGE"] == 89.0
    assert isinstance(transformed[7]["AGE"], float)

    assert transformed[8]["AGE"] is None
    assert transformed[9]["AGE"] is True


def test_scrub_error_message_direct():
    """Verify that scrub_error_message successfully redacts raw identifiers and quoted values."""
    msg = "Referential inconsistency: Subject 'SUBJ-BAD' not found in DM on SITE-X of STUDY-001."
    scrubbed = scrub_error_message(msg)

    assert "SUBJ-BAD" not in scrubbed
    assert "SITE-X" not in scrubbed
    assert "STUDY-001" not in scrubbed
    assert "[REDACTED_SUBJECT]" in scrubbed or "[REDACTED_VALUE]" in scrubbed
    assert "[REDACTED_SITE]" in scrubbed
    assert "[REDACTED_STUDY]" in scrubbed


def test_dataset_json_validation_passes_after_transform():
    """Verify transformed records successfully pass Dataset-JSON validations."""
    salt = "trial-salt"
    records = {
        "DM": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "DM",
                "USUBJID": "SUBJ-001",
                "SUBJID": "SUBJ-001",
                "SEX": "F",
                "RACE": "WHITE",
                "ARM": "Active",
            }
        ],
        "AE": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "AE",
                "USUBJID": "SUBJ-001",
                "AESEQ": 1,
                "AETERM": "Adverse Event",
                "AESER": "N",
            }
        ],
        "SUPPAE": [
            {
                "STUDYID": "STUDY-001",
                "RDOMAIN": "AE",
                "USUBJID": "SUBJ-001",
                "IDVAR": "AESEQ",
                "IDVARVAL": "1",
                "QNAM": "SUPP1",
                "QLABEL": "Supp Label",
                "QVAL": "Value",
            }
        ],
    }

    # Apply deid
    deid_records = deidentify_export_data(records, salt)

    # Validate referential consistency holds with pseudonymized values
    dataset_json = serialize_to_dataset_json(data=deid_records, study_id="STUDY-001")

    # Should not raise any validation exception
    validate_dataset_json(dataset_json)


# ==============================================================================
# INTEGRATION TESTS (Authorization, DB Logs, and Error Redaction)
# ==============================================================================


@pytest.mark.asyncio
async def test_authorization_disallowed_role_receives_403(
    populate_subject_data,
) -> None:
    """Verify that disallowed roles (e.g., Investigator/Auditor) receive HTTP 403 on exports."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Investigator role is not authorized for exports
        headers = get_auth_headers(roles="Site Investigator")
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001", headers=headers
        )
        assert res.status_code == 403

        # Auditor role is not authorized for exports
        headers_auditor = get_auth_headers(roles="Auditor")
        res_auditor = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001",
            headers=headers_auditor,
        )
        assert res_auditor.status_code == 403


@pytest.mark.asyncio
async def test_authorization_allowed_role_succeeds(populate_subject_data) -> None:
    """Verify that authorized roles (e.g., Data Manager / statistician) receive HTTP 200 on exports."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001", headers=headers
        )
        assert res.status_code == 200


@pytest.mark.asyncio
async def test_error_redaction_and_scrubbing_on_failed_export(
    populate_subject_data,
) -> None:
    """Verify that raw subject identifiers and field values are scrubbed and redacted from saved export errors."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        # Trigger validation failure by passing blank study_id (resolved from SUBJ-BAD in database)
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=  ", headers=headers
        )
        assert res.status_code == 422

        # Verify the exception detail response does not leak raw subject identifier "SUBJ-BAD"
        res_detail = res.json()["detail"]
        assert "SUBJ-BAD" not in res_detail

        # Check the persisted row in BiostatExport DB table
        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(BiostatExport.status == "FAILED")
            res_db = await session.execute(stmt)
            export_row = res_db.scalars().first()

            assert export_row is not None
            assert export_row.error_message is not None
            # Check absolutely no raw subject identifiers exist in the error log
            assert "SUBJ-BAD" not in export_row.error_message
            assert "SITE-X" not in export_row.error_message
