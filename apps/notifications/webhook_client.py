import os
import httpx
from typing import Any, Dict
from apps.notifications.models import Notification
from packages.security.signing import generate_canonical_signature, canonical_serialize

async def send_webhook_notification(notification: Notification) -> None:
    webhook_url = os.getenv("WEBHOOK_URL", "http://localhost:8080/webhook")
    webhook_signing_secret = os.getenv("WEBHOOK_SIGNING_SECRET", "default_secret")
    webhook_timeout = float(os.getenv("WEBHOOK_TIMEOUT", "10.0"))

    # Build the payload
    payload = {
        "id": notification.id,
        "recipient_user_id": notification.recipient_user_id,
        "recipient_role": notification.recipient_role,
        "category": notification.category.value
        if hasattr(notification.category, "value")
        else notification.category,
        "priority": notification.priority.value
        if hasattr(notification.priority, "value")
        else notification.priority,
        "channels": notification.channels,
        "message_content": notification.message_content,
        "related_entity_id": notification.related_entity_id,
        "related_entity_type": notification.related_entity_type,
        "status": notification.status.value
        if hasattr(notification.status, "value")
        else notification.status,
        "created_at": notification.created_at.isoformat()
        if hasattr(notification.created_at, "isoformat")
        else str(notification.created_at),
        "created_by": notification.created_by,
        "version_index": notification.version_index,
    }

    # Deterministic payload signing (sort keys, no extra spacing)
    payload_bytes = canonical_serialize(payload)
    sig = generate_canonical_signature(payload, webhook_signing_secret.encode("utf-8"))

    headers = {
        "Content-Type": "application/json",
        "X-Cadence-Signature": sig,
    }

    async with httpx.AsyncClient(timeout=webhook_timeout) as client:
        response = await client.post(
            webhook_url, content=payload_bytes, headers=headers
        )
        response.raise_for_status()
