"""Base interfaces and data models for Cadence Sentinel checks.

Requirements: PRD-SYS-049, ADR-2190
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from pathlib import Path

from pydantic import BaseModel, Field


class SentinelResult(BaseModel):
    """Outcome of a single sentinel validation check."""

    name: str
    passed: bool
    exit_code: int = 0
    duration_seconds: float = 0.0
    summary: str = ""
    violations: list[str] = Field(default_factory=list)
    remediation: str | None = None


class SentinelCheck(ABC):
    """Abstract base class for modular repository sentinel checks."""

    def __init__(self, repo_root: Path | None = None) -> None:
        self.repo_root = repo_root or Path(__file__).resolve().parents[2]

    @property
    @abstractmethod
    def name(self) -> str:
        """Unique identifier name of the sentinel gate."""
        ...

    @property
    @abstractmethod
    def description(self) -> str:
        """Human-readable description of what the sentinel validates."""
        ...

    @abstractmethod
    async def run(self) -> SentinelResult:
        """Executes the sentinel validation logic asynchronously."""
        ...
