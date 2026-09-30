"""Ports and Protocols for Digital Data Flow (DDF) Extensibility."""

from abc import ABC, abstractmethod

from packages.ddf.models import AccessibilityReport


class AccessibilityValidatorPort(ABC):
    """Port interface for evaluating XML/XForms layout and WCAG accessibility."""

    @abstractmethod
    async def validate(self, openrosa_xml: str) -> AccessibilityReport:
        """Evaluates layout accessibility on the given OpenRosa XML string.

        Args:
            openrosa_xml: Rendered OpenRosa XForms XML string.

        Returns:
            AccessibilityReport containing violation and pass metrics.
        """
        ...
