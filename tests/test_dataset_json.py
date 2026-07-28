"""
Unit tests for Dataset-JSON Serializer and Validator.
"""

import pytest

from apps.execution.biostat import (
    DatasetJSONValidationError,
    serialize_to_dataset_json,
    validate_dataset_json,
)


def test_serialize_single_dataset_dm():
    """Verify serialization of a single dataset (DM) works correctly."""
    dm_records = [
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
        },
        {
            "STUDYID": "STUDY-001",
            "DOMAIN": "DM",
            "USUBJID": "STUDY-001-SITE-A-SUBJ-002",
            "SUBJID": "SUBJ-002",
            "RFSTDTC": "2026-08-02",
            "RFENDTC": "2026-08-25",
            "BRTHDTC": "1985-10-20",
            "AGE": 40,
            "AGEU": "YEARS",
            "SEX": "F",
            "RACE": "BLACK OR AFRICAN AMERICAN",
            "ARM": "Placebo Arm",
        },
    ]

    dj = serialize_to_dataset_json(
        data=dm_records,
        study_id="STUDY-001",
        metadata_version_id="MDV.001",
        file_oid="FILE-001",
    )

    assert dj.datasetJSONVersion == "1.0.0"
    assert dj.fileOID == "FILE-001"
    assert dj.clinicalData is not None
    assert dj.clinicalData.studyOID == "STUDY-001"
    assert dj.clinicalData.metaDataVersionOID == "MDV.001"

    # Confirm itemGroupData DM key
    assert "IG.DM" in dj.clinicalData.itemGroupData
    group = dj.clinicalData.itemGroupData["IG.DM"]
    assert group.records == 2
    assert group.name == "DM"
    assert group.label == "Demographics"

    # Confirm ordered items metadata
    item_names = [item.name for item in group.items]
    assert item_names == [
        "STUDYID",
        "DOMAIN",
        "USUBJID",
        "SUBJID",
        "RFSTDTC",
        "RFENDTC",
        "BRTHDTC",
        "AGE",
        "AGEU",
        "SEX",
        "RACE",
        "ARM",
    ]

    # Confirm itemData rows are in exact order
    assert len(group.itemData) == 2
    assert group.itemData[0] == [
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


def test_serialize_bundle():
    """Verify serialization of a bundled dict of multiple datasets."""
    data_bundle = {
        "DM": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-01-001",
                "SUBJID": "001",
                "SEX": "M",
                "RACE": "WHITE",
                "ARM": "Active",
            }
        ],
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,
                "AETERM": "HEADACHE",
                "AESER": "N",
            }
        ],
    }

    dj = serialize_to_dataset_json(
        data=data_bundle,
        study_id="STUDY-01",
    )

    assert "IG.DM" in dj.clinicalData.itemGroupData
    assert "IG.AE" in dj.clinicalData.itemGroupData

    dm_group = dj.clinicalData.itemGroupData["IG.DM"]
    ae_group = dj.clinicalData.itemGroupData["IG.AE"]

    assert dm_group.records == 1
    assert ae_group.records == 1


def test_validation_success_on_valid_bundle():
    """Verify that validator passes on a perfectly valid dataset bundle."""
    data_bundle = {
        "DM": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-01-001",
                "SUBJID": "001",
                "SEX": "M",
                "RACE": "WHITE",
                "ARM": "Active",
            }
        ],
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,
                "AETERM": "HEADACHE",
                "AESER": "N",
            }
        ],
        "ADSL": [
            {
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-001",
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
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-001",
                "ASTDT": 24323,
                "AEDECOD": "Headache",
                "AESEQ": 1,
                "ARM": "Active",
                "ACTARM": "Active",
                "SAFFL": "Y",
                "ITTFL": "Y",
                "SITEID": "SITE-A",
            }
        ],
    }

    dj = serialize_to_dataset_json(data=data_bundle, study_id="STUDY-01")

    # This should execute successfully without raising any exceptions
    validate_dataset_json(dj)


