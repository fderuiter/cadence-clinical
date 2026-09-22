"""Regulatory Biostatistical Export Test Suite.

Validates SAS Transport (XPT v5/v8) binary encoding, CDISC ODM-XML v1.3.2 generation,
CDISC Dataset-JSON 1.0.0 compliance, and HIPAA/GDPR de-identified CSV exports.

Requirements:
- @req:PRD-SYS-001
- @req:PRD-SYS-004
- @req:PRD-CRF-008
- @req:Trace-1
- @req:Trace-7
- @req:Trace-12
"""

import csv
import hashlib
import hmac
import io
import json
import os
import time
import zipfile
from datetime import datetime

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.biostat.csv_export import (
    serialize_bundle_to_csv_zip,
    serialize_to_csv,
)
from apps.execution.biostat.deid import (
    deidentify_export_data,
    deidentify_record,
    scrub_error_message,
)
from apps.execution.biostat.odm_xml import (
    _infer_odm_data_type,
    build_audit_record,
    serialize_to_odm_xml,
    validate_odm_xml_string,
)
from apps.execution.biostat.xpt import (
    _infer_variable_type_and_length,
    _read_xpt_v5,
    _read_xpt_v8,
    double_to_ibm,
    ibm_to_double,
    read_xpt,
    write_xpt,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    BiostatExport,
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
)
from apps.execution.demographics import encrypt_demographics
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


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

        # Create LB observation
        lb_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="LB",
            test_code="GLUC",
            test_name="Glucose",
            value=95.0,
            unit="mg/dL",
            normalized_value=95.0,
            normalized_unit="mg/dL",
            observation_date=datetime.fromisoformat("2026-08-05"),
        )
        session.add(lb_obs)

        # Create MH observation
        mh_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="MH",
            test_code="MHTERM",
            test_name="Medical History Term",
            value_string="Hypertension",
            observation_date=datetime.fromisoformat("2026-08-05"),
        )
        session.add(mh_obs)

        # Create CM observation and visit
        visit = ClinicalVisit(
            id="visit_1",
            study_id="STUDY-001",
            subject_id="SUBJ-101",
            visit_name="Screening Visit",
            visit_date=datetime.fromisoformat("2026-08-01"),
        )
        session.add(visit)

        cm_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="CM",
            test_code="CMTRT",
            test_name="Concomitant Medication",
            value_string="Aspirin",
            visit_id="visit_1",
            observation_date=datetime.fromisoformat("2026-08-01"),
        )
        session.add(cm_obs)

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


# =========================================================================
# 1. SAS Transport XPT v5 and v8 Binary Serializer Tests
# =========================================================================


def test_ibm_360_float_encoding_roundtrip():
    """Validates IBM 360 64-bit floating point binary encoding and decoding.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    test_values = [
        0.0,
        0,
        1.0,
        -1.0,
        120.0,
        -42.5,
        0.0625,
        1000000.0,
        3.141592653589793,
        0.000123456,
        1e-30,
        1e30,
        None,
    ]

    for val in test_values:
        encoded = double_to_ibm(val)
        assert len(encoded) == 8
        decoded = ibm_to_double(encoded)
        if val is None:
            assert decoded is None
        else:
            assert pytest.approx(decoded, rel=1e-5) == val

    # Test edge case decoding with truncated bytes and zero bytes
    assert ibm_to_double(b"") == 0.0
    assert ibm_to_double(b"\x00" * 8) == 0.0
    assert ibm_to_double(b".\x00\x00\x00\x00\x00\x00\x00") is None


def test_infer_variable_type_and_length():
    """Validates type and length inference logic for SAS variables.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    # 1. Default metadata provided
    t, length, label = _infer_variable_type_and_length(
        "AGE", [34, 45], {"type": "integer", "label": "Subject Age"}
    )
    assert t == 1
    assert length == 8
    assert label == "Subject Age"

    t2, l2, lbl2 = _infer_variable_type_and_length(
        "SEX", ["Male", "Female"], {"type": "string", "label": "Sex", "length": 10}
    )
    assert t2 == 2
    assert l2 == 10

    # 2. Inferred from values: boolean
    t3, l3, _ = _infer_variable_type_and_length("FLAG", [True, False])
    assert t3 == 2

    # 3. Inferred numeric from string floats
    t4, l4, _ = _infer_variable_type_and_length("NUMSTR", ["12.5", "100.2"])
    assert t4 == 1

    # 4. Inferred character
    t5, l5, _ = _infer_variable_type_and_length("CHARVAR", ["Alpha", "Beta Long Text"])
    assert t5 == 2
    assert l5 >= 14

    # 5. Empty values fallback
    t6, l6, _ = _infer_variable_type_and_length("EMPTY", [None, None])
    assert t6 == 1


