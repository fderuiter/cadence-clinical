"""
Dedicated test suite verifying Centralized Schema-driven Date-Time Validation.
Ensures that:
1. Core models reject timezone-naive inputs.
2. Core models accept timezone-aware inputs (with 'Z' or offset) without pre-processing.
3. No silent fallback to system times.
4. Serialized clinical outputs format UTC timestamps strictly with trailing 'Z'.
"""

from datetime import datetime, timezone

import pytest
from audit import AuditFields
from protocol_authoring.models import Comment
from protocol_render.models import ExportMetadata
from pydantic import ValidationError
from signature import SignatureManifestation, SigningReason


def test_reject_timezone_naive_datetime_objects():
    """Verify that core models reject timezone-naive datetime objects."""
    naive_dt = datetime(2026, 7, 30, 18, 41, 42)

    # 1. AuditFields
    with pytest.raises(ValidationError) as excinfo:
        AuditFields(
            created_at=naive_dt,
            created_by="user1",
            reason_for_change="Initial creation",
        )
    assert "Datetime must be timezone-aware" in str(excinfo.value)

    # 2. SignatureManifestation
    with pytest.raises(ValidationError) as excinfo:
        SignatureManifestation(
            signer_id="signer1",
            timestamp=naive_dt,
            signing_reason=SigningReason.AUTHOR,
            ip_address="127.0.0.1",
            sha256_hash="hash123",
        )
    assert "Datetime must be timezone-aware" in str(excinfo.value)

    # 3. ExportMetadata
    with pytest.raises(ValidationError) as excinfo:
        ExportMetadata(
            creator="creator1",
            timestamp=naive_dt,
        )
    assert "Datetime must be timezone-aware" in str(excinfo.value)


def test_reject_timezone_naive_strings():
    """Verify that core models reject timezone-naive ISO string inputs."""
    naive_str = "2026-07-30T18:41:42"

    with pytest.raises(ValidationError) as excinfo:
        AuditFields(
            created_at=naive_str,
            created_by="user1",
            reason_for_change="Test change",
        )
    assert "Datetime must be timezone-aware" in str(excinfo.value)


def test_accept_timezone_aware_inputs():
    """Verify that both trailing Z and offset formats parse successfully."""
    z_str = "2026-07-30T18:41:42Z"
    offset_str = "2026-07-30T18:41:42-07:00"

    # Z-string should succeed and parse to UTC
    audit_z = AuditFields(
        created_at=z_str,
        created_by="user1",
        reason_for_change="Test Z parsing",
    )
    assert audit_z.created_at == datetime(2026, 7, 30, 18, 41, 42, tzinfo=timezone.utc)

    # Offset string should succeed and convert to UTC
    audit_offset = AuditFields(
        created_at=offset_str,
        created_by="user1",
        reason_for_change="Test offset parsing",
    )
    # 2026-07-30T18:41:42-07:00 is 2026-07-31T01:41:42Z
    assert audit_offset.created_at == datetime(
        2026, 7, 31, 1, 41, 42, tzinfo=timezone.utc
    )


def test_no_silent_fallback_to_system_time():
    """Verify that invalid inputs throw a validation error, blocking any silent fallback."""
    invalid_inputs = ["invalid-date-string", "2026-13-45T99:99:99", ""]

    for bad_input in invalid_inputs:
        with pytest.raises(ValidationError):
            AuditFields(
                created_at=bad_input,
                created_by="user1",
                reason_for_change="Test fallback block",
            )


def test_serialized_clinical_outputs_trailing_z():
    """Verify that serialized outputs format UTC timestamps strictly with trailing Z."""
    # Test on ExportMetadata (clinical dataset/document exports)
    export_metadata = ExportMetadata(
        creator="biostat1",
        timestamp="2026-07-30T18:41:42-07:00",  # parses and converts to UTC
    )

    # In Python, it is a datetime object in UTC
    assert export_metadata.timestamp.tzinfo == timezone.utc

    # Serializing to JSON (e.g. model_dump_json or model_dump(mode='json'))
    json_data = export_metadata.model_dump(mode="json")
    assert json_data["timestamp"] == "2026-07-31T01:41:42Z"

    # Directly in model dump JSON string
    json_str = export_metadata.model_dump_json()
    assert '"timestamp":"2026-07-31T01:41:42Z"' in json_str


def test_pydantic_defaults_are_timezone_aware():
    """Verify that default factories produce timezone-aware datetime values in UTC."""
    # 1. AuditFields
    audit = AuditFields(created_by="user1", reason_for_change="test defaults")
    assert audit.created_at.tzinfo == timezone.utc

    # 2. ExportMetadata
    export_metadata = ExportMetadata(creator="user1")
    assert export_metadata.timestamp.tzinfo == timezone.utc

    # 3. Comment
    comment = Comment(comment_id="c1", thread_id="t1", text="text", created_by="u1")
    assert comment.created_at.tzinfo == timezone.utc
