#!/usr/bin/env python3
"""Pre-tool execution GxP safety guardrail for Google Antigravity.

Intercepts tool execution payloads to block destructive git commands,
dangerous filesystem operations, and direct manual mutations to locked
GxP compliance artifacts.

Requirements: PRD-SYS-053, ADR-2192
"""

from __future__ import annotations

import json
import re
import sys
from typing import Any

# Prohibited git command patterns
PROHIBITED_GIT_PATTERNS = [
    (
        re.compile(r"\bgit\s+push\s+.*(-f|--force)\b"),
        "Destructive git force push is prohibited.",
    ),
    (
        re.compile(r"\bgit\s+reset\s+--hard\b"),
        "Destructive git hard reset is prohibited.",
    ),
    (re.compile(r"\bgit\s+clean\s+.*-f"), "Destructive git clean is prohibited."),
    (
        re.compile(r"\bgit\s+checkout\s+\.\s*$"),
        "Indiscriminate git checkout of all files is prohibited.",
    ),
    (
        re.compile(r"\bgit\s+branch\s+-D\b"),
        "Force deletion of git branches is prohibited.",
    ),
]

# Prohibited dangerous shell command patterns
PROHIBITED_SHELL_PATTERNS = [
    (
        re.compile(
            r"\brm\s+(-[a-zA-Z]*r[a-zA-Z]*f|[a-zA-Z]*f[a-zA-Z]*r)\s+(/|~|apps|packages|docs)(\s|$|/)"
        ),
        "Recursive wipe of root or top-level project directories is prohibited.",
    ),
    (
        re.compile(r"\bDROP\s+DATABASE\b", re.IGNORECASE),
        "Direct database destruction (DROP DATABASE) is prohibited.",
    ),
    (
        re.compile(r"\bTRUNCATE\s+TABLE\b", re.IGNORECASE),
        "Direct table truncation (TRUNCATE TABLE) without migration is prohibited.",
    ),
    (
        re.compile(r"\bsudo\s+"),
        "Privileged execution (sudo) is prohibited in agent sandbox.",
    ),
    (
        re.compile(r"\bchmod\s+777\b"),
        "Indiscriminate permission elevation (chmod 777) is prohibited.",
    ),
]

# Locked GxP compliance documents that must only be generated via automation
LOCKED_GXP_ARTIFACTS = [
    "docs/SDLC/Requirements_Traceability_Matrix.md",
    "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
]


def evaluate_command_safety(command_line: str) -> tuple[bool, str | None]:
    """Evaluates whether a shell command is safe to execute under GxP rules.

    Args:
        command_line: The raw shell command string.

    Returns:
        Tuple of (is_allowed, optional_denial_reason).
    """
    cmd = command_line.strip()
    if not cmd:
        return True, None

    for pattern, reason in PROHIBITED_GIT_PATTERNS:
        if pattern.search(cmd):
            return False, f"GxP Git Safety Guardrail: {reason}"

    for pattern, reason in PROHIBITED_SHELL_PATTERNS:
        if pattern.search(cmd):
            return False, f"GxP System Safety Guardrail: {reason}"

    return True, None


def evaluate_file_mutation_safety(target_file: str) -> tuple[bool, str | None]:
    """Evaluates whether a target file path may be manually mutated by an agent.

    Args:
        target_file: The target file path string.

    Returns:
        Tuple of (is_allowed, optional_denial_reason).
    """
    normalized = target_file.replace("\\", "/").strip()
    for locked in LOCKED_GXP_ARTIFACTS:
        if normalized == locked or normalized.endswith(f"/{locked}"):
            return (
                False,
                f"GxP Compliance Guardrail: Direct manual editing of '{locked}' is prohibited. "
                "Regenerate automatically via 'python scripts/sync_gxp.py'.",
            )

    return True, None


def handle_pre_tool_payload(payload: dict[str, Any]) -> dict[str, Any]:
    """Processes incoming PreToolUse JSON payload and computes decision.

    Args:
        payload: Input dictionary received from Antigravity lifecycle hook.

    Returns:
        Decision dictionary with decision and optional reason.
    """
    tool_call = payload.get("toolCall", {})
    name = tool_call.get("name", "")
    args = tool_call.get("args", {})

    if name == "run_command":
        cmd = args.get("CommandLine", "")
        allowed, reason = evaluate_command_safety(cmd)
        if not allowed:
            return {"decision": "deny", "reason": reason}
        return {"decision": "allow"}

    if name in ("write_to_file", "replace_file_content"):
        target_file = args.get("TargetFile", "")
        allowed, reason = evaluate_file_mutation_safety(target_file)
        if not allowed:
            return {"decision": "deny", "reason": reason}
        return {"decision": "allow"}

    return {"decision": "allow"}


def main() -> int:
    """CLI entrypoint reading protojson from stdin and writing decision to stdout."""
    raw_input = sys.stdin.read().strip()
    if not raw_input:
        print(json.dumps({"decision": "allow"}))
        return 0

    try:
        payload = json.loads(raw_input)
        response = handle_pre_tool_payload(payload)
    except Exception as exc:
        # Fallback to safe allow with non-blocking error log
        sys.stderr.write(
            f"[WARNING] [pre_tool_guardrail.py] Error processing hook input: {exc}\n"
        )
        response = {"decision": "allow"}

    print(json.dumps(response))
    return 0


if __name__ == "__main__":
    sys.exit(main())