def test_sas_xpt_v5_serialization_and_deserialization():
    """Validates SAS Transport v5 binary format serialization and parsing.

    @req:PRD-SYS-001
    @req:PRD-CRF-008
    @req:Trace-1
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "SUBJ-101",
            "AGE": 34,
            "SEX": "M",
        },
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "SUBJ-102",
            "AGE": 45,
            "SEX": "F",
        },
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "SUBJ-103",
            "AGE": None,
            "SEX": "U",
        },
    ]

    meta_input = [
        {"name": "STUDYID", "type": "string", "label": "Study ID"},
        {"name": "DOMAIN", "type": "string", "label": "Domain"},
        {"name": "USUBJID", "type": "string", "label": "Unique Subject ID"},
        {"name": "AGE", "type": "integer", "label": "Age"},
        {"name": "SEX", "type": "string", "label": "Sex"},
    ]

    xpt_bytes = write_xpt(
        dataset_name="DM", records=records, version="v5", variables_metadata=meta_input
    )
    assert len(xpt_bytes) > 0
    assert len(xpt_bytes) % 80 == 0
    assert b"HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!" in xpt_bytes
    assert b"HEADER RECORD*******NAMESTR HEADER RECORD!!!!!!!" in xpt_bytes
    assert b"HEADER RECORD*******OBS     HEADER RECORD!!!!!!!" in xpt_bytes

    meta, parsed_records = read_xpt(xpt_bytes)
    assert meta["version"] == "v5"
    assert len(parsed_records) == 3
    assert parsed_records[0]["USUBJID"] == "SUBJ-101"
    assert parsed_records[0]["AGE"] == 34.0
    assert parsed_records[0]["SEX"] == "M"
    assert parsed_records[2]["AGE"] is None


def test_sas_xpt_v8_serialization_and_deserialization():
    """Validates SAS Transport v8 binary format serialization with extended variable names.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    records = [
        {
            "EXTENDED_VAR_IDENTIFIER": "LONG_NAME_TEST_1",
            "ANALYSIS_VALUE_STANDARDIZED": 145.8,
            "SUBJECT_UNIQUE_IDENTIFIER": "SUBJ-101",
        },
        {
            "EXTENDED_VAR_IDENTIFIER": "LONG_NAME_TEST_2",
            "ANALYSIS_VALUE_STANDARDIZED": None,
            "SUBJECT_UNIQUE_IDENTIFIER": "SUBJ-102",
        },
    ]

    xpt_v8_bytes = write_xpt(dataset_name="EXTENDED", records=records, version="v8")
    assert len(xpt_v8_bytes) % 80 == 0
    assert b"HEADER RECORD*******LIBV8   HEADER RECORD!!!!!!!" in xpt_v8_bytes
    assert b"HEADER RECORD*******NAMSTRV8HEADER RECORD!!!!!!!" in xpt_v8_bytes
    assert b"HEADER RECORD*******OBSV8   HEADER RECORD!!!!!!!" in xpt_v8_bytes

    meta, parsed = read_xpt(xpt_v8_bytes)
    assert meta["version"] == "v8"
    assert len(parsed) == 2
    assert parsed[0]["EXTENDED_VAR_IDENTIFIER"] == "LONG_NAME_TEST_1"
    assert pytest.approx(parsed[0]["ANALYSIS_VALUE_STANDARDIZED"]) == 145.8
    assert parsed[1]["ANALYSIS_VALUE_STANDARDIZED"] is None


def test_xpt_reader_error_handling():
    """Validates error handling in XPT parser for malformed inputs.

    @req:PRD-SYS-001
    """
    with pytest.raises(ValueError, match="too short"):
        read_xpt(b"short data")

    with pytest.raises(ValueError, match="NAMESTR header record not found"):
        _read_xpt_v5(b"HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!" + b" " * 200)

    with pytest.raises(ValueError, match="NAMSTRV8 header record not found"):
        _read_xpt_v8(b"HEADER RECORD*******LIBV8   HEADER RECORD!!!!!!!" + b" " * 200)


def test_sas_xpt_trailing_and_all_blank_rows_roundtrip():
    """Validates 100% roundtrip fidelity for datasets with trailing blank rows and all-blank rows.

    @req:PRD-SYS-001
    @req:Trace-1
    @req:Trace-17
    """
    # Scenario A: 5 records (3 non-blank + 2 trailing empty strings)
    recs_a = [
        {"VAR1": "HELLO"},
        {"VAR1": "WORLD"},
        {"VAR1": "FOO"},
        {"VAR1": ""},
        {"VAR1": ""},
    ]
    meta_a = [{"name": "VAR1", "type": "string", "length": 8}]
    b_v5_a = write_xpt("TEST", recs_a, version="v5", variables_metadata=meta_a)
    _, parsed_v5_a = read_xpt(b_v5_a)
    assert len(parsed_v5_a) == 5
    assert parsed_v5_a == recs_a

    b_v8_a = write_xpt("TEST", recs_a, version="v8", variables_metadata=meta_a)
    _, parsed_v8_a = read_xpt(b_v8_a)
    assert len(parsed_v8_a) == 5
    assert parsed_v8_a == recs_a

    # Scenario B: 100 records (85 populated + 15 trailing blank strings)
    recs_b = [{"VAR1": f"R{i}"} for i in range(85)] + [{"VAR1": ""} for _ in range(15)]
    meta_b = [{"name": "VAR1", "type": "string", "length": 8}]
    b_v5_b = write_xpt("TEST", recs_b, version="v5", variables_metadata=meta_b)
    _, parsed_v5_b = read_xpt(b_v5_b)
    assert len(parsed_v5_b) == 100
    assert parsed_v5_b == recs_b

    b_v8_b = write_xpt("TEST", recs_b, version="v8", variables_metadata=meta_b)
    _, parsed_v8_b = read_xpt(b_v8_b)
    assert len(parsed_v8_b) == 100
    assert parsed_v8_b == recs_b

    # Scenario C: 2 records (all blank strings)
    recs_c = [{"VAR1": ""}, {"VAR1": ""}]
    meta_c = [{"name": "VAR1", "type": "string", "length": 8}]
    b_v5_c = write_xpt("TEST", recs_c, version="v5", variables_metadata=meta_c)
    _, parsed_v5_c = read_xpt(b_v5_c)
    assert len(parsed_v5_c) == 2
    assert parsed_v5_c == recs_c

    b_v8_c = write_xpt("TEST", recs_c, version="v8", variables_metadata=meta_c)
    _, parsed_v8_c = read_xpt(b_v8_c)
    assert len(parsed_v8_c) == 2
    assert parsed_v8_c == recs_c


