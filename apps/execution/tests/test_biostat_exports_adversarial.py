"""Adversarial Verification and Stress Test Suite for Biostat Exports and Serializers.

Thoroughly exercises:
1. SAS Transport (XPT v5 and v8) 80-byte header cards, IBM 360 64-bit float codec,
   NAMESTR structs (140B / 512B), and length limits.
2. CDISC ODM-XML v1.3.2 namespaces, MetaDataVersion, SubjectData hierarchy,
   and 21 CFR Part 11 <AuditRecord> elements.
3. CDISC Dataset-JSON v1.0.0 Pydantic v2 schemas, 2D value matrix, and all 7
   conformance validation error codes.
4. HIPAA Safe Harbor & GDPR deterministic HMAC date shifting in [-365, +365],
   longitudinal parity, pseudonymization, and age capping.

Requirements:
- @req:PRD-SYS-001
- @req:PRD-SYS-004
- @req:PRD-CRF-008
- @req:Trace-1
- @req:Trace-7
- @req:Trace-12
"""

import xml.etree.ElementTree as ET
from datetime import datetime

import pytest

from apps.execution.biostat.deid import (
    deidentify_record,
    scrub_error_message,
)
from apps.execution.biostat.odm_xml import (
    serialize_to_odm_xml,
    validate_odm_xml_string,
)
from apps.execution.biostat.serializer import (
    serialize_dataset_json,
    serialize_to_dataset_json,
)
from apps.execution.biostat.validator import (
    CONTROLLED_TERMINOLOGY_VIOLATION,
    DUPLICATE_SEQUENCE,
    EMPTY_STUDYID_USUBJID,
    MISSING_REQUIRED_VARIABLES,
    NULL_FLAVOR_INCONSISTENCY,
    REFERENTIAL_INCONSISTENCY,
    SUPPLEMENTAL_QUALIFIER_VIOLATION,
    DatasetJSONValidationError,
    validate_dataset_json,
)
from apps.execution.biostat.xpt import (
    double_to_ibm,
    ibm_to_double,
    read_xpt,
    write_xpt,
    write_xpt_v5,
    write_xpt_v8,
)
from packages.deid.transforms import (
    cap_age_string,
    get_subject_date_shift,
    normalize_and_cap_age,
)

# =============================================================================
# 1. SAS Transport (XPT v5 and v8) & IBM 360 64-bit Floating Point Tests
# =============================================================================


