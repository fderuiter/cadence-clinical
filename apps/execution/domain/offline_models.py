"""Pydantic data models for Offline Sync batch delta ingestion and ePRO offline sync contracts.

Requirements: PRD-SYS-001
"""

from datetime import datetime
from enum import StrEnum
from typing import Any, Literal, TypeAlias

from pydantic import BaseModel, Field

# Recursive JSON value type representation
JsonValue: TypeAlias = (  # noqa: UP040
    str | int | float | bool | None | list[Any] | dict[str, Any]
)

# Submission status for ePRO reconciliation
EPROSubmissionStatus = Literal[
    "CREATED",
    "UPDATED_CLIENT_WINS",
    "MERGED",
    "IGNORED_SERVER_WINS",
    "STRUCTURAL_CONFLICT",
]


class OfflineDeltaItem(BaseModel):
    """An individual sync delta item representing an entity mutation.

    Requirements: PRD-SYS-001
    """

    delta_id: str = Field(..., description="Unique delta identifier")
    entity_type: str = Field(..., description="Type of mutated entity")
    entity_id: str = Field(..., description="Unique ID of mutated entity")
    action: Literal["CREATE", "UPDATE", "SUBMIT"] = Field(
        ..., description="Mutation action type"
    )
    payload: dict[str, Any] = Field(..., description="Payload data for the entity")
    client_timestamp_utc: str = Field(
        ..., description="UTC timestamp of the mutation on client"
    )
    reason_for_change: str = Field(..., description="Reason for the mutation change")


class OfflineBatchSyncRequest(BaseModel):
    """Request schema for offline batch delta sync.

    Requirements: PRD-SYS-001
    """

    client_batch_id: str = Field(
        ..., description="Unique client-supplied batch identifier for idempotency"
    )
    device_id: str = Field(
        ..., description="Identifier of the device performing the sync"
    )
    deltas: list[OfflineDeltaItem] = Field(
        ..., description="List of sync deltas to process"
    )


class OfflineBatchSyncResponse(BaseModel):
    """Response schema for offline batch delta sync.

    Requirements: PRD-SYS-001
    """

    client_batch_id: str = Field(
        ..., description="Unique client-supplied batch identifier"
    )
    status: Literal["SUCCESS", "PARTIAL_SUCCESS", "ALREADY_PROCESSED"] = Field(
        ..., description="Processing status of the batch"
    )
    processed_count: int = Field(
        ..., description="Number of successfully processed deltas"
    )
    conflicts: list[dict[str, Any]] = Field(
        default_factory=list,
        description="List of conflicts encountered during processing",
    )


class ConflictStrategyEnum(StrEnum):
    """Explicit validated conflict resolution strategies.

    Requirements: PRD-SYS-001
    """

    CLIENT_WINS = "CLIENT_WINS"
    SERVER_WINS = "SERVER_WINS"
    MERGE = "MERGE"


class EPROOfflineMarker(BaseModel):
    """Offline queue reconciliation and conflict resolution parameters.

    Requirements: PRD-SYS-001
    """

    sequence_number: int = Field(
        ..., description="The queue order sequence from device"
    )
    client_id: str = Field(..., description="Unique identifier for the mobile device")
    conflict_strategy: ConflictStrategyEnum = Field(
        ConflictStrategyEnum.CLIENT_WINS,
        description="Conflict strategy to resolve duplicate submissions",
    )
    signature: str | None = Field(
        None, description="Optional HMAC-SHA256 signature of the payload for integrity"
    )
    timestamps: dict[str, datetime] | None = Field(
        None, description="Optional per-field UTC timestamps"
    )


class OfflineSyncMarkers(BaseModel):
    """Offline queue reconciliation and conflict resolution parameters.

    Requirements: PRD-SYS-001
    """

    sequence_number: int = Field(
        ..., description="The queue order sequence from device"
    )
    client_id: str = Field(..., description="Unique identifier for the mobile device")
    conflict_strategy: Literal["CLIENT_WINS", "SERVER_WINS", "MERGE"] = Field(
        ...,
        description="Conflict strategy to resolve duplicate submissions",
    )
    signature: str | None = Field(
        None, description="Optional HMAC-SHA256 signature of the payload for integrity"
    )
    timestamps: dict[str, datetime] | None = Field(
        None, description="Optional per-field UTC timestamps"
    )


