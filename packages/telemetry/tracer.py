import contextlib
from collections.abc import Mapping
from typing import Any

from fastapi import FastAPI

try:
    from opentelemetry import trace
    from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
    from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor
    from opentelemetry.sdk.resources import Resource
    from opentelemetry.sdk.trace import TracerProvider
    from opentelemetry.trace.propagation.tracecontext import TraceContextTextMapPropagator
    _HAS_OTEL = True
except ImportError:
    _HAS_OTEL = False

from packages.telemetry.logging import configure_structured_logging

_IS_INSTRUMENTED = False


def setup_telemetry(service_name: str, app: FastAPI | None = None) -> Any:
    """Initialize OpenTelemetry SDK, FastAPI instrumentor, HTTPX instrumentor, and structured logging."""
    global _IS_INSTRUMENTED

    # Configure structured logging
    configure_structured_logging(service_name)

    if not _HAS_OTEL:
        return None

    # Ensure TracerProvider is configured
    provider = trace.get_tracer_provider()
    if not isinstance(provider, TracerProvider):
        resource = Resource.create({"service.name": service_name})
        provider = TracerProvider(resource=resource)
        trace.set_tracer_provider(provider)

    # Instrument HTTPX clients if not already done
    if not _IS_INSTRUMENTED:
        with contextlib.suppress(Exception):
            HTTPXClientInstrumentor().instrument()
        _IS_INSTRUMENTED = True

    # Instrument FastAPI application if provided
    if app is not None:
        with contextlib.suppress(Exception):
            FastAPIInstrumentor().instrument_app(app)

    return provider


def get_tracer(name: str = "cadence.telemetry") -> Any:
    """Returns a tracer instance."""
    if _HAS_OTEL:
        return trace.get_tracer(name)
    return None


def extract_trace_context(headers: Mapping[str, str]) -> Any:
    """Extract W3C trace context from HTTP headers."""
    if _HAS_OTEL:
        normalized_headers = {k.lower(): v for k, v in headers.items()}
        return TraceContextTextMapPropagator().extract(normalized_headers)
    return None


def inject_trace_context(
    headers: dict[str, str], context: Any = None
) -> dict[str, str]:
    """Inject active W3C trace context into HTTP headers dict."""
    if _HAS_OTEL:
        TraceContextTextMapPropagator().inject(headers, context=context)
    return headers
