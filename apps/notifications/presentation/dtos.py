"""Pydantic schemas for Notifications service presentation layer."""

from pydantic import BaseModel, ConfigDict, Field

from apps.notifications.infrastructure.models import (
    NotificationCategory,
    NotificationPriority,
    NotificationStatus,
)


class NotificationCreate(BaseModel):
    recipient_user_id: str | None = Field(None, description="Optional target user ID")
    recipient_role: str | None = Field(None, description="Optional target role")
    category: NotificationCategory = Field(
        ..., description="Category: ALERTS, SYSTEM, ACTION_ITEMS"
    )
    priority: NotificationPriority = Field(
        ..., description="Priority: LOW, MEDIUM, HIGH, CRITICAL"
    )
    channels: str = Field(
        "IN_APP", description="Comma-separated delivery channels (e.g. 'IN_APP,EMAIL')"
    )
    message_content: str = Field(..., description="Message content")
    related_entity_id: str | None = Field(
        None, description="Optional related entity ID"
    )
    related_entity_type: str | None = Field(
        None, description="Optional related entity type"
    )


class NotificationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    recipient_user_id: str | None = None
    recipient_role: str | None = None
    category: NotificationCategory
    priority: NotificationPriority
    channels: str
    message_content: str
    related_entity_id: str | None = None
    related_entity_type: str | None = None
    status: NotificationStatus
    delivery_state: str
    retries: int
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str
