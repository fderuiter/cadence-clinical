"""LibreOffice document auto format conversion worker and background dispatcher.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import os
import sys

from apps.fileshare.adapters.conversion_adapter import get_conversion_adapter
from apps.fileshare.adapters.database import db_manager
from apps.fileshare.adapters.repositories import SqlAlchemyFileRecordRepository
from apps.fileshare.domain.models import ConversionStatus, FileRecord
from apps.fileshare.ports.conversion_port import ConversionPort
from apps.fileshare.ports.repository_port import FileRecordRepositoryPort
from packages.security.notifications import (
    NotificationCategory,
    NotificationEvent,
    NotificationPriority,
    publish_notification,
)

logger = logging.getLogger("fileshare.conversion_worker")


class DocumentConversionWorker:
    """Worker service responsible for converting Office document records into PDF renditions."""

    def __init__(
        self,
        file_repo: FileRecordRepositoryPort,
        conversion_port: ConversionPort,
    ) -> None:
        self.file_repo = file_repo
        self.conversion_port = conversion_port

    async def convert_file(self, file_id: str) -> FileRecord:
        """Converts a single Office document record to PDF and updates status."""
        file_record = await self.file_repo.get_by_id(file_id)
        if not file_record:
            raise ValueError(f"File record '{file_id}' not found.")

        # Set status to PENDING if not already
        if file_record.conversion_status != ConversionStatus.PENDING:
            in_progress = FileRecord(
                id=file_record.id,
                study_id=file_record.study_id,
                site_id=file_record.site_id,
                filename=file_record.filename,
                mime_type=file_record.mime_type,
                size_bytes=file_record.size_bytes,
                object_key=file_record.object_key,
                checksum_sha256=file_record.checksum_sha256,
                version_index=file_record.version_index,
                uploaded_by=file_record.uploaded_by,
                uploaded_at=file_record.uploaded_at,
                is_on_hold=file_record.is_on_hold,
                hold_reason=file_record.hold_reason,
                is_deleted=file_record.is_deleted,
                status=file_record.status,
                scan_status=file_record.scan_status,
                scan_engine=file_record.scan_engine,
                scan_completed_at=file_record.scan_completed_at,
                scan_threat_name=file_record.scan_threat_name,
                transcode_status=file_record.transcode_status,
                hls_manifest_key=file_record.hls_manifest_key,
                transcode_error=file_record.transcode_error,
                pdf_rendition_key=file_record.pdf_rendition_key,
                conversion_status=ConversionStatus.PENDING,
                conversion_error=None,
                approval_status=file_record.approval_status,
                signature_manifestation=file_record.signature_manifestation,
                approved_by=file_record.approved_by,
                approved_at=file_record.approved_at,
                approval_reason=file_record.approval_reason,
                created_at=file_record.created_at,
                created_by=file_record.created_by,
                reason_for_change="Starting document format conversion to PDF",
            )
            file_record = await self.file_repo.save(in_progress)

        try:
            pdf_key = await self.conversion_port.convert_to_pdf(file_record.object_key)

            ready_record = FileRecord(
                id=file_record.id,
                study_id=file_record.study_id,
                site_id=file_record.site_id,
                filename=file_record.filename,
                mime_type=file_record.mime_type,
                size_bytes=file_record.size_bytes,
                object_key=file_record.object_key,
                checksum_sha256=file_record.checksum_sha256,
                version_index=file_record.version_index,
                uploaded_by=file_record.uploaded_by,
                uploaded_at=file_record.uploaded_at,
                is_on_hold=file_record.is_on_hold,
                hold_reason=file_record.hold_reason,
                is_deleted=file_record.is_deleted,
                status=file_record.status,
                scan_status=file_record.scan_status,
                scan_engine=file_record.scan_engine,
                scan_completed_at=file_record.scan_completed_at,
                scan_threat_name=file_record.scan_threat_name,
                transcode_status=file_record.transcode_status,
                hls_manifest_key=file_record.hls_manifest_key,
                transcode_error=file_record.transcode_error,
                pdf_rendition_key=pdf_key,
                conversion_status=ConversionStatus.READY,
                conversion_error=None,
                approval_status=file_record.approval_status,
                signature_manifestation=file_record.signature_manifestation,
                approved_by=file_record.approved_by,
                approved_at=file_record.approved_at,
                approval_reason=file_record.approval_reason,
                created_at=file_record.created_at,
                created_by=file_record.created_by,
                reason_for_change="Completed document format conversion to PDF",
            )
            saved = await self.file_repo.save(ready_record)
            logger.info("Auto conversion complete for file %s -> %s", file_id, pdf_key)
            return saved

        except Exception as exc:
            logger.error(
                "Document conversion failed for file %s: %s",
                file_id,
                exc,
                exc_info=True,
            )
            failed_record = FileRecord(
                id=file_record.id,
                study_id=file_record.study_id,
                site_id=file_record.site_id,
                filename=file_record.filename,
                mime_type=file_record.mime_type,
                size_bytes=file_record.size_bytes,
                object_key=file_record.object_key,
                checksum_sha256=file_record.checksum_sha256,
                version_index=file_record.version_index,
                uploaded_by=file_record.uploaded_by,
                uploaded_at=file_record.uploaded_at,
                is_on_hold=file_record.is_on_hold,
                hold_reason=file_record.hold_reason,
                is_deleted=file_record.is_deleted,
                status=file_record.status,
                scan_status=file_record.scan_status,
                scan_engine=file_record.scan_engine,
                scan_completed_at=file_record.scan_completed_at,
                scan_threat_name=file_record.scan_threat_name,
                transcode_status=file_record.transcode_status,
                hls_manifest_key=file_record.hls_manifest_key,
                transcode_error=file_record.transcode_error,
                pdf_rendition_key=None,
                conversion_status=ConversionStatus.FAILED,
                conversion_error=str(exc)[:2000],
                approval_status=file_record.approval_status,
                signature_manifestation=file_record.signature_manifestation,
                approved_by=file_record.approved_by,
                approved_at=file_record.approved_at,
                approval_reason=file_record.approval_reason,
                created_at=file_record.created_at,
                created_by=file_record.created_by,
                reason_for_change="Document format conversion failure",
            )
            saved = await self.file_repo.save(failed_record)

            # Alert uploader regarding conversion failure
            with contextlib.suppress(Exception):
                await publish_notification(
                    NotificationEvent(
                        recipient_user_id=file_record.uploaded_by,
                        category=NotificationCategory.ALERTS,
                        priority=NotificationPriority.MEDIUM,
                        channels="IN_APP,EMAIL",
                        message_content=(
                            f"Document Conversion Failed: File '{file_record.filename}' uploaded to study "
                            f"'{file_record.study_id}' could not be converted to PDF. "
                            f"Original file remains accessible for download. Error: {exc}."
                        ),
                        related_entity_id=file_record.id,
                        related_entity_type="file_record",
                    )
                )

            return saved

    async def poll_and_process_pending(self) -> int:
        """Polls for all file records currently in PENDING conversion status and processes them."""
        pending_records = await self.file_repo.list_pending_conversions()
        processed_count = 0
        for record in pending_records:
            try:
                await self.convert_file(record.id)
                processed_count += 1
            except Exception as exc:
                logger.error(
                    "Error processing pending conversion %s: %s", record.id, exc
                )
        return processed_count


_conversion_worker_task: asyncio.Task[None] | None = None
_conversion_stop_event: asyncio.Event = asyncio.Event()


async def run_conversion_daemon(poll_interval: float = 3.0) -> None:
    """Daemon loop continuously polling and converting pending Office document records."""
    logger.info(
        "Starting document conversion daemon with poll interval %.1fs",
        poll_interval,
    )
    while not _conversion_stop_event.is_set():
        try:
            session_maker = db_manager.get_session_maker()
            async with session_maker() as session:
                file_repo = SqlAlchemyFileRecordRepository(session)
                conversion_port = get_conversion_adapter()
                worker = DocumentConversionWorker(
                    file_repo=file_repo, conversion_port=conversion_port
                )
                await worker.poll_and_process_pending()
                await session.commit()
        except asyncio.CancelledError:
            break
        except Exception as exc:
            logger.error("Error in conversion daemon loop: %s", exc, exc_info=True)

        with contextlib.suppress(TimeoutError):
            await asyncio.wait_for(_conversion_stop_event.wait(), timeout=poll_interval)


def start_conversion_worker() -> None:
    """Starts the background conversion daemon task."""
    global _conversion_worker_task
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    _conversion_stop_event.clear()
    _conversion_worker_task = asyncio.create_task(run_conversion_daemon())


def stop_conversion_worker() -> None:
    """Signals and stops the background conversion daemon task."""
    global _conversion_worker_task
    _conversion_stop_event.set()
    if _conversion_worker_task:
        _conversion_worker_task.cancel()


async def enqueue_conversion_task(file_id: str) -> None:
    """Dispatches an asynchronous in-process background conversion job for immediate execution."""

    async def _runner() -> None:
        session_maker = db_manager.get_session_maker()
        async with session_maker() as session:
            file_repo = SqlAlchemyFileRecordRepository(session)
            conversion_port = get_conversion_adapter()
            worker = DocumentConversionWorker(
                file_repo=file_repo, conversion_port=conversion_port
            )
            await worker.convert_file(file_id)
            await session.commit()

    asyncio.create_task(_runner())


__all__ = [
    "DocumentConversionWorker",
    "enqueue_conversion_task",
    "run_conversion_daemon",
    "start_conversion_worker",
    "stop_conversion_worker",
]
