"""Unit tests for the Schedule of Activities (SoA) Vector Timeline Engine.

@req:PRD-SYS-001, PRD-CRF-004
"""

from packages.engine_core.timeline.engine import VectorTimelineEngine
from packages.engine_core.timeline.models import (
    CycleTemplateDefinition,
    EncounterScheduleItem,
    TimelineScheduleDefinition,
    VisitWindowEnvelope,
)


def _build_test_timeline_definition() -> TimelineScheduleDefinition:
    """Helper building a realistic oncology/phase III protocol timeline definition."""
    static_encounters = [
        EncounterScheduleItem(
            encounter_id="SCR",
            name="Screening Visit",
            anchor_event="SCREENING",
            target_day_offset=-14,
            window=VisitWindowEnvelope(window_before_days=14, window_after_days=0),
            is_mandatory=True,
        ),
        EncounterScheduleItem(
            encounter_id="RAND",
            name="Randomization / Baseline",
            anchor_event="RANDOMIZATION",
            target_day_offset=0,
            window=VisitWindowEnvelope(window_before_days=0, window_after_days=3),
            is_mandatory=True,
        ),
        EncounterScheduleItem(
            encounter_id="D1",
            name="Day 1 (First Dose)",
            anchor_event="FIRST_DOSE",
            target_day_offset=0,
            window=VisitWindowEnvelope(window_before_days=0, window_after_days=1),
            is_mandatory=True,
        ),
        EncounterScheduleItem(
            encounter_id="W4",
            name="Week 4 Follow-up",
            anchor_event="FIRST_DOSE",
            target_day_offset=28,
            window=VisitWindowEnvelope(
                window_before_days=3,
                window_after_days=3,
                major_deviation_tolerance_days=7,
            ),
            is_mandatory=True,
        ),
        EncounterScheduleItem(
            encounter_id="W8_CHAINED",
            name="Week 8 (Chained to W4)",
            anchor_event="PREVIOUS_VISIT",
            predecessor_encounter_id="W4",
            target_day_offset=28,
            window=VisitWindowEnvelope(window_before_days=3, window_after_days=3),
            is_mandatory=True,
        ),
    ]

    # Cycle template: 3 cycles of Q3W (21 days) with Day 1 and Day 8 visits
    cycle_template = CycleTemplateDefinition(
        template_id="Q3W_CYCLES",
        name="Q3W Treatment Cycles 1-3",
        cycle_count=3,
        cycle_length_days=21,
        encounters_per_cycle=[
            EncounterScheduleItem(
                encounter_id="CYCLE_D1",
                name="Treatment Day 1",
                anchor_event="CYCLE_START",
                target_day_offset=0,
                window=VisitWindowEnvelope(window_before_days=1, window_after_days=2),
            ),
            EncounterScheduleItem(
                encounter_id="CYCLE_D8",
                name="Safety Check Day 8",
                anchor_event="CYCLE_START",
                target_day_offset=7,
                window=VisitWindowEnvelope(window_before_days=1, window_after_days=1),
            ),
        ],
    )

    return TimelineScheduleDefinition(
        study_id="STUDY-ONCOLOGY-001",
        version_index=1,
        cycle_templates=[cycle_template],
        encounters=static_encounters,
        reason_for_change="Initial protocol timeline specification",
    )


def test_timeline_definition_checksum_and_model():
    """Verify TimelineScheduleDefinition SHA-256 seal computation and properties.

    @req:PRD-SYS-001
    """
    definition = _build_test_timeline_definition()
    assert definition.study_id == "STUDY-ONCOLOGY-001"
    assert definition.version_index == 1
    assert definition.sha256_checksum is not None
    assert len(definition.sha256_checksum) == 64


def test_cycle_template_expansion():
    """Verify expanding parameterized multi-cycle templates into sequential visits.

    @req:PRD-CRF-004
    """
    definition = _build_test_timeline_definition()
    engine = VectorTimelineEngine(definition)

    all_items = engine.get_all_encounters()
    # 5 static + (3 cycles * 2 encounters) = 11 encounters
    assert len(all_items) == 11

    # Check Cycle 1
    c1_d1 = next(i for i in all_items if i.encounter_id == "CYCLE_D1_C1")
    assert c1_d1.target_day_offset == 0
    assert c1_d1.cycle_number == 1

    c1_d8 = next(i for i in all_items if i.encounter_id == "CYCLE_D8_C1")
    assert c1_d8.target_day_offset == 7

    # Check Cycle 3 (Base offset = 2 * 21 = 42)
    c3_d1 = next(i for i in all_items if i.encounter_id == "CYCLE_D1_C3")
    assert c3_d1.target_day_offset == 42
    assert c3_d1.cycle_number == 3

    c3_d8 = next(i for i in all_items if i.encounter_id == "CYCLE_D8_C3")
    assert c3_d8.target_day_offset == 49


