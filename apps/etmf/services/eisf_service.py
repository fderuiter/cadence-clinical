"""Electronic Investigator Site File (eISF) Binder and Redaction Service.

Requirements: PRD-SYS-001
"""

import hashlib
import uuid
from typing import List, Optional

from sqlalchemy import Boolean, String, select
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

import packages  # noqa: F401
from apps.compliance.services.phi_redactor import PHIRedactorService
from packages.security.rbac import Principal, can_access_site


class Base(DeclarativeBase):
    """Declarative base for eISF service database models.

    Requirements: PRD-SYS-001
    """

    pass


class EISFBinderDocument(Base):
    """SQLAlchemy model representing site-isolated regulatory documents.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "eisf_binder_documents"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    filename: Mapped[str] = mapped_column(String(255), nullable=False)
    _content: Mapped[str] = mapped_column("content", String, nullable=False)
    sha256_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    uploaded_by: Mapped[str] = mapped_column(String(255), nullable=False)
    is_redacted: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    parent_document_id: Mapped[Optional[str]] = mapped_column(
        String(36), nullable=True, index=True
    )

    @property
    def content(self) -> bytes:
        """Returns document content as bytes.

        Requirements: PRD-SYS-001
        """
        return self._content.encode("utf-8")

    @content.setter
    def content(self, value: bytes) -> None:
        """Sets document content from bytes.

        Requirements: PRD-SYS-001
        """
        self._content = value.decode("utf-8", errors="ignore")


class EISFBinderService:
    """Service class handling site isolation checks and non-destructive PHI redactions on eISF binders.

    Requirements: PRD-SYS-001
    """

    def __init__(self, session) -> None:
        """Initialize the binder service with a database session.

        Args:
            session: SQLAlchemy async database session.
        """
        self.session = session
        self.redactor = PHIRedactorService()

    async def get_site_binder(
        self, site_id: str, requesting_user: Principal
    ) -> List[EISFBinderDocument]:
        """Query site-scoped documents for a site if the user is authorized.

        Args:
            site_id: Target investigator site ID.
            requesting_user: Normalized Principal record of requesting user.

        Returns:
            List of non-redacted EISFBinderDocument records for the site.

        Raises:
            PermissionError: If requesting user is not authorized to access site_id.
        """
        if not can_access_site(requesting_user, site_id):
            raise PermissionError("Access Denied: Cross-site boundaries enforced.")

        stmt = select(EISFBinderDocument).where(
            (EISFBinderDocument.site_id == site_id)
            & (EISFBinderDocument.is_redacted.is_(False))
        )
        res = await self.session.execute(stmt)
        return list(res.scalars().all())

    async def upload_site_document(
        self,
        site_id: str,
        filename: str,
        content: bytes,
        uploading_user: Principal,
    ) -> EISFBinderDocument:
        """Process document upload and register document record under specific site.

        Args:
            site_id: Target investigator site ID.
            filename: Original file name.
            content: Raw document content bytes.
            uploading_user: Normalized Principal record of uploader.

        Returns:
            The created EISFBinderDocument instance.

        Raises:
            PermissionError: If uploading user is not authorized for site_id.
        """
        if not can_access_site(uploading_user, site_id):
            raise PermissionError("Access Denied: Cross-site boundaries enforced.")

        sha256_hash = hashlib.sha256(content).hexdigest()

        doc = EISFBinderDocument(
            site_id=site_id,
            filename=filename,
            content=content,
            sha256_hash=sha256_hash,
            uploaded_by=uploading_user.user_id,
            is_redacted=False,
            parent_document_id=None,
        )

        self.session.add(doc)
        await self.session.flush()
        await self.session.commit()
        return doc

    async def create_redacted_copy(
        self,
        document_id: str,
        phi_terms: List[str],
    ) -> EISFBinderDocument:
        """Create a redacted copy of a document while preserving the original.

        Args:
            document_id: The unique identifier of the source document.
            phi_terms: Specific terms/patient names to redact.

        Returns:
            The newly created redacted EISFBinderDocument record.

        Raises:
            ValueError: If source document with document_id does not exist.
        """
        stmt = select(EISFBinderDocument).where(EISFBinderDocument.id == document_id)
        res = await self.session.execute(stmt)
        original_doc = res.scalar_one_or_none()

        if not original_doc:
            raise ValueError(f"Source document with ID '{document_id}' not found.")

        # Apply redaction workflow
        redacted_content = self.redactor.redact_content(original_doc.content, phi_terms)
        redacted_hash = hashlib.sha256(redacted_content).hexdigest()

        # Generate a descriptive filename for the redacted successor
        if "." in original_doc.filename:
            parts = original_doc.filename.rsplit(".", 1)
            redacted_filename = f"{parts[0]}_redacted.{parts[1]}"
        else:
            redacted_filename = f"{original_doc.filename}_redacted"

        redacted_doc = EISFBinderDocument(
            site_id=original_doc.site_id,
            filename=redacted_filename,
            content=redacted_content,
            sha256_hash=redacted_hash,
            uploaded_by=original_doc.uploaded_by,
            is_redacted=True,
            parent_document_id=original_doc.id,
        )

        self.session.add(redacted_doc)
        await self.session.flush()
        await self.session.commit()
        return redacted_doc
