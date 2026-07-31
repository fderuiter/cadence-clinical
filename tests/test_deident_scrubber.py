"""Tests for HIPAA de-identification, date-shifting, and subject ID pseudonymization scrubber.

Requirements: PRD-SYS-001
"""

from typing import Any, Dict, List

from sdtm.scrubber_models import DeidentConfig

from apps.execution.exports.sdtm_json_builder import SDTMJSONBuilder
from apps.execution.services.deident_scrubber import (
    HIPAADataScrubber,
    scrub_dataset,
)


def test_scrubber_preserves_date_intervals():
    """Test that date-shifting preserves relative days between AE start date and AE end date.

    Requirements: PRD-SYS-001
    """
    config = DeidentConfig(
        study_salt="secret-study-salt-123",
        enable_date_shift=True,
        max_date_shift_days=365,
        scrub_free_text=False,
    )

    records: List[Dict[str, Any]] = [
        {
            "STUDYID": "STUDY-01",
            "USUBJID": "SUBJ-001",
            "AESTDTC": "2026-05-12",
            "AEENDTC": "2026-05-15",
        }
    ]

    scrubbed, summary = scrub_dataset(records, config)

    assert summary.records_processed == 1
    assert summary.dates_shifted == 2

    # Verify that the relative interval is preserved (exactly 3 days difference)
    import datetime

    start_date = datetime.date.fromisoformat(scrubbed[0]["AESTDTC"])
    end_date = datetime.date.fromisoformat(scrubbed[0]["AEENDTC"])
    delta = end_date - start_date
    assert delta.days == 3


def test_scrubber_subject_id_non_reversible():
    """Test that raw subject ID cannot be reverse-engineered without secret study salt.

    Requirements: PRD-SYS-001
    """
    scrubber_with_salt1 = HIPAADataScrubber(study_salt="secret-salt-alpha")
    scrubber_with_salt2 = HIPAADataScrubber(study_salt="secret-salt-beta")

    raw_subject = "PATIENT-999"

    pseudo_id1 = scrubber_with_salt1.pseudonymize_usubjid("STUDY-01", raw_subject)
    pseudo_id2 = scrubber_with_salt2.pseudonymize_usubjid("STUDY-01", raw_subject)

    # They should be completely different pseudonymized strings
    assert pseudo_id1 != pseudo_id2
    assert "PATIENT-999" not in pseudo_id1
    assert "PATIENT-999" not in pseudo_id2


def test_free_text_pii_scrubbing():
    """Test that free-text PII scrubbing removes SSNs, phone numbers, emails, and postal addresses from comments.

    Requirements: PRD-SYS-001
    """
    config = DeidentConfig(
        study_salt="secret-study-salt-123",
        enable_date_shift=False,
        max_date_shift_days=365,
        scrub_free_text=True,
    )

    records: List[Dict[str, Any]] = [
        {
            "STUDYID": "STUDY-01",
            "USUBJID": "SUBJ-001",
            "AETERM": "Patient has SSN 123-45-6789 and email jules@example.com.",
            "COMMENTS": "Contact phone is (555) 123-4567. Lives at 123 Main Street.",
        }
    ]

    scrubbed, summary = scrub_dataset(records, config)

    aeterm = scrubbed[0]["AETERM"]
    comments = scrubbed[0]["COMMENTS"]

    assert "123-45-6789" not in aeterm
    assert "[REDACTED_SSN]" in aeterm
    assert "jules@example.com" not in aeterm
    assert "[REDACTED_EMAIL]" in aeterm

    assert "(555) 123-4567" not in comments
    assert "[REDACTED_PHONE]" in comments
    assert "123 Main Street" not in comments
    assert "[REDACTED_ADDRESS]" in comments


def test_sdtm_json_builder_integration():
    """Test that SDTMJSONBuilder integrates the scrubber and serializes correctly.

    Requirements: PRD-SYS-001
    """
    config = DeidentConfig(
        study_salt="test-salt",
        enable_date_shift=True,
        max_date_shift_days=365,
        scrub_free_text=True,
    )

    builder = SDTMJSONBuilder(config=config)

    records = [
        {
            "STUDYID": "STUDY-01",
            "USUBJID": "SUBJ-101",
            "SUBJID": "SUBJ-101",
            "DOMAIN": "DM",
            "RFSTDTC": "2026-05-12",
            "ARM": "Active Arm",
            "SEX": "M",
            "RACE": "WHITE",
        }
    ]

    dataset_json = builder.build_sdtm_dataset_json(
        study_id="STUDY-01",
        domain="DM",
        records=records,
    )

    # Check that dataset-json structure is correctly produced
    assert "clinicalData" in dataset_json
    item_group_data = dataset_json["clinicalData"]["itemGroupData"]
    assert "IG.DM" in item_group_data

    # Check that USUBJID was pseudonymized
    item_data = item_group_data["IG.DM"]["itemData"]
    assert len(item_data) == 1
    # Check that the dates were shifted and USUBJID is pseudonymized
    # The serialisation builds actual_keys/variables list and sorts. Let's find index of USUBJID
    items_meta = item_group_data["IG.DM"]["items"]
    usubjid_idx = next(
        i for i, item in enumerate(items_meta) if item["name"] == "USUBJID"
    )
    usubjid_val = item_data[0][usubjid_idx]
    assert usubjid_val != "SUBJ-101"
    assert "SUBJ-101" not in usubjid_val
