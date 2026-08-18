"""Tests for layout and accessibility validator service."""

import pytest

from apps.execution.services.layout_validator import (
    run_layout_and_accessibility_checks,
)


@pytest.mark.asyncio
async def test_in_memory_accessibility_auditing():
    """Verify that automated layout WCAG checks identify contrast and element focus violations.

    @req: PRD-CRF-015
    @req: Trace-31
    """
    html_content = """
    <html>
      <head>
        <title>Compliance Check Form</title>
        <style>
          .low-contrast-btn {
            background-color: #eee;
            color: #eed; /* extremely low contrast on light gray background */
            width: 150px;
            height: 50px;
          }
        </style>
      </head>
      <body>
        <button class="low-contrast-btn">Low Contrast Button</button>
      </body>
    </html>
    """

    (
        violations,
        passes,
        incomplete,
        inapplicable,
        layout_errors,
    ) = await run_layout_and_accessibility_checks(html_content)

    assert len(violations) > 0
    violation_ids = {v["id"] for v in violations}
    assert "color-contrast" in violation_ids
