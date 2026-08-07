"""Unit tests for laboratory domain and transport schemas.

Requirements: PRD-SYS-001
"""

from datetime import datetime

import pytest
from execution.lab_models import (
    LabSourceEnum,
    LabTestMasterRecord,
    LabUnitConversionRecord,
)
from execution.lab_transport_models import (
    LabRangeRecalculateRequest,
    LabRangeRecalculateResponse,
    LabReferenceRangeCreate,
    LabReferenceRangeResponse,
    LabReferenceRangeUpdate,
    LabTestMasterCreate,
    LabTestMasterResponse,
    LabUnitConversionCreate,
    LabUnitConversionResponse,
)
from pydantic import ValidationError


def test_lab_source_enum_values():
    """Verify that LabSourceEnum contains the expected members and values."""
    assert LabSourceEnum.CENTRAL == "CENTRAL"
    assert LabSourceEnum.LOCAL == "LOCAL"


def test_lab_test_master_record_valid():
    """Verify successful parsing of LabTestMasterRecord with valid data."""
    data = {
        "id": "master-01",
        "study_id": "STUDY-101",
        "test_code": "HEMOGLOBIN",
        "test_name": "Hemoglobin",
        "default_unit": "g/dL",
        "normalized_unit": "g/L",
        "loinc_code": "718-7",
        "created_at": datetime.now(),
        "created_by": "user_admin",
        "reason_for_change": "Initial catalog definition",
        "version_index": 1,
    }
    record = LabTestMasterRecord(**data)
    assert record.id == "master-01"
    assert record.study_id == "STUDY-101"
    assert record.loinc_code == "718-7"


def test_lab_test_master_record_invalid():
    """Verify that LabTestMasterRecord raises validation errors on invalid data types."""
    data = {
        "id": "master-01",
        "study_id": "STUDY-101",
        "test_code": "HEMOGLOBIN",
        "test_name": "Hemoglobin",
        "default_unit": "g/dL",
        "normalized_unit": "g/L",
        "loinc_code": 12345,  # Invalid type (should be string)
        "created_at": "not-a-datetime",  # Invalid type
        "created_by": "user_admin",
        "reason_for_change": "Initial catalog definition",
        "version_index": "one",  # Invalid type (should be int)
    }
    with pytest.raises(ValidationError):
        LabTestMasterRecord(**data)


def test_lab_unit_conversion_record_valid():
    """Verify successful parsing of LabUnitConversionRecord with valid data."""
    data = {
        "id": "conv-01",
        "study_id": "STUDY-101",
        "test_code": "HEMOGLOBIN",
        "from_unit": "g/dL",
        "to_unit": "g/L",
        "factor": 10.0,
        "offset": None,
        "created_at": datetime.now(),
        "created_by": "user_admin",
        "reason_for_change": "UCUM standard multiplier",
        "version_index": 1,
    }
    record = LabUnitConversionRecord(**data)
    assert record.factor == 10.0
    assert record.offset is None


def test_lab_reference_range_create_valid():
    """Verify successful parsing of LabReferenceRangeCreate payload."""
    data = {
        "study_id": "STUDY-101",
        "test_code": "WBC",
        "test_name": "White Blood Cell Count",
        "source": "CENTRAL",
        "site_id": None,
        "unit": "10^9/L",
        "normalized_unit": "10^9/L",
        "sex_applicability": "ALL",
        "age_low": 18.0,
        "age_high": 120.0,
        "low_bound": 4.5,
        "high_bound": 11.0,
        "critical_low": 2.0,
        "critical_high": 20.0,
        "reason_for_change": "Establishing global study bounds",
    }
    payload = LabReferenceRangeCreate(**data)
    assert payload.study_id == "STUDY-101"
    assert payload.source == LabSourceEnum.CENTRAL
    assert payload.reason_for_change == "Establishing global study bounds"


