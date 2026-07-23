import asyncio
import logging
import uuid
from datetime import datetime, timedelta
from typing import Optional

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from apps.execution.database.context import current_change_reason, current_session
from apps.execution.database.models import TranslationJob

logger = logging.getLogger(__name__)


class QueueWorker:
    """Manages the background transactional translation queue processor.

    This worker implements a PostgreSQL-backed transactional queue with row-level
    locking, heartbeats, exponential backoff, and failure retries.

    Attributes:
        session_maker (async_sessionmaker[AsyncSession]): The DB session factory.
        worker_id (str): Unique identifier for this worker instance.
        polling_interval (float): Base sleeping interval for empty queue polling.
        max_polling_interval (float): Maximum backoff sleep limit.
        timeout_seconds (float): Stuck task timeout detection limit in seconds.
    """

    def __init__(
        self,
        session_maker: async_sessionmaker[AsyncSession],
        worker_id: Optional[str] = None,
        polling_interval: float = 1.0,
        max_polling_interval: float = 30.0,
        timeout_seconds: float = 30.0,
    ) -> None:
        """Initializes the queue worker instance.

        Args:
            session_maker (async_sessionmaker[AsyncSession]): The DB session factory.
            worker_id (Optional[str]): Unique identifier of the worker.
            polling_interval (float): Minimum sleep time when polling.
            max_polling_interval (float): Maximum sleep backoff time.
            timeout_seconds (float): Heartbeat stale threshold.
        """
        self.session_maker = session_maker
        self.worker_id = worker_id or f"worker-{uuid.uuid4().hex[:8]}"
        self.polling_interval = polling_interval
        self.max_polling_interval = max_polling_interval
        self.timeout_seconds = timeout_seconds

        self._running = False
        self._worker_task: Optional[asyncio.Task] = None
        self._sweeper_task: Optional[asyncio.Task] = None
        self._wakeup_event = asyncio.Event()

    async def start(self) -> None:
        """Starts the main polling and sweeping async loops."""
        self._running = True
        self._worker_task = asyncio.create_task(self._poll_loop())
        self._sweeper_task = asyncio.create_task(self._sweep_loop())
        logger.info(f"QueueWorker {self.worker_id} started successfully.")

    async def stop(self) -> None:
        """Gracefully stops the worker execution tasks."""
        self._running = False
        self._wakeup_event.set()
        if self._worker_task:
            self._worker_task.cancel()
            try:
                await self._worker_task
            except asyncio.CancelledError:
                pass
        if self._sweeper_task:
            self._sweeper_task.cancel()
            try:
                await self._sweeper_task
            except asyncio.CancelledError:
                pass
        logger.info(f"QueueWorker {self.worker_id} stopped.")

    def wakeup(self) -> None:
        """Instantly wakes up the worker loop to process pending jobs."""
        self._wakeup_event.set()

    async def _poll_loop(self) -> None:
        """Main polling loop with exponential backoff and instant wakeup."""
        backoff = self.polling_interval
        while self._running:
            try:
                # Claim and process one pending job
                job_processed = await self.claim_and_process_one()
                if job_processed:
                    backoff = self.polling_interval
                    continue

                # No job found; wait on wakeup event or timeout backoff
                try:
                    await asyncio.wait_for(self._wakeup_event.wait(), timeout=backoff)
                    self._wakeup_event.clear()
                except asyncio.TimeoutError:
                    pass

                # Apply exponential backoff
                backoff = min(backoff * 2.0, self.max_polling_interval)

            except Exception as e:
                # AC 4: Recover from transient database disconnection events without crashing
                logger.error(
                    f"Transient error in QueueWorker poll loop: {e}", exc_info=True
                )
                await asyncio.sleep(self.polling_interval)

    async def _sweep_loop(self) -> None:
        """Sweeper loop to periodically find stuck PROCESSING jobs and reset or fail them."""
        while self._running:
            try:
                await self.sweep_stuck_jobs()
            except Exception as e:
                # AC 4: Recover from transient database disconnection events without crashing
                logger.error(
                    f"Transient error in QueueWorker sweeper loop: {e}", exc_info=True
                )

            # Periodically sleep (e.g. half of the timeout limit up to 5 seconds)
            await asyncio.sleep(min(self.timeout_seconds / 2.0, 5.0))

    async def claim_and_process_one(self) -> bool:
        """Claim a single PENDING job using row-level locking.

        Returns:
            bool: True if a job was successfully claimed and processed, False otherwise.
        """
        job_id = None

        async with self.session_maker() as session:
            async with session.begin():
                token = current_session.set(session)
                try:
                    current_change_reason.set("claim_translation_job")
                    is_sqlite = "sqlite" in str(session.bind.dialect.name)

                    # Retrieve oldest PENDING job
                    stmt = (
                        select(TranslationJob)
                        .where(TranslationJob.status == "PENDING")
                        .order_by(TranslationJob.created_at.asc())
                        .limit(1)
                    )

                    # Requirement 2: Row-level locking to prevent duplicates
                    if not is_sqlite:
                        stmt = stmt.with_for_update(skip_locked=True)

                    result = await session.execute(stmt)
                    job = result.scalars().first()

                    if job:
                        job_id = job.id
                        job.status = "PROCESSING"
                        job.worker_id = self.worker_id
                        job.heartbeat_at = datetime.utcnow()
                finally:
                    current_session.reset(token)

        if not job_id:
            return False

        # Execute translation job outside of the initial lock transaction
        try:
            await self.execute_job(job_id)
        except Exception as e:
            logger.error(
                f"Translation Job {job_id} processing failed: {e}", exc_info=True
            )
            await self.handle_job_failure(job_id, e)

        return True

    async def execute_job(self, job_id: str) -> None:
        """Invokes the translator worker on the specified job.

        Args:
            job_id (str): The primary key identifier of the TranslationJob.
        """
        from apps.execution.translator import execute_translation_job

        await execute_translation_job(job_id, self.session_maker)

    async def handle_job_failure(self, job_id: str, exc: Exception) -> None:
        """Handles failure of a claimed job by rescheduling or marking it failed.

        Args:
            job_id (str): The primary key identifier of the TranslationJob.
            exc (Exception): The exception that caused the failure.
        """
        async with self.session_maker() as session:
            async with session.begin():
                token = current_session.set(session)
                try:
                    current_change_reason.set("translation_job_failure")
                    job = await session.get(TranslationJob, job_id)
                    if job:
                        # Check if this is a deterministic validation failure (like input/schema checks)
                        is_validation_error = isinstance(exc, ValueError)

                        if (
                            not is_validation_error
                            and job.retry_count < job.max_retries
                        ):
                            # Requirement 5: Automatically retry failed tasks up to configured threshold
                            job.status = "PENDING"
                            job.retry_count += 1
                            job.worker_id = None
                            job.heartbeat_at = None
                            job.error_message = (
                                f"Retrying due to transient error: {exc}"
                            )
                            logger.info(
                                f"Rescheduled job {job_id} for retry ({job.retry_count}/{job.max_retries})."
                            )
                        else:
                            # Max retries exhausted or deterministic validation error
                            job.status = "FAILED"
                            if is_validation_error:
                                job.error_message = str(exc)
                            else:
                                job.error_message = f"Max retries ({job.max_retries}) exhausted. Error: {exc}"
                            logger.error(
                                f"Job {job_id} permanently failed. Error: {exc}"
                            )
                finally:
                    current_session.reset(token)

    async def sweep_stuck_jobs(self) -> None:
        """Finds stuck tasks in PROCESSING state and reschedules or fails them."""
        cutoff_time = datetime.utcnow() - timedelta(seconds=self.timeout_seconds)

        async with self.session_maker() as session:
            async with session.begin():
                token = current_session.set(session)
                try:
                    current_change_reason.set("sweep_stuck_jobs")
                    is_sqlite = "sqlite" in str(session.bind.dialect.name)

                    # Find tasks stuck in PROCESSING state past timeout duration
                    stmt = (
                        select(TranslationJob)
                        .where(TranslationJob.status == "PROCESSING")
                        .where(
                            (TranslationJob.heartbeat_at < cutoff_time)
                            | (TranslationJob.heartbeat_at.is_(None))
                        )
                    )
                    if not is_sqlite:
                        stmt = stmt.with_for_update(skip_locked=True)

                    result = await session.execute(stmt)
                    stuck_jobs = result.scalars().all()

                    for job in stuck_jobs:
                        logger.warning(
                            f"Sweeper detected stuck job {job.id} (last heartbeat: {job.heartbeat_at})."
                        )
                        if job.retry_count < job.max_retries:
                            # Reschedule
                            job.status = "PENDING"
                            job.retry_count += 1
                            job.worker_id = None
                            job.heartbeat_at = None
                            job.error_message = f"Job recovered (heartbeat timeout past {self.timeout_seconds}s) - rescheduled."
                        else:
                            # Max retries exhausted
                            job.status = "FAILED"
                            job.error_message = f"Job timed out (heartbeat timeout past {self.timeout_seconds}s) - maximum retries exhausted."
                finally:
                    current_session.reset(token)


