"""Unit tests for ClamAVScanAdapter and MockScanAdapter.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

import asyncio
from collections.abc import AsyncIterable

import pytest

from apps.fileshare.adapters.clamav_adapter import ClamAVScanAdapter, MockScanAdapter
from apps.fileshare.domain.models import ScanStatus


async def _async_byte_generator(
    data: bytes, chunk_size: int = 16
) -> AsyncIterable[bytes]:
    for i in range(0, len(data), chunk_size):
        yield data[i : i + chunk_size]


@pytest.mark.asyncio
async def test_mock_scan_adapter_clean_file():
    """Verify MockScanAdapter returns CLEAN for normal non-malicious payloads.

    @req:PRD-DOC-001
    """
    adapter = MockScanAdapter()
    stream = _async_byte_generator(
        b"Normal benign clinical study trial document content."
    )
    result = await adapter.scan_stream(stream)

    assert result.status == ScanStatus.CLEAN
    assert result.threat_name is None
    assert result.engine == "clamav-1.3"
    assert result.is_clean is True


@pytest.mark.asyncio
async def test_mock_scan_adapter_eicar_detection():
    """Verify MockScanAdapter detects standard EICAR test signature and returns INFECTED.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    adapter = MockScanAdapter()
    eicar_payload = (
        b"X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"
    )
    stream = _async_byte_generator(eicar_payload)
    result = await adapter.scan_stream(stream)

    assert result.status == ScanStatus.INFECTED
    assert result.threat_name == "Win.Test.EICAR_HDB-1"
    assert result.is_clean is False


@pytest.mark.asyncio
async def test_mock_scan_adapter_forced_status():
    """Verify MockScanAdapter respects forced override status.

    @req:PRD-DOC-001
    """
    adapter = MockScanAdapter()
    adapter.force_status = ScanStatus.INFECTED
    adapter.force_threat_name = "Trojan.Custom.Test"

    stream = _async_byte_generator(b"Some benign bytes")
    result = await adapter.scan_stream(stream)

    assert result.status == ScanStatus.INFECTED
    assert result.threat_name == "Trojan.Custom.Test"


@pytest.mark.asyncio
async def test_mock_scan_adapter_simulated_error():
    """Verify MockScanAdapter simulates transient scanner errors.

    @req:PRD-DOC-001
    """
    adapter = MockScanAdapter()
    adapter.simulate_error_count = 1

    stream1 = _async_byte_generator(b"Payload 1")
    result1 = await adapter.scan_stream(stream1)
    assert result1.status == ScanStatus.SCAN_ERROR

    stream2 = _async_byte_generator(b"Payload 2")
    result2 = await adapter.scan_stream(stream2)
    assert result2.status == ScanStatus.CLEAN


@pytest.mark.asyncio
async def test_clamav_scan_adapter_clean_response(monkeypatch):
    """Verify ClamAVScanAdapter parses OK clean response correctly.

    @req:PRD-DOC-001
    """

    class MockStreamWriter:
        def __init__(self):
            self.written = []

        def write(self, data: bytes):
            self.written.append(data)

        async def drain(self):
            pass

        def close(self):
            pass

        async def wait_closed(self):
            pass

    class MockStreamReader:
        async def read(self, n: int = 4096) -> bytes:
            return b"stream: OK\x00\n"

    async def mock_open_connection(host, port):
        return MockStreamReader(), MockStreamWriter()

    monkeypatch.setattr(asyncio, "open_connection", mock_open_connection)

    adapter = ClamAVScanAdapter(host="127.0.0.1", port=3310)
    stream = _async_byte_generator(b"Clean file payload")
    result = await adapter.scan_stream(stream)

    assert result.status == ScanStatus.CLEAN
    assert result.threat_name is None
    assert result.is_clean is True


@pytest.mark.asyncio
async def test_clamav_scan_adapter_infected_response(monkeypatch):
    """Verify ClamAVScanAdapter parses FOUND malware response correctly.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """

    class MockStreamWriter:
        def __init__(self):
            self.written = []

        def write(self, data: bytes):
            self.written.append(data)

        async def drain(self):
            pass

        def close(self):
            pass

        async def wait_closed(self):
            pass

    class MockStreamReader:
        async def read(self, n: int = 4096) -> bytes:
            return b"stream: Win.Test.EICAR_HDB-1 FOUND\x00\n"

    async def mock_open_connection(host, port):
        return MockStreamReader(), MockStreamWriter()

    monkeypatch.setattr(asyncio, "open_connection", mock_open_connection)

    adapter = ClamAVScanAdapter(host="127.0.0.1", port=3310)
    stream = _async_byte_generator(b"Malicious payload")
    result = await adapter.scan_stream(stream)

    assert result.status == ScanStatus.INFECTED
    assert result.threat_name == "Win.Test.EICAR_HDB-1"
    assert result.is_clean is False


@pytest.mark.asyncio
async def test_clamav_scan_adapter_connection_failure(monkeypatch):
    """Verify ClamAVScanAdapter safely handles connection errors without raising unhandled exceptions.

    @req:PRD-DOC-001
    """

    async def mock_open_connection_fail(host, port):
        raise ConnectionRefusedError("Connection refused to clamd daemon")

    monkeypatch.setattr(asyncio, "open_connection", mock_open_connection_fail)

    adapter = ClamAVScanAdapter(host="127.0.0.1", port=3310)
    stream = _async_byte_generator(b"Any payload")
    result = await adapter.scan_stream(stream)

    assert result.status == ScanStatus.SCAN_ERROR
    assert "Connection refused" in result.details