def test_lab_reference_range_create_invalid_enum():
    """Verify that an invalid source value triggers validation error in LabReferenceRangeCreate."""
    data = {
        "study_id": "STUDY-101",
        "test_code": "WBC",
        "test_name": "White Blood Cell Count",
        "source": "INVALID-SOURCE",  # Must be CENTRAL or LOCAL
        "unit": "10^9/L",
        "normalized_unit": "10^9/L",
        "sex_applicability": "ALL",
        "reason_for_change": "Establishing global study bounds",
    }
    with pytest.raises(ValidationError):
        LabReferenceRangeCreate(**data)


def test_lab_reference_range_response_valid():
    """Verify serialization of LabReferenceRangeResponse with backward-compatible fields."""
    data = {
        "id": "range-01",
        "study_id": "STUDY-101",
        "test_code": "WBC",
        "test_name": "White Blood Cell Count",
        "source": "CENTRAL",
        "site_id": None,
        "unit": "10^9/L",
        "normalized_unit": "10^9/L",
        "sex_applicability": "ALL",
        "age_low": 18.0,
        "age_high": 120.0,
        "low_bound": 4.5,
        "high_bound": 11.0,
        "critical_low": 2.0,
        "critical_high": 20.0,
        "version": 1,
        "is_deleted": False,
    }
    res = LabReferenceRangeResponse(**data)
    assert res.source == LabSourceEnum.CENTRAL
    assert res.sex_applicability == "ALL"
    assert res.low_bound == 4.5
    assert res.high_bound == 11.0


def test_lab_range_recalculate_request_and_response():
    """Verify recalculated requests and responses preserve fields correctly."""
    req_data = {"study_id": "STUDY-101", "test_code": "WBC"}
    req = LabRangeRecalculateRequest(**req_data)
    assert req.study_id == "STUDY-101"

    res_data = {
        "status": "success",
        "study_id": "STUDY-101",
        "test_code": "WBC",
        "updated_count": 42,
    }
    res = LabRangeRecalculateResponse(**res_data)
    assert res.status == "success"
    assert res.updated_count == 42


def test_lab_test_master_create_valid():
    """Verify parsing of LabTestMasterCreate schema."""
    data = {
        "study_id": "STUDY-101",
        "test_code": "ALT",
        "test_name": "Alanine Aminotransferase",
        "default_unit": "U/L",
        "normalized_unit": "U/L",
        "loinc_code": "1742-6",
        "reason_for_change": "Adding liver enzyme",
    }
    create = LabTestMasterCreate(**data)
    assert create.loinc_code == "1742-6"


def test_lab_test_master_response_valid():
    """Verify parsing of LabTestMasterResponse schema."""
    data = {
        "id": "master-02",
        "study_id": "STUDY-101",
        "test_code": "ALT",
        "test_name": "Alanine Aminotransferase",
        "default_unit": "U/L",
        "normalized_unit": "U/L",
        "loinc_code": "1742-6",
        "version_index": 1,
        "version": 1,
        "is_deleted": False,
    }
    res = LabTestMasterResponse(**data)
    assert res.test_code == "ALT"


def test_lab_unit_conversion_create_valid():
    """Verify parsing of LabUnitConversionCreate schema."""
    data = {
        "study_id": "STUDY-101",
        "test_code": "ALT",
        "from_unit": "U/L",
        "to_unit": "U/L",
        "factor": 1.0,
        "offset": None,
        "reason_for_change": "Identity conversion",
    }
    create = LabUnitConversionCreate(**data)
    assert create.factor == 1.0


def test_lab_unit_conversion_response_valid():
    """Verify parsing of LabUnitConversionResponse schema."""
    data = {
        "id": "conv-02",
        "study_id": "STUDY-101",
        "test_code": "ALT",
        "from_unit": "U/L",
        "to_unit": "U/L",
        "factor": 1.0,
        "offset": None,
        "version_index": 1,
        "version": 1,
        "is_deleted": False,
    }
    res = LabUnitConversionResponse(**data)
    assert res.factor == 1.0


def test_lab_reference_range_update_valid():
    """Verify parsing of LabReferenceRangeUpdate schema."""
    data = {
        "test_name": "New descriptive name",
        "reason_for_change": "Refining test name descriptor",
    }
    update = LabReferenceRangeUpdate(**data)
    assert update.test_name == "New descriptive name"
    assert update.reason_for_change == "Refining test name descriptor"