def test_ibm360_float_codec_exhaustive_boundaries():
    """Validates IBM 360 float encoding across standard, extreme, and boundary numbers.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    # 1. Exact Zero and Negatives
    assert double_to_ibm(0.0) == b"\x00" * 8
    assert double_to_ibm(0) == b"\x00" * 8
    assert ibm_to_double(b"\x00" * 8) == 0.0

    # 2. Missing Value Representation
    missing_bytes = double_to_ibm(None)
    assert missing_bytes == b".\x00\x00\x00\x00\x00\x00\x00"
    assert missing_bytes[0] == 0x2E  # ASCII '.'
    assert ibm_to_double(missing_bytes) is None
    assert ibm_to_double(b".\x00\x00\x00\x00\x00\x00\x00") is None

    # 3. Exact Powers of 16 (where IBM base-16 mantissa normalization aligns)
    powers_of_16 = [
        16.0**-10,
        16.0**-2,
        16.0**-1,
        1.0,
        16.0,
        256.0,
        4096.0,
        65536.0,
        16.0**10,
    ]
    for p in powers_of_16:
        enc = double_to_ibm(p)
        dec = ibm_to_double(enc)
        assert pytest.approx(dec, rel=1e-7) == p

        enc_neg = double_to_ibm(-p)
        dec_neg = ibm_to_double(enc_neg)
        assert pytest.approx(dec_neg, rel=1e-7) == -p

    # 4. Mantissa boundaries (0.0625 <= mantissa < 1.0)
    boundary_vals = [
        0.0625,  # 1/16
        0.0625000000000001,
        0.125,  # 2/16
        0.5,
        0.99999999999999,
        1.0 - (1.0 / (1 << 56)),
    ]
    for bv in boundary_vals:
        enc = double_to_ibm(bv)
        dec = ibm_to_double(enc)
        assert pytest.approx(dec, rel=1e-6) == bv

    # 5. Very small and very large numbers
    large_val = 1.23456789e40
    enc_large = double_to_ibm(large_val)
    assert pytest.approx(ibm_to_double(enc_large), rel=1e-5) == large_val

    small_val = 9.87654321e-40
    enc_small = double_to_ibm(small_val)
    assert pytest.approx(ibm_to_double(enc_small), rel=1e-5) == small_val

    # 6. Truncated byte buffers
    assert ibm_to_double(b"\x00") == 0.0
    assert ibm_to_double(b"\x41\x10") == ibm_to_double(
        b"\x41\x10\x00\x00\x00\x00\x00\x00"
    )


def test_ibm360_collision_with_dot_missing_value():
    """Demonstrates IBM 360 float decoding bug on positive numbers with exponent -18 (biased exp 46 = 0x2E).

    @req:PRD-SYS-001
    @req:Trace-1
    """
    val = 0.5 * (16.0**-18)  # 1.0587911840678754e-22
    enc = double_to_ibm(val)
    assert enc[0] == 0x2E
    decoded = ibm_to_double(enc)
    # Currently fails because ibm_to_double only checks b[0] == 0x2E without checking trailing null bytes
    assert decoded is not None, f"Expected float {val}, got None due to 0x2E collision"
    assert pytest.approx(decoded, rel=1e-5) == val


def test_xpt_blank_character_row_truncation():
    """Validates XPT reader fidelity on middle blank rows, trailing blank rows, and all-blank datasets.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    records = [
        {"VAR1": "ROW1", "VAR2": "TEXT1"},
        {"VAR1": "", "VAR2": ""},  # Blank row
        {"VAR1": "ROW3", "VAR2": "TEXT3"},
    ]
    meta = [
        {"name": "VAR1", "type": "string"},
        {"name": "VAR2", "type": "string"},
    ]
    xpt_bytes = write_xpt("TEST_BLANK", records, version="v5", variables_metadata=meta)
    m, parsed = read_xpt(xpt_bytes)
    assert len(parsed) == 3, (
        f"Expected 3 records, but parser truncated to {len(parsed)} records"
    )

    # Scenario A: 5 records with trailing empty strings
    recs_a = [
        {"VAR1": "HELLO"},
        {"VAR1": "WORLD"},
        {"VAR1": "FOO"},
        {"VAR1": ""},
        {"VAR1": ""},
    ]
    meta_a = [{"name": "VAR1", "type": "string", "length": 8}]
    b_a = write_xpt("TEST_A", recs_a, version="v5", variables_metadata=meta_a)
    _, parsed_a = read_xpt(b_a)
    assert len(parsed_a) == 5, f"Expected 5, got {len(parsed_a)}"
    assert parsed_a == recs_a

    # Scenario B: 100 records with 15 trailing blanks
    recs_b = [{"VAR1": f"R{i}"} for i in range(85)] + [{"VAR1": ""} for _ in range(15)]
    b_b = write_xpt("TEST_B", recs_b, version="v5", variables_metadata=meta_a)
    _, parsed_b = read_xpt(b_b)
    assert len(parsed_b) == 100, f"Expected 100, got {len(parsed_b)}"
    assert parsed_b == recs_b

    # Scenario C: 2 records (all blank)
    recs_c = [{"VAR1": ""}, {"VAR1": ""}]
    b_c = write_xpt("TEST_C", recs_c, version="v5", variables_metadata=meta_a)
    _, parsed_c = read_xpt(b_c)
    assert len(parsed_c) == 2, f"Expected 2, got {len(parsed_c)}"
    assert parsed_c == recs_c


