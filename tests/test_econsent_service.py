from datetime import datetime

import pytest
import pytest_asyncio
from execution.econsent_models import EConsentSignRequest
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.econsent.services.econsent_service import (
    EConsentWorkflowEngine,
    process_econsent_signature,
)
from apps.execution.database.models import (
    Base,
    ComprehensionQuizResult,
    ConsentFormRecord,
    ConsentSignature,
)
from packages.security.audit_logger import audit_logger_engine


@pytest_asyncio.fixture()
async def db_session():
    """Setup in-memory SQLite database and return a session maker for testing.

    Requirements: PRD-SYS-001
    """
    engine = create_async_engine("sqlite+aiosqlite:///:memory:", echo=False)
    async with engine.begin() as conn:
        from sqlalchemy import text

        await conn.execute(text("ATTACH DATABASE ':memory:' AS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)

    session_maker = async_sessionmaker(
        engine, class_=AsyncSession, expire_on_commit=False
    )
    async with session_maker() as session:
        yield session

    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await engine.dispose()


@pytest.mark.asyncio
async def test_successful_signature_capture(db_session: AsyncSession):
    """Verify that a valid signature capture creates a signed PDF and updates DB state to SIGNED.

    Requirements: PRD-SYS-001
    """
    # 1. Seed a passing comprehension quiz result
    quiz = ComprehensionQuizResult(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        score=95.0,
        passed=True,
    )
    db_session.add(quiz)
    await db_session.commit()

    # 2. Prepare valid capture signature request
    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg><path d='M 10 10 L 20 20'/></svg>",
        otp_auth_code="123456",
        reason_for_change="I consent to join this trial.",
    )

    # 3. Process the signature
    # Clear the audit logger chain so we can assert precisely
    audit_logger_engine._chain.clear()

    response = await process_econsent_signature(db_session, payload)

    assert response.consent_record_id is not None
    assert response.signed_pdf_url.startswith("file:///tmp/consent_pdfs/")
    assert isinstance(response.signature_timestamp_utc, datetime)
    assert len(response.verification_hash) == 64

    # 4. Verify DB updates
    stmt_record = select(ConsentFormRecord).where(
        ConsentFormRecord.id == response.consent_record_id
    )
    record = (await db_session.execute(stmt_record)).scalar_one()
    assert record.status == "SIGNED"
    assert record.is_verified is True
    assert record.printed_name == "John Doe"

    stmt_sig = select(ConsentSignature).where(ConsentSignature.subject_id == "SUBJ-001")
    sig = (await db_session.execute(stmt_sig)).scalars().first()
    assert sig is not None
    assert sig.status == "SIGNED"
    assert sig.verification_hash == response.verification_hash

    # 5. Verify central audit logger events are recorded correctly
    assert len(audit_logger_engine._chain) == 1
    audit_log = audit_logger_engine._chain[0]
    assert audit_log.service_name == "econsent"
    assert audit_log.action_type == "SIGN"
    assert audit_log.entity_name == "ConsentFormRecord"
    assert audit_log.entity_id == response.consent_record_id
    assert audit_log.reason_for_change == "I consent to join this trial."
    assert audit_log.details["event_type"] == "ECONSENT_SIGNED"
    assert audit_log.details["subject_id"] == "SUBJ-001"
    assert audit_log.details["icf_version_id"] == "ICF-V1.0"


@pytest.mark.asyncio
async def test_failed_comprehension_quiz_blocks_signature(db_session: AsyncSession):
    """Verify that a failed comprehension quiz blocks signature submission.

    Requirements: PRD-SYS-001
    """
    # Seed a failed comprehension quiz (score < 80%)
    quiz = ComprehensionQuizResult(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        score=75.0,
        passed=True,  # wait, even if "passed" is True, score is < 80.0
    )
    db_session.add(quiz)
    await db_session.commit()

    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg></svg>",
        otp_auth_code="123456",
        reason_for_change="I consent.",
    )

    with pytest.raises(
        ValueError, match="Comprehension quiz not passed with required score >= 80%"
    ):
        await process_econsent_signature(db_session, payload)


@pytest.mark.asyncio
async def test_incomplete_comprehension_quiz_blocks_signature(db_session: AsyncSession):
    """Verify that an incomplete/missing comprehension quiz blocks signature submission.

    Requirements: PRD-SYS-001
    """
    # No quiz seeded in DB at all!
    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg></svg>",
        otp_auth_code="123456",
        reason_for_change="I consent.",
    )

    with pytest.raises(
        ValueError, match="Comprehension quiz not passed with required score >= 80%"
    ):
        await process_econsent_signature(db_session, payload)


@pytest.mark.asyncio
async def test_invalid_otp_auth_code_blocks_signature(db_session: AsyncSession):
    """Verify that an invalid OTP authentication code blocks signature submission.

    Requirements: PRD-SYS-001
    """
    # Seed a passing comprehension quiz result
    quiz = ComprehensionQuizResult(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        score=100.0,
        passed=True,
    )
    db_session.add(quiz)
    await db_session.commit()

    payload = EConsentSignRequest(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1.0",
        printed_name="John Doe",
        relationship_to_subject="SELF",
        signature_svg="<svg></svg>",
        otp_auth_code="wrong_code",  # invalid
        reason_for_change="I consent.",
    )

    with pytest.raises(ValueError, match="Invalid OTP authentication code"):
        await process_econsent_signature(db_session, payload)


@pytest.mark.asyncio
async def test_workflow_engine_legacy_signature_capture(db_session: AsyncSession):
    """Verify that EConsentWorkflowEngine successfully executes raw signature capture.

    Requirements: PRD-SYS-001
    """
    engine = EConsentWorkflowEngine(db_session)
    signature = await engine.execute_signature_capture(
        subject_id="SUBJ-002",
        icf_version_id="ICF-V2.0",
        printed_name="Jane Smith",
        signature_svg="<svg><path d='M0 0 L10 10'/></svg>",
        reason_for_change="Captured via tablet UI",
    )

    assert signature.id is not None
    assert signature.subject_id == "SUBJ-002"
    assert signature.icf_version_id == "ICF-V2.0"
    assert signature.printed_name == "Jane Smith"
    assert signature.status == "SIGNED"
    assert len(signature.verification_hash) == 64

    # Verify database entry
    stmt = select(ConsentSignature).where(ConsentSignature.subject_id == "SUBJ-002")
    db_sig = (await db_session.execute(stmt)).scalar_one()
    assert db_sig.id == signature.id
