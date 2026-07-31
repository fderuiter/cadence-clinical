import os
from email.message import EmailMessage
import aiosmtplib
from jinja2 import Environment, FileSystemLoader, select_autoescape
from apps.notifications.models import Notification

TEMPLATE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "templates")

# Setup Jinja2 environment referencing the apps/notifications/templates directory
env = Environment(
    loader=FileSystemLoader(TEMPLATE_DIR),
    autoescape=select_autoescape(default_for_string=True, default=True),
)

# Standard template fallbacks in case Jinja2 Environment cannot find templates on disk.
FALLBACK_TEMPLATES = {
    "etmf_expiry_alert.html.j2": """
        <h2>TMF Document Expiration Warning</h2>
        <p>Study ID: {{ study_id }}</p>
        <p>Document Name: {{ payload.document_name }}</p>
        <p>Expiration Date: {{ payload.expiration_date }}</p>
    """,
    "edc_query_raised.html.j2": """
        <h2>EDC Clinical Query Raised</h2>
        <p>Study ID: {{ study_id }}</p>
        <p>Query: {{ payload.query_message }}</p>
    """,
    "sae_reconciliation.html.j2": """
        <h2>SAE Reconciliation Discrepancy Flagged</h2>
        <p>Study ID: {{ study_id }}</p>
        <p>Subject ID: {{ payload.subject_id }}</p>
        <p>Flag Reason: {{ payload.flag_reason }}</p>
    """,
    "protocol_amendment.html.j2": """
        <h2>Protocol Amendment Submitted</h2>
        <p>Study ID: {{ study_id }}</p>
        <p>Amendment Tag: {{ payload.amendment_tag }}</p>
    """,
}

# Default Jinja2 HTML email template fallback
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
    smtp_host = os.getenv("SMTP_HOST", "localhost")
    smtp_port = int(os.getenv("SMTP_PORT", "1025"))
    smtp_username = os.getenv("SMTP_USERNAME")
    smtp_password = os.getenv("SMTP_PASSWORD")
    smtp_use_tls = os.getenv("SMTP_USE_TLS", "false").lower() == "true"
    smtp_use_ssl = os.getenv("SMTP_USE_SSL", "false").lower() == "true"
    smtp_sender = os.getenv("SMTP_SENDER", "no-reply@cadenceclinical.com")

    # Resolve recipient email address
    if notification.recipient_user_id:
        recipient = f"{notification.recipient_user_id}@cadenceclinical.com"
    elif notification.recipient_role:
        recipient = f"{notification.recipient_role}@cadenceclinical.com"
    else:
        recipient = "admin@cadenceclinical.com"

    # Build the EmailMessage
    msg = EmailMessage()
    msg["From"] = smtp_sender
    msg["To"] = recipient
    priority_val = notification.priority.value if hasattr(notification.priority, "value") else notification.priority
    category_val = notification.category.value if hasattr(notification.category, "value") else notification.category
    msg["Subject"] = (
        f"[{priority_val}] {category_val}: New Notification"
    )

    # Determine template to render
    from apps.notifications.services.email_renderer import get_template_name_for_event

    template_name = None
    if notification.related_entity_type:
        template_name = get_template_name_for_event(notification.related_entity_type)
        if template_name == "default_alert.html.j2":
            template_name = None

    context = {
        "study_id": "STUDY-MOCK",
        "event_id": notification.related_entity_id or "",
        "timestamp_utc": notification.created_at.isoformat() if hasattr(notification.created_at, "isoformat") else str(notification.created_at),
        "payload": {
            "document_name": notification.message_content,
            "artifact_code": notification.related_entity_id or "",
            "expiration_date": "N/A",
            "query_message": notification.message_content,
            "flag_reason": notification.message_content,
            "amendment_tag": notification.message_content,
        }
    }

    rendered_html = None
    if template_name:
        try:
            template = env.get_template(template_name)
            rendered_html = template.render(**context)
        except Exception:
            # Fallback to in-memory/FALLBACK_TEMPLATES if env.get_template fails or templates are not on disk
            if template_name in FALLBACK_TEMPLATES:
                from jinja2 import Template as JinjaTemplate
                rendered_html = JinjaTemplate(FALLBACK_TEMPLATES[template_name]).render(**context)

    if not rendered_html:
        # Fallback to general email template
        rendered_html = env.from_string(EMAIL_HTML_TEMPLATE).render(
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
