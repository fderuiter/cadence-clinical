import json
import logging
import sys
from datetime import UTC, datetime
from typing import Any

from opentelemetry import trace


class JSONTraceLogFormatter(logging.Formatter):
    """Structured JSON log formatter that automatically injects OpenTelemetry trace_id and span_id."""

    def __init__(
        self, service_name: str = "unknown_service", *args: Any, **kwargs: Any
    ) -> None:
        super().__init__(*args, **kwargs)
        self.service_name = service_name

    def format(self, record: logging.LogRecord) -> str:
        current_span = trace.get_current_span()
        span_context = current_span.get_span_context() if current_span else None

        if span_context and span_context.is_valid:
            trace_id = format(span_context.trace_id, "032x")
            span_id = format(span_context.span_id, "016x")
        else:
            trace_id = "00000000000000000000000000000000"
            span_id = "0000000000000000"

        log_data: dict[str, Any] = {
            "timestamp": datetime.fromtimestamp(record.created, tz=UTC).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
            "service": self.service_name,
            "trace_id": trace_id,
            "span_id": span_id,
        }

        if record.exc_info:
            log_data["exception"] = self.formatException(record.exc_info)

        return json.dumps(log_data)


def configure_structured_logging(
    service_name: str, log_level: int = logging.INFO
) -> None:
    """Configures structured JSON logging for all logging.getLogger outputs."""
    root_logger = logging.getLogger()
    root_logger.setLevel(log_level)

    # Remove existing stream handlers to avoid duplicate output
    for handler in list(root_logger.handlers):
        if isinstance(handler, logging.StreamHandler):
            root_logger.removeHandler(handler)

    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JSONTraceLogFormatter(service_name=service_name))
    root_logger.addHandler(handler)