def test_xpt_80byte_card_framing_and_padding_stress():
    """Verifies that all XPT v5 and v8 outputs strictly adhere to 80-byte card multiples.

    @req:PRD-SYS-001
    @req:PRD-CRF-008
    @req:Trace-1

    """
    # Test varying variable counts: 1 to 25 variables
    for num_vars in [1, 2, 3, 5, 8, 10, 15, 20, 25]:
        records = [
            {
                f"VAR_{i}": f"VAL_{row}_{i}" if i % 2 == 0 else row * 10.5 + i
                for i in range(num_vars)
            }
            for row in range(7)  # Odd number of rows to test observation padding
        ]

        # 1. Test XPT v5
        xpt5 = write_xpt("TESTDS", records, version="v5")
        assert len(xpt5) % 80 == 0, (
            f"XPT v5 with {num_vars} vars not multiple of 80: {len(xpt5)}"
        )
        assert b"HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!" in xpt5
        assert b"HEADER RECORD*******NAMESTR HEADER RECORD!!!!!!!" in xpt5
        assert b"HEADER RECORD*******OBS     HEADER RECORD!!!!!!!" in xpt5

        meta5, parsed5 = read_xpt(xpt5)
        assert meta5["version"] == "v5"
        assert len(parsed5) == 7
        assert len(meta5["variables"]) == num_vars

        # 2. Test XPT v8
        xpt8 = write_xpt("TESTDS_LONG_V8", records, version="v8")
        assert len(xpt8) % 80 == 0, (
            f"XPT v8 with {num_vars} vars not multiple of 80: {len(xpt8)}"
        )
        assert b"HEADER RECORD*******LIBV8   HEADER RECORD!!!!!!!" in xpt8
        assert b"HEADER RECORD*******NAMSTRV8HEADER RECORD!!!!!!!" in xpt8
        assert b"HEADER RECORD*******OBSV8   HEADER RECORD!!!!!!!" in xpt8

        meta8, parsed8 = read_xpt(xpt8)
        assert meta8["version"] == "v8"
        assert len(parsed8) == 7
        assert len(meta8["variables"]) == num_vars


def test_xpt_name_length_and_label_limits_handling():
    """Validates truncation and padding for variable names and labels under v5 and v8.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    long_var_name = "VERY_LONG_VARIABLE_NAME_EXCEEDING_LIMITS"
    long_label = "This is an extremely long variable description label designed to exceed standard boundaries"

    records = [{long_var_name: "TestVal", "NUM_VAL": 123.45}]
    meta = [
        {"name": long_var_name, "type": "string", "label": long_label},
        {"name": "NUM_VAL", "type": "float", "label": "Short label"},
    ]

    # v5: names capped at 8 chars, labels at 40 chars
    xpt5 = write_xpt_v5("DATASET_V5", records, variables_metadata=meta)
    assert len(xpt5) % 80 == 0
    meta5, parsed5 = read_xpt(xpt5)
    v5_var_names = [v["name"] for v in meta5["variables"]]
    assert long_var_name[:8] in v5_var_names
    v5_labels = [v["label"] for v in meta5["variables"]]
    assert any(lbl == long_label[:40] for lbl in v5_labels)

    # v8: names capped at 32 chars, labels at 256 chars
    xpt8 = write_xpt_v8("DATASET_V8", records, variables_metadata=meta)
    assert len(xpt8) % 80 == 0
    meta8, parsed8 = read_xpt(xpt8)
    v8_var_names = [v["name"] for v in meta8["variables"]]
    assert long_var_name[:32] in v8_var_names
    v8_labels = [v["label"] for v in meta8["variables"]]
    assert any(lbl == long_label[:256] for lbl in v8_labels)


def test_xpt_large_dataset_roundtrip_integrity():
    """Generates a 500-record dataset and verifies 100% precision and round-trip data parity.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    records = []
    for i in range(500):
        records.append(
            {
                "USUBJID": f"SUBJ-{i:04d}",
                "AGE": float(20 + (i % 60)),
                "WEIGHT": 50.0 + (i * 0.125),
                "ARM": "ACTIVE ARM" if i % 2 == 0 else "PLACEBO",
                "FLAG": "Y" if i % 5 == 0 else "N",
            }
        )

    xpt_bytes = write_xpt("BIGDATA", records, version="v5")
    assert len(xpt_bytes) % 80 == 0

    meta, parsed = read_xpt(xpt_bytes)
    assert len(parsed) == 500
    for i in range(500):
        assert parsed[i]["USUBJID"] == f"SUBJ-{i:04d}"
        assert pytest.approx(parsed[i]["AGE"]) == float(20 + (i % 60))
        assert pytest.approx(parsed[i]["WEIGHT"]) == 50.0 + (i * 0.125)
        assert parsed[i]["ARM"] == ("ACTIVE ARM" if i % 2 == 0 else "PLACEBO")
        assert parsed[i]["FLAG"] == ("Y" if i % 5 == 0 else "N")


# =============================================================================
# 2. CDISC ODM-XML v1.3.2 & 21 CFR Part 11 Audit Trail Tests
# =============================================================================


