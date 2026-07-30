import logging
from typing import List, Optional

from packages.security.context import current_user_id

logger = logging.getLogger("tickets-notification-events")


def generate_ticket_notification_payloads(
    ticket,
    event_type: str,
    old_status: Optional[str] = None,
    new_status: Optional[str] = None,
    comment_body: Optional[str] = None,
) -> List[dict]:
    """
    Generates list of notification payloads based on ticket state, event type, and recipient policy.

    Recipient policy:
    - Target assignee_user when set, else assignee_role.
    - Also notify reporter.
    - Exclude acting principal (current X-User-Id) from recipients.
    - Emit one payload per distinct recipient.
    """
    # Extract current actor
    actor = current_user_id.get()

    recipients = []

    # 1. Target assignee_user when set, else assignee_role
    if ticket.assignee_user:
        if ticket.assignee_user != actor:
            recipients.append(
                {"recipient_user_id": ticket.assignee_user, "recipient_role": None}
            )
    elif ticket.assignee_role:
        recipients.append(
            {"recipient_user_id": None, "recipient_role": ticket.assignee_role}
        )

    # 2. Also notify reporter
    if ticket.reporter and ticket.reporter != actor:
        # Avoid duplicating reporter if reporter is already the assignee_user
        if not any(r["recipient_user_id"] == ticket.reporter for r in recipients):
            recipients.append(
                {"recipient_user_id": ticket.reporter, "recipient_role": None}
            )

    if not recipients:
        logger.info(
            "No eligible recipients found for ticket '%s' event '%s' after actor exclusion (actor: '%s').",
            ticket.id,
            event_type,
            actor,
        )
        return []

    # Map category, priority, and message content per policy
    # Policy:
    # - assignment & comments -> ACTION_ITEMS
    # - status transitions -> SYSTEM
    # - default priority: MEDIUM
    # - default channels: "IN_APP"
    if event_type == "assignment":
        category = "ACTION_ITEMS"
        priority = "MEDIUM"
        message_content = f"Ticket {ticket.reference} has been assigned to {ticket.assignee_user or ticket.assignee_role}."
    elif event_type == "comment":
        category = "ACTION_ITEMS"
        priority = "MEDIUM"
        if comment_body:
            snippet = (
                comment_body[:60] + "..." if len(comment_body) > 60 else comment_body
            )
            message_content = (
                f"New comment added to ticket {ticket.reference}: '{snippet}'."
            )
        else:
            message_content = f"New comment added to ticket {ticket.reference}."
    elif event_type == "transition":
        category = "SYSTEM"
        priority = "MEDIUM"
        if old_status and new_status:
            message_content = f"Ticket {ticket.reference} status transitioned from {old_status} to {new_status}."
        elif new_status:
            message_content = (
                f"Ticket {ticket.reference} status transitioned to {new_status}."
            )
        else:
            message_content = f"Ticket {ticket.reference} status transitioned."
    else:
        category = "ACTION_ITEMS"
        priority = "MEDIUM"
        message_content = f"Notification for ticket {ticket.reference}."

    # Embed deterministic idempotency token
    related_entity_id = f"{ticket.id}:{event_type}:{ticket.version_index}"

    payloads = []
    for recipient in recipients:
        payload = {
            "recipient_user_id": recipient["recipient_user_id"],
            "recipient_role": recipient["recipient_role"],
            "category": category,
            "priority": priority,
            "channels": "IN_APP",
            "message_content": message_content,
            "related_entity_id": related_entity_id,
            "related_entity_type": "ticket",
        }
        payloads.append(payload)

    return payloads