def test_xpt_external_dataset_zero_header_count():
    """Validates parsing of legacy/external XPT files containing 0 in the OBS header count field.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    import struct

    # Construct minimal valid v5 XPT with 1 numeric variable and 0 count in OBS header
    rec_headers = (
        b"HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!000000000000000000000000000000  "
        + b"SAS     SAS     SASLIB  6.06    bsd4.3  "
        + b" " * 40
        + b"HEADER RECORD*******MEMBER  HEADER RECORD!!!!!!!000000000000000001600000000000  "
        + b"HEADER RECORD*******DSCRPTR HEADER RECORD!!!!!!!000000000000000000000000000000  "
        + b"SAS     SASDATA TEST    6.06    bsd4.3  "
        + b" " * 40
        + b"HEADER RECORD*******NAMESTR HEADER RECORD!!!!!!!000000000000000001400001000000  "
    )
    namestr_entry = struct.pack(
        ">hhhh8s40s8shhh2s8shhi52s",
        1,
        0,
        8,
        1,
        b"NUMVAR  ",
        b"Number Variable                         ",
        b" " * 8,
        0,
        0,
        0,
        b"\x00\x00",
        b" " * 8,
        0,
        0,
        0,
        b" " * 52,
    )
    namestr_block = namestr_entry + b" " * 20  # Pad to 160 (multiple of 80)
    obs_header_zeros = (
        b"HEADER RECORD*******OBS     HEADER RECORD!!!!!!!" + b"0" * 30 + b"  "
    )
    # 2 rows of 8-byte numeric floats + 64 bytes of card padding (spaces)
    obs_block = obs_header_zeros + double_to_ibm(42.0) + double_to_ibm(99.5) + b" " * 64

    full_xpt = rec_headers + namestr_block + obs_block
    meta, parsed = read_xpt(full_xpt)
    assert len(parsed) == 2
    assert parsed[0]["NUMVAR"] == 42.0
    assert parsed[1]["NUMVAR"] == 99.5


# =========================================================================
# 2. CDISC ODM-XML v1.3.2 Serializer Tests
# =========================================================================


def test_cdisc_odm_xml_serialization_and_audit_trail():
    """Validates CDISC ODM-XML v1.3.2 generation with embedded <AuditRecord> elements.

    @req:PRD-SYS-004
    @req:PRD-CRF-008
    @req:Trace-7
    """
    bundle = {
        "DM": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "SUBJ-101",
                "AGE": 35,
                "SEX": "M",
                "created_by": "crc_user",
                "reason_for_change": "Initial Screening Entry",
            }
        ],
        "VS": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "SUBJ-101",
                "VSTESTCD": "SYSBP",
                "VSORRES": 120.0,
                "created_by": "crc_user",
                "reason_for_change": "Baseline measurement",
            }
        ],
    }

    xml_output = serialize_to_odm_xml(
        study_id="STUDY-001",
        data=bundle,
        audit_user="system_audit",
        change_reason="Export Wizard Regulatory Extraction",
    )

    assert xml_output.startswith("<?xml")
    assert 'ODMVersion="1.3.2"' in xml_output
    assert "<Study OID=" in xml_output
    assert "<ClinicalData" in xml_output
    assert '<SubjectData SubjectKey="SUBJ-101">' in xml_output
    assert "<AuditRecord>" in xml_output
    assert "<UserRef UserOID=" in xml_output
    assert "<ReasonForChange>" in xml_output
    assert "<ItemGroupData ItemGroupOID=" in xml_output
    assert "<ItemData ItemOID=" in xml_output

    # Validate well-formedness
    assert validate_odm_xml_string(xml_output) is True


def test_odm_xml_single_list_and_helpers():
    """Validates ODM-XML helpers, datatype inference, and single list serialization.

    @req:PRD-SYS-004
    @req:Trace-7
    """
    # Test datatype inference
    assert _infer_odm_data_type(None) == "text"
    assert _infer_odm_data_type(True) == "boolean"
    assert _infer_odm_data_type(123) == "integer"
    assert _infer_odm_data_type(45.6) == "float"
    assert _infer_odm_data_type("2026-08-14") == "date"
    assert _infer_odm_data_type("2026-08-14T12:00:00Z") == "datetime"
    assert _infer_odm_data_type("Some text") == "text"

    # Test audit record builder with location and custom timestamp
    dt = datetime(2026, 8, 14, 12, 0, 0)
    audit_el = build_audit_record(
        user_id="user_test",
        reason_for_change="Test reason",
        timestamp=dt,
        location_id="SITE-101",
    )
    assert audit_el.find("{http://www.cdisc.org/ns/odm/v1.3}LocationRef") is not None

    # Test serialize_to_odm_xml with single list
    single_list = [
        {"STUDYID": "STUDY-001", "DOMAIN": "DM", "USUBJID": "SUBJ-101", "AGE": 30}
    ]
    xml_single = serialize_to_odm_xml(study_id="STUDY-001", data=single_list)
    assert validate_odm_xml_string(xml_single) is True

    # Test validation helper edge cases
    assert validate_odm_xml_string("<InvalidXML") is False
    assert validate_odm_xml_string("<Root>Not ODM</Root>") is False
    assert (
        validate_odm_xml_string('<ODM ODMVersion="1.2"><Study OID="1"/></ODM>') is False
    )
    assert (
        validate_odm_xml_string('<ODM ODMVersion="1.3.2"><Study OID="1"/></ODM>')
        is False
    )


# =========================================================================
# 3. De-Identified CSV and ZIP Bundle Tests
# =========================================================================


def test_deidentified_csv_and_zip_export():
    """Validates HIPAA Safe Harbor / GDPR de-identified CSV and ZIP archives.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:Trace-12
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "USUBJID": "SUBJ-101",
            "SITEID": "SITE-A",
            "AGE": 95,
            "RFSTDTC": "2026-05-15",
        },
        {
            "STUDYID": "STUDY-001",
            "USUBJID": "SUBJ-102",
            "SITEID": "SITE-B",
            "AGE": 42,
            "RFSTDTC": "2026-06-20",
        },
    ]

    # 1. Safe Harbor Profile
    csv_str = serialize_to_csv(records, privacy_profile="SAFE_HARBOR", salt="test-salt")
    reader = list(csv.DictReader(io.StringIO(csv_str)))
    assert len(reader) == 2
    assert reader[0]["STUDYID"] == "STUDY-001"
    assert reader[0]["USUBJID"] != "SUBJ-101"
    assert reader[0]["AGE"] == "89"
    assert reader[1]["AGE"] == "42"

    # 2. Limited Data Set Profile
    csv_lds = serialize_to_csv(
        records, privacy_profile="LIMITED_DATA_SET", salt="test-salt"
    )
    reader_lds = list(csv.DictReader(io.StringIO(csv_lds)))
    assert reader_lds[0]["RFSTDTC"] == "2026-05-15"  # Date preserved

    # 3. GDPR Pseudonymized Profile
    csv_gdpr = serialize_to_csv(
        records, privacy_profile="GDPR_PSEUDONYMIZED", salt="test-salt"
    )
    reader_gdpr = list(csv.DictReader(io.StringIO(csv_gdpr)))
    assert reader_gdpr[0]["USUBJID"] != "SUBJ-101"

    # 4. Unrestricted Profile
    csv_raw = serialize_to_csv(records, privacy_profile="UNRESTRICTED")
    reader_raw = list(csv.DictReader(io.StringIO(csv_raw)))
    assert reader_raw[0]["USUBJID"] == "SUBJ-101"
    assert reader_raw[0]["AGE"] == "95"

    # 5. Empty input
    assert serialize_to_csv([]) == ""

    # 6. ZIP bundle with audit fields
    records_with_audit = [
        {
            "STUDYID": "STUDY-001",
            "USUBJID": "SUBJ-101",
            "created_by": "tester",
            "reason_for_change": "audit test",
        }
    ]
    bundle = {"DM": records_with_audit}
    zip_bytes = serialize_bundle_to_csv_zip(
        bundle,
        privacy_profile="SAFE_HARBOR",
        salt="test-salt",
        include_audit_fields=True,
    )
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as zf:
        dm_csv = zf.read("dm.csv").decode("utf-8")
        assert "created_by" in dm_csv


