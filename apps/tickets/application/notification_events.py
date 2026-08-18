"""
Notification events payload builder for Tickets microservice.
"""

import logging

from packages.security.context import current_user_id

logger = logging.getLogger("tickets-notification-events")


def generate_ticket_notification_payloads(
    ticket: any,
    event_type: str,
    old_status: str | None = None,
    new_status: str | None = None,
    comment_body: str | None = None,
    comment_visibility: str | None = None,
) -> list[dict]:
    """
    Generates list of notification payloads based on ticket state, event type, and recipient policy.

    Recipient policy:
    - Target assignee_user when set, else assignee_role.
    - Also notify reporter (unless internal sponsor comment on a site-reported ticket).
    - Exclude acting principal (current X-User-Id) from recipients.
    - Emit one payload per distinct recipient.
    """
    actor = current_user_id.get()

    recipients = []

    if ticket.assignee_user:
        if ticket.assignee_user != actor:
            recipients.append(
                {"recipient_user_id": ticket.assignee_user, "recipient_role": None}
            )
    elif ticket.assignee_role:
        recipients.append(
            {"recipient_user_id": None, "recipient_role": ticket.assignee_role}
        )

    # For internal sponsor comments, do not dispatch to regular site reporter unless explicitly assigned
    is_internal_note = comment_visibility == "INTERNAL_SPONSOR"
    if ticket.reporter and ticket.reporter != actor and not is_internal_note:
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

    if event_type == "assignment":
        category = "ACTION_ITEMS"
        priority = "MEDIUM"
        message_content = f"Ticket {ticket.reference} has been assigned to {ticket.assignee_user or ticket.assignee_role}."
    elif event_type == "comment":
        category = "ACTION_ITEMS"
        priority = "MEDIUM"
        prefix = "[Internal Note] " if is_internal_note else ""
        if comment_body:
            snippet = (
                comment_body[:60] + "..." if len(comment_body) > 60 else comment_body
            )
            message_content = (
                f"{prefix}New comment added to ticket {ticket.reference}: '{snippet}'."
            )
        else:
            message_content = f"{prefix}New comment added to ticket {ticket.reference}."
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
    elif event_type == "sla_warning":
        category = "ACTION_ITEMS"
        priority = "HIGH"
        message_content = f"SLA Amber Warning: Ticket {ticket.reference} has reached 75% of its SLA duration."
    elif event_type == "sla_breach":
        category = "ACTION_ITEMS"
        priority = "CRITICAL"
        message_content = f"SLA Breach Alert: Ticket {ticket.reference} has breached its resolution SLA target."
    elif event_type == "signature":
        category = "SYSTEM"
        priority = "MEDIUM"
        message_content = f"21 CFR Part 11 Electronic Signature captured on ticket {ticket.reference}."
    elif event_type == "critical_deviation":
        category = "ACTION_ITEMS"
        priority = "CRITICAL"
        message_content = f"Critical Protocol Deviation logged: {ticket.reference}."
    else:
        category = "ACTION_ITEMS"
        priority = "MEDIUM"
        message_content = f"Notification for ticket {ticket.reference}."

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
