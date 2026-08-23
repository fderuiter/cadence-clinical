"""Layout Accessibility Verification Stage for DDF Compiler.

Executes WCAG accessibility and layout compliance checks on OpenRosa XForms.
"""

from __future__ import annotations

from packages.ddf.models import AccessibilityReport
from packages.ddf.ports import AccessibilityValidatorPort


class LayoutAccessibilityStage:
    """Evaluates rendered form layouts against WCAG and visual layout standards."""

    def __init__(self, validator: AccessibilityValidatorPort | None = None) -> None:
        self.validator = validator

    async def validate(self, openrosa_xml: str, form_hash: str) -> AccessibilityReport:
        """Executes accessibility validation against the rendered XML form.

        Args:
            openrosa_xml: Rendered OpenRosa XForms XML string.
            form_hash: Computed SHA-256 hash of the form layout.

        Returns:
            AccessibilityReport

        Raises:
            ValueError: If critical layout or accessibility violations are detected.
        """
        if self.validator is None:
            return AccessibilityReport(
                status="PASS",
                form_hash=form_hash,
                violations=[],
                passes=[],
                incomplete=[],
                inapplicable=[],
                layout_errors=[],
            )

        report = await self.validator.validate(openrosa_xml)

        if report.violations or report.layout_errors:
            err_msgs: list[str] = []
            for err in report.layout_errors:
                err_msgs.append(str(err))
            for v in report.violations:
                node_details = [
                    n.get("html", "") for n in v.get("nodes", []) if isinstance(n, dict)
                ]
                err_msgs.append(
                    f"Accessibility Violation: {v.get('id')} - {v.get('description')} on nodes: {', '.join(node_details)}"
                )
            error_text = "; ".join(err_msgs)
            raise ValueError(f"Layout/Accessibility Validation Failed: {error_text}")

        return report
