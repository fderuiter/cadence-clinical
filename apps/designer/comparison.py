import re
from typing import Any, Dict, List, Set, Tuple


def flatten_dict(d: Any, parent_key: str = "", sep: str = ".") -> Dict[str, Any]:
    """
    Recursively flattens a nested dictionary, list, or Pydantic model into a flat dictionary.
    Normalizes complex nested structures to enable 1D path-by-path comparison.
    """
    items: List[Tuple[str, Any]] = []

    # Handle Pydantic models (e.g. converting to dict/model_dump)
    if hasattr(d, "model_dump") and callable(getattr(d, "model_dump")):
        d = d.model_dump()
    elif hasattr(d, "dict") and callable(getattr(d, "dict")):
        d = d.dict()
    elif hasattr(d, "__dict__"):
        d = d.__dict__

    if isinstance(d, dict):
        for k, v in d.items():
            new_key = f"{parent_key}{sep}{k}" if parent_key else k
            items.extend(flatten_dict(v, new_key, sep=sep).items())
    elif isinstance(d, list):
        for i, v in enumerate(d):
            new_key = f"{parent_key}{sep}[{i}]" if parent_key else f"[{i}]"
            items.extend(flatten_dict(v, new_key, sep=sep).items())
    else:
        items.append((parent_key, d))

    return dict(items)


def collect_original_ids(d: Any) -> Dict[str, str]:
    """
    Traverses a nested structure to collect all mapping pairs between UUIDs and original string IDs
    based on '_original_id' fields.
    """
    mappings = {}

    if hasattr(d, "model_dump") and callable(getattr(d, "model_dump")):
        d = d.model_dump()
    elif hasattr(d, "dict") and callable(getattr(d, "dict")):
        d = d.dict()

    if isinstance(d, dict):
        orig_id = d.get("_original_id")
        obj_id = d.get("id")
        if orig_id and obj_id:
            mappings[str(obj_id)] = str(orig_id)
            mappings[str(orig_id)] = str(obj_id)
        for v in d.values():
            mappings.update(collect_original_ids(v))
    elif isinstance(d, list):
        for item in d:
            mappings.update(collect_original_ids(item))

    return mappings


def normalize_version(v: Any) -> str:
    """
    Normalizes semantic versions by stripping trailing zeros and whitespace.
    e.g. "1.1" and "1.1.0" become semantically identical.
    """
    if not isinstance(v, str):
        return str(v)
    v_clean = v.strip()
    # If it is a semantic version, normalize by removing trailing '.0' or similar
    match = re.match(r"^(\d+)\.(\d+)(?:\.(\d+))?$", v_clean)
    if match:
        major, minor, patch = match.groups()
        patch_val = int(patch) if patch is not None else 0
        if patch_val == 0:
            return f"{int(major)}.{int(minor)}"
        return f"{int(major)}.{int(minor)}.{patch_val}"
    return v_clean


def is_empty_value(val: Any) -> bool:
    """
    Checks if a value is semantically empty (None, empty string, empty list, empty dict).
    """
    if val is None:
        return True
    if isinstance(val, (str, list, dict)) and len(val) == 0:
        return True
    return False


