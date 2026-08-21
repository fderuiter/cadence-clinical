import functools
import inspect
from collections.abc import Callable
from typing import Any, ParamSpec, TypeVar

from fastapi import HTTPException, Request

from packages.security.context import (
    current_change_reason,
    current_signature_context,
    current_user_id,
)
from packages.security.regulated_actions import SemanticAction
from packages.security.sig_token_verifier import verify_and_consume_sig_token

P = ParamSpec("P")
R = TypeVar("R")


def require_step_up(
    semantic_action: str | SemanticAction | None = None,
    action: str | None = None,
) -> Callable[[Callable[P, R]], Callable[P, R]]:
    """Declarative 21 CFR Part 11 step-up authorization decorator.

    Enforces re-authentication step-up verification, single-use token consumption,
    expiration checks within 60s, user identity binding, and mandatory change reason
    validation directly on regulated domain service handlers or FastAPI endpoints.

    Args:
        semantic_action: Optional SemanticAction enum or string identifier (e.g. quality.capa.close).
        action: Optional path or action identifier.

    Returns:
        Callable: Decorator wrapping the target service or endpoint handler.
    """
    semantic_str = (
        semantic_action.value
        if isinstance(semantic_action, SemanticAction)
        else semantic_action
    )

    def decorator(func: Callable[P, R]) -> Callable[P, R]:
        @functools.wraps(func)
        async def async_wrapper(*args: P.args, **kwargs: P.kwargs) -> Any:
            _enforce_step_up_checks(args, kwargs, semantic_str, action)
            return await func(*args, **kwargs)  # type: ignore

        @functools.wraps(func)
        def sync_wrapper(*args: P.args, **kwargs: P.kwargs) -> R:
            _enforce_step_up_checks(args, kwargs, semantic_str, action)
            return func(*args, **kwargs)

        if inspect.iscoroutinefunction(func):
            return async_wrapper  # type: ignore
        return sync_wrapper  # type: ignore

    return decorator


def _enforce_step_up_checks(
    args: tuple[Any, ...],
    kwargs: dict[str, Any],
    semantic_action: str | None,
    action: str | None,
) -> None:
    # 1. Extract Request object if present
    req: Request | None = None
    for arg in args:
        if isinstance(arg, Request):
            req = arg
            break
    if req is None:
        for val in kwargs.values():
            if isinstance(val, Request):
                req = val
                break

    # 2. Extract and validate Change Reason (Requirement 5 & Acceptance Criterion 4)
    change_reason: str | None = None
    if req is not None:
        change_reason = req.headers.get("X-Change-Reason") or getattr(
            req.state, "change_reason", None
        )
    if not change_reason:
        change_reason = current_change_reason.get()

    if (
        change_reason is None
        or not str(change_reason).strip()
        or str(change_reason).strip() == "system_operation"
    ):
        raise HTTPException(
            status_code=400,
            detail="Missing change justification reason",
        )

    # 3. Extract Signature Token
    sig_token: str | None = None
    if req is not None:
        sig_token = req.headers.get("X-Sig-Token") or req.headers.get("x-sig-token")
    if not sig_token:
        sig_token = kwargs.get("sig_token") or kwargs.get("x_sig_token")
    if not sig_token:
        ctx_sig = current_signature_context.get()
        if isinstance(ctx_sig, dict):
            sig_token = ctx_sig.get("sig_token") or ctx_sig.get("raw_token")
        elif isinstance(ctx_sig, str):
            sig_token = ctx_sig

    if not sig_token or not str(sig_token).strip():
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # 4. Extract Expected User ID
    user_id: str | None = None
    if req is not None:
        user_id = getattr(req.state, "user_id", None) or req.headers.get("X-User-Id")
    if not user_id:
        user_id = current_user_id.get()

    # 5. Verify and consume signature token (Requirement 4 & Acceptance Criteria 2 & 3)
    payload = verify_and_consume_sig_token(
        sig_token=str(sig_token).strip(),
        expected_user_id=user_id or "system",
    )

    # Validate semantic action match if specified
    token_semantic = payload.get("semantic_action")
    if semantic_action and token_semantic and token_semantic != semantic_action:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Save verified signature context in contextvars
    current_signature_context.set(payload)
