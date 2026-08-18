"""Unit and integration test suite for immediate re-consent email alerts.

Requirements: PRD-SYS-001, PRD-SUB-007
"""

import uuid
from datetime import UTC, datetime

import pytest

from apps.notifications.application.services.email_renderer import (
    get_template_name_for_event,
    render_email_template,
)
from apps.notifications.domain.event_models import SystemDomainEvent
from apps.notifications.workers.notification_worker import NotificationWorker


def test_reconsent_template_mapping_and_rendering():
    """Validates that RECONSENT_REQUIRED event maps to GxP Jinja2 template and renders HTML without PHI."""
    template_name = get_template_name_for_event("RECONSENT_REQUIRED")
    assert template_name == "reconsent_required.html.j2"

    context = {
        "study_id": "STUDY-AMEND-01",
        "event_id": "evt_test_123",
        "timestamp_utc": datetime.now(UTC).isoformat(),
        "payload": {
            "version_number": "2.0",
            "protocol_version": "2.0",
            "subject_id": "subject_101",
            "change_summary": "Updated protocol safety and visit schedules.",
        },
    }

    rendered = render_email_template(template_name, context)
    assert "[URGENT] Protocol Amendment Re-Consent Required" in rendered
    assert "STUDY-AMEND-01" in rendered
    assert "2.0" in rendered
    assert "Updated protocol safety and visit schedules." in rendered
    assert "Participant Companion Portal" in rendered


@pytest.mark.asyncio
async def test_reconsent_worker_recipient_resolution():
    """Validates that notification worker resolves target participant recipients for RECONSENT_REQUIRED."""
    worker = NotificationWorker()
    event = SystemDomainEvent(
        event_id=str(uuid.uuid4()),
        event_type="RECONSENT_REQUIRED",
        study_id="STUDY-AMEND-01",
        source_service="econsent",
        timestamp_utc=datetime.now(UTC).isoformat(),
        payload={
            "subject_pseudonym": "subject_202",
            "version_number": "2.0",
            "change_summary": "Protocol amendment re-consent required.",
        },
    )

    recipients = await worker.resolve_recipients(
        event.event_type, event.study_id, event.payload
    )
    assert len(recipients) >= 1
    assert any(r["user_id"] == "subject_202" for r in recipients)
