import contextvars
import functools
from contextlib import contextmanager
from datetime import datetime, timezone
from typing import Any, Callable, Generator, ParamSpec, TypeVar

P = ParamSpec("P")
R = TypeVar("R")

# Context variables for the current execution context
current_user_id = contextvars.ContextVar("current_user_id", default="system")
current_change_reason = contextvars.ContextVar(
    "current_change_reason", default="system_operation"
)
current_ip_address = contextvars.ContextVar("current_ip_address", default="127.0.0.1")
current_timestamp = contextvars.ContextVar("current_timestamp", default=None)


@contextmanager
def audit_context(
    user_id: str | None = None,
    change_reason: str | None = None,
    ip_address: str | None = None,
    timestamp: datetime | None = None,
) -> Generator[None, None, None]:
    """Context manager to bind user identity, change reason, IP address, and timestamp context variables.

    Ensures that background tasks maintain the initiating user's context for audit
    logging, and guarantees cleanup of context variables after task completion or
    error, preventing identity leakage.

    Args:
        user_id (str | None): The unique identifier of the initiating user.
        change_reason (str | None): The justification or reason for change.
        ip_address (str | None): The network IP address of the client.
        timestamp (datetime | None): The timestamp of the operation.

    Yields:
        None
    """
    u = user_id if user_id is not None else "system"
    r = change_reason if change_reason is not None else "system_operation"
    ip = ip_address if ip_address is not None else "127.0.0.1"
    ts = (
        timestamp
        if timestamp is not None
        else datetime.now(timezone.utc).replace(tzinfo=None)
    )

    user_token = current_user_id.set(u)
    reason_token = current_change_reason.set(r)
    ip_token = current_ip_address.set(ip)
    ts_token = current_timestamp.set(ts)
    try:
        yield
    finally:
        current_user_id.reset(user_token)
        current_change_reason.reset(reason_token)
        current_ip_address.reset(ip_token)
        current_timestamp.reset(ts_token)


def audit_context_decorator(
    user_id_getter: Callable[..., str | None] | None = None,
    change_reason_getter: Callable[..., str | None] | None = None,
    ip_address_getter: Callable[..., str | None] | None = None,
):
    """Decorator to automatically apply audit context to a function execution.

    Args:
        user_id_getter (Callable): A function that extracts the user ID from the decorated function's arguments.
        change_reason_getter (Callable): A function that extracts the change reason from the decorated function's arguments.
        ip_address_getter (Callable): A function that extracts the IP address from the decorated function's arguments.
    """

    def decorator(func: Callable[..., Any]) -> Callable[..., Any]:
        @functools.wraps(func)
        async def async_wrapper(*args: P.args, **kwargs: P.kwargs) -> Any:
            user_id = user_id_getter(*args, **kwargs) if user_id_getter else None
            change_reason = (
                change_reason_getter(*args, **kwargs) if change_reason_getter else None
            )
            ip_address = (
                ip_address_getter(*args, **kwargs) if ip_address_getter else None
            )
            with audit_context(
                user_id=user_id, change_reason=change_reason, ip_address=ip_address
            ):
                return await func(*args, **kwargs)  # type: ignore

        @functools.wraps(func)
        def sync_wrapper(*args: P.args, **kwargs: P.kwargs) -> R:
            user_id = user_id_getter(*args, **kwargs) if user_id_getter else None
            change_reason = (
                change_reason_getter(*args, **kwargs) if change_reason_getter else None
            )
            ip_address = (
                ip_address_getter(*args, **kwargs) if ip_address_getter else None
            )
            with audit_context(
                user_id=user_id, change_reason=change_reason, ip_address=ip_address
            ):
                return func(*args, **kwargs)

        import asyncio

        if asyncio.iscoroutinefunction(func):
            return async_wrapper  # type: ignore
        return sync_wrapper  # type: ignore

    return decorator
