"""Decoupled service handlers for developer tooling operations.

Requirements: PRD-SYS-049, ADR-2190
"""

from __future__ import annotations

import subprocess
import time
from pathlib import Path
from typing import Any

from packages.tooling_core.contracts import (
    CommandEnvelope,
    FastTestRequest,
    FastTestResponse,
    ZoomInspectRequest,
    ZoomInspectResponse,
)

# In-memory zoom registry mapping tokens to raw string logs/traces
_ZOOM_REGISTRY: dict[str, dict[str, Any]] = {}


def register_zoom_payload(token: str, entity_type: str, raw_content: str) -> str:
    """Stores detailed payload in zoom registry with progressive disclosure token."""
    lines = raw_content.strip().splitlines()
    _ZOOM_REGISTRY[token] = {
        "entity_type": entity_type,
        "lines": lines,
        "created_at": time.time(),
    }
    return token


def handle_zoom_inspect(
    req: ZoomInspectRequest,
) -> CommandEnvelope[ZoomInspectResponse]:
    """Unpacks granular logs or traces corresponding to a zoom token."""
    entry = _ZOOM_REGISTRY.get(req.zoom_token)
    if not entry:
        return CommandEnvelope(
            success=False,
            exit_code=1,
            summary={"error": f"Zoom token {req.zoom_token} not found or expired"},
            remediation="Re-run the parent tool to generate a fresh zoom token",
        )

    all_lines = entry["lines"]
    total = len(all_lines)
    sliced = all_lines[req.offset : req.offset + req.limit]
    has_more = (req.offset + req.limit) < total

    resp = ZoomInspectResponse(
        zoom_token=req.zoom_token,
        entity_type=entry["entity_type"],
        total_lines=total,
        lines=sliced,
        has_more=has_more,
    )
    return CommandEnvelope(
        success=True,
        exit_code=0,
        summary={
            "total_lines": total,
            "returned_lines": len(sliced),
            "has_more": has_more,
        },
        data=resp,
    )


def handle_fast_tests(
    req: FastTestRequest, repo_root: Path | None = None
) -> CommandEnvelope[FastTestResponse]:
    """Runs fast unit and contract tests with in-memory isolation."""
    root = repo_root or Path(__file__).resolve().parents[2]
    cmd = ["uv", "run", "pytest", "--no-cov", "-m", "not integration and not e2e"]

    if req.subsystem:
        cmd.append(req.subsystem)
    else:
        cmd.extend(["packages/testing/tests/", "packages/cli/tests/"])

    if req.failed_first:
        cmd.append("--failed-first")

    start = time.time()
    res = subprocess.run(cmd, cwd=str(root), capture_output=True, text=True)
    duration = round(time.time() - start, 3)

    token = f"zoom-fast-tests-{int(time.time())}"
    register_zoom_payload(token, "pytest_stdout", res.stdout + "\n" + res.stderr)

    success = res.returncode == 0
    remediation = None if success else "uv run cadence test --fast --failed-first"

    resp = FastTestResponse(
        passed=0 if not success else 1,
        failed=1 if not success else 0,
        duration_seconds=duration,
        command=cmd,
    )

    return CommandEnvelope(
        success=success,
        exit_code=res.returncode,
        summary={"duration": f"{duration}s", "passed": success},
        remediation=remediation,
        zoom_token=token,
        data=resp,
    )
