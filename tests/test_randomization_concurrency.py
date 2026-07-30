import asyncio
import uuid

import pytest
import pytest_asyncio
from fastapi import HTTPException
from sqlalchemy import select

from apps.execution.cryptography import AllocationKeyManager
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
    RandomizationConfig,
    StratumState,
    SubjectRandomization,
)
from apps.execution.randomization_service import randomize_subject


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    from apps.execution.database.migrate import deploy_database_triggers

    # Use a named in-memory SQLite database with shared cache
    db_uri = f"sqlite+aiosqlite:///file:memdb_rand_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)

    # Keep one connection open to prevent in-memory database from being closed/wiped
    keepalive = await db_manager.engine.connect()

    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)

    await keepalive.close()
    await db_manager.close()


async def seed_randomization_data():
    current_user_id.set("admin")
    current_change_reason.set("Seed randomization test data")

    # Bootstrap AllocationKeyManager inside a session first to generate and store the salt
    key_mgr = AllocationKeyManager()

    @transactional(lambda: db_manager.get_session_maker()())
    async def bootstrap_keys():
        session = current_session.get()
        await key_mgr.load_from_db(session)

    await bootstrap_keys()

    # Now encrypt with the bootstrapped key
    encrypted_block = key_mgr.encrypt({"block_sizes": [4]})

    @transactional(lambda: db_manager.get_session_maker()())
    async def seed():
        session = current_session.get()
        config = RandomizationConfig(
            study_id="STUDY_CONCURRENCY",
            algorithm_type="STRATIFIED_BLOCK",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            stratification_factors={"gender": ["M", "F"]},
            encrypted_block_config=encrypted_block,
            seed=123,
        )
        session.add(config)

        # Pre-seed StratumState to avoid concurrent inserts in sqlite
        stratum = StratumState(
            study_id="STUDY_CONCURRENCY",
            stratum_key="gender=M",
            block_index=0,
            encrypted_sequence=None,
        )
        session.add(stratum)

        # Seed subjects (initially default to SCREENING)
        subjects = []
        for i in range(1, 6):
            subj = ClinicalSubject(
                subject_id=f"SUBJ_{i:03d}",
                study_id="STUDY_CONCURRENCY",
                strat_factors={"gender": "M"},
            )
            session.add(subj)
            subjects.append(subj)
        await session.flush()

        # Transition subjects 2, 3, 4, 5 to ENROLLED
        # Leave subject 1 in SCREENING state for failure tests
        for subj in subjects[1:]:
            subj.status = "ENROLLED"
        await session.flush()

    await seed()


@pytest.mark.asyncio
async def test_concurrent_randomization_unique_and_monotonic():
    """Verify that concurrent randomizations obtain unique and monotonic block positions."""
    await seed_randomization_data()

    # We will randomize subjects 2, 3, 4, 5 concurrently (all are ENROLLED)
    tasks = [
        randomize_subject(
            study_id="STUDY_CONCURRENCY",
            subject_id=f"SUBJ_{i:03d}",
            change_reason=f"Randomize subject {i}",
            user_id="investigator_1",
        )
        for i in range(2, 6)
    ]

    results = await asyncio.gather(*tasks)
    assert len(results) == 4

    # Let's query StratumState to see advanced block_index
    async with db_manager.get_session_maker()() as session:
        # Check SubjectRandomizations
        stmt_rand = select(SubjectRandomization).where(
            SubjectRandomization.study_id == "STUDY_CONCURRENCY"
        )
        res_rand = await session.execute(stmt_rand)
        rands = res_rand.scalars().all()
        print(f"DEBUG: Found {len(rands)} subject randomizations:")
        for r in rands:
            print(
                f"DEBUG: subject_id={r.subject_id}, stratum_key={r.stratum_key}, id={r.id}"
            )

        stmt = select(StratumState).where(
            StratumState.study_id == "STUDY_CONCURRENCY",
            StratumState.stratum_key == "gender=M",
        )
        res = await session.execute(stmt)
        stratum = res.scalars().first()
        print(
            f"DEBUG: StratumState block_index={stratum.block_index if stratum else 'None'}"
        )

        assert stratum is not None
        # Since 4 subjects are randomized, block_index should be exactly 4
        assert stratum.block_index == 4

        # Let's decrypt the sequence
        key_mgr = AllocationKeyManager()
        await key_mgr.load_from_db(session)
        decrypted_seq = key_mgr.decrypt(stratum.encrypted_sequence)
        assert len(decrypted_seq["sequence"]) == 4

        # Check that allocated treatment arms decrypt successfully
        allocated_arms = []
        for r in rands:
            decrypted_alloc = key_mgr.decrypt(r.encrypted_allocation)
            arm = decrypted_alloc["allocation"]
            assert arm in ["Arm A", "Arm B"]
            allocated_arms.append(arm)

        # Check arms are distributed 2 and 2
        assert allocated_arms.count("Arm A") == 2
        assert allocated_arms.count("Arm B") == 2


@pytest.mark.asyncio
async def test_forced_failure_rolls_back_atomically():
    """Verify that a forced failure rolls back both SubjectRandomization insert and StratumState block_index advancement."""
    await seed_randomization_data()

    # Let's verify initial state of StratumState
    async with db_manager.get_session_maker()() as session:
        stmt = select(StratumState).where(
            StratumState.study_id == "STUDY_CONCURRENCY",
            StratumState.stratum_key == "gender=M",
        )
        res = await session.execute(stmt)
        stratum = res.scalars().first()
        assert stratum is not None
        assert stratum.block_index == 0

    # Now attempt to randomize SUBJ_001 (which is still in SCREENING state) and assert it raises an error
    with pytest.raises((HTTPException, ValueError)):
        await randomize_subject(
            study_id="STUDY_CONCURRENCY",
            subject_id="SUBJ_001",
            change_reason="Attempt invalid randomization",
            user_id="investigator_1",
        )

    # Now verify that:
    # 1. StratumState.block_index is still 0
    # 2. No SubjectRandomization exists for SUBJ_001
    async with db_manager.get_session_maker()() as session:
        stmt_stratum = select(StratumState).where(
            StratumState.study_id == "STUDY_CONCURRENCY",
            StratumState.stratum_key == "gender=M",
        )
        res_stratum = await session.execute(stmt_stratum)
        stratum = res_stratum.scalars().first()
        assert stratum is not None
        assert stratum.block_index == 0
        assert stratum.encrypted_sequence is None

        stmt_rand = select(SubjectRandomization).where(
            SubjectRandomization.subject_id == "SUBJ_001"
        )
        res_rand = await session.execute(stmt_rand)
        rand = res_rand.scalars().first()
        assert rand is None
