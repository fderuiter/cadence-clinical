import json
import logging
import time
from typing import List

from apps.execution.notifications_client import publish_notification, run_async

"""
Module for managing automated trial locks and security notifications.
This module intercepts write operations globally or per trial when a security compromise
is detected, while allowing read operations, ensuring data integrity without blocking safety queries.
"""

logger = logging.getLogger("NotificationRouter")


class NotificationRouter:
    """Routes alerts to designated safety leads and security representatives."""

    def send_email(self, recipients: List[str], message: str):
        """Sends an email notification to the specified recipients."""
        try:
            category = "SYSTEM"
            priority = "MEDIUM"
            related_entity_type = None
            related_entity_id = None

            # 1. Trial-lock check
            if "Trial locked" in message or "URGENT: Trial locked" in message:
                category = "ALERTS"
                priority = "CRITICAL"
                related_entity_type = "trial-lock"

            # 2. Query-aging check
            elif "Daily Clinical Query Aging Digest" in message:
                category = "ACTION_ITEMS"
                priority = "HIGH"
                study_id = None
                site_id = None
                for line in message.split("\n"):
                    if line.startswith("Study:"):
                        study_id = line.split(":", 1)[1].strip()
                    elif line.startswith("Site:"):
                        site_id = line.split(":", 1)[1].strip()
                if study_id or site_id:
                    related_entity_type = "study-site"
                    related_entity_id = (
                        f"{study_id or 'Unknown'}:{site_id or 'Unknown'}"
                    )

            for recipient in recipients:
                payload = {
                    "recipient_user_id": recipient,
                    "category": category,
                    "priority": priority,
                    "channels": "EMAIL",
                    "message_content": message,
                    "related_entity_id": related_entity_id,
                    "related_entity_type": related_entity_type,
                }
                run_async(publish_notification(payload))
        except Exception as e:
            logger.error("Failed to send email notification: %s", e, exc_info=True)

    def send_sms(self, phone_numbers: List[str], message: str):
        """Sends an SMS notification to the specified phone numbers."""
        try:
            category = "SYSTEM"
            priority = "MEDIUM"
            related_entity_type = None
            related_entity_id = None

            if "Trial locked" in message or "URGENT: Trial locked" in message:
                category = "ALERTS"
                priority = "CRITICAL"
                related_entity_type = "trial-lock"

            for number in phone_numbers:
                payload = {
                    "recipient_user_id": number,
                    "category": category,
                    "priority": priority,
                    "channels": "IN_APP",
                    "message_content": message,
                    "related_entity_id": related_entity_id,
                    "related_entity_type": related_entity_type,
                }
                run_async(publish_notification(payload))
        except Exception as e:
            logger.error("Failed to send SMS notification: %s", e, exc_info=True)

    def send_webhook(self, url: str, payload: dict):
        """Sends a webhook payload to the specified URL."""
        try:
            category = "SYSTEM"
            priority = "MEDIUM"
            related_entity_type = "webhook-url"
            related_entity_id = url

            message = payload.get("text") if isinstance(payload, dict) else None
            if not message:
                message = json.dumps(payload)

            if "Trial locked" in message or "URGENT: Trial locked" in message:
                category = "ALERTS"
                priority = "CRITICAL"
                related_entity_type = "trial-lock"

            notification_payload = {
                "category": category,
                "priority": priority,
                "channels": "WEBHOOK",
                "message_content": message,
                "related_entity_id": related_entity_id,
                "related_entity_type": related_entity_type,
            }
            run_async(publish_notification(notification_payload))
        except Exception as e:
            logger.error("Failed to send webhook notification: %s", e, exc_info=True)

    def send_dashboard_notification(self, recipients: List[str], payload: dict):
        """Sends a dashboard notification to the specified recipients."""
        try:
            # 1. SDV-drop check
            if "observation_id" in payload:
                category = "ALERTS"
                priority = "HIGH"
                related_entity_type = "observation"
                related_entity_id = payload.get("observation_id")
                message_content = payload.get(
                    "message", "Previously verified field modified..."
                )

                for recipient in recipients:
                    notif_payload = {
                        "recipient_user_id": recipient,
                        "category": category,
                        "priority": priority,
                        "channels": "IN_APP",
                        "message_content": message_content,
                        "related_entity_id": related_entity_id,
                        "related_entity_type": related_entity_type,
                    }
                    run_async(publish_notification(notif_payload))

            # 2. Emergency-unblinding check
            elif payload.get("event_type") == "emergency-unblinding":
                category = "ALERTS"
                priority = "CRITICAL"
                related_entity_type = "subject"
                related_entity_id = payload.get("subject_id")
                message_content = payload.get("message")

                # Emit alerts targeting free-text recipient_role values "Sponsor Safety Lead", "Lead CRA", and "IDMC"
                roles = payload.get(
                    "recipient_roles", ["Sponsor Safety Lead", "Lead CRA", "IDMC"]
                )
                for role in roles:
                    notif_payload = {
                        "recipient_role": role,
                        "category": category,
                        "priority": priority,
                        "channels": "IN_APP",
                        "message_content": message_content,
                        "related_entity_id": related_entity_id,
                        "related_entity_type": related_entity_type,
                    }
                    run_async(publish_notification(notif_payload))
            else:
                # Fallback / general
                category = "SYSTEM"
                priority = "MEDIUM"
                related_entity_type = payload.get("related_entity_type")
                related_entity_id = payload.get("related_entity_id")
                message_content = payload.get("message") or str(payload)

                for recipient in recipients:
                    notif_payload = {
                        "recipient_user_id": recipient,
                        "category": category,
                        "priority": priority,
                        "channels": "IN_APP",
                        "message_content": message_content,
                        "related_entity_id": related_entity_id,
                        "related_entity_type": related_entity_type,
                    }
                    run_async(publish_notification(notif_payload))
        except Exception as e:
            logger.error("Failed to send dashboard notification: %s", e, exc_info=True)


