"""Unit tests for the Cadence Clinical GitHub project synchronization and label-based backlog gating.

Requirements: Trace-34
"""

import json
from unittest.mock import patch

import pytest

from scripts.sync_github_project import main


@pytest.fixture
def mock_gh_project_list():
    """Mock JSON string representing items in the project board."""
    return json.dumps(
        {
            "items": [
                {
                    "id": "item_enhancement_and_triage_id",
                    "content": {"type": "Issue", "number": 101},
                    "status": "Ready",
                    "priority": "P2",
                    "size": "M",
                },
                {
                    "id": "item_enhancement_only_id",
                    "content": {"type": "Issue", "number": 102},
                    "status": "Backlog",
                    "priority": "P2",
                    "size": "M",
                },
                {
                    "id": "item_bug_and_triage_id",
                    "content": {"type": "Issue", "number": 103},
                    "status": "Backlog",
                    "priority": "P2",
                    "size": "S",
                },
                {
                    "id": "item_bug_triage_enhancement_id",
                    "content": {"type": "Issue", "number": 104},
                    "status": "Ready",
                    "priority": "P2",
                    "size": "S",
                },
                {
                    "id": "item_task_triage_only_id",
                    "content": {"type": "Issue", "number": 105},
                    "status": "Ready",
                    "priority": "P2",
                    "size": "M",
                },
            ]
        }
    )


@patch("scripts.sync_github_project.run_gql")
@patch("scripts.sync_github_project.run_cmd")
@patch("scripts.sync_github_project.fetch_all_issues_gql")
def test_label_based_backlog_gating(
    mock_fetch_issues, mock_run_cmd, mock_run_gql, mock_gh_project_list
):
    """Verify that the label-based backlog gating logic is correctly applied to issue status selection.

    @req:Trace-34
    """
    # Define our issues map returned by fetch_all_issues_gql
    mock_issues = {
        # 1. Newly created issue with both enhancement and needs-triage labels -> forced to Backlog
        101: {
            "id": "issue_101",
            "databaseId": 1001,
            "number": 101,
            "title": "New feature request",
            "state": "OPEN",
            "url": "https://github.com/fderuiter/cadence-clinical/issues/101",
            "body": "This is a new feature request.",
            "labels": {"nodes": [{"name": "enhancement"}, {"name": "needs-triage"}]},
            "milestone": None,
            "parent": None,
            "subIssues": {"nodes": []},
            "blockedBy": {"nodes": []},
            "blocking": {"nodes": []},
        },
        # 2. Issue with needs-triage removed (only enhancement) -> allowed to move to Ready
        102: {
            "id": "issue_102",
            "databaseId": 1002,
            "number": 102,
            "title": "Triaged feature request",
            "state": "OPEN",
            "url": "https://github.com/fderuiter/cadence-clinical/issues/102",
            "body": "This feature request has been triaged.",
            "labels": {"nodes": [{"name": "enhancement"}]},
            "milestone": None,
            "parent": None,
            "subIssues": {"nodes": []},
            "blockedBy": {"nodes": []},
            "blocking": {"nodes": []},
        },
        # 3. Bug report with bug and needs-triage -> bypasses gating, allowed to go to Ready
        103: {
            "id": "issue_103",
            "databaseId": 1003,
            "number": 103,
            "title": "Urgent bug fix",
            "state": "OPEN",
            "url": "https://github.com/fderuiter/cadence-clinical/issues/103",
            "body": "This is a bug fix that bypasses gating.",
            "labels": {"nodes": [{"name": "bug"}, {"name": "needs-triage"}]},
            "milestone": None,
            "parent": None,
            "subIssues": {"nodes": []},
            "blockedBy": {"nodes": []},
            "blocking": {"nodes": []},
        },
        # 4. Bug report with bug, needs-triage, and enhancement -> does NOT bypass, forced to Backlog
        104: {
            "id": "issue_104",
            "databaseId": 1004,
            "number": 104,
            "title": "Bug that is also an enhancement",
            "state": "OPEN",
            "url": "https://github.com/fderuiter/cadence-clinical/issues/104",
            "body": "This bug is treated as an enhancement.",
            "labels": {
                "nodes": [
                    {"name": "bug"},
                    {"name": "needs-triage"},
                    {"name": "enhancement"},
                ]
            },
            "milestone": None,
            "parent": None,
            "subIssues": {"nodes": []},
            "blockedBy": {"nodes": []},
            "blocking": {"nodes": []},
        },
        # 5. Non-bug task with needs-triage (no enhancement) -> forced to Backlog
        105: {
            "id": "issue_105",
            "databaseId": 1005,
            "number": 105,
            "title": "Non-bug triage task",
            "state": "OPEN",
            "url": "https://github.com/fderuiter/cadence-clinical/issues/105",
            "body": "This is a non-bug task needing triage.",
            "labels": {"nodes": [{"name": "needs-triage"}]},
            "milestone": None,
            "parent": None,
            "subIssues": {"nodes": []},
            "blockedBy": {"nodes": []},
            "blocking": {"nodes": []},
        },
    }

    mock_fetch_issues.return_value = mock_issues

    def mock_run_cmd_side_effect(args):
        # When fetching the project items list, return our mock_gh_project_list
        if "item-list" in args:
            return mock_gh_project_list
        # For edits, return mock success
        return "{}"

    mock_run_cmd.side_effect = mock_run_cmd_side_effect

    # Run the synchronization pipeline main logic
    main()

    # Track edit calls made by run_cmd
    called_edits = []
    for call in mock_run_cmd.call_args_list:
        args = call[0][0]
        if "item-edit" in args:
            called_edits.append(args)

    # We expect status updates for the following:
    # 1. Issue 101: Current project status is "Ready", target status is "Backlog" -> Should edit status to Backlog
    # 2. Issue 102: Current project status is "Backlog", target status is "Ready" -> Should edit status to Ready
    # 3. Issue 103: Current project status is "Backlog", target status is "Ready" -> Should edit status to Ready
    # 4. Issue 104: Current project status is "Ready", target status is "Backlog" -> Should edit status to Backlog
    # 5. Issue 105: Current project status is "Ready", target status is "Backlog" -> Should edit status to Backlog

    edit_by_item_id = {}
    for edit in called_edits:
        try:
            id_idx = edit.index("--id") + 1
            item_id = edit[id_idx]
            field_idx = edit.index("--field-id") + 1
            field_id = edit[field_idx]
            val_idx = edit.index("--single-select-option-id") + 1
            val_id = edit[val_idx]
            if field_id == "PVTSSF_lAHOB5yjmM4BeuvnzhZGxXA":  # STATUS_FIELD_ID
                edit_by_item_id[item_id] = val_id
        except ValueError:
            continue

    # Option ID mapping: "Backlog" -> "f75ad846", "Ready" -> "e18bf179"
    assert edit_by_item_id.get("item_enhancement_and_triage_id") == "f75ad846"
    assert edit_by_item_id.get("item_enhancement_only_id") == "e18bf179"
    assert edit_by_item_id.get("item_bug_and_triage_id") == "e18bf179"
    assert edit_by_item_id.get("item_bug_triage_enhancement_id") == "f75ad846"
    assert edit_by_item_id.get("item_task_triage_only_id") == "f75ad846"


