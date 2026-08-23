"""Ports and Protocols for Digital Data Flow (DDF) Extensibility."""

from typing import Protocol, runtime_checkable

from packages.ddf.models import AccessibilityReport


@runtime_checkable
class AccessibilityValidatorPort(Protocol):
    """Port interface for evaluating XML/XForms layout and WCAG accessibility."""

    async def validate(self, openrosa_xml: str) -> AccessibilityReport:
        """Evaluates layout accessibility on the given OpenRosa XML string.

        Args:
            openrosa_xml: Rendered OpenRosa XForms XML string.

        Returns:
            AccessibilityReport containing violation and pass metrics.
        """
        ...
