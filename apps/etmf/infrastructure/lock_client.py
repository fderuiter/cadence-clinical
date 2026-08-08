import logging
import os
import sys
import time

import httpx
from fastapi import HTTPException

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("etmf-lock-client")

trial_lock_override: bool | None = None


async def verify_trial_lock_status(is_testing: bool | None = None) -> bool:
    if trial_lock_override is not None:
        return trial_lock_override

    if is_testing is None:
        is_testing = "pytest" in sys.modules or "PYTEST_CURRENT_TEST" in os.environ
    if is_testing:
        try:
            exec_lock_name = "apps.execution.trial_lock"
            if exec_lock_name in sys.modules:
                tl_mgr = sys.modules[exec_lock_name].TrialLockManager
                if tl_mgr.is_locked():
                    return True
        except Exception:
            pass
        return False

    execution_url = os.getenv("EXECUTION_URL", "http://localhost:8002")
    gateway_secret_env = os.getenv(
        "GATEWAY_SECRET", default="internal-gateway-secret-12345"
    )
    gateway_secret = (
        gateway_secret_env.encode("utf-8")
        if isinstance(gateway_secret_env, str)
        else gateway_secret_env
    )

    user_id = "etmf-service"
    roles = "Data Manager"
    timestamp = str(time.time())

    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=gateway_secret,
        change_reason="",
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": "",
    }

    url = f"{execution_url.rstrip('/')}/api/v1/execution/locks"
    try:
        async with httpx.AsyncClient(timeout=5.0) as client:
            response = await client.get(url, headers=headers)
            if response.status_code == 200:
                data = response.json()
                return bool(data.get("trial_locked", False))
            raise HTTPException(
                status_code=502,
                detail=f"Failed to verify trial lock state: Execution service returned {response.status_code}",
            )
    except httpx.RequestError as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to connect to execution service for trial lock validation: {str(e)}",
        )


async def trigger_global_trial_lock(
    reason: str, is_testing: bool | None = None
) -> None:
    logger.warning("Triggering global trial lock due to: %s", reason)

    if is_testing is None:
        is_testing = "pytest" in sys.modules or "PYTEST_CURRENT_TEST" in os.environ
    if is_testing:
        try:
            exec_lock_name = "apps.execution.trial_lock"
            if exec_lock_name in sys.modules:
                tl_mgr = sys.modules[exec_lock_name].TrialLockManager
                tl_mgr.lock_trial(reason=reason)
                return
        except Exception:
            pass

    execution_url = os.getenv("EXECUTION_URL", "http://localhost:8002")
    gateway_secret_env = os.getenv(
        "GATEWAY_SECRET", default="internal-gateway-secret-12345"
    )
    gateway_secret = (
        gateway_secret_env.encode("utf-8")
        if isinstance(gateway_secret_env, str)
        else gateway_secret_env
    )

    user_id = "etmf-service"
    roles = "Data Manager"
    timestamp = str(time.time())

    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=gateway_secret,
        change_reason=reason,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": reason,
    }

    url = f"{execution_url.rstrip('/')}/api/v1/execution/locks/trial/lock"
    try:
        async with httpx.AsyncClient(timeout=5.0) as client:
            response = await client.post(url, headers=headers)
            if response.status_code != 200:
                logger.error(
                    "Failed to set global trial lock, status code: %s",
                    response.status_code,
                )
                raise HTTPException(
                    status_code=502,
                    detail=f"Failed to set global trial lock: Execution service returned {response.status_code}",
                )
    except httpx.RequestError as e:
        logger.error(
            "Failed to connect to execution service to trigger global trial lock: %s", e
        )
        raise HTTPException(
            status_code=502,
            detail=f"Failed to connect to execution service to trigger global trial lock: {str(e)}",
        )
