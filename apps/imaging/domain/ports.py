"""Domain ports and interface definitions for Medical Imaging microservice.

Requirements: PRD-IMG-001, PRD-IMG-002, ADR-2199
"""

from abc import abstractmethod

from apps.imaging.domain.models import (
    DeidentificationCertificateEntity,
    ImagingStudyEnvelopeEntity,
)
from packages.hexagonal import RepositoryPort


class IImagingRepository(RepositoryPort[ImagingStudyEnvelopeEntity]):
    """Repository port interface for persisting imaging study envelopes and certificates."""

    async def get_by_id(self, entity_id: str) -> ImagingStudyEnvelopeEntity | None:
        return None

    async def save(
        self, entity: ImagingStudyEnvelopeEntity
    ) -> ImagingStudyEnvelopeEntity:
        return entity

    @abstractmethod
    async def get_study_envelope(
        self, study_instance_uid: str
    ) -> ImagingStudyEnvelopeEntity | None:
        """Retrieves a study envelope by its StudyInstanceUID."""
        ...

    @abstractmethod
    async def save_certificate(
        self, certificate: DeidentificationCertificateEntity
    ) -> None:
        """Persists a cryptographic de-identification certificate."""
        ...