def test_validator_missing_required_variables():
    """Verify that missing required variables raises validation error."""
    dm_invalid = [
        {
            "STUDYID": "STUDY-01",
            "DOMAIN": "DM",
            "USUBJID": "STUDY-01-001",
            "SUBJID": "001",
            # "SEX" is missing
            "RACE": "WHITE",
            "ARM": "Active",
        }
    ]

    dj = serialize_to_dataset_json(data=dm_invalid, study_id="STUDY-01")

    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)

    assert "Missing required variable(s)" in str(exc.value)
    assert "SEX" in str(exc.value)


def test_validator_empty_studyid_usubjid():
    """Verify that empty or null STUDYID/USUBJID fields trigger error."""
    dm_invalid = [
        {
            "STUDYID": "  ",  # blank string
            "DOMAIN": "DM",
            "USUBJID": "STUDY-01-001",
            "SUBJID": "001",
            "SEX": "M",
            "RACE": "WHITE",
            "ARM": "Active",
        }
    ]

    dj = serialize_to_dataset_json(data=dm_invalid, study_id="STUDY-01")

    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)

    assert "STUDYID is empty or missing" in str(exc.value)


def test_validator_duplicate_sequence_numbers():
    """Verify that duplicate sequence numbers per subject trigger error."""
    ae_invalid = [
        {
            "STUDYID": "STUDY-01",
            "DOMAIN": "AE",
            "USUBJID": "STUDY-01-001",
            "AESEQ": 1,
            "AETERM": "Nausea",
            "AESER": "N",
        },
        {
            "STUDYID": "STUDY-01",
            "DOMAIN": "AE",
            "USUBJID": "STUDY-01-001",
            "AESEQ": 1,  # Duplicate sequence
            "AETERM": "Vomiting",
            "AESER": "N",
        },
    ]

    dj = serialize_to_dataset_json(data=ae_invalid, study_id="STUDY-01")

    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)

    assert "Duplicate key found" in str(exc.value)
    assert "AESEQ" in str(exc.value)


def test_validator_adam_referential_consistency_subject_not_in_adsl_or_dm():
    """Verify that ADSL subject not in DM, or ADAE/ADVS subject not in ADSL triggers error."""
    # Scenario A: ADSL subject not in DM
    bundle_a = {
        "DM": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-01-001",
                "SUBJID": "001",
                "SEX": "M",
                "RACE": "WHITE",
                "ARM": "Active",
            }
        ],
        "ADSL": [
            {
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-002",  # Not in DM
                "SUBJID": "002",
                "SITEID": "SITE-A",
                "ARM": "Active",
                "ACTARM": "Active",
                "SAFFL": "Y",
                "ITTFL": "Y",
            }
        ],
    }

    dj_a = serialize_to_dataset_json(data=bundle_a, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj_a)
    assert "Referential inconsistency" in str(exc.value)
    assert "STUDY-01-002" in str(exc.value)

    # Scenario B: ADAE subject not in ADSL
    bundle_b = {
        "ADSL": [
            {
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-001",
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
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-002",  # Not in ADSL
                "ASTDT": 100,
                "AEDECOD": "Rash",
                "AESEQ": 1,
            }
        ],
    }

    dj_b = serialize_to_dataset_json(data=bundle_b, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj_b)
    assert "Referential inconsistency" in str(exc.value)
    assert "STUDY-01-002" in str(exc.value)