# =========================================================================
# 5. Biostat Domain Modules Comprehensive Unit Tests
# =========================================================================


def test_biostat_dates_helpers():
    """Validates date parsing, imputation, and SAS date transformations.

    @req:PRD-SYS-001
    """
    from apps.execution.biostat.dates import (
        impute_partial_date,
        to_sas_date,
    )

    # to_sas_date
    assert to_sas_date(None) is None
    assert to_sas_date("") is None
    sas_d = to_sas_date("1960-01-01")
    assert sas_d == 0
    sas_d2 = to_sas_date("1960-01-02")
    assert sas_d2 == 1
    sas_d3 = to_sas_date(datetime(1960, 1, 1))
    assert sas_d3 == 0

    # impute_partial_date
    assert impute_partial_date(None) is None
    assert impute_partial_date("") is None
    assert impute_partial_date("2026-08-14") == "2026-08-14"
    assert impute_partial_date("2026-08") == "2026-08-01"
    assert impute_partial_date("2026") == "2026-01-01"
    assert impute_partial_date("2026-08-UN", direction="START") == "2026-08-01"
    assert impute_partial_date("2026-02-UN", direction="END") == "2026-02-28"
    assert impute_partial_date("2024-02-UN", direction="END") == "2024-02-29"


def test_biostat_mappings_metadata():
    """Validates SDTM declarative variable mappings.

    @req:PRD-SYS-001
    """
    from apps.execution.biostat.mappings import SDTM_MAPPINGS

    assert len(SDTM_MAPPINGS) > 0
    dm_mappings = [m for m in SDTM_MAPPINGS if m.domain == "DM"]
    assert len(dm_mappings) > 0
    assert any(m.variable_name == "USUBJID" for m in dm_mappings)


