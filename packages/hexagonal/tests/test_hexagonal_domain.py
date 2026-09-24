from datetime import datetime

import pytest
import pytest_asyncio

from apps.execution.adapters.repositories import (
    InMemoryAuditRepository,
    InMemoryConsentRepository,
    InMemorySubjectRepository,
    SQLAlchemyAuditRepository,
    SQLAlchemyConsentRepository,
    SQLAlchemySubjectRepository,
)
from apps.execution.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ConsentFormRecordDomain,
    ConsentSignatureDomain,
)
from apps.execution.subject_lifecycle import (
    InvalidStateTransitionError,
    LockedFactorMutationError,
)


def test_subject_lifecycle_pure_domain_transitions():
    """Verify that clinical subject state transitions obey strict lifecycle paths purely in the domain layer, database-free.

    All states and transition logic are validated database-free to ensure perfect
    isolation in our pure Python hexagonal domain models.

    @req:PRD-SYS-001
    """
    # 1. Start in SCREENING (default state for new clinical subjects)
    # The clinical subject state machine defaults newly registered participants
    # to the SCREENING state to enforce initial eligibility validation gates.
    # We verify that screening is the foundational starting status.
    subject = ClinicalSubjectDomain(
        subject_id="SUBJ-001", study_id="STUDY_A", strat_factors={"age": "GE_65"}
    )
    assert subject.status == "SCREENING"
    assert subject.strat_factors == {"age": "GE_65"}

    # 2. Screening -> Enrolled is valid
    subject.status = "ENROLLED"
    assert subject.status == "ENROLLED"

    # 3. Enrolled -> Active is illegal (must go through Randomized)
    with pytest.raises(InvalidStateTransitionError):
        subject.status = "ACTIVE"


def test_subject_stratification_factors_locking_domain():
    """Verify that stratification factors are mutable pre-randomization but locked post-randomization.

    @req:PRD-SYS-001
    """
    subject = ClinicalSubjectDomain(
        subject_id="SUBJ-001", study_id="STUDY_A", strat_factors={"age": "GE_65"}
    )
    # Mutable pre-randomization
    subject.strat_factors = {"age": "LT_65"}
    assert subject.strat_factors == {"age": "LT_65"}

    # Transition to Enrolled
    subject.status = "ENROLLED"

    # Transition to Randomized using randomization helper
    subject.randomize(
        randomization_id="RAND-111",
        kit_reference="KIT-999",
        strat_factors={"age": "LT_65"},
    )
    assert subject.status == "RANDOMIZED"
    assert subject.randomization_id == "RAND-111"
    assert subject.kit_reference == "KIT-999"

    # Idempotency is allowed (setting same factors post-randomization)
    subject.strat_factors = {"age": "LT_65"}

    # Changing factors post-randomization triggers domain error
    with pytest.raises(LockedFactorMutationError):
        subject.strat_factors = {"age": "GE_65"}


def test_unblinding_and_withdrawal_domain():
    """Verify emergency unblinding and withdrawal transitions update fields correctly in domain model.

    @req:PRD-SYS-001
    """
    subject = ClinicalSubjectDomain(
        subject_id="SUBJ-001", study_id="STUDY_A", status="SCREENING"
    )
    # Move through flow
    subject.status = "ENROLLED"
    subject.randomize("RAND-101", "KIT-202", {"strata": "A"})

    # Emergency unblinding
    subject.unblind(unblinded_by="cra_john", reason="Severe allergic reaction")
    assert subject.status == "UNBLINDED"
    assert subject.is_unblinded is True
    assert subject.unblinded_by == "cra_john"
    assert subject.unblinded_reason == "Severe allergic reaction"
    assert isinstance(subject.unblinded_at, datetime)

    # Withdrawal of consent
    subject.withdraw(reason="Patient moved away")
    assert subject.status == "WITHDRAWN"
    assert subject.withdrawal_reason == "Patient moved away"
    assert isinstance(subject.withdrawn_at, datetime)


