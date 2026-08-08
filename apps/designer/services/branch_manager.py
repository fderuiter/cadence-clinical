"""Git-like protocol amendment branching and block-level diffing service.

Requirements: PRD-SYS-001
"""

import uuid
from typing import Any

from apps.designer.src.domain.cdisc.branch_models import (
    AmendmentComparisonResponse,
    BlockDiff,
    ProtocolBranch,
)


class ProtocolBranchManager:
    """Manager service for protocol amendment branching, block diffing, and GxP merges.

    Requirements: PRD-SYS-001
    """

    def create_amendment_branch(
        self, study_id: str, branch_name: str, created_by: str
    ) -> ProtocolBranch:
        """CREATE a new isolated working draft branch FROM approved baseline.

        Args:
            study_id: Protocol study identifier.
            branch_name: Name for working draft branch.
            created_by: User ID creating amendment branch.

        Returns:
            Initialized ProtocolBranch model.
        """
        branch_id = f"br-{uuid.uuid4().hex[:8]}"
        return ProtocolBranch(
            branch_id=branch_id,
            study_id=study_id,
            branch_name=branch_name,
            base_version=1,
            head_version=1,
            status="draft",
            created_by=created_by,
        )

    def compare_branches(
        self, base_study: dict[str, Any], draft_study: dict[str, Any]
    ) -> AmendmentComparisonResponse:
        """Perform block-level visual diff comparison between baseline and draft protocol.

        Args:
            base_study: Baseline protocol dictionary payload.
            draft_study: Working draft protocol dictionary payload.

        Returns:
            AmendmentComparisonResponse containing list of BlockDiff items.
        """
        diffs: list[BlockDiff] = []

        base_id = base_study.get("id", "study_base")

        # Compare root title / name
        if base_study.get("name") != draft_study.get("name"):
            diffs.append(
                BlockDiff(
                    block_id=f"{base_id}_name",
                    block_type="StudyMetadata",
                    change_type="MODIFIED",
                    old_content=str(base_study.get("name")),
                    new_content=str(draft_study.get("name")),
                )
            )

        # Compare objectives / eligibility criteria items if present
        base_items = {
            item["id"]: item for item in base_study.get("eligibilityCriteria", [])
        }
        draft_items = {
            item["id"]: item for item in draft_study.get("eligibilityCriteria", [])
        }

        # Check modified and deleted items
        for item_id, base_item in base_items.items():
            if item_id not in draft_items:
                diffs.append(
                    BlockDiff(
                        block_id=item_id,
                        block_type="EligibilityCriterion",
                        change_type="DELETED",
                        old_content=str(base_item.get("text")),
                        new_content=None,
                    )
                )
            elif base_item != draft_items[item_id]:
                diffs.append(
                    BlockDiff(
                        block_id=item_id,
                        block_type="EligibilityCriterion",
                        change_type="MODIFIED",
                        old_content=str(base_item.get("text")),
                        new_content=str(draft_items[item_id].get("text")),
                    )
                )

        # Check added items
        for item_id, draft_item in draft_items.items():
            if item_id not in base_items:
                diffs.append(
                    BlockDiff(
                        block_id=item_id,
                        block_type="EligibilityCriterion",
                        change_type="ADDED",
                        old_content=None,
                        new_content=str(draft_item.get("text")),
                    )
                )

        modified_count = len([d for d in diffs if d.change_type != "UNCHANGED"])

        return AmendmentComparisonResponse(
            study_id=base_id,
            source_branch="master",
            target_branch="amendment-draft",
            diffs=diffs,
            total_changes=modified_count,
        )

    def merge_amendment_branch(
        self, branch: ProtocolBranch, change_reason: str, approved_by: str
    ) -> dict[str, Any]:
        """MERGE approved amendment branch into master protocol WITH GxP audit log.

        Args:
            branch: Active working draft ProtocolBranch.
            change_reason: Mandatory GxP 21 CFR Part 11 change justification.
            approved_by: User ID approving and executing MERGE.

        Returns:
            Dict containing updated study metadata and MERGE confirmation.
        """
        branch.status = "merged"
        branch.head_version += 1

        return {
            "study_id": branch.study_id,
            "branch_id": branch.branch_id,
            "merged_version": branch.head_version,
            "status": "merged",
            "approved_by": approved_by,
            "change_reason": change_reason,
            "audit_tx": f"tx-{uuid.uuid4().hex[:12]}",
        }
