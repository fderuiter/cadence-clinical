from datetime import datetime, timezone

import pytest

from apps.interop.sync_engine import (
    SignatureValidationError,
    SyncMetadata,
    SyncRecord,
    get_signature_payload,
    reconcile_records,
    verify_record_signature,
)
from packages.security.signing import generate_canonical_signature


def test_strategy_client_wins_no_existing():
    """
    CLIENT_WINS strategy when no existing record exists: incoming should be created.
    """
    # @req:PRD-EDC-008
    incoming = SyncRecord(
        deduplication_key="key_1",
        data={"key": "val"},
        metadata=SyncMetadata(
            timestamps={"key": datetime.now(timezone.utc)}, modified_by="device_1"
        ),
    )

    res = reconcile_records({}, None, incoming, "CLIENT_WINS")
    assert res["status"] == "CREATED"
    assert res["data"] == {"key": "val"}
    assert res["metadata"].modified_by == "device_1"


def test_strategy_client_wins_existing():
    """
    CLIENT_WINS strategy when existing record exists: incoming should overwrite everything.
    """
    # @req:PRD-EDC-008
    existing_data = {"key": "old_val", "other": "keep"}
    existing_metadata = SyncMetadata(
        timestamps={"key": datetime.now(timezone.utc)}, modified_by="device_old"
    )
    incoming = SyncRecord(
        deduplication_key="key_1",
        data={"key": "new_val"},
        metadata=SyncMetadata(
            timestamps={"key": datetime.now(timezone.utc)}, modified_by="device_new"
        ),
    )

    res = reconcile_records(existing_data, existing_metadata, incoming, "CLIENT_WINS")
    assert res["status"] == "UPDATED_CLIENT_WINS"
    assert res["data"] == {"key": "new_val"}
    assert res["metadata"].modified_by == "device_new"


def test_strategy_server_wins():
    """
    SERVER_WINS strategy: existing record is kept; incoming is ignored.
    """
    # @req:PRD-EDC-008
    existing_data = {"key": "old_val"}
    existing_metadata = SyncMetadata(
        timestamps={"key": datetime.now(timezone.utc)}, modified_by="device_old"
    )
    incoming = SyncRecord(
        deduplication_key="key_1",
        data={"key": "new_val"},
        metadata=SyncMetadata(
            timestamps={"key": datetime.now(timezone.utc)}, modified_by="device_new"
        ),
    )

    res = reconcile_records(existing_data, existing_metadata, incoming, "SERVER_WINS")
    assert res["status"] == "IGNORED_SERVER_WINS"
    assert res["data"] == {"key": "old_val"}
    assert res["metadata"].modified_by == "device_old"


def test_strategy_merge_independent_fields():
    """
    MERGE strategy: independent fields from existing and incoming are merged.
    """
    # @req:PRD-EDC-008
    existing_data = {"key_a": "val_a"}
    existing_metadata = SyncMetadata(
        timestamps={"key_a": datetime(2026, 1, 1, tzinfo=timezone.utc)},
        modified_by="device_old",
    )
    incoming = SyncRecord(
        deduplication_key="key_1",
        data={"key_b": "val_b"},
        metadata=SyncMetadata(
            timestamps={"key_b": datetime(2026, 1, 2, tzinfo=timezone.utc)},
            modified_by="device_new",
        ),
    )

    res = reconcile_records(existing_data, existing_metadata, incoming, "MERGE")
    assert res["status"] == "MERGED"
    assert res["data"] == {"key_a": "val_a", "key_b": "val_b"}
    assert res["metadata"].timestamps["key_a"] == datetime(
        2026, 1, 1, tzinfo=timezone.utc
    )
    assert res["metadata"].timestamps["key_b"] == datetime(
        2026, 1, 2, tzinfo=timezone.utc
    )


def test_strategy_merge_lww_incoming_wins():
    """
    MERGE strategy with overlapping fields: incoming has a newer timestamp, so incoming wins.
    """
    # @req:PRD-EDC-008
    existing_data = {"key": "old_val"}
    existing_metadata = SyncMetadata(
        timestamps={"key": datetime(2026, 1, 1, 10, 0, 0, tzinfo=timezone.utc)},
        modified_by="device_old",
    )
    incoming = SyncRecord(
        deduplication_key="key_1",
        data={"key": "new_val"},
        metadata=SyncMetadata(
            timestamps={"key": datetime(2026, 1, 1, 11, 0, 0, tzinfo=timezone.utc)},
            modified_by="device_new",
        ),
    )

    res = reconcile_records(existing_data, existing_metadata, incoming, "MERGE")
    assert res["status"] == "MERGED"
    assert res["data"] == {"key": "new_val"}
    assert res["metadata"].timestamps["key"] == datetime(
        2026, 1, 1, 11, 0, 0, tzinfo=timezone.utc
    )


