import pytest
from pydantic import ValidationError
from apps.execution.routers.coding_schemas import (
    CoderActionRequest,
    DictionaryImportRequest,
    DictTypeEnum,
)

def test_coder_action_request_validation_override_happy_path():
    # Correct override request
    req = CoderActionRequest(
        action="OVERRIDE",
        code="12345",
        term="Some AE term",
        reason_for_change="Clinically more appropriate.",
    )
    assert req.action == "OVERRIDE"
    assert req.code == "12345"
    assert req.term == "Some AE term"
    assert req.reason_for_change == "Clinically more appropriate."

def test_coder_action_request_validation_override_blank_fields():
    # Missing code
    with pytest.raises(ValidationError) as exc:
        CoderActionRequest(
            action="OVERRIDE",
            code="",
            term="Some AE term",
            reason_for_change="Clinically more appropriate.",
        )
    assert "code is required and cannot be blank" in str(exc.value)

    # Missing term
    with pytest.raises(ValidationError) as exc:
        CoderActionRequest(
            action="OVERRIDE",
            code="12345",
            term="   ",
            reason_for_change="Clinically more appropriate.",
        )
    assert "term is required and cannot be blank" in str(exc.value)

    # Missing reason_for_change
    with pytest.raises(ValidationError) as exc:
        CoderActionRequest(
            action="OVERRIDE",
            code="12345",
            term="Some AE term",
            reason_for_change=None,
        )
    assert "reason_for_change is required and cannot be blank" in str(exc.value)

def test_coder_action_request_validation_accept():
    # ACCEPT does not require these fields
    req = CoderActionRequest(
        action="ACCEPT",
        suggestion_index=0,
    )
    assert req.action == "ACCEPT"
    assert req.suggestion_index == 0
    assert req.code is None
    assert req.term is None

def test_dictionary_import_request_blank_version():
    with pytest.raises(ValidationError) as exc:
        DictionaryImportRequest(
            dictionary_type=DictTypeEnum.MEDDRA,
            version="  ",
        )
    assert "Version must be a non-empty string." in str(exc.value)