def test_consent_signature_immutability_domain():
    """Verify that signed consent records cannot be modified or deleted directly in the domain layer.

    @req:PRD-SYS-001
    """
    sig = ConsentSignatureDomain(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1",
        printed_name="Alice Smith",
        status="SIGNED",
    )

    # Direct modification of attributes throws ValueError
    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        sig.printed_name = "Alice Jones"

    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        sig.status = "REVOKED"

    # Deleting properties throws ValueError
    with pytest.raises(ValueError, match="Cannot delete consent records"):
        del sig.printed_name


def test_consent_form_record_immutability_domain():
    """Verify that ConsentFormRecord blocks updates when SIGNED, except status transition to RECONSENT_REQUIRED.

    @req:PRD-SYS-001
    """
    record = ConsentFormRecordDomain(
        subject_id="SUBJ-001",
        icf_version_id="ICF-V1",
        printed_name="Alice Smith",
        status="PENDING",
    )

    # Allowed to modify in PENDING
    record.printed_name = "Alice Bob"
    assert record.printed_name == "Alice Bob"

    # Transition to SIGNED
    record.status = "SIGNED"

    # Once SIGNED, modification of fields is blocked
    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        record.printed_name = "Alice Jones"

    # Status transition to anything other than RECONSENT_REQUIRED is blocked
    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        record.status = "PENDING"

    # Status transition to RECONSENT_REQUIRED is allowed
    record.status = "RECONSENT_REQUIRED"
    assert record.status == "RECONSENT_REQUIRED"

    # Under RECONSENT_REQUIRED, immutable fields like subject_id cannot be changed
    with pytest.raises(ValueError, match="Cannot modify signed consent records"):
        record.subject_id = "SUBJ-999"


def test_safety_audit_log_immutability_domain():
    """Verify that AuditLog domain model blocks updates and deletions to enforce GxP append-only integrity.

    @req:PRD-SYS-001
    """
    log = AuditLogDomain(
        table_name="clinical_subjects",
        record_id="SUBJ-001",
        action="INSERT",
        user_id="cra_alice",
        change_reason="Subject created",
    )

    # Direct update is blocked
    with pytest.raises(
        ValueError, match="Audit logs are append-only and cannot be modified"
    ):
        log.user_id = "cra_bob"

    # Deletion of properties is blocked
    with pytest.raises(ValueError, match="Deletion of AuditLog is strictly forbidden"):
        del log.change_reason


@pytest.mark.asyncio
async def test_workflows_with_in_memory_repositories():
    """Verify clean workflow simulation utilizing mock in-memory repositories completely database-free.

    @req:PRD-SYS-001
    """
    subj_repo = InMemorySubjectRepository()
    consent_repo = InMemoryConsentRepository()
    audit_repo = InMemoryAuditRepository()

    # 1. Create and save subject
    subject = ClinicalSubjectDomain(
        id="subj-uuid-1",
        subject_id="SUBJ-001",
        study_id="STUDY_A",
        strat_factors={"age": "GE_65"},
    )
    await subj_repo.save(subject)

    # Retrieve subject
    fetched = await subj_repo.get_by_id("subj-uuid-1")
    assert fetched is not None
    assert fetched.subject_id == "SUBJ-001"
    assert fetched.status == "SCREENING"

    # 2. Change state and randomize
    fetched.status = "ENROLLED"
    fetched.randomize("RAND-1", "KIT-1", {"age": "GE_65"})
    await subj_repo.save(fetched)

    # Verify updated state retrieved
    fetched_updated = await subj_repo.get_by_id("subj-uuid-1")
    assert fetched_updated.status == "RANDOMIZED"
    assert fetched_updated.randomization_id == "RAND-1"

    # 3. Try deleting audit log through repository
    log = AuditLogDomain(
        id="log-1",
        table_name="clinical_subjects",
        record_id="subj-uuid-1",
        action="UPDATE",
    )
    await audit_repo.save(log)

    with pytest.raises(ValueError, match="Deletion of AuditLog is strictly forbidden"):
        await audit_repo.delete(log)

    # 4. Try deleting consent form record through repository
    form = ConsentFormRecordDomain(id="form-1", subject_id="SUBJ-001", status="SIGNED")
    await consent_repo.save_form_record(form)

    with pytest.raises(ValueError, match="Cannot delete consent records"):
        await consent_repo.delete_form_record(form)


