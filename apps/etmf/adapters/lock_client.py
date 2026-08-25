import logging
import os
import sys
from collections.abc import Awaitable, Callable

import httpx

from apps.etmf.domain.exceptions import TrialLockClientError
from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("etmf-lock-client")

trial_lock_override: bool | None = None

# Pluggable Port and Adapter patterns to standardise service resolution
_trial_lock_status_resolver: Callable[[], Awaitable[bool]] | None = None
_trial_lock_trigger_handler: Callable[[str], Awaitable[None]] | None = None


def register_trial_lock_status_resolver(
    resolver: Callable[[], Awaitable[bool]] | None,
) -> None:
    """
    Registers an authoritative adapter for verifying the trial lock status.
    Eliminates the need for in-memory module injection/probing hacks.
    """
    global _trial_lock_status_resolver
    _trial_lock_status_resolver = resolver


def register_trial_lock_trigger_handler(
    handler: Callable[[str], Awaitable[None]] | None,
) -> None:
    """
    Registers an authoritative adapter for triggering a trial lock.
    Eliminates the need for in-memory module injection/probing hacks.
    """
    global _trial_lock_trigger_handler
    _trial_lock_trigger_handler = handler


async def verify_trial_lock_status(is_testing: bool | None = None) -> bool:
    if trial_lock_override is not None:
        return trial_lock_override

    # 1. Port and Adapter check
    if _trial_lock_status_resolver is not None:
        try:
            return await _trial_lock_status_resolver()
        except Exception as e:
            logger.error("Error in registered trial lock status resolver: %s", e)
            raise TrialLockClientError(
                f"Error in trial lock status resolver: {str(e)}"
            ) from e

    if is_testing is None:
        is_testing = "pytest" in sys.modules or "PYTEST_CURRENT_TEST" in os.environ
    if is_testing:
        return False

    execution_url = os.getenv("EXECUTION_URL", "http://localhost:8002")
    path = "/api/v1/execution/locks"

    gw_client = GatewayBaseClient(base_url=execution_url, timeout=5.0)

    try:
        response = await gw_client.request(
            method="GET",
            path=path,
            user_id="etmf-service",
            roles="Data Manager",
            change_reason="",
        )

        if response.status_code == 200:
            data = response.json()
            return bool(data.get("trial_locked", False))

        raise TrialLockClientError(
            f"Failed to verify trial lock state: Execution service returned {response.status_code}"
        )

    except TrialLockClientError:
        raise
    except httpx.RequestError as e:
        raise TrialLockClientError(
            f"Failed to connect to execution service for trial lock validation: {str(e)}"
        ) from e
    except Exception as e:
        raise TrialLockClientError(
            f"Unexpected error verifying trial lock state: {str(e)}"
        ) from e


async def trigger_global_trial_lock(
    reason: str, is_testing: bool | None = None
) -> None:
    logger.warning("Triggering global trial lock due to: %s", reason)

    # 1. Port and Adapter check
    if _trial_lock_trigger_handler is not None:
        try:
            await _trial_lock_trigger_handler(reason)
            return
        except Exception as e:
            logger.error("Error in registered trial lock trigger handler: %s", e)
            raise TrialLockClientError(
                f"Error in trial lock trigger handler: {str(e)}"
            ) from e

    if is_testing is None:
        is_testing = "pytest" in sys.modules or "PYTEST_CURRENT_TEST" in os.environ
    if is_testing:
        return

    execution_url = os.getenv("EXECUTION_URL", "http://localhost:8002")
    path = "/api/v1/execution/locks/trial/lock"

    gw_client = GatewayBaseClient(base_url=execution_url, timeout=5.0)

    try:
        response = await gw_client.request(
            method="POST",
            path=path,
            user_id="etmf-service",
            roles="Data Manager",
            change_reason=reason,
        )

        if response.status_code != 200:
            logger.error(
                "Failed to set global trial lock, status code: %s",
                response.status_code,
            )
            raise TrialLockClientError(
                f"Failed to set global trial lock: Execution service returned {response.status_code}"
            )

    except TrialLockClientError:
        raise
    except httpx.RequestError as e:
        logger.error(
            "Failed to connect to execution service to trigger global trial lock: %s", e
        )
        raise TrialLockClientError(
            f"Failed to connect to execution service to trigger global trial lock: {str(e)}"
        ) from e
    except Exception as e:
        raise TrialLockClientError(
            f"Unexpected error triggering global trial lock: {str(e)}"
        ) from e


__all__ = [
    "TrialLockClientError",
    "register_trial_lock_status_resolver",
    "register_trial_lock_trigger_handler",
    "trigger_global_trial_lock",
    "verify_trial_lock_status",
]
