"""
USDM Version Handling & Adapter Module

This module implements the USDM version-detection and normalization adapter (Task 2).
It handles the v2/v3 lineage differences behind a single unified interface.

Since USDM has no top-level `usdmVersion` envelope (meaning detection cannot rely on a version literal),
this adapter infers the USDM lineage (v2 vs v3) using declarative structural heuristics.

Design Intent & Traceability:
- Governed by ADR 2026-07-22-schema-mapping-design, focusing on lossless-mapping intent.
- Infers lineage using structural shapes, and accepts an optional explicit override from caller.
- Defaults sensibly to "v3" (matching the installed usdm_model package version).
- Records auditable and testable version resolution evidence.
- Normalizes structural shape differences so downstream validation and mapping can operate on a single canonical shape.
"""

from typing import Any, Dict, List, Optional, Tuple

# Declarative Heuristics definition
# Each heuristic has a name, a target key/shape, and the predicted version if matched.
# Specific/nested keys are listed first to prioritize detailed structural features over top-level root keys.
V2_HEURISTICS = [
    {
        "key": "studyArms",
        "description": "Presence of 'studyArms' key in the study structure",
        "version": "v2",
    },
    {
        "key": "studyEpochs",
        "description": "Presence of 'studyEpochs' key in the study structure",
        "version": "v2",
    },
    {
        "key": "studyDesign",
        "description": "Presence of 'studyDesign' key in the study structure",
        "version": "v2",
    },
    {
        "key": "designs",
        "description": "Presence of 'designs' key in the study structure",
        "version": "v2",
    },
    {
        "key": "studyVersions",
        "description": "Presence of top-level 'studyVersions' key in the study root",
        "version": "v2",
    },
]

V3_HEURISTICS = [
    {
        "key": "arms",
        "description": "Presence of 'arms' key in the study structure",
        "version": "v3",
    },
    {
        "key": "epochs",
        "description": "Presence of 'epochs' key in the study structure",
        "version": "v3",
    },
    {
        "key": "studyDesigns",
        "description": "Presence of 'studyDesigns' key in the study structure",
        "version": "v3",
    },
    {
        "key": "versions",
        "description": "Presence of top-level 'versions' key in the study root",
        "version": "v3",
    },
]


def find_keys_recursively(d: Any, target_keys: set) -> Dict[str, List[str]]:
    """
    Recursively scans dictionaries and lists to find paths to target keys.
    Returns a dict mapping found key to a list of path strings.
    """
    found = {}

    def walk(node: Any, path: str):
        if isinstance(node, dict):
            for k, v in node.items():
                current_path = f"{path}.{k}" if path else k
                if k in target_keys:
                    if k not in found:
                        found[k] = []
                    found[k].append(current_path)
                walk(v, current_path)
        elif isinstance(node, list):
            for idx, item in enumerate(node):
                walk(item, f"{path}[{idx}]")

    walk(d, "")
    return found


def resolve_usdm_version(
    payload: Dict[str, Any], override: Optional[str] = None
) -> Tuple[str, List[str]]:
    """
    Resolves USDM v2 vs v3 version using declarative structural heuristics or explicit override.
    Defaults to the lineage matching the installed usdm_model package ('v3').
    Records the evidence used during resolution for auditing and testing.
    """
    evidence = []

    # 1. Handle explicit override
    if override:
        if override in ("v2", "v3"):
            evidence.append(f"Explicit version override provided: '{override}'")
            return override, evidence
        else:
            evidence.append(
                f"Ignored invalid override '{override}'. Falling back to structural rules."
            )

    # 2. Gather all characteristic keys recursively
    v2_keys = {rule["key"] for rule in V2_HEURISTICS}
    v3_keys = {rule["key"] for rule in V3_HEURISTICS}
    all_target_keys = v2_keys.union(v3_keys)

    found_paths = find_keys_recursively(payload, all_target_keys)

    # 3. Check v2 keys first
    for rule in V2_HEURISTICS:
        key = rule["key"]
        if key in found_paths:
            paths_str = ", ".join(found_paths[key])
            evidence.append(f"Heuristics Match: {rule['description']} (found at {paths_str}) -> Resolved to v2.")
            return "v2", evidence

    # 4. Check v3 keys next
    for rule in V3_HEURISTICS:
        key = rule["key"]
        if key in found_paths:
            paths_str = ", ".join(found_paths[key])
            evidence.append(f"Heuristics Match: {rule['description']} (found at {paths_str}) -> Resolved to v3.")
            return "v3", evidence

    # 5. Deep fallback based on general key matching
    for key in payload.keys():
        if "version" in key.lower() and key != "versions":
            evidence.append(f"Fallback Heuristics: Detected key '{key}' in root -> Treating as USDM v3 fallback.")
            return "v3", evidence

    # Default sensibly (matching installed usdm_model which is v3-based)
    evidence.append(
        "No version-specific keys found in payload structure. Defaulting to 'v3' (installed usdm_model lineage)."
    )
    return "v3", evidence


def normalize_usdm_payload(payload: Dict[str, Any], version: str) -> Dict[str, Any]:
    """
    Normalizes USDM shape differences into a single canonical shape expected by usdm_model.Study.
    This acts as the translation layer ensuring lossless mapping (ADR 2026-07-22-schema-mapping-design).
    """
    import copy

    normalized = copy.deepcopy(payload)

    # If v2, rename studyVersions -> versions
    if version == "v2":
        if "studyVersions" in normalized and "versions" not in normalized:
            normalized["versions"] = normalized.pop("studyVersions")

    # Ensure versions is a list of dicts
    versions_list = normalized.get("versions")
    if isinstance(versions_list, list):
        for ver in versions_list:
            if not isinstance(ver, dict):
                continue

            # Normalize StudyVersion fields
            if "studyDesign" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("studyDesign")
            elif "designs" in ver and "studyDesigns" not in ver:
                ver["studyDesigns"] = ver.pop("designs")

            designs = ver.get("studyDesigns")
            if isinstance(designs, list):
                for design in designs:
                    if not isinstance(design, dict):
                        continue

                    # In StudyDesign, normalize studyArms -> arms, studyEpochs -> epochs
                    if "studyArms" in design and "arms" not in design:
                        design["arms"] = design.pop("studyArms")
                    if "studyEpochs" in design and "epochs" not in design:
                        design["epochs"] = design.pop("studyEpochs")

    # Ensure basic instanceType values are present if absent
    if "instanceType" not in normalized:
        normalized["instanceType"] = "Study"

    return normalized