class TrialLockManager:
    """
    Manages the global or trial-specific freeze state and routes alerts.
    """

    _is_locked = False
    _locked_at = None
    _router = NotificationRouter()
    _locked_sites = set()
    _locked_visits = set()
    _locked_forms = set()
    _locked_subjects = set()

    @classmethod
    def lock_site(cls, site_id: str):
        """Locks a specific site by site_id."""
        cls._locked_sites.add(str(site_id))

    @classmethod
    def unlock_site(cls, site_id: str):
        """Unlocks a specific site by site_id."""
        cls._locked_sites.discard(str(site_id))

    @classmethod
    def is_site_locked(cls, site_id: str) -> bool:
        """Checks if a site is locked."""
        return str(site_id) in cls._locked_sites

    @classmethod
    def lock_visit(cls, visit_id: str):
        """Locks a specific visit by visit_id."""
        cls._locked_visits.add(str(visit_id))

    @classmethod
    def unlock_visit(cls, visit_id: str):
        """Unlocks a specific visit by visit_id."""
        cls._locked_visits.discard(str(visit_id))

    @classmethod
    def is_visit_locked(cls, visit_id: str) -> bool:
        """Checks if a visit is locked."""
        return str(visit_id) in cls._locked_visits

    @classmethod
    def lock_form(cls, form_id: str):
        """Locks a specific form by form_id."""
        cls._locked_forms.add(str(form_id))

    @classmethod
    def unlock_form(cls, form_id: str):
        """Unlocks a specific form by form_id."""
        cls._locked_forms.discard(str(form_id))

    @classmethod
    def is_form_locked(cls, form_id: str) -> bool:
        """Checks if a form is locked."""
        return str(form_id) in cls._locked_forms

    @classmethod
    def lock_subject(cls, subject_id: str):
        """Locks a specific subject by subject_id."""
        cls._locked_subjects.add(str(subject_id))

    @classmethod
    def unlock_subject(cls, subject_id: str):
        """Unlocks a specific subject by subject_id."""
        cls._locked_subjects.discard(str(subject_id))

    @classmethod
    def is_subject_locked(cls, subject_id: str) -> bool:
        """Checks if a subject is locked."""
        return str(subject_id) in cls._locked_subjects

    @classmethod
    def lock_trial(cls, reason: str = "Security violation detected"):
        """Freezes the trial into a read-only state and dispatches alerts."""
        if not cls._is_locked:
            cls._is_locked = True
            cls._locked_at = time.time()

            # Dispatch high-priority notifications to designated contacts
            message = f"URGENT: Trial locked. Reason: {reason}"

            cls._router.send_email(
                ["security@cadence.clinical", "safety@cadence.clinical"], message
            )
            cls._router.send_sms(["+1234567890", "+0987654321"], message)
            cls._router.send_webhook(
                "https://hooks.cadence.clinical/alerts", {"text": message}
            )

    @classmethod
    def unlock_trial(cls):
        """Unlocks the trial-wide lock state."""
        cls._is_locked = False
        cls._locked_at = None

    @classmethod
    def is_locked(cls) -> bool:
        """Returns True if the trial is currently locked."""
        return cls._is_locked

    @classmethod
    def reset(cls):
        """Resets lock (mostly for testing)."""
        cls._is_locked = False
        cls._locked_at = None
        cls._locked_sites.clear()
        cls._locked_visits.clear()
        cls._locked_forms.clear()
        cls._locked_subjects.clear()