def test_compute_subject_schedule_with_anchors_and_chaining():
    """Verify target date calculations with relative anchors and dynamic predecessor chaining.

    @req:PRD-SYS-001, PRD-CRF-004
    """
    definition = _build_test_timeline_definition()
    engine = VectorTimelineEngine(definition)

    anchors = {
        "SCREENING": "2026-09-01",
        "RANDOMIZATION": "2026-09-15",
        "FIRST_DOSE": "2026-09-15",
        "CYCLE_START": "2026-09-15",
    }
    # Predecessor completed date for W4: Completed on 2026-10-15 (2 days late from target 2026-10-13)
    completed = {"W4": "2026-10-15"}

    schedule = engine.compute_subject_schedule(
        anchor_dates=anchors, completed_visit_dates=completed
    )

    sched_map = {s.encounter_id: s for s in schedule}

    # Screening: 2026-09-01 - 14 days = 2026-08-18
    assert sched_map["SCR"].target_date == "2026-08-18"
    assert sched_map["SCR"].window_start_date == "2026-08-04"
    assert sched_map["SCR"].window_end_date == "2026-08-18"

    # W4: 2026-09-15 + 28 days = 2026-10-13
    assert sched_map["W4"].target_date == "2026-10-13"
    assert sched_map["W4"].window_start_date == "2026-10-10"
    assert sched_map["W4"].window_end_date == "2026-10-16"

    # W8_CHAINED: Anchored dynamically to completed W4 (2026-10-15) + 28 days = 2026-11-12
    assert sched_map["W8_CHAINED"].target_date == "2026-11-12"
    assert sched_map["W8_CHAINED"].anchor_used == "PREDECESSOR(W4)"
    assert sched_map["W8_CHAINED"].window_start_date == "2026-11-09"
    assert sched_map["W8_CHAINED"].window_end_date == "2026-11-15"


def test_evaluate_visit_deviation_classifications():
    """Verify visit deviation scoring across within-window, early, late, and major out-of-window.

    @req:PRD-CRF-004
    """
    definition = _build_test_timeline_definition()
    engine = VectorTimelineEngine(definition)

    anchors = {"FIRST_DOSE": "2026-09-15"}
    # Target date for W4 is 2026-10-13 (Window: 2026-10-10 to 2026-10-16, Major threshold: 7 days)

    # 1. Within Window (2026-10-14)
    dev_normal = engine.evaluate_visit_deviation(
        encounter_id="W4", actual_date="2026-10-14", anchor_dates=anchors
    )
    assert dev_normal.classification == "WITHIN_WINDOW"
    assert dev_normal.is_deviation is False
    assert dev_normal.deviation_severity == "NONE"

    # 2. Early Visit Minor (2026-10-08 -> 2 days before window start 2026-10-10)
    dev_early = engine.evaluate_visit_deviation(
        encounter_id="W4", actual_date="2026-10-08", anchor_dates=anchors
    )
    assert dev_early.classification == "EARLY_VISIT"
    assert dev_early.is_deviation is True
    assert dev_early.deviation_severity == "MINOR"

    # 3. Late Visit Minor (2026-10-20 -> 4 days after window end 2026-10-16)
    dev_late = engine.evaluate_visit_deviation(
        encounter_id="W4", actual_date="2026-10-20", anchor_dates=anchors
    )
    assert dev_late.classification == "LATE_VISIT"
    assert dev_late.is_deviation is True
    assert dev_late.deviation_severity == "MINOR"

    # 4. Major Out of Window (2026-10-26 -> 10 days after window end, exceeding 7-day major threshold)
    dev_major = engine.evaluate_visit_deviation(
        encounter_id="W4", actual_date="2026-10-26", anchor_dates=anchors
    )
    assert dev_major.classification == "OUT_OF_WINDOW_MAJOR"
    assert dev_major.is_deviation is True
    assert dev_major.deviation_severity == "MAJOR"
