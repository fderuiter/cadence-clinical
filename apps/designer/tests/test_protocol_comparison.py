"""Unit test suite for Git-like protocol amendment branching and block-level diffing.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from apps.designer.services.branch_manager import ProtocolBranchManager


def test_create_amendment_branch() -> None:
    """Validate creating an isolated protocol amendment working branch.

    Requirements: PRD-SYS-001
    """
    manager = ProtocolBranchManager()
    branch = manager.create_amendment_branch(
        study_id="study_usdm_001",
        branch_name="amendment-v2.0-draft",
        created_by="user_author_01",
    )

    assert branch.study_id == "study_usdm_001"
    assert branch.branch_name == "amendment-v2.0-draft"
    assert branch.status == "draft"
    assert branch.base_version == 1
    assert branch.created_by == "user_author_01"


def test_compare_branches_block_diffing() -> None:
    """Validate block-level diffing detects added, modified, and deleted protocol nodes.

    Requirements: PRD-SYS-001
    """
    base_payload = {
        "id": "study_001",
        "name": "Original Protocol Name",
        "eligibilityCriteria": [
            {"id": "crit_01", "text": "Age >= 18"},
            {"id": "crit_02", "text": "No renal failure"},
        ],
    }

    draft_payload = {
        "id": "study_001",
        "name": "Amended Protocol Name",
        "eligibilityCriteria": [
            {"id": "crit_01", "text": "Age >= 18 and Age <= 75"},  # Modified
            {"id": "crit_03", "text": "Signed consent form"},  # Added
            # crit_02 deleted
        ],
    }

    manager = ProtocolBranchManager()
    comparison = manager.compare_branches(base_payload, draft_payload)

    assert comparison.study_id == "study_001"
    assert (
        comparison.total_changes == 4
    )  # Name modified, crit_01 modified, crit_02 deleted, crit_03 added

    diff_types = {d.block_id: d.change_type for d in comparison.diffs}
    assert diff_types["study_001_name"] == "MODIFIED"
    assert diff_types["crit_01"] == "MODIFIED"
    assert diff_types["crit_02"] == "DELETED"
    assert diff_types["crit_03"] == "ADDED"


def test_merge_amendment_branch() -> None:
    """Validate merging approved amendment branch updates status and creates GxP audit log.

    Requirements: PRD-SYS-001
    """
    manager = ProtocolBranchManager()
    branch = manager.create_amendment_branch("study_002", "amendment-v3", "user_01")

    result = manager.merge_amendment_branch(
        branch=branch,
        change_reason="Protocol amendment V3 approved by EC",
        approved_by="medical_monitor_99",
    )

    assert result["status"] == "merged"
    assert result["merged_version"] == 2
    assert result["approved_by"] == "medical_monitor_99"
    assert "audit_tx" in result
    assert branch.status == "merged"
