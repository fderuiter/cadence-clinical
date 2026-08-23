"""CRC Source Data Reconciliation Application Service.

Implements non-destructive CRC review workflow (Accept & Commit, Reject with Reason, Raise Clinical Query)
preventing silent background data overwrites and guaranteeing 21 CFR Part 11 audit attribution.

Requirements: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, ADR-2199, FDA Electronic Source Data Guidance
"""

from __future__ import annotations

from typing import Any

from apps.interop.domain.esource_models import (
    EhrCandidateRecord,
)
from apps.interop.domain.ports import IEsourceReconciliationRepositoryPort


class ReconciliationError(Exception):
    """Base exception for eSource reconciliation domain errors."""

    def __init__(self, message: str, status_code: int = 400) -> None:
        super().__init__(message)
        self.message = message
        self.status_code = status_code


class EsourceReconciliationService:
    """Orchestrates candidate persistence, non-destructive CRC reconciliation actions, and Part 11 provenance."""

    def __init__(
        self, repository: IEsourceReconciliationRepositoryPort | None = None
    ) -> None:
        self.repository = repository

    async def save_candidates(
        self,
        repository: IEsourceReconciliationRepositoryPort,
        candidates: list[EhrCandidateRecord],
        user_id: str = "esource_ingestion_pipeline",
        user_role: str = "system",
        change_reason: str = "Automated SMART on FHIR eSource Ingestion",
    ) -> list[Any]:
        """Persist a batch of normalized candidate EHR records into the reconciliation queue."""
        repo = self.repository or repository
        return await repo.save_candidates_batch(
            candidates=candidates,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
        )

    async def list_candidates(
        self,
        repository: IEsourceReconciliationRepositoryPort,
        study_id: str | None = None,
        subject_id: str | None = None,
        site_id: str | None = None,
        domain: str | None = None,
        status: str | None = None,
        skip: int = 0,
        limit: int = 100,
    ) -> tuple[list[Any], int]:
        """Query reconciliation candidate items with filtering and pagination."""
        repo = self.repository or repository
        return await repo.list_candidates(
            study_id=study_id,
            subject_id=subject_id,
            site_id=site_id,
            domain=domain,
            status=status,
            skip=skip,
            limit=limit,
        )

    async def get_candidate_by_id(
        self,
        repository: IEsourceReconciliationRepositoryPort,
        candidate_id: str,
    ) -> Any | None:
        """Retrieve a specific candidate record by ID."""
        repo = self.repository or repository
        return await repo.get_by_id(candidate_id)

    async def accept_and_commit(
        self,
        repository: IEsourceReconciliationRepositoryPort,
        candidate_id: str,
        user_id: str,
        user_role: str,
        signature_token: str | None = None,
        commit_reason: str | None = None,
        change_reason: str = "CRC eSource Reconciliation Accept & Commit",
    ) -> Any:
        """1-Click 'Accept & Commit' candidate EHR data into eCRF with Part 11 audit attribution."""
        repo = self.repository or repository
        try:
            return await repo.accept_candidate(
                candidate_id=candidate_id,
                user_id=user_id,
                user_role=user_role,
                signature_token=signature_token,
                commit_reason=commit_reason,
                change_reason=change_reason,
            )
        except ValueError as exc:
            raise ReconciliationError(str(exc), status_code=400) from exc

    async def reject_candidate(
        self,
        repository: IEsourceReconciliationRepositoryPort,
        candidate_id: str,
        user_id: str,
        user_role: str,
        rejection_reason: str,
        change_reason: str = "CRC eSource Reconciliation Reject",
    ) -> Any:
        """1-Click 'Reject with Reason' to discard candidate EHR data with mandatory justification."""
        if not rejection_reason or not rejection_reason.strip():
            raise ReconciliationError(
                "Rejection rationale is required under GxP regulatory guidelines",
                status_code=400,
            )

        repo = self.repository or repository
        try:
            return await repo.reject_candidate(
                candidate_id=candidate_id,
                user_id=user_id,
                user_role=user_role,
                rejection_reason=rejection_reason,
                change_reason=change_reason,
            )
        except ValueError as exc:
            raise ReconciliationError(str(exc), status_code=400) from exc

    async def raise_clinical_query(
        self,
        repository: IEsourceReconciliationRepositoryPort,
        candidate_id: str,
        user_id: str,
        user_role: str,
        query_explanation: str,
        discrepancy_type: str = "VALUE_MISMATCH",
        change_reason: str = "CRC Raised eSource Discrepancy Query",
    ) -> tuple[Any, Any]:
        """1-Click 'Raise Clinical Query' to open a formal GxP discrepancy query for an EHR-to-eCRF mismatch."""
        if not query_explanation or not query_explanation.strip():
            raise ReconciliationError(
                "Query discrepancy explanation is required", status_code=400
            )

        repo = self.repository or repository
        try:
            return await repo.raise_clinical_query(
                candidate_id=candidate_id,
                user_id=user_id,
                user_role=user_role,
                query_explanation=query_explanation,
                discrepancy_type=discrepancy_type,
                change_reason=change_reason,
            )
        except ValueError as exc:
            raise ReconciliationError(str(exc), status_code=400) from exc

    async def get_provenance_audit(
        self,
        repository: IEsourceReconciliationRepositoryPort,
        candidate_id: str,
    ) -> dict[str, Any]:
        """Retrieve complete immutable FHIR provenance metadata and Part 11 audit history for a record."""
        repo = self.repository or repository
        try:
            return await repo.get_provenance_audit(candidate_id)
        except ValueError as exc:
            raise ReconciliationError(str(exc), status_code=404) from exc
