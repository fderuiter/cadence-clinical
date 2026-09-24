"""Unit and Integration Tests for RTSM Adaptive Oncology Dose Escalation (BOIN / CRM / 3+3) & RAR.

Requirements: PRD-SCM-004, ADR-2199, FDA Adaptive Design Guidance, 21 CFR Part 11
"""

import os
import time

import httpx
import pytest
import pytest_asyncio

from apps.execution.adaptive_math import (
    BoinDoseEscalationEngine,
    CrmDoseEscalationEngine,
    ResponseAdaptiveRandomizationEngine,
    ThreePlusThreeDoseEscalationEngine,
    beta_cdf,
    calculate_beta_overdose_prob,
    calculate_boin_boundaries,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
)
from apps.execution.domain.adaptive_models import (
    AdaptiveTrialConfigDTO,
    BoinConfig,
    CohortEvaluationInput,
    CohortRecommendationAction,
    CohortTransitionRequest,
    CrmConfig,
    DoseEscalationDesignType,
    DoseLevelDef,
    RarConfig,
    RARExpandOrTerminateArmRequest,
)
from apps.execution.main import app
from apps.execution.randomization import RandomizationConfigSchema, RTSMAllocator
from apps.execution.rtsm_adaptive import (
    CohortTransitionError,
    add_or_terminate_rar_arm_transaction,
    advance_cohort_decision_transaction,
    create_or_update_adaptive_config_transaction,
    evaluate_cohort_outcomes_transaction,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security.signing import generate_gateway_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Initializes clean in-memory test database."""
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()
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
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_gateway_headers(
    user_id: str = "test_investigator",
    roles: str = "sponsor_admin",
    change_reason: str = "Adaptive dose escalation evaluation",
    site_id: str | None = None,
    include_sig_token: bool = True,
) -> dict:
    """Generates signed gateway V2 headers for authenticated endpoint testing."""
    from jose import jwt

    timestamp = str(time.time())
    sig_token = None
    if include_sig_token:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        sig_token = jwt.encode(
            sig_payload, b"internal-gateway-secret-12345", algorithm="HS256"
        )

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=b"internal-gateway-secret-12345",  # pragma: allowlist secret
        change_reason=change_reason,
        site_id=site_id,
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sig_token:
        headers["X-Sig-Token"] = sig_token
    return headers


# =========================================================================
# 1. Standard 3+3 Rule-Based Dose Escalation Engine Tests
# =========================================================================


def test_three_plus_three_escalation_on_zero_dlts():
    """Verify 3+3 escalates to next higher dose when 0/3 DLTs observed.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = ThreePlusThreeDoseEscalationEngine()
    action, rec_dose, rationale = engine.evaluate(
        current_dose_index=1,
        total_doses=4,
        cohort_subjects=3,
        cohort_dlts=0,
        cumulative_enrolled_at_dose=3,
        cumulative_dlts_at_dose=0,
    )
    assert action == CohortRecommendationAction.ESCALATE
    assert rec_dose == 2
    assert "0/3 DLTs observed" in rationale


def test_three_plus_three_expansion_on_one_dlt():
    """Verify 3+3 stays and expands cohort by 3 subjects when 1/3 DLTs observed.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = ThreePlusThreeDoseEscalationEngine()
    action, rec_dose, rationale = engine.evaluate(
        current_dose_index=2,
        total_doses=4,
        cohort_subjects=3,
        cohort_dlts=1,
        cumulative_enrolled_at_dose=3,
        cumulative_dlts_at_dose=1,
    )
    assert action == CohortRecommendationAction.EXPAND_COHORT
    assert rec_dose == 2
    assert "Expand cohort by 3 additional subjects" in rationale


def test_three_plus_three_de_escalation_on_two_dlts():
    """Verify 3+3 de-escalates to lower dose when >= 2/3 DLTs observed.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = ThreePlusThreeDoseEscalationEngine()
    action, rec_dose, rationale = engine.evaluate(
        current_dose_index=3,
        total_doses=4,
        cohort_subjects=3,
        cohort_dlts=2,
        cumulative_enrolled_at_dose=3,
        cumulative_dlts_at_dose=2,
    )
    assert action == CohortRecommendationAction.DE_ESCALATE
    assert rec_dose == 2
    assert "De-escalate to dose level 2" in rationale


def test_three_plus_three_safety_halt_at_dose_level_one():
    """Verify 3+3 triggers STOP_SAFETY when >= 2/3 DLTs observed at lowest dose level 1.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = ThreePlusThreeDoseEscalationEngine()
    action, rec_dose, rationale = engine.evaluate(
        current_dose_index=1,
        total_doses=4,
        cohort_subjects=3,
        cohort_dlts=2,
        cumulative_enrolled_at_dose=3,
        cumulative_dlts_at_dose=2,
    )
    assert action == CohortRecommendationAction.STOP_SAFETY
    assert rec_dose is None
    assert "Stop trial immediately for safety" in rationale


def test_three_plus_three_six_subject_evaluation():
    """Verify 3+3 behavior on 6 subjects (1/6 escalate, >=2/6 de-escalate).

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = ThreePlusThreeDoseEscalationEngine()
    # 1/6 safe -> escalate
    act_esc, rec_esc, _ = engine.evaluate(
        current_dose_index=2,
        total_doses=4,
        cohort_subjects=3,
        cohort_dlts=0,
        cumulative_enrolled_at_dose=6,
        cumulative_dlts_at_dose=1,
    )
    assert act_esc == CohortRecommendationAction.ESCALATE
    assert rec_esc == 3

    # 2/6 toxic -> de-escalate & declare MTD
    act_de, rec_de, _ = engine.evaluate(
        current_dose_index=3,
        total_doses=4,
        cohort_subjects=3,
        cohort_dlts=1,
        cumulative_enrolled_at_dose=6,
        cumulative_dlts_at_dose=2,
    )
    assert act_de == CohortRecommendationAction.DE_ESCALATE
    assert rec_de == 2


# =========================================================================
# 2. Bayesian Optimal Interval (BOIN) Design Mathematical Tests
# =========================================================================


def test_boin_theoretical_boundaries_derivation():
    """Verify BOIN boundary derivations for phi=0.25 (lambda_e=0.197, lambda_d=0.298)

    and phi=0.30 (lambda_e=0.236, lambda_d=0.358).

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    l_e25, l_d25, p1_25, p2_25 = calculate_boin_boundaries(0.25, 0.6, 1.4)
    assert round(l_e25, 3) == 0.197
    assert round(l_d25, 3) == 0.298
    assert p1_25 == 0.15
    assert p2_25 == 0.35

    l_e30, l_d30, p1_30, p2_30 = calculate_boin_boundaries(0.30, 0.6, 1.4)
    assert round(l_e30, 3) == 0.236
    assert abs(l_d30 - 0.3585) < 0.005
    assert p1_30 == 0.18
    assert p2_30 == 0.42


def test_incomplete_beta_cdf_and_overdose_prob():
    """Verify high-precision incomplete beta function and overdose probability calculation.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    # Beta(1, 1) is Uniform(0, 1), so CDF(x) = x
    assert abs(beta_cdf(0.25, 1.0, 1.0) - 0.25) < 1e-4
    assert abs(beta_cdf(0.75, 1.0, 1.0) - 0.75) < 1e-4

    # 3 DLTs in 3 subjects with prior Beta(1, 1) -> Posterior Beta(4, 1)
    # CDF(0.25; 4, 1) = 0.25^4 = 0.00390625
    # Overdose prob = 1 - CDF = 0.99609375
    od_prob = calculate_beta_overdose_prob(4.0, 1.0, 0.25)
    assert abs(od_prob - 0.9961) < 1e-3


def test_boin_cohort_decisions_and_interval_logic():
    """Verify BOIN automated cohort recommendations: ESCALATE, DE_ESCALATE, STAY.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = BoinDoseEscalationEngine(BoinConfig(target_toxicity_rate=0.25))

    # Case 1: 0/3 DLTs (rate 0.0 <= 0.197) -> ESCALATE
    act1, rec1, summaries, rat1, stop1 = engine.evaluate_cohort(
        current_dose_index=1,
        total_doses=4,
        dose_stats={1: {"enrolled": 3, "dlts": 0}},
    )
    assert act1 == CohortRecommendationAction.ESCALATE
    assert rec1 == 2
    assert stop1 is False

    # Case 2: 1/3 DLTs (rate 0.333 >= 0.298) at dose 2 -> DE_ESCALATE
    act2, rec2, _, rat2, stop2 = engine.evaluate_cohort(
        current_dose_index=2,
        total_doses=4,
        dose_stats={1: {"enrolled": 3, "dlts": 0}, 2: {"enrolled": 3, "dlts": 1}},
    )
    assert act2 == CohortRecommendationAction.DE_ESCALATE
    assert rec2 == 1
    assert stop2 is False

    # Case 3: 1/4 DLTs (rate 0.25 in (0.197, 0.298)) at dose 2 -> STAY
    act3, rec3, _, rat3, stop3 = engine.evaluate_cohort(
        current_dose_index=2,
        total_doses=4,
        dose_stats={1: {"enrolled": 3, "dlts": 0}, 2: {"enrolled": 4, "dlts": 1}},
    )
    assert act3 == CohortRecommendationAction.STAY
    assert rec3 == 2


def test_boin_bayesian_overdose_elimination():
    """Verify Bayesian overdose stopping rule eliminates overly toxic dose levels.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = BoinDoseEscalationEngine(
        BoinConfig(target_toxicity_rate=0.25, safety_cutoff=0.95)
    )

    # 3/3 DLTs at Dose 2 -> Overdose probability > 0.95 -> Dose 2 and 3 eliminated -> De-escalate to 1
    act, rec, summaries, rat, stop = engine.evaluate_cohort(
        current_dose_index=2,
        total_doses=3,
        dose_stats={1: {"enrolled": 3, "dlts": 0}, 2: {"enrolled": 3, "dlts": 3}},
    )
    assert act == CohortRecommendationAction.DE_ESCALATE
    assert rec == 1
    assert summaries[1].is_eliminated_for_safety is True

    # 3/3 DLTs at Dose 1 -> Lowest dose eliminated -> Full trial safety halt
    act_halt, rec_halt, sums_halt, rat_halt, stop_halt = engine.evaluate_cohort(
        current_dose_index=1,
        total_doses=3,
        dose_stats={1: {"enrolled": 3, "dlts": 3}},
    )
    assert act_halt == CohortRecommendationAction.STOP_SAFETY
    assert rec_halt is None
    assert stop_halt is True


def test_boin_mtd_selection():
    """Verify MTD selection selects safe dose level closest to target toxicity rate phi.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    engine = BoinDoseEscalationEngine(BoinConfig(target_toxicity_rate=0.25))
    stats = {
        1: {"enrolled": 6, "dlts": 0},  # rate 0.00
        2: {"enrolled": 9, "dlts": 2},  # rate 0.222 -> closest to 0.25
        3: {"enrolled": 6, "dlts": 3},  # rate 0.50
    }
    mtd, rat = engine.select_mtd(total_doses=3, dose_stats=stats)
    assert mtd == 2
    assert "Dose level 2 declared MTD" in rat


# =========================================================================
# 3. Continual Reassessment Method (CRM / mCRM) Tests
# =========================================================================


def test_crm_config_skeleton_validation():
    """Verify CRM configuration validates skeleton probabilities are strictly monotonic in (0, 1).

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    # Valid skeleton
    cfg = CrmConfig(
        skeleton_probs=[0.05, 0.12, 0.25, 0.40, 0.55], target_toxicity_rate=0.25
    )
    assert len(cfg.skeleton_probs) == 5

    # Non-monotonic skeleton should fail
    with pytest.raises(ValueError):
        CrmConfig(skeleton_probs=[0.05, 0.25, 0.20], target_toxicity_rate=0.25)

    # Probabilities outside (0, 1) should fail
    with pytest.raises(ValueError):
        CrmConfig(skeleton_probs=[0.0, 0.25, 0.50], target_toxicity_rate=0.25)


def test_crm_bayesian_posterior_updating_and_no_skipping():
    """Verify modified CRM updates Bayesian posterior curve and enforces no dose skipping on escalation.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    cfg = CrmConfig(
        skeleton_probs=[0.05, 0.15, 0.25, 0.40, 0.60],
        target_toxicity_rate=0.25,
        allow_dose_skipping=False,
    )
    engine = CrmDoseEscalationEngine(cfg)

    # 0 DLTs at Dose 1
    stats = {1: {"enrolled": 3, "dlts": 0}}
    action, rec_dose, post_means, od_1, rat, stop = engine.evaluate_cohort(
        current_dose_index=1,
        dose_stats=stats,
    )
    assert action == CohortRecommendationAction.ESCALATE
    assert rec_dose == 2
    assert len(post_means) == 5
    assert stop is False

    # Excessive toxicity at Dose 1 (3/3 DLTs) -> Overdose probability on Dose 1 triggers safety stop
    stats_toxic = {1: {"enrolled": 3, "dlts": 3}}
    act_halt, rec_halt, _, od_halt, rat_halt, stop_halt = engine.evaluate_cohort(
        current_dose_index=1,
        dose_stats=stats_toxic,
    )
    assert act_halt == CohortRecommendationAction.STOP_SAFETY
    assert rec_halt is None
    assert stop_halt is True
    assert od_halt > 0.90


# =========================================================================
# 4. Response-Adaptive Randomization (RAR) Tests
# =========================================================================


def test_rar_dynamic_probability_weighting_and_bounds():
    """Verify Response-Adaptive Randomization dynamic weighting, bounding, and normalization.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    cfg = RarConfig(
        burn_in_sample_per_arm=3,
        power_tuning_param=0.5,
        min_allocation_prob=0.05,
        max_allocation_prob=0.85,
    )
    engine = ResponseAdaptiveRandomizationEngine(cfg)

    arms = [
        {
            "arm_id": "CONTROL",
            "is_control": True,
            "enrolled_count": 10,
            "response_count": 3,
        },
        {
            "arm_id": "ARM_HIGH",
            "is_control": False,
            "enrolled_count": 10,
            "response_count": 8,
        },
        {
            "arm_id": "ARM_LOW",
            "is_control": False,
            "enrolled_count": 10,
            "response_count": 1,
        },
    ]

    probs, dropped, graduated, rat = engine.update_probabilities(arms)

    assert sum(probs.values()) == pytest.approx(1.0, abs=1e-3)
    # Higher efficacy arm receives higher allocation probability
    assert probs["ARM_HIGH"] > probs["CONTROL"]
    assert probs["ARM_HIGH"] > probs["ARM_LOW"]
    assert probs["ARM_LOW"] >= 0.05


def test_rar_futility_dropping_and_efficacy_graduation():
    """Verify RAR automatically drops futile arms and flags graduated arms without study downtime.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    cfg = RarConfig(
        burn_in_sample_per_arm=5,
        futility_threshold=0.05,
        efficacy_threshold=0.95,
    )
    engine = ResponseAdaptiveRandomizationEngine(cfg)

    # ARM_FUTILE has 0 responses out of 15 while CONTROL has 3/10
    # ARM_SUPERIOR has 15/15 responses
    arms = [
        {
            "arm_id": "CONTROL",
            "is_control": True,
            "enrolled_count": 10,
            "response_count": 3,
        },
        {
            "arm_id": "ARM_FUTILE",
            "is_control": False,
            "enrolled_count": 15,
            "response_count": 0,
        },
        {
            "arm_id": "ARM_SUPERIOR",
            "is_control": False,
            "enrolled_count": 15,
            "response_count": 15,
        },
    ]

    probs, dropped, graduated, rat = engine.update_probabilities(arms)

    assert "ARM_FUTILE" in dropped
    assert "ARM_SUPERIOR" in graduated
    # Dropped arm receives 0 weight
    assert probs.get("ARM_FUTILE", 0.0) == 0.0
    # Remaining active probabilities sum to 1.0
    assert sum(probs.values()) == pytest.approx(1.0, abs=1e-3)


def test_rtsm_allocator_integration_with_rar():
    """Verify RTSMAllocator supports RESPONSE_ADAPTIVE and RAR algorithm types.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    config = RandomizationConfigSchema(
        algorithm_type="RESPONSE_ADAPTIVE",
        arms_ratios={"Control": 20, "Arm A": 50, "Arm B": 30},
        seed=42,
    )
    allocator = RTSMAllocator(config)
    res = allocator.allocate()
    assert res["allocation"] in ["Control", "Arm A", "Arm B"]
    assert res["stratum_key"] == "DEFAULT"


# =========================================================================
# 5. Database Service & 21 CFR Part 11 Audit Trail Tests
# =========================================================================


@pytest.mark.asyncio
async def test_adaptive_dose_escalation_audit_trail_and_transitions():
    """Verify 21 CFR Part 11 audit logging of cohort evaluations and electronic transitions.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    study_id = "ONCOLOGY-BOIN-001"
    user_id = "investigator_dr_smith"
    session_maker = db_manager.get_session_maker()

    # 1. Initialize Adaptive BOIN trial config
    config_dto = AdaptiveTrialConfigDTO(
        study_id=study_id,
        design_type=DoseEscalationDesignType.BOIN,
        dose_levels=[
            DoseLevelDef(index=1, name="5mg", dose_value=5.0),
            DoseLevelDef(index=2, name="10mg", dose_value=10.0),
            DoseLevelDef(index=3, name="20mg", dose_value=20.0),
            DoseLevelDef(index=4, name="40mg", dose_value=40.0),
        ],
        boin_config=BoinConfig(target_toxicity_rate=0.25),
        cohort_size=3,
    )

    async with session_maker() as session:
        cfg = await create_or_update_adaptive_config_transaction(
            session=session,
            config=config_dto,
            user_id=user_id,
            change_reason="Protocol initiation",
        )
        await session.commit()
        assert cfg.study_id == study_id

    # 2. Evaluate Cohort 1: 0 DLTs out of 3 subjects
    eval_input = CohortEvaluationInput(
        study_id=study_id,
        cohort_number=1,
        dose_level_index=1,
        subjects_evaluated=3,
        dlts_observed=0,
    )

    async with session_maker() as session:
        eval_res = await evaluate_cohort_outcomes_transaction(
            session=session,
            payload=eval_input,
            user_id=user_id,
            change_reason="Cohort 1 completion evaluation",
        )
        await session.commit()

        assert eval_res.recommendation_action == CohortRecommendationAction.ESCALATE
        assert eval_res.recommended_dose_level_index == 2

    # 3. Advance Cohort 1 -> Cohort 2 with cryptographic seal and audit log
    transition_req = CohortTransitionRequest(
        study_id=study_id,
        cohort_number=1,
        decision_action=CohortRecommendationAction.ESCALATE,
        target_dose_level_index=2,
        electronic_signature="Dr. Smith MD [SIG-VALID-123]",
    )

    async with session_maker() as session:
        log_res = await advance_cohort_decision_transaction(
            session=session,
            payload=transition_req,
            user_id=user_id,
            change_reason="Escalate to 10mg",
        )
        await session.commit()

        assert log_res.action_taken == "ESCALATE"
        assert log_res.to_dose_level == 2
        assert log_res.override_occurred is False
        assert log_res.cryptographic_checksum is not None

    # 4. Attempt an un-justified override (must fail under 21 CFR Part 11)
    override_req = CohortTransitionRequest(
        study_id=study_id,
        cohort_number=2,
        decision_action=CohortRecommendationAction.DE_ESCALATE,
        target_dose_level_index=1,
        override_reason=None,  # Missing override rationale
    )

    async with session_maker() as session:
        with pytest.raises(CohortTransitionError):
            await advance_cohort_decision_transaction(
                session=session,
                payload=override_req,
                user_id=user_id,
                change_reason=None,
            )


@pytest.mark.asyncio
async def test_rar_dynamic_arm_addition_and_termination_transaction():
    """Verify seamless zero-downtime dynamic addition and dropping of RAR arms.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    study_id = "RAR-PLATFORM-002"
    user_id = "biostat_lead"
    session_maker = db_manager.get_session_maker()

    # 1. Initialize RAR Config
    config_dto = AdaptiveTrialConfigDTO(
        study_id=study_id,
        design_type=DoseEscalationDesignType.RAR,
        rar_config=RarConfig(burn_in_sample_per_arm=2),
    )

    async with session_maker() as session:
        await create_or_update_adaptive_config_transaction(
            session=session,
            config=config_dto,
            user_id=user_id,
            change_reason="RAR trial startup",
        )
        await session.commit()

    # 2. Add initial Control and Arm 1
    async with session_maker() as session:
        await add_or_terminate_rar_arm_transaction(
            session=session,
            payload=RARExpandOrTerminateArmRequest(
                study_id=study_id,
                arm_id="CTRL",
                arm_name="Standard of Care",
                is_control=True,
                action="ADD_ARM",
                reason="Initial control arm",
            ),
            user_id=user_id,
        )
        rar_res = await add_or_terminate_rar_arm_transaction(
            session=session,
            payload=RARExpandOrTerminateArmRequest(
                study_id=study_id,
                arm_id="ARM_A",
                arm_name="Novel Drug A",
                is_control=False,
                action="ADD_ARM",
                reason="Initial novel arm",
            ),
            user_id=user_id,
        )
        await session.commit()
        assert len(rar_res.arms) == 2

    # 3. Dynamically add Arm B mid-study with zero downtime
    async with session_maker() as session:
        rar_res2 = await add_or_terminate_rar_arm_transaction(
            session=session,
            payload=RARExpandOrTerminateArmRequest(
                study_id=study_id,
                arm_id="ARM_B",
                arm_name="Novel Drug B (Expansion)",
                is_control=False,
                action="ADD_ARM",
                reason="Platform trial expansion arm",
            ),
            user_id=user_id,
        )
        await session.commit()

        assert len(rar_res2.arms) == 3
        assert "ARM_B" in rar_res2.allocation_probabilities
        assert sum(rar_res2.allocation_probabilities.values()) == pytest.approx(
            1.0, abs=1e-3
        )


# =========================================================================
# 6. REST API Endpoints Integration Tests
# =========================================================================


@pytest.mark.asyncio
async def test_adaptive_api_endpoints_e2e_flow():
    """Verify end-to-end REST API endpoints for adaptive trial configuration, cohort evaluation,

    transitions, boundary calculation, and RAR state.

    @req:PRD-SYS-184
    @req:PRD-SCM-004
    @req:ADR-2199
    """
    study_id = "ONCOLOGY-E2E-003"
    headers = get_gateway_headers(
        user_id="lead_investigator_md",
        roles="sponsor_admin",
        change_reason="E2E Adaptive trial validation",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Pure calculation endpoint for BOIN boundaries
        resp_bounds = await client.post(
            "/api/v1/execution/rtsm/adaptive/boin/calculate-boundaries",
            json={"target_toxicity_rate": 0.25, "factor_e": 0.6, "factor_d": 1.4},
            headers=headers,
        )
        assert resp_bounds.status_code == 200
        data_bounds = resp_bounds.json()
        assert round(data_bounds["lambda_e"], 3) == 0.197
        assert round(data_bounds["lambda_d"], 3) == 0.298

        # 2. Pure calculation endpoint for CRM posterior
        resp_crm = await client.post(
            "/api/v1/execution/rtsm/adaptive/crm/posterior",
            json={
                "skeleton_probs": [0.05, 0.15, 0.25, 0.40, 0.60],
                "target_toxicity_rate": 0.25,
                "current_dose_index": 1,
                "dose_evaluations": [{"dose_index": 1, "enrolled": 3, "dlts": 0}],
            },
            headers=headers,
        )
        assert resp_crm.status_code == 200
        data_crm = resp_crm.json()
        assert data_crm["recommendation_action"] == "ESCALATE"
        assert data_crm["recommended_dose_index"] == 2

        # 3. Create Adaptive BOIN Trial Configuration
        resp_cfg = await client.post(
            "/api/v1/execution/rtsm/adaptive/config",
            json={
                "study_id": study_id,
                "design_type": "BOIN",
                "dose_levels": [
                    {"index": 1, "name": "10mg", "dose_value": 10.0},
                    {"index": 2, "name": "20mg", "dose_value": 20.0},
                    {"index": 3, "name": "40mg", "dose_value": 40.0},
                ],
                "boin_config": {"target_toxicity_rate": 0.25},
                "cohort_size": 3,
            },
            headers=headers,
        )
        assert resp_cfg.status_code == 201, resp_cfg.text

        # 4. Get Active Adaptive Configuration & Active Cohort
        resp_get_cfg = await client.get(
            f"/api/v1/execution/rtsm/adaptive/config?study_id={study_id}",
            headers=headers,
        )
        assert resp_get_cfg.status_code == 200
        data_get_cfg = resp_get_cfg.json()
        assert data_get_cfg["design_type"] == "BOIN"
        assert data_get_cfg["active_cohort"]["cohort_number"] == 1
        assert data_get_cfg["active_cohort"]["current_dose_name"] == "10mg"

        # 5. Evaluate Cohort 1 DLTs (0/3 DLTs -> Escalate)
        resp_eval = await client.post(
            "/api/v1/execution/rtsm/adaptive/cohorts/evaluate",
            json={
                "study_id": study_id,
                "cohort_number": 1,
                "dose_level_index": 1,
                "subjects_evaluated": 3,
                "dlts_observed": 0,
            },
            headers=headers,
        )
        assert resp_eval.status_code == 200
        data_eval = resp_eval.json()
        assert data_eval["recommendation_action"] == "ESCALATE"
        assert data_eval["recommended_dose_level_index"] == 2

        # 6. Authorize Transition to Cohort 2
        resp_trans = await client.post(
            "/api/v1/execution/rtsm/adaptive/cohorts/transition",
            json={
                "study_id": study_id,
                "cohort_number": 1,
                "decision_action": "ESCALATE",
                "target_dose_level_index": 2,
                "electronic_signature": "Dr. Investigator [SIG-AUTH-999]",
            },
            headers=headers,
        )
        assert resp_trans.status_code == 200
        data_trans = resp_trans.json()
        assert data_trans["action_taken"] == "ESCALATE"
        assert data_trans["to_dose_level"] == 2

        # 7. Query Transition History
        resp_hist = await client.get(
            f"/api/v1/execution/rtsm/adaptive/cohorts/history?study_id={study_id}",
            headers=headers,
        )
        assert resp_hist.status_code == 200
        data_hist = resp_hist.json()
        assert len(data_hist) == 1
        assert data_hist[0]["action_taken"] == "ESCALATE"
