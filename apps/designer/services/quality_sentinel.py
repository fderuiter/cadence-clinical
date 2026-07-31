# ruff: noqa: E402
"""Deterministic Protocol Quality Sentinel and site feasibility analyzer service.

Requirements: PRD-SYS-001
"""

import os
import re
import sys
from typing import Any, Dict

# Inject 'core-models' path into sys.path
_core_models_path = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "..", "..", "packages", "core-models")
)
if _core_models_path not in sys.path:
    sys.path.insert(0, _core_models_path)


from cdisc.sentinel_models import (
    AmendmentImpactReport,
    AttritionStep,
    BurdenTraceItem,
    BurdenTraceReport,
    FeasibilityReport,
    ProtocolQualityScore,
    QualityRuleFinding,
    ReadabilityReport,
)

# Try importing the eligibility evaluation tools
try:
    from eligibility.evaluator import evaluate_node
    from eligibility.models import ExpressionNode
except ImportError as e:
    print("IMPORT ERROR IN SENTINEL:", e)
    evaluate_node = None
    ExpressionNode = None


PATIENT_FIXTURES = [
    {
        "id": "PT_01",
        "name": "Alice",
        "age": 25,
        "gender": "F",
        "systolic_bp": 120,
        "diastolic_bp": 80,
        "has_diabetes": False,
        "has_liver_disease": False,
        "pregnancy_status": False,
    },
    {
        "id": "PT_02",
        "name": "Bob",
        "age": 65,
        "gender": "M",
        "systolic_bp": 145,
        "diastolic_bp": 95,
        "has_diabetes": True,
        "has_liver_disease": False,
        "pregnancy_status": False,
    },
    {
        "id": "PT_03",
        "name": "Charlie",
        "age": 16,  # Underage
        "gender": "M",
        "systolic_bp": 115,
        "diastolic_bp": 75,
        "has_diabetes": False,
        "has_liver_disease": False,
        "pregnancy_status": False,
    },
    {
        "id": "PT_04",
        "name": "Diana",
        "age": 34,
        "gender": "F",
        "systolic_bp": 118,
        "diastolic_bp": 78,
        "has_diabetes": False,
        "has_liver_disease": True,  # Liver disease
        "pregnancy_status": True,  # Pregnant
    },
    {
        "id": "PT_05",
        "name": "Ethan",
        "age": 52,
        "gender": "M",
        "systolic_bp": 135,
        "diastolic_bp": 85,
        "has_diabetes": False,
        "has_liver_disease": False,
        "pregnancy_status": False,
    },
]


def make_patient_context(patient: Dict[str, Any]) -> Dict[str, Any]:
    """Dynamically construct standard and namespaced keys from patient attributes."""
    context = {}
    for k, v in patient.items():
        context[k] = v
    context["eCRF.DM.AGE"] = patient.get("age")
    context["AGE"] = patient.get("age")
    context["eCRF.DM.SEX"] = patient.get("gender")
    context["SEX"] = patient.get("gender")
    context["GENDER"] = patient.get("gender")
    context["eCRF.VS.SYSBP"] = patient.get("systolic_bp")
    context["VSSBP"] = patient.get("systolic_bp")
    context["SYSBP"] = patient.get("systolic_bp")
    context["eCRF.VS.DIABP"] = patient.get("diastolic_bp")
    context["DIABP"] = patient.get("diastolic_bp")
    context["eCRF.MH.DIABETES"] = patient.get("has_diabetes")
    context["DIABETES"] = patient.get("has_diabetes")
    context["eCRF.MH.LIVER_DISEASE"] = patient.get("has_liver_disease")
    context["LIVER_DISEASE"] = patient.get("has_liver_disease")
    context["eCRF.MH.PREGNANT"] = patient.get("pregnancy_status")
    context["PREGNANT"] = patient.get("pregnancy_status")
    return context


def count_syllables_word(word: str) -> int:
    """Heuristic syllable counter for deterministic readability metrics."""
    word = word.lower().strip(".:,;?!\"'()[]{}")
    if not word:
        return 0
    vowels = "aeiouy"
    count = 0
    prev_char_is_vowel = False
    for char in word:
        is_vowel = char in vowels
        if is_vowel and not prev_char_is_vowel:
            count += 1
        prev_char_is_vowel = is_vowel
    if word.endswith("e"):
        count -= 1
    if word.endswith("le") and len(word) > 2 and word[-3] not in vowels:
        count += 1
    if count <= 0:
        count = 1
    return count


