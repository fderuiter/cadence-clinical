"""Unit and contract tests for Antigravity GxP Lifecycle Hooks & AI Coding Guardrails.

@req:PRD-SYS-053
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

from scripts.hooks.post_write_check import handle_post_tool_payload
from scripts.hooks.pre_invocation_reminder import handle_pre_invocation_payload
from scripts.hooks.pre_tool_guardrail import (
    evaluate_command_safety,
    evaluate_file_mutation_safety,
    handle_pre_tool_payload,
)
from scripts.hooks.stop_sentinel import handle_stop_payload

REPO_ROOT = Path(__file__).resolve().parents[2]


def test_evaluate_command_safety_prohibited_git_commands():
    """Verify destructive git operations are denied with explicit GxP reason.

    @req:PRD-SYS-053
    """
    prohibited = [
        "git push origin main --force",
        "git push -f",
        "git push origin head -f",
        "git reset --hard HEAD~1",
        "git reset --hard",
        "git clean -fdx",
        "git clean -f",
        "git checkout .",
        "git branch -D feature-branch",
    ]
    for cmd in prohibited:
        allowed, reason = evaluate_command_safety(cmd)
        assert allowed is False, f"Expected '{cmd}' to be prohibited"
        assert reason is not None
        assert "GxP Git Safety Guardrail" in reason


def test_evaluate_command_safety_allowed_commands():
    """Verify standard developer and test commands are allowed.

    @req:PRD-SYS-053
    """
    allowed_commands = [
        "git status",
        "git log -n 5",
        "git diff",
        "git add .",
        "git commit -m 'feat: test'",
        "uv run pytest",
        "uv run cadence test --fast",
        "uv run cadence check",
        "uv run python scripts/sync_gxp.py",
        "ruff check .",
    ]
    for cmd in allowed_commands:
        allowed, reason = evaluate_command_safety(cmd)
        assert allowed is True, f"Expected '{cmd}' to be allowed"
        assert reason is None


def test_evaluate_command_safety_prohibited_system_commands():
    """Verify dangerous system wipes and unauthorized drops are denied.

    @req:PRD-SYS-053
    """
    dangerous = [
        "rm -rf /",
        "rm -rf ~",
        "rm -rf apps",
        "rm -rf packages",
        "rm -rf docs",
        "DROP DATABASE clinical_trial",
        "drop database execution;",
        "TRUNCATE TABLE audit_log",
        "sudo rm -f something",
        "chmod 777 sensitive.key",
    ]
    for cmd in dangerous:
        allowed, reason = evaluate_command_safety(cmd)
        assert allowed is False, f"Expected '{cmd}' to be prohibited"
        assert reason is not None
        assert "GxP System Safety Guardrail" in reason


def test_evaluate_file_mutation_safety_locked_gxp_docs():
    """Verify direct manual edits to locked GxP compliance docs are blocked.

    @req:PRD-SYS-053
    """
    locked_files = [
        "docs/SDLC/Requirements_Traceability_Matrix.md",
        "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
        f"{REPO_ROOT}/docs/SDLC/Requirements_Traceability_Matrix.md",
        f"{REPO_ROOT}/docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
    ]
    for path in locked_files:
        allowed, reason = evaluate_file_mutation_safety(path)
        assert allowed is False, f"Expected editing '{path}' to be blocked"
        assert reason is not None
        assert "GxP Compliance Guardrail" in reason
        assert "sync_gxp.py" in reason


def test_evaluate_file_mutation_safety_allowed_files():
    """Verify edits to regular application, documentation, and test files are allowed.

    @req:PRD-SYS-053
    """
    allowed_files = [
        "apps/execution/main.py",
        "packages/cli/formatting.py",
        "docs/specs/01-antigravity-gxp-lifecycle-hooks.md",
        "scripts/tests/test_antigravity_gxp_hooks.py",
    ]
    for path in allowed_files:
        allowed, reason = evaluate_file_mutation_safety(path)
        assert allowed is True, f"Expected editing '{path}' to be allowed"
        assert reason is None


def test_handle_pre_tool_payload_run_command():
    """Verify handle_pre_tool_payload processes run_command payloads accurately.

    @req:PRD-SYS-053
    """
    denied_payload = {
        "toolCall": {
            "name": "run_command",
            "args": {"CommandLine": "git push --force origin main"},
        },
        "stepIdx": 1,
    }
    resp = handle_pre_tool_payload(denied_payload)
    assert resp["decision"] == "deny"
    assert "reason" in resp

    allowed_payload = {
        "toolCall": {
            "name": "run_command",
            "args": {"CommandLine": "uv run pytest"},
        },
        "stepIdx": 2,
    }
    resp = handle_pre_tool_payload(allowed_payload)
    assert resp["decision"] == "allow"


def test_handle_pre_tool_payload_file_write():
    """Verify handle_pre_tool_payload processes file write payloads accurately.

    @req:PRD-SYS-053
    """
    locked_payload = {
        "toolCall": {
            "name": "write_to_file",
            "args": {"TargetFile": "docs/SDLC/Requirements_Traceability_Matrix.md"},
        },
        "stepIdx": 3,
    }
    resp = handle_pre_tool_payload(locked_payload)
    assert resp["decision"] == "deny"
    assert "sync_gxp.py" in resp["reason"]

    regular_payload = {
        "toolCall": {
            "name": "replace_file_content",
            "args": {"TargetFile": "apps/execution/services/observation_service.py"},
        },
        "stepIdx": 4,
    }
    resp = handle_pre_tool_payload(regular_payload)
    assert resp["decision"] == "allow"


def test_post_write_check_contract():
    """Verify PostToolUse handler adheres to empty JSON output contract.

    @req:PRD-SYS-053
    """
    payload = {"stepIdx": 5, "conversationId": "test-uuid"}
    resp = handle_post_tool_payload(payload)
    assert resp == {}


def test_pre_invocation_reminder_contract():
    """Verify PreInvocation handler injects GxP ephemeral compliance message.

    @req:PRD-SYS-053
    """
    payload = {"invocationNum": 1, "conversationId": "test-uuid"}
    resp = handle_pre_invocation_payload(payload)
    assert "injectSteps" in resp
    assert len(resp["injectSteps"]) == 1
    assert "ephemeralMessage" in resp["injectSteps"][0]
    assert "PRD-SYS-053" in resp["injectSteps"][0]["ephemeralMessage"]


def test_stop_sentinel_contract():
    """Verify Stop sentinel handler permits clean session termination.

    @req:PRD-SYS-053
    """
    payload = {
        "executionNum": 1,
        "terminationReason": "model_stop",
        "fullyIdle": True,
    }
    resp = handle_stop_payload(payload)
    assert resp["decision"] == "allow"


def test_hooks_json_configuration_integrity():
    """Verify .agents/hooks.json exists, is valid JSON, and maps to existing scripts.

    @req:PRD-SYS-053
    """
    hooks_file = REPO_ROOT / ".agents" / "hooks.json"
    assert hooks_file.exists(), ".agents/hooks.json must exist"

    data = json.loads(hooks_file.read_text())
    assert "gxp-guardrails" in data
    group = data["gxp-guardrails"]
    assert group["enabled"] is True

    # Check PreToolUse
    assert "PreToolUse" in group
    pre_tool_cmd = group["PreToolUse"][0]["hooks"][0]["command"]
    assert "scripts/hooks/pre_tool_guardrail.py" in pre_tool_cmd
    assert (REPO_ROOT / "scripts/hooks/pre_tool_guardrail.py").exists()

    # Check PostToolUse
    assert "PostToolUse" in group
    post_tool_cmd = group["PostToolUse"][0]["hooks"][0]["command"]
    assert "scripts/hooks/post_write_check.py" in post_tool_cmd
    assert (REPO_ROOT / "scripts/hooks/post_write_check.py").exists()

    # Check PreInvocation
    assert "PreInvocation" in group
    pre_inv_cmd = group["PreInvocation"][0]["command"]
    assert "scripts/hooks/pre_invocation_reminder.py" in pre_inv_cmd
    assert (REPO_ROOT / "scripts/hooks/pre_invocation_reminder.py").exists()

    # Check Stop
    assert "Stop" in group
    stop_cmd = group["Stop"][0]["command"]
    assert "scripts/hooks/stop_sentinel.py" in stop_cmd
    assert (REPO_ROOT / "scripts/hooks/stop_sentinel.py").exists()


def test_subprocess_stdin_stdout_pipeline():
    """Verify hook scripts execute via stdio subprocess with valid JSON serialization.

    @req:PRD-SYS-053
    """
    # 1. Test pre_tool_guardrail denial via stdin
    deny_input = json.dumps(
        {
            "toolCall": {
                "name": "run_command",
                "args": {"CommandLine": "git push --force"},
            }
        }
    )
    res_deny = subprocess.run(
        [sys.executable, str(REPO_ROOT / "scripts/hooks/pre_tool_guardrail.py")],
        input=deny_input,
        capture_output=True,
        text=True,
        check=True,
    )
    deny_json = json.loads(res_deny.stdout)
    assert deny_json["decision"] == "deny"

    # 2. Test pre_invocation_reminder injection via stdin
    inv_input = json.dumps({"invocationNum": 2})
    res_inv = subprocess.run(
        [sys.executable, str(REPO_ROOT / "scripts/hooks/pre_invocation_reminder.py")],
        input=inv_input,
        capture_output=True,
        text=True,
        check=True,
    )
    inv_json = json.loads(res_inv.stdout)
    assert "injectSteps" in inv_json
