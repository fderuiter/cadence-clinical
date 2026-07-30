import uuid
from typing import Any, Dict, List, Optional

from sqlalchemy import select

from apps.execution.database.models import ClinicalObservation, MigrationRule


class ReconciledObservation(ClinicalObservation):
    """Subclass of ClinicalObservation supporting dict-like key-value access and provenance tracking."""

    def __init__(self, **kwargs):
        # We can extract provenance if passed in kwargs
        self._provenance: List[Dict[str, Any]] = kwargs.pop("provenance", [])
        super().__init__(**kwargs)

    @property
    def provenance(self) -> List[Dict[str, Any]]:
        return self._provenance

    @provenance.setter
    def provenance(self, value: List[Dict[str, Any]]) -> None:
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


def find_migration_path(
    rules_by_src: Dict[str, List[str]], current: str, target: str, visited: set
) -> Optional[List[str]]:
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


async def reconcile_observations(
    session, observations: List[ClinicalObservation], target_version: str
) -> List[ReconciledObservation]:
    """Reconciles observations dynamically and non-destructively to a target protocol version."""
    if not observations:
        return []

    # Get study_id from observations (all must belong to the same study)
    study_id = observations[0].study_id

    # Fetch all migration rules for this study
    stmt = select(MigrationRule).where(
        MigrationRule.study_id == study_id, MigrationRule.is_deleted.is_(False)
    )
    res = await session.execute(stmt)
    rules = list(res.scalars().all())

    # Build the adjacency list of version transitions
    rules_by_src: Dict[str, List[str]] = {}
    transitions: Dict[str, Dict[str, List[MigrationRule]]] = {}
    for r in rules:
        rules_by_src.setdefault(r.source_version, [])
        if r.target_version not in rules_by_src[r.source_version]:
            rules_by_src[r.source_version].append(r.target_version)
        transitions.setdefault(r.source_version, {})
        transitions[r.source_version].setdefault(r.target_version, [])
        transitions[r.source_version][r.target_version].append(r)

    # Convert all input observations to ReconciledObservation instances first (non-destructive copy)
    reconciled: List[ReconciledObservation] = []
    for obs in observations:
        # Copy attributes
        attrs = {
            "id": obs.id,
            "subject_id": obs.subject_id,
            "study_id": obs.study_id,
            "site_id": obs.site_id,
            "visit_id": obs.visit_id,
            "domain": obs.domain,
            "observation_date": obs.observation_date,
            "test_code": obs.test_code,
            "test_name": obs.test_name,
            "value": obs.value,
            "value_string": obs.value_string,
            "unit": obs.unit,
            "normalized_value": obs.normalized_value,
            "normalized_unit": obs.normalized_unit,
            "is_outlier": obs.is_outlier,
            "is_sdv_verified": obs.is_sdv_verified,
            "sdv_verified_by": obs.sdv_verified_by,
            "sdv_verified_at": obs.sdv_verified_at,
            "page_id": obs.page_id,
            "lab_source": obs.lab_source,
            "lab_site_id": obs.lab_site_id,
            "lab_indicator": obs.lab_indicator,
            "lab_out_of_range": obs.lab_out_of_range,
            "matched_normal_bounds": obs.matched_normal_bounds,
            "protocol_version_tag": obs.protocol_version_tag,
            "protocol_version_index": obs.protocol_version_index,
            "provenance": list(getattr(obs, "provenance", [])),
        }
        reconciled.append(ReconciledObservation(**attrs))

    # Apply migration path for each observation that is not yet at target_version
    # Because 'add' rules require checking coordinates, we process version-by-version along paths.

    changed = True
    while changed:
        changed = False
        # Group observations by their current protocol_version_tag
        by_ver: Dict[str, List[ReconciledObservation]] = {}
        for obs in reconciled:
            ver = obs.protocol_version_tag or "1.0"  # default fallback if unstamped
            by_ver.setdefault(ver, [])
            by_ver[ver].append(obs)

        # For any version that is not the target_version, try to find a path to target_version
        for ver, obs_list in list(by_ver.items()):
            if ver == target_version:
                continue
            path = find_migration_path(rules_by_src, ver, target_version, set())
            if path and len(path) > 1:
                # We can perform the first hop: path[0] -> path[1]
                src_ver = path[0]
                tgt_ver = path[1]
                step_rules = transitions.get(src_ver, {}).get(tgt_ver, [])

                # Partition reconciled into:
                # 1. Those undergoing this hop
                # 2. Others (passed through)
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

                migrated_step: List[ReconciledObservation] = []

                # Group to_migrate by (subject_id, visit_id, domain) to support 'add' rules per group
                groups: Dict[tuple, List[ReconciledObservation]] = {}
                for o in to_migrate:
                    key = (o.subject_id, o.visit_id, o.domain, o.site_id)
                    groups.setdefault(key, [])
                    groups[key].append(o)

                # Process each group
                for (sub_id, vis_id, dom, sit_id), group_obs in groups.items():
                    # Check rename and remove rules
                    for o in group_obs:
                        # Find any matching rename or remove rule
                        matched_rule = None
                        for r in step_rules:
                            if (
                                r.rule_type in ("rename", "remove")
                                and r.source_field == o.test_code
                            ):
                                matched_rule = r
                                break

                        if matched_rule:
                            if matched_rule.rule_type == "rename":
                                o.test_code = matched_rule.target_field
                                o.protocol_version_tag = tgt_ver
                                o.provenance.append(
                                    {
                                        "action": "rename",
                                        "source_version": src_ver,
                                        "target_version": tgt_ver,
                                        "source_field": matched_rule.source_field,
                                        "target_field": matched_rule.target_field,
                                    }
                                )
                                migrated_step.append(o)
                            elif matched_rule.rule_type == "remove":
                                # Omit/filter out from migrated list
                                pass
                        else:
                            # Carry over
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

                    # Now process "add" rules for this group
                    for r in step_rules:
                        if r.rule_type == "add":
                            # Check if target_field already exists in the migrated group or original group
                            exists = any(
                                o.test_code == r.target_field
                                for o in migrated_step
                                if o.subject_id == sub_id
                                and o.visit_id == vis_id
                                and o.domain == dom
                            )
                            if not exists:
                                # Create synthetic observation
                                new_obs = ReconciledObservation(
                                    id=f"syn_{uuid.uuid4().hex[:12]}",
                                    subject_id=sub_id,
                                    study_id=study_id,
                                    site_id=sit_id,
                                    visit_id=vis_id,
                                    domain=dom,
                                    test_code=r.target_field,
                                    test_name=r.target_field,
                                    value=r.default_value_float,
                                    value_string=r.default_value_string,
                                    protocol_version_tag=tgt_ver,
                                    provenance=[
                                        {
                                            "action": "add",
                                            "source_version": src_ver,
                                            "target_version": tgt_ver,
                                            "target_field": r.target_field,
                                            "default_value_string": r.default_value_string,
                                            "default_value_float": r.default_value_float,
                                        }
                                    ],
                                )
                                migrated_step.append(new_obs)

                reconciled = others + migrated_step
                changed = True
                break

    return reconciled
