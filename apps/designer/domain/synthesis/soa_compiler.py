"""Dynamic Schedule of Activities (SoA) Matrix Compilation Engine.

Compiles dynamic Schedule of Activities (SoA) visit-vs-procedure matrix payloads
from Neo4j graph relationships (`(en:Encounter)-[:PERFORMS]->(ac:Activity)`)
or directly from in-memory CDISC USDM protocol data structures.

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
"""

from __future__ import annotations

import logging
from typing import Any

from apps.designer.db import MOCK_STUDIES
from apps.designer.delta import MOCK_SOA_DATA, _init_mock_soa
from apps.designer.domain.cdisc.usdm_importer import _normalize_usdm_payload
from apps.designer.domain.cdisc.usdm_models import (
    USDMStudy,
)
from apps.designer.domain.digitization_models import (
    USDMProtocolExtractionResponse,
)
from packages.database.mock_graph import MockGraphDriver

logger = logging.getLogger(__name__)


class SoACompiler:
    """Compiler service that generates unified SoAMatrixView payloads.

    Queries Neo4j graph structures for study epochs, encounters, study arms,
    activities, and `PERFORMS` edges, with automatic fallback to in-memory
    mock SoA structures or raw USDM study representations.
    """

    def __init__(self, driver: Any | None = None) -> None:
        """Initializes the SoACompiler with an optional Neo4j database driver.

        Args:
            driver: Optional Neo4j AsyncDriver or MockGraphDriver.
        """
        self.driver = driver

    async def compile(
        self,
        study_id_or_data: str
        | USDMStudy
        | USDMProtocolExtractionResponse
        | dict[str, Any]
        | None = None,
    ) -> dict[str, Any]:
        """Compiles the complete Schedule of Activities (SoA) matrix view payload.

        Args:
            study_id_or_data: Study ID string, USDMStudy model instance,
                USDMProtocolExtractionResponse, or raw USDM JSON dictionary.

        Returns:
            Dictionary matching the SoAMatrixView presentation contract:
            {
                "epochs": [...],
                "encounters": [...],
                "arms": [...],
                "rows": [...]
            }
        """
        if study_id_or_data is None:
            if self.driver is not None and isinstance(
                self.driver,
                (str, USDMStudy, USDMProtocolExtractionResponse, dict),
            ):
                study_id_or_data = self.driver
                self.driver = None
            else:
                return {"epochs": [], "encounters": [], "arms": [], "rows": []}

        # 1. USDMProtocolExtractionResponse DTO
        if isinstance(study_id_or_data, USDMProtocolExtractionResponse):
            return self._compile_from_extraction_response(study_id_or_data)

        # 2. USDMStudy Pydantic model
        if isinstance(study_id_or_data, USDMStudy):
            return self._compile_from_usdm_study(study_id_or_data)

        # 3. Raw USDM / Protocol dictionary
        if isinstance(study_id_or_data, dict):
            return self._compile_from_dict_payload(study_id_or_data)

        # 4. Study ID string -> Query Neo4j or in-memory state
        if isinstance(study_id_or_data, str):
            study_id = study_id_or_data.strip()
            clean_study_id = study_id.removesuffix("_v1")
            version_id = f"{clean_study_id}_v1"

            is_mock_driver = (
                self.driver is None
                or isinstance(self.driver, MockGraphDriver)
                or hasattr(self.driver, "sessions")
                or type(self.driver).__name__ == "MockGraphDriver"
            )

            if not is_mock_driver:
                try:
                    res = await self._query_neo4j_soa(
                        study_id, clean_study_id, version_id
                    )
                    if res and (
                        res.get("epochs") or res.get("encounters") or res.get("rows")
                    ):
                        return res
                except Exception as exc:
                    logger.warning(
                        "Neo4j SoA query encountered error, using in-memory fallback: %s",
                        exc,
                    )

            # In-memory fallback
            return self._compile_from_in_memory_state(
                study_id, clean_study_id, version_id
            )

        return {"epochs": [], "encounters": [], "arms": [], "rows": []}

    def _compile_from_usdm_study(self, study: USDMStudy) -> dict[str, Any]:
        """Compiles SoA matrix from a USDMStudy Pydantic domain model."""
        epochs_list: list[dict[str, Any]] = []
        encounters_list: list[dict[str, Any]] = []
        arms_list: list[dict[str, Any]] = []
        rows_list: list[dict[str, Any]] = []

        seen_epochs: set[str] = set()
        seen_encounters: set[str] = set()
        seen_arms: set[str] = set()
        seen_activities: set[str] = set()

        all_designs = study.study_designs or []

        # 1. Arms
        for design in all_designs:
            for arm in design.arms:
                if arm.id not in seen_arms:
                    arms_list.append(
                        {
                            "arm_id": arm.id,
                            "arm_name": arm.name or arm.id,
                            "sequence": len(arms_list) + 1,
                        }
                    )
                    seen_arms.add(arm.id)

        # 2. Epochs
        epoch_name_to_id: dict[str, str] = {}
        for design in all_designs:
            for ep in design.epochs:
                if ep.id not in seen_epochs:
                    seq = (
                        ep.sequence_number or ep.sequence_index or len(epochs_list) + 1
                    )
                    epochs_list.append(
                        {
                            "epoch_id": ep.id,
                            "epoch_name": ep.name or ep.id,
                            "sequence": int(seq),
                            "arm_id": None,
                        }
                    )
                    seen_epochs.add(ep.id)
                    if ep.name:
                        epoch_name_to_id[ep.name] = ep.id

        epochs_list.sort(key=lambda x: x["sequence"])
        default_epoch_id = epochs_list[0]["epoch_id"] if epochs_list else ""

        # 3. Encounters
        for design in all_designs:
            for enc in design.encounters:
                if enc.id not in seen_encounters:
                    ep_id = (
                        enc.epoch_id
                        or epoch_name_to_id.get(enc.epoch_name or "")
                        or default_epoch_id
                    )
                    target_day = enc.target_day
                    if target_day is None and enc.start_date is not None:
                        try:
                            target_day = int(enc.start_date)
                        except ValueError:
                            target_day = None

                    encounters_list.append(
                        {
                            "encounter_id": enc.id,
                            "encounter_name": enc.name or enc.id,
                            "epoch_id": ep_id,
                            "sequence": len(encounters_list) + 1,
                            "arm_id": None,
                            "target_day": target_day,
                        }
                    )
                    seen_encounters.add(enc.id)

        encounters_list.sort(
            key=lambda x: (
                x["target_day"] if x["target_day"] is not None else 999999,
                x["sequence"],
            )
        )
        for i, enc in enumerate(encounters_list):
            enc["sequence"] = i + 1

        # 4. Activities & Rows
        for design in all_designs:
            for act in design.activities:
                if act.id not in seen_activities:
                    assigned_visits = set(act.assigned_visit_names or [])
                    assigned_enc_ids = set(act.assigned_encounter_ids or [])

                    cells: list[dict[str, Any]] = []
                    for enc in encounters_list:
                        enc_id = enc["encounter_id"]
                        enc_name = enc["encounter_name"]
                        ep_id = enc["epoch_id"]

                        is_applicable = (
                            enc_id in assigned_enc_ids
                            or enc_name in assigned_visits
                            or enc_id in assigned_visits
                        )

                        cells.append(
                            {
                                "activity_id": act.id,
                                "encounter_id": enc_id,
                                "epoch_id": ep_id,
                                "is_applicable": is_applicable,
                                "details": None,
                                "arm_id": None,
                                "derived_from_soa": False,
                            }
                        )

                    rows_list.append(
                        {
                            "activity_id": act.id,
                            "activity_name": act.name or act.id,
                            "cells": cells,
                            "derived_from_soa": False,
                        }
                    )
                    seen_activities.add(act.id)

        return {
            "epochs": epochs_list,
            "encounters": encounters_list,
            "arms": arms_list,
            "rows": rows_list,
        }

    def _compile_from_extraction_response(
        self, dto: USDMProtocolExtractionResponse
    ) -> dict[str, Any]:
        """Compiles SoA matrix from a USDMProtocolExtractionResponse DTO."""
        epochs_list: list[dict[str, Any]] = []
        encounters_list: list[dict[str, Any]] = []
        arms_list: list[dict[str, Any]] = []
        rows_list: list[dict[str, Any]] = []

        # Arms
        for idx, arm in enumerate(dto.arms or []):
            arm_id = f"arm_{idx + 1}"
            arms_list.append(
                {
                    "arm_id": arm_id,
                    "arm_name": arm.name or arm_id,
                    "sequence": idx + 1,
                }
            )

        # Epochs
        epoch_name_to_id: dict[str, str] = {}
        for idx, ep in enumerate(dto.epochs or []):
            ep_id = f"epoch_{idx + 1}"
            epochs_list.append(
                {
                    "epoch_id": ep_id,
                    "epoch_name": ep.name or ep_id,
                    "sequence": ep.sequence_index or (idx + 1),
                    "arm_id": None,
                }
            )
            if ep.name:
                epoch_name_to_id[ep.name] = ep_id

        epochs_list.sort(key=lambda x: x["sequence"])
        default_epoch_id = epochs_list[0]["epoch_id"] if epochs_list else ""

        # Encounters
        for idx, v in enumerate(dto.visits or []):
            enc_id = f"enc_{idx + 1}"
            ep_id = epoch_name_to_id.get(v.epoch_name or "") or default_epoch_id
            encounters_list.append(
                {
                    "encounter_id": enc_id,
                    "encounter_name": v.visit_name or enc_id,
                    "epoch_id": ep_id,
                    "sequence": idx + 1,
                    "arm_id": None,
                    "target_day": v.target_day,
                }
            )

        encounters_list.sort(
            key=lambda x: (
                x["target_day"] if x["target_day"] is not None else 999999,
                x["sequence"],
            )
        )
        for i, enc in enumerate(encounters_list):
            enc["sequence"] = i + 1

        # Activities & Rows
        for idx, act in enumerate(dto.activities or []):
            act_id = f"act_{idx + 1}"
            assigned_visits = set(act.assigned_visit_names or [])

            cells: list[dict[str, Any]] = []
            for enc in encounters_list:
                enc_id = enc["encounter_id"]
                enc_name = enc["encounter_name"]
                ep_id = enc["epoch_id"]

                is_applicable = enc_name in assigned_visits or enc_id in assigned_visits

                cells.append(
                    {
                        "activity_id": act_id,
                        "encounter_id": enc_id,
                        "epoch_id": ep_id,
                        "is_applicable": is_applicable,
                        "details": None,
                        "arm_id": None,
                        "derived_from_soa": False,
                    }
                )

            rows_list.append(
                {
                    "activity_id": act_id,
                    "activity_name": act.activity_name or act_id,
                    "cells": cells,
                    "derived_from_soa": False,
                }
            )

        return {
            "epochs": epochs_list,
            "encounters": encounters_list,
            "arms": arms_list,
            "rows": rows_list,
        }

    def _compile_from_dict_payload(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Normalizes and compiles SoA matrix from a raw dictionary."""
        # Try USDM model validation first if standard top-level USDM keys exist
        if any(
            k in payload
            for k in (
                "studyDesigns",
                "studyDesign",
                "study_designs",
                "usdmVersion",
                "protocolTitle",
            )
        ):
            try:
                normalized = _normalize_usdm_payload(payload)
                study_model = USDMStudy.model_validate(normalized)
                return self._compile_from_usdm_study(study_model)
            except Exception as exc:
                logger.debug(
                    "USDMStudy validation on dict payload fell back to generic dict compiler: %s",
                    exc,
                )

        return self._compile_from_raw_dict(payload)

    def _compile_from_raw_dict(self, data: dict[str, Any]) -> dict[str, Any]:
        """Compiles SoA matrix from an arbitrary or normalized dictionary."""
        # Epochs
        raw_epochs = data.get("epochs") or []
        epochs_list: list[dict[str, Any]] = []
        epoch_name_to_id: dict[str, str] = {}
        for idx, ep in enumerate(raw_epochs):
            if not isinstance(ep, dict):
                continue
            ep_id = ep.get("epoch_id") or ep.get("id") or f"epoch_{idx + 1}"
            ep_name = ep.get("epoch_name") or ep.get("name") or ep.get("title") or ep_id
            seq = (
                ep.get("sequence")
                or ep.get("sequence_number")
                or ep.get("sequence_index")
                or (idx + 1)
            )
            epochs_list.append(
                {
                    "epoch_id": str(ep_id),
                    "epoch_name": str(ep_name),
                    "sequence": int(seq),
                    "arm_id": ep.get("arm_id"),
                }
            )
            epoch_name_to_id[str(ep_name)] = str(ep_id)
            epoch_name_to_id[str(ep_id)] = str(ep_id)

        epochs_list.sort(key=lambda x: x["sequence"])
        default_epoch_id = epochs_list[0]["epoch_id"] if epochs_list else ""

        # Arms
        raw_arms = data.get("arms") or []
        arms_list: list[dict[str, Any]] = []
        for idx, arm in enumerate(raw_arms):
            if not isinstance(arm, dict):
                continue
            arm_id = arm.get("arm_id") or arm.get("id") or f"arm_{idx + 1}"
            arm_name = arm.get("arm_name") or arm.get("name") or arm_id
            seq = arm.get("sequence") or arm.get("sequence_number") or (idx + 1)
            arms_list.append(
                {
                    "arm_id": str(arm_id),
                    "arm_name": str(arm_name),
                    "sequence": int(seq),
                }
            )
        arms_list.sort(key=lambda x: x["sequence"])

        # Encounters / Visits
        raw_encounters = data.get("encounters") or data.get("visits") or []
        encounters_list: list[dict[str, Any]] = []
        for idx, enc in enumerate(raw_encounters):
            if not isinstance(enc, dict):
                continue
            enc_id = (
                enc.get("encounter_id")
                or enc.get("visit_id")
                or enc.get("id")
                or f"enc_{idx + 1}"
            )
            enc_name = (
                enc.get("encounter_name")
                or enc.get("visit_name")
                or enc.get("name")
                or enc_id
            )
            raw_ep = enc.get("epoch_id") or enc.get("epoch_name") or ""
            ep_id = epoch_name_to_id.get(str(raw_ep)) or default_epoch_id
            target_day = enc.get("target_day")
            if target_day is None and enc.get("startDate") is not None:
                try:
                    target_day = int(enc["startDate"])
                except ValueError, TypeError:
                    target_day = None

            seq = enc.get("sequence") or enc.get("sequence_number") or (idx + 1)
            encounters_list.append(
                {
                    "encounter_id": str(enc_id),
                    "encounter_name": str(enc_name),
                    "epoch_id": str(ep_id),
                    "sequence": int(seq),
                    "arm_id": enc.get("arm_id"),
                    "target_day": target_day,
                }
            )

        encounters_list.sort(
            key=lambda x: (
                x["target_day"] if x["target_day"] is not None else 999999,
                x["sequence"],
            )
        )
        for i, enc in enumerate(encounters_list):
            enc["sequence"] = i + 1

        # Check if pre-computed rows exist
        existing_rows = data.get("rows")
        if existing_rows and isinstance(existing_rows, list):
            rows_list: list[dict[str, Any]] = []
            for r in existing_rows:
                if not isinstance(r, dict):
                    continue
                act_id = str(
                    r.get("activity_id") or r.get("procedure_id") or r.get("id") or ""
                )
                act_name = str(
                    r.get("activity_name")
                    or r.get("procedure_name")
                    or r.get("name")
                    or act_id
                )
                cells: list[dict[str, Any]] = []
                for cell in r.get("cells", []):
                    if not isinstance(cell, dict):
                        continue
                    cells.append(
                        {
                            "activity_id": str(cell.get("activity_id") or act_id),
                            "encounter_id": str(
                                cell.get("encounter_id") or cell.get("visit_id") or ""
                            ),
                            "epoch_id": str(cell.get("epoch_id") or ""),
                            "is_applicable": bool(cell.get("is_applicable")),
                            "details": cell.get("details"),
                            "arm_id": cell.get("arm_id"),
                            "derived_from_soa": bool(
                                cell.get("derived_from_soa", False)
                            ),
                        }
                    )
                rows_list.append(
                    {
                        "activity_id": act_id,
                        "activity_name": act_name,
                        "cells": cells,
                        "derived_from_soa": bool(r.get("derived_from_soa", False)),
                    }
                )
            return {
                "epochs": epochs_list,
                "encounters": encounters_list,
                "arms": arms_list,
                "rows": rows_list,
            }

        # Build rows from activities / procedures
        raw_activities = data.get("activities") or data.get("procedures") or []
        rows_list = []
        for idx, act in enumerate(raw_activities):
            if not isinstance(act, dict):
                continue
            act_id = str(
                act.get("activity_id")
                or act.get("procedure_id")
                or act.get("id")
                or f"act_{idx + 1}"
            )
            act_name = str(
                act.get("activity_name")
                or act.get("procedure_name")
                or act.get("name")
                or act_id
            )
            assigned_visits = set(act.get("assigned_visit_names") or [])
            assigned_enc_ids = set(act.get("assigned_encounter_ids") or [])

            cells = []
            for enc in encounters_list:
                enc_id = enc["encounter_id"]
                enc_name = enc["encounter_name"]
                ep_id = enc["epoch_id"]

                is_applicable = (
                    enc_id in assigned_enc_ids
                    or enc_name in assigned_visits
                    or enc_id in assigned_visits
                )

                cells.append(
                    {
                        "activity_id": act_id,
                        "encounter_id": enc_id,
                        "epoch_id": ep_id,
                        "is_applicable": is_applicable,
                        "details": None,
                        "arm_id": None,
                        "derived_from_soa": False,
                    }
                )

            rows_list.append(
                {
                    "activity_id": act_id,
                    "activity_name": act_name,
                    "cells": cells,
                    "derived_from_soa": False,
                }
            )

        return {
            "epochs": epochs_list,
            "encounters": encounters_list,
            "arms": arms_list,
            "rows": rows_list,
        }

    def _compile_from_in_memory_state(
        self, study_id: str, clean_study_id: str, version_id: str
    ) -> dict[str, Any]:
        """Compiles SoA matrix from in-memory mock repositories (MOCK_SOA_DATA / MOCK_STUDIES)."""
        soa_data = (
            MOCK_SOA_DATA.get(version_id)
            or MOCK_SOA_DATA.get(study_id)
            or MOCK_SOA_DATA.get(clean_study_id)
        )

        if not soa_data or not soa_data.get("epochs"):
            # Check MOCK_STUDIES
            mock_study = (
                MOCK_STUDIES.get(study_id)
                or MOCK_STUDIES.get(clean_study_id)
                or MOCK_STUDIES.get(version_id)
            )
            if mock_study:
                return self._compile_from_dict_payload(mock_study)

        if not soa_data:
            _init_mock_soa(version_id)
            soa_data = MOCK_SOA_DATA[version_id]

        # Extract epochs
        epochs_dict = soa_data.get("epochs", {})
        raw_epochs = [
            ep
            for ep in epochs_dict.values()
            if not ep.get("is_retired") and not ep.get("is_deleted")
        ]

        # Extract visits / encounters
        visits_dict = soa_data.get("visits", {})
        raw_encounters = [
            v
            for v in visits_dict.values()
            if not v.get("is_retired") and not v.get("is_deleted")
        ]

        # Extract procedures / activities
        procedures_dict = soa_data.get("procedures", {})
        raw_procedures = [
            p
            for p in procedures_dict.values()
            if not p.get("is_retired") and not p.get("is_deleted")
        ]

        # Extract arms
        arms_dict = soa_data.get("arms", {})
        raw_arms = [
            a
            for a in arms_dict.values()
            if not a.get("is_retired") and not a.get("is_deleted")
        ]

        links = soa_data.get("links", [])

        # Map links
        visit_to_epoch: dict[str, str] = {}
        applicability_set: set[tuple[str, str]] = set()
        timing_map: dict[str, str] = {}
        target_to_arm: dict[str, str] = {}

        for link in links:
            l_type = link.get("type")
            from_id = link.get("from_id")
            to_id = link.get("to_id")

            if l_type == "epoch_visit":
                ep_id = link.get("epoch_id") or from_id
                v_id = link.get("visit_id") or to_id
                if ep_id and v_id:
                    visit_to_epoch[str(v_id)] = str(ep_id)

            elif l_type in ("visit_procedure", "performs"):
                v_id = link.get("visit_id") or from_id
                p_id = link.get("procedure_id") or link.get("activity_id") or to_id
                if v_id and p_id:
                    applicability_set.add((str(v_id), str(p_id)))

            elif l_type == "timing":
                src_id = from_id or link.get("visit_id") or link.get("procedure_id")
                t_name = to_id or link.get("timing_name")
                if src_id and t_name:
                    timing_map[str(src_id)] = str(t_name)

            elif l_type == "arm_applicability":
                arm_id = link.get("arm_id") or from_id
                tgt_id = link.get("target_id") or to_id
                if arm_id and tgt_id:
                    target_to_arm[str(tgt_id)] = str(arm_id)

        # 1. Arms
        arms_list: list[dict[str, Any]] = []
        for idx, sa in enumerate(raw_arms):
            arm_id = sa.get("id") or f"arm_{idx + 1}"
            arms_list.append(
                {
                    "arm_id": str(arm_id),
                    "arm_name": str(sa.get("name") or arm_id),
                    "sequence": int(sa.get("sequence") or (idx + 1)),
                }
            )
        arms_list.sort(key=lambda x: x["sequence"])

        # 2. Epochs
        epochs_list: list[dict[str, Any]] = []
        for idx, ep in enumerate(raw_epochs):
            ep_id = ep.get("id") or f"epoch_{idx + 1}"
            seq = (
                ep.get("sequence")
                or ep.get("sequence_number")
                or ep.get("sequence_index")
                or (idx + 1)
            )
            epochs_list.append(
                {
                    "epoch_id": str(ep_id),
                    "epoch_name": str(ep.get("name") or ep_id),
                    "sequence": int(seq),
                    "arm_id": target_to_arm.get(str(ep_id)),
                }
            )
        epochs_list.sort(key=lambda x: x["sequence"])
        default_epoch_id = epochs_list[0]["epoch_id"] if epochs_list else ""

        # 3. Encounters
        encounters_list: list[dict[str, Any]] = []
        for idx, v in enumerate(raw_encounters):
            v_id = v.get("id") or f"enc_{idx + 1}"
            ep_id = (
                v.get("epoch_id") or visit_to_epoch.get(str(v_id)) or default_epoch_id
            )
            encounters_list.append(
                {
                    "encounter_id": str(v_id),
                    "encounter_name": str(v.get("name") or v_id),
                    "epoch_id": str(ep_id),
                    "sequence": int(v.get("sequence") or (idx + 1)),
                    "arm_id": target_to_arm.get(str(v_id)),
                    "target_day": v.get("target_day"),
                }
            )
        encounters_list.sort(
            key=lambda x: (
                x["target_day"] if x["target_day"] is not None else 999999,
                x["sequence"],
            )
        )
        for i, enc in enumerate(encounters_list):
            enc["sequence"] = i + 1

        # 4. Rows
        rows_list: list[dict[str, Any]] = []
        for idx, p in enumerate(raw_procedures):
            p_id = p.get("id") or f"proc_{idx + 1}"
            p_name = p.get("name") or p_id

            cells: list[dict[str, Any]] = []
            for enc in encounters_list:
                enc_id = enc["encounter_id"]
                enc_name = enc["encounter_name"]
                ep_id = enc["epoch_id"]

                is_applicable = (
                    (enc_id, str(p_id)) in applicability_set
                    or (enc_name, str(p_id)) in applicability_set
                    or (enc_id, str(p_name)) in applicability_set
                    or (enc_name, str(p_name)) in applicability_set
                )

                details = None
                if is_applicable:
                    details = timing_map.get(enc_id) or timing_map.get(str(p_id))

                cells.append(
                    {
                        "activity_id": str(p_id),
                        "encounter_id": str(enc_id),
                        "epoch_id": str(ep_id),
                        "is_applicable": is_applicable,
                        "details": details,
                        "arm_id": target_to_arm.get(enc_id)
                        or target_to_arm.get(str(p_id)),
                        "derived_from_soa": False,
                    }
                )

            rows_list.append(
                {
                    "activity_id": str(p_id),
                    "activity_name": str(p_name),
                    "cells": cells,
                    "derived_from_soa": False,
                }
            )

        return {
            "epochs": epochs_list,
            "encounters": encounters_list,
            "arms": arms_list,
            "rows": rows_list,
        }

    async def _query_neo4j_soa(
        self, study_id: str, clean_study_id: str, version_id: str
    ) -> dict[str, Any]:
        """Queries active Neo4j database instance for USDM and CDISC protocol entities."""
        query = """
        MATCH (s:Study)
        WHERE s.id = $study_id OR s.id = $clean_study_id OR s.id = $version_id
        OPTIONAL MATCH (s)-[:HAS_DESIGN]->(sd:StudyDesign)
        OPTIONAL MATCH (s)-[:HAS_VERSION]->(sv:StudyVersion)

        // 1. Epochs
        OPTIONAL MATCH (s)-[:HAS_EPOCH]->(ep1:StudyEpoch)
        OPTIONAL MATCH (sd)-[:HAS_EPOCH]->(ep2:StudyEpoch)
        OPTIONAL MATCH (sv)-[:HAS_EPOCH]->(ep3:Epoch)
        WITH s, sd, sv, [e IN collect(distinct ep1) + collect(distinct ep2) + collect(distinct ep3) WHERE e IS NOT NULL] AS raw_epochs

        // 2. Arms
        OPTIONAL MATCH (s)-[:HAS_ARM]->(a1:StudyArm)
        OPTIONAL MATCH (sd)-[:HAS_ARM]->(a2:StudyArm)
        OPTIONAL MATCH (sv)-[:HAS_ARM]->(a3:StudyArm)
        WITH s, sd, sv, raw_epochs, [a IN collect(distinct a1) + collect(distinct a2) + collect(distinct a3) WHERE a IS NOT NULL] AS raw_arms

        // 3. Encounters / Visits
        OPTIONAL MATCH (ep:StudyEpoch)-[:CONTAINS_ENCOUNTER]->(enc1:Encounter)
        WHERE ep IN raw_epochs
        OPTIONAL MATCH (s)-[:HAS_VISIT]->(enc2:Encounter)
        OPTIONAL MATCH (sv)-[:HAS_VISIT]->(v1:Visit)
        OPTIONAL MATCH (ep_leg:Epoch)-[:HAS_VISIT]->(v2:Visit)
        WHERE ep_leg IN raw_epochs
        WITH s, sd, sv, raw_epochs, raw_arms, [enc IN collect(distinct enc1) + collect(distinct enc2) + collect(distinct v1) + collect(distinct v2) WHERE enc IS NOT NULL] AS raw_encounters

        // 4. Activities / Procedures
        OPTIONAL MATCH (s)-[:HAS_ACTIVITY]->(ac1:Activity)
        OPTIONAL MATCH (sd)-[:HAS_ACTIVITY]->(ac2:Activity)
        OPTIONAL MATCH (sv)-[:HAS_PROCEDURE]->(p1:Procedure)
        WITH s, sd, sv, raw_epochs, raw_arms, raw_encounters, [ac IN collect(distinct ac1) + collect(distinct ac2) + collect(distinct p1) WHERE ac IS NOT NULL] AS raw_activities

        // 5. PERFORMS / HAS_PROCEDURE Links
        OPTIONAL MATCH (en:Encounter)-[:PERFORMS]->(ac:Activity)
        WHERE en IN raw_encounters AND ac IN raw_activities
        OPTIONAL MATCH (v:Visit)-[:HAS_PROCEDURE]->(p:Procedure)
        WHERE v IN raw_encounters AND p IN raw_activities
        WITH s, sd, sv, raw_epochs, raw_arms, raw_encounters, raw_activities,
             [link IN collect(distinct {encounter_id: en.id, activity_id: ac.id}) + collect(distinct {encounter_id: v.id, activity_id: p.id}) WHERE link.encounter_id IS NOT NULL AND link.activity_id IS NOT NULL] AS performs_links

        // 6. Epoch-Encounter links
        OPTIONAL MATCH (ep_c:StudyEpoch)-[:CONTAINS_ENCOUNTER]->(enc_c:Encounter)
        WHERE ep_c IN raw_epochs AND enc_c IN raw_encounters
        OPTIONAL MATCH (ep_l:Epoch)-[:HAS_VISIT]->(v_l:Visit)
        WHERE ep_l IN raw_epochs AND v_l IN raw_encounters
        WITH raw_epochs, raw_arms, raw_encounters, raw_activities, performs_links,
             [link IN collect(distinct {epoch_id: ep_c.id, encounter_id: enc_c.id}) + collect(distinct {epoch_id: ep_l.id, encounter_id: v_l.id}) WHERE link.epoch_id IS NOT NULL AND link.encounter_id IS NOT NULL] AS epoch_enc_links

        RETURN
            raw_epochs AS epochs,
            raw_encounters AS encounters,
            raw_arms AS arms,
            raw_activities AS activities,
            performs_links AS performs_links,
            epoch_enc_links AS epoch_enc_links
        """
        async with self.driver.session() as session:
            res = await session.run(
                query,
                study_id=study_id,
                clean_study_id=clean_study_id,
                version_id=version_id,
            )
            record = await res.single()
            if not record:
                return {
                    "epochs": [],
                    "encounters": [],
                    "arms": [],
                    "rows": [],
                }

            raw_epochs = [
                e
                for e in (record.get("epochs") or [])
                if e and not e.get("is_retired") and not e.get("is_deleted")
            ]
            raw_encounters = [
                e
                for e in (record.get("encounters") or [])
                if e and not e.get("is_retired") and not e.get("is_deleted")
            ]
            raw_activities = [
                e
                for e in (record.get("activities") or [])
                if e and not e.get("is_retired") and not e.get("is_deleted")
            ]
            raw_arms = [
                e
                for e in (record.get("arms") or [])
                if e and not e.get("is_retired") and not e.get("is_deleted")
            ]

            performs_links = record.get("performs_links") or []
            epoch_enc_links = record.get("epoch_enc_links") or []

            # Arms
            arms_list = []
            seen_arms = set()
            for idx, sa in enumerate(raw_arms):
                sa_id = sa.get("id")
                if not sa_id or sa_id in seen_arms:
                    continue
                arms_list.append(
                    {
                        "arm_id": str(sa_id),
                        "arm_name": str(sa.get("name") or sa_id),
                        "sequence": int(sa.get("sequence") or (idx + 1)),
                    }
                )
                seen_arms.add(sa_id)
            arms_list.sort(key=lambda x: x["sequence"])

            # Epochs
            epochs_list = []
            seen_epochs = set()
            for idx, ep in enumerate(raw_epochs):
                ep_id = ep.get("id")
                if not ep_id or ep_id in seen_epochs:
                    continue
                seq = (
                    ep.get("sequence")
                    or ep.get("sequence_number")
                    or ep.get("sequence_index")
                    or (idx + 1)
                )
                epochs_list.append(
                    {
                        "epoch_id": str(ep_id),
                        "epoch_name": str(ep.get("name") or ep_id),
                        "sequence": int(seq),
                        "arm_id": ep.get("arm_id"),
                    }
                )
                seen_epochs.add(ep_id)
            epochs_list.sort(key=lambda x: x["sequence"])
            default_epoch_id = epochs_list[0]["epoch_id"] if epochs_list else ""

            # Map epoch-encounter links
            enc_to_epoch = {}
            for link in epoch_enc_links:
                if link.get("encounter_id") and link.get("epoch_id"):
                    enc_to_epoch[str(link["encounter_id"])] = str(link["epoch_id"])

            # Encounters
            encounters_list = []
            seen_encs = set()
            for idx, enc in enumerate(raw_encounters):
                enc_id = enc.get("id")
                if not enc_id or enc_id in seen_encs:
                    continue
                ep_id = (
                    enc.get("epoch_id")
                    or enc_to_epoch.get(str(enc_id))
                    or default_epoch_id
                )
                encounters_list.append(
                    {
                        "encounter_id": str(enc_id),
                        "encounter_name": str(enc.get("name") or enc_id),
                        "epoch_id": str(ep_id),
                        "sequence": int(enc.get("sequence") or (idx + 1)),
                        "arm_id": enc.get("arm_id"),
                        "target_day": enc.get("target_day"),
                    }
                )
                seen_encs.add(enc_id)

            encounters_list.sort(
                key=lambda x: (
                    x["target_day"] if x["target_day"] is not None else 999999,
                    x["sequence"],
                )
            )
            for i, enc in enumerate(encounters_list):
                enc["sequence"] = i + 1

            # Applicability set
            applicability_set = {
                (str(p["encounter_id"]), str(p["activity_id"]))
                for p in performs_links
                if p.get("encounter_id") and p.get("activity_id")
            }

            # Rows
            rows_list = []
            seen_acts = set()
            for act in raw_activities:
                act_id = act.get("id")
                if not act_id or act_id in seen_acts:
                    continue
                act_name = act.get("name") or act_id

                cells = []
                for enc in encounters_list:
                    enc_id = enc["encounter_id"]
                    enc_name = enc["encounter_name"]
                    ep_id = enc["epoch_id"]

                    is_applicable = (
                        (enc_id, str(act_id)) in applicability_set
                        or (enc_name, str(act_id)) in applicability_set
                        or (enc_id, str(act_name)) in applicability_set
                        or (enc_name, str(act_name)) in applicability_set
                    )

                    cells.append(
                        {
                            "activity_id": str(act_id),
                            "encounter_id": str(enc_id),
                            "epoch_id": str(ep_id),
                            "is_applicable": is_applicable,
                            "details": None,
                            "arm_id": None,
                            "derived_from_soa": False,
                        }
                    )

                rows_list.append(
                    {
                        "activity_id": str(act_id),
                        "activity_name": str(act_name),
                        "cells": cells,
                        "derived_from_soa": False,
                    }
                )
                seen_acts.add(act_id)

            return {
                "epochs": epochs_list,
                "encounters": encounters_list,
                "arms": arms_list,
                "rows": rows_list,
            }


async def compile_soa_matrix_payload(
    driver: Any | None = None,
    study_id_or_data: str
    | USDMStudy
    | USDMProtocolExtractionResponse
    | dict[str, Any]
    | None = None,
) -> dict[str, Any]:
    """Compiles a dynamic Schedule of Activities (SoA) matrix view payload.

    Extracts epochs, encounters, study arms, activities, and encounter-to-activity
    PERFORMS applicability matrices from Neo4j graph database or in-memory USDM
    protocol models.

    Args:
        driver: Active Neo4j AsyncDriver instance, MockGraphDriver, or None.
        study_id_or_data: Study ID string, USDMStudy model instance,
            USDMProtocolExtractionResponse, or raw USDM dict.

    Returns:
        Dictionary conforming to the SoAMatrixView presentation contract:
        {
            "epochs": [...],
            "encounters": [...],
            "arms": [...],
            "rows": [...]
        }

    Requirement: PRD-MDR-007
    """
    compiler = SoACompiler(driver=driver)
    return await compiler.compile(study_id_or_data)
