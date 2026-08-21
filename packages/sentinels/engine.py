"""Concurrent orchestration engine for Sentinel quality gates.

Requirements: PRD-SYS-049, ADR-2190
"""

from __future__ import annotations

import asyncio
from pathlib import Path

from packages.cli.formatting import TerminalDocument
from packages.sentinels.base import SentinelCheck, SentinelResult


class SentinelEngine:
    """Coordinates and executes registered Sentinel checks concurrently."""

    def __init__(self, repo_root: Path | None = None) -> None:
        self.repo_root = repo_root or Path(__file__).resolve().parents[2]
        self._checks: list[SentinelCheck] = []

    def register(self, check: SentinelCheck) -> None:
        """Registers a SentinelCheck instance with the engine."""
        self._checks.append(check)

    async def execute_all(self, target_gate: str | None = None) -> list[SentinelResult]:
        """Executes checks concurrently and returns results."""
        selected = [c for c in self._checks if not target_gate or c.name == target_gate]
        if not selected:
            return []

        tasks = [check.run() for check in selected]
        return await asyncio.gather(*tasks)

    def to_terminal_document(self, results: list[SentinelResult]) -> TerminalDocument:
        """Formats sentinel execution results into an authored TerminalDocument."""
        passed = sum(1 for r in results if r.passed)
        failed = sum(1 for r in results if not r.passed)
        total_time = sum(r.duration_seconds for r in results)

        doc = TerminalDocument(
            title="Architecture Sentinels & Quality Gates",
            subtitle=f"{len(results)} sentinel gates evaluated concurrently",
        )
        doc.add_metric("Passed", passed, style="green")
        doc.add_metric("Failed", failed, style="red" if failed > 0 else "green")
        doc.add_metric("Total Time", f"{round(total_time, 2)}s", style="cyan")

        first_remediation = None
        for r in results:
            stat = "pass" if r.passed else "fail"
            detail = f"({r.duration_seconds}s) {r.summary}"
            doc.add_item(r.name, status=stat, detail=detail)
            if not r.passed and not first_remediation and r.remediation:
                first_remediation = r.remediation

        if failed == 0:
            doc.set_cta("Run 'uv run cadence test --fast' for inner-loop testing")
        else:
            doc.set_cta("Review and resolve failed sentinel gates above")
            if first_remediation:
                doc.set_remediation(first_remediation)

        return doc
