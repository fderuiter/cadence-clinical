"""
Unit tests for CDISC Dataset-JSON v1.0 Builder and Pilot Validator.

Requirements Traceability: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

from apps.execution.services.dataset_json_builder import (
    DatasetJsonBuilder,
    build_dataset_json,
    validate_dataset_json_conformance,
)


def test_build_domain_dataset_dm():
    """Verify that generating a DM dataset produces a valid Dataset-JSON structure.

    Requirements: PRD-SYS-001
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-001",
            "SUBJID": "SUBJ-001",
            "RFSTDTC": "2026-08-01",
            "RFENDTC": "2026-08-31",
            "BRTHDTC": "1990-05-15",
            "AGE": 36,
            "AGEU": "YEARS",
            "SEX": "M",
            "RACE": "WHITE",
            "ARM": "Active Arm",
        }
    ]

    builder = DatasetJsonBuilder(study_id="STUDY-001")
    payload_dict = builder.build_domain_dataset("DM", records)

    assert payload_dict["datasetJSONVersion"] == "1.0.0"
    assert payload_dict["fileOID"] == "www.cdisc.org/dataset-json/v1.0/STUDY-001/dm"
    assert "clinicalData" in payload_dict
    assert payload_dict["clinicalData"]["studyOID"] == "STUDY-001"
    assert payload_dict["clinicalData"]["metaDataVersionOID"] == "MDV.001"

    # Verify itemGroupData
    group_data = payload_dict["clinicalData"]["itemGroupData"]["DM"]
    assert group_data["records"] == 1
    assert group_data["name"] == "DM"
    assert group_data["label"] == "Demographics"

    # Verify items/variables
    items = group_data["items"]
    assert len(items) == 12
    assert items[0]["name"] == "STUDYID"
    assert items[7]["name"] == "AGE"
    assert items[7]["type"] == "integer"

    # Verify itemData row structure
    assert len(group_data["itemData"]) == 1
    assert group_data["itemData"][0] == [
        "STUDY-001",
        "DM",
        "STUDY-001-SITE-A-SUBJ-001",
        "SUBJ-001",
        "2026-08-01",
        "2026-08-31",
        "1990-05-15",
        36,
        "YEARS",
        "M",
        "WHITE",
        "Active Arm",
    ]


def test_build_dataset_json_validation():
    """Verify build_dataset_json successfully validates schema and returns strong type.

    Requirements: PRD-SYS-001
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-001",
            "SUBJID": "SUBJ-001",
            "SEX": "F",
            "RACE": "ASIAN",
            "ARM": "Placebo Arm",
        }
    ]

    payload = build_dataset_json("DM", records, study_id="STUDY-001")
    assert payload.datasetJSONVersion == "1.0.0"
    assert payload.clinicalData["studyOID"] == "STUDY-001"


def test_conformance_validation_missing_mandatory_vars():
    """Verify missing mandatory SDTM variables triggers conformance errors.

    Requirements: PRD-SYS-001
    """
    # Create a payload missing USUBJID
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            # "USUBJID" is missing
            "SUBJID": "SUBJ-001",
            "SEX": "M",
            "RACE": "WHITE",
            "ARM": "Active Arm",
        }
    ]

    payload = build_dataset_json("DM", records, study_id="STUDY-001")
    errors = validate_dataset_json_conformance(payload.model_dump())

    assert len(errors) > 0
    assert any("USUBJID" in err.message for err in errors)


def test_conformance_validation_missing_sequence_non_dm():
    """Verify non-DM domain checks for sequence variable.

    Requirements: PRD-SYS-001
    """
    # AE requires AESEQ, but we omit it
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "AE",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-001",
            "AETERM": "Headache",
            "AESER": "N",
        }
    ]

    payload = build_dataset_json("AE", records, study_id="STUDY-001")
    errors = validate_dataset_json_conformance(payload.model_dump())

    assert len(errors) > 0
    assert any("AESEQ" in err.message for err in errors)


def test_conformance_validation_invalid_data_type():
    """Verify that an invalid data type (e.g. string in integer field) triggers conformance error.

    Requirements: PRD-SYS-001
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-001",
            "SUBJID": "SUBJ-001",
            "AGE": "Thirty-Six",  # String instead of integer
            "SEX": "M",
            "RACE": "WHITE",
            "ARM": "Active Arm",
        }
    ]

    payload = build_dataset_json("DM", records, study_id="STUDY-001")
    errors = validate_dataset_json_conformance(payload.model_dump())

    assert len(errors) > 0
    assert any("AGE" in err.message for err in errors)
    assert any("integer" in err.message for err in errors)


def test_conformance_validation_float_data_type_error():
    """Verify that an invalid data type for float fields triggers conformance error.

    Requirements: PRD-SYS-001
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "VS",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-001",
            "VSSEQ": 1,
            "VSTESTCD": "SYSBP",
            "VSTEST": "Systolic Blood Pressure",
            "VSORRES": "One-Twenty",  # String instead of float/numeric
        }
    ]

    payload = build_dataset_json("VS", records, study_id="STUDY-001")
    errors = validate_dataset_json_conformance(payload.model_dump())

    assert len(errors) > 0
    assert any("VSORRES" in err.message for err in errors)
    assert any("float" in err.message for err in errors)


def test_conformance_validation_string_data_type_error():
    """Verify that an invalid data type for string fields triggers conformance error.

    Requirements: PRD-SYS-001
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-001",
            "SUBJID": "SUBJ-001",
            "SEX": 123,  # Integer instead of string
            "RACE": "WHITE",
            "ARM": "Active Arm",
        }
    ]

    payload = build_dataset_json("DM", records, study_id="STUDY-001")
    errors = validate_dataset_json_conformance(payload.model_dump())

    assert len(errors) > 0
    assert any("SEX" in err.message for err in errors)
    assert any("string" in err.message for err in errors)


def test_dynamic_fallback_unknown_domain():
    """Verify fallback when domain is unknown to SDTMIG_V34_METADATA.

    Requirements: PRD-SYS-001
    """
    records = [
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "XX",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-001",
            "XXSEQ": 1,
            "XXVAL": "Custom String",
            "XXNUM": 42.5,
        }
    ]

    payload = build_dataset_json("XX", records, study_id="STUDY-001")
    assert payload.datasetJSONVersion == "1.0.0"
    group_data = payload.clinicalData["itemGroupData"]["XX"]
    assert group_data["name"] == "XX"

    items_map = {item["name"]: item for item in group_data["items"]}
    assert items_map["XXVAL"]["type"] == "string"
    assert items_map["XXNUM"]["type"] == "float"
    assert items_map["XXSEQ"]["type"] == "integer"
