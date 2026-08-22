"""Asynchronous in-memory job repository for Protocol Digitization Stage DAG.

Provides thread-safe and async-safe persistent checkpoint storage, job tracking,
and query capabilities for protocol digitization workflows.

Requirements: PRD-DDF-001, PRD-SYS-001, PRD-MDR-007
"""

from __future__ import annotations

import asyncio
import logging
from datetime import UTC, datetime

from apps.designer.domain.digitization_dag_models import (
    DigitizationJob,
    StageCheckpoint,
)
from apps.designer.domain.ports import DigitizationJobRepositoryPort

logger = logging.getLogger(__name__)


class DigitizationJobStore(DigitizationJobRepositoryPort):
    """Thread-safe in-memory store for DigitizationJob records and stage checkpoints."""

    def __init__(self) -> None:
        """Initializes the job store with an empty dictionary and an asyncio lock."""
        self._jobs: dict[str, DigitizationJob] = {}
        self._lock = asyncio.Lock()

    async def create_job(self, job: DigitizationJob) -> DigitizationJob:
        """Stores a newly initialized DigitizationJob.

        Args:
            job: The DigitizationJob instance to store.

        Returns:
            The stored DigitizationJob.
        """
        async with self._lock:
            self._jobs[job.job_id] = job
            return job.model_copy(deep=True)

    async def get_job(self, job_id: str) -> DigitizationJob | None:
        """Retrieves a DigitizationJob by its unique identifier.

        Args:
            job_id: Unique job UUID string.

        Returns:
            DigitizationJob instance if found, None otherwise.
        """
        async with self._lock:
            job = self._jobs.get(job_id)
            return job.model_copy(deep=True) if job else None

    async def update_job(self, job: DigitizationJob) -> DigitizationJob:
        """Updates an existing DigitizationJob with new state and timestamps.

        Args:
            job: The updated DigitizationJob instance.

        Returns:
            The persisted DigitizationJob.
        """
        async with self._lock:
            job.updated_at = datetime.now(UTC)
            self._jobs[job.job_id] = job
            return job.model_copy(deep=True)

    async def save_checkpoint(
        self, job_id: str, checkpoint: StageCheckpoint
    ) -> DigitizationJob:
        """Appends or updates a stage checkpoint for a specific job.

        Args:
            job_id: Unique job UUID string.
            checkpoint: StageCheckpoint to record.

        Returns:
            The updated DigitizationJob.

        Raises:
            KeyError: If job_id does not exist in the store.
        """
        async with self._lock:
            job = self._jobs.get(job_id)
            if not job:
                raise KeyError(f"Digitization job '{job_id}' not found.")

            job.checkpoints[checkpoint.stage.value] = checkpoint
            job.current_stage = checkpoint.stage
            job.updated_at = datetime.now(UTC)
            self._jobs[job_id] = job
            return job.model_copy(deep=True)

    async def list_jobs(
        self, study_id: str | None = None, limit: int = 50
    ) -> list[DigitizationJob]:
        """Lists stored digitization jobs, optionally filtered by study ID.

        Args:
            study_id: Optional filter for study ID.
            limit: Maximum records to return.

        Returns:
            List of matching DigitizationJob records sorted newest first.
        """
        async with self._lock:
            jobs = list(self._jobs.values())
            if study_id:
                jobs = [j for j in jobs if j.study_id == study_id]
            jobs.sort(key=lambda j: j.created_at, reverse=True)
            return [j.model_copy(deep=True) for j in jobs[:limit]]

    async def get_by_id(self, entity_id: str) -> DigitizationJob | None:
        """Alias for get_job to satisfy RepositoryPort interface."""
        return await self.get_job(entity_id)

    async def save(self, entity: DigitizationJob) -> DigitizationJob:
        """Alias for update_job to satisfy RepositoryPort interface."""
        return await self.update_job(entity)

    async def clear(self) -> None:
        """Clears all stored jobs (for testing fixtures)."""
        async with self._lock:
            self._jobs.clear()


# Global module singleton instance
_job_store_instance = DigitizationJobStore()


def get_digitization_job_store() -> DigitizationJobStore:
    """Provides dependency injection access to the global DigitizationJobStore instance."""
    return _job_store_instance
