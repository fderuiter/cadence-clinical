import uuid
from typing import Any


class ReconciledObservation:
    """Subclass/replacement of ClinicalObservation supporting dict-like key-value access and provenance tracking."""

    def __init__(self, **kwargs):
        self._provenance: list[dict[str, Any]] = kwargs.pop("provenance", [])
        for k, v in kwargs.items():
            setattr(self, k, v)

    @property
    def provenance(self) -> list[dict[str, Any]]:
        return self._provenance

    @provenance.setter
    def provenance(self, value: list[dict[str, Any]]) -> None:
        self._provenance = value

    def __getitem__(self, key: str) -> Any:
        if key == "provenance":
            return self._provenance
        if hasattr(self, key):
            return getattr(self, key)
        raise KeyError(key)

    def __setitem__(self, key: str, value: Any) -> None:
        if key == "provenance":
            self._provenance = value
        else:
            setattr(self, key, value)

    def get(self, key: str, default: Any = None) -> Any:
        if key == "provenance":
            return self._provenance
        if hasattr(self, key):
            return getattr(self, key)
        return default

    def keys(self):
        return [
            "id",
            "subject_id",
            "study_id",
            "site_id",
            "visit_id",
            "domain",
            "observation_date",
            "test_code",
            "test_name",
            "value",
            "value_string",
            "unit",
            "normalized_value",
            "normalized_unit",
            "is_outlier",
            "is_sdv_verified",
            "sdv_verified_by",
            "sdv_verified_at",
            "page_id",
            "lab_source",
            "lab_site_id",
            "lab_indicator",
            "lab_out_of_range",
            "matched_normal_bounds",
            "protocol_version_tag",
            "protocol_version_index",
            "provenance",
        ]

    def values(self):
        return [self.get(k) for k in self.keys()]

    def items(self):
        return [(k, self.get(k)) for k in self.keys()]


def get_val(obj: Any, key: str, default: Any = None) -> Any:
    if obj is None:
        return default
    if isinstance(obj, dict):
        return obj.get(key, default)
    if hasattr(obj, "model_dump"):
        return obj.model_dump().get(key, default)
    if hasattr(obj, "dict"):
        return obj.dict().get(key, default)
    if hasattr(obj, key):
        return getattr(obj, key, default)
    return default


def find_migration_path(
    rules_by_src: dict[str, list[str]], current: str, target: str, visited: set
) -> list[str] | None:
    if current == target:
        return [current]
    if current in visited:
        return None
    visited.add(current)
    if current not in rules_by_src:
        return None
    for next_ver in rules_by_src[current]:
        path = find_migration_path(rules_by_src, next_ver, target, visited)
        if path is not None:
            return [current] + path
    visited.remove(current)
    return None


