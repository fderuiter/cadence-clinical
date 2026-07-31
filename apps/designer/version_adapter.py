"""
USDM Version Adapter Module for Cadence Clinical.

This module is designed to handle the CDISC USDM v2/v3 lineage differences behind a single
interface. Standard USDM structures have no explicit top-level 'usdmVersion' envelope,
so version detection must rely on structural heuristics rather than a version literal.

We enforce a bidirectional, lossless-mapping intent between USDM representations and
internal structures, as specified in the schema-mapping-design ADR.

Under this contract:
1. Version detection uses declarative structural heuristics.
2. Inferred or overridden versions, along with the detailed evidence used, are recorded to
   ensure the decision is auditable and testable.
3. Lightweight declarative normalization reconciles shape differences into a single
   canonical shape (matching the installed usdm_model package which adheres to the v3 lineage).
"""

import copy
from typing import Any, Dict, List, Optional, Tuple

# Declarative lists of characteristic key indicators for USDM v2 and USDM v3
V2_INDICATORS = {
    "studyVersions",
    "studyDesign",
    "designs",
    "studyArms",
    "studyEpochs",
    "studyEstimands",
    "studyIndications",
    "studyInterventions",
    "studyObjectives",
    "studyPopulations",
}

V3_INDICATORS = {
    "versions",
    "studyDesigns",
    "arms",
    "epochs",
    "estimands",
    "indications",
    "objectives",
    "population",
}


def scan_indicators(data: Any, path: str = "") -> Tuple[int, int, List[str]]:
    """
    Recursively scans the payload to identify and count characteristic v2 vs v3 keys.
    Collects specific paths and key occurrences as auditable evidence.
    """
    v2_count = 0
    v3_count = 0
    evidence = []

    if isinstance(data, dict):
        for k, v in data.items():
            current_path = f"{path}.{k}" if path else k
            if k in V2_INDICATORS:
                v2_count += 1
                evidence.append(
                    f"Found characteristic USDM v2 key '{k}' at path '{current_path}'"
                )
            if k in V3_INDICATORS:
                v3_count += 1
                evidence.append(
                    f"Found characteristic USDM v3 key '{k}' at path '{current_path}'"
                )
            # Recursively scan dictionary values
            child_v2, child_v3, child_evidence = scan_indicators(v, current_path)
            v2_count += child_v2
            v3_count += child_v3
            evidence.extend(child_evidence)

    elif isinstance(data, list):
        for idx, item in enumerate(data):
            current_path = f"{path}[{idx}]"
            child_v2, child_v3, child_evidence = scan_indicators(item, current_path)
            v2_count += child_v2
            v3_count += child_v3
            evidence.extend(child_evidence)

    return v2_count, v3_count, evidence


def infer_usdm_version(
    payload: Dict[str, Any], override: Optional[str] = None
) -> Tuple[str, List[str]]:
    """
    Infers the USDM lineage (v2 vs v3) of a payload using structural heuristics,
    or honors an explicit version override.

    Returns:
        Tuple[str, List[str]]: Resolved version ("v2" or "v3") and a list of evidence strings.
    """
    evidence: List[str] = []

    # 1. Handle optional explicit version override
    if override:
        if override in ("v2", "v3"):
            evidence.append(f"Explicit version override applied: '{override}'")
            return override, evidence
        else:
            evidence.append(
                f"Ignored invalid override '{override}'. Falling back to structural heuristics."
            )

    # 2. Check root-level key indicators first (strongest indicators)
    if "studyVersions" in payload:
        evidence.append(
            "Detected root-level key 'studyVersions', strongly indicating USDM v2 lineage."
        )
        return "v2", evidence

    if "versions" in payload:
        evidence.append(
            "Detected root-level key 'versions', strongly indicating USDM v3 lineage."
        )
        return "v3", evidence

    # 3. Perform recursive scanning if root keys are ambiguous or absent
    v2_count, v3_count, structural_evidence = scan_indicators(payload)
    evidence.extend(structural_evidence)

    if v2_count > v3_count:
        evidence.append(
            f"Heuristic inference: USDM v2 (v2 indicators: {v2_count} vs v3 indicators: {v3_count})"
        )
        return "v2", evidence
    elif v3_count > v2_count:
        evidence.append(
            f"Heuristic inference: USDM v3 (v3 indicators: {v3_count} vs v2 indicators: {v2_count})"
        )
        return "v3", evidence

    # 4. Default to the lineage matching the installed usdm_model package (v3)
    evidence.append(
        "Lineage is structurally ambiguous. Defaulting to 'v3' matching installed usdm_model package."
    )
    return "v3", evidence


