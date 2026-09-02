#!/usr/bin/env python3
"""Pre-invocation GxP context injector for Google Antigravity.

Injects in-flight ephemeral compliance reminders and 21 CFR Part 11
guidelines into agent reasoning loops before model generation.

Requirements: PRD-SYS-053, ADR-2192
"""

from __future__ import annotations

import json
import sys
from typing import Any

GXP_EPHEMERAL_REMINDER = (
    "GxP Sentinel (PRD-SYS-053): Enforce 21 CFR Part 11 standards across all changes: "
    "1) Adhere to C# 14 / .NET 10 target standards (file-scoped namespaces, nullable enabled), "
    "2) Sort using directives (System first, then alphabetical), "
    "3) Add @req: and [GxPRequirement] to all test docstrings/attributes, and "
    "4) Sync compliance docs via 'python scripts/sync_gxp.py'."
)


def handle_pre_invocation_payload(payload: dict[str, Any]) -> dict[str, Any]:
    """Generates ephemeral context injection steps for the incoming model invocation.

    Args:
        payload: Input dictionary received from Antigravity PreInvocation hook.

    Returns:
        Dictionary containing injectSteps array.
    """
    return {
        "injectSteps": [
            {
                "ephemeralMessage": GXP_EPHEMERAL_REMINDER,
            }
        ]
    }


def main() -> int:
    """CLI entrypoint reading protojson from stdin and emitting injectSteps to stdout."""
    raw_input = sys.stdin.read().strip()
    payload: dict[str, Any] = {}
    if raw_input:
        try:
            payload = json.loads(raw_input)
        except Exception as exc:
            sys.stderr.write(
                f"[WARNING] [pre_invocation_reminder.py] Error parsing JSON: {exc}\n"
            )

    response = handle_pre_invocation_payload(payload)
    print(json.dumps(response))
    return 0


if __name__ == "__main__":
    sys.exit(main())
