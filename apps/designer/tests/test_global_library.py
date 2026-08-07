"""
Unit Tests for Global Library Domain Contracts.

Verifies that the Pydantic request and response models enforce GxP-compliant metadata,
strict discriminated union payload structure validation, non-empty change reasons,
and accurate clinical layout representations.
"""

import pytest
from pydantic import TypeAdapter, ValidationError

from apps.designer.library import (
    CreateLibraryObjectRequest,
    DataElementPayload,
    LibraryObjectDetail,
    LibraryStatus,
    ObjectType,
    UpdateLibraryObjectRequest,
)


def test_valid_form_detail_validation():
    """
    Test successful validation of a FORM library object response.
    # @req:PRD-MDR-001
    """
    form_data = {
        "id": "lib_form_vitals",
        "version": "1.0.0",
        "status": "DRAFT",
        "sponsor_id": "spon_oncology",
        "tenant_id": "tenant_001",
        "created_at": "2026-07-29T12:00:00Z",
        "created_by": "usr_designer_01",
        "object_type": "FORM",
        "payload": {
            "items": [
                {
                    "item_id": "item_vitals_sbp",
                    "name": "VSSBP",
                    "question_text": "Systolic Blood Pressure (mmHg)",
                    "data_type": "integer",
                    "required": True,
                }
            ]
        },
    }

    # Verify we can parse it via the discriminated union TypeAdapter
    adapter = TypeAdapter(LibraryObjectDetail)
    obj = adapter.validate_python(form_data)

    assert obj.object_type == ObjectType.FORM
    assert obj.id == "lib_form_vitals"
    assert obj.version == "1.0.0"
    assert obj.status == LibraryStatus.DRAFT
    assert obj.sponsor_id == "spon_oncology"
    assert obj.tenant_id == "tenant_001"
    assert len(obj.payload.items) == 1
    assert obj.payload.items[0].item_id == "item_vitals_sbp"
    assert obj.payload.items[0].name == "VSSBP"


def test_valid_data_element_detail_validation():
    """
    Test successful validation of a DATA_ELEMENT library object response.
    # @req:PRD-MDR-001
    """
    de_data = {
        "id": "lib_de_weight",
        "version": "1.1.0",
        "status": "APPROVED",
        "sponsor_id": "spon_pediatrics",
        "tenant_id": "tenant_002",
        "created_at": "2026-07-29T12:00:00Z",
        "created_by": "usr_designer_01",
        "reason_for_change": "Added standard pediatric units.",
        "object_type": "DATA_ELEMENT",
        "payload": {
            "data_type": "numeric",
            "allowable_units": ["kg", "lb"],
            "default_unit": "kg",
        },
    }

    adapter = TypeAdapter(LibraryObjectDetail)
    obj = adapter.validate_python(de_data)

    assert obj.object_type == ObjectType.DATA_ELEMENT
    assert obj.id == "lib_de_weight"
    assert obj.reason_for_change == "Added standard pediatric units."
    assert obj.payload.data_type == "numeric"
    assert "kg" in obj.payload.allowable_units
    assert obj.payload.default_unit == "kg"


def test_valid_arm_detail_validation():
    """
    Test successful validation of an ARM library object response.
    # @req:PRD-MDR-001
    """
    arm_data = {
        "id": "lib_arm_treatment_a",
        "version": "2.0.1",
        "status": "APPROVED",
        "sponsor_id": "spon_oncology",
        "tenant_id": "tenant_001",
        "created_at": "2026-07-29T12:00:00Z",
        "created_by": "usr_designer_01",
        "object_type": "ARM",
        "payload": {
            "attributes": {
                "arm_type": "TREATMENT",
                "target_sample_size": 150,
                "randomization_ratio": "1:1",
            }
        },
    }

    adapter = TypeAdapter(LibraryObjectDetail)
    obj = adapter.validate_python(arm_data)

    assert obj.object_type == ObjectType.ARM
    assert obj.payload.attributes.arm_type == "TREATMENT"
    assert obj.payload.attributes.target_sample_size == 150
    assert obj.payload.attributes.randomization_ratio == "1:1"


def test_valid_visit_detail_validation():
    """
    Test successful validation of a VISIT library object response.
    # @req:PRD-MDR-001
    """
    visit_data = {
        "id": "lib_visit_screening",
        "version": "1.0.0",
        "status": "APPROVED",
        "sponsor_id": "spon_oncology",
        "tenant_id": "tenant_001",
        "created_at": "2026-07-29T12:00:00Z",
        "created_by": "usr_designer_01",
        "object_type": "VISIT",
        "payload": {
            "attributes": {
                "visit_type": "SCREENING",
                "planned_day": 0,
                "window_days": 2,
            }
        },
    }

    adapter = TypeAdapter(LibraryObjectDetail)
    obj = adapter.validate_python(visit_data)

    assert obj.object_type == ObjectType.VISIT
    assert obj.payload.attributes.visit_type == "SCREENING"
    assert obj.payload.attributes.planned_day == 0
    assert obj.payload.attributes.window_days == 2


