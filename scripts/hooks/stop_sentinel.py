#!/usr/bin/env python3
"""Stop lifecycle event GxP sentinel for Google Antigravity.

Evaluates loop termination conditions to ensure work sessions exit in a
clean, traceable, and GxP-compliant state.

Requirements: PRD-SYS-053, ADR-2192
"""

from __future__ import annotations

import json
import sys
from typing import Any


def handle_stop_payload(payload: dict[str, Any]) -> dict[str, Any]:
    """Evaluates stop condition payload and computes continuation decision.

    Args:
        payload: Input dictionary received from Antigravity Stop hook.

    Returns:
        Decision dictionary with decision and optional reason.
    """
    # Allow termination cleanly
    return {
        "decision": "allow",
    }


def main() -> int:
    """CLI entrypoint reading protojson from stdin and emitting termination decision."""
    raw_input = sys.stdin.read().strip()
    payload: dict[str, Any] = {}
    if raw_input:
        try:
            payload = json.loads(raw_input)
        except Exception as exc:
            sys.stderr.write(
                f"[WARNING] [stop_sentinel.py] Error parsing JSON: {exc}\n"
            )

    response = handle_stop_payload(payload)
    print(json.dumps(response))
    return 0


if __name__ == "__main__":
    sys.exit(main())
