"""Repository ports for Medical Imaging microservice entities.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002, PRD-IMG-003, PRD-IMG-005, PRD-SYS-001
"""

from abc import ABC, abstractmethod
from typing import Any

from apps.imaging.domain.models import (
    DeidentificationCertificateRecord,
    DualReaderAdjudicationSession,
    ImagingStudyEnvelope,
)
from packages.hexagonal import RepositoryPort


class ImagingStudyRepositoryPort(RepositoryPort[ImagingStudyEnvelope], ABC):
    """Port for persisting and querying ImagingStudyEnvelope domain entities."""

    @abstractmethod
    async def get_by_id(
        self, entity_id: str, *args: Any, **kwargs: Any
    ) -> ImagingStudyEnvelope | None:
        """Retrieve study envelope by ID."""
        pass

    @abstractmethod
    async def get_by_study_instance_uid(
        self, study_uid: str
    ) -> ImagingStudyEnvelope | None:
        """Retrieve study envelope by StudyInstanceUID."""
        pass

    @abstractmethod
    async def list_by_filters(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        subject_id: str | None = None,
        modality: str | None = None,
        skip: int = 0,
        limit: int = 50,
    ) -> list[ImagingStudyEnvelope]:
        """List studies matching clinical metadata filters."""
        pass


class ImagingCertificateRepositoryPort(
    RepositoryPort[DeidentificationCertificateRecord], ABC
):
    """Port for persisting and querying cryptographic de-identification certificates."""

    @abstractmethod
    async def get_by_id(
        self, entity_id: str, *args: Any, **kwargs: Any
    ) -> DeidentificationCertificateRecord | None:
        """Retrieve certificate record by certificate ID."""
        pass


class ImagingAdjudicationRepositoryPort(
    RepositoryPort[DualReaderAdjudicationSession], ABC
):
    """Port for persisting and querying Blinded Dual-Reader Adjudication sessions.

    Requirements: PRD-IMG-003, PRD-IMG-005, PRD-SYS-001
    """

    @abstractmethod
    async def get_by_id(
        self, entity_id: str, *args: Any, **kwargs: Any
    ) -> DualReaderAdjudicationSession | None:
        """Retrieve adjudication session by session ID.

        Args:
            entity_id: The unique identifier of the adjudication session.

        Returns:
            DualReaderAdjudicationSession if found, None otherwise.
        """
        pass

    @abstractmethod
    async def save(
        self, entity: DualReaderAdjudicationSession, *args: Any, **kwargs: Any
    ) -> DualReaderAdjudicationSession:
        """Persist or update adjudication session state.

        Args:
            entity: Domain session entity to save.

        Returns:
            Updated DualReaderAdjudicationSession domain entity.
        """
        pass

    @abstractmethod
    async def list_by_filters(
        self,
        study_id: str | None = None,
        subject_id: str | None = None,
        timepoint_name: str | None = None,
        workflow_status: str | None = None,
        skip: int = 0,
        limit: int = 50,
    ) -> list[DualReaderAdjudicationSession]:
        """List adjudication sessions matching query filters.

        Args:
            study_id: Optional clinical study ID filter.
            subject_id: Optional clinical subject ID filter.
            timepoint_name: Optional protocol timepoint filter.
            workflow_status: Optional workflow status filter.
            skip: Offset for pagination.
            limit: Maximum items to return.

        Returns:
            List of matching DualReaderAdjudicationSession entities.
        """
        pass

    @abstractmethod
    async def record_tumor_response_assessment(
        self,
        session: DualReaderAdjudicationSession,
        signature_digest: str,
        actor_id: str,
        rationale: str,
    ) -> None:
        """Persist or synchronize a TumorResponseAssessment record upon adjudication sign-off.

        Args:
            session: Adjudicated domain session.
            signature_digest: SHA-256 digital signature digest.
            actor_id: Signing adjudicator identity.
            rationale: 21 CFR Part 11 justification rationale.
        """
        pass

    @abstractmethod
    async def record_audit_log(
        self,
        action: str,
        actor_id: str,
        reason_for_change: str | None = None,
        details: dict[str, Any] | None = None,
    ) -> None:
        """Record a 21 CFR Part 11 compliant audit log entry.

        Args:
            action: Action code identifier.
            actor_id: Identity of the acting user/service.
            reason_for_change: Optional Part 11 justification reason.
            details: Contextual payload dictionary.
        """
        pass
