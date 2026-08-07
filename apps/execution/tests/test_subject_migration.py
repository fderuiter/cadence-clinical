"""Unit test suite for live subject data migration engine.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from apps.execution.services.subject_migration import LiveSubjectMigrationEngine


def test_migrate_subject_submissions_field_remapping() -> None:
    """Validate migrating subject eCRF submissions re-maps renamed fields and updates version.

    Requirements: PRD-SYS-001
    """
    engine = LiveSubjectMigrationEngine()

    submissions = [
        {
            "form_id": "form_vs_01",
            "protocol_version": "1.0",
            "data": {"SYSBP": 120, "DIABP": 80, "OLD_WEIGHT": 70.5},
        },
        {
            "form_id": "form_lb_01",
            "protocol_version": "1.0",
            "data": {"GLUCOSE": 95},
        },
    ]

    field_mapping = {"OLD_WEIGHT": "WEIGHT_KG"}

    res = engine.migrate_subject_submissions(
        subject_id="sub_mig_01",
        old_version="1.0",
        new_version="2.0",
        form_submissions=submissions,
        field_mapping=field_mapping,
    )

    assert res["status"] == "COMPLETED"
    assert res["migrated_submissions_count"] == 2
    assert res["updated_fields_count"] == 1

    # Assert field was re-mapped correctly
    assert submissions[0]["protocol_version"] == "2.0"
    assert "OLD_WEIGHT" not in submissions[0]["data"]
    assert submissions[0]["data"]["WEIGHT_KG"] == 70.5
