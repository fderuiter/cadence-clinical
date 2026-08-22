"""ConversionPort interface for converting Office documents to PDF renditions.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

from abc import ABC, abstractmethod


class ConversionPort(ABC):
    """Port interface for converting office document formats into PDF renditions."""

    @abstractmethod
    async def convert_to_pdf(
        self, source_key: str, destination_key: str | None = None
    ) -> str:
        """Converts a document stored at source_key into a PDF rendition in object storage.

        Args:
            source_key: Object storage key of the source document.
            destination_key: Optional custom destination key for the PDF rendition.

        Returns:
            The object storage key of the generated PDF rendition.
        """
        raise NotImplementedError


__all__ = ["ConversionPort"]
