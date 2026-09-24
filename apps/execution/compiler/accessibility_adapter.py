"""Accessibility Validator Adapter for Execution Service.

Adapts the Playwright/axe-core layout validator to the DDF AccessibilityValidatorPort protocol.
"""

from apps.execution.services.layout_validator import run_layout_and_accessibility_checks
from packages.ddf.models import AccessibilityReport
from packages.ddf.ports import AccessibilityValidatorPort


class PlaywrightAccessibilityValidatorAdapter(AccessibilityValidatorPort):
    """Executes Playwright and axe-core accessibility evaluations."""

    async def validate(self, openrosa_xml: str) -> AccessibilityReport:
        """Runs physical layout and WCAG accessibility validations."""
        (
            violations,
            passes,
            incomplete,
            inapplicable,
            layout_errors,
        ) = await run_layout_and_accessibility_checks(openrosa_xml)

        status = "FAIL" if (violations or layout_errors) else "PASS"

        import hashlib

        form_hash = hashlib.sha256(openrosa_xml.encode("utf-8")).hexdigest()

        return AccessibilityReport(
            status=status,
            form_hash=form_hash,
            violations=violations,
            passes=passes,
            incomplete=incomplete,
            inapplicable=inapplicable,
            layout_errors=layout_errors,
        )