class EPROOfflineEntry(BaseModel):
    """A single participant ePRO/eCOA diary submission.

    Requirements: PRD-SYS-001
    """

    subject_id: str = Field(..., description="Pseudonymized identifier of the subject")
    diary_id: str = Field(..., description="Unique identifier for the diary or survey")
    device_timestamp: datetime = Field(
        ..., description="ISO 8601 timestamp when the entry was created on device"
    )
    answers: dict[str, Any] = Field(
        ..., description="The questionnaire response key-values"
    )
    offline_sync_markers: EPROOfflineMarker = Field(
        ..., description="The offline sync queue conflict tracking parameters"
    )


class EPROBulkSyncRequest(BaseModel):
    """A bulk list of ePRO submissions for offline queue reconciliation.

    Requirements: PRD-SYS-001
    """

    submissions: list[EPROOfflineEntry] = Field(
        ..., description="A list of queued ePRO submissions"
    )


class EPROSubmitResponse(BaseModel):
    """Response payload for a single participant ePRO submission.

    Requirements: PRD-SYS-001
    """

    status: str = Field(..., description="Sync resolution status")
    id: str | None = Field(None, description="Unique record identifier")
    subject_id: str | None = Field(
        None, description="Pseudonymized identifier of the subject"
    )
    diary_id: str | None = Field(
        None, description="Unique identifier for the diary or survey"
    )
    answers: dict[str, Any] | None = Field(
        None, description="The questionnaire response key-values"
    )
    sync_status: str | None = Field(None, description="Sync resolution status")
    version_index: int | None = Field(
        None, description="The current version of the record"
    )
    query: dict[str, Any] | None = Field(
        None, description="Optional clinical query detail"
    )
    signature_validation: dict[str, Any] | None = Field(
        None, description="Signature validation status"
    )
    reconciliation_result: dict[str, Any] | None = Field(
        None, description="Reconciliation details"
    )
    audit_details: dict[str, Any] | None = Field(
        None, description="Audit entry details"
    )
    offline_sync_markers: EPROOfflineMarker | None = Field(
        None, description="Sync markers including sequence_number and client_id"
    )


class EPROBulkSyncResponse(BaseModel):
    """Response payload for bulk list of ePRO offline sync submissions.

    Requirements: PRD-SYS-001
    """

    status: str = Field(..., description="Bulk processing overall status")
    processed_count: int = Field(..., description="Number of submissions processed")
    created_count: int = Field(..., description="Number of created records")
    updated_count: int = Field(..., description="Number of updated records")
    ignored_count: int = Field(..., description="Number of ignored records")
    conflict_count: int = Field(..., description="Number of conflicts encountered")
    results: list[EPROSubmitResponse] = Field(
        ..., description="Per-item processing results"
    )


