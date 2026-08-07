from datetime import datetime

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.context import (
    current_change_reason,
    current_session,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
)
from apps.execution.subject_lifecycle import (
    InvalidStateTransitionError,
    LockedFactorMutationError,
    guard_subject_transition,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    import os

    from apps.execution.database.migrate import deploy_database_triggers

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
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def test_pure_python_transition_guard():
    """Verify that only documented transitions are allowed by the pure-Python guard."""
    # Screen fails and screen passes
    guard_subject_transition("SCREENING", "SCREEN_FAILED")
    guard_subject_transition("SCREENING", "ENROLLED")

    # Enroll to randomize
    guard_subject_transition("ENROLLED", "RANDOMIZED")

    # Randomized to active or withdrawn or unblinded
    guard_subject_transition("RANDOMIZED", "ACTIVE")
    guard_subject_transition("RANDOMIZED", "WITHDRAWN")
    guard_subject_transition("RANDOMIZED", "UNBLINDED")

    # Active to completed, withdrawn, or unblinded
    guard_subject_transition("ACTIVE", "COMPLETED")
    guard_subject_transition("ACTIVE", "WITHDRAWN")
    guard_subject_transition("ACTIVE", "UNBLINDED")

    # Unblinded to withdrawn or completed
    guard_subject_transition("UNBLINDED", "WITHDRAWN")
    guard_subject_transition("UNBLINDED", "COMPLETED")

    # Same state is fine
    guard_subject_transition("SCREENING", "SCREENING")

    # Initial transition
    guard_subject_transition(None, "SCREENING")

    # Invalid transitions raise error
    with pytest.raises(InvalidStateTransitionError) as exc:
        guard_subject_transition("SCREENING", "ACTIVE")
    assert exc.value.error_code == "INVALID_STATE_TRANSITION"

    with pytest.raises(InvalidStateTransitionError):
        guard_subject_transition("SCREEN_FAILED", "ENROLLED")

    with pytest.raises(InvalidStateTransitionError):
        guard_subject_transition("WITHDRAWN", "ACTIVE")

    with pytest.raises(InvalidStateTransitionError):
        guard_subject_transition(None, "ACTIVE")


@pytest.mark.asyncio
async def test_subject_initial_state_and_persistence():
    """Verify that a subject begins in pre-randomization state SCREENING and conforms to GxP persistence."""
    current_user_id.set("investigator_1")
    current_change_reason.set("Enrolling new subject SUBJ-001")

    @transactional(lambda: db_manager.get_session_maker()())
    async def create_subject():
        session = current_session.get()
        subject = ClinicalSubject(
            subject_id="SUBJ-001",
            study_id="STUDY_XYZ",
            strat_factors={"age_group": "GE_65", "gender": "F"},
        )
        session.add(subject)
        await session.flush()
        return subject.id

    subj_id = await create_subject()

    # Verify initial state, factors and audited persistence
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        assert subj.status == "SCREENING"
        assert subj.strat_factors == {"age_group": "GE_65", "gender": "F"}
        assert subj.is_unblinded is False
        assert subj.randomization_id is None
        assert subj.kit_reference is None


@pytest.mark.asyncio
async def test_subject_state_transitions():
    """Verify allowed and forbidden state transitions persist to database, raising InvalidStateTransitionError when violating."""
    current_user_id.set("investigator_1")
    current_change_reason.set("Test subject transition paths")

    @transactional(lambda: db_manager.get_session_maker()())
    async def setup_subject():
        session = current_session.get()
        subject = ClinicalSubject(subject_id="SUBJ-002", study_id="STUDY_XYZ")
        session.add(subject)
        await session.flush()
        return subject.id

    subj_id = await setup_subject()

    # 1. Valid transitions: SCREENING -> ENROLLED -> RANDOMIZED
    @transactional(lambda: db_manager.get_session_maker()())
    async def transition_to_enrolled():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.status = "ENROLLED"
        await session.flush()

    await transition_to_enrolled()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        assert subj.status == "ENROLLED"

    # 2. Invalid transition: ENROLLED -> ACTIVE (must go through RANDOMIZED first)
    @transactional(lambda: db_manager.get_session_maker()())
    async def invalid_transition_attempt():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.status = "ACTIVE"
        await session.flush()

    with pytest.raises(InvalidStateTransitionError):
        await invalid_transition_attempt()


@pytest.mark.asyncio
async def test_stratification_factors_locking():
    """Verify that stratification factors are mutable in pre-randomization state but immutable post-randomization."""
    current_user_id.set("investigator_1")
    current_change_reason.set("Stratification factors test")

    @transactional(lambda: db_manager.get_session_maker()())
    async def setup_subject():
        session = current_session.get()
        subject = ClinicalSubject(
            subject_id="SUBJ-003",
            study_id="STUDY_XYZ",
            strat_factors={"site": "SITE-A"},
        )
        session.add(subject)
        await session.flush()
        return subject.id

    subj_id = await setup_subject()

    # 1. Mutate stratification factors in pre-randomization state (SCREENING) -> Allowed
    @transactional(lambda: db_manager.get_session_maker()())
    async def update_factors():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.strat_factors = {"site": "SITE-B"}
        await session.flush()

    await update_factors()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        assert subj.strat_factors == {"site": "SITE-B"}

    # 2. Transition to ENROLLED then RANDOMIZED
    @transactional(lambda: db_manager.get_session_maker()())
    async def randomize():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.status = "ENROLLED"
        await session.flush()
        # Call the randomize helper method
        subj.randomize(
            randomization_id="RAND-999",
            kit_reference="KIT-ABC",
            strat_factors={"site": "SITE-B"},
        )
        await session.flush()

    await randomize()

    # 3. Setting SAME stratification factors post-randomization -> Allowed (idempotency check)
    @transactional(lambda: db_manager.get_session_maker()())
    async def set_same_factors():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.strat_factors = {"site": "SITE-B"}
        await session.flush()

    await set_same_factors()

    # 4. Mutate stratification factors post-randomization -> LockedFactorMutationError
    @transactional(lambda: db_manager.get_session_maker()())
    async def mutate_factors_post_rand():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.strat_factors = {"site": "SITE-C"}
        await session.flush()

    with pytest.raises(LockedFactorMutationError) as exc:
        await mutate_factors_post_rand()
    assert exc.value.error_code == "LOCKED_FACTOR_MUTATION"


@pytest.mark.asyncio
async def test_unblinding_and_withdrawal_behavior():
    """Verify emergency unblinding and withdrawal transitions capture details and execute behavior correctly."""
    current_user_id.set("investigator_1")
    current_change_reason.set("Test unblind and withdrawal workflow")

    @transactional(lambda: db_manager.get_session_maker()())
    async def setup_subject():
        session = current_session.get()
        subject = ClinicalSubject(subject_id="SUBJ-004", study_id="STUDY_XYZ")
        session.add(subject)
        await session.flush()
        # transition to enrolled then randomized
        subject.status = "ENROLLED"
        await session.flush()
        subject.randomize("RAND-101", "KIT-X", {"gender": "M"})
        await session.flush()
        return subject.id

    subj_id = await setup_subject()

    # 1. Unblind subject
    @transactional(lambda: db_manager.get_session_maker()())
    async def unblind_subj():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.unblind(unblinded_by="doctor_smith", reason="SAE-Life-Threatening-Event")
        await session.flush()

    await unblind_subj()

    # Verify unblinding fields and state
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        assert subj.status == "UNBLINDED"
        assert subj.is_unblinded is True
        assert subj.unblinded_by == "doctor_smith"
        assert subj.unblinded_reason == "SAE-Life-Threatening-Event"
        assert isinstance(subj.unblinded_at, datetime)

    # 2. Withdraw subject
    @transactional(lambda: db_manager.get_session_maker()())
    async def withdraw_subj():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        subj.withdraw(reason="Subject withdrew consent")
        await session.flush()

    await withdraw_subj()

    # Verify withdrawal fields and state
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.id == subj_id)
        )
        subj = result.scalars().one()
        assert subj.status == "WITHDRAWN"
        assert subj.withdrawal_reason == "Subject withdrew consent"
        assert isinstance(subj.withdrawn_at, datetime)
