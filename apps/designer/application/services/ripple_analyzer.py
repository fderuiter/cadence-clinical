"""Protocol Amendment Ripple-Effect Analyzer.

Evaluates semantic and structural graph deltas, Schedule of Activities (SoA) matrices,
and narrative text differences across protocol amendments to calculate operational scope changes,
enforce subject re-consent gating, and dispatch multi-domain operational tickets.

Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
"""

import datetime as dt
import logging
import uuid
from typing import Any

from apps.designer.application.services.branch_manager import ProtocolBranchManager
from apps.designer.domain.cdisc.branch_models import (
    MigrationDirective,
)
from apps.designer.domain.cdisc.ripple_models import (
    DataCaptureEcrfImpact,
    DomainQueue,
    NarrativeDelta,
    OperationalTicketBlueprint,
    ProtocolImpactAssessment,
    ReConsentGatingPlan,
    RegulatoryComplianceImpact,
    SubjectManagementRtsmImpact,
)

logger = logging.getLogger(__name__)

# Keywords indicating safety profile changes or regulatory risks in narrative text
SAFETY_RISK_KEYWORDS = [
    "adverse reaction",
    "adverse event",
    "black box",
    "cardiotoxicity",
    "contraindication",
    "discontinuation criteria",
    "dose limiting toxicity",
    "dose reduction",
    "hepatotoxicity",
    "increased risk",
    "risk profile",
    "safety alert",
    "safety warning",
    "serious adverse event",
    "toxicity",
    "warning",
]

# Keywords indicating dosing and RTSM modifications in narrative text
DOSING_KEYWORDS = [
    "cohort expansion",
    "dose escalation",
    "dose level",
    "dose reduction",
    "dosing regimen",
    "dosing schedule",
    "investigational product",
    "kit dispensation",
    "loading dose",
    "maintenance dose",
    "randomization ratio",
    "stratification",
    "titration",
]


