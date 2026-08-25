"""Backward-compatibility re-export shim for eTMF trial lock client."""

from apps.etmf.adapters.lock_client import (
    TrialLockClientError,
    register_trial_lock_status_resolver,
    register_trial_lock_trigger_handler,
    trigger_global_trial_lock,
    verify_trial_lock_status,
)

__all__ = [
    "TrialLockClientError",
    "register_trial_lock_status_resolver",
    "register_trial_lock_trigger_handler",
    "trigger_global_trial_lock",
    "verify_trial_lock_status",
]
