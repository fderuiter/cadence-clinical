import importlib
import logging
from typing import Any

logger = logging.getLogger("packages.security.asgi_registry")

_SERVICE_APPS: dict[str, Any] = {}

SERVICE_MODULE_MAP: dict[str, str] = {
    "designer": "apps.designer.main",
    "execution": "apps.execution.main",
    "etmf": "apps.etmf.main",
    "interop": "apps.interop.main",
    "ctms": "apps.ctms.main",
    "notifications": "apps.notifications.main",
    "quality": "apps.quality.main",
    "safety": "apps.safety.main",
    "tickets": "apps.tickets.main",
    "org": "apps.org.main",
    "eisf": "apps.eisf.main",
    "econsent": "apps.econsent.main",
}

PORT_SERVICE_MAP: dict[str, str] = {
    "8001": "designer",
    "8002": "execution",
    "8003": "etmf",
    "8004": "interop",
    "8005": "quality",
    "8006": "notifications",
    "8007": "ctms",
    "8008": "safety",
    "8009": "tickets",
    "8010": "eisf",
    "8011": "econsent",
    "8012": "org",
}


def register_service_app(service_name: str, app: Any) -> None:
    """Register an ASGI application instance for in-process routing.

    Args:
        service_name: Canonical service name identifier.
        app: The ASGI or FastAPI application instance.
    """
    _SERVICE_APPS[service_name.lower().strip()] = app


def get_service_app(service_name: str) -> Any | None:
    """Get the FastAPI app instance for a given service name.

    Args:
        service_name: Canonical service name identifier.

    Returns:
        Optional[Any]: The ASGI application instance or None if unavailable.
    """
    key = service_name.lower().strip()
    return _SERVICE_APPS.get(key)


def resolve_service_name(url_or_name: str) -> str | None:
    """Resolve a URL, port, or path string to a canonical microservice identifier.

    Args:
        url_or_name: URL, host:port string, or service key.

    Returns:
        Optional[str]: Resolved canonical service key or None.
    """
    if not url_or_name:
        return None
    val = str(url_or_name).lower().strip()

    # Match registered apps or known service names by substring first
    all_keys = set(_SERVICE_APPS.keys()) | set(SERVICE_MODULE_MAP.keys())
    for s_name in sorted(all_keys, key=len, reverse=True):
        if s_name in val:
            return s_name

    # Fallback to port number matching if no service name matched
    for port, s_name in PORT_SERVICE_MAP.items():
        if f":{port}" in val:
            return s_name

    return None
