"""ScanPort port abstraction for antivirus and malware scanning.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

from abc import ABC, abstractmethod
from collections.abc import AsyncIterable
from dataclasses import dataclass

from apps.fileshare.domain.models import ScanStatus


@dataclass(frozen=True)
class ScanResult:
    """Represents the outcome of a virus/malware inspection."""

    status: ScanStatus
    threat_name: str | None = None
    engine: str = "clamav-1.3"
    details: str | None = None

    @property
    def is_clean(self) -> bool:
        """Helper evaluating if the file is free of malware."""
        return self.status == ScanStatus.CLEAN

    @property
    def is_infected(self) -> bool:
        """Helper evaluating if malware/threat was detected."""
        return self.status == ScanStatus.INFECTED


class ScanPort(ABC):
    """Port for streaming malware inspection engines."""

    @abstractmethod
    async def scan_stream(self, stream: AsyncIterable[bytes]) -> ScanResult:
        """Streams byte chunks through scanner engine and returns ScanResult.

        Args:
            stream: Async iterable yielding binary chunks.

        Returns:
            ScanResult containing status, engine identifier, and threat name if infected.
        """
        pass

    async def scan_bytes(self, data: bytes) -> ScanResult:
        """Convenience method to scan in-memory byte arrays.

        Args:
            data: Binary payload to scan.

        Returns:
            ScanResult.
        """

        async def _generator() -> AsyncIterable[bytes]:
            chunk_size = 65536
            for i in range(0, len(data), chunk_size):
                yield data[i : i + chunk_size]

        return await self.scan_stream(_generator())


__all__ = ["ScanPort", "ScanResult"]