def test_odm_xml_namespaces_and_root_attributes():
    """Validates ODM-XML namespace registration and mandatory header attributes.

    @req:PRD-SYS-004
    @req:Trace-7
    """
    xml_str = serialize_to_odm_xml(
        study_id="STUDY-TEST-001",
        data={"DM": [{"STUDYID": "STUDY-TEST-001", "USUBJID": "SUBJ-1"}]},
        file_oid="FILE.TEST.001",
        originator="Cadence Test Originator",
        source_system="Cadence Testing Engine",
        source_system_version="2.0.0",
    )

    root = ET.fromstring(xml_str)
    assert root.tag == "{http://www.cdisc.org/ns/odm/v1.3}ODM"
    assert root.attrib.get("ODMVersion") == "1.3.2"
    assert root.attrib.get("FileType") == "Snapshot"
    assert root.attrib.get("FileOID") == "FILE.TEST.001"
    assert root.attrib.get("Originator") == "Cadence Test Originator"
    assert root.attrib.get("SourceSystem") == "Cadence Testing Engine"
    assert root.attrib.get("SourceSystemVersion") == "2.0.0"
    assert "CreationDateTime" in root.attrib


def test_odm_xml_21cfr_part11_audit_trail_embedded():
    """Validates presence and correctness of 21 CFR Part 11 <AuditRecord> at subject & item levels.

    @req:PRD-SYS-004
    @req:Trace-7
    """
    ts = "2026-08-14T10:30:00Z"
    data_bundle = {
        "VS": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "SUBJ-201",
                "VSTESTCD": "DIABP",
                "VSORRES": 80.0,
                "_audit_user": "dr_smith",
                "_audit_reason": "Baseline physical exam",
                "_audit_timestamp": ts,
                "created_by": "nurse_jones",
                "reason_for_change": "Direct measurement on calibrated cuff",
                "created_at": ts,
            }
        ]
    }

    xml_str = serialize_to_odm_xml(study_id="STUDY-001", data=data_bundle)
    assert validate_odm_xml_string(xml_str) is True

    root = ET.fromstring(xml_str)
    odm_ns = {"odm": "http://www.cdisc.org/ns/odm/v1.3"}

    # 1. Subject-level AuditRecord
    subj_data = root.find(".//odm:SubjectData[@SubjectKey='SUBJ-201']", odm_ns)
    assert subj_data is not None
    subj_audit = subj_data.find("odm:AuditRecord", odm_ns)
    assert subj_audit is not None
    assert subj_audit.find("odm:UserRef", odm_ns).attrib["UserOID"] == "dr_smith"
    assert (
        subj_audit.find("odm:ReasonForChange", odm_ns).text == "Baseline physical exam"
    )
    assert subj_audit.find("odm:DateTimeStamp", odm_ns).text == ts

    # 2. Item-level AuditRecord
    item_data = subj_data.find(".//odm:ItemData[@ItemOID='IT.VS.VSORRES']", odm_ns)
    assert item_data is not None
    assert item_data.attrib["Value"] == "80.0"
    item_audit = item_data.find("odm:AuditRecord", odm_ns)
    assert item_audit is not None
    assert item_audit.find("odm:UserRef", odm_ns).attrib["UserOID"] == "nurse_jones"
    assert (
        item_audit.find("odm:ReasonForChange", odm_ns).text
        == "Direct measurement on calibrated cuff"
    )


def test_odm_xml_escaping_and_special_characters():
    """Ensures clinical notes with XML special characters (<, >, &, \", ') parse cleanly.

    @req:PRD-SYS-004
    @req:Trace-7
    """
    special_term = 'Patient had <Mild & Moderate> "Episode" with cough & fever @ 38.5°C'
    data = {
        "AE": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "SUBJ-301",
                "AETERM": special_term,
            }
        ]
    }

    xml_str = serialize_to_odm_xml(study_id="STUDY-001", data=data)
    assert validate_odm_xml_string(xml_str) is True

    root = ET.fromstring(xml_str)
    odm_ns = {"odm": "http://www.cdisc.org/ns/odm/v1.3"}
    item = root.find(".//odm:ItemData[@ItemOID='IT.AE.AETERM']", odm_ns)
    assert item is not None
    assert item.attrib["Value"] == special_term


