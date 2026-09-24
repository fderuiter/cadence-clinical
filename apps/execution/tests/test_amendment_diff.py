"""Unit test suite for USDM protocol version diffing engine.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from apps.execution.services.amendment_diff import StudyVersionDiffEngine


def test_compare_usdm_snapshots_added_removed_modified() -> None:
    """Validate USDM protocol snapshot comparison detects added, removed, and modified activities.

    Requirements: PRD-SYS-001
    """
    v1_snapshot = {
        "version": "1.0",
        "activities": [
            {"id": "act_01", "name": "Vital Signs", "description": "Baseline VS"},
            {"id": "act_02", "name": "ECG", "description": "Standard 12-lead"},
        ],
    }

    v2_snapshot = {
        "version": "2.0",
        "activities": [
            {
                "id": "act_01",
                "name": "Vital Signs & Weight",
                "description": "Expanded VS",
            },  # Modified
            {
                "id": "act_03",
                "name": "PK Blood Draw",
                "description": "Pharmacokinetics",
            },  # Added
        ],  # act_02 removed
    }

    engine = StudyVersionDiffEngine()
    diff = engine.compare_usdm_snapshots(v1_snapshot, v2_snapshot)

    assert diff["version_from"] == "1.0"
    assert diff["version_to"] == "2.0"

    assert len(diff["added_activities"]) == 1
    assert diff["added_activities"][0]["id"] == "act_03"

    assert len(diff["removed_activities"]) == 1
    assert diff["removed_activities"][0]["id"] == "act_02"

    assert len(diff["modified_fields"]) == 1
    assert diff["modified_fields"][0]["activity_id"] == "act_01"
    assert diff["modified_fields"][0]["new_name"] == "Vital Signs & Weight"
