"""
Unit tests for the shared ProtocolVersionRef domain contract.

Tests cover happy-path validations, edge cases, invalid inputs, and cross-service
serialization/deserialization scenarios.
"""

import json

import pytest
from protocol_version_ref import ProtocolVersionRef, ProtocolVersionStatus
from pydantic import ValidationError


def test_protocol_version_ref_valid_payload():
    """
    Verify that a valid payload parses and populates the model fields correctly.
    """
    payload = {
        "study_id": "STUDY-001",
        "version_tag": "v1.2",
        "version_index": 3,
        "status": "ACTIVE",
    }
    model = ProtocolVersionRef(**payload)

    assert model.study_id == "STUDY-001"
    assert model.version_tag == "v1.2"
    assert model.version_index == 3
    assert model.status == ProtocolVersionStatus.ACTIVE


def test_protocol_version_ref_validation_blank_fields():
    """
    Verify that blank or whitespace-only study_id or version_tag raises validation errors.
    """
    # Empty string for study_id
    with pytest.raises(ValidationError) as excinfo:
        ProtocolVersionRef(
            study_id="",
            version_tag="1.0",
            version_index=1,
            status="DRAFT",
        )
    assert "Study ID cannot be empty" in str(excinfo.value)

    # Whitespace string for study_id
    with pytest.raises(ValidationError) as excinfo:
        ProtocolVersionRef(
            study_id="   ",
            version_tag="1.0",
            version_index=1,
            status="DRAFT",
        )
    assert "Study ID cannot be empty" in str(excinfo.value)

    # Empty string for version_tag
    with pytest.raises(ValidationError) as excinfo:
        ProtocolVersionRef(
            study_id="STUDY-001",
            version_tag="",
            version_index=1,
            status="DRAFT",
        )
    assert "Version tag cannot be empty" in str(excinfo.value)

    # Whitespace string for version_tag
    with pytest.raises(ValidationError) as excinfo:
        ProtocolVersionRef(
            study_id="STUDY-001",
            version_tag="  \t  ",
            version_index=1,
            status="DRAFT",
        )
    assert "Version tag cannot be empty" in str(excinfo.value)


def test_protocol_version_ref_validation_index():
    """
    Verify that the version_index must be >= 1 and rejects 0 or negative values.
    """
    # Index 0
    with pytest.raises(ValidationError) as excinfo:
        ProtocolVersionRef(
            study_id="STUDY-001",
            version_tag="1.0",
            version_index=0,
            status="DRAFT",
        )
    assert "Version index must be a positive integer >= 1" in str(excinfo.value)

    # Negative index
    with pytest.raises(ValidationError) as excinfo:
        ProtocolVersionRef(
            study_id="STUDY-001",
            version_tag="1.0",
            version_index=-5,
            status="DRAFT",
        )
    assert "Version index must be a positive integer >= 1" in str(excinfo.value)


def test_protocol_version_ref_validation_status():
    """
    Verify that only valid ProtocolVersionStatus values are accepted.
    """
    # Invalid status string
    with pytest.raises(ValidationError) as excinfo:
        ProtocolVersionRef(
            study_id="STUDY-001",
            version_tag="1.0",
            version_index=1,
            status="UNKNOWN_STATUS",
        )
    assert "Input should be" in str(excinfo.value)


@pytest.mark.parametrize(
    "status_val", ["DRAFT", "ACTIVE", "LOCKED", "PUBLISHED", "ARCHIVED", "FROZEN"]
)
def test_protocol_version_ref_accepted_statuses(status_val):
    """
    Verify all standard statuses in the enum are correctly validated and accepted.
    """
    model = ProtocolVersionRef(
        study_id="STUDY-001",
        version_tag="1.0",
        version_index=1,
        status=status_val,
    )
    assert model.status == status_val


def test_protocol_version_ref_serialization():
    """
    Verify serialization to/from JSON to satisfy cross-service payload usage.
    """
    original_model = ProtocolVersionRef(
        study_id="STUDY-999",
        version_tag="v3.1.2",
        version_index=42,
        status="LOCKED",
    )

    # Serialize to JSON string
    json_str = original_model.model_dump_json()

    # Parse JSON back to dict
    data = json.loads(json_str)
    assert data["study_id"] == "STUDY-999"
    assert data["version_tag"] == "v3.1.2"
    assert data["version_index"] == 42
    assert data["status"] == "LOCKED"

    # Deserialize back to model
    deserialized_model = ProtocolVersionRef.model_validate_json(json_str)
    assert deserialized_model == original_model