def reconcile_observations(
    observations: list[Any], migration_rules: list[Any], target_version: str
) -> list[ReconciledObservation]:
    """Reconciles observations dynamically and non-destructively to a target protocol version."""
    if not observations:
        return []

    study_id = get_val(observations[0], "study_id")

    # Filter out active migration rules
    rules = []
    for r in migration_rules:
        is_del = get_val(r, "is_deleted", False)
        if is_del is not True:
            rules.append(r)

    # Build the adjacency list of version transitions
    rules_by_src: dict[str, list[str]] = {}
    transitions: dict[str, dict[str, list[Any]]] = {}
    for r in rules:
        src_ver = get_val(r, "source_version")
        tgt_ver = get_val(r, "target_version")
        if not src_ver or not tgt_ver:
            continue
        rules_by_src.setdefault(src_ver, [])
        if tgt_ver not in rules_by_src[src_ver]:
            rules_by_src[src_ver].append(tgt_ver)
        transitions.setdefault(src_ver, {})
        transitions[src_ver].setdefault(tgt_ver, [])
        transitions[src_ver][tgt_ver].append(r)

    # Convert all input observations to ReconciledObservation instances first
    reconciled: list[ReconciledObservation] = []
    for obs in observations:
        attrs = {
            "id": get_val(obs, "id"),
            "subject_id": get_val(obs, "subject_id"),
            "study_id": get_val(obs, "study_id"),
            "site_id": get_val(obs, "site_id"),
            "visit_id": get_val(obs, "visit_id"),
            "domain": get_val(obs, "domain"),
            "observation_date": get_val(obs, "observation_date"),
            "test_code": get_val(obs, "test_code"),
            "test_name": get_val(obs, "test_name"),
            "value": get_val(obs, "value"),
            "value_string": get_val(obs, "value_string"),
            "unit": get_val(obs, "unit"),
            "normalized_value": get_val(obs, "normalized_value"),
            "normalized_unit": get_val(obs, "normalized_unit"),
            "is_outlier": get_val(obs, "is_outlier", False),
            "is_sdv_verified": get_val(obs, "is_sdv_verified", False),
            "sdv_verified_by": get_val(obs, "sdv_verified_by"),
            "sdv_verified_at": get_val(obs, "sdv_verified_at"),
            "page_id": get_val(obs, "page_id"),
            "lab_source": get_val(obs, "lab_source"),
            "lab_site_id": get_val(obs, "lab_site_id"),
            "lab_indicator": get_val(obs, "lab_indicator"),
            "lab_out_of_range": get_val(obs, "lab_out_of_range", False),
            "matched_normal_bounds": get_val(obs, "matched_normal_bounds"),
            "protocol_version_tag": get_val(obs, "protocol_version_tag"),
            "protocol_version_index": get_val(obs, "protocol_version_index"),
            "provenance": list(get_val(obs, "provenance") or []),
        }
        reconciled.append(ReconciledObservation(**attrs))

    changed = True
    while changed:
        changed = False
        by_ver: dict[str, list[ReconciledObservation]] = {}
        for obs in reconciled:
            ver = obs.protocol_version_tag or "1.0"
            by_ver.setdefault(ver, [])
            by_ver[ver].append(obs)

        for ver, obs_list in list(by_ver.items()):
            if ver == target_version:
                continue
            path = find_migration_path(rules_by_src, ver, target_version, set())
            if path and len(path) > 1:
                src_ver = path[0]
                tgt_ver = path[1]
                step_rules = transitions.get(src_ver, {}).get(tgt_ver, [])

                to_migrate = [
                    o
                    for o in reconciled
                    if (o.protocol_version_tag or "1.0") == src_ver
                ]
                others = [
                    o
                    for o in reconciled
                    if (o.protocol_version_tag or "1.0") != src_ver
                ]

                migrated_step: list[ReconciledObservation] = []

                groups: dict[tuple, list[ReconciledObservation]] = {}
                for o in to_migrate:
                    key = (o.subject_id, o.visit_id, o.domain, o.site_id)
                    groups.setdefault(key, [])
                    groups[key].append(o)

                for (sub_id, vis_id, dom, sit_id), group_obs in groups.items():
                    for o in group_obs:
                        matched_rule = None
                        for r in step_rules:
                            r_type = get_val(r, "rule_type")
                            r_src = get_val(r, "source_field")
                            if r_type in ("rename", "remove") and r_src == o.test_code:
                                matched_rule = r
                                break

                        if matched_rule:
                            r_type = get_val(matched_rule, "rule_type")
                            r_src = get_val(matched_rule, "source_field")
                            r_tgt = get_val(matched_rule, "target_field")
                            if r_type == "rename":
                                o.test_code = r_tgt
                                o.protocol_version_tag = tgt_ver
                                o.provenance.append(
                                    {
                                        "action": "rename",
                                        "source_version": src_ver,
                                        "target_version": tgt_ver,
                                        "source_field": r_src,
                                        "target_field": r_tgt,
                                    }
                                )
                                migrated_step.append(o)
                            elif r_type == "remove":
                                pass
                        else:
                            o.protocol_version_tag = tgt_ver
                            o.provenance.append(
                                {
                                    "action": "carry_over",
                                    "source_version": src_ver,
                                    "target_version": tgt_ver,
                                    "field": o.test_code,
                                }
                            )
                            migrated_step.append(o)

                    for r in step_rules:
                        r_type = get_val(r, "rule_type")
                        r_tgt = get_val(r, "target_field")
                        r_def_str = get_val(r, "default_value_string")
                        r_def_flt = get_val(r, "default_value_float")
                        if r_type == "add":
                            exists = any(
                                o.test_code == r_tgt
                                for o in migrated_step
                                if o.subject_id == sub_id
                                and o.visit_id == vis_id
                                and o.domain == dom
                            )
                            if not exists:
                                new_obs = ReconciledObservation(
                                    id=f"syn_{uuid.uuid4().hex[:12]}",
                                    subject_id=sub_id,
                                    study_id=study_id,
                                    site_id=sit_id,
                                    visit_id=vis_id,
                                    domain=dom,
                                    test_code=r_tgt,
                                    test_name=r_tgt,
                                    value=r_def_flt,
                                    value_string=r_def_str,
                                    protocol_version_tag=tgt_ver,
                                    provenance=[
                                        {
                                            "action": "add",
                                            "source_version": src_ver,
                                            "target_version": tgt_ver,
                                            "target_field": r_tgt,
                                            "default_value_string": r_def_str,
                                            "default_value_float": r_def_flt,
                                        }
                                    ],
                                )
                                migrated_step.append(new_obs)

                reconciled = others + migrated_step
                changed = True
                break

    return reconciled