class EPROPersistedEntryResponse(BaseModel):
    """Persisted ePRO entry response schema representing GxP-compliant metadata.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique record identifier")
    subject_id: str = Field(..., description="Pseudonymized identifier of the subject")
    diary_id: str = Field(..., description="Unique identifier for the diary or survey")
    device_timestamp: datetime = Field(
        ..., description="ISO 8601 timestamp when the entry was created on device"
    )
    answers: dict[str, Any] = Field(
        ..., description="The questionnaire response key-values"
    )
    sync_status: str = Field(..., description="Sync resolution status")
    # Mandatory GxP Audit Fields
    created_at: datetime = Field(
        ..., description="The timestamp when the record was created"
    )
    created_by: str = Field(
        ..., description="The identity of the user who created the record"
    )
    reason_for_change: str = Field(
        ..., description="The 21 CFR Part 11 reason for change"
    )
    version_index: int = Field(..., description="The current version of the record")


class EPROSubmissionRequest(BaseModel):
    """Submission payload request model mirroring EPROSubmissionPayload.

    Requirements: PRD-SYS-001
    """

    subject_id: str = Field(..., description="Pseudonymized identifier of the subject")
    diary_id: str = Field(..., description="Unique identifier for the diary or survey")
    device_timestamp: datetime = Field(
        ..., description="ISO 8601 timestamp when the entry was created on device"
    )
    answers: dict[str, JsonValue] = Field(
        ..., description="The questionnaire response key-values"
    )
    offline_sync_markers: OfflineSyncMarkers = Field(
        ..., description="The offline sync queue conflict tracking parameters"
    )


class EPROSubmissionResponse(BaseModel):
    """Response payload schema for ePRO submission reconciliation.

    Requirements: PRD-SYS-001
    """

    status: EPROSubmissionStatus = Field(..., description="Sync reconciliation status")
    id: str | None = Field(None, description="Unique record identifier")
    subject_id: str | None = Field(
        None, description="Pseudonymized identifier of the subject"
    )
    diary_id: str | None = Field(
        None, description="Unique identifier for the diary or survey"
    )
    answers: dict[str, JsonValue] | None = Field(
        None, description="The questionnaire response key-values"
    )
    sync_status: str | None = Field(None, description="Sync resolution status")

    # GxP audit fields
    created_at: datetime | None = Field(
        None, description="The timestamp when the record was created"
    )
    created_by: str | None = Field(
        None, description="The identity of the user who created the record"
    )
    reason_for_change: str | None = Field(
        None, description="The 21 CFR Part 11 reason for change"
    )
    version_index: int | None = Field(
        None, description="The current version of the record"
    )


class SubjectNotificationResponse(BaseModel):
    """Subject notification response schema.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique notification identifier")
    subject_id: str = Field(..., description="Unique subject identifier")
    assignment_id: str | None = Field(
        None, description="Optional associated assignment identifier"
    )
    due_at: datetime = Field(
        ..., description="The timestamp when the assignment is due"
    )
    channel: str = Field(..., description="The channel of delivery")
    delivery_status: str = Field(..., description="The status of delivery")
    is_read: bool = Field(
        ..., description="Flag indicating if the notification is read"
    )
    read_at: datetime | None = Field(
        None, description="The timestamp when the notification was read"
    )
    created_at: datetime = Field(
        ..., description="The timestamp when the record was created"
    )
    created_by: str = Field(
        ..., description="The identity of the user who created the record"
    )
    reason_for_change: str = Field(
        ..., description="The 21 CFR Part 11 reason for change"
    )
    version_index: int = Field(..., description="The current version of the record")


class AcknowledgeNotificationRequest(BaseModel):
    """Request schema for acknowledging a notification.

    Requirements: PRD-SYS-001
    """

    reason_for_change: str = Field(
        ..., description="21 CFR Part 11 compliant reason for change"
    )


class EPROScheduleItemResponse(BaseModel):
    """An ePRO schedule item response model mirroring SubjectAssignment.

    Requirements: PRD-SYS-001
    """

    id: str | None = Field(None, description="Unique schedule item identifier")
    subject_id: str = Field(..., description="Pseudonymized identifier of the subject")
    instrument_id: str = Field(
        ..., description="Unique identifier for the assigned instrument"
    )
    start_date: datetime = Field(..., description="Start of the due/recurrence window")
    end_date: datetime = Field(..., description="End of the due/recurrence window")
    recurrence_pattern: str | None = Field(
        None, description="Optional recurrence pattern like DAILY, WEEKLY"
    )
    due_at: datetime | None = Field(None, description="Optional specific due date/time")

    # GxP audit fields
    created_at: datetime = Field(
        ..., description="The timestamp when the record was created"
    )
    created_by: str = Field(
        ..., description="The identity of the user who created the record"
    )
    reason_for_change: str = Field(
        ..., description="The 21 CFR Part 11 reason for change"
    )
    version_index: int = Field(..., description="The current version of the record")


class EPRODiaryFormDefinitionResponse(BaseModel):
    """A diary form definition response model mirroring Instrument.

    Requirements: PRD-SYS-001
    """

    id: str | None = Field(None, description="Unique instrument identifier")
    name: str = Field(..., description="The name of the questionnaire/diary")
    description: str | None = Field(
        None, description="Optional description of the diary form"
    )
    items: dict[str, JsonValue] = Field(..., description="Items/questions")
    response_types: dict[str, JsonValue] = Field(
        ..., description="Response types and options"
    )
    scoring_metadata: dict[str, JsonValue] = Field(..., description="Scoring metadata")

    # GxP audit fields
    created_at: datetime = Field(
        ..., description="The timestamp when the record was created"
    )
    created_by: str = Field(
        ..., description="The identity of the user who created the record"
    )
    reason_for_change: str = Field(
        ..., description="The 21 CFR Part 11 reason for change"
    )
    version_index: int = Field(..., description="The current version of the record")
