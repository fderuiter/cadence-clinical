"""Integration test suite for backward-compatible eCRF form rehydration across protocol amendments.

Requirements: PRD-SYS-001
"""

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.execution.tests.test_lock_router import _make_auth_headers
from apps.execution.main import app
from apps.execution.services.subject_migration import LiveSubjectMigrationEngine

client = TestClient(app)


def test_backward_compatible_form_rehydration_lifecycle() -> None:
    """Validate full amendment publishing, live subject migration, and backward-compatible rehydration.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(
        user_id="designer_200",
        roles="study_designer",
        change_reason="Publish Amendment v2.0 for rehydration test",
    )

    baseline = {
        "version": "1.0",
        "activities": [
            {"id": "act_01", "name": "Vital Signs", "description": "Baseline VS"}
        ],
    }
    amended = {
        "version": "2.0",
        "activities": [
            {
                "id": "act_01",
                "name": "Vital Signs & Weight",
                "description": "Expanded VS",
            }
        ],
    }

    # Step 1: Publish Amendment v2.0 via API
    res_pub = client.post(
        "/api/v1/execution/amendments/publish",
        json={
            "study_id": "study_rehydrate_01",
            "version_number": "2.0",
            "description": "Expand Vital Signs activity",
            "baseline_snapshot": baseline,
            "amended_snapshot": amended,
        },
        headers=headers,
    )
    assert res_pub.status_code == 200

    # Step 2: Migrate existing subject eCRF submissions
    submissions = [
        {
            "form_id": "form_vs_rehydrate_01",
            "protocol_version": "1.0",
            "data": {"SYSBP": 118, "DIABP": 78, "OLD_WT": 68.0},
        }
    ]

    migration_engine = LiveSubjectMigrationEngine()
    mig_res = migration_engine.migrate_subject_submissions(
        subject_id="sub_rehydrate_100",
        old_version="1.0",
        new_version="2.0",
        form_submissions=submissions,
        field_mapping={"OLD_WT": "WEIGHT_KG"},
    )

    assert mig_res["status"] == "COMPLETED"
    assert mig_res["migrated_submissions_count"] == 1
    assert mig_res["updated_fields_count"] == 1

    # Step 3: Verify rehydrated submission retains data integrity under v2.0
    rehydrated = submissions[0]
    assert rehydrated["protocol_version"] == "2.0"
    assert rehydrated["data"]["SYSBP"] == 118
    assert rehydrated["data"]["DIABP"] == 78
    assert rehydrated["data"]["WEIGHT_KG"] == 68.0
    assert "OLD_WT" not in rehydrated["data"]
