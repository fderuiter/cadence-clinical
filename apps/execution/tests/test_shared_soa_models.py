"""
Tests for shared SoA models module in core-models.
"""

import pytest

from apps.designer.src.domain.protocol_authoring import (
    ActivityAssignmentRequest,
    Epoch,
    EpochProperties,
    Procedure,
    ProcedureProperties,
    StudyArm,
    TimingWindow,
    TimingWindowProperties,
    Visit,
    VisitProperties,
    VisitReorderItem,
    VisitReorderRequest,
)


def test_study_arm_validation():
    # Regular initialization
    arm = StudyArm(
        id="arm_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="Active",
        arm_type="Active classification",
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert arm.id == "arm_1"
    assert arm.arm_type == "Active classification"
    assert arm.created_by == "tester"

    # Synonym population (type -> arm_type)
    arm_syn = StudyArm(
        id="arm_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="Active",
        type="Active classification",
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert arm_syn.arm_type == "Active classification"

    # Synonym population (arm_type -> type) on extra
    arm_syn2 = StudyArm(
        id="arm_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="Active",
        arm_type="Active classification",
        created_by="tester",
        reason_for_change="Created for testing",
    )
    # Since extra="allow" is set on the model, type should be present when dumped
    data = arm_syn2.model_dump()
    assert data["type"] == "Active classification"


def test_epoch_validation():
    # Regular epoch
    epoch = Epoch(
        id="ep_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="Screening",
        sequence_order=1,
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert epoch.name == "Screening"
    assert epoch.sequence_order == 1

    # Synonym (epoch_name -> name, sequence -> sequence_order)
    epoch_syn = Epoch(
        id="ep_1",
        study_id="study_123",
        study_version_id="sv_123",
        epoch_name="Treatment",
        sequence=2,
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert epoch_syn.name == "Treatment"
    assert epoch_syn.sequence_order == 2


def test_visit_validation():
    # Regular visit
    visit = Visit(
        id="v_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="Week 1",
        epoch_id="ep_1",
        sequence=1,
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert visit.name == "Week 1"

    # Synonym (encounter_name -> name)
    visit_syn = Visit(
        id="v_1",
        study_id="study_123",
        study_version_id="sv_123",
        encounter_name="Week 2",
        epoch_id="ep_1",
        sequence=2,
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert visit_syn.name == "Week 2"


def test_procedure_validation():
    # Regular procedure
    proc = Procedure(
        id="p_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="Lab draw",
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert proc.name == "Lab draw"

    # Synonym (activity_name -> name)
    proc_syn = Procedure(
        id="p_1",
        study_id="study_123",
        study_version_id="sv_123",
        activity_name="ECG",
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert proc_syn.name == "ECG"


def test_timing_window_validation():
    # Valid non-conditional
    tw = TimingWindow(
        id="tw_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="3 days",
        conditional=False,
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert tw.conditional is False

    # Valid conditional with reason
    tw_cond = TimingWindow(
        id="tw_1",
        study_id="study_123",
        study_version_id="sv_123",
        name="3 days",
        conditional=True,
        reason="Required for safety",
        created_by="tester",
        reason_for_change="Created for testing",
    )
    assert tw_cond.conditional is True
    assert tw_cond.reason == "Required for safety"

    # Invalid conditional with empty/whitespace reason
    with pytest.raises(ValueError, match="A non-empty 'reason' must be provided"):
        TimingWindow(
            id="tw_1",
            study_id="study_123",
            study_version_id="sv_123",
            name="3 days",
            conditional=True,
            reason="   ",
            created_by="tester",
            reason_for_change="Created for testing",
        )


def test_properties_payload_contracts():
    # EpochProperties
    props_epoch = EpochProperties(epoch_name="Screening", sequence=1)
    assert props_epoch.epoch_name == "Screening"

    with pytest.raises(
        ValueError, match="Either 'name' or 'epoch_name' must be provided"
    ):
        EpochProperties(sequence=1)

    # VisitProperties
    props_visit = VisitProperties(encounter_name="Week 1", sequence=1)
    assert props_visit.encounter_name == "Week 1"

    with pytest.raises(
        ValueError, match="Either 'name' or 'encounter_name' must be provided"
    ):
        VisitProperties(sequence=1)

    # ProcedureProperties
    props_proc = ProcedureProperties(activity_name="Fasting ECG")
    assert props_proc.activity_name == "Fasting ECG"

    with pytest.raises(
        ValueError, match="Either 'name' or 'activity_name' must be provided"
    ):
        ProcedureProperties()

    # TimingWindowProperties
    props_tw = TimingWindowProperties(name="TW", conditional=True, reason="Reason")
    assert props_tw.conditional is True

    with pytest.raises(ValueError, match="A non-empty 'reason' must be provided"):
        TimingWindowProperties(name="TW", conditional=True)


def test_visit_reorder_request():
    req = VisitReorderRequest(
        visits=[
            VisitReorderItem(visit_id="v1", sequence=1),
            VisitReorderItem(visit_id="v2", sequence=2),
        ]
    )
    assert len(req.visits) == 2
    assert req.visits[0].visit_id == "v1"
    assert req.visits[0].sequence == 1


def test_activity_assignment_request():
    # Using activity_ids
    req_act = ActivityAssignmentRequest(visit_id="v1", activity_ids=["p1", "p2"])
    assert req_act.visit_id == "v1"
    assert req_act.activity_ids == ["p1", "p2"]
    assert req_act.procedure_ids == ["p1", "p2"]

    # Using procedure_ids
    req_proc = ActivityAssignmentRequest(visit_id="v1", procedure_ids=["p1", "p2"])
    assert req_proc.visit_id == "v1"
    assert req_proc.activity_ids == ["p1", "p2"]
    assert req_proc.procedure_ids == ["p1", "p2"]

    # Neither provided
    with pytest.raises(
        ValueError,
        match="At least one of 'procedure_ids' or 'activity_ids' must be provided",
    ):
        ActivityAssignmentRequest(visit_id="v1")
