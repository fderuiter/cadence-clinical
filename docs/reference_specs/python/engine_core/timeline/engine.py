"""Pure mathematical execution engine for Schedule of Activities (SoA) Vector Timelines.

Evaluates relative anchor event offsets, asymmetric window envelopes, cycle template
expansions, predecessor DAG chaining, and automated deviation classifications under 21 CFR Part 11.
"""

from datetime import date, datetime, timedelta

from packages.engine_core.timeline.models import (
    AnchorEventType,
    CalculatedVisitSchedule,
    CycleTemplateDefinition,
    EncounterScheduleItem,
    TimelineScheduleDefinition,
    VisitDeviationAssessment,
    VisitDeviationClassification,
)


def _parse_date(dt: date | datetime | str) -> date:
    """Safely normalizes input date/datetime/string to standard datetime.date."""
    if isinstance(dt, datetime):
        return dt.date()
    if isinstance(dt, date):
        return dt
    if isinstance(dt, str):
        cleaned = dt.strip().split("T")[0]
        return date.fromisoformat(cleaned)
    raise ValueError(f"Invalid date format: {dt}")


class VectorTimelineEngine:
    """Pure in-memory vector timeline scheduler and deviation analysis runtime."""

    def __init__(self, definition: TimelineScheduleDefinition | None = None) -> None:
        self.definition = definition or TimelineScheduleDefinition(
            study_id="DEFAULT_STUDY"
        )

    def expand_cycle_templates(
        self,
        template: CycleTemplateDefinition,
        anchor_event: AnchorEventType = "CYCLE_START",
        start_cycle: int = 1,
    ) -> list[EncounterScheduleItem]:
        """Expands a repeating multi-cycle template into discrete sequential encounter items."""
        expanded: list[EncounterScheduleItem] = []

        for cycle_idx in range(start_cycle, start_cycle + template.cycle_count):
            cycle_base_offset = (cycle_idx - 1) * template.cycle_length_days

            for item in template.encounters_per_cycle:
                enc_id = f"{item.encounter_id}_C{cycle_idx}"
                enc_name = (
                    f"Cycle {cycle_idx} {item.name}"
                    if "Cycle" not in item.name
                    else item.name
                )
                target_offset = cycle_base_offset + item.target_day_offset

                expanded.append(
                    EncounterScheduleItem(
                        encounter_id=enc_id,
                        name=enc_name,
                        anchor_event=anchor_event,
                        target_day_offset=target_offset,
                        window=item.window.model_copy(),
                        is_mandatory=item.is_mandatory,
                        epoch_id=item.epoch_id,
                        cycle_number=cycle_idx,
                    )
                )

        return expanded

    def get_all_encounters(self) -> list[EncounterScheduleItem]:
        """Returns flattened list of static encounters plus expanded cycle templates."""
        all_items: list[EncounterScheduleItem] = list(self.definition.encounters)

        for template in self.definition.cycle_templates:
            expanded = self.expand_cycle_templates(template)
            all_items.extend(expanded)

        return all_items

    def compute_subject_schedule(
        self,
        anchor_dates: dict[AnchorEventType | str, date | datetime | str],
        completed_visit_dates: dict[str, date | datetime | str] | None = None,
        encounters: list[EncounterScheduleItem] | None = None,
    ) -> list[CalculatedVisitSchedule]:
        """Calculates exact calendar visit targets and window opening/closing bounds.

        Args:
            anchor_dates: Map of anchor events (e.g. {'FIRST_DOSE': '2026-09-01'}).
            completed_visit_dates: Map of predecessor encounter IDs to actual dates.
            encounters: Optional override list of encounters to evaluate.

        Returns:
            List of CalculatedVisitSchedule objects sorted by target date.
        """
        normalized_anchors: dict[str, date] = {
            str(k).upper(): _parse_date(v) for k, v in anchor_dates.items()
        }
        completed: dict[str, date] = {
            k: _parse_date(v) for k, v in (completed_visit_dates or {}).items()
        }

        items = encounters if encounters is not None else self.get_all_encounters()
        calculated: list[CalculatedVisitSchedule] = []

        for item in items:
            anchor_key = item.anchor_event.upper()
            anchor_dt: date | None = None
            anchor_used_name: str = item.anchor_event

            # Check if dynamically chained to predecessor visit
            if item.anchor_event == "PREVIOUS_VISIT" and item.predecessor_encounter_id:
                if item.predecessor_encounter_id in completed:
                    anchor_dt = completed[item.predecessor_encounter_id]
                    anchor_used_name = f"PREDECESSOR({item.predecessor_encounter_id})"
                elif item.predecessor_encounter_id in normalized_anchors:
                    anchor_dt = normalized_anchors[item.predecessor_encounter_id]
                    anchor_used_name = f"PREDECESSOR({item.predecessor_encounter_id})"

            # Standard anchor event lookup
            if anchor_dt is None:
                if anchor_key in normalized_anchors:
                    anchor_dt = normalized_anchors[anchor_key]
                elif "FIRST_DOSE" in normalized_anchors:
                    anchor_dt = normalized_anchors["FIRST_DOSE"]
                    anchor_used_name = "FIRST_DOSE_FALLBACK"
                elif "RANDOMIZATION" in normalized_anchors:
                    anchor_dt = normalized_anchors["RANDOMIZATION"]
                    anchor_used_name = "RANDOMIZATION_FALLBACK"
                elif "SCREENING" in normalized_anchors:
                    anchor_dt = normalized_anchors["SCREENING"]
                    anchor_used_name = "SCREENING_FALLBACK"

            if anchor_dt is None:
                continue

            target_dt = anchor_dt + timedelta(days=item.target_day_offset)
            window_start = target_dt - timedelta(days=item.window.window_before_days)
            window_end = target_dt + timedelta(days=item.window.window_after_days)

            calculated.append(
                CalculatedVisitSchedule(
                    encounter_id=item.encounter_id,
                    name=item.name,
                    cycle_number=item.cycle_number,
                    target_date=target_dt.isoformat(),
                    window_start_date=window_start.isoformat(),
                    window_end_date=window_end.isoformat(),
                    anchor_used=anchor_used_name,
                    anchor_date=anchor_dt.isoformat(),
                    is_mandatory=item.is_mandatory,
                    epoch_id=item.epoch_id,
                )
            )

        calculated.sort(key=lambda s: s.target_date)
        return calculated

    def evaluate_visit_deviation(
        self,
        encounter_id: str,
        actual_date: date | datetime | str,
        anchor_dates: dict[AnchorEventType | str, date | datetime | str],
        completed_visit_dates: dict[str, date | datetime | str] | None = None,
        encounters: list[EncounterScheduleItem] | None = None,
    ) -> VisitDeviationAssessment:
        """Assesses an actual visit completion against its scheduled window envelope."""
        schedules = self.compute_subject_schedule(
            anchor_dates=anchor_dates,
            completed_visit_dates=completed_visit_dates,
            encounters=encounters,
        )

        sched_map = {s.encounter_id: s for s in schedules}
        if encounter_id not in sched_map:
            raise ValueError(
                f"Encounter '{encounter_id}' not found in calculated subject schedule"
            )

        sched = sched_map[encounter_id]
        actual_dt = _parse_date(actual_date)
        target_dt = date.fromisoformat(sched.target_date)
        win_start = date.fromisoformat(sched.window_start_date)
        win_end = date.fromisoformat(sched.window_end_date)

        delta_days = (actual_dt - target_dt).days

        # Retrieve window envelope for major tolerance threshold
        all_items = encounters if encounters is not None else self.get_all_encounters()
        matching_item = next(
            (i for i in all_items if i.encounter_id == encounter_id), None
        )
        major_threshold = (
            matching_item.window.major_deviation_tolerance_days if matching_item else 7
        )

        classification: VisitDeviationClassification
        is_deviation: bool
        severity: str
        rationale: str

        if win_start <= actual_dt <= win_end:
            classification = "WITHIN_WINDOW"
            is_deviation = False
            severity = "NONE"
            rationale = f"Visit occurred on target {sched.target_date} (within permissible window [{sched.window_start_date} to {sched.window_end_date}])."
        elif actual_dt < win_start:
            days_early = (win_start - actual_dt).days
            if days_early > major_threshold:
                classification = "OUT_OF_WINDOW_MAJOR"
                is_deviation = True
                severity = "MAJOR"
                rationale = f"Visit occurred {days_early} days before window opening ({sched.window_start_date}), exceeding major deviation threshold of {major_threshold} days."
            else:
                classification = "EARLY_VISIT"
                is_deviation = True
                severity = "MINOR"
                rationale = f"Visit occurred {days_early} days before window opening ({sched.window_start_date})."
        else:
            days_late = (actual_dt - win_end).days
            if days_late > major_threshold:
                classification = "OUT_OF_WINDOW_MAJOR"
                is_deviation = True
                severity = "MAJOR"
                rationale = f"Visit occurred {days_late} days after window closing ({sched.window_end_date}), exceeding major deviation threshold of {major_threshold} days."
            else:
                classification = "LATE_VISIT"
                is_deviation = True
                severity = "MINOR"
                rationale = f"Visit occurred {days_late} days after window closing ({sched.window_end_date})."

        return VisitDeviationAssessment(
            encounter_id=encounter_id,
            actual_date=actual_dt.isoformat(),
            target_date=sched.target_date,
            window_start_date=sched.window_start_date,
            window_end_date=sched.window_end_date,
            delta_days_from_target=delta_days,
            classification=classification,
            is_deviation=is_deviation,
            deviation_severity=severity,  # type: ignore[arg-type]
            rationale=rationale,
        )