# =============================================================================
# 3. CDISC Dataset-JSON v1.0.0 & Conformance Validator Tests
# =============================================================================


def test_dataset_json_pydantic_v2_model_and_2d_matrix():
    """Validates Dataset-JSON Pydantic v2 schemas and 2D matrix (itemData) structure.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "SUBJ-001",
            "AGE": 30,
            "SEX": "M",
            "RACE": "WHITE",
            "ARM": "ARM A",
        },
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "SUBJ-002",
            "AGE": 45,
            "SEX": "F",
            "RACE": "ASIAN",
            "ARM": "ARM B",
        },
    ]
    ds_json_obj = serialize_to_dataset_json(
        records, study_id="STUDY-001", dataset_name="DM"
    )

    assert ds_json_obj.datasetJSONVersion == "1.0.0"
    assert ds_json_obj.clinicalData is not None
    assert "IG.DM" in ds_json_obj.clinicalData.itemGroupData

    ig = ds_json_obj.clinicalData.itemGroupData["IG.DM"]
    assert ig.records == 2
    assert len(ig.itemData) == 2
    assert len(ig.items) == len(ig.itemData[0])

    # Validate JSON string serialization output
    json_str = serialize_dataset_json(records, study_id="STUDY-001", dataset_name="DM")
    assert "datasetJSONVersion" in json_str or "datasetJsonVersion" in json_str
    assert "IG.DM" in json_str


def test_validator_missing_required_variables_code():
    """Validates that missing mandatory SDTM/ADaM variables trigger MISSING_REQUIRED_VARIABLES.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    # DM missing SEX, RACE, ARM
    bad_dm = {
        "DM": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "DM",
                "USUBJID": "SUBJ-001",
                "SUBJID": "001",
            }
        ]
    }
    bad_json = serialize_to_dataset_json(bad_dm, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info:
        validate_dataset_json(bad_json)
    assert MISSING_REQUIRED_VARIABLES in str(exc_info.value)
    assert "SEX" in str(exc_info.value)
    assert "RACE" in str(exc_info.value)
    assert "ARM" in str(exc_info.value)


def test_validator_empty_studyid_usubjid_code():
    """Validates that blank or whitespace STUDYID/USUBJID triggers EMPTY_STUDYID_USUBJID.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    bad_records = {
        "DM": [
            {
                "STUDYID": "   ",
                "DOMAIN": "DM",
                "USUBJID": "SUBJ-001",
                "SUBJID": "001",
                "SEX": "M",
                "RACE": "WHITE",
                "ARM": "ARM A",
            },
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "DM",
                "USUBJID": "",
                "SUBJID": "002",
                "SEX": "F",
                "RACE": "WHITE",
                "ARM": "ARM A",
            },
        ]
    }
    bad_json = serialize_to_dataset_json(bad_records, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info:
        validate_dataset_json(bad_json)
    assert EMPTY_STUDYID_USUBJID in str(exc_info.value)


def test_validator_duplicate_sequence_code():
    """Validates that duplicate sequence numbers per subject trigger DUPLICATE_SEQUENCE.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    bad_ae = {
        "AE": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "AE",
                "USUBJID": "SUBJ-001",
                "AESEQ": 1,
                "AETERM": "Fever",
                "AESER": "N",
            },
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "AE",
                "USUBJID": "SUBJ-001",
                "AESEQ": 1,
                "AETERM": "Chills",
                "AESER": "N",
            },
        ]
    }
    bad_json = serialize_to_dataset_json(bad_ae, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info:
        validate_dataset_json(bad_json)
    assert DUPLICATE_SEQUENCE in str(exc_info.value)
    assert "AESEQ" in str(exc_info.value)


def test_validator_controlled_terminology_violation_code():
    """Validates that non-standard controlled terminology values trigger CONTROLLED_TERMINOLOGY_VIOLATION.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    bad_dm = {
        "DM": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "DM",
                "USUBJID": "SUBJ-001",
                "SUBJID": "001",
                "SEX": "NON_BINARY_UNREGISTERED",
                "RACE": "MARTIAN_COLONIST",
                "ARM": "ARM A",
            }
        ]
    }
    bad_json = serialize_to_dataset_json(bad_dm, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info:
        validate_dataset_json(bad_json)
    assert CONTROLLED_TERMINOLOGY_VIOLATION in str(exc_info.value)


def test_validator_null_flavor_inconsistency_code():
    """Validates that --STAT='NOT DONE' inconsistent with --REASND or results triggers NULL_FLAVOR_INCONSISTENCY.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    # 1. NOT DONE with missing REASND
    bad_vs1 = {
        "VS": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "VS",
                "USUBJID": "SUBJ-001",
                "VSSEQ": 1,
                "VSTESTCD": "SYSBP",
                "VSTEST": "Systolic BP",
                "VSSTAT": "NOT DONE",
                "VSREASND": "",
            }
        ]
    }
    bad_json1 = serialize_to_dataset_json(bad_vs1, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info1:
        validate_dataset_json(bad_json1)
    assert NULL_FLAVOR_INCONSISTENCY in str(exc_info1.value)

    # 2. NOT DONE with measurement populated
    bad_vs2 = {
        "VS": [
            {
                "STUDYID": "STUDY-001",
                "DOMAIN": "VS",
                "USUBJID": "SUBJ-001",
                "VSSEQ": 1,
                "VSTESTCD": "SYSBP",
                "VSTEST": "Systolic BP",
                "VSSTAT": "NOT DONE",
                "VSREASND": "Equipment Failure",
                "VSORRES": 120.0,
            }
        ]
    }
    bad_json2 = serialize_to_dataset_json(bad_vs2, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info2:
        validate_dataset_json(bad_json2)
    assert NULL_FLAVOR_INCONSISTENCY in str(exc_info2.value)


def test_validator_referential_inconsistency_code():
    """Validates ADaM cross-domain referential consistency checks.

    @req:PRD-SYS-001
    @req:Trace-1
    """
    bundle = {
        "ADSL": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "SUBJ-001",
                "SUBJID": "001",
                "SITEID": "SITE-A",
                "ARM": "Active",
                "ACTARM": "Active",
                "SAFFL": "Y",
                "ITTFL": "Y",
            }
        ],
        "ADAE": [
            {
                "STUDYID": "STUDY-001",
                "USUBJID": "SUBJ-GHOST",  # Not in ADSL
                "AESEQ": 1,
                "AETERM": "Nausea",
                "ASTDT": 22000,
                "AEDECOD": "Nausea",
            }
        ],
    }
    bad_json = serialize_to_dataset_json(bundle, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info:
        validate_dataset_json(bad_json)
    assert REFERENTIAL_INCONSISTENCY in str(exc_info.value)
    assert "SUBJ-GHOST" in str(exc_info.value)


def test_validator_supplemental_qualifier_violation_code():
    """Validates SUPP-- domain checks (RDOMAIN match, IDVAR/IDVARVAL parity).

    @req:PRD-SYS-001
    @req:Trace-1
    """
    # 1. IDVAR populated but IDVARVAL empty
    bundle = {
        "SUPPAE": [
            {
                "STUDYID": "STUDY-001",
                "RDOMAIN": "AE",
                "USUBJID": "SUBJ-001",
                "IDVAR": "AESEQ",
                "IDVARVAL": "",  # Empty
                "QNAM": "AELOC",
                "QLABEL": "Location",
                "QVAL": "Head",
            }
        ]
    }
    bad_json = serialize_to_dataset_json(bundle, study_id="STUDY-001")
    with pytest.raises(DatasetJSONValidationError) as exc_info:
        validate_dataset_json(bad_json)
    assert SUPPLEMENTAL_QUALIFIER_VIOLATION in str(exc_info.value)


# =============================================================================
# 4. HIPAA Safe Harbor & GDPR De-Identification Tests
# =============================================================================


def test_deid_deterministic_date_shift_range_and_invariants():
    """Verifies that HMAC-SHA256 date shift is strictly within [-365, +365] days across 5,000 subjects.

    @req:PRD-SYS-001
    @req:Trace-12
    """
    salt = "gxp-test-salt-secret-999"
    seen_offsets = set()

    for i in range(5000):
        subj_id = f"SUBJ-TEST-{i:05d}"
        shift = get_subject_date_shift(subj_id, salt)
        assert -365 <= shift <= 365, (
            f"Shift {shift} out of [-365, +365] bounds for {subj_id}"
        )
        seen_offsets.add(shift)

        # Determinism check: repeating with same salt returns same shift
        assert get_subject_date_shift(subj_id, salt) == shift

    # Verify coverage across the distribution (expecting wide spread)
    assert len(seen_offsets) > 700, (
        f"Expected near-complete coverage of 731 days, got {len(seen_offsets)}"
    )


def test_deid_longitudinal_parity_across_multi_domain_patient_journey():
    """Verifies that inter-event intervals (T2 - T1) are strictly invariant after de-identification.

    @req:PRD-SYS-001
    @req:Trace-12
    """
    salt = "production-salt-phase1"
    raw_record_dm = {
        "USUBJID": "SUBJ-PATIENT-1",
        "RFSTDTC": "2026-01-01",  # Baseline
    }
    raw_record_ae = {
        "USUBJID": "SUBJ-PATIENT-1",
        "AESTDTC": "2026-01-15",  # 14 days after baseline
        "AEENDTC": "2026-01-20",  # 19 days after baseline
    }
    raw_record_vs = {
        "USUBJID": "SUBJ-PATIENT-1",
        "VSDTC": "2026-02-01",  # 31 days after baseline
    }

    deid_dm = deidentify_record(raw_record_dm, salt)
    deid_ae = deidentify_record(raw_record_ae, salt)
    deid_vs = deidentify_record(raw_record_vs, salt)

    # Convert shifted dates back to datetime
    base_dt = datetime.fromisoformat(deid_dm["RFSTDTC"])
    ae_start_dt = datetime.fromisoformat(deid_ae["AESTDTC"])
    ae_end_dt = datetime.fromisoformat(deid_ae["AEENDTC"])
    vs_dt = datetime.fromisoformat(deid_vs["VSDTC"])

    # Assert invariant differences
    assert (ae_start_dt - base_dt).days == 14
    assert (ae_end_dt - base_dt).days == 19
    assert (vs_dt - base_dt).days == 31
    assert (ae_end_dt - ae_start_dt).days == 5


def test_deid_partial_dates_and_numeric_sas_dates():
    """Validates date shifting on partial dates (YYYY-MM, YYYY) and numeric SAS dates.

    @req:PRD-SYS-001
    @req:Trace-12
    """
    salt = "test-salt"
    subj = "SUBJ-PARTIAL"
    shift = get_subject_date_shift(subj, salt)

    # Partial date string
    rec = {
        "USUBJID": subj,
        "RFSTDTC": "2026-06",
        "TRTSDT": 22000,
    }
    deid_r = deidentify_record(rec, salt)

    assert deid_r["TRTSDT"] == 22000 + shift
    assert deid_r["RFSTDTC"] != "2026-06" or shift == 0


def test_deid_age_capping_and_string_redaction():
    """Validates that AGE > 89 is capped to 89 across int, float, and text forms.

    @req:PRD-SYS-001
    @req:Trace-12
    """
    assert normalize_and_cap_age(90) == 89
    assert normalize_and_cap_age(102) == 89
    assert normalize_and_cap_age(89) == 89
    assert normalize_and_cap_age(45) == 45
    assert normalize_and_cap_age(92.5) == 89.0
    assert normalize_and_cap_age("95") == "89"
    assert normalize_and_cap_age("89") == "89"

    # Text cap
    assert (
        cap_age_string("Subject is age 95 with severe hypertension")
        == "Subject is age 89+ with severe hypertension"
    )
    assert cap_age_string("92 years old male") == "89+ years old male"
    assert cap_age_string("45 years old female") == "45 years old female"


def test_deid_error_message_scrubbing_pii():
    """Verifies that error logs scrub identifiers and quoted values.

    @req:PRD-SYS-001
    @req:Trace-12
    """
    err = "Export failed for SUBJ-999 at SITE-X in STUDY-XYZ: Value 'CONFIDENTIAL_SECRET' or \"INTERNAL_KEY\" invalid."
    scrubbed = scrub_error_message(err)

    assert "SUBJ-999" not in scrubbed
    assert "SITE-X" not in scrubbed
    assert "STUDY-XYZ" not in scrubbed
    assert "CONFIDENTIAL_SECRET" not in scrubbed
    assert "INTERNAL_KEY" not in scrubbed
    assert "[REDACTED_SUBJECT]" in scrubbed
    assert "[REDACTED_SITE]" in scrubbed
    assert "[REDACTED_STUDY]" in scrubbed
    assert "[REDACTED_VALUE]" in scrubbed