def test_biostat_deid_and_scrubbing_helpers():
    """Validates date shifting, deid record transforms, and PII log scrubbing.

    @req:PRD-SYS-001
    @req:Trace-12
    """
    from apps.execution.biostat.deid import (
        shift_partial_date,
    )

    # shift_partial_date
    assert shift_partial_date("", 10) == ""
    assert shift_partial_date("   ", 10) == "   "
    shifted_full = shift_partial_date("2026-08-14", 10)
    assert shifted_full == "2026-08-24"
    shifted_month = shift_partial_date("2026-08", 30)
    assert "2026" in shifted_month
    shifted_year = shift_partial_date("2026", 365)
    assert "2027" in shifted_year
    assert shift_partial_date("invalid-date", 10) == "[DATE_INVALID]"

    # deidentify_record
    rec = {
        "USUBJID": "SUBJ-101",
        "SUBJID": "101",
        "SITEID": "SITE-A",
        "AGE": 95,
        "RFSTDTC": "2026-08-14",
        "TRTSDT": 22000,
    }
    deid_r = deidentify_record(rec, salt="my-salt")
    assert deid_r["USUBJID"] != "SUBJ-101"
    assert deid_r["AGE"] == 89
    assert deid_r["TRTSDT"] != 22000

    # deidentify_export_data with dict and list and non-collection
    dict_bundle = {"DM": [rec]}
    deid_bundle = deidentify_export_data(dict_bundle, salt="my-salt")
    assert "DM" in deid_bundle
    list_bundle = [rec]
    deid_list = deidentify_export_data(list_bundle, salt="my-salt")
    assert len(deid_list) == 1
    assert deidentify_export_data(None, salt="s") is None

    # scrub_error_message
    raw_err = "Failure for SUBJ-101 at SITE-A in STUDY-001: invalid value 'SECRET_VAL' or \"CONFIDENTIAL\""
    scrubbed = scrub_error_message(raw_err)
    assert "SUBJ-101" not in scrubbed
    assert "SITE-A" not in scrubbed
    assert "STUDY-001" not in scrubbed
    assert "SECRET_VAL" not in scrubbed
    assert "CONFIDENTIAL" not in scrubbed
    assert scrub_error_message("") == ""


def test_biostat_extractors_and_derivations():
    """Validates SDTM extraction functions and ADaM ADAE / ADVS derivations.

    @req:PRD-SYS-001
    @req:PRD-CRF-008
    """
    from apps.execution.biostat.adae import derive_adae
    from apps.execution.biostat.advs import derive_advs
    from apps.execution.biostat.extractors import (
        calculate_age,
        get_demographics,
        get_value,
    )
    from apps.execution.biostat.models import SUPPRecord

    # calculate_age edge cases
    assert calculate_age(None, None) is None
    assert calculate_age("2026-08-14", None) is None
    assert calculate_age("invalid", "invalid") is None
    assert calculate_age("1990-05-15", "2026-08-14") is None  # negative
    age_val = calculate_age("2026-08-14", "1990-05-15")
    assert age_val == 36

    # get_value & get_demographics
    assert get_value({"a": 1}, "a") == 1
    assert get_value(object(), "non_existent", 42) == 42
    assert get_demographics({}) == {}

    # SUPPRecord
    supp = SUPPRecord(
        STUDYID="STUDY-001",
        RDOMAIN="DM",
        USUBJID="SUBJ-101",
        IDVAR="AESEQ",
        IDVARVAL="1",
        QNAM="AELOC",
        QLABEL="Location",
        QVAL="Head",
    )
    row_vals = supp.to_row(["STUDYID", "RDOMAIN", "USUBJID", "QNAM", "QVAL"])
    assert row_vals == ["STUDY-001", "DM", "SUBJ-101", "AELOC", "Head"]

    # ADAE derivation
    adsl_list = [
        {
            "STUDYID": "STUDY-001",
            "USUBJID": "SUBJ-101",
            "TRTSDT": 22000,
            "SITEID": "SITE-A",
        }
    ]
    ae_list = [
        {
            "STUDYID": "STUDY-001",
            "USUBJID": "SUBJ-101",
            "AESEQ": 1,
            "AETERM": "Headache",
            "AESTDTC": "2026-08-01",
            "AEENDTC": "2026-08-05",
            "AESEV": "MILD",
        }
    ]
    adae_recs = derive_adae(adsl_list, ae_list)
    assert len(adae_recs) == 1
    assert adae_recs[0]["AETERM"] == "Headache"

    # ADVS derivation
    vs_list = [
        {
            "STUDYID": "STUDY-001",
            "USUBJID": "SUBJ-101",
            "VSSEQ": 1,
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic BP",
            "VSSTRESN": 120.0,
            "VSDTC": "2026-08-05",
            "VSBLFL": "Y",
        }
    ]
    advs_recs = derive_advs(adsl_list, vs_list)
    assert len(advs_recs) == 1
    assert advs_recs[0]["PARAMCD"] == "SYSBP"