def normalize_payload_to_canonical(
    payload: Dict[str, Any], version: str
) -> Dict[str, Any]:
    """
    Performs lightweight, lossless normalization to map USDM v2 and v3 differences
    into a single canonical shape matching the installed usdm_model package (v3).

    Args:
        payload (Dict[str, Any]): The raw USDM input dictionary.
        version (str): The resolved version ('v2' or 'v3').

    Returns:
        Dict[str, Any]: A new deep-copied normalized USDM payload.
    """
    normalized = copy.deepcopy(payload)

    # Standardize root-level Study properties
    if "instanceType" not in normalized:
        normalized["instanceType"] = "Study"

    # If v2, perform lineage-specific key conversions
    if version == "v2":
        if "studyVersions" in normalized and "versions" not in normalized:
            normalized["versions"] = normalized.pop("studyVersions")

    # Reconcile deeply nested lists (StudyVersion, StudyDesign, etc.)
    versions_list = normalized.get("versions")
    if isinstance(versions_list, list):
        for v_idx, ver in enumerate(versions_list):
            if not isinstance(ver, dict):
                continue

            if "instanceType" not in ver:
                ver["instanceType"] = "StudyVersion"

            # Reconcile StudyDesigns container
            if "studyDesign" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("studyDesign")
            elif "designs" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("designs")

            designs = ver.get("studyDesigns")
            if isinstance(designs, list):
                for d_idx, design in enumerate(designs):
                    if not isinstance(design, dict):
                        continue

                    # Ensure standard StudyDesign instance type
                    if "instanceType" not in design:
                        design["instanceType"] = "InterventionalStudyDesign"

                    # Normalize studyArms -> arms
                    if "studyArms" in design and "arms" not in design:
                        design["arms"] = design.pop("studyArms")

                    # Normalize studyEpochs -> epochs
                    if "studyEpochs" in design and "epochs" not in design:
                        design["epochs"] = design.pop("studyEpochs")

                    # Normalize studyPopulations / populations -> population
                    if "studyPopulations" in design and "population" not in design:
                        pops = design.pop("studyPopulations")
                        if isinstance(pops, list) and len(pops) > 0:
                            design["population"] = pops[0]
                    elif "populations" in design and "population" not in design:
                        pops = design.pop("populations")
                        if isinstance(pops, list) and len(pops) > 0:
                            design["population"] = pops[0]

                    # Normalize studyEstimands -> estimands
                    if "studyEstimands" in design and "estimands" not in design:
                        design["estimands"] = design.pop("studyEstimands")

                    # Normalize studyIndications -> indications
                    if "studyIndications" in design and "indications" not in design:
                        design["indications"] = design.pop("studyIndications")

                    # Normalize studyObjectives -> objectives
                    if "studyObjectives" in design and "objectives" not in design:
                        design["objectives"] = design.pop("studyObjectives")

                    # Normalize studyInterventions -> studyInterventionIds
                    if (
                        "studyInterventions" in design
                        and "studyInterventionIds" not in design
                    ):
                        design["studyInterventionIds"] = design.pop(
                            "studyInterventions"
                        )

                    # Recurse on nested elements (e.g. arms, epochs, activities, encounters)
                    # to make sure their instance types are set
                    for arm in design.get("arms", []):
                        if isinstance(arm, dict) and "instanceType" not in arm:
                            arm["instanceType"] = "StudyArm"

                    for epoch in design.get("epochs", []):
                        if isinstance(epoch, dict) and "instanceType" not in epoch:
                            epoch["instanceType"] = "StudyEpoch"

                    for encounter in design.get("encounters", []):
                        if (
                            isinstance(encounter, dict)
                            and "instanceType" not in encounter
                        ):
                            encounter["instanceType"] = "Encounter"

                    for activity in design.get("activities", []):
                        if (
                            isinstance(activity, dict)
                            and "instanceType" not in activity
                        ):
                            activity["instanceType"] = "Activity"

    return normalized
