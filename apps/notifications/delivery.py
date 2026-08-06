import hashlib
import hmac
import json
import os
from email.message import EmailMessage

import aiosmtplib
import httpx
from jinja2 import Template

from apps.notifications.models import Notification

# Default Jinja2 HTML email template
EMAIL_HTML_TEMPLATE = """<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; margin: 0; padding: 20px; background-color: #f9f9f9; }
        .container { max-width: 600px; margin: 0 auto; background: #ffffff; padding: 20px; border: 1px solid #e0e0e0; border-radius: 5px; }
        .header { font-size: 18px; font-weight: bold; margin-bottom: 10px; color: #333; }
        .meta { color: #666; font-size: 12px; margin-bottom: 20px; border-bottom: 1px solid #eee; padding-bottom: 10px; }
        .content { font-size: 14px; line-height: 1.5; color: #444; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">[{{ priority }}] New {{ category }} Notification</div>
        <div class="meta">
            <strong>Created At:</strong> {{ created_at }}<br>
            <strong>Created By:</strong> {{ created_by }}
            {% if related_entity_id %}
            <br><strong>Related Entity:</strong> {{ related_entity_type }} ({{ related_entity_id }})
            {% endif %}
        </div>
        <div class="content">
            {{ message_content }}
        </div>
    </div>
</body>
</html>
"""


async def send_email_notification(notification: Notification) -> None:
    """
    Sends an email notification via SMTP configured by SMTP_* env vars.
    """
    smtp_host = os.getenv("SMTP_HOST", "localhost")
    smtp_port = int(os.getenv("SMTP_PORT", "1025"))
    smtp_username = os.getenv("SMTP_USERNAME")
    smtp_password = os.getenv("SMTP_PASSWORD")
    smtp_use_tls = os.getenv("SMTP_USE_TLS", "false").lower() == "true"
    smtp_use_ssl = os.getenv("SMTP_USE_SSL", "false").lower() == "true"
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    smtp_sender = os.getenv("SMTP_SENDER", f"no-reply@{brand_domain}")

    # Resolve recipient email address
    if notification.recipient_user_id:
        recipient = f"{notification.recipient_user_id}@{brand_domain}"
    elif notification.recipient_role:
        recipient = f"{notification.recipient_role}@{brand_domain}"
    else:
        recipient = f"admin@{brand_domain}"

    # Build the EmailMessage
    msg = EmailMessage()
    msg["From"] = smtp_sender
    msg["To"] = recipient
    msg["Subject"] = (
        f"[{notification.priority}] {notification.category}: New Notification"
    )

    # Render HTML Body
    rendered_html = Template(EMAIL_HTML_TEMPLATE).render(
        priority=notification.priority.value
        if hasattr(notification.priority, "value")
        else notification.priority,
        category=notification.category.value
        if hasattr(notification.category, "value")
        else notification.category,
        created_at=notification.created_at.isoformat()
        if hasattr(notification.created_at, "isoformat")
        else str(notification.created_at),
        created_by=notification.created_by,
        related_entity_id=notification.related_entity_id,
        related_entity_type=notification.related_entity_type,
        message_content=notification.message_content,
    )

    msg.set_content(notification.message_content)
    msg.add_alternative(rendered_html, subtype="html")

    # Connect and send via SMTP
    client = aiosmtplib.SMTP(
        hostname=smtp_host,
        port=smtp_port,
        use_tls=smtp_use_ssl,
    )
    await client.connect()
    if smtp_use_tls:
        await client.starttls()
    if smtp_username and smtp_password:
        await client.login(smtp_username, smtp_password)
    await client.send_message(msg)
    await client.quit()


async def send_webhook_notification(notification: Notification) -> None:
    """
    Sends a webhook notification payload using httpx.
    """
    webhook_url = os.getenv("WEBHOOK_URL", "http://localhost:8080/webhook")
    webhook_signing_secret = os.getenv(
        "WEBHOOK_SIGNING_SECRET", "default_secret"
    )  # pragma: allowlist secret
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

    # Deterministic payload signing (sort keys)
    payload_bytes = json.dumps(payload, sort_keys=True).encode("utf-8")

    # Generate HMAC-SHA256 signature
    sig = hmac.new(
        webhook_signing_secret.encode("utf-8"), payload_bytes, hashlib.sha256
    ).hexdigest()

    headers = {
        "Content-Type": "application/json",
        "X-Cadence-Signature": sig,
    }

    async with httpx.AsyncClient(timeout=webhook_timeout) as client:
        response = await client.post(
            webhook_url, content=payload_bytes, headers=headers
        )
        response.raise_for_status()
