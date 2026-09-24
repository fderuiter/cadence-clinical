"""Document intelligence background worker for multimodal layout parsing, DIA classification, and CRA QC staging.

Requirements: PRD-TMF-006, 21 CFR Part 11
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import os
import sys
from datetime import UTC, datetime
from typing import Any

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.repositories import SQLETMFRepository
from apps.etmf.application.document_intelligence_use_case import (
    AnalyzeDocumentIntelligenceUseCase,
)
from apps.etmf.domain.exceptions import DocumentNotFoundError
from apps.etmf.domain.intelligence_models import DocumentIntelligenceReport
from apps.etmf.domain.models import DocumentStatus
from apps.etmf.domain.ports import ETMFRepositoryPort

logger = logging.getLogger("etmf.document_intelligence_worker")


class DocumentIntelligenceWorker:
    """Worker service responsible for analyzing uploaded documents and staging into CRA QC."""

    def __init__(self, repo: ETMFRepositoryPort) -> None:
        self.repo = repo
        self.analyzer = AnalyzeDocumentIntelligenceUseCase()

    async def process_document(
        self,
        document_id: str,
        actor_id: str = "system",
        actor_role: str = "ai_worker",
        reason_for_change: str = "Automated multimodal DIA classification and metadata extraction",
    ) -> tuple[Any, DocumentIntelligenceReport]:
        """Analyzes a single document, populates the relational envelope, and stages into QC."""
        doc = await self.repo.get_document_by_id(document_id)
        if not doc:
            raise DocumentNotFoundError(f"Document '{document_id}' not found.")

        content = doc.content or ""
        mime_type = getattr(doc, "mime_type", "application/pdf")
        filename = getattr(doc, "filename", "document.pdf")
        study_id = getattr(doc, "study_id", None)
        site_id = getattr(doc, "site_id", None)
        existing_signer = getattr(doc, "signer", None)
        existing_manifestation = getattr(doc, "signature_manifestation", None)

        report = self.analyzer.execute(
            content=content,
            filename=filename,
            mime_type=mime_type,
            study_id_hint=study_id,
            site_id_hint=site_id,
            document_id=doc.id,
            existing_signer=existing_signer,
            existing_manifestation=existing_manifestation,
        )

        primary = report.primary_classification
        doc.artifact_code = primary.artifact_code
        doc.artifact_type = primary.artifact_name
        doc.zone = primary.zone_code
        doc.section = primary.section_code
        doc.status = DocumentStatus.TECHNICAL_QC.value

        # Populate relational envelope
        if report.extracted_metadata.issue_date:
            doc.issue_date = report.extracted_metadata.issue_date

        if report.extracted_metadata.expiration_date:
            d = report.extracted_metadata.expiration_date
            doc.expiration_date = datetime(d.year, d.month, d.day, 0, 0, 0, tzinfo=UTC)

        if report.extracted_metadata.investigator_name:
            doc.document_owner_id = report.extracted_metadata.investigator_name

        meta = dict(getattr(doc, "metadata_json", {}) or {})
        meta["document_intelligence"] = report.model_dump(mode="json")
        meta["ai_staging_status"] = "DRAFT_AI"
        meta["confidence_score"] = primary.confidence
        meta["processed_by_ai"] = True
        doc.metadata_json = meta
        doc.reason_for_change = reason_for_change

        saved_doc = await self.repo.save_document(doc)

        # Append Part 11 Audit Log
        details = (
            f"Multimodal AI worker processed document '{filename}' as '{primary.artifact_name}' "
            f"({primary.artifact_code}) with confidence {primary.confidence:.2f} (Tier: {report.confidence_tier.value})."
        )
        await self.repo.create_audit_log(
            user_id=actor_id,
            user_role=actor_role,
            action="AI_DOCUMENT_INTELLIGENCE_PROCESSED",
            document_id=doc.id,
            details=details,
            reason_for_change=reason_for_change,
        )

        logger.info(
            "Document intelligence analysis completed for doc %s -> %s (%s)",
            document_id,
            primary.artifact_code,
            primary.artifact_name,
        )
        return saved_doc, report

    async def poll_and_process_pending(self) -> int:
        """Polls for documents in DRAFT or pending AI classification and processes them."""
        # Find all documents with pending status
        docs = await self.repo.get_documents_filtered(None, None, None, None, None)
        processed_count = 0
        for d in docs:
            meta = getattr(d, "metadata_json", {}) or {}
            needs_processing = (
                d.status == DocumentStatus.DRAFT.value
                and not meta.get("processed_by_ai")
            ) or meta.get("needs_ai_processing") is True

            if needs_processing:
                try:
                    await self.process_document(d.id)
                    processed_count += 1
                except Exception as exc:
                    logger.error(
                        "Error processing document intelligence for %s: %s",
                        d.id,
                        exc,
                        exc_info=True,
                    )
        return processed_count


_intelligence_worker_task: asyncio.Task[None] | None = None
_intelligence_stop_event: asyncio.Event = asyncio.Event()


async def run_document_intelligence_daemon(poll_interval: float = 5.0) -> None:
    """Daemon loop continuously polling and running intelligence pipeline on pending documents."""
    logger.info(
        "Starting document intelligence daemon with poll interval %.1fs",
        poll_interval,
    )
    while not _intelligence_stop_event.is_set():
        try:
            session_maker = db_manager.get_session_maker()
            async with session_maker() as session:
                repo = SQLETMFRepository(session)
                worker = DocumentIntelligenceWorker(repo=repo)
                await worker.poll_and_process_pending()
                await session.commit()
        except asyncio.CancelledError:
            break
        except Exception as exc:
            logger.error(
                "Error in document intelligence daemon loop: %s", exc, exc_info=True
            )

        with contextlib.suppress(TimeoutError):
            await asyncio.wait_for(
                _intelligence_stop_event.wait(), timeout=poll_interval
            )


def start_document_intelligence_worker() -> None:
    """Starts the background document intelligence daemon task."""
    global _intelligence_worker_task
    if "pytest" in sys.modules or os.getenv("TESTING") == "true":
        return
    _intelligence_stop_event.clear()
    _intelligence_worker_task = asyncio.create_task(run_document_intelligence_daemon())


def stop_document_intelligence_worker() -> None:
    """Signals and stops the background document intelligence daemon task."""
    global _intelligence_worker_task
    _intelligence_stop_event.set()
    if _intelligence_worker_task:
        _intelligence_worker_task.cancel()


async def enqueue_document_intelligence_task(document_id: str) -> None:
    """Dispatches an asynchronous in-process background job for immediate document analysis."""

    async def _runner() -> None:
        session_maker = db_manager.get_session_maker()
        async with session_maker() as session:
            repo = SQLETMFRepository(session)
            worker = DocumentIntelligenceWorker(repo=repo)
            await worker.process_document(document_id)
            await session.commit()

    asyncio.create_task(_runner())


__all__ = [
    "DocumentIntelligenceWorker",
    "enqueue_document_intelligence_task",
    "run_document_intelligence_daemon",
    "start_document_intelligence_worker",
    "stop_document_intelligence_worker",
]
