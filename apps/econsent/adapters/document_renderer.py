"""Re-export document renderer from domain layer for backward compatibility."""

from apps.econsent.domain.document_renderer import (
    render_verifiable_consent_html,
)

__all__ = ["render_verifiable_consent_html"]
