import asyncio
import logging
import secrets
from typing import Optional

import sqlalchemy.exc
from sqlalchemy import select

from apps.execution.cryptography import AllocationKeyManager
from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
    get_session,
)
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import (
    ClinicalSubject,
    RandomizationConfig,
    StratumState,
    SubjectRandomization,
)
from apps.execution.eligibility_service import verify_subject_eligible_for_randomization
from apps.execution.subject_lifecycle import guard_subject_transition

logger = logging.getLogger("randomization-service")


@transactional(lambda: db_manager.get_session_maker()())
async def _randomize_subject_tx(
    study_id: str,
    subject_id: str,
    kit_reference: Optional[str] = None,
) -> SubjectRandomization:
    session = get_session()

    # 1. Fetch Subject
    stmt_subj = select(ClinicalSubject).where(ClinicalSubject.subject_id == subject_id)
    result_subj = await session.execute(stmt_subj)
    subject = result_subj.scalars().first()
    if not subject:
        raise ValueError(f"Subject {subject_id} not found.")

    # 2. Enforce preconditions
    guard_subject_transition("ENROLLED", "RANDOMIZED")
    guard_subject_transition(subject.status, "RANDOMIZED")
    verify_subject_eligible_for_randomization(subject)

    # 3. Load study RandomizationConfig
    stmt_config = select(RandomizationConfig).where(
        RandomizationConfig.study_id == study_id,
        RandomizationConfig.is_deleted.is_(False),
    )
    result_config = await session.execute(stmt_config)
    config_row = result_config.scalars().first()
    if not config_row:
        raise ValueError(f"Randomization configuration not found for study {study_id}.")

    # 4. Instantiate Key Manager and load keys
    key_mgr = AllocationKeyManager()
    await key_mgr.load_from_db(session)

    block_sizes = None
    if config_row.encrypted_block_config:
        decrypted_block = key_mgr.decrypt(config_row.encrypted_block_config)
        block_sizes = decrypted_block.get("block_sizes")

    # 5. Initialize RTSMAllocator
    from apps.execution.rtsm_allocation import (
        RandomizationConfigSchema,
        RTSMAllocator,
        generate_canonical_stratum_key,
    )

    schema = RandomizationConfigSchema(
        algorithm_type=config_row.algorithm_type,
        arms_ratios=config_row.arms_ratios,
        stratification_factors=config_row.stratification_factors,
        block_sizes=block_sizes,
        seed=config_row.seed,
    )
    allocator = RTSMAllocator(schema)

    # Determine active factors and stratum key
    active_factors = []
    if config_row.stratification_factors:
        if isinstance(config_row.stratification_factors, dict):
            active_factors = list(config_row.stratification_factors.keys())
        else:
            active_factors = list(config_row.stratification_factors)

    stratum_key = generate_canonical_stratum_key(subject.strat_factors, active_factors)
    logger.warning(f"DEBUG_RAND: subject_id={subject_id}, stratum_key={stratum_key}")

    # 6. Load / lock StratumState
    stmt_stratum = (
        select(StratumState)
        .where(
            StratumState.study_id == study_id, StratumState.stratum_key == stratum_key
        )
        .with_for_update()
    )
    result_stratum = await session.execute(stmt_stratum)
    stratum = result_stratum.scalars().first()
    logger.warning(f"DEBUG_RAND: stratum found? {stratum is not None}")

    if not stratum:
        logger.warning("DEBUG_RAND: stratum NOT found, inserting...")
        # Create it on first use
        stratum = StratumState(
            study_id=study_id,
            stratum_key=stratum_key,
            block_index=0,
            encrypted_sequence=None,
        )
        session.add(stratum)
        await session.flush()

    # 7. Decrypt sequence if it exists
    sequence = None
    if stratum.encrypted_sequence:
        decrypted_seq = key_mgr.decrypt(stratum.encrypted_sequence)
        sequence = decrypted_seq.get("sequence")

    # 8. Query previous allocations if MINIMIZATION
    previous_allocations = None
    if config_row.algorithm_type == "MINIMIZATION":
        stmt_prev = (
            select(SubjectRandomization, ClinicalSubject)
            .join(
                ClinicalSubject,
                ClinicalSubject.subject_id == SubjectRandomization.subject_id,
            )
            .where(SubjectRandomization.study_id == study_id)
        )

        result_prev = await session.execute(stmt_prev)
        prev_rows = result_prev.all()

        previous_allocations = []
        for rand_row, subj_row in prev_rows:
            try:
                decrypted_alloc = key_mgr.decrypt(rand_row.encrypted_allocation)
                allocation = decrypted_alloc.get("allocation")
                if allocation:
                    previous_allocations.append(
                        {
                            "subject_factors": subj_row.strat_factors,
                            "allocation": allocation,
                        }
                    )
            except Exception:
                pass

    # 9. Perform allocation
    allocation_result = allocator.allocate(
        subject_factors=subject.strat_factors,
        sequence=sequence,
        block_index=stratum.block_index,
        previous_allocations=previous_allocations,
    )

    allocated_arm = allocation_result["allocation"]

    # 10. Update StratumState sequence and index
    if "updated_sequence" in allocation_result:
        updated_seq = allocation_result["updated_sequence"]
        stratum.encrypted_sequence = key_mgr.encrypt(
            {"sequence": updated_seq}, session=session
        )

    if "updated_block_index" in allocation_result:
        stratum.block_index = allocation_result["updated_block_index"]

    # 11. Create SubjectRandomization row
    if not kit_reference:
        kit_reference = f"KIT-{secrets.token_hex(4).upper()}"

    encrypted_alloc = key_mgr.encrypt({"allocation": allocated_arm}, session=session)

    assignment = SubjectRandomization(
        study_id=study_id,
        site_id=subject.site_id,
        subject_id=subject_id,
        stratum_key=stratum_key,
        encrypted_allocation=encrypted_alloc,
        kit_reference=kit_reference,
    )
    session.add(assignment)
    await session.flush()

    # 12. Transition Subject and assign details
    subject.randomize(
        randomization_id=assignment.id,
        kit_reference=kit_reference,
        strat_factors=subject.strat_factors,
    )

    return assignment


RANDOMIZATION_LOCK = asyncio.Lock()


async def randomize_subject(
    study_id: str,
    subject_id: str,
    change_reason: str,
    user_id: str,
    kit_reference: Optional[str] = None,
) -> SubjectRandomization:
    """
    Service to randomize a subject for a study with bounded retries
    around unique-constraint or serialization failures.
    """
    user_token = current_user_id.set(user_id)
    reason_token = current_change_reason.set(change_reason)
    async with RANDOMIZATION_LOCK:
        try:
            max_retries = 5
            for attempt in range(max_retries):
                try:
                    # Run the transactional function
                    return await _randomize_subject_tx(
                        study_id=study_id,
                        subject_id=subject_id,
                        kit_reference=kit_reference,
                    )
                except (
                    sqlalchemy.exc.IntegrityError,
                    sqlalchemy.exc.OperationalError,
                    sqlalchemy.orm.exc.StaleDataError,
                ) as e:
                    logger.warning(
                        f"Concurrency conflict/integrity error on attempt {attempt + 1}: {e}. Retrying..."
                    )
                    if attempt == max_retries - 1:
                        raise
                    await asyncio.sleep(0.1 * (attempt + 1))
        finally:
            current_user_id.reset(user_token)
            current_change_reason.reset(reason_token)
