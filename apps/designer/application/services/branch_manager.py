"""Git-like protocol amendment branching and block-level diffing service.

Requirements: PRD-SYS-001
"""

import uuid
from typing import Any

from apps.designer.domain.cdisc.branch_models import (
    AmendmentComparisonResponse,
    AmendmentImpactSummary,
    BlockDiff,
    EntityDiff,
    MigrationDirective,
    ProtocolBranch,
    SchemaRevisionSummary,
    SemanticDiffResponse,
)


class ProtocolBranchManager:
    """Manager service for protocol amendment branching, block diffing, and GxP merges.

    Requirements: PRD-SYS-001, PRD-SUB-007
    """

    def create_amendment_branch(
        self, study_id: str, branch_name: str, created_by: str, base_version: int = 1
    ) -> ProtocolBranch:
        """CREATE a new isolated working draft branch FROM approved baseline.

        Args:
            study_id: Protocol study identifier.
            branch_name: Name for working draft branch.
            created_by: User ID creating amendment branch.
            base_version: Base version index.

        Returns:
            Initialized ProtocolBranch model.
        """
        branch_id = f"br-{uuid.uuid4().hex[:8]}"
        return ProtocolBranch(
            branch_id=branch_id,
            study_id=study_id,
            branch_name=branch_name,
            base_version=base_version,
            head_version=base_version,
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
        base_id = base_study.get("id") or base_study.get("study_id") or "study_base"

        # Compare root title / name
        base_name = base_study.get("name") or base_study.get("title")
        draft_name = draft_study.get("name") or draft_study.get("title")
        if base_name != draft_name:
            diffs.append(
                BlockDiff(
                    block_id=f"{base_id}_name",
                    block_type="StudyMetadata",
                    change_type="MODIFIED",
                    old_content=str(base_name),
                    new_content=str(draft_name),
                )
            )

        # Compare objectives / eligibility criteria items if present
        base_items = {
            item.get("id", f"crit_{i}"): item
            for i, item in enumerate(base_study.get("eligibilityCriteria", []))
        }
        draft_items = {
            item.get("id", f"crit_{i}"): item
            for i, item in enumerate(draft_study.get("eligibilityCriteria", []))
        }

        # Check modified and deleted criteria
        for item_id, base_item in base_items.items():
            if item_id not in draft_items:
                diffs.append(
                    BlockDiff(
                        block_id=item_id,
                        block_type="EligibilityCriterion",
                        change_type="DELETED",
                        old_content=str(
                            base_item.get("text") or base_item.get("description")
                        ),
                        new_content=None,
                    )
                )
            elif base_item != draft_items[item_id]:
                diffs.append(
                    BlockDiff(
                        block_id=item_id,
                        block_type="EligibilityCriterion",
                        change_type="MODIFIED",
                        old_content=str(
                            base_item.get("text") or base_item.get("description")
                        ),
                        new_content=str(
                            draft_items[item_id].get("text")
                            or draft_items[item_id].get("description")
                        ),
                    )
                )

        # Check added criteria
        for item_id, draft_item in draft_items.items():
            if item_id not in base_items:
                diffs.append(
                    BlockDiff(
                        block_id=item_id,
                        block_type="EligibilityCriterion",
                        change_type="ADDED",
                        old_content=None,
                        new_content=str(
                            draft_item.get("text") or draft_item.get("description")
                        ),
                    )
                )

        modified_count = len(
            [d for d in diffs if d.change_type not in ("UNCHANGED", "PRESERVED")]
        )

        return AmendmentComparisonResponse(
            study_id=base_id,
            source_branch="master",
            target_branch="amendment-draft",
            diffs=diffs,
            total_changes=modified_count,
        )

    def compute_semantic_diff(
        self,
        study_id: str,
        base_version_tag: str,
        amended_version_tag: str,
        base_payload: dict[str, Any] | None = None,
        draft_payload: dict[str, Any] | None = None,
        requires_reconsent: bool = False,
    ) -> SemanticDiffResponse:
        """Compute multi-layer semantic diff across USDM Graph, SoA Matrix, Eligibility Criteria, and eCRF forms.

        Requirements: PRD-SYS-001, PRD-SUB-007
        """
        base_data = base_payload or self._resolve_protocol_payload(
            study_id, base_version_tag
        )
        draft_data = draft_payload or self._resolve_protocol_payload(
            study_id, amended_version_tag
        )

        # 1. Diff USDM Graph & SoA Matrix (Arms, Epochs, Encounters/Visits, Activities/Procedures)
        usdm_diffs: list[EntityDiff] = []
        soa_diffs: list[EntityDiff] = []

        # Extract entities from payloads
        base_arms = self._extract_named_map(base_data.get("arms", []))
        draft_arms = self._extract_named_map(draft_data.get("arms", []))

        base_visits = self._extract_visits_map(base_data)
        draft_visits = self._extract_visits_map(draft_data)

        base_activities = self._extract_activities_map(base_data)
        draft_activities = self._extract_activities_map(draft_data)

        # Arms diff
        arms_rev = {"added": 0, "removed": 0, "modified": 0, "unchanged": 0}
        for arm_id, b_arm in base_arms.items():
            if arm_id not in draft_arms:
                arms_rev["removed"] += 1
                diff = EntityDiff(
                    entity_id=arm_id,
                    entity_type="Arm",
                    name=b_arm.get("name", arm_id),
                    change_type="REMOVED",
                    spec=b_arm.get("description") or b_arm.get("type"),
                    delta_note=f"Study Arm '{b_arm.get('name', arm_id)}' deprecated in amendment.",
                )
                usdm_diffs.append(diff)
            elif b_arm != draft_arms[arm_id]:
                arms_rev["modified"] += 1
                d_arm = draft_arms[arm_id]
                diff = EntityDiff(
                    entity_id=arm_id,
                    entity_type="Arm",
                    name=d_arm.get("name", arm_id),
                    change_type="MODIFIED",
                    spec=d_arm.get("description") or d_arm.get("type"),
                    old_value=b_arm,
                    new_value=d_arm,
                    delta_note=f"Study Arm '{d_arm.get('name', arm_id)}' modified.",
                )
                usdm_diffs.append(diff)
            else:
                arms_rev["unchanged"] += 1
                diff = EntityDiff(
                    entity_id=arm_id,
                    entity_type="Arm",
                    name=b_arm.get("name", arm_id),
                    change_type="PRESERVED",
                    spec=b_arm.get("description") or b_arm.get("type"),
                )
                usdm_diffs.append(diff)

        for arm_id, d_arm in draft_arms.items():
            if arm_id not in base_arms:
                arms_rev["added"] += 1
                diff = EntityDiff(
                    entity_id=arm_id,
                    entity_type="Arm",
                    name=d_arm.get("name", arm_id),
                    change_type="ADDED",
                    spec=d_arm.get("description") or d_arm.get("type"),
                    delta_note=f"New Study Arm '{d_arm.get('name', arm_id)}' added.",
                )
                usdm_diffs.append(diff)

        # Encounters / Visits diff
        enc_rev = {"added": 0, "removed": 0, "modified": 0, "unchanged": 0}
        affected_visits = []
        for v_id, b_vis in base_visits.items():
            if v_id not in draft_visits:
                enc_rev["removed"] += 1
                affected_visits.append(b_vis.get("name", v_id))
                diff = EntityDiff(
                    entity_id=v_id,
                    entity_type="Encounter",
                    name=b_vis.get("name", v_id),
                    change_type="REMOVED",
                    spec=b_vis.get("spec") or f"Day {b_vis.get('day', 0)}",
                    schedule=b_vis.get("schedule") or f"Day {b_vis.get('day', 0)}",
                    delta_note=f"Encounter '{b_vis.get('name', v_id)}' removed from schedule.",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)
            elif b_vis != draft_visits[v_id]:
                enc_rev["modified"] += 1
                d_vis = draft_visits[v_id]
                affected_visits.append(d_vis.get("name", v_id))
                diff = EntityDiff(
                    entity_id=v_id,
                    entity_type="Encounter",
                    name=d_vis.get("name", v_id),
                    change_type="MODIFIED",
                    spec=d_vis.get("spec") or f"Day {d_vis.get('day', 0)}",
                    schedule=d_vis.get("schedule") or f"Day {d_vis.get('day', 0)}",
                    old_value=b_vis,
                    new_value=d_vis,
                    delta_note=d_vis.get("delta_note")
                    or f"Encounter '{d_vis.get('name', v_id)}' schedule or procedure set updated.",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)
            else:
                enc_rev["unchanged"] += 1
                diff = EntityDiff(
                    entity_id=v_id,
                    entity_type="Encounter",
                    name=b_vis.get("name", v_id),
                    change_type="PRESERVED",
                    spec=b_vis.get("spec") or f"Day {b_vis.get('day', 0)}",
                    schedule=b_vis.get("schedule") or f"Day {b_vis.get('day', 0)}",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)

        for v_id, d_vis in draft_visits.items():
            if v_id not in base_visits:
                enc_rev["added"] += 1
                affected_visits.append(d_vis.get("name", v_id))
                diff = EntityDiff(
                    entity_id=v_id,
                    entity_type="Encounter",
                    name=d_vis.get("name", v_id),
                    change_type="ADDED",
                    spec=d_vis.get("spec") or f"Day {d_vis.get('day', 0)}",
                    schedule=d_vis.get("schedule") or f"Day {d_vis.get('day', 0)}",
                    delta_note=d_vis.get("delta_note")
                    or f"New visit encounter '{d_vis.get('name', v_id)}' added to protocol.",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)

        # Activities / Procedures diff
        act_rev = {"added": 0, "removed": 0, "modified": 0, "unchanged": 0}
        affected_activities = []
        for a_id, b_act in base_activities.items():
            if a_id not in draft_activities:
                act_rev["removed"] += 1
                affected_activities.append(b_act.get("name", a_id))
                diff = EntityDiff(
                    entity_id=a_id,
                    entity_type="Activity",
                    name=b_act.get("name", a_id),
                    change_type="REMOVED",
                    spec=b_act.get("spec") or b_act.get("description"),
                    schedule=b_act.get("schedule") or "All Visits",
                    delta_note=f"Procedure '{b_act.get('name', a_id)}' removed.",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)
            elif b_act != draft_activities[a_id]:
                act_rev["modified"] += 1
                d_act = draft_activities[a_id]
                affected_activities.append(d_act.get("name", a_id))
                diff = EntityDiff(
                    entity_id=a_id,
                    entity_type="Activity",
                    name=d_act.get("name", a_id),
                    change_type="MODIFIED",
                    spec=d_act.get("spec") or d_act.get("description"),
                    schedule=d_act.get("schedule") or "All Visits",
                    old_value=b_act,
                    new_value=d_act,
                    delta_note=d_act.get("delta_note")
                    or f"Procedure '{d_act.get('name', a_id)}' requirements expanded.",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)
            else:
                act_rev["unchanged"] += 1
                diff = EntityDiff(
                    entity_id=a_id,
                    entity_type="Activity",
                    name=b_act.get("name", a_id),
                    change_type="PRESERVED",
                    spec=b_act.get("spec") or b_act.get("description"),
                    schedule=b_act.get("schedule") or "All Visits",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)

        for a_id, d_act in draft_activities.items():
            if a_id not in base_activities:
                act_rev["added"] += 1
                affected_activities.append(d_act.get("name", a_id))
                diff = EntityDiff(
                    entity_id=a_id,
                    entity_type="Activity",
                    name=d_act.get("name", a_id),
                    change_type="ADDED",
                    spec=d_act.get("spec") or d_act.get("description"),
                    schedule=d_act.get("schedule") or "Assigned Visits",
                    delta_note=d_act.get("delta_note")
                    or f"New clinical procedure '{d_act.get('name', a_id)}' added.",
                )
                usdm_diffs.append(diff)
                soa_diffs.append(diff)

        # 2. Diff Eligibility Criteria
        eligibility_diffs: list[EntityDiff] = []
        base_crit = {
            c.get("id", f"crit_{i}"): c
            for i, c in enumerate(base_data.get("eligibilityCriteria", []))
        }
        draft_crit = {
            c.get("id", f"crit_{i}"): c
            for i, c in enumerate(draft_data.get("eligibilityCriteria", []))
        }
        crit_rev = {"added": 0, "removed": 0, "modified": 0, "unchanged": 0}

        for c_id, b_c in base_crit.items():
            if c_id not in draft_crit:
                crit_rev["removed"] += 1
                diff = EntityDiff(
                    entity_id=c_id,
                    entity_type="Criterion",
                    name=f"Criterion {c_id}",
                    change_type="REMOVED",
                    spec=str(b_c.get("text") or b_c.get("description")),
                    delta_note=f"Eligibility criterion {c_id} removed.",
                )
                eligibility_diffs.append(diff)
            elif b_c != draft_crit[c_id]:
                crit_rev["modified"] += 1
                d_c = draft_crit[c_id]
                diff = EntityDiff(
                    entity_id=c_id,
                    entity_type="Criterion",
                    name=f"Criterion {c_id}",
                    change_type="MODIFIED",
                    spec=str(d_c.get("text") or d_c.get("description")),
                    old_value=b_c,
                    new_value=d_c,
                    delta_note=f"Eligibility criterion {c_id} condition updated from '{b_c.get('text')}' to '{d_c.get('text')}'.",
                )
                eligibility_diffs.append(diff)
            else:
                crit_rev["unchanged"] += 1
                diff = EntityDiff(
                    entity_id=c_id,
                    entity_type="Criterion",
                    name=f"Criterion {c_id}",
                    change_type="PRESERVED",
                    spec=str(b_c.get("text") or b_c.get("description")),
                )
                eligibility_diffs.append(diff)

        for c_id, d_c in draft_crit.items():
            if c_id not in base_crit:
                crit_rev["added"] += 1
                diff = EntityDiff(
                    entity_id=c_id,
                    entity_type="Criterion",
                    name=f"Criterion {c_id}",
                    change_type="ADDED",
                    spec=str(d_c.get("text") or d_c.get("description")),
                    delta_note=f"New eligibility criterion {c_id} added: '{d_c.get('text')}'.",
                )
                eligibility_diffs.append(diff)

        # 3. Diff eCRF Forms
        ecrf_form_diffs: list[EntityDiff] = []
        base_forms = self._extract_named_map(base_data.get("forms", []))
        draft_forms = self._extract_named_map(draft_data.get("forms", []))
        forms_rev = {"added": 0, "removed": 0, "modified": 0, "unchanged": 0}

        for f_id, b_f in base_forms.items():
            if f_id not in draft_forms:
                forms_rev["removed"] += 1
                diff = EntityDiff(
                    entity_id=f_id,
                    entity_type="Form",
                    name=b_f.get("name") or b_f.get("form_key", f_id),
                    change_type="REMOVED",
                    spec=b_f.get("description") or "eCRF Data Capture Form",
                    delta_note=f"Form '{b_f.get('name', f_id)}' deprecated.",
                )
                ecrf_form_diffs.append(diff)
            elif b_f != draft_forms[f_id]:
                forms_rev["modified"] += 1
                d_f = draft_forms[f_id]
                diff = EntityDiff(
                    entity_id=f_id,
                    entity_type="Form",
                    name=d_f.get("name") or d_f.get("form_key", f_id),
                    change_type="MODIFIED",
                    spec=d_f.get("description") or "eCRF Data Capture Form",
                    old_value=b_f,
                    new_value=d_f,
                    delta_note=f"Form '{d_f.get('name', f_id)}' fields/definition updated.",
                )
                ecrf_form_diffs.append(diff)
            else:
                forms_rev["unchanged"] += 1
                diff = EntityDiff(
                    entity_id=f_id,
                    entity_type="Form",
                    name=b_f.get("name") or b_f.get("form_key", f_id),
                    change_type="PRESERVED",
                    spec=b_f.get("description") or "eCRF Data Capture Form",
                )
                ecrf_form_diffs.append(diff)

        for f_id, d_f in draft_forms.items():
            if f_id not in base_forms:
                forms_rev["added"] += 1
                diff = EntityDiff(
                    entity_id=f_id,
                    entity_type="Form",
                    name=d_f.get("name") or d_f.get("form_key", f_id),
                    change_type="ADDED",
                    spec=d_f.get("description") or "eCRF Data Capture Form",
                    delta_note=f"New eCRF form '{d_f.get('name', f_id)}' added.",
                )
                ecrf_form_diffs.append(diff)

        # 4. Calculate Amendment Impact Summary
        # Burden score: visit additions (+1.5 per visit) + procedure additions (+2.0 per procedure)
        burden_delta = float(
            (enc_rev["added"] - enc_rev["removed"]) * 1.5
            + (act_rev["added"] - act_rev["removed"]) * 2.0
            + (enc_rev["modified"] + act_rev["modified"]) * 0.5
        )

        is_substantial = (
            requires_reconsent
            or enc_rev["added"] > 0
            or enc_rev["removed"] > 0
            or act_rev["added"] > 0
            or crit_rev["added"] > 0
            or crit_rev["modified"] > 0
            or (base_version_tag.split(".")[0] != amended_version_tag.split(".")[0])
        )

        reconsent_flag = requires_reconsent or (
            is_substantial
            and (enc_rev["added"] > 0 or act_rev["added"] > 0 or crit_rev["added"] > 0)
        )

        cost = (
            5000.0
            + enc_rev["added"] * 1500.0
            + act_rev["added"] * 800.0
            + forms_rev["added"] * 300.0
            + (enc_rev["modified"] + act_rev["modified"]) * 250.0
        )

        narrative = (
            f"Protocol amendment from v{base_version_tag} to v{amended_version_tag}. "
            f"Changes include {enc_rev['added']} added visit(s), {enc_rev['modified']} modified visit(s), "
            f"{act_rev['added']} added procedure(s), and {crit_rev['added'] + crit_rev['modified']} eligibility revision(s). "
            f"Calculated patient burden delta is {burden_delta:+.1f}. "
            f"{'Mandatory in-flight subject re-consent is mandated.' if reconsent_flag else 'Administrative changes; subject re-consent not mandated.'}"
        )

        schema_summary = SchemaRevisionSummary(
            arms=arms_rev,
            epochs={"added": 0, "removed": 0, "modified": 0, "unchanged": 1},
            encounters=enc_rev,
            activities=act_rev,
            eligibility_criteria=crit_rev,
            forms=forms_rev,
        )

        impact_summary = AmendmentImpactSummary(
            base_version=base_version_tag,
            amended_version=amended_version_tag,
            burden_delta=round(burden_delta, 1),
            affected_visits_count=len(affected_visits),
            affected_visits=affected_visits,
            affected_activities_count=len(affected_activities),
            affected_activities=affected_activities,
            schema_revisions=schema_summary,
            is_substantial=is_substantial,
            requires_reconsent=reconsent_flag,
            estimated_cost_usd=cost,
            narrative_summary=narrative,
        )

        # 5. Generate Automated Migration Directives
        directives = []
        if reconsent_flag:
            directives.append(
                MigrationDirective(
                    directive_id=f"dir-reconsent-{uuid.uuid4().hex[:6]}",
                    action="RECONSENT_GATE",
                    description=f"Mandate signed ICF v{amended_version_tag} before in-flight active subjects can proceed with newly scheduled visits.",
                    affected_cohort="ACTIVE",
                    target_version=amended_version_tag,
                )
            )

        directives.append(
            MigrationDirective(
                directive_id=f"dir-schema-upgrade-{uuid.uuid4().hex[:6]}",
                action="SCHEMA_UPGRADE",
                description=f"Dynamically project upcoming visits and eCRF forms under v{amended_version_tag} specifications.",
                affected_cohort="ACTIVE",
                target_version=amended_version_tag,
            )
        )

        directives.append(
            MigrationDirective(
                directive_id=f"dir-preserve-historical-{uuid.uuid4().hex[:6]}",
                action="PRESERVE_HISTORICAL",
                description=f"Permanently retain completed visits and clinical observations under v{base_version_tag} schema.",
                affected_cohort="ALL",
                target_version=base_version_tag,
            )
        )

        return SemanticDiffResponse(
            study_id=study_id,
            base_version_tag=base_version_tag,
            amended_version_tag=amended_version_tag,
            usdm_graph_diffs=usdm_diffs,
            soa_matrix_diffs=soa_diffs,
            eligibility_diffs=eligibility_diffs,
            ecrf_form_diffs=ecrf_form_diffs,
            impact_summary=impact_summary,
            migration_directives=directives,
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

    def _extract_named_map(
        self, items: list[dict[str, Any]] | dict[str, Any]
    ) -> dict[str, dict[str, Any]]:
        if isinstance(items, dict):
            return items
        res = {}
        for item in items:
            key = (
                item.get("id")
                or item.get("arm_id")
                or item.get("form_key")
                or item.get("name")
            )
            if key:
                res[key] = item
        return res

    def _extract_visits_map(self, payload: dict[str, Any]) -> dict[str, dict[str, Any]]:
        visits = {}
        # From direct visits list
        for v in payload.get("visits", []):
            vid = v.get("id") or v.get("visit_id") or v.get("name")
            if vid:
                visits[vid] = v

        # From arms/epochs hierarchy
        for arm in payload.get("arms", []):
            if isinstance(arm, dict):
                for v in arm.get("visits", []):
                    vid = v.get("id") or v.get("visit_id") or v.get("name")
                    if vid:
                        visits[vid] = v

        # Fallback to default fixture for CADENCE-101 demo if empty
        if not visits:
            visits = {
                "v1": {
                    "id": "v1",
                    "name": "Visit 1: Screening",
                    "spec": "Demographics, Eligibility",
                    "schedule": "Day -7",
                },
                "v2": {
                    "id": "v2",
                    "name": "Visit 2: Baseline",
                    "spec": "Vitals, ECG, Labs",
                    "schedule": "Day 1",
                },
                "v3": {
                    "id": "v3",
                    "name": "Visit 3: Treatment Cycle 1",
                    "spec": "Dosing, Safety Labs",
                    "schedule": "Day 14",
                },
            }
        return visits

    def _extract_activities_map(
        self, payload: dict[str, Any]
    ) -> dict[str, dict[str, Any]]:
        acts = {}
        for a in payload.get("activities", []) or payload.get("procedures", []):
            aid = a.get("id") or a.get("activity_id") or a.get("name")
            if aid:
                acts[aid] = a

        for arm in payload.get("arms", []):
            if isinstance(arm, dict):
                for v in arm.get("visits", []):
                    for a in v.get("activities", []):
                        aid = a.get("id") or a.get("activity_id") or a.get("name")
                        if aid:
                            acts[aid] = a

        if not acts:
            acts = {
                "act_chem": {
                    "id": "act_chem",
                    "name": "Standard Safety Chemistry",
                    "spec": "CBC + Chem Panel",
                    "schedule": "Bi-weekly",
                },
            }
        return acts

    def _resolve_protocol_payload(
        self, study_id: str, version_tag: str
    ) -> dict[str, Any]:
        """Resolves protocol payload from in-memory projections or builds CADENCE-101 amendment structure."""
        from apps.designer.db import (
            MOCK_STUDIES,
            MOCK_STUDY_PROJECTIONS_BY_VERSION,
        )

        key = f"{study_id}:{version_tag}"
        if key in MOCK_STUDY_PROJECTIONS_BY_VERSION:
            return MOCK_STUDY_PROJECTIONS_BY_VERSION[key]

        if (
            study_id in MOCK_STUDIES
            and MOCK_STUDIES[study_id].get("current_version") == version_tag
        ):
            return MOCK_STUDIES[study_id]

        # For version 2.0 / amendment demo payloads, generate amended structure with Visit 3.5 PK and Troponin biomarker
        if "2." in version_tag or "amendment" in version_tag.lower():
            return {
                "id": study_id,
                "name": f"CADENCE-101 Protocol (Amended v{version_tag})",
                "arms": [
                    {
                        "id": "arm_a",
                        "name": "Arm A: Active Dose",
                        "description": "Cohort: 100mg Daily",
                    }
                ],
                "visits": [
                    {
                        "id": "v1",
                        "name": "Visit 1: Screening",
                        "spec": "Demographics, Eligibility",
                        "schedule": "Day -7",
                    },
                    {
                        "id": "v2",
                        "name": "Visit 2: Baseline",
                        "spec": "Vitals, ECG, Labs",
                        "schedule": "Day 1",
                    },
                    {
                        "id": "v3",
                        "name": "Visit 3: Treatment Cycle 1",
                        "spec": "Dosing, Safety Labs, PK Blood Draw",
                        "schedule": "Day 14",
                        "delta_note": "Added PK Blood Draw form and expanded safety lab range criteria.",
                    },
                    {
                        "id": "v4",
                        "name": "Visit 3.5: Interim PK Assessment",
                        "spec": "Pharmacokinetics, Biomarkers",
                        "schedule": "Day 21",
                        "delta_note": "New mid-cycle pharmacokinetic visit added in Amendment 2.0.",
                    },
                ],
                "activities": [
                    {
                        "id": "act_chem",
                        "name": "Standard Safety Chemistry",
                        "spec": "Assay: CBC + Chem Panel + Biomarkers",
                        "schedule": "Bi-weekly",
                        "delta_note": "Added high-sensitivity troponin biomarker requirement.",
                    },
                    {
                        "id": "act_pk",
                        "name": "PK Blood Draw",
                        "spec": "Pharmacokinetics Plasma Assay",
                        "schedule": "Visit 3, Visit 3.5",
                        "delta_note": "Added PK blood draw procedure.",
                    },
                ],
                "eligibilityCriteria": [
                    {"id": "crit_01", "text": "Age >= 18 and Age <= 75"},
                    {"id": "crit_02", "text": "Confirmed solid tumor diagnosis"},
                    {"id": "crit_03", "text": "Signed informed consent (v2.0)"},
                ],
                "forms": [
                    {"id": "f_demo", "form_key": "DEMO", "name": "Demographics"},
                    {
                        "id": "f_pk",
                        "form_key": "PK_ASSAY",
                        "name": "Pharmacokinetics Blood Draw",
                    },
                ],
            }

        # Baseline v1.0 structure
        return {
            "id": study_id,
            "name": "CADENCE-101 Baseline Protocol",
            "arms": [
                {
                    "id": "arm_a",
                    "name": "Arm A: Active Dose",
                    "description": "Cohort: 100mg Daily",
                }
            ],
            "visits": [
                {
                    "id": "v1",
                    "name": "Visit 1: Screening",
                    "spec": "Demographics, Eligibility",
                    "schedule": "Day -7",
                },
                {
                    "id": "v2",
                    "name": "Visit 2: Baseline",
                    "spec": "Vitals, ECG, Labs",
                    "schedule": "Day 1",
                },
                {
                    "id": "v3",
                    "name": "Visit 3: Treatment Cycle 1",
                    "spec": "Dosing, Safety Labs",
                    "schedule": "Day 14",
                },
            ],
            "activities": [
                {
                    "id": "act_chem",
                    "name": "Standard Safety Chemistry",
                    "spec": "Assay: CBC + Chem Panel",
                    "schedule": "Bi-weekly",
                }
            ],
            "eligibilityCriteria": [
                {"id": "crit_01", "text": "Age >= 18"},
                {"id": "crit_02", "text": "Confirmed solid tumor diagnosis"},
            ],
            "forms": [
                {"id": "f_demo", "form_key": "DEMO", "name": "Demographics"},
            ],
        }
