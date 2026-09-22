"""ClamAV virus and malware scanner adapter implementing ScanPort.

Connects to ClamAV daemon via TCP socket and streams binary payloads using clamd INSTREAM protocol.
Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import asyncio
import logging
import os
import struct
from collections.abc import AsyncIterable

from apps.fileshare.domain.models import ScanStatus
from apps.fileshare.ports.scan_port import ScanPort, ScanResult

logger = logging.getLogger("apps.fileshare.clamav")

EICAR_SIGNATURE = b"EICAR-STANDARD-ANTIVIRUS-TEST-FILE"


class ClamAVScanAdapter(ScanPort):
    """Production ClamAV TCP socket streaming adapter."""

    def __init__(
        self,
        host: str | None = None,
        port: int | None = None,
        timeout: float = 30.0,
        engine_name: str = "clamav-1.3",
    ) -> None:
        from packages.config import get_fileshare_settings

        settings = get_fileshare_settings()
        self.host = host if host is not None else settings.clamav_host
        self.port = port if port is not None else settings.clamav_port
        self.timeout = timeout
        self.engine_name = engine_name

    async def scan_stream(self, stream: AsyncIterable[bytes]) -> ScanResult:
        """Stream bytes to ClamAV over TCP socket using zINSTREAM protocol."""
        reader = None
        writer = None
        try:
            connect_coro = asyncio.open_connection(self.host, self.port)
            reader, writer = await asyncio.wait_for(connect_coro, timeout=self.timeout)

            # Initiate INSTREAM scan
            writer.write(b"zINSTREAM\x00")
            await writer.drain()

            # Stream chunks with 4-byte big-endian length prefix
            async for chunk in stream:
                if not chunk:
                    continue
                length_prefix = struct.pack("!I", len(chunk))
                writer.write(length_prefix + chunk)
                await writer.drain()

            # Signal end of stream with 0-length prefix
            writer.write(struct.pack("!I", 0))
            await writer.drain()

            # Read response
            raw_response = await asyncio.wait_for(
                reader.read(4096), timeout=self.timeout
            )
            response_text = (
                raw_response.decode("utf-8", errors="replace").strip("\x00\r\n").strip()
            )

            logger.info("ClamAV response received: %s", response_text)

            if "OK" in response_text:
                return ScanResult(
                    status=ScanStatus.CLEAN,
                    engine=self.engine_name,
                    details=response_text,
                )

            if "FOUND" in response_text:
                # e.g., "stream: Win.Test.EICAR_HDB-1 FOUND"
                cleaned = (
                    response_text.replace("stream:", "").replace("FOUND", "").strip()
                )
                threat_name = cleaned or "Malware.Detected"
                return ScanResult(
                    status=ScanStatus.INFECTED,
                    threat_name=threat_name,
                    engine=self.engine_name,
                    details=response_text,
                )

            # Error string returned by clamd (e.g., INSTREAM size limit exceeded)
            return ScanResult(
                status=ScanStatus.SCAN_ERROR,
                engine=self.engine_name,
                details=response_text or "Unknown response from scanner",
            )

        except (TimeoutError, OSError, Exception) as exc:
            logger.warning(
                "ClamAV scan connection or execution failed: %s (%s:%s)",
                exc,
                self.host,
                self.port,
            )
            return ScanResult(
                status=ScanStatus.SCAN_ERROR,
                engine=self.engine_name,
                details=f"Scanner communication error: {exc}",
            )
        finally:
            if writer:
                try:
                    writer.close()
                    await writer.wait_closed()
                except Exception:
                    pass


class MockScanAdapter(ScanPort):
    """Test fake for virus scanning with automatic EICAR string detection."""

    def __init__(
        self,
        default_status: ScanStatus = ScanStatus.CLEAN,
        engine_name: str = "clamav-1.3",
    ) -> None:
        self.default_status = default_status
        self.engine_name = engine_name
        self.force_status: ScanStatus | None = None
        self.force_threat_name: str | None = None
        self.simulate_error_count: int = 0
        self.scan_count: int = 0

    async def scan_stream(self, stream: AsyncIterable[bytes]) -> ScanResult:
        """Scans stream in memory and checks for forced status or EICAR string."""
        self.scan_count += 1

        if self.simulate_error_count > 0:
            self.simulate_error_count -= 1
            return ScanResult(
                status=ScanStatus.SCAN_ERROR,
                engine=self.engine_name,
                details="Simulated scanner error",
            )

        if self.force_status is not None:
            return ScanResult(
                status=self.force_status,
                threat_name=self.force_threat_name,
                engine=self.engine_name,
                details="Forced mock status",
            )

        # Buffer payload to inspect for EICAR signature
        chunks: list[bytes] = []
        async for chunk in stream:
            chunks.append(chunk)

        full_payload = b"".join(chunks)
        if EICAR_SIGNATURE in full_payload:
            return ScanResult(
                status=ScanStatus.INFECTED,
                threat_name="Win.Test.EICAR_HDB-1",
                engine=self.engine_name,
                details="EICAR standard test virus signature detected",
            )

        return ScanResult(
            status=self.default_status,
            engine=self.engine_name,
            details="Clean",
        )


_global_scan_adapter: ScanPort | None = None


def set_scan_adapter(adapter: ScanPort) -> None:
    """Explicitly registers the global ScanPort adapter (used in test fixtures)."""
    global _global_scan_adapter
    _global_scan_adapter = adapter


def get_scan_adapter() -> ScanPort:
    """Resolves the active ScanPort adapter for fileshare operations."""
    global _global_scan_adapter
    if _global_scan_adapter is not None:
        return _global_scan_adapter

    return ClamAVScanAdapter()


__all__ = [
    "ClamAVScanAdapter",
    "MockScanAdapter",
    "get_scan_adapter",
    "set_scan_adapter",
]
