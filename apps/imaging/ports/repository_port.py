"""Repository ports for Medical Imaging microservice entities.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from abc import ABC, abstractmethod
from typing import Any

from apps.imaging.domain.models import (
    DeidentificationCertificateRecord,
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
