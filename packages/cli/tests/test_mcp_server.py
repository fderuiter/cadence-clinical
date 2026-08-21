"""Unit and protocol tests for the native Stdio MCP Server.

@req:PRD-SYS-049
"""

import json

from packages.cli.mcp.server import CadenceMcpServer


def test_mcp_server_initialize():
    """Verify MCP JSON-RPC initialize handshake returns server info and capabilities.

    @req:PRD-SYS-049
    """
    server = CadenceMcpServer()
    req = {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {}}
    res = server.handle_message(req)

    assert res["jsonrpc"] == "2.0"
    assert res["id"] == 1
    assert res["result"]["serverInfo"]["name"] == "cadence-clinical-mcp"
    assert "tools" in res["result"]["capabilities"]


def test_mcp_server_tools_list():
    """Verify tools/list exposes all 6 workflow tools with valid input schemas.

    @req:PRD-SYS-049
    """
    server = CadenceMcpServer()
    req = {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}}
    res = server.handle_message(req)

    assert res["jsonrpc"] == "2.0"
    tools = res["result"]["tools"]
    tool_names = [t["name"] for t in tools]
    assert "doctor_diagnose" in tool_names
    assert "run_sentinels" in tool_names
    assert "run_fast_tests" in tool_names
    assert "seed_clinical_scenario" in tool_names
    assert "sync_gxp_compliance" in tool_names
    assert "introspect_service_contracts" in tool_names
    assert "inspect_zoom_target" in tool_names

    for tool in tools:
        assert "description" in tool
        assert "inputSchema" in tool
        assert tool["inputSchema"]["type"] == "object"


def test_mcp_server_tool_call_doctor():
    """Verify tools/call executes doctor_diagnose and returns structured response envelope.

    @req:PRD-SYS-049
    """
    server = CadenceMcpServer()
    req = {
        "jsonrpc": "2.0",
        "id": 3,
        "method": "tools/call",
        "params": {
            "name": "doctor_diagnose",
            "arguments": {"auto_heal": False, "summary": True},
        },
    }
    res = server.handle_message(req)
    assert res["id"] == 3
    content = res["result"]["content"][0]["text"]
    payload = json.loads(content)
    assert "status" in payload
    assert "summary" in payload
    assert "metrics" in payload
    assert "zoom_token" in payload


def test_mcp_server_zoom_inspection():
    """Verify inspect_zoom_target unpacks detailed logs corresponding to a zoom token.

    @req:PRD-SYS-049
    """
    from packages.tooling_core.handlers import register_zoom_payload

    token = "zoom-test-inspect-token-123"
    register_zoom_payload(
        token, "test_trace", "Traceback line 1\nTraceback line 2\nTraceback line 3"
    )

    server = CadenceMcpServer()
    req = {
        "jsonrpc": "2.0",
        "id": 4,
        "method": "tools/call",
        "params": {
            "name": "inspect_zoom_target",
            "arguments": {"zoom_token": token, "offset": 0, "limit": 2},
        },
    }
    res = server.handle_message(req)
    assert res["id"] == 4
    payload = json.loads(res["result"]["content"][0]["text"])
    assert payload["success"] is True
    assert payload["data"]["total_lines"] == 3
    assert payload["data"]["lines"] == ["Traceback line 1", "Traceback line 2"]
    assert payload["data"]["has_more"] is True


def test_mcp_server_tool_call_invalid_tool():
    """Verify tools/call returns error for unknown tool.

    @req:PRD-SYS-049
    """
    server = CadenceMcpServer()
    req = {
        "jsonrpc": "2.0",
        "id": 5,
        "method": "tools/call",
        "params": {
            "name": "unknown_tool_xyz",
            "arguments": {},
        },
    }
    res = server.handle_message(req)
    assert "error" in res
    assert res["error"]["code"] == -32601