def test_invalid_mismatched_type_payload_fails():
    """
    Test that mismatched object-type and payload combinations fail validation.
    # @req:PRD-MDR-001
    """
    # Ex: object_type is FORM, but payload belongs to ARM
    mismatched_data = {
        "id": "lib_form_invalid",
        "version": "1.0.0",
        "status": "DRAFT",
        "sponsor_id": "spon_oncology",
        "tenant_id": "tenant_001",
        "created_at": "2026-07-29T12:00:00Z",
        "created_by": "usr_designer_01",
        "object_type": "FORM",
        "payload": {
            "attributes": {
                "arm_type": "TREATMENT",
                "target_sample_size": 150,
                "randomization_ratio": "1:1",
            }
        },
    }

    adapter = TypeAdapter(LibraryObjectDetail)
    with pytest.raises(ValidationError) as exc_info:
        adapter.validate_python(mismatched_data)

    # Check that validation specifically identified payload issues
    assert "payload" in str(exc_info.value) or "items" in str(exc_info.value)


def test_invalid_data_element_default_unit_fails():
    """
    Test that data-element validation fails if default_unit is not in allowable_units.
    # @req:PRD-MDR-001
    """
    invalid_de_payload = {
        "data_type": "numeric",
        "allowable_units": ["kg", "lb"],
        "default_unit": "inches",  # Not in allowable_units
    }

    with pytest.raises(ValidationError) as exc_info:
        DataElementPayload.model_validate(invalid_de_payload)

    assert "default_unit" in str(exc_info.value)
    assert "one of the allowable_units" in str(exc_info.value)


def test_mutation_creation_requires_non_empty_change_reason():
    """
    Verify that all creation requests enforce a non-empty change reason.
    # @req:PRD-MDR-001
    """
    valid_request_data = {
        "id": "lib_form_vitals",
        "version": "1.0.0",
        "status": "DRAFT",
        "sponsor_id": "spon_oncology",
        "object_type": "FORM",
        "change_reason": "Initial drafting of baseline vital signs template.",
        "payload": {
            "items": [
                {
                    "item_id": "item_sbp",
                    "name": "VSSBP",
                    "question_text": "Systolic BP",
                    "data_type": "integer",
                }
            ]
        },
    }

    # Verify valid parses successfully
    adapter = TypeAdapter(CreateLibraryObjectRequest)
    obj = adapter.validate_python(valid_request_data)
    assert obj.change_reason == "Initial drafting of baseline vital signs template."

    # Test completely missing change_reason
    missing_reason = valid_request_data.copy()
    missing_reason.pop("change_reason")
    with pytest.raises(ValidationError) as exc_info:
        adapter.validate_python(missing_reason)
    assert "Field required" in str(exc_info.value) or "change_reason" in str(
        exc_info.value
    )

    # Test empty change_reason
    empty_reason = valid_request_data.copy()
    empty_reason["change_reason"] = ""
    with pytest.raises(ValidationError) as exc_info:
        adapter.validate_python(empty_reason)
    assert "Change reason cannot be empty" in str(exc_info.value)

    # Test blank (whitespace only) change_reason
    blank_reason = valid_request_data.copy()
    blank_reason["change_reason"] = "    \n   "
    with pytest.raises(ValidationError) as exc_info:
        adapter.validate_python(blank_reason)
    assert "Change reason cannot be empty" in str(exc_info.value)


def test_mutation_update_requires_non_empty_reason_for_change():
    """
    Verify that all update requests enforce a non-empty reason_for_change.
    # @req:PRD-MDR-001
    """
    valid_update_data = {
        "object_type": "FORM",
        "reason_for_change": "Updating vital sign layout to match CDASH 2.0 specifications.",
        "payload": {
            "items": [
                {
                    "item_id": "item_sbp",
                    "name": "VSSBP",
                    "question_text": "Systolic BP (mmHg)",
                    "data_type": "integer",
                }
            ]
        },
    }

    adapter = TypeAdapter(UpdateLibraryObjectRequest)
    obj = adapter.validate_python(valid_update_data)
    assert (
        obj.reason_for_change
        == "Updating vital sign layout to match CDASH 2.0 specifications."
    )

    # Test completely missing reason_for_change
    missing_reason = valid_update_data.copy()
    missing_reason.pop("reason_for_change")
    with pytest.raises(ValidationError) as exc_info:
        adapter.validate_python(missing_reason)
    assert "Field required" in str(exc_info.value) or "reason_for_change" in str(
        exc_info.value
    )

    # Test empty reason_for_change
    empty_reason = valid_update_data.copy()
    empty_reason["reason_for_change"] = ""
    with pytest.raises(ValidationError) as exc_info:
        adapter.validate_python(empty_reason)
    assert "Change reason cannot be empty" in str(exc_info.value)

    # Test blank (whitespace only) reason_for_change
    blank_reason = valid_update_data.copy()
    blank_reason["reason_for_change"] = "\t   "
    with pytest.raises(ValidationError) as exc_info:
        adapter.validate_python(blank_reason)
    assert "Change reason cannot be empty" in str(exc_info.value)