# Singleton QueueWorker instance and accessors
_global_worker: Optional[QueueWorker] = None


def init_queue_worker(
    session_maker: async_sessionmaker[AsyncSession],
    worker_id: Optional[str] = None,
    polling_interval: float = 1.0,
    max_polling_interval: float = 30.0,
    timeout_seconds: float = 30.0,
) -> QueueWorker:
    """Initializes the global singleton QueueWorker instance.

    Args:
        session_maker (async_sessionmaker[AsyncSession]): The database session maker.
        worker_id (Optional[str]): Unique ID of this worker instance.
        polling_interval (float): Base empty polling sleep interval.
        max_polling_interval (float): Maximum backoff sleep limit.
        timeout_seconds (float): Heartbeat stale threshold.

    Returns:
        QueueWorker: The initialized queue worker.
    """
    global _global_worker
    _global_worker = QueueWorker(
        session_maker=session_maker,
        worker_id=worker_id,
        polling_interval=polling_interval,
        max_polling_interval=max_polling_interval,
        timeout_seconds=timeout_seconds,
    )
    return _global_worker


def get_queue_worker() -> QueueWorker:
    """Retrieves the global singleton QueueWorker instance.

    Returns:
        QueueWorker: The active queue worker.

    Raises:
        RuntimeError: If the queue worker has not been initialized.
    """
    global _global_worker
    if _global_worker is None:
        raise RuntimeError(
            "QueueWorker has not been initialized. Call init_queue_worker first."
        )
    return _global_worker
