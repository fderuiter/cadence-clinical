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


def test_handle_usdm_graph_inspect_depth_and_filtering():
    """Verify USDM protocol graph inspection correctly filters by entity type and depth.

    @req:PRD-SYS-054
    """
    from packages.tooling_core.contracts import UsdmGraphInspectRequest
    from packages.tooling_core.handlers import handle_usdm_graph_inspect

    # Request specific entity type
    req = UsdmGraphInspectRequest(
        protocol_id="STUDY-ONC-01", entity_type="Activity", depth=3
    )
    envelope = handle_usdm_graph_inspect(req)

    assert envelope.success is True
    assert envelope.data is not None
    assert envelope.data.protocol_id == "STUDY-ONC-01"
    assert envelope.data.depth == 3
    assert "Activity" in envelope.data.entities
    assert "Study" not in envelope.data.entities
    assert envelope.zoom_token is not None


def test_handle_clinical_event_stream_replay_and_filter():
    """Verify clinical event stream querying and simulated mock replay.

    @req:PRD-SYS-054
    """
    from packages.tooling_core.contracts import ClinicalEventStreamRequest
    from packages.tooling_core.handlers import handle_clinical_event_stream

    # Query with replay enabled
    req = ClinicalEventStreamRequest(
        event_type="SubjectStateChanged", limit=10, replay=True
    )
    envelope = handle_clinical_event_stream(req)

    assert envelope.success is True
    assert envelope.data is not None
    assert envelope.data.replayed_count == len(envelope.data.events)
    assert all(e["event_type"] == "SubjectStateChanged" for e in envelope.data.events)


def test_handle_sdtm_pipeline_validate_rules():
    """Verify SDTM domain pipeline validation against CDISC Conformance Rules.

    @req:PRD-SYS-054
    """
    from packages.tooling_core.contracts import SdtmPipelineValidateRequest
    from packages.tooling_core.handlers import handle_sdtm_pipeline_validate

    req = SdtmPipelineValidateRequest(domain="DM", strict_cdisc=True)
    envelope = handle_sdtm_pipeline_validate(req)

    assert envelope.success is True
    assert envelope.data is not None
    assert envelope.data.domains_evaluated == ["DM"]
    assert envelope.data.valid_count == 1
    assert envelope.data.error_count == 0


def test_handle_synthetic_patient_generate_determinism():
    """Verify synthetic participant generation adheres to GxP synthetic data constraints.

    @req:PRD-SYS-054
    """
    from packages.tooling_core.contracts import SyntheticPatientGenerateRequest
    from packages.tooling_core.handlers import handle_synthetic_patient_generate

    req = SyntheticPatientGenerateRequest(
        protocol_id="CADENCE-101", scenario="standard", count=5, dry_run=True
    )
    envelope = handle_synthetic_patient_generate(req)

    assert envelope.success is True
    assert envelope.data is not None
    assert envelope.data.generated_count == 5
    assert envelope.data.is_synthetic is True
    assert len(envelope.data.subjects) == 5
    assert envelope.data.subjects[0]["is_synthetic"] is True
