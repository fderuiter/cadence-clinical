import logging
from typing import TypedDict

from apps.execution.database.context import audit_context
from apps.execution.database.models import ClinicalObservation

logger = logging.getLogger("execution-notification-events")


class LabAlertPayload(TypedDict):
    category: str
    priority: str
    message_content: str
    related_entity_id: str
    related_entity_type: str
    related_entity_subject_id: str
    recipient_user_id: str
    recipients: list[str]


def generate_critical_lab_notification_payload(
    observation: ClinicalObservation,
    lab_indicator: str,
) -> LabAlertPayload:
    """
    Generates a notification payload dict for critical lab breaches.

    Args:
        observation: The clinical observation model instance.
        lab_indicator: The critical indicator value (e.g. LOW LOW, HIGH HIGH).

    Returns:
        LabAlertPayload: Strongly typed notification payload dict.
    """
    pi_email = (
        f"pi_{observation.site_id}@cadence.clinical"
        if observation.site_id
        else "pi@cadence.clinical"
    )
    cra_email = (
        f"cra_{observation.study_id}@cadence.clinical"
        if observation.study_id
        else "cra@cadence.clinical"
    )

    message = (
        f"Critical lab value detected: {lab_indicator} for subject "
        f"{observation.subject_id}, test code {observation.test_code}."
    )

    return {
        "category": "ALERTS",
        "priority": "CRITICAL",
        "message_content": message,
        "related_entity_id": observation.id,
        "related_entity_type": "lab-observation",
        "related_entity_subject_id": observation.subject_id,
        "recipient_user_id": pi_email,
        "recipients": [pi_email, cra_email],
    }


async def publish_notification_background(
    payload: dict,
    user_id: str | None = None,
    change_reason: str | None = None,
) -> None:
    """
    Background task wrapper to publish a notification while preserving GxP context.

    Args:
        payload: The notification payload to publish.
        user_id: Optional GxP user ID.
        change_reason: Optional GxP change reason.
    """
    from apps.execution.notifications_client import publish_notification

    with audit_context(user_id, change_reason):
        await publish_notification(payload)
