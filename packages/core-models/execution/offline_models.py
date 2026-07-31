"""Pydantic data models for Offline Sync batch delta ingestion.

Requirements: PRD-SYS-001
"""

from typing import Any, Dict, List, Literal

from pydantic import BaseModel, Field


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
    payload: Dict[str, Any] = Field(..., description="Payload data for the entity")
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
    deltas: List[OfflineDeltaItem] = Field(
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
    conflicts: List[Dict[str, Any]] = Field(
        default_factory=list,
        description="List of conflicts encountered during processing",
    )
