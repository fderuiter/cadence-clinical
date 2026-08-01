"""Study version diffing engine comparing USDM protocol snapshots across protocol amendments.

Requirements: PRD-SYS-001
"""

from typing import Any

import packages  # noqa: F401


class StudyVersionDiffEngine:
    """Engine comparing USDM protocol JSON snapshots to identify structural amendment changes.

    Requirements: PRD-SYS-001
    """

    def compare_usdm_snapshots(
        self, snapshot_v1: dict[str, Any], snapshot_v2: dict[str, Any]
    ) -> dict[str, Any]:
        """Compare two USDM protocol version snapshots and generate structured diff report.

        Args:
            snapshot_v1: Baseline USDM protocol snapshot dictionary.
            snapshot_v2: Amended USDM protocol snapshot dictionary.

        Returns:
            Structured comparison report containing added/removed activities and modified fields.
        """
        v1_num = snapshot_v1.get("version", "1.0")
        v2_num = snapshot_v2.get("version", "2.0")

        v1_acts = {a["id"]: a for a in snapshot_v1.get("activities", []) if "id" in a}
        v2_acts = {a["id"]: a for a in snapshot_v2.get("activities", []) if "id" in a}

        added_act_ids = set(v2_acts.keys()) - set(v1_acts.keys())
        removed_act_ids = set(v1_acts.keys()) - set(v2_acts.keys())
        common_act_ids = set(v1_acts.keys()) & set(v2_acts.keys())

        added_activities = [v2_acts[aid] for aid in sorted(added_act_ids)]
        removed_activities = [v1_acts[aid] for aid in sorted(removed_act_ids)]

        modified_fields: list[dict[str, Any]] = []
        for aid in sorted(common_act_ids):
            a1 = v1_acts[aid]
            a2 = v2_acts[aid]
            if a1.get("name") != a2.get("name") or a1.get("description") != a2.get(
                "description"
            ):
                modified_fields.append(
                    {
                        "activity_id": aid,
                        "old_name": a1.get("name"),
                        "new_name": a2.get("name"),
                        "old_description": a1.get("description"),
                        "new_description": a2.get("description"),
                    }
                )

        summary = (
            f"Protocol Amendment Diff v{v1_num} -> v{v2_num}: "
            f"{len(added_activities)} added activities, "
            f"{len(removed_activities)} removed activities, "
            f"{len(modified_fields)} modified activities."
        )

        return {
            "version_from": str(v1_num),
            "version_to": str(v2_num),
            "added_activities": added_activities,
            "removed_activities": removed_activities,
            "modified_fields": modified_fields,
            "summary_of_changes": summary,
        }
