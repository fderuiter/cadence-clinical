import json
import logging
import re

from fastapi import FastAPI
from fastapi.testclient import TestClient

from packages.security.gateway_client import (
    GatewayBaseClient,
    create_service_auth_headers,
)
from packages.security.middleware import GatewayAuthMiddleware
from packages.telemetry import (
    JSONTraceLogFormatter,
    extract_trace_context,
    get_tracer,
    inject_trace_context,
    setup_telemetry,
)


def test_json_trace_log_formatter_no_active_span():
    formatter = JSONTraceLogFormatter(service_name="test_service")
    record = logging.LogRecord(
        name="test_logger",
        level=logging.INFO,
        pathname="test.py",
        lineno=10,
        msg="Test message",
        args=(),
        exc_info=None,
    )
    output = formatter.format(record)
    data = json.loads(output)

    assert data["service"] == "test_service"
    assert data["message"] == "Test message"
    assert data["level"] == "INFO"
    assert data["logger"] == "test_logger"
    assert data["trace_id"] == "00000000000000000000000000000000"
    assert data["span_id"] == "0000000000000000"


def test_json_trace_log_formatter_with_active_span():
    tracer = get_tracer("test_tracer")
    formatter = JSONTraceLogFormatter(service_name="active_span_service")

    with tracer.start_as_current_span("test_span") as span:
        span_ctx = span.get_span_context()
        record = logging.LogRecord(
            name="test_logger",
            level=logging.WARNING,
            pathname="test.py",
            lineno=20,
            msg="Span message",
            args=(),
            exc_info=None,
        )
        output = formatter.format(record)
        data = json.loads(output)

        expected_trace_id = format(span_ctx.trace_id, "032x")
        expected_span_id = format(span_ctx.span_id, "016x")

        assert data["service"] == "active_span_service"
        assert data["trace_id"] == expected_trace_id
        assert data["span_id"] == expected_span_id


def test_w3c_trace_context_extract_and_inject():
    headers = {
        "traceparent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        "tracestate": "rojo=1,concho=2",
    }
    extracted_ctx = extract_trace_context(headers)
    assert extracted_ctx is not None

    out_headers: dict[str, str] = {}
    inject_trace_context(out_headers, context=extracted_ctx)

    assert "traceparent" in out_headers
    assert "4bf92f3577b34da6a3ce929d0e0e4736" in out_headers["traceparent"]


def test_gateway_middleware_traceparent_propagation_and_generation():
    app = FastAPI(title="Telemetry Middleware Test App")
    setup_telemetry("middleware_test", app)
    app.add_middleware(GatewayAuthMiddleware)

    @app.get("/health")
    async def health():
        return {"status": "ok"}

    client = TestClient(app)

    # 1. Test request without traceparent header -> expects newly generated valid traceparent header in response
    resp1 = client.get("/health")
    assert resp1.status_code == 200
    assert "traceparent" in resp1.headers
    tp1 = resp1.headers["traceparent"]
    assert re.match(r"^00-[a-f0-9]{32}-[a-f0-9]{16}-0[01]$", tp1)

    # 2. Test request with existing incoming W3C traceparent -> expects traceparent propagated
    incoming_tp = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
    resp2 = client.get("/health", headers={"traceparent": incoming_tp})
    assert resp2.status_code == 200
    assert "traceparent" in resp2.headers
    assert "4bf92f3577b34da6a3ce929d0e0e4736" in resp2.headers["traceparent"]


def test_gateway_client_trace_header_injection():
    headers = create_service_auth_headers("user_123")
    assert "traceparent" in headers
    assert re.match(r"^00-[a-f0-9]{32}-[a-f0-9]{16}-0[01]$", headers["traceparent"])

    base_client = GatewayBaseClient()
    built_headers = base_client.build_headers("user_456", "admin", "testing_reason")
    assert "traceparent" in built_headers
    assert re.match(
        r"^00-[a-f0-9]{32}-[a-f0-9]{16}-0[01]$", built_headers["traceparent"]
    )
