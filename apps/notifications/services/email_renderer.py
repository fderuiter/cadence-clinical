import os
from typing import Any, Dict

from jinja2 import Environment, FileSystemLoader, Template, select_autoescape

TEMPLATE_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "templates"
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


def render_email_template(template_name: str, context: Dict[str, Any]) -> str:
    """
    Renders a GxP-compliant HTML email notification from Jinja2 templates.
    Falls back to embedded string templates if disk template files are inaccessible.

    Requirements: PRD-SYS-001
    """
    if os.path.exists(TEMPLATE_DIR):
        try:
            env = Environment(
                loader=FileSystemLoader(TEMPLATE_DIR),
                autoescape=select_autoescape(["html", "xml", "j2"]),
            )
            template = env.get_template(template_name)
            return template.render(**context)
        except Exception:
            # Fallback to in-memory templates on loading errors
            pass

    # Use in-memory fallback
    fallback_source = FALLBACK_TEMPLATES.get(
        template_name, "<h3>Notification</h3><p>{{ payload }}</p>"
    )
    template = Template(fallback_source)
    return template.render(**context)


def get_template_name_for_event(event_type: str) -> str:
    """
    Maps Clinical Domain Event types to Jinja2 template file names.
    """
    mapping = {
        "ETMF_DOCUMENT_EXPIRING": "etmf_expiry_alert.html.j2",
        "EDC_QUERY_RAISED": "edc_query_raised.html.j2",
        "SAE_RECONCILIATION_FLAG": "sae_reconciliation.html.j2",
        "PROTOCOL_AMENDMENT_SUBMITTED": "protocol_amendment.html.j2",
    }
    return mapping.get(event_type, "default_alert.html.j2")