def test_all_domain_extractors_and_supp_records():
    """Validates SDTM extractors for DM, AE, VS, LB, MH and supplemental qualifiers.

    @req:PRD-SYS-001
    @req:PRD-CRF-008
    """
    from apps.execution.biostat.extractors import (
        extract_ae,
        extract_dm,
        extract_lb,
        extract_mh,
        extract_vs,
    )
    from apps.execution.biostat.mappings import (
        get_mappings_by_domain,
        get_mappings_for_domain,
    )

    # Mappings helpers
    dm_maps = get_mappings_for_domain("DM")
    assert len(dm_maps) > 0
    all_maps = get_mappings_by_domain()
    assert "DM" in all_maps
    assert "AE" in all_maps

    subjects = [
        {
            "subject_id": "SUBJ-101",
            "study_id": "STUDY-001",
            "site_id": "SITE-A",
            "demographics": {
                "birthdate": "1990-05-15",
                "gender": "male",
                "race": "white",
            },
        }
    ]

    # DM
    ex_obs = [
        {
            "subject_id": "SUBJ-101",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2026-01-01",
        }
    ]
    dm_recs = extract_dm(subjects, ex_obs)
    assert len(dm_recs) == 1
    assert dm_recs[0]["AGE"] is not None

    # AE with unmapped qualifier
    ae_obs = [
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "p1",
            "test_code": "AETERM",
            "value_string": "Nausea",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "p1",
            "test_code": "AESEV",
            "value_string": "MILD",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "p1",
            "test_code": "AESER",
            "value_string": "N",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "p1",
            "test_code": "AESTDTC",
            "value_string": "2026-02-01",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "AE",
            "page_id": "p1",
            "test_code": "AETREAT",
            "test_name": "Treatment for AE",
            "value_string": "Antacid",
        },
    ]
    ae_recs, supp_ae = extract_ae(subjects, ae_obs)
    assert len(ae_recs) == 1
    assert len(supp_ae) == 2

    # VS
    vs_obs = [
        {
            "subject_id": "SUBJ-101",
            "domain": "VS",
            "page_id": "p_vs_1",
            "test_code": "SYSBP",
            "test_name": "Systolic BP",
            "value": 120.0,
            "unit": "mmHg",
            "observation_date": "2026-03-01T10:00:00",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "VS",
            "page_id": "p_vs_1",
            "test_code": "VSCOMM",
            "test_name": "Comments",
            "value_string": "Normal resting",
            "observation_date": "2026-03-01T10:00:00",
        },
    ]
    vs_recs, supp_vs = extract_vs(subjects, vs_obs)
    assert len(vs_recs) == 2

    # LB
    lb_obs = [
        {
            "subject_id": "SUBJ-101",
            "domain": "LB",
            "page_id": "p_lb_1",
            "test_code": "GLUC",
            "test_name": "Glucose",
            "value": 90.0,
            "unit": "mg/dL",
            "observation_date": "2026-04-01T09:00:00",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "LB",
            "page_id": "p_lb_1",
            "test_code": "LBCOMM",
            "test_name": "Comments",
            "value_string": "Fasting confirmed",
            "observation_date": "2026-04-01T09:00:00",
        },
    ]
    lb_recs, supp_lb = extract_lb(subjects, lb_obs)
    assert len(lb_recs) == 2

    # MH
    mh_obs = [
        {
            "subject_id": "SUBJ-101",
            "domain": "MH",
            "page_id": "p_mh_1",
            "test_code": "MHTERM",
            "test_name": "Medical History Term",
            "value_string": "Asthma",
            "observation_date": "2026-01-01T00:00:00",
        },
        {
            "subject_id": "SUBJ-101",
            "domain": "MH",
            "page_id": "p_mh_1",
            "test_code": "MHCOMM",
            "test_name": "Comments",
            "value_string": "Mild seasonal",
            "observation_date": "2026-01-01T00:00:00",
        },
    ]
    mh_recs, supp_mh = extract_mh(subjects, mh_obs)
    assert len(mh_recs) == 1
    assert len(supp_mh) == 1


