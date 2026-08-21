"""Unit and contract tests for SentinelEngine and SentinelCheck interface.

@req:PRD-SYS-049
"""

import pytest

from packages.sentinels.base import SentinelCheck, SentinelResult
from packages.sentinels.engine import SentinelEngine


class DummyPassingCheck(SentinelCheck):
    @property
    def name(self) -> str:
        return "dummy-passing"

    @property
    def description(self) -> str:
        return "Dummy check that always passes"

    async def run(self) -> SentinelResult:
        return SentinelResult(
            name=self.name,
            passed=True,
            summary="0 violations",
            duration_seconds=0.01,
        )


class DummyFailingCheck(SentinelCheck):
    @property
    def name(self) -> str:
        return "dummy-failing"

    @property
    def description(self) -> str:
        return "Dummy check that flags violations"

    async def run(self) -> SentinelResult:
        return SentinelResult(
            name=self.name,
            passed=False,
            exit_code=1,
            summary="1 violation found",
            violations=["Violation in file.py"],
            remediation="uv run cadence fix --all",
            duration_seconds=0.02,
        )


@pytest.mark.asyncio
async def test_sentinel_engine_execution():
    """Verify SentinelEngine executes checks concurrently and formats TerminalDocument.

    @req:PRD-SYS-049
    """
    engine = SentinelEngine()
    engine.register(DummyPassingCheck())
    engine.register(DummyFailingCheck())

    results = await engine.execute_all()
    assert len(results) == 2
    assert results[0].passed is True
    assert results[1].passed is False

    doc = engine.to_terminal_document(results)
    assert doc.title == "Architecture Sentinels & Quality Gates"
    payload = doc.to_dict()
    assert payload["metrics"][0]["value"] == 1  # 1 Passed
    assert payload["metrics"][1]["value"] == 1  # 1 Failed
    assert payload["remediation"] == "uv run cadence fix --all"
