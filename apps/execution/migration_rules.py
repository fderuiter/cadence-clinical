from typing import List, Dict, Any, Optional
from sqlalchemy import select
from apps.execution.database.models import MigrationRule, ClinicalObservation

class ReconciledObservation(ClinicalObservation):
    """A wrapper that subclass ClinicalObservation and allows both attribute access
    and dictionary-like key access.

    This ensures 100% type parity (including isinstance checks, database primary keys,
    audit dates, etc.) and compatibility with both standard object attribute access
    and dictionary-like access.
    """
    def __init__(self, original_obs: ClinicalObservation, data: dict):
        # Copy all attributes from the original ClinicalObservation instance
        for k, v in original_obs.__dict__.items():
            if k != "_sa_instance_state":
                self.__dict__[k] = v
        # Overwrite/set the mapped fields
        for k, v in data.items():
            self.__dict__[k] = v
        self.__dict__["_data"] = {**original_obs.__dict__, **data}
        self._data.pop("_sa_instance_state", None)

    def __getitem__(self, key: str) -> Any:
        return self._data[key]

    def __setitem__(self, key: str, value: Any) -> None:
        self._data[key] = value
        self.__dict__[key] = value

    def get(self, key: str, default: Any = None) -> Any:
        return self._data.get(key, default)

    def keys(self):
        return self._data.keys()

    def values(self):
        return self._data.values()

    def items(self):
        return self._data.items()

    def dict(self) -> dict:
        return self._data

async def register_migration_rule(
    session,
    study_id: str,
    source_version_index: int,
    target_version_index: int,
    rules: dict
) -> MigrationRule:
    """Registers or updates a protocol version transition migration rule for a study."""
    stmt = select(MigrationRule).where(
        MigrationRule.study_id == study_id,
        MigrationRule.source_version_index == source_version_index,
        MigrationRule.target_version_index == target_version_index,
        MigrationRule.is_deleted.is_(False)
    )
    res = await session.execute(stmt)
    rule_record = res.scalars().first()

    if rule_record:
        rule_record.rules = rules
    else:
        rule_record = MigrationRule(
            study_id=study_id,
            source_version_index=source_version_index,
            target_version_index=target_version_index,
            rules=rules
        )
        session.add(rule_record)

    return rule_record

async def reconcile_observations(
    session,
    study_id: str,
    observations: List[ClinicalObservation],
    target_version_index: Optional[int] = None
) -> List[ReconciledObservation]:
    """Reconciles clinical observations to the target protocol version index without modifying the source rows.

    Exposes deterministic provenance metadata for each reconciled observation.
    """
    if not observations:
        return []

    # If no target version index is specified, determine it
    if target_version_index is None:
        stmt = select(MigrationRule).where(
            MigrationRule.study_id == study_id,
            MigrationRule.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        rules_list = res.scalars().all()
        if rules_list:
            target_version_index = max(r.target_version_index for r in rules_list)
        else:
            indices = [o.protocol_version_index for o in observations if o.protocol_version_index is not None]
            target_version_index = max(indices) if indices else 1

    # Fetch all migration rules for this study
    stmt = select(MigrationRule).where(
        MigrationRule.study_id == study_id,
        MigrationRule.is_deleted.is_(False)
    )
    res = await session.execute(stmt)
    all_rules = res.scalars().all()

    # Build an adjacency list for version transitions
    transitions = {}
    for r in all_rules:
        transitions[r.source_version_index] = (r.target_version_index, r.rules)

    reconciled = []
    for obs in observations:
        original_test_code = obs.test_code
        original_test_name = obs.test_name
        original_version_index = obs.protocol_version_index or 1
        original_version_tag = obs.protocol_version_tag

        current_test_code = original_test_code
        current_test_name = original_test_name
        current_version_index = original_version_index

        steps = []
        action = "CARRIED_OVER"

        # Trace transition path from original_version_index to target_version_index
        visited = set()
        while current_version_index < target_version_index:
            if current_version_index in visited:
                break  # prevent infinite loops
            visited.add(current_version_index)

            if current_version_index not in transitions:
                break

            next_ver, rules = transitions[current_version_index]

            # Map renamed/removed fields
            renamed = rules.get("renamed_fields", {})
            removed = rules.get("removed_fields", [])

            if current_test_code in renamed:
                new_code = renamed[current_test_code]
                steps.append(f"{current_test_code} -> {new_code} (v{current_version_index}->v{next_ver})")
                current_test_code = new_code
                action = "RENAMED"
            elif current_test_code in removed:
                steps.append(f"{current_test_code} removed (v{current_version_index}->v{next_ver})")
                action = "REMOVED"
            else:
                steps.append(f"{current_test_code} carried-over (v{current_version_index}->v{next_ver})")

            current_version_index = next_ver

        if action == "CARRIED_OVER" and original_version_index == target_version_index:
            action = "ORIGINAL"

        provenance = {
            "action": action,
            "original_test_code": original_test_code,
            "original_test_name": original_test_name,
            "original_protocol_version_index": original_version_index,
            "original_protocol_version_tag": original_version_tag,
            "target_protocol_version_index": target_version_index,
            "steps": steps
        }

        obs_dict = {
            "test_code": current_test_code,
            "test_name": current_test_name,
            "protocol_version_tag": original_version_tag,
            "protocol_version_index": original_version_index,
            "provenance": provenance
        }
        reconciled.append(ReconciledObservation(obs, obs_dict))

    return reconciled