def test_biostat_adsl_and_validation_rules():
    """Validates ADSL derivation branches and Dataset-JSON validation checks.

    @req:PRD-SYS-001
    @req:PRD-CRF-008
    """
    from apps.execution.biostat.adsl import derive_adsl
    from apps.execution.biostat.serializer import serialize_to_dataset_json
    from apps.execution.biostat.validator import (
        DatasetJSONValidationError,
        validate_dataset_json,
    )

    # 1. Test ADSL derivation with rich subject demographics
    subjects = [
        {
            "subject_id": "SUBJ-1",
            "study_id": "STUDY-001",
            "site_id": "SITE-A",
            "demographics": {
                "birthdate": "1985-03-20",
                "gender": "Female",
                "race": "Asian",
                "arm": "Arm A",
                "actarm": "Arm A Active",
            },
        },
        {
            "subject_id": "SUBJ-2",
            "study_id": "STUDY-001",
            "site_id": "SITE-B",
            "demographics": {},
        },
    ]
    observations = [
        {
            "subject_id": "SUBJ-1",
            "domain": "EX",
            "test_code": "EXSTDTC",
            "value_string": "2026-01-10",
        },
        {
            "subject_id": "SUBJ-1",
            "domain": "DS",
            "test_code": "DSSTDTC",
            "value_string": "2026-06-15",
        },
    ]

    adsl_list = derive_adsl(subjects, observations)
    assert len(adsl_list) == 2
    assert adsl_list[0]["ARM"] == "Arm A"
    assert adsl_list[0]["ACTARM"] == "Arm A Active"
    assert adsl_list[1]["ARM"] == "SCREEN FAILURE"

    # 2. Test Dataset-JSON serializer & validation passes
    bundle = {
        "DM": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-001-SITE-A-SUBJ-1",
                "SUBJID": "SUBJ-1",
                "SITEID": "SITE-A",
                "AGE": 41,
                "AGEU": "YEARS",
                "SEX": "F",
                "RACE": "ASIAN",
                "ARM": "Arm A",
                "ACTARM": "Arm A Active",
            }
        ]
    }
    ds_json = serialize_to_dataset_json(bundle, study_id="STUDY-001")
    validate_dataset_json(ds_json)

    # 3. Test Dataset-JSON validation failures
    invalid_bundle = {
        "DM": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-001-SITE-A-SUBJ-1",
                "AGE": -5,  # Negative age
                "SEX": "UNKNOWN_VALUE",  # Invalid controlled terminology
                "RFSTDTC": "2026/08/14",  # Non ISO-8601 date
            }
        ]
    }
    invalid_json = serialize_to_dataset_json(invalid_bundle, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError):
        validate_dataset_json(invalid_json)

    # 4. Referential consistency error: ADAE subject not in ADSL
    ref_bundle = {
        "ADAE": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "NON-EXISTENT-SUBJ",
                "AESEQ": 1,
                "AETERM": "Rash",
            }
        ],
        "ADSL": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "STUDY-001-SITE-A-SUBJ-1",
            }
        ],
    }
    ref_json = serialize_to_dataset_json(ref_bundle, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError):
        validate_dataset_json(ref_json)

    # 5. SUPP validation error: RDOMAIN mismatch
    supp_bundle = {
        "SUPPAE": [
            {
                "STUDYID": "STUDY-001",
                "RDOMAIN": "DM",  # Should be AE
                "USUBJID": "STUDY-001-SITE-A-SUBJ-1",
                "IDVAR": "AESEQ",
                "IDVARVAL": "1",
                "QNAM": "AELOC",
                "QLABEL": "Loc",
                "QVAL": "Arm",
            }
        ]
    }
    supp_json = serialize_to_dataset_json(supp_bundle, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError):
        validate_dataset_json(supp_json)


# =========================================================================
# 4. API Endpoints & Verification Tests
# =========================================================================


@pytest.mark.asyncio
async def test_sdtm_domain_export_success(populate_test_data) -> None:
    """Verify successful export of an SDTM domain in Dataset-JSON format.

    @req:PRD-SYS-001
    @req:Trace-1
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

        assert data["datasetJSONVersion"] == "1.0.0"
        assert "clinicalData" in data
        assert "IG.DM" in data["clinicalData"]["itemGroupData"]

        group = data["clinicalData"]["itemGroupData"]["IG.DM"]
        assert group["records"] == 1
        assert len(group["itemData"]) == 1

        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(BiostatExport.export_type == "SDTM")
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert export_log.status == "SUCCESS"
            assert export_log.dataset_name == "DM"


@pytest.mark.asyncio
async def test_sdtm_all_domains_and_formats(populate_test_data) -> None:
    """Verify all SDTM domains (DM, AE, VS, LB, MH, CM) across XPT, ODM, and CSV.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:Trace-1
    @req:Trace-7
    @req:Trace-12
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")

        for dom in ["DM", "AE", "VS", "LB", "MH", "CM"]:
            # XPT
            res_xpt = await client.get(
                f"/api/v1/execution/biostat/sdtm/{dom}?study_id=STUDY-001&format=xpt&version=v8",
                headers=headers,
            )
            assert res_xpt.status_code == 200
            assert res_xpt.headers["content-type"] == "application/x-sas-xport"

            # ODM-XML
            res_odm = await client.get(
                f"/api/v1/execution/biostat/sdtm/{dom}?study_id=STUDY-001&format=odm",
                headers=headers,
            )
            assert res_odm.status_code == 200
            assert "application/xml" in res_odm.headers["content-type"]

            # CSV
            res_csv = await client.get(
                f"/api/v1/execution/biostat/sdtm/{dom}?study_id=STUDY-001&format=csv&privacy_profile=UNRESTRICTED",
                headers=headers,
            )
            assert res_csv.status_code == 200
            assert "text/csv" in res_csv.headers["content-type"]