@patch("scripts.sync_github_project.run_gql")
@patch("scripts.sync_github_project.run_cmd")
def test_sync_with_unsupported_relationships(
    mock_run_cmd, mock_run_gql, mock_gh_project_list
):
    """Verify that the sync tool handles unsupported relationship fields gracefully.

    @req:Trace-34
    """
    import scripts.sync_github_project

    # Reset any cached state from other tests
    scripts.sync_github_project.RELATIONSHIPS_SUPPORTED = True

    calls = []

    def mock_run_gql_side_effect(query, variables=None):
        calls.append(query)
        if "parent" in query or "subIssues" in query:
            # First query, containing relationships, simulated to fail/unsupported
            return None
        # Fallback query
        return {
            "data": {
                "repository": {
                    "issues": {
                        "nodes": [
                            {
                                "id": "issue_101",
                                "databaseId": 1001,
                                "number": 101,
                                "title": "Fallback issue",
                                "state": "OPEN",
                                "url": "https://github.com/fderuiter/cadence-clinical/issues/101",
                                "body": "Fallback body",
                                "labels": {"nodes": []},
                                "milestone": None,
                            }
                        ],
                        "pageInfo": {"hasNextPage": False, "endCursor": None},
                    }
                }
            }
        }

    mock_run_gql.side_effect = mock_run_gql_side_effect

    def mock_run_cmd_side_effect(args):
        if "item-list" in args:
            return mock_gh_project_list
        return "{}"

    mock_run_cmd.side_effect = mock_run_cmd_side_effect

    # Run main logic
    scripts.sync_github_project.main()

    # Assert that RELATIONSHIPS_SUPPORTED became False
    assert scripts.sync_github_project.RELATIONSHIPS_SUPPORTED is False

    # Check that at least two queries were made (the relationship query and the fallback query)
    assert len(calls) >= 2
    assert "subIssues" in calls[0]
    assert "subIssues" not in calls[1]