def compare_payloads(original: Any, round_tripped: Any) -> Dict[str, Any]:
    """
    Compares two payloads path-by-path and classifies differences into Material and Non-material.
    Returns a structured fidelity report detailing added, dropped, and altered paths.
    """
    flat_orig = flatten_dict(original)
    flat_rt = flatten_dict(round_tripped)

    orig_id_mappings = collect_original_ids(original)
    rt_id_mappings = collect_original_ids(round_tripped)
    # Combine ID mappings for bidirectional lookup
    id_mappings = {**orig_id_mappings, **rt_id_mappings}

    added_paths: List[Dict[str, Any]] = []
    dropped_paths: List[Dict[str, Any]] = []
    altered_paths: List[Dict[str, Any]] = []

    all_keys: Set[str] = set(flat_orig.keys()).union(set(flat_rt.keys()))

    material_difference_count = 0
    non_material_difference_count = 0

    for key in sorted(all_keys):
        # Ignore cosmetic/formatting differences by ignoring standard metadata fields
        # like formatting whitespace or unmapped preservation fields.
        in_orig = key in flat_orig
        in_rt = key in flat_rt

        val_orig = flat_orig.get(key) if in_orig else None
        val_rt = flat_rt.get(key) if in_rt else None

        # 1. Check if both are empty/none (non-material difference / no difference)
        if is_empty_value(val_orig) and is_empty_value(val_rt):
            continue

        if not in_orig:
            # Key was ADDED
            # Added non-material fields: _original_id, instanceType, preservation_metadata
            is_mat = True
            reason = "Field was added in the round-tripped payload."

            # Non-material additions
            if any(
                part in key
                for part in (
                    "_original_id",
                    "instanceType",
                    "preservation_metadata",
                    "audit_metadata",
                    "reason_for_change",
                )
            ):
                is_mat = False
                reason = f"Non-material: standard structural metadata '{key}' added."

            if is_mat:
                material_difference_count += 1
            else:
                non_material_difference_count += 1

            added_paths.append(
                {"field": key, "value": val_rt, "is_material": is_mat, "reason": reason}
            )

        elif not in_rt:
            # Key was DROPPED
            is_mat = True
            reason = "Field was dropped in the round-tripped payload."

            # Non-material drops (e.g. empty fields or private/unmapped preservation fields)
            if any(
                part in key
                for part in (
                    "_original_id",
                    "instanceType",
                    "preservation_metadata",
                    "audit_metadata",
                    "reason_for_change",
                )
            ):
                is_mat = False
                reason = f"Non-material: standard structural metadata '{key}' dropped."

            if is_mat:
                material_difference_count += 1
            else:
                non_material_difference_count += 1

            dropped_paths.append(
                {
                    "field": key,
                    "value": val_orig,
                    "is_material": is_mat,
                    "reason": reason,
                }
            )

        else:
            # Key is in both but might be ALTERED
            # Normalize strings, whitespace, and version formats
            str_orig = str(val_orig).strip() if val_orig is not None else ""
            str_rt = str(val_rt).strip() if val_rt is not None else ""

            # Standardize multiple whitespaces
            str_orig_norm = " ".join(str_orig.split())
            str_rt_norm = " ".join(str_rt.split())

            # Check semantic equality
            if val_orig == val_rt or str_orig_norm == str_rt_norm:
                continue

            # Version format check (e.g., 1.1 vs 1.1.0)
            if normalize_version(val_orig) == normalize_version(val_rt):
                continue

            # ID mapping check (e.g. string ID was converted to UUID but is semantically the same)
            is_id_translated = False
            if isinstance(val_orig, str) and isinstance(val_rt, str):
                if (
                    id_mappings.get(val_rt) == val_orig
                    or id_mappings.get(val_orig) == val_rt
                ):
                    is_id_translated = True

            if is_id_translated:
                continue

            # If we reach here, there is an actual difference.
            # Determine materiality
            is_mat = True
            reason = "Value has altered between payloads."

            # If it's standard metadata or version difference that is non-material
            if any(
                part in key
                for part in ("_original_id", "instanceType", "preservation_metadata")
            ):
                is_mat = False
                reason = f"Non-material: metadata field '{key}' updated."

            if is_mat:
                material_difference_count += 1
            else:
                non_material_difference_count += 1

            altered_paths.append(
                {
                    "field": key,
                    "old_value": val_orig,
                    "new_value": val_rt,
                    "is_material": is_mat,
                    "reason": reason,
                }
            )

    is_lossless = material_difference_count == 0

    return {
        "lossless": is_lossless,
        "material_difference_count": material_difference_count,
        "non_material_difference_count": non_material_difference_count,
        "added": added_paths,
        "dropped": dropped_paths,
        "altered": altered_paths,
    }