@pytest.mark.asyncio
async def test_adam_all_datasets_and_formats(populate_test_data) -> None:
    """Verify all ADaM datasets (ADSL, ADAE, ADVS) across Dataset-JSON, XPT, ODM, and CSV.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:Trace-1
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")

        for ds in ["ADSL", "ADAE", "ADVS"]:
            # JSON
            res_json = await client.get(
                f"/api/v1/execution/biostat/adam/{ds}?study_id=STUDY-001",
                headers=headers,
            )
            assert res_json.status_code == 200
            assert res_json.json()["datasetJSONVersion"] == "1.0.0"

            # XPT
            res_xpt = await client.get(
                f"/api/v1/execution/biostat/adam/{ds}?study_id=STUDY-001&format=xpt",
                headers=headers,
            )
            assert res_xpt.status_code == 200
            assert res_xpt.headers["content-type"] == "application/x-sas-xport"

            # ODM
            res_odm = await client.get(
                f"/api/v1/execution/biostat/adam/{ds}?study_id=STUDY-001&format=odm",
                headers=headers,
            )
            assert res_odm.status_code == 200

            # CSV
            res_csv = await client.get(
                f"/api/v1/execution/biostat/adam/{ds}?study_id=STUDY-001&format=csv",
                headers=headers,
            )
            assert res_csv.status_code == 200


@pytest.mark.asyncio
async def test_biostat_bundle_formats(populate_test_data) -> None:
    """Verify biostat bundle export in JSON, ZIP, and ODM formats.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:Trace-1
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="CRA")

        # JSON
        res_json = await client.get(
            "/api/v1/execution/biostat/bundle?study_id=STUDY-001",
            headers=headers,
        )
        assert res_json.status_code == 200
        assert res_json.json()["datasetJSONVersion"] == "1.0.0"

        # ZIP
        res_zip = await client.get(
            "/api/v1/execution/biostat/bundle?study_id=STUDY-001&format=zip",
            headers=headers,
        )
        assert res_zip.status_code == 200
        assert res_zip.headers["content-type"] == "application/zip"

        # ODM
        res_odm = await client.get(
            "/api/v1/execution/biostat/bundle?study_id=STUDY-001&format=odm",
            headers=headers,
        )
        assert res_odm.status_code == 200
        assert "<ODM" in res_odm.text


@pytest.mark.asyncio
async def test_export_wizard_parameterized_scenarios(populate_test_data) -> None:
    """Verify export wizard endpoint with filters, single dataset, zip bundles.

    @req:PRD-SYS-001
    @req:PRD-SYS-004
    @req:PRD-CRF-008
    @req:Trace-1
    @req:Trace-7
    @req:Trace-12
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="sponsor_statistician")

        # 1. Single dataset XPT
        res_single_xpt = await client.post(
            "/api/v1/execution/exports/wizard",
            headers=headers,
            json={
                "study_id": "STUDY-001",
                "format": "xpt_v8",
                "domains": ["DM"],
                "datasets": [],
                "site_ids": ["SITE-A"],
                "cohorts": ["Active Arm"],
            },
        )
        assert res_single_xpt.status_code == 200
        assert "application/x-sas-xport" in res_single_xpt.headers["content-type"]

        # 2. Multi-dataset CSV ZIP
        res_csv_zip = await client.post(
            "/api/v1/execution/exports/wizard",
            headers=headers,
            json={
                "study_id": "STUDY-001",
                "format": "csv_zip",
                "domains": ["DM", "AE"],
                "datasets": ["ADSL"],
                "include_audit_trail": True,
            },
        )
        assert res_csv_zip.status_code == 200
        assert "application/zip" in res_csv_zip.headers["content-type"]

        # 3. Non-matching filter 404
        res_not_found = await client.post(
            "/api/v1/execution/exports/wizard",
            headers=headers,
            json={
                "study_id": "STUDY-001",
                "domains": ["DM"],
                "site_ids": ["NON_EXISTENT_SITE"],
            },
        )
        assert res_not_found.status_code == 404


@pytest.mark.asyncio
async def test_invalid_requests_and_unauthorized() -> None:
    """Verify HTTP 400 for invalid domains/datasets and 401 for unauthenticated calls.

    @req:PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")

        # Invalid SDTM domain
        res_sdtm = await client.get(
            "/api/v1/execution/biostat/sdtm/INVALID?study_id=STUDY-001",
            headers=headers,
        )
        assert res_sdtm.status_code == 400

        # Invalid ADaM dataset
        res_adam = await client.get(
            "/api/v1/execution/biostat/adam/INVALID?study_id=STUDY-001",
            headers=headers,
        )
        assert res_adam.status_code == 400

        # Unauthenticated
        res_unauth = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=STUDY-001",
        )
        assert res_unauth.status_code == 401


@pytest.mark.asyncio
async def test_export_validation_failure_handling(populate_test_data) -> None:
    """Verify that validation failures trigger HTTP 422 and log FAILED status with scrubbed message.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager")
        res = await client.get(
            "/api/v1/execution/biostat/sdtm/DM?study_id=  ",
            headers=headers,
        )
        assert res.status_code == 422
        assert "Dataset-JSON validation failed" in res.json()["detail"]

        async with db_manager.get_session_maker()() as session:
            stmt = select(BiostatExport).where(
                BiostatExport.export_type == "SDTM", BiostatExport.status == "FAILED"
            )
            db_res = await session.execute(stmt)
            export_log = db_res.scalars().first()
            assert export_log is not None
            assert "STUDYID is empty or missing" in export_log.error_message
