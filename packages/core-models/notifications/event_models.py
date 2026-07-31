from typing import Any, Dict, List, Literal

from pydantic import BaseModel, Field


class SystemDomainEvent(BaseModel):
    """
    Pydantic v2 schema representing an asynchronous domain event emitted across clinical microservices.
    """

    event_id: str = Field(
        ..., description="Unique UUID or identifier for the domain event."
    )
    event_type: str = Field(
        ...,
        description="Type of clinical or system domain event (e.g., EDC_QUERY_RAISED).",
    )
    source_service: str = Field(
        ..., description="The microservice emitting the event (e.g., edc, etmf)."
    )
    study_id: str = Field(..., description="The associated clinical study/trial ID.")
    payload: Dict[str, Any] = Field(
        default_factory=dict,
        description="Event-specific metadata or structured payload details.",
    )
    timestamp_utc: str = Field(
        ..., description="ISO-8601 UTC timestamp of event generation."
    )


class NotificationDispatchJob(BaseModel):
    """
    Pydantic v2 schema representing a resolved, ready-to-deliver notification dispatch job.
    """

    job_id: str = Field(
        ..., description="Unique UUID for tracking the notification delivery job."
    )
    recipient_user_ids: List[str] = Field(
        ..., description="List of resolved target keycloak user IDs."
    )
    notification_payload: Dict[str, Any] = Field(
        ..., description="Structured content and fields for the notification."
    )
    channels: List[Literal["WEBSOCKET", "EMAIL", "SMS"]] = Field(
        default_factory=lambda: ["WEBSOCKET", "EMAIL"],
        description="Target distribution channels for this job.",
    )
