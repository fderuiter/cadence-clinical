import time
from typing import List

"""
Module for managing automated trial locks and security notifications.
This module intercepts write operations globally or per trial when a security compromise
is detected, while allowing read operations, ensuring data integrity without blocking safety queries.
"""


class NotificationRouter:
    """Routes alerts to designated safety leads and security representatives."""

    def send_email(self, recipients: List[str], message: str):
        """Sends an email notification to the specified recipients."""
        # Simulate email sending
        pass

    def send_sms(self, phone_numbers: List[str], message: str):
        """Sends an SMS notification to the specified phone numbers."""
        # Simulate SMS sending
        pass

    def send_webhook(self, url: str, payload: dict):
        """Sends a webhook payload to the specified URL."""
        # Simulate webhook
        pass

    def send_dashboard_notification(self, recipients: List[str], payload: dict):
        """Sends a dashboard notification to the specified recipients."""
        # Simulate dashboard notification sending or print/log for testing
        pass


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
