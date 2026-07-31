"""GxP 21 CFR Part 11 audit verification test suite for eConsent workflow.

Verifies that patient consent records are immutable, securely bound to the
active ICF version index, store high-resolution SVG signatures, and correctly trigger
re-consent upon protocol/ICF version updates.

Requirements: PRD-SYS-001
"""

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
    ConsentFormRecord,
    ConsentSignature,
)
from apps.execution.services.econsent_service import EConsentService


@pytest_asyncio.fixture(name="db_session")
async def db_session_fixture():
    """Initializes a test in-memory SQLite database and yields an active session.

    Requirements: PRD-SYS-001
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        yield session

    await db_manager.close()


@pytest.mark.asyncio
async def test_econsent_signature_audit_compliance(db_session) -> None:
    """Validate eConsent signature capture records GxP 21 CFR Part 11 compliant audit trail.

    Requirements: PRD-SYS-001
    """
    # Create candidate ConsentFormRecord for ICF Version 2.0
    candidate = ConsentFormRecord(
        subject_id="SUBJ-101",
        icf_version_id="icf_v2_0",
        status="PENDING",
    )
    db_session.add(candidate)
    await db_session.commit()

    service = EConsentService(session=db_session)
    signature = await service.sign_informed_consent(
        subject_id="SUBJ-101",
        icf_version_id="icf_v2_0",
        printed_name="Jane Doe",
        signature_svg_data="data:image/svg+xml;base64,PHN2Zz4...",
        otp_auth_code="992812",
        meaning="Subject Informed Consent Sign-Off",
    )

    assert signature.status == "SIGNED"
    assert signature.printed_name == "Jane Doe"
    assert signature.icf_version_id == "icf_v2_0"


@pytest.mark.asyncio
async def test_econsent_signature_capture_success(db_session) -> None:
    """Validate complete successful eConsent signing capture and database presence.

    Requirements: PRD-SYS-001
    """
    # Create candidate ConsentFormRecord
    candidate = ConsentFormRecord(
        subject_id="SUBJ-102",
        icf_version_id="icf_v2_0",
        status="PENDING",
    )
    db_session.add(candidate)
    await db_session.commit()

    service = EConsentService(session=db_session)
    signature = await service.sign_informed_consent(
        subject_id="SUBJ-102",
        icf_version_id="icf_v2_0",
        printed_name="John Doe",
        signature_svg_data="data:image/svg+xml;base64,PD94bW...",
        otp_auth_code="123456",
        meaning="Subject Informed Consent Sign-Off",
    )

    # Assert status updates to SIGNED and ConsentSignature is stored
    assert signature.status == "SIGNED"
    assert signature.printed_name == "John Doe"
    assert signature.signature_svg_data == "data:image/svg+xml;base64,PD94bW..."
    assert signature.otp_auth_code == "123456"
    assert signature.meaning == "Subject Informed Consent Sign-Off"
    assert signature.cryptographic_token is not None
    assert len(signature.cryptographic_token) == 64  # SHA-256 length

    # Refresh and query ConsentFormRecord
    stmt_record = select(ConsentFormRecord).where(
        ConsentFormRecord.subject_id == "SUBJ-102"
    )
    res_record = await db_session.execute(stmt_record)
    record = res_record.scalar_one()
    assert record.status == "SIGNED"
    assert record.signed_at is not None

    # Query the stored ConsentSignature record
    stmt_sig = select(ConsentSignature).where(ConsentSignature.subject_id == "SUBJ-102")
    res_sig = await db_session.execute(stmt_sig)
    db_signature = res_sig.scalar_one()
    assert db_signature.id == signature.id

    # Trying to sign already-signed record raises error
    with pytest.raises(ValueError, match="Consent form record is already signed"):
        await service.sign_informed_consent(
            subject_id="SUBJ-102",
            icf_version_id="icf_v2_0",
            printed_name="John Doe Duplicate",
            signature_svg_data="data:image/svg+xml;base64,PD94bW...",
            otp_auth_code="123456",
        )


@pytest.mark.asyncio
async def test_protocol_amendment_triggers_reconsent(db_session) -> None:
    """Validate that protocol amendment updates the subject status automatically to RECONSENT_REQUIRED.

    Requirements: PRD-SYS-001
    """
    # Create candidate clinical subject and transition legally to ACTIVE
    subject = ClinicalSubject(
        subject_id="SUBJ-103",
        study_id="STUDY-ABC",
    )
    db_session.add(subject)
    await db_session.flush()

    subject.status = "ENROLLED"
    subject.status = "RANDOMIZED"
    subject.status = "ACTIVE"
    await db_session.flush()

    # Create candidate ConsentFormRecord for Version 2.0
    candidate = ConsentFormRecord(
        subject_id="SUBJ-103",
        icf_version_id="icf_v2_0",
        status="PENDING",
    )
    db_session.add(candidate)
    await db_session.commit()

    service = EConsentService(session=db_session)

    # Subject signs ICF Version 2.0
    await service.sign_informed_consent(
        subject_id="SUBJ-103",
        icf_version_id="icf_v2_0",
        printed_name="Alice Smith",
        signature_svg_data="data:image/svg+xml;base64,PHN...",
        otp_auth_code="789123",
        meaning="Subject Informed Consent Sign-Off",
    )

    # Subject is active and signed
    assert subject.status == "ACTIVE"

    # Now, protocol amendment: Update study ICF version to v3.0
    await service.update_study_icf_version(
        study_id="STUDY-ABC", new_icf_version_id="icf_v3_0"
    )

    # Assert subject status automatically updates to RECONSENT_REQUIRED
    assert subject.status == "RECONSENT_REQUIRED"

    # Assert outstanding previous signed consent form is transitioned to RECONSENT_REQUIRED
    stmt_record = select(ConsentFormRecord).where(
        ConsentFormRecord.subject_id == "SUBJ-103",
        ConsentFormRecord.icf_version_id == "icf_v2_0",
    )
    res_record = await db_session.execute(stmt_record)
    record = res_record.scalar_one()
    assert record.status == "RECONSENT_REQUIRED"


@pytest.mark.asyncio
async def test_consent_record_immutability(db_session) -> None:
    """Validate Part 11 compliance prevents editing or deleting signed consent records.

    Requirements: PRD-SYS-001
    """
    # Create and sign record
    candidate = ConsentFormRecord(
        subject_id="SUBJ-104",
        icf_version_id="icf_v2_0",
        status="PENDING",
    )
    db_session.add(candidate)
    await db_session.commit()

    service = EConsentService(session=db_session)
    _signature = await service.sign_informed_consent(
        subject_id="SUBJ-104",
        icf_version_id="icf_v2_0",
        printed_name="Bob Brown",
        signature_svg_data="data:image/svg+xml;base64,U0hB...",
        otp_auth_code="456789",
        meaning="Subject Informed Consent Sign-Off",
    )

    # Refresh record
    stmt_record = select(ConsentFormRecord).where(
        ConsentFormRecord.subject_id == "SUBJ-104"
    )
    res_record = await db_session.execute(stmt_record)
    record = res_record.scalar_one()

    # Attempting to modify signed record fields raises ValueError
    # Let's try modifying an existing field of ConsentFormRecord
    record.subject_id = "SUBJ-X"

    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        await db_session.commit()

    await db_session.rollback()

    # Attempting to delete ConsentFormRecord raises ValueError
    res_record = await db_session.execute(stmt_record)
    record = res_record.scalar_one()
    await db_session.delete(record)

    with pytest.raises(
        ValueError, match="Hard deletion of ConsentFormRecord is forbidden"
    ):
        await db_session.commit()

    await db_session.rollback()

    # Query signature and attempt to modify fields
    stmt_sig = select(ConsentSignature).where(ConsentSignature.subject_id == "SUBJ-104")
    res_sig = await db_session.execute(stmt_sig)
    sig_record = res_sig.scalar_one()

    sig_record.printed_name = "Bob Black"
    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        await db_session.commit()

    await db_session.rollback()

    # Attempting to delete ConsentSignature raises ValueError
    res_sig = await db_session.execute(stmt_sig)
    sig_record = res_sig.scalar_one()
    await db_session.delete(sig_record)

    with pytest.raises(ValueError, match="Cannot delete consent records"):
        await db_session.commit()

    await db_session.rollback()
