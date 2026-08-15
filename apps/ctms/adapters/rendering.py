import os
from datetime import datetime
from typing import Any

from jinja2 import Environment, FileSystemLoader, select_autoescape

TEMPLATES_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), "templates")

# Initialize Jinja2 environment with FileSystemLoader and autoescape enabled
env = Environment(
    loader=FileSystemLoader(TEMPLATES_DIR),
    autoescape=select_autoescape(["html", "xml", "j2"]),
)


def _format_date(val: datetime | str | None) -> str:
    if isinstance(val, datetime):
        return val.isoformat()
    return str(val or "")


def render_confirmation_letter(
    study_id: str,
    site_id: str,
    cra_id: str,
    visit_type: str,
    scheduled_date: datetime | str,
    created_at: datetime | str,
) -> str:
    """Renders the confirmation letter using Jinja2 template."""
    template = env.get_template("confirmation_letter.j2")
    return template.render(
        study_id=study_id,
        site_id=site_id,
        cra_id=cra_id,
        visit_type=visit_type,
        scheduled_date=_format_date(scheduled_date),
        created_at=_format_date(created_at),
    )


def render_follow_up_letter(
    study_id: str,
    site_id: str,
    cra_id: str,
    visit_type: str,
    actual_date: datetime | str,
    findings: list[dict[str, Any]],
    created_at: datetime | str,
) -> str:
    """Renders the follow-up letter using Jinja2 template."""
    template = env.get_template("follow_up_letter.j2")
    return template.render(
        study_id=study_id,
        site_id=site_id,
        cra_id=cra_id,
        visit_type=visit_type,
        actual_date=_format_date(actual_date),
        findings=findings,
        created_at=_format_date(created_at),
    )