class ProtocolQualitySentinel:
    """Quality evaluation service auditing USDM study specifications against regulatory rules.

    Requirements: PRD-SYS-001
    """

    def evaluate_protocol_quality(
        self, study_payload: Dict[str, Any]
    ) -> ProtocolQualityScore:
        """Audit authored protocol payload and compute quality score and burden index.

        Args:
            study_payload: USDM Study dictionary structure.

        Returns:
            ProtocolQualityScore summary report.
        """
        study_id = str(study_payload.get("id", "study_unnamed"))
        findings: list[QualityRuleFinding] = []

        # Rule 1: Check Study Design presence
        designs = (
            study_payload.get("studyDesigns")
            or study_payload.get("study_designs")
            or []
        )
        if not designs:
            findings.append(
                QualityRuleFinding(
                    rule_id="SENTINEL_STRUCT_01",
                    severity="ERROR",
                    category="Structural",
                    message="Protocol is missing required study design structure.",
                    target_node_id=study_id,
                )
            )

        # Rule 2: Check Eligibility Criteria presence
        criteria = (
            study_payload.get("eligibilityCriteria")
            or study_payload.get("eligibility_criteria")
            or []
        )
        if not criteria:
            findings.append(
                QualityRuleFinding(
                    rule_id="SENTINEL_REG_02",
                    severity="WARNING",
                    category="Regulatory",
                    message="Protocol lacks defined inclusion and exclusion criteria.",
                    target_node_id=study_id,
                )
            )

        # Rule 3: Check Objectives / Endpoints
        objectives = study_payload.get("objectives") or []
        if not objectives and designs:
            first_design = designs[0] if isinstance(designs, list) else {}
            objectives = (
                first_design.get("objectives") if isinstance(first_design, dict) else []
            )

        if not objectives:
            findings.append(
                QualityRuleFinding(
                    rule_id="SENTINEL_REG_03",
                    severity="ERROR",
                    category="Regulatory",
                    message="Protocol lacks defined primary study objectives or endpoints.",
                    target_node_id=study_id,
                )
            )

        # Extract SoA-related items
        encounters_list = []
        procedures_list = []
        forms_list = []
        valid_visits = set()
        valid_procedures = set()
        valid_fields = set()

        if designs and isinstance(designs, list):
            for d in designs:
                if isinstance(d, dict):
                    # Gather visits/encounters
                    encs = d.get("encounters", []) or d.get("visits", [])
                    encounters_list.extend(encs)
                    for enc in encs:
                        if isinstance(enc, dict):
                            enc_id = (
                                enc.get("id")
                                or enc.get("encounter_id")
                                or enc.get("visit_id")
                            )
                            if enc_id:
                                valid_visits.add(str(enc_id))

                    # Gather activities/procedures
                    acts = d.get("activities", []) or d.get("procedures", [])
                    procedures_list.extend(acts)
                    for act in acts:
                        if isinstance(act, dict):
                            act_id = (
                                act.get("id")
                                or act.get("activity_id")
                                or act.get("procedure_id")
                            )
                            act_name = (
                                act.get("name")
                                or act.get("activity_name")
                                or act.get("procedure_name")
                            )
                            if act_id:
                                valid_procedures.add(str(act_id))
                            if act_name:
                                valid_procedures.add(str(act_name))

                            # Extract fields inside activity
                            forms = act.get("forms", [])
                            forms_list.extend(forms)
                            for form in forms:
                                if isinstance(form, dict):
                                    fields = form.get("fields", [])
                                    for f in fields:
                                        if isinstance(f, dict):
                                            f_id = f.get("id") or f.get("field_id")
                                            f_name = f.get("name") or f.get(
                                                "field_name"
                                            )
                                            if f_id:
                                                valid_fields.add(str(f_id))
                                            if f_name:
                                                valid_fields.add(str(f_name))

        # Capture fields also directly from payload if present
        for arm in study_payload.get("arms", []):
            for visit in arm.get("visits", []):
                valid_visits.add(str(visit.get("visit_id", "")))
                for act in visit.get("activities", []):
                    valid_procedures.add(str(act.get("activity_id", "")))
                    if "name" in act:
                        valid_procedures.add(str(act["name"]))

        # --- Deterministic Inconsistency Findings ---
        blocks_data = study_payload.get("blocks", [])
        if isinstance(blocks_data, list):
            for block in blocks_data:
                if isinstance(block, dict):
                    props = block.get("properties", {}) or block
                    block_id = (
                        block.get("id") or block.get("block_id") or "block_unnamed"
                    )

                    # Check referenced visits in blocks
                    ref_visit = props.get("visit_id")
                    if ref_visit and str(ref_visit) not in valid_visits:
                        findings.append(
                            QualityRuleFinding(
                                rule_id="SENTINEL_INCON_VISIT",
                                severity="WARNING",
                                category="Inconsistency",
                                message=f"Block '{block_id}' references Visit '{ref_visit}' which does not exist in the Study Schedule of Activities.",
                                target_node_id=block_id,
                            )
                        )

                    # Check referenced procedures in blocks
                    ref_proc = props.get("procedure_id") or props.get("activity_id")
                    if ref_proc and str(ref_proc) not in valid_procedures:
                        findings.append(
                            QualityRuleFinding(
                                rule_id="SENTINEL_INCON_PROC",
                                severity="WARNING",
                                category="Inconsistency",
                                message=f"Block '{block_id}' references Procedure/Activity '{ref_proc}' which does not exist in the Study Schedule of Activities.",
                                target_node_id=block_id,
                            )
                        )

        # Check Eligibility Criteria variables referencing valid fields
        if isinstance(criteria, list):
            for crit in criteria:
                if isinstance(crit, dict):
                    crit_id = (
                        crit.get("id")
                        or crit.get("criterion_id")
                        or "criterion_unnamed"
                    )
                    dsl_src = crit.get("dsl_source", "")
                    # Extract referenced variables from DSL (simple variable-name extractor)
                    variables = re.findall(r"\b[a-zA-Z_][a-zA-Z0-9_\.]*\b", dsl_src)
                    for var in variables:
                        # Skip numeric/boolean keywords or namespaces
                        if var in (
                            "True",
                            "False",
                            "true",
                            "false",
                            "and",
                            "or",
                            "not",
                            "eCRF",
                            "DM",
                            "VS",
                            "MH",
                        ):
                            continue
                        # If namespaced (e.g. eCRF.DM.AGE), extract final part
                        final_part = var.split(".")[-1]
                        if (
                            final_part not in valid_fields
                            and var not in valid_fields
                            and final_part.upper()
                            not in (
                                "AGE",
                                "SEX",
                                "SYSBP",
                                "DIABP",
                                "DIABETES",
                                "LIVER_DISEASE",
                                "PREGNANT",
                            )
                        ):
                            findings.append(
                                QualityRuleFinding(
                                    rule_id="SENTINEL_INCON_ELIG",
                                    severity="WARNING",
                                    category="Inconsistency",
                                    message=f"Eligibility criterion '{crit_id}' references variable '{var}' which is not defined in any eCRF forms or activities.",
                                    target_node_id=crit_id,
                                )
                            )

        # --- Deterministic Readability Analysis ---
        narratives = []
        if isinstance(blocks_data, list):
            for block in blocks_data:
                if isinstance(block, dict):
                    props = block.get("properties", {}) or block
                    text_val = props.get("text") or block.get("text")
                    if text_val:
                        narratives.append(str(text_val))

        # Fallback text if no blocks are present
        if not narratives:
            narratives.append(
                str(study_payload.get("desc", ""))
                or str(study_payload.get("description", ""))
                or "Clinical protocol for testing."
            )

        full_text = " ".join(narratives)
        words = [w.strip(".,;:?!\"'()[]{}") for w in full_text.split() if w.strip()]
        sentences = [s.strip() for s in re.split(r"[.!?]+", full_text) if s.strip()]

        word_count = len(words)
        sentence_count = max(1, len(sentences))
        syllable_count = sum(count_syllables_word(w) for w in words)

        if word_count > 0:
            fre = (
                206.835
                - 1.015 * (word_count / sentence_count)
                - 84.6 * (syllable_count / word_count)
            )
            fkgl = (
                0.39 * (word_count / sentence_count)
                + 11.8 * (syllable_count / word_count)
                - 15.59
            )
            fre = max(0.0, min(100.0, fre))
            fkgl = max(0.0, min(20.0, fkgl))
        else:
            fre = 100.0
            fkgl = 0.0

        # Readability interpretation
        if fre >= 90:
            interpretation = "Very Easy (5th grade reading level)"
        elif fre >= 80:
            interpretation = "Easy (6th grade reading level)"
        elif fre >= 70:
            interpretation = "Fairly Easy (7th grade reading level)"
        elif fre >= 60:
            interpretation = "Standard (8th-9th grade reading level)"
        elif fre >= 50:
            interpretation = "Fairly Difficult (High School student)"
        elif fre >= 30:
            interpretation = "Difficult (College student)"
        else:
            interpretation = "Very Confusing (College graduate level)"

        readability_report = ReadabilityReport(
            flesch_reading_ease=round(fre, 1),
            flesch_kincaid_grade_level=round(fkgl, 1),
            word_count=word_count,
            sentence_count=sentence_count,
            syllable_count=syllable_count,
            interpretation=interpretation,
        )

        # --- Traceable Burden Score ---
        encounters_cnt = len(encounters_list) or len(study_payload.get("visits", []))
        forms_cnt = len(forms_list) or len(study_payload.get("forms", []))

        visit_burden = float(encounters_cnt * 1.5)
        procedure_burden = 0.0
        trace_items = [
            BurdenTraceItem(
                component="visits",
                count=encounters_cnt,
                weight=1.5,
                subtotal=visit_burden,
                explanation=f"Patient visits / encounters count ({encounters_cnt}) evaluated with standard multiplier of 1.5.",
            )
        ]

        # Calculate procedure-level burden recursively with invasiveness modifiers
        for proc in procedures_list:
            if isinstance(proc, dict):
                p_name = str(
                    proc.get("name")
                    or proc.get("activity_name")
                    or proc.get("procedure_name")
                    or ""
                ).lower()
                base_w = 2.0
                inv_modifier = 0.0
                modifier_explanation = ""

                if "biopsy" in p_name or "surgery" in p_name or "surgical" in p_name:
                    inv_modifier = 10.0
                    modifier_explanation = " (+10.0 high invasiveness)"
                elif (
                    "blood" in p_name
                    or "phlebotomy" in p_name
                    or "venipuncture" in p_name
                    or "lab" in p_name
                ):
                    inv_modifier = 3.0
                    modifier_explanation = " (+3.0 moderate invasiveness)"
                elif (
                    "mri" in p_name
                    or "ct scan" in p_name
                    or "imaging" in p_name
                    or "x-ray" in p_name
                    or "ecg" in p_name
                    or "electrocardiogram" in p_name
                ):
                    inv_modifier = 5.0
                    modifier_explanation = " (+5.0 device burden)"
                elif (
                    "injection" in p_name
                    or "infusion" in p_name
                    or "intravenous" in p_name
                ):
                    inv_modifier = 8.0
                    modifier_explanation = " (+8.0 delivery burden)"

                total_w = base_w + inv_modifier
                procedure_burden += total_w
                trace_items.append(
                    BurdenTraceItem(
                        component=f"procedure: {p_name[:30]}",
                        count=1,
                        weight=total_w,
                        subtotal=total_w,
                        explanation=f"Procedure '{p_name}' has base weight of {base_w}{modifier_explanation}.",
                    )
                )

        activity_burden = float(forms_cnt * 1.0)
        if forms_cnt > 0:
            trace_items.append(
                BurdenTraceItem(
                    component="forms",
                    count=forms_cnt,
                    weight=1.0,
                    subtotal=activity_burden,
                    explanation=f"Clinical Report Forms (CRFs) count ({forms_cnt}) evaluated with standard weight of 1.0.",
                )
            )

        total_burden = float(visit_burden + procedure_burden + activity_burden)

        burden_report = BurdenTraceReport(
            visit_burden=round(visit_burden, 1),
            procedure_burden=round(procedure_burden, 1),
            activity_burden=round(activity_burden, 1),
            total_burden=round(total_burden, 1),
            trace=trace_items,
        )

        if total_burden > 25.0:
            findings.append(
                QualityRuleFinding(
                    rule_id="SENTINEL_BURDEN_04",
                    severity="WARNING",
                    category="Burden",
                    message=f"Patient Operational Burden Index ({total_burden:.1f}) exceeds recommended threshold (25.0).",
                    target_node_id=study_id,
                )
            )

        # --- Amendment Impact/Cost Analyzer ---
        parent_version = study_payload.get("parent_version")
        impact_report = None
        if parent_version:
            # Look up parent frozen projection from mock memory
            from apps.designer.db import MOCK_STUDY_PROJECTIONS_BY_VERSION

            parent_key = f"{study_id}:{parent_version}"
            parent_projection = MOCK_STUDY_PROJECTIONS_BY_VERSION.get(parent_key)

            if parent_projection:
                # Compare forms count
                p_forms = len(parent_projection.get("forms", []))
                c_forms = forms_cnt

                added_forms = max(0, c_forms - p_forms)
                deleted_forms = max(0, p_forms - c_forms)
                modified_forms = 0  # Heuristic fallback

                # Calculate estimated cost
                cost = 0.0
                cost += added_forms * 300.0
                cost += deleted_forms * 100.0
                cost += 5000.0  # Base protocol writing & IRB resubmission overhead

                p_burden = float(
                    len(parent_projection.get("visits", [])) * 1.5
                    + len(parent_projection.get("activities", [])) * 2.0
                )
                burden_change = total_burden - p_burden

                explanation = (
                    f"Amendment comparison from baseline version {parent_version} to {study_payload.get('current_version', 'amendment')}. "
                    f"Determined {added_forms} added forms and {deleted_forms} deleted forms. "
                    f"Operational burden index change is {burden_change:+.1f}."
                )

                impact_report = AmendmentImpactReport(
                    base_version=parent_version,
                    amended_version=study_payload.get("current_version"),
                    added_forms_count=added_forms,
                    modified_forms_count=modified_forms,
                    deleted_forms_count=deleted_forms,
                    estimated_cost_usd=cost,
                    burden_change=round(burden_change, 1),
                    explanation=explanation,
                )

        if not impact_report:
            impact_report = AmendmentImpactReport(
                base_version=None,
                amended_version=study_payload.get("current_version", "1.0"),
                added_forms_count=0,
                modified_forms_count=0,
                deleted_forms_count=0,
                estimated_cost_usd=0.0,
                burden_change=0.0,
                explanation="Initial protocol version; no amendment impact/cost computed.",
            )

        # --- Patient Population Feasibility / Attrition ---
        attrition_steps = []
        starting_cohort = len(PATIENT_FIXTURES)
        current_cohort = list(PATIENT_FIXTURES)

        if evaluate_node and ExpressionNode:
            # Sequentially evaluate eligibility criteria
            for crit in criteria:
                if isinstance(crit, dict):
                    crit_id = crit.get("id") or crit.get("criterion_id") or "crit"
                    crit_type = crit.get("criterion_type") or "inclusion"
                    desc = crit.get("description") or crit.get("dsl_source") or ""
                    cond_dict = crit.get("condition")

                    if cond_dict and isinstance(cond_dict, dict):
                        try:
                            ast_node = ExpressionNode.model_validate(cond_dict)
                        except Exception:
                            ast_node = None

                        if ast_node:
                            passed_pts = []
                            failed_pts = []
                            prior_count = len(current_cohort)

                            for pt in current_cohort:
                                ctx = make_patient_context(pt)
                                node_eval = evaluate_node(ast_node, ctx)

                                # Evaluate expected outcome match
                                expected = crit.get("expected_outcome", True)
                                is_met = (
                                    node_eval.value == expected
                                    if not node_eval.is_indeterminate
                                    else False
                                )

                                if is_met:
                                    passed_pts.append(pt)
                                else:
                                    failed_pts.append(pt)

                            # Update remaining active cohort
                            current_cohort = passed_pts
                            lost_cnt = prior_count - len(current_cohort)
                            rate_lost = (
                                (lost_cnt / prior_count) * 100.0
                                if prior_count > 0
                                else 0.0
                            )

                            attrition_steps.append(
                                AttritionStep(
                                    criterion_id=crit_id,
                                    type=crit_type,
                                    description=desc,
                                    passed_count=len(passed_pts),
                                    failed_count=len(failed_pts),
                                    remaining_count=len(current_cohort),
                                    attrition_rate=round(rate_lost, 1),
                                )
                            )

        final_eligible = len(current_cohort)
        eligibility_rate = (
            (final_eligible / starting_cohort) * 100.0 if starting_cohort > 0 else 0.0
        )

        feasibility_report = FeasibilityReport(
            starting_cohort_size=starting_cohort,
            final_eligible_count=final_eligible,
            overall_eligibility_rate=round(eligibility_rate, 1),
            attrition_steps=attrition_steps,
        )

        # Calculate Overall Quality Score
        error_count = len([f for f in findings if f.severity == "ERROR"])
        warning_count = len([f for f in findings if f.severity == "WARNING"])

        base_score = 100.0 - (25.0 * error_count + 10.0 * warning_count)
        quality_score = max(0.0, min(100.0, base_score))

        return ProtocolQualityScore(
            study_id=study_id,
            quality_score=quality_score,
            patient_burden_index=total_burden,
            findings=findings,
            passed=error_count == 0,
            readability=readability_report,
            burden_details=burden_report,
            amendment_impact=impact_report,
            feasibility=feasibility_report,
        )
