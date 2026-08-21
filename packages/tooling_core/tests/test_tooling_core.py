"""Unit and contract tests for tooling core contracts and handlers.

@req:PRD-SYS-049
"""

from packages.tooling_core.contracts import (
    CommandEnvelope,
    DoctorDiagnoseResponse,
    ZoomInspectRequest,
)
from packages.tooling_core.handlers import (
    handle_zoom_inspect,
    register_zoom_payload,
)


def test_command_envelope_serialization():
    """Verify CommandEnvelope correctly serializes structured data, remediation, and zoom tokens.

    @req:PRD-SYS-049
    """
    resp = DoctorDiagnoseResponse(
        python_version="3.14.7",
        sqlite_ok=True,
        postgres_ok=True,
        neo4j_ok=True,
        issues=[],
        remediations=[],
    )
    envelope = CommandEnvelope[DoctorDiagnoseResponse](
        success=True,
        exit_code=0,
        summary={"status": "healthy"},
        remediation="uv run cadence doctor --auto-fix",
        zoom_token="zoom-doc-1234",
        data=resp,
    )

    data_dict = envelope.model_dump()
    assert data_dict["success"] is True
    assert data_dict["exit_code"] == 0
    assert data_dict["summary"]["status"] == "healthy"
    assert data_dict["remediation"] == "uv run cadence doctor --auto-fix"
    assert data_dict["zoom_token"] == "zoom-doc-1234"
    assert data_dict["data"]["python_version"] == "3.14.7"


def test_zoom_inspect_handler():
    """Verify zoom inspection correctly retrieves and paginates detailed traces.

    @req:PRD-SYS-049
    """
    token = "test-token-trace-999"
    raw_trace = "line 1\nline 2\nline 3\nline 4\nline 5"
    register_zoom_payload(token, "stack_trace", raw_trace)

    # Inspect first 2 lines
    req = ZoomInspectRequest(zoom_token=token, offset=0, limit=2)
    envelope = handle_zoom_inspect(req)
    assert envelope.success is True
    assert envelope.data is not None
    assert envelope.data.total_lines == 5
    assert envelope.data.lines == ["line 1", "line 2"]
    assert envelope.data.has_more is True

    # Inspect remaining lines
    req_next = ZoomInspectRequest(zoom_token=token, offset=2, limit=5)
    env_next = handle_zoom_inspect(req_next)
    assert env_next.data is not None
    assert env_next.data.lines == ["line 3", "line 4", "line 5"]
    assert env_next.data.has_more is False
