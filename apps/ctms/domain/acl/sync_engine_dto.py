from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, Field

from packages.security.signing import verify_canonical_signature


class CTMSSignatureValidationError(ValueError):
    pass


class CTMSSyncMetadataDTO(BaseModel):
    timestamps: dict[str, datetime] = Field(
        default_factory=dict,
        description="Per-field UTC timestamps indicating when each field was modified.",
    )
    modified_by: str = Field(
        ...,
        description="The identity, device, or user that modified this record.",
    )
    signature: str | None = Field(
        None,
        description="HMAC-SHA256 signature of the payload for cryptographic integrity.",
    )

    def __getitem__(self, item: str) -> Any:
        return getattr(self, item)


class CTMSSyncRecordDTO(BaseModel):
    deduplication_key: str = Field(
        ...,
        description="Natural deduplication key (e.g. 'STUDY-01:SITE-01:VISIT-101').",
    )
    data: dict[str, Any] = Field(..., description="Record payload key-values.")
    metadata: CTMSSyncMetadataDTO = Field(
        ...,
        description="Sync metadata containing timestamps and cryptographic signature.",
    )

    def __getitem__(self, item: str) -> Any:
        return getattr(self, item)


class CTMSSyncReconciliationResultDTO(BaseModel):
    data: dict[str, Any] = Field(..., description="Reconciled record data dictionary.")
    metadata: CTMSSyncMetadataDTO = Field(..., description="Reconciled sync metadata.")
    status: str = Field(
        ...,
        description="Outcome status (e.g. 'CREATED', 'UPDATED_CLIENT_WINS', 'MERGED').",
    )

    def __getitem__(self, item: str) -> Any:
        return getattr(self, item)


def normalize_to_utc(dt: datetime) -> datetime:
    if dt.tzinfo is None:
        return dt.replace(tzinfo=UTC)
    return dt.astimezone(UTC)


def get_ctms_signature_payload(record: CTMSSyncRecordDTO) -> dict[str, Any]:
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


def verify_ctms_record_signature(record: CTMSSyncRecordDTO, secret: bytes) -> bool:
    if not record.metadata.signature:
        return False
    payload = get_ctms_signature_payload(record)
    return verify_canonical_signature(payload, record.metadata.signature, secret)


def reconcile_ctms_records(
    existing_data: dict[str, Any],
    existing_metadata: CTMSSyncMetadataDTO | None,
    incoming_record: CTMSSyncRecordDTO,
    strategy: str,
    secret: bytes | None = None,
    require_signature: bool = False,
) -> CTMSSyncReconciliationResultDTO:
    if require_signature or incoming_record.metadata.signature is not None:
        if not secret:
            raise CTMSSignatureValidationError(
                "A secret must be provided for signature verification."
            )
        if not incoming_record.metadata.signature:
            raise CTMSSignatureValidationError(
                "Required signature is missing from the incoming record."
            )
        if not verify_ctms_record_signature(incoming_record, secret):
            raise CTMSSignatureValidationError(
                "Invalid signature on the incoming record."
            )

    strategy_upper = strategy.upper()
    if strategy_upper not in ("CLIENT_WINS", "SERVER_WINS", "MERGE"):
        strategy_upper = "CLIENT_WINS"

    if not existing_data:
        return CTMSSyncReconciliationResultDTO(
            data=incoming_record.data,
            metadata=incoming_record.metadata,
            status="CREATED",
        )

    if strategy_upper == "CLIENT_WINS":
        return CTMSSyncReconciliationResultDTO(
            data=incoming_record.data,
            metadata=incoming_record.metadata,
            status="UPDATED_CLIENT_WINS",
        )

    if strategy_upper == "SERVER_WINS":
        fallback_meta = existing_metadata or CTMSSyncMetadataDTO(
            timestamps={k: datetime(1970, 1, 1, tzinfo=UTC) for k in existing_data},
            modified_by="server",
        )
        return CTMSSyncReconciliationResultDTO(
            data=existing_data,
            metadata=fallback_meta,
            status="IGNORED_SERVER_WINS",
        )

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
            merged_timestamps[key] = (
                normalize_to_utc(existing_metadata.timestamps[key])
                if (existing_metadata and key in existing_metadata.timestamps)
                else epoch
            )
        elif in_incoming and not in_existing:
            merged_data[key] = incoming_record.data[key]
            merged_timestamps[key] = (
                normalize_to_utc(incoming_record.metadata.timestamps[key])
                if key in incoming_record.metadata.timestamps
                else epoch
            )
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

    merged_meta = CTMSSyncMetadataDTO(
        timestamps=merged_timestamps,
        modified_by=incoming_record.metadata.modified_by,
    )
    return CTMSSyncReconciliationResultDTO(
        data=merged_data,
        metadata=merged_meta,
        status="MERGED",
    )
