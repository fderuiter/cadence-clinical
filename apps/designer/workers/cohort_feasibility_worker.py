"""
Asynchronous background worker and advisory locking coordinator for Cohort Feasibility Engine.

Ensures single-instance execution per study cohort using PostgreSQL transaction advisory locks
or thread-safe in-memory lock tracking in non-PostgreSQL / testing environments.
"""

from __future__ import annotations

import asyncio
import logging
import zlib
from datetime import UTC, datetime
from typing import Literal

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from apps.designer.domain.cohort_feasibility_models import (
    CohortFeasibilityJob,
    CohortFeasibilityReport,
)

logger = logging.getLogger("cohort-feasibility-worker")

# Thread-safe in-memory active advisory locks for non-Postgres / test environments
_in_memory_locks: set[str] = set()
_lock_mutex = asyncio.Lock()

# Global job status store
_jobs_store: dict[str, CohortFeasibilityJob] = {}
ADVISORY_LOCK_BASE_KEY = 42010


def derive_advisory_lock_key(study_id: str, cohort_id: str | None = None) -> int:
    """Derives a deterministic 31-bit integer key for PostgreSQL pg_try_advisory_xact_lock.

    Args:
        study_id: Target study ID.
        cohort_id: Optional target cohort ID.

    Returns:
        Positive 31-bit integer.
    """
    raw_str = f"feasibility:{study_id}:{cohort_id or 'default'}"
    crc = zlib.crc32(raw_str.encode("utf-8"))
    # Keep within signed integer range for Postgres advisory lock (31-bit)
    return crc & 0x7FFFFFFF


async def acquire_advisory_lock(
    session: AsyncSession | None, study_id: str, cohort_id: str | None = None
) -> bool:
    """Attempts to acquire PostgreSQL advisory lock or in-memory lock for a study cohort.

    Args:
        session: Optional AsyncSession connected to PostgreSQL.
        study_id: Target study ID.
        cohort_id: Optional cohort ID.

    Returns:
        True if lock was successfully acquired, False if lock is already held.
    """
    lock_str = f"{study_id}:{cohort_id or 'default'}"

    # First check in-memory lock tracking to prevent duplicate concurrent tasks in memory
    async with _lock_mutex:
        if lock_str in _in_memory_locks:
            logger.info(
                "In-memory advisory lock already held for %s. Skipping duplicate task.",
                lock_str,
            )
            return False
        _in_memory_locks.add(lock_str)

    # If session exists and dialect is PostgreSQL, attempt database-level advisory lock
    if session and session.bind and session.bind.dialect.name == "postgresql":
        lock_key = derive_advisory_lock_key(study_id, cohort_id)
        try:
            res = await session.execute(
                text(f"SELECT pg_try_advisory_xact_lock({lock_key});")
            )
            acquired = bool(res.scalar())
            if not acquired:
                async with _lock_mutex:
                    _in_memory_locks.discard(lock_str)
                logger.info(
                    "PostgreSQL advisory lock %d already held for study %s, cohort %s.",
                    lock_key,
                    study_id,
                    cohort_id,
                )
                return False
        except Exception as err:
            logger.warning(
                "Error attempting PostgreSQL advisory lock: %s. Proceeding with in-memory lock.",
                err,
            )

    return True


async def release_advisory_lock(study_id: str, cohort_id: str | None = None) -> None:
    """Releases in-memory advisory lock for study cohort.

    Args:
        study_id: Target study ID.
        cohort_id: Target cohort ID.
    """
    lock_str = f"{study_id}:{cohort_id or 'default'}"
    async with _lock_mutex:
        _in_memory_locks.discard(lock_str)


class CohortFeasibilityWorker:
    """Worker managing asynchronous execution and status polling of feasibility jobs."""

    @classmethod
    def register_job(
        cls, job_id: str, study_id: str, cohort_id: str | None
    ) -> CohortFeasibilityJob:
        """Registers a new job in PENDING status.

        Args:
            job_id: Unique job ID.
            study_id: Target study ID.
            cohort_id: Optional cohort ID.

        Returns:
            Initial CohortFeasibilityJob record.
        """
        now_iso = datetime.now(UTC).isoformat()
        job = CohortFeasibilityJob(
            job_id=job_id,
            study_id=study_id,
            cohort_id=cohort_id,
            status="PENDING",
            created_at=now_iso,
            report=None,
        )
        _jobs_store[job_id] = job
        return job

    @classmethod
    def get_job(cls, job_id: str) -> CohortFeasibilityJob | None:
        """Retrieves job state by job ID.

        Args:
            job_id: Job identifier.

        Returns:
            CohortFeasibilityJob if found, else None.
        """
        return _jobs_store.get(job_id)

    @classmethod
    def update_job_status(
        cls,
        job_id: str,
        status: Literal[
            "PENDING", "RUNNING", "COMPLETED", "FAILED", "DUPLICATE_LOCKED"
        ],
        report: CohortFeasibilityReport | None = None,
    ) -> CohortFeasibilityJob | None:
        """Updates job status and attaches report upon completion.

        Args:
            job_id: Job identifier.
            status: Target status.
            report: Optional CohortFeasibilityReport result.

        Returns:
            Updated CohortFeasibilityJob record.
        """
        job = _jobs_store.get(job_id)
        if job:
            job.status = status
            if report:
                job.report = report
        return job


__all__ = [
    "ADVISORY_LOCK_BASE_KEY",
    "CohortFeasibilityWorker",
    "acquire_advisory_lock",
    "derive_advisory_lock_key",
    "release_advisory_lock",
]
