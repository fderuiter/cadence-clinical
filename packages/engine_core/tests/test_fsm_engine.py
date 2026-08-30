"""Unit tests for the generic Finite State Machine (FSM) Lifecycle Engine.

@req:PRD-SUB-001, PRD-CRF-010, PRD-SYS-001
"""

from packages.engine_core.fsm.engine import (
    StateMachine,
)
from packages.engine_core.fsm.models import (
    StateMachineDefinition,
    StateNode,
    TransitionContext,
    TransitionEdge,
)
from packages.engine_core.rules.ast import ExpressionNode, FieldReference


def _build_subject_lifecycle_definition() -> StateMachineDefinition:
    """Helper creating standard clinical subject lifecycle machine definition."""
    states = [
        StateNode(name="SCREENING", state_type="initial", label="Subject in Screening"),
        StateNode(name="SCREEN_FAILED", state_type="terminal", label="Screen Failed"),
        StateNode(name="ENROLLED", state_type="normal", label="Enrolled / Consented"),
        StateNode(name="RANDOMIZED", state_type="normal", label="Randomized"),
        StateNode(name="ACTIVE", state_type="normal", label="Active on Study"),
        StateNode(name="COMPLETED", state_type="terminal", label="Study Completed"),
        StateNode(name="WITHDRAWN", state_type="terminal", label="Consent Withdrawn"),
    ]

    transitions = [
        # Screening -> Screen Failed
        TransitionEdge(
            trigger="IE_CRITERIA_FAIL",
            from_state="SCREENING",
            to_state="SCREEN_FAILED",
            allowed_roles=["site_crc", "site_investigator", "super_admin"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        # Screening -> Enrolled (requires guard: ie_eligible == True)
        TransitionEdge(
            trigger="IE_CRITERIA_PASS",
            from_state="SCREENING",
            to_state="ENROLLED",
            guard=ExpressionNode(
                type="comparison",
                operator="==",
                operands=[
                    ExpressionNode(
                        type="field_ref",
                        field_ref=FieldReference(field_id="ie_eligible"),
                    ),
                    ExpressionNode(type="constant", value=True),
                ],
            ),
            allowed_roles=["site_crc", "site_investigator", "super_admin"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        # Enrolled -> Randomized (requires signature)
        TransitionEdge(
            trigger="EXECUTE_RANDOMIZATION",
            from_state="ENROLLED",
            to_state="RANDOMIZED",
            allowed_roles=["site_investigator", "super_admin"],
            requires_signature=True,
            requires_reason_for_change=True,
            min_reason_length=10,
            action_hooks=["ALLOCATE_TREATMENT_KIT", "NOTIFY_UNBLINDED_PHARMACIST"],
        ),
        # Randomized -> Active (First dose)
        TransitionEdge(
            trigger="ADMINISTER_FIRST_DOSE",
            from_state="RANDOMIZED",
            to_state="ACTIVE",
            allowed_roles=["site_crc", "site_investigator", "super_admin"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        # Active -> Completed
        TransitionEdge(
            trigger="COMPLETE_PROTOCOL",
            from_state="ACTIVE",
            to_state="COMPLETED",
            allowed_roles=["site_investigator", "super_admin"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        # Active / Randomized -> Withdrawn
        TransitionEdge(
            trigger="WITHDRAW_CONSENT",
            from_state="ACTIVE",
            to_state="WITHDRAWN",
            allowed_roles=["site_crc", "site_investigator", "super_admin"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
        TransitionEdge(
            trigger="WITHDRAW_CONSENT",
            from_state="RANDOMIZED",
            to_state="WITHDRAWN",
            allowed_roles=["site_crc", "site_investigator", "super_admin"],
            requires_reason_for_change=True,
            min_reason_length=10,
        ),
    ]

    return StateMachineDefinition(
        machine_id="FSM-SUBJECT-LIFECYCLE",
        name="Standard Subject Lifecycle State Machine",
        version_index=1,
        states=states,
        transitions=transitions,
        initial_state="SCREENING",
        reason_for_change="Initial configuration of subject lifecycle DAG",
    )


def test_state_machine_initialization_and_properties():
    """Verify state machine properties and state queries.

    @req:PRD-SUB-001
    """
    definition = _build_subject_lifecycle_definition()
    fsm = StateMachine[str, str](definition)

    assert fsm.machine_id == "FSM-SUBJECT-LIFECYCLE"
    assert fsm.version_index == 1
    assert fsm.initial_state == "SCREENING"
    assert fsm.is_valid_state("ENROLLED") is True
    assert fsm.is_valid_state("INVALID_STATE") is False
    assert fsm.is_terminal_state("COMPLETED") is True
    assert fsm.is_terminal_state("SCREENING") is False


def test_state_machine_guard_condition_success_and_failure():
    """Verify guard expression evaluation blocks or allows transitions.

    @req:PRD-SUB-001, PRD-CRF-004
    """
    definition = _build_subject_lifecycle_definition()
    fsm = StateMachine[str, str](definition)

    # Attempt transition when guard evaluates to False
    ctx_fail = TransitionContext(
        current_state="SCREENING",
        trigger="IE_CRITERIA_PASS",
        user_roles=["site_crc"],
        reason_for_change="Subject passed initial pre-screening checks",
        payload={"ie_eligible": False},
    )
    result_fail = fsm.transition(ctx_fail)
    assert result_fail.success is False
    assert result_fail.to_state == "SCREENING"
    assert "Guard condition failed" in (result_fail.error_message or "")

    # Attempt transition when guard evaluates to True
    ctx_pass = TransitionContext(
        current_state="SCREENING",
        trigger="IE_CRITERIA_PASS",
        user_roles=["site_crc"],
        reason_for_change="Subject satisfied all inclusion/exclusion criteria",
        payload={"ie_eligible": True},
    )
    result_pass = fsm.transition(ctx_pass)
    assert result_pass.success is True
    assert result_pass.from_state == "SCREENING"
    assert result_pass.to_state == "ENROLLED"
    assert result_pass.audit_envelope is not None
    assert result_pass.audit_envelope.sha256_digest != ""


def test_state_machine_role_authorization_gating():
    """Verify role-based authorization gates unauthorized persona transitions.

    @req:PRD-SUB-001, PRD-SYS-001
    """
    definition = _build_subject_lifecycle_definition()
    fsm = StateMachine[str, str](definition)

    # Auditor role cannot execute randomization
    ctx_unauthorized = TransitionContext(
        current_state="ENROLLED",
        trigger="EXECUTE_RANDOMIZATION",
        user_roles=["auditor"],
        reason_for_change="Randomization request by auditor",
        signature_token="sig_token_123",
    )
    res = fsm.transition(ctx_unauthorized)
    assert res.success is False
    assert "not authorized" in (res.error_message or "")


def test_state_machine_electronic_signature_requirement():
    """Verify electronic signature requirement blocks transitions without signature token.

    @req:PRD-SUB-001, PRD-SYS-001
    """
    definition = _build_subject_lifecycle_definition()
    fsm = StateMachine[str, str](definition)

    # Missing signature token on Randomization
    ctx_no_sig = TransitionContext(
        current_state="ENROLLED",
        trigger="EXECUTE_RANDOMIZATION",
        user_roles=["site_investigator"],
        reason_for_change="Executing block randomization for enrolled subject",
        signature_token=None,
    )
    res = fsm.transition(ctx_no_sig)
    assert res.success is False
    assert "requires a valid 21 CFR Part 11 electronic signature" in (
        res.error_message or ""
    )

    # With signature token
    ctx_sig = TransitionContext(
        current_state="ENROLLED",
        trigger="EXECUTE_RANDOMIZATION",
        user_roles=["site_investigator"],
        reason_for_change="Executing block randomization for enrolled subject",
        signature_token="sig_manifest_abc123",
    )
    res_success = fsm.transition(ctx_sig)
    assert res_success.success is True
    assert res_success.to_state == "RANDOMIZED"
    assert res_success.audit_envelope.signature_verified is True
    assert res_success.executed_action_hooks == [
        "ALLOCATE_TREATMENT_KIT",
        "NOTIFY_UNBLINDED_PHARMACIST",
    ]


def test_state_machine_reason_for_change_length_enforcement():
    """Verify Part 11 reason-for-change length enforcement.

    @req:PRD-CRF-010, PRD-SYS-001
    """
    definition = _build_subject_lifecycle_definition()
    fsm = StateMachine[str, str](definition)

    # Short reason for change (< 10 chars)
    ctx_short = TransitionContext(
        current_state="SCREENING",
        trigger="IE_CRITERIA_FAIL",
        user_roles=["site_crc"],
        reason_for_change="Failed",  # only 6 chars
    )
    res = fsm.transition(ctx_short)
    assert res.success is False
    assert "minimum 10 characters" in (res.error_message or "")
