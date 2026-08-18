"""
Tests for metadata-bound entity filtering and abstract consent client integration.
"""

import httpx
import pytest
from fastapi import HTTPException
from sqlalchemy import Column, Integer, String, select
from sqlalchemy.orm import DeclarativeBase

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, ClinicalSubject
from apps.execution.econsent_client import (
    EConsentClient,
    IConsentClient,
    IConsentVerificationClient,
    get_consent_verification_client,
    set_consent_verification_client,
)


class DummyForeignBase(DeclarativeBase):
    pass


class DummyForeignSiblingModel(DummyForeignBase):
    """Simulates a model from a sibling microservice (e.g., eTMF or CTMS)."""

    __tablename__ = "tmf_documents_custom"

    id = Column(Integer, primary_key=True, autoincrement=True)
    title = Column(String(255))


@pytest.mark.asyncio
async def test_shared_session_coexistence_auditing():
    """Verify that execution auditing filters entities strictly through AuditedModel inspection.

    When an AuditedModel and a foreign sibling service model coexist in a shared session,
    execution auditing logs the AuditedModel change without crashing or auditing foreign models.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        from apps.execution.database.models import Base

        await conn.run_sync(Base.metadata.create_all)
        await conn.run_sync(DummyForeignBase.metadata.create_all)

    async with db_manager.get_session_maker()() as session:
        # Create both an execution AuditedModel and a foreign sibling model in the same session
        subj = ClinicalSubject(
            subject_id="SUBJ-SHARED-01",
            study_id="STUDY-SHARED-01",
            site_id="SITE-01",
            status="SCREENING",
        )
        foreign_doc = DummyForeignSiblingModel(title="External Protocol Doc")

        session.add(subj)
        session.add(foreign_doc)
        await session.commit()

        # Verify that AuditLog captured ClinicalSubject but NOT DummyForeignSiblingModel
        stmt_subj_audit = select(AuditLog).where(
            AuditLog.table_name == "clinical_subjects",
            AuditLog.record_id == str(subj.id),
        )
        subj_audit = (await session.execute(stmt_subj_audit)).scalars().first()
        assert subj_audit is not None
        assert subj_audit.action == "INSERT"

        stmt_foreign_audit = select(AuditLog).where(
            AuditLog.table_name == "tmf_documents_custom"
        )
        foreign_audit = (await session.execute(stmt_foreign_audit)).scalars().first()
        assert foreign_audit is None

    await db_manager.close()


@pytest.mark.asyncio
async def test_abstract_consent_client_interface_and_graceful_network_failure():
    """Verify that consent client uses abstract IConsentVerificationClient interface

    and that network connection errors during econsent check fail gracefully without disrupting
    the primary execution database transaction.
    """
    client = get_consent_verification_client()
    assert isinstance(client, IConsentVerificationClient)
    assert isinstance(client, IConsentClient)
    assert isinstance(client, EConsentClient)

    # Test custom mock implementation of IConsentVerificationClient
    class MockConsentVerificationClient(IConsentVerificationClient):
        async def get_subject_consent_status(
            self,
            subject_pseudonym: str,
            study_id: str | None = None,
            client: httpx.AsyncClient | None = None,
        ) -> dict:
            raise HTTPException(
                status_code=502,
                detail="Failed to connect to eConsent service: ConnectionRefusedError",
            )

    mock_client = MockConsentVerificationClient()
    set_consent_verification_client(mock_client)

    try:
        assert get_consent_verification_client() is mock_client

        db_manager.init_db("sqlite+aiosqlite:///:memory:")
        async with db_manager.engine.begin() as conn:
            from apps.execution.database.models import Base

            await conn.run_sync(Base.metadata.create_all)

        async with db_manager.get_session_maker()() as session:
            # Updating a subject when econsent service is down should fail gracefully
            subj = ClinicalSubject(
                subject_id="SUBJ-RESILIENT-01",
                study_id="STUDY-RESILIENT-01",
                site_id="SITE-01",
                status="SCREENING",
            )
            session.add(subj)
            await session.commit()

            # Session commit succeeded despite 502 network error from econsent service
            stmt = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == "SUBJ-RESILIENT-01"
            )
            fetched_subj = (await session.execute(stmt)).scalars().first()
            assert fetched_subj is not None

        await db_manager.close()
    finally:
        # Reset default consent client
        set_consent_verification_client(None)