def test_strategy_merge_lww_existing_wins():
    """
    MERGE strategy with overlapping fields: existing has a newer timestamp, so existing wins.
    """
    # @req:PRD-EDC-008
    existing_data = {"key": "old_val"}
    existing_metadata = SyncMetadata(
        timestamps={"key": datetime(2026, 1, 1, 12, 0, 0, tzinfo=timezone.utc)},
        modified_by="device_old",
    )
    incoming = SyncRecord(
        deduplication_key="key_1",
        data={"key": "new_val"},
        metadata=SyncMetadata(
            timestamps={"key": datetime(2026, 1, 1, 11, 0, 0, tzinfo=timezone.utc)},
            modified_by="device_new",
        ),
    )

    res = reconcile_records(existing_data, existing_metadata, incoming, "MERGE")
    assert res["status"] == "MERGED"
    assert res["data"] == {"key": "old_val"}
    assert res["metadata"].timestamps["key"] == datetime(
        2026, 1, 1, 12, 0, 0, tzinfo=timezone.utc
    )


def test_strategy_merge_lww_timestamp_tie():
    """
    MERGE strategy with overlapping fields: exact timestamps, tiebreaker goes to lexicographically greater modified_by.
    """
    # @req:PRD-EDC-008
    # Case A: Incoming modified_by "device_zzz" > existing modified_by "device_old" -> incoming wins
    existing_data = {"key": "old_val"}
    ts = datetime(2026, 1, 1, 12, 0, 0, tzinfo=timezone.utc)
    existing_metadata = SyncMetadata(timestamps={"key": ts}, modified_by="device_old")
    incoming_wins = SyncRecord(
        deduplication_key="key_1",
        data={"key": "new_val"},
        metadata=SyncMetadata(timestamps={"key": ts}, modified_by="device_zzz"),
    )

    res = reconcile_records(existing_data, existing_metadata, incoming_wins, "MERGE")
    assert res["data"] == {"key": "new_val"}

    # Case B: Incoming modified_by "device_aaa" < existing modified_by "device_old" -> existing wins
    incoming_loses = SyncRecord(
        deduplication_key="key_1",
        data={"key": "new_val"},
        metadata=SyncMetadata(timestamps={"key": ts}, modified_by="device_aaa"),
    )

    res2 = reconcile_records(existing_data, existing_metadata, incoming_loses, "MERGE")
    assert res2["data"] == {"key": "old_val"}


def test_signature_validation_happy_path():
    """
    Verify valid signature verification behaves correctly and doesn't raise errors.
    """
    # @req:PRD-EDC-008
    secret = b"test_secret_key"
    record = SyncRecord(
        deduplication_key="sub_123:diary_abc",
        data={"field_1": "value_1"},
        metadata=SyncMetadata(
            timestamps={"field_1": datetime(2026, 8, 1, tzinfo=timezone.utc)},
            modified_by="device_xyz",
        ),
    )

    # Compute valid canonical signature
    payload = get_signature_payload(record)
    sig = generate_canonical_signature(payload, secret)
    record.metadata.signature = sig

    # Valid check
    assert verify_record_signature(record, secret) is True

    # Reconcile shouldn't throw error
    res = reconcile_records(
        {}, None, record, "CLIENT_WINS", secret=secret, require_signature=True
    )
    assert res["status"] == "CREATED"


def test_signature_validation_failures():
    """
    Verify that invalid, missing, or mismatched signatures raise SignatureValidationError when required.
    """
    # @req:PRD-EDC-008
    secret = b"test_secret_key"
    record = SyncRecord(
        deduplication_key="sub_123:diary_abc",
        data={"field_1": "value_1"},
        metadata=SyncMetadata(
            timestamps={"field_1": datetime(2026, 8, 1, tzinfo=timezone.utc)},
            modified_by="device_xyz",
        ),
    )

    # 1. Signature missing, require_signature is True
    with pytest.raises(SignatureValidationError) as exc_info:
        reconcile_records(
            {}, None, record, "CLIENT_WINS", secret=secret, require_signature=True
        )
    assert "Required signature is missing" in str(exc_info.value)

    # 2. Secret missing but require_signature is True or signature is present
    record.metadata.signature = "some_signature"
    with pytest.raises(SignatureValidationError) as exc_info:
        reconcile_records({}, None, record, "CLIENT_WINS", require_signature=True)
    assert "A secret must be provided" in str(exc_info.value)

    # 3. Signature is present but invalid (secret matches, but signature is wrong)
    with pytest.raises(SignatureValidationError) as exc_info:
        reconcile_records(
            {}, None, record, "CLIENT_WINS", secret=secret, require_signature=False
        )
    assert "Invalid signature" in str(exc_info.value)


def test_generic_natural_deduplication_key():
    """
    Verify generic key-behavior behaves correctly (e.g. site_id:visit_id or trial_id:milestone_id).
    """
    incoming = SyncRecord(
        deduplication_key="site_001:visit_005",
        data={"cra_signature": "signed", "status": "completed"},
        metadata=SyncMetadata(
            timestamps={
                "cra_signature": datetime(2026, 5, 1, tzinfo=timezone.utc),
                "status": datetime(2026, 5, 2, tzinfo=timezone.utc),
            },
            modified_by="cra_john",
        ),
    )

    res = reconcile_records({}, None, incoming, "CLIENT_WINS")
    assert res["status"] == "CREATED"
    assert res["data"]["status"] == "completed"
