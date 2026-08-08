import logging
from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, Field

from packages.security.signing import verify_canonical_signature

logger = logging.getLogger(__name__)


class SignatureValidationError(ValueError):
    """Exception raised when signature validation fails or is missing when required."""

    pass


class SyncMetadata(BaseModel):
    """
    Conflict resolution and validation metadata for a record.
    """

    timestamps: dict[str, datetime] = Field(
        default_factory=dict,
        description="Per-field UTC timestamps indicating when each field in 'data' was modified",
    )
    modified_by: str = Field(
        ...,
        description="The identity/device/user that modified this record, used for tiebreaking",
    )
    signature: str | None = Field(
        None,
        description="HMAC-SHA256 signature of the payload for cryptographic integrity",
    )


class SyncRecord(BaseModel):
    """
    A domain-agnostic synchronization record structure.
    """

    deduplication_key: str = Field(
        ...,
        description="Caller-supplied natural deduplication key (e.g. subject_id:diary_id)",
    )
    data: dict[str, Any] = Field(..., description="The record data key-values")
    metadata: SyncMetadata = Field(
        ..., description="Conflict resolution and validation metadata"
    )


def normalize_to_utc(dt: datetime) -> datetime:
    """Normalizes a datetime object to timezone-aware UTC."""
    if dt.tzinfo is None:
        return dt.replace(tzinfo=UTC)
    return dt.astimezone(UTC)


def get_signature_payload(record: SyncRecord) -> dict[str, Any]:
    """
    Constructs the canonical signature payload by serializing datetimes to ISO-8601 strings
    and omitting the signature field.
    """
    timestamps_dict = {}
    for k, v in record.metadata.timestamps.items():
        if isinstance(v, datetime):
            timestamps_dict[k] = normalize_to_utc(v).isoformat()
        else:
            timestamps_dict[k] = str(v)

    return {
        "deduplication_key": record.deduplication_key,
        "data": record.data,
        "metadata": {
            "timestamps": timestamps_dict,
            "modified_by": record.metadata.modified_by,
        },
    }


def verify_record_signature(record: SyncRecord, secret: bytes) -> bool:
    """
    Verifies that the provided HMAC-SHA256 signature matches the canonically serialized JSON payload.
    """
    if not record.metadata.signature:
        return False
    payload = get_signature_payload(record)
    return verify_canonical_signature(payload, record.metadata.signature, secret)


def reconcile_records(
    existing_data: dict[str, Any],
    existing_metadata: SyncMetadata | None,
    incoming_record: SyncRecord,
    strategy: str,
    secret: bytes | None = None,
    require_signature: bool = False,
) -> dict[str, Any]:
    """
    Reconciles existing data/metadata and an incoming SyncRecord based on the selected conflict strategy.
    """
    if require_signature or incoming_record.metadata.signature is not None:
        if not secret:
            raise SignatureValidationError(
                "A secret must be provided for signature verification."
            )
        if not incoming_record.metadata.signature:
            raise SignatureValidationError(
                "Required signature is missing from the incoming record."
            )
        if not verify_record_signature(incoming_record, secret):
            raise SignatureValidationError("Invalid signature on the incoming record.")

    strategy_upper = strategy.upper()
    if strategy_upper not in ("CLIENT_WINS", "SERVER_WINS", "MERGE"):
        strategy_upper = "CLIENT_WINS"

    if not existing_data:
        return {
            "data": incoming_record.data,
            "metadata": incoming_record.metadata,
            "status": "CREATED",
        }

    if strategy_upper == "CLIENT_WINS":
        return {
            "data": incoming_record.data,
            "metadata": incoming_record.metadata,
            "status": "UPDATED_CLIENT_WINS",
        }

    if strategy_upper == "SERVER_WINS":
        fallback_metadata = existing_metadata or SyncMetadata(
            timestamps={k: datetime(1970, 1, 1, tzinfo=UTC) for k in existing_data},
            modified_by="server",
        )
        return {
            "data": existing_data,
            "metadata": fallback_metadata,
            "status": "IGNORED_SERVER_WINS",
        }

    if strategy_upper == "MERGE":
        merged_data = {}
        merged_timestamps: dict[str, datetime] = {}
        existing_m_by = existing_metadata.modified_by if existing_metadata else "server"
        epoch = datetime(1970, 1, 1, tzinfo=UTC)

        all_keys = set(existing_data.keys()).union(incoming_record.data.keys())

        for key in all_keys:
            in_existing = key in existing_data
            in_incoming = key in incoming_record.data

            if in_existing and not in_incoming:
                merged_data[key] = existing_data[key]
                if existing_metadata and key in existing_metadata.timestamps:
                    merged_timestamps[key] = normalize_to_utc(
                        existing_metadata.timestamps[key]
                    )
                else:
                    merged_timestamps[key] = epoch

            elif in_incoming and not in_existing:
                merged_data[key] = incoming_record.data[key]
                if key in incoming_record.metadata.timestamps:
                    merged_timestamps[key] = normalize_to_utc(
                        incoming_record.metadata.timestamps[key]
                    )
                else:
                    merged_timestamps[key] = epoch

            else:
                t_exist = (
                    normalize_to_utc(existing_metadata.timestamps[key])
                    if (existing_metadata and key in existing_metadata.timestamps)
                    else epoch
                )
                t_inc = (
                    normalize_to_utc(incoming_record.metadata.timestamps[key])
                    if key in incoming_record.metadata.timestamps
                    else epoch
                )

                if t_inc > t_exist:
                    merged_data[key] = incoming_record.data[key]
                    merged_timestamps[key] = t_inc
                elif t_inc < t_exist:
                    merged_data[key] = existing_data[key]
                    merged_timestamps[key] = t_exist
                else:
                    m_exist = existing_m_by
                    m_inc = incoming_record.metadata.modified_by
                    if m_inc > m_exist:
                        merged_data[key] = incoming_record.data[key]
                        merged_timestamps[key] = t_inc
                    else:
                        merged_data[key] = existing_data[key]
                        merged_timestamps[key] = t_exist

        merged_metadata = SyncMetadata(
            timestamps=merged_timestamps,
            modified_by=incoming_record.metadata.modified_by,
        )
        return {
            "data": merged_data,
            "metadata": merged_metadata,
            "status": "MERGED",
        }

    return {
        "data": incoming_record.data,
        "metadata": incoming_record.metadata,
        "status": "ERROR",
    }