class ProtocolAmendmentRippleAnalyzer:
    """Automated impact analysis engine for protocol amendments.

    Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
    """

    def __init__(self, branch_manager: ProtocolBranchManager | None = None) -> None:
        self.branch_manager = branch_manager or ProtocolBranchManager()

    def analyze_amendment_impact(
        self,
        study_id: str,
        base_version_tag: str = "1.0.0",
        amended_version_tag: str = "2.0.0",
        amendment_type: str = "minor",
        requires_reconsent_override: bool | None = None,
        base_payload: dict[str, Any] | None = None,
        draft_payload: dict[str, Any] | None = None,
        active_subject_ids: list[str] | None = None,
    ) -> ProtocolImpactAssessment:
        """Analyze ripple-effect impact across graph, SoA, and narrative layers.

        Args:
            study_id: Protocol study identifier.
            base_version_tag: Baseline protocol version tag.
            amended_version_tag: Amended protocol version tag.
            amendment_type: "major" or "minor".
            requires_reconsent_override: Optional explicit re-consent flag override.
            base_payload: Optional explicit baseline protocol dictionary.
            draft_payload: Optional explicit amended protocol dictionary.
            active_subject_ids: Optional list of active subject IDs to evaluate for re-consent gating.

        Returns:
            Structured ProtocolImpactAssessment instance.
        """
        assessment_id = f"pia-{uuid.uuid4().hex[:8]}"
        now_iso = dt.datetime.now(dt.UTC).isoformat()

        # 1. Resolve payloads
        base_data = base_payload or self.branch_manager._resolve_protocol_payload(
            study_id, base_version_tag
        )
        draft_data = draft_payload or self.branch_manager._resolve_protocol_payload(
            study_id, amended_version_tag
        )

        # 2. Compute multi-layer semantic diff using branch manager
        diff_res = self.branch_manager.compute_semantic_diff(
            study_id=study_id,
            base_version_tag=base_version_tag,
            amended_version_tag=amended_version_tag,
            base_payload=base_data,
            draft_payload=draft_data,
            requires_reconsent=bool(requires_reconsent_override),
        )

        graph_diffs = diff_res.usdm_graph_diffs
        soa_diffs = diff_res.soa_matrix_diffs

        # 3. Evaluate narrative deltas and scan for clinical/safety triggers
        narrative_deltas = self._evaluate_narrative_deltas(base_data, draft_data)
        has_safety_narrative_trigger = any(
            nd.safety_risk_impact for nd in narrative_deltas
        )

        # 4. Evaluate SoA timing window adjustments and dosing adjustments
        visit_window_adjustments = self._extract_visit_window_adjustments(
            base_data, draft_data
        )
        dosing_changes = self._extract_dosing_changes(
            base_data, draft_data, narrative_deltas
        )

        # 5. Determine substantiality and re-consent gating
        is_major = (
            "major" in amendment_type.lower()
            or base_version_tag.split(".")[0] != amended_version_tag.split(".")[0]
        )

        added_visits = [
            d.name
            for d in soa_diffs
            if d.change_type == "ADDED" and d.entity_type == "Encounter"
        ]
        modified_visits = [
            d.name
            for d in soa_diffs
            if d.change_type == "MODIFIED" and d.entity_type == "Encounter"
        ]
        removed_visits = [
            d.name
            for d in soa_diffs
            if d.change_type == "REMOVED" and d.entity_type == "Encounter"
        ]

        added_activities = [
            d.name
            for d in soa_diffs
            if d.change_type == "ADDED" and d.entity_type == "Activity"
        ]

        added_arms = [
            d.name
            for d in graph_diffs
            if d.change_type == "ADDED" and d.entity_type == "Arm"
        ]
        modified_arms = [
            d.name
            for d in graph_diffs
            if d.change_type == "MODIFIED" and d.entity_type == "Arm"
        ]
        removed_arms = [
            d.name
            for d in graph_diffs
            if d.change_type == "REMOVED" and d.entity_type == "Arm"
        ]

        added_forms = [
            d.name for d in diff_res.ecrf_form_diffs if d.change_type == "ADDED"
        ]
        modified_forms = [
            d.name for d in diff_res.ecrf_form_diffs if d.change_type == "MODIFIED"
        ]
        removed_forms = [
            d.name for d in diff_res.ecrf_form_diffs if d.change_type == "REMOVED"
        ]

        is_substantial = (
            is_major
            or bool(requires_reconsent_override)
            or has_safety_narrative_trigger
            or len(added_visits) > 0
            or len(removed_visits) > 0
            or len(added_activities) > 0
            or len(added_arms) > 0
            or len(removed_arms) > 0
            or len(diff_res.eligibility_diffs) > 0
        )

        # Re-consent requirement logic
        if requires_reconsent_override is not None:
            requires_reconsent = requires_reconsent_override
        else:
            requires_reconsent = (
                is_major
                or has_safety_narrative_trigger
                or (
                    is_substantial
                    and (
                        len(added_visits) > 0
                        or len(added_activities) > 0
                        or any(
                            d.change_type in ("ADDED", "MODIFIED")
                            for d in diff_res.eligibility_diffs
                        )
                    )
                )
            )

        # Safety risk classification
        if has_safety_narrative_trigger and is_major:
            safety_risk_level = "CRITICAL"
        elif has_safety_narrative_trigger or is_major or len(added_arms) > 0:
            safety_risk_level = "HIGH"
        elif (
            len(added_visits) > 0
            or len(added_activities) > 0
            or len(visit_window_adjustments) > 0
        ):
            safety_risk_level = "MEDIUM"
        else:
            safety_risk_level = "LOW"

        # Resolve active subject IDs for gating
        flagged_subjects = self._resolve_active_subjects_for_gating(
            study_id=study_id,
            active_subject_ids=active_subject_ids,
            requires_reconsent=requires_reconsent,
        )

        # 6. Build Domain Manifest: DATA_CAPTURE_ECRF
        ecrf_affected_visits = sorted(list(set(added_visits + modified_visits)))
        new_cdash_fields = self._generate_cdash_fields(added_forms, added_activities)
        rule_mods_count = (
            len(added_forms) * 2 + len(modified_forms) + len(added_activities)
        )
        ecrf_build_hours = round(
            len(added_forms) * 8.0
            + len(modified_forms) * 4.0
            + len(added_visits) * 3.0
            + len(added_activities) * 2.5
            + rule_mods_count * 1.0,
            1,
        )

        ecrf_action_items = [
            f"Review updated Schedule of Activities for {len(ecrf_affected_visits)} affected visit(s).",
        ]
        if added_forms:
            ecrf_action_items.append(
                f"Design and validate {len(added_forms)} new CDASH eCRF form(s): {', '.join(added_forms)}."
            )
        if modified_forms:
            ecrf_action_items.append(
                f"Update field specifications and layout on {len(modified_forms)} existing form(s): {', '.join(modified_forms)}."
            )
        if rule_mods_count > 0:
            ecrf_action_items.append(
                f"Configure and unit-test {rule_mods_count} dynamic edit checks and cross-form validation rules."
            )
        ecrf_action_items.append(
            f"Execute User Acceptance Testing (UAT) for Protocol v{amended_version_tag} EDC deployment."
        )

        data_capture_ecrf = DataCaptureEcrfImpact(
            affected_forms_count=len(added_forms)
            + len(modified_forms)
            + len(removed_forms),
            added_forms=added_forms,
            modified_forms=modified_forms,
            removed_forms=removed_forms,
            affected_visits=ecrf_affected_visits,
            new_cdash_fields=new_cdash_fields,
            rule_modifications_count=rule_mods_count,
            estimated_build_hours=ecrf_build_hours,
            action_items=ecrf_action_items,
        )

        # 7. Build Domain Manifest: SUBJECT_MANAGEMENT_RTSM
        rtsm_affected_arms = sorted(
            list(set(added_arms + modified_arms + removed_arms))
        )
        requires_kit_reallocation = (
            len(added_arms) > 0 or len(added_visits) > 0 or len(dosing_changes) > 0
        )
        randomization_ratio_changed = len(added_arms) > 0 or len(removed_arms) > 0

        rtsm_action_items = [
            f"Verify RTSM randomization configuration for study arms: {', '.join(rtsm_affected_arms) if rtsm_affected_arms else 'Standard Cohorts'}.",
        ]
        if visit_window_adjustments:
            rtsm_action_items.append(
                f"Update EDC/RTSM scheduling engine with {len(visit_window_adjustments)} revised visit window calculation parameters."
            )
        if requires_kit_reallocation:
            rtsm_action_items.append(
                "Recalculate investigational product supply forecast and adjust depot buffer thresholds."
            )
        if dosing_changes:
            rtsm_action_items.append(
                f"Implement cohort dose tier modifications: {', '.join(d.get('description', 'regimen update') for d in dosing_changes)}."
            )
        rtsm_action_items.append(
            f"Issue updated dispensation and IP handling guidelines to site pharmacies for v{amended_version_tag}."
        )

        subject_management_rtsm = SubjectManagementRtsmImpact(
            cohort_adjustments_count=len(rtsm_affected_arms),
            affected_arms=rtsm_affected_arms,
            dosing_changes=dosing_changes,
            visit_window_adjustments=visit_window_adjustments,
            requires_kit_reallocation=requires_kit_reallocation,
            randomization_ratio_changed=randomization_ratio_changed,
            action_items=rtsm_action_items,
        )

        # 8. Build Domain Manifest: REGULATORY_COMPLIANCE
        submission_type = (
            "FULL_COMMITTEE"
            if is_substantial and safety_risk_level in ("HIGH", "CRITICAL")
            else ("EXPEDITED" if is_substantial else "NOTIFICATION")
        )
        affected_cohorts = ["ACTIVE"] if requires_reconsent else ["ALL"]
        if is_substantial:
            affected_cohorts.extend(["SCREENING", "ENROLLED"])
        affected_cohorts = sorted(list(set(affected_cohorts)))

        reg_action_items = [
            f"Prepare and file {submission_type} dossier with Institutional Review Boards (IRB) / Ethics Committees (IEC).",
            f"Publish updated Informed Consent Form (ICF) v{amended_version_tag} within eConsent authoring module.",
        ]
        if requires_reconsent:
            reg_action_items.append(
                f"Activate automated in-flight re-consent gating for {len(flagged_subjects)} active subject(s) in EDC runtime."
            )
        reg_action_items.append(
            "Issue protocol amendment notification briefing to Site Principal Investigators and CRA Monitors."
        )

        regulatory_compliance = RegulatoryComplianceImpact(
            safety_risk_level=safety_risk_level,
            requires_reconsent=requires_reconsent,
            is_substantial_amendment=is_substantial,
            icf_version_upgrade=f"v{amended_version_tag}",
            irb_iec_submission_type=submission_type,
            affected_subject_cohorts=affected_cohorts,
            flagged_active_subjects=flagged_subjects,
            action_items=reg_action_items,
        )

        # 9. Build Re-Consent Gating Plan
        gating_justification = (
            f"Mandatory in-flight subject re-consent is mandated for Protocol Amendment v{amended_version_tag} "
            f"due to {safety_risk_level} safety risk classification, changes to eligibility criteria, and addition of new clinical procedures."
            if requires_reconsent
            else f"Administrative / minor changes in v{amended_version_tag}; active in-flight subject re-consent is not mandated."
        )

        reconsent_gating_plan = ReConsentGatingPlan(
            gating_mandated=requires_reconsent,
            affected_cohort="ACTIVE",
            flagged_subject_count=len(flagged_subjects),
            flagged_subject_ids=flagged_subjects,
            justification=gating_justification,
        )

        # 10. Generate Operational Ticket Blueprints
        operational_tickets = self._generate_operational_tickets(
            study_id=study_id,
            base_version_tag=base_version_tag,
            amended_version_tag=amended_version_tag,
            data_capture_ecrf=data_capture_ecrf,
            subject_management_rtsm=subject_management_rtsm,
            regulatory_compliance=regulatory_compliance,
            reconsent_gating_plan=reconsent_gating_plan,
        )

        # 11. Migration Directives
        migration_directives = diff_res.migration_directives
        if requires_reconsent and not any(
            d.action == "RECONSENT_GATE" for d in migration_directives
        ):
            migration_directives.insert(
                0,
                MigrationDirective(
                    directive_id=f"dir-reconsent-{uuid.uuid4().hex[:6]}",
                    action="RECONSENT_GATE",
                    description=f"Mandate signed ICF v{amended_version_tag} before in-flight active subjects can proceed with newly scheduled visits.",
                    affected_cohort="ACTIVE",
                    target_version=amended_version_tag,
                ),
            )

        # 12. Executive summary narrative
        executive_summary = (
            f"Protocol Amendment Ripple-Effect Assessment: {study_id} (v{base_version_tag} -> v{amended_version_tag}, {amendment_type.upper()}). "
            f"Scope impacts {len(ecrf_affected_visits)} visit(s), {len(added_forms) + len(modified_forms)} eCRF form(s), "
            f"and {len(rtsm_affected_arms)} study arm(s). Overall patient burden delta is {diff_res.impact_summary.burden_delta:+.1f}. "
            f"Safety risk classified as {safety_risk_level}. "
            f"{'In-flight subject re-consent gating is MANDATED for ' + str(len(flagged_subjects)) + ' active subject(s).' if requires_reconsent else 'Administrative update; in-flight re-consent gating is not required.'} "
            f"Generated {len(operational_tickets)} domain-routed operational tickets."
        )

        return ProtocolImpactAssessment(
            assessment_id=assessment_id,
            study_id=study_id,
            base_version=base_version_tag,
            amended_version=amended_version_tag,
            amendment_type=amendment_type,
            is_substantial=is_substantial,
            requires_reconsent=requires_reconsent,
            patient_burden_delta=diff_res.impact_summary.burden_delta,
            estimated_cost_usd=diff_res.impact_summary.estimated_cost_usd,
            executive_summary=executive_summary,
            graph_deltas=graph_diffs,
            soa_deltas=soa_diffs,
            narrative_deltas=narrative_deltas,
            data_capture_ecrf=data_capture_ecrf,
            subject_management_rtsm=subject_management_rtsm,
            regulatory_compliance=regulatory_compliance,
            reconsent_gating_plan=reconsent_gating_plan,
            operational_tickets=operational_tickets,
            migration_directives=migration_directives,
            created_at=now_iso,
        )

    def _evaluate_narrative_deltas(
        self, base_data: dict[str, Any], draft_data: dict[str, Any]
    ) -> list[NarrativeDelta]:
        """Compares protocol narrative text sections and scans for safety/dosing risk triggers."""
        deltas: list[NarrativeDelta] = []

        base_narrative = base_data.get("narrative") or base_data.get("sections") or {}
        draft_narrative = (
            draft_data.get("narrative") or draft_data.get("sections") or {}
        )

        # Standard canonical section keys if dictionary or list
        base_map = self._normalize_narrative_map(base_narrative)
        draft_map = self._normalize_narrative_map(draft_narrative)

        all_keys = sorted(list(set(base_map.keys()) | set(draft_map.keys())))

        for key in all_keys:
            b_sec = base_map.get(key)
            d_sec = draft_map.get(key)

            if b_sec is None and d_sec is not None:
                title = d_sec.get("title", key)
                text_content = d_sec.get("text") or d_sec.get("content", "")
                has_safety = self._has_safety_keywords(text_content)
                deltas.append(
                    NarrativeDelta(
                        section_id=key,
                        section_title=title,
                        change_type="ADDED",
                        old_text=None,
                        new_text=text_content,
                        delta_summary=f"Section '{title}' added in amendment.",
                        safety_risk_impact=has_safety,
                    )
                )
            elif d_sec is None and b_sec is not None:
                title = b_sec.get("title", key)
                text_content = b_sec.get("text") or b_sec.get("content", "")
                deltas.append(
                    NarrativeDelta(
                        section_id=key,
                        section_title=title,
                        change_type="REMOVED",
                        old_text=text_content,
                        new_text=None,
                        delta_summary=f"Section '{title}' removed.",
                        safety_risk_impact=False,
                    )
                )
            elif b_sec != d_sec:
                title = d_sec.get("title", key) if d_sec else key
                b_text = b_sec.get("text") or b_sec.get("content", "") if b_sec else ""
                d_text = d_sec.get("text") or d_sec.get("content", "") if d_sec else ""
                has_safety = self._has_safety_keywords(
                    d_text
                ) or self._has_safety_keywords(b_text)
                deltas.append(
                    NarrativeDelta(
                        section_id=key,
                        section_title=title,
                        change_type="MODIFIED",
                        old_text=b_text,
                        new_text=d_text,
                        delta_summary=f"Section '{title}' text content updated.",
                        safety_risk_impact=has_safety,
                    )
                )

        return deltas

    def _normalize_narrative_map(
        self, narrative_obj: dict[str, Any] | list[dict[str, Any]]
    ) -> dict[str, dict[str, Any]]:
        res = {}
        if isinstance(narrative_obj, dict):
            for k, v in narrative_obj.items():
                if isinstance(v, dict):
                    res[k] = v
                elif isinstance(v, str):
                    res[k] = {"title": k.replace("_", " ").title(), "text": v}
        elif isinstance(narrative_obj, list):
            for i, item in enumerate(narrative_obj):
                sec_id = item.get("id") or item.get("section_id") or f"sec_{i}"
                res[sec_id] = item
        return res

    def _has_safety_keywords(self, text: str) -> bool:
        if not text:
            return False
        text_lower = text.lower()
        return any(kw in text_lower for kw in SAFETY_RISK_KEYWORDS)

    def _extract_visit_window_adjustments(
        self, base_data: dict[str, Any], draft_data: dict[str, Any]
    ) -> list[dict[str, Any]]:
        adjustments = []
        base_visits = self.branch_manager._extract_visits_map(base_data)
        draft_visits = self.branch_manager._extract_visits_map(draft_data)

        for vid, d_vis in draft_visits.items():
            if vid in base_visits:
                b_vis = base_visits[vid]
                b_win = b_vis.get("window") or b_vis.get("timing_window")
                d_win = d_vis.get("window") or d_vis.get("timing_window")
                if b_win != d_win and d_win is not None:
                    adjustments.append(
                        {
                            "visit_id": vid,
                            "visit_name": d_vis.get("name", vid),
                            "baseline_window": b_win or "Standard (+/- 3 days)",
                            "amended_window": d_win,
                        }
                    )
            elif d_vis.get("window") or d_vis.get("timing_window"):
                adjustments.append(
                    {
                        "visit_id": vid,
                        "visit_name": d_vis.get("name", vid),
                        "baseline_window": "N/A (New Visit)",
                        "amended_window": d_vis.get("window")
                        or d_vis.get("timing_window"),
                    }
                )
        return adjustments

    def _extract_dosing_changes(
        self,
        base_data: dict[str, Any],
        draft_data: dict[str, Any],
        narrative_deltas: list[NarrativeDelta],
    ) -> list[dict[str, Any]]:
        changes = []
        base_arms = self.branch_manager._extract_named_map(base_data.get("arms", []))
        draft_arms = self.branch_manager._extract_named_map(draft_data.get("arms", []))

        for aid, d_arm in draft_arms.items():
            if aid in base_arms:
                b_arm = base_arms[aid]
                if b_arm.get("description") != d_arm.get("description") or b_arm.get(
                    "dose"
                ) != d_arm.get("dose"):
                    changes.append(
                        {
                            "arm_id": aid,
                            "arm_name": d_arm.get("name", aid),
                            "baseline_regimen": b_arm.get("description")
                            or b_arm.get("dose", "Standard"),
                            "amended_regimen": d_arm.get("description")
                            or d_arm.get("dose", "Updated"),
                        }
                    )
            else:
                changes.append(
                    {
                        "arm_id": aid,
                        "arm_name": d_arm.get("name", aid),
                        "baseline_regimen": "N/A (New Arm)",
                        "amended_regimen": d_arm.get("description")
                        or d_arm.get("dose", "New Cohort"),
                    }
                )

        # Scan narrative for dosing changes if no structural arm change was captured
        if not changes:
            for nd in narrative_deltas:
                if nd.new_text and any(
                    kw in nd.new_text.lower() for kw in DOSING_KEYWORDS
                ):
                    changes.append(
                        {
                            "arm_id": nd.section_id,
                            "arm_name": nd.section_title,
                            "baseline_regimen": "Previous Regimen",
                            "amended_regimen": nd.delta_summary,
                        }
                    )
        return changes

    def _resolve_active_subjects_for_gating(
        self,
        study_id: str,
        active_subject_ids: list[str] | None,
        requires_reconsent: bool,
    ) -> list[str]:
        if not requires_reconsent:
            return []
        if active_subject_ids is not None:
            return sorted(active_subject_ids)

        # Default canonical active cohort subjects for mock/demo studies
        return [f"{study_id}-SUBJ-001", f"{study_id}-SUBJ-002", f"{study_id}-SUBJ-003"]

    def _generate_cdash_fields(
        self, added_forms: list[str], added_activities: list[str]
    ) -> list[str]:
        fields = []
        for form in added_forms:
            form_lower = form.lower()
            if "pk" in form_lower or "pharmacokinetic" in form_lower:
                fields.extend(["PKDAT", "PKTIM", "PKTPT", "PKORRES", "PKSTRESC"])
            elif "demo" in form_lower:
                fields.extend(["BRTHDAT", "SEX", "RACE", "ETHNIC"])
            elif "vital" in form_lower or "vs" in form_lower:
                fields.extend(["SYSBP", "DIABP", "PULSE", "TEMP", "RESP"])
            elif "lab" in form_lower or "chem" in form_lower:
                fields.extend(["LBTESTCD", "LBORRES", "LBSTRESC", "LBNRIND"])
            else:
                prefix = form[:4].upper()
                fields.extend([f"{prefix}DAT", f"{prefix}STAT", f"{prefix}VAL"])

        for act in added_activities:
            act_lower = act.lower()
            if "troponin" in act_lower or "biomarker" in act_lower:
                fields.extend(["TROPONIN_I", "TROPONIN_T", "TROP_UNIT", "TROP_EVAL"])
            elif "ecg" in act_lower:
                fields.extend(["EGINT", "QTCF", "EGEVAL"])

        return sorted(list(set(fields)))

    def _generate_operational_tickets(
        self,
        study_id: str,
        base_version_tag: str,
        amended_version_tag: str,
        data_capture_ecrf: DataCaptureEcrfImpact,
        subject_management_rtsm: SubjectManagementRtsmImpact,
        regulatory_compliance: RegulatoryComplianceImpact,
        reconsent_gating_plan: ReConsentGatingPlan,
    ) -> list[OperationalTicketBlueprint]:
        """Generates domain-routed operational ticket blueprints with pre-filled action plans."""
        tickets: list[OperationalTicketBlueprint] = []

        # 1. Ticket for DATA_CAPTURE_ECRF
        ecrf_ticket = OperationalTicketBlueprint(
            domain_queue=DomainQueue.DATA_CAPTURE_ECRF,
            title=f"[eCRF Build] Protocol Amendment v{amended_version_tag} Form & Visit Revisions ({study_id})",
            description=(
                f"Protocol Amendment from v{base_version_tag} to v{amended_version_tag} introduces data capture updates:\n"
                f"- Affected Visits: {', '.join(data_capture_ecrf.affected_visits) or 'None'}\n"
                f"- New Forms: {', '.join(data_capture_ecrf.added_forms) or 'None'}\n"
                f"- Modified Forms: {', '.join(data_capture_ecrf.modified_forms) or 'None'}\n"
                f"- New CDASH Fields: {', '.join(data_capture_ecrf.new_cdash_fields) or 'None'}\n"
                f"- Estimated Build Effort: {data_capture_ecrf.estimated_build_hours} hours."
            ),
            category="CHANGE_REQUEST",
            priority="HIGH",
            gxp_severity="MAJOR",
            assignee_role="data_management_lead",
            action_plan=data_capture_ecrf.action_items,
            due_date_offset_days=14,
            context_payload={
                "study_id": study_id,
                "base_version": base_version_tag,
                "amended_version": amended_version_tag,
                "domain_queue": DomainQueue.DATA_CAPTURE_ECRF.value,
                "added_forms": data_capture_ecrf.added_forms,
                "affected_visits": data_capture_ecrf.affected_visits,
                "estimated_hours": data_capture_ecrf.estimated_build_hours,
            },
        )
        tickets.append(ecrf_ticket)

        # 2. Ticket for SUBJECT_MANAGEMENT_RTSM
        rtsm_priority = (
            "HIGH" if subject_management_rtsm.cohort_adjustments_count > 0 else "MEDIUM"
        )
        rtsm_ticket = OperationalTicketBlueprint(
            domain_queue=DomainQueue.SUBJECT_MANAGEMENT_RTSM,
            title=f"[RTSM & Supply] Protocol Amendment v{amended_version_tag} Cohort & Visit Windows ({study_id})",
            description=(
                f"RTSM and supply chain updates required for Protocol Amendment v{amended_version_tag}:\n"
                f"- Affected Study Arms: {', '.join(subject_management_rtsm.affected_arms) or 'None'}\n"
                f"- Visit Window Adjustments: {len(subject_management_rtsm.visit_window_adjustments)} modified schedule window(s)\n"
                f"- Investigational Product Reallocation Required: {'YES' if subject_management_rtsm.requires_kit_reallocation else 'NO'}\n"
                f"- Randomization Ratio Revision: {'YES' if subject_management_rtsm.randomization_ratio_changed else 'NO'}."
            ),
            category="SITE_OPERATIONS",
            priority=rtsm_priority,
            gxp_severity="MAJOR",
            assignee_role="rtsm_lead",
            action_plan=subject_management_rtsm.action_items,
            due_date_offset_days=10,
            context_payload={
                "study_id": study_id,
                "base_version": base_version_tag,
                "amended_version": amended_version_tag,
                "domain_queue": DomainQueue.SUBJECT_MANAGEMENT_RTSM.value,
                "affected_arms": subject_management_rtsm.affected_arms,
                "requires_kit_reallocation": subject_management_rtsm.requires_kit_reallocation,
            },
        )
        tickets.append(rtsm_ticket)

        # 3. Ticket for REGULATORY_COMPLIANCE
        reg_priority = (
            "CRITICAL" if regulatory_compliance.requires_reconsent else "MEDIUM"
        )
        reg_severity = (
            "CRITICAL" if regulatory_compliance.requires_reconsent else "MAJOR"
        )
        reg_ticket = OperationalTicketBlueprint(
            domain_queue=DomainQueue.REGULATORY_COMPLIANCE,
            title=f"[Regulatory & Ethics] Protocol Amendment v{amended_version_tag} Re-Consent Gating & IRB Submission ({study_id})",
            description=(
                f"Regulatory and Ethics Committee compliance package for Protocol Amendment v{amended_version_tag}:\n"
                f"- Safety Risk Classification: {regulatory_compliance.safety_risk_level}\n"
                f"- IRB/IEC Submission Pathway: {regulatory_compliance.irb_iec_submission_type}\n"
                f"- In-Flight Re-Consent Gating: {'MANDATED' if regulatory_compliance.requires_reconsent else 'NOT REQUIRED'}\n"
                f"- Flagged Active Subjects for Gating: {reconsent_gating_plan.flagged_subject_count} active subject(s)\n"
                f"- Rationale: {reconsent_gating_plan.justification}"
            ),
            category="REGULATORY_ETMF",
            priority=reg_priority,
            gxp_severity=reg_severity,
            assignee_role="regulatory_affairs_lead",
            action_plan=regulatory_compliance.action_items,
            due_date_offset_days=7 if regulatory_compliance.requires_reconsent else 21,
            context_payload={
                "study_id": study_id,
                "base_version": base_version_tag,
                "amended_version": amended_version_tag,
                "domain_queue": DomainQueue.REGULATORY_COMPLIANCE.value,
                "safety_risk_level": regulatory_compliance.safety_risk_level,
                "requires_reconsent": regulatory_compliance.requires_reconsent,
                "flagged_active_subjects": reconsent_gating_plan.flagged_subject_ids,
            },
        )
        tickets.append(reg_ticket)

        return tickets