def test_validator_adam_referential_consistency_demographic_mismatch():
    """Verify that mismatch of shared demographics/population flags between ADAE and ADSL triggers error."""
    bundle = {
        "ADSL": [
            {
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-001",
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
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-001",
                "ASTDT": 100,
                "AEDECOD": "Rash",
                "AESEQ": 1,
                "ARM": "Active",
                "ACTARM": "Placebo",  # Mismatched ACTARM (Placebo vs Active in ADSL)
                "SAFFL": "Y",
                "ITTFL": "Y",
                "SITEID": "SITE-A",
            }
        ],
    }

    dj = serialize_to_dataset_json(data=bundle, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "Referential inconsistency" in str(exc.value)
    assert "ACTARM value 'Placebo' does not match ADSL value 'Active'" in str(exc.value)


def test_validator_adam_referential_consistency_missing_source_event():
    """Verify that ADAE record AESEQ not found in SDTM AE triggers error."""
    bundle = {
        "ADSL": [
            {
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-001",
                "SUBJID": "001",
                "SITEID": "SITE-A",
                "ARM": "Active",
                "ACTARM": "Active",
                "SAFFL": "Y",
                "ITTFL": "Y",
            }
        ],
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,  # Only sequence 1 exists
                "AETERM": "Rash",
                "AESER": "N",
            }
        ],
        "ADAE": [
            {
                "STUDYID": "STUDY-01",
                "USUBJID": "STUDY-01-001",
                "ASTDT": 100,
                "AEDECOD": "Rash",
                "AESEQ": 2,  # Sequence 2 does not exist in AE
                "ARM": "Active",
                "ACTARM": "Active",
                "SAFFL": "Y",
                "ITTFL": "Y",
                "SITEID": "SITE-A",
            }
        ],
    }

    dj = serialize_to_dataset_json(data=bundle, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "Sequence AESEQ='2' for subject 'STUDY-01-001' not found in AE" in str(
        exc.value
    )


def test_validator_controlled_terminology_failures():
    """Verify that invalid controlled terminology raises clear validation errors with CONTROLLED_TERMINOLOGY_VIOLATION code."""
    # Invalid SEX
    bundle_sex = {
        "DM": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-01-001",
                "SUBJID": "001",
                "SEX": "INVALID_SEX",
                "RACE": "WHITE",
                "ARM": "Active",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_sex, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "CONTROLLED_TERMINOLOGY_VIOLATION" in str(exc.value)
    assert "SEX value 'INVALID_SEX' is not a valid CDISC controlled terminology." in str(exc.value)

    # Invalid RACE
    bundle_race = {
        "DM": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-01-001",
                "SUBJID": "001",
                "SEX": "M",
                "RACE": "INVALID_RACE",
                "ARM": "Active",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_race, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "CONTROLLED_TERMINOLOGY_VIOLATION" in str(exc.value)
    assert "RACE value 'INVALID_RACE' is not a valid CDISC controlled terminology." in str(exc.value)


def test_validator_null_flavor_and_stat_reasnd_consistency():
    """Verify that --STAT / --REASND and null-flavor rules are correctly enforced."""
    # VSSTAT is 'NOT DONE' but VSREASND is empty
    bundle_missing_reasnd = {
        "VS": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "VS",
                "USUBJID": "STUDY-01-001",
                "VSSEQ": 1,
                "VSTESTCD": "SYSBP",
                "VSTEST": "Systolic Blood Pressure",
                "VSSTAT": "NOT DONE",
                "VSREASND": "",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_missing_reasnd, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "NULL_FLAVOR_INCONSISTENCY" in str(exc.value)
    assert "VSSTAT is 'NOT DONE', but VSREASND is empty or missing" in str(exc.value)

    # VSSTAT is 'NOT DONE' but measurement field is populated
    bundle_stat_with_meas = {
        "VS": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "VS",
                "USUBJID": "STUDY-01-001",
                "VSSEQ": 1,
                "VSTESTCD": "SYSBP",
                "VSTEST": "Systolic Blood Pressure",
                "VSSTAT": "NOT DONE",
                "VSREASND": "Equipment Failure",
                "VSORRES": 120.0,
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_stat_with_meas, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "NULL_FLAVOR_INCONSISTENCY" in str(exc.value)
    assert "VSSTAT is 'NOT DONE', but measurement field VSORRES is populated with '120.0'." in str(exc.value)


def test_validator_supp_dataset_linkage_and_structure():
    """Verify that SUPP-- structural and parent referential linkage rules are validated."""
    # 1. RDOMAIN mismatch
    bundle_rdomain_mismatch = {
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,
                "AETERM": "Headache",
                "AESER": "N",
            }
        ],
        "SUPPAE": [
            {
                "STUDYID": "STUDY-01",
                "RDOMAIN": "DM",  # Mismatch (should be AE)
                "USUBJID": "STUDY-01-001",
                "IDVAR": "AESEQ",
                "IDVARVAL": "1",
                "QNAM": "AELOC",
                "QLABEL": "Location",
                "QVAL": "LEFT",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_rdomain_mismatch, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "SUPPLEMENTAL_QUALIFIER_VIOLATION" in str(exc.value)
    assert "RDOMAIN 'DM' must match parent domain 'AE'" in str(exc.value)

    # 2. Parent record not found for USUBJID
    bundle_parent_missing = {
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,
                "AETERM": "Headache",
                "AESER": "N",
            }
        ],
        "SUPPAE": [
            {
                "STUDYID": "STUDY-01",
                "RDOMAIN": "AE",
                "USUBJID": "STUDY-01-999",  # USUBJID does not exist in AE
                "IDVAR": "AESEQ",
                "IDVARVAL": "1",
                "QNAM": "AELOC",
                "QLABEL": "Location",
                "QVAL": "LEFT",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_parent_missing, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "SUPPLEMENTAL_QUALIFIER_VIOLATION" in str(exc.value)
    assert "Parent record not found for USUBJID='STUDY-01-999' in AE" in str(exc.value)

    # 3. IDVAR not found in parent
    bundle_idvar_missing = {
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,
                "AETERM": "Headache",
                "AESER": "N",
            }
        ],
        "SUPPAE": [
            {
                "STUDYID": "STUDY-01",
                "RDOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "IDVAR": "INVALID_VAR",  # Not in AE
                "IDVARVAL": "1",
                "QNAM": "AELOC",
                "QLABEL": "Location",
                "QVAL": "LEFT",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_idvar_missing, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "SUPPLEMENTAL_QUALIFIER_VIOLATION" in str(exc.value)
    assert "Identifying variable IDVAR='INVALID_VAR' not found in parent dataset AE" in str(exc.value)

    # 4. IDVARVAL value mismatch
    bundle_idvarval_mismatch = {
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,
                "AETERM": "Headache",
                "AESER": "N",
            }
        ],
        "SUPPAE": [
            {
                "STUDYID": "STUDY-01",
                "RDOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "IDVAR": "AESEQ",
                "IDVARVAL": "999",  # AESEQ 999 does not exist for STUDY-01-001
                "QNAM": "AELOC",
                "QLABEL": "Location",
                "QVAL": "LEFT",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_idvarval_mismatch, study_id="STUDY-01")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "SUPPLEMENTAL_QUALIFIER_VIOLATION" in str(exc.value)
    assert "Identifying variable value IDVARVAL='999' for IDVAR='AESEQ' not found in parent record" in str(exc.value)

    # 5. Completely valid SUPP-- linkage passes validation
    bundle_valid = {
        "AE": [
            {
                "STUDYID": "STUDY-01",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "AESEQ": 1,
                "AETERM": "Headache",
                "AESER": "N",
            }
        ],
        "SUPPAE": [
            {
                "STUDYID": "STUDY-01",
                "RDOMAIN": "AE",
                "USUBJID": "STUDY-01-001",
                "IDVAR": "AESEQ",
                "IDVARVAL": "1",
                "QNAM": "AELOC",
                "QLABEL": "Location",
                "QVAL": "LEFT",
            }
        ]
    }
    dj = serialize_to_dataset_json(data=bundle_valid, study_id="STUDY-01")
    validate_dataset_json(dj)  # Should pass with no exception
