"""End-to-end integration test for Core Platform Engines (#4533).

Combines UREE expression evaluation, PEP 695 generic StateMachine lifecycle execution,
and Event-Driven SLA countdown clocks in a realistic clinical trial workflow.

@req:PRD-SUB-001, PRD-CRF-004, PRD-SYS-001, PRD-SYS-050
"""

from datetime import UTC, datetime

from packages.engine_core.fsm.engine import StateMachine
from packages.engine_core.fsm.models import (
    StateMachineDefinition,
    StateNode,
    TransitionContext,
    TransitionEdge,
)
from packages.engine_core.fsm.validator import validate_state_machine_graph
from packages.engine_core.rules.ast import (
    ExpressionNode,
    FieldReference,
)
from packages.engine_core.rules.evaluator import evaluate_expression
from packages.engine_core.sla.engine import SLAEngine
from packages.engine_core.sla.models import (
    EscalationAction,
    EscalationTier,
    SLAPolicy,
)


def test_end_to_end_clinical_subject_workflow():
    """Verify end-to-end clinical workflow: I/E rule evaluation, FSM progression, and SLA tracking.

    @req:PRD-SUB-001, PRD-CRF-004, PRD-SYS-001, PRD-SYS-050
    """
    # 1. Author Inclusion/Exclusion UREE rule: (age >= 18 AND consent == True AND sys_bp < 160)
    ie_expression = ExpressionNode(
        type="logical",
        operator="and",
        operands=[
            ExpressionNode(
                type="comparison",
                operator=">=",
                operands=[
                    ExpressionNode(
                        type="field_ref", field_ref=FieldReference(field_id="age")
                    ),
                    ExpressionNode(type="constant", value=18),
                ],
            ),
            ExpressionNode(
                type="comparison",
                operator="==",
                operands=[
                    ExpressionNode(
                        type="field_ref",
                        field_ref=FieldReference(field_id="consent_signed"),
                    ),
                    ExpressionNode(type="constant", value=True),
                ],
            ),
            ExpressionNode(
                type="comparison",
                operator="<",
                operands=[
                    ExpressionNode(
                        type="field_ref",
                        field_ref=FieldReference(field_id="systolic_bp"),
                    ),
                    ExpressionNode(type="constant", value=160),
                ],
            ),
        ],
    )

    # 2. Build and validate Subject Lifecycle StateMachine
    states = [
        StateNode(name="SCREENING", state_type="initial"),
        StateNode(name="SCREEN_FAILED", state_type="terminal"),
        StateNode(name="ENROLLED", state_type="normal"),
        StateNode(name="RANDOMIZED", state_type="normal"),
        StateNode(name="ACTIVE", state_type="normal"),
        StateNode(name="COMPLETED", state_type="terminal"),
        StateNode(name="WITHDRAWN", state_type="terminal"),
    ]

    transitions = [
        TransitionEdge(
            trigger="IE_FAIL",
            from_state="SCREENING",
            to_state="SCREEN_FAILED",
            allowed_roles=["site_crc", "site_investigator"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        TransitionEdge(
            trigger="IE_PASS",
            from_state="SCREENING",
            to_state="ENROLLED",
            guard=ExpressionNode(
                type="comparison",
                operator="==",
                operands=[
                    ExpressionNode(type="variable", variable_name="ie_eligible"),
                    ExpressionNode(type="constant", value=True),
                ],
            ),
            allowed_roles=["site_crc", "site_investigator"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        TransitionEdge(
            trigger="RANDOMIZE",
            from_state="ENROLLED",
            to_state="RANDOMIZED",
            allowed_roles=["site_investigator", "super_admin"],
            requires_signature=True,
            requires_reason_for_change=True,
            min_reason_length=10,
            action_hooks=["ALLOCATE_IP_KIT"],
        ),
        TransitionEdge(
            trigger="FIRST_DOSE",
            from_state="RANDOMIZED",
            to_state="ACTIVE",
            allowed_roles=["site_crc", "site_investigator"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        TransitionEdge(
            trigger="COMPLETE",
            from_state="ACTIVE",
            to_state="COMPLETED",
            allowed_roles=["site_investigator"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        TransitionEdge(
            trigger="WITHDRAW",
            from_state="ACTIVE",
            to_state="WITHDRAWN",
            allowed_roles=["site_crc", "site_investigator"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
    ]

    fsm_def = StateMachineDefinition(
        machine_id="FSM-CLINICAL-E2E",
        name="Clinical E2E Subject Lifecycle",
        states=states,
        transitions=transitions,
        initial_state="SCREENING",
        reason_for_change="Authoring complete subject lifecycle DAG",
    )

    validation_errors = validate_state_machine_graph(fsm_def)
    assert len(validation_errors) == 0

    fsm = StateMachine[str, str](fsm_def)

    # 3. Evaluate candidate subject clinical data via UREE
    subject_screening_data = {
        "age": 45,
        "consent_signed": True,
        "systolic_bp": 138,
    }
    is_eligible = evaluate_expression(ie_expression, subject_screening_data)
    assert is_eligible is True

    # 4. Advance state machine from SCREENING to ENROLLED
    ctx_enroll = TransitionContext(
        current_state="SCREENING",
        trigger="IE_PASS",
        user_id="crc.user@site101.org",
        user_roles=["site_crc"],
        reason_for_change="Subject verified against protocol I/E criteria",
        payload={"ie_eligible": is_eligible},
    )
    res_enroll = fsm.transition(ctx_enroll)
    assert res_enroll.success is True
    assert res_enroll.to_state == "ENROLLED"
    assert res_enroll.audit_envelope is not None
    assert res_enroll.audit_envelope.sha256_digest != ""

    # 5. Advance from ENROLLED to RANDOMIZED with PI electronic signature
    ctx_rand = TransitionContext(
        current_state="ENROLLED",
        trigger="RANDOMIZE",
        user_id="dr.smith@site101.org",
        user_roles=["site_investigator"],
        reason_for_change="Executing stratified block randomization",
        signature_token="sig_manifest_e2e_789xyz",
        payload={"treatment_arm": "ARM_INVESTIGATIONAL"},
    )
    res_rand = fsm.transition(ctx_rand)
    assert res_rand.success is True
    assert res_rand.to_state == "RANDOMIZED"
    assert res_rand.audit_envelope.signature_verified is True
    assert "ALLOCATE_IP_KIT" in res_rand.executed_action_hooks

    # 6. Advance to ACTIVE on first dose administration
    ctx_dose = TransitionContext(
        current_state="RANDOMIZED",
        trigger="FIRST_DOSE",
        user_id="crc.user@site101.org",
        user_roles=["site_crc"],
        reason_for_change="First dose of IP administered to subject at site",
    )
    res_dose = fsm.transition(ctx_dose)
    assert res_dose.success is True
    assert res_dose.to_state == "ACTIVE"

    # 7. Initialize and evaluate SLA escalation clock for site visit entry (24h SLA)
    sla_policy = SLAPolicy(
        policy_id="SLA-VISIT-ENTRY",
        name="Visit Data Entry SLA",
        base_hours_by_priority={"HIGH": 24.0},
        warning_threshold_pct=75.0,
        critical_threshold_pct=90.0,
        escalation_tiers=[
            EscalationTier(
                tier_id="TIER-CRA-ALERT",
                name="CRA Overdue Escalation",
                breach_threshold_pct=100.0,
                action=EscalationAction(
                    target_priority="CRITICAL",
                    notify_roles=["cra_monitor", "data_manager"],
                ),
            )
        ],
        reason_for_change="Establishing visit entry timeline policy",
    )
    sla_engine = SLAEngine(sla_policy)

    start_time = datetime(2026, 8, 28, 8, 0, 0, tzinfo=UTC)
    due_time = sla_engine.compute_sla_due_date(start_time, priority="HIGH")
    assert due_time == datetime(2026, 8, 29, 8, 0, 0, tzinfo=UTC)

    # Simulate evaluation at 20 hours elapsed (83.33% elapsed -> WARNING)
    check_time = datetime(2026, 8, 29, 4, 0, 0, tzinfo=UTC)
    clock_status = sla_engine.compute_countdown_clock(
        start_time, due_time, as_of=check_time
    )
    assert clock_status.alert_level == "WARNING"
    assert clock_status.remaining_hours == 4.0

    # Simulate evaluation at 26 hours elapsed (108.33% elapsed -> OVERDUE + Escalation)
    overdue_time = datetime(2026, 8, 29, 10, 0, 0, tzinfo=UTC)
    overdue_clock = sla_engine.compute_countdown_clock(
        start_time, due_time, as_of=overdue_time
    )
    assert overdue_clock.alert_level == "OVERDUE"
    assert overdue_clock.is_overdue is True

    escalation_res = sla_engine.evaluate_escalations(overdue_clock)
    assert len(escalation_res.triggered_tiers) == 1
    assert escalation_res.recommended_priority == "CRITICAL"
    assert "cra_monitor" in escalation_res.notification_roles