# ---------------------------------------------------------------------------
# Database Integration Tests for Repository Adapters
# ---------------------------------------------------------------------------


@pytest_asyncio.fixture
async def db_session():
    import os

    from apps.execution.database.core import db_manager
    from apps.execution.database.migrate import deploy_database_triggers
    from apps.execution.database.models import Base

    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)

    async with db_manager.get_session_maker()() as session:
        yield session

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_sqlalchemy_subject_repository_persistence(db_session):
    """Verify that SQLAlchemySubjectRepository correctly persists changes to/from the database.

    @req:PRD-SYS-001
    """
    repo = SQLAlchemySubjectRepository(db_session)

    # 1. Save new subject domain model
    subj_domain = ClinicalSubjectDomain(
        id="subj-uuid-999",
        subject_id="SUBJ-999",
        study_id="STUDY_PERSIST",
        status="SCREENING",
        strat_factors={"gender": "F"},
    )
    await repo.save(subj_domain)
    await db_session.commit()

    # 2. Retrieve subject domain model
    fetched = await repo.get_by_id("subj-uuid-999")
    assert fetched is not None
    assert fetched.subject_id == "SUBJ-999"
    assert fetched.status == "SCREENING"
    assert fetched.strat_factors == {"gender": "F"}

    # 3. Transition status and save again
    fetched.status = "ENROLLED"
    await repo.save(fetched)
    await db_session.commit()

    # 4. Confirm update persisted
    fetched_updated = await repo.get_by_id("subj-uuid-999")
    assert fetched_updated.status == "ENROLLED"


@pytest.mark.asyncio
async def test_sqlalchemy_consent_repository_persistence(db_session):
    """Verify that SQLAlchemyConsentRepository correctly persists consent models.

    @req:PRD-SYS-001
    """
    repo = SQLAlchemyConsentRepository(db_session)

    # 1. Save consent signature
    sig_domain = ConsentSignatureDomain(
        id="sig-uuid-1",
        subject_id="SUBJ-999",
        icf_version_id="ICF-V1",
        printed_name="Alice Smith",
        status="SIGNED",
    )
    await repo.save_signature(sig_domain)

    # 2. Save consent form record
    form_domain = ConsentFormRecordDomain(
        id="form-uuid-1",
        subject_id="SUBJ-999",
        icf_version_id="ICF-V1",
        printed_name="Alice Smith",
        status="PENDING",
    )
    await repo.save_form_record(form_domain)
    await db_session.commit()

    # 3. Fetch and assert
    fetched_sig = await repo.get_signature_by_id("sig-uuid-1")
    assert fetched_sig is not None
    assert fetched_sig.printed_name == "Alice Smith"
    assert fetched_sig.status == "SIGNED"

    fetched_form = await repo.get_form_record_by_id("form-uuid-1")
    assert fetched_form is not None
    assert fetched_form.status == "PENDING"


@pytest.mark.asyncio
async def test_sqlalchemy_audit_repository_persistence(db_session):
    """Verify that SQLAlchemyAuditRepository correctly persists safety audit logs.

    @req:PRD-SYS-001
    """
    repo = SQLAlchemyAuditRepository(db_session)

    log_domain = AuditLogDomain(
        id="log-uuid-1",
        table_name="clinical_subjects",
        record_id="subj-uuid-1",
        action="INSERT",
        user_id="user-1",
        change_reason="Created subject",
    )
    await repo.save(log_domain)
    await db_session.commit()

    fetched = await repo.get_by_id("log-uuid-1")
    assert fetched is not None
    assert fetched.table_name == "clinical_subjects"
    assert fetched.action == "INSERT"
    assert fetched.change_reason == "Created subject"
