import os
from enum import Enum
from typing import Any, Dict, List, Optional, Tuple

import httpx
import usdm_model
from pydantic import BaseModel

from apps.designer.db import get_study_projection, terminology_cache
from apps.designer.usdm_ingestion import (
    USDMValidationReport,
    validate_usdm_payload,
)


class CodeValidationState(str, Enum):
    """
    Validation state of a controlled terminology concept code.
    """

    VALID = "VALID"
    INVALID = "INVALID"
    DEGRADED = "DEGRADED"


class ConceptReference(BaseModel):
    """
    Identifies a specific study element referencing a terminology concept.
    """

    element_type: str  # e.g., 'arm', 'visit'
    element_id: str  # e.g., 'arm_1', 'visit_1'
    element_name: str  # e.g., 'Arm A', 'Visit 1'
    attribute: str  # e.g., 'type_concept_id', 'visit_type_concept_id'


class ConceptValidationReport(BaseModel):
    """
    Detailed validation status for a single terminology concept code.
    """

    concept_code: str
    state: CodeValidationState
    decode: Optional[str] = None
    system: Optional[str] = None
    error_message: Optional[str] = None
    references: List[ConceptReference] = []


class StudyTerminologyValidationReport(BaseModel):
    """
    Aggregated terminology validation report for an entire study structure.
    """

    study_id: str
    is_valid: bool
    total_concepts: int
    valid_count: int
    invalid_count: int
    degraded_count: int
    concepts: List[ConceptValidationReport]


def validate_concept_codes(codes: List[str]) -> List[ConceptValidationReport]:
    """
    Validates a list of concept codes through the terminology cache.
    """
    reports = []
    # De-duplicate to perform unique lookups, while retaining list structure
    for code in sorted(set(codes)):
        state = CodeValidationState.VALID
        decode = None
        system = None
        error_msg = None

        try:
            concept = terminology_cache.get(code)
            if concept is None:
                state = CodeValidationState.INVALID
                error_msg = f"Concept code '{code}' not found in terminology database."
            else:
                decode = concept.get("decode")
                system = concept.get("system")
                is_valid = concept.get("valid")
                if is_valid is False:
                    state = CodeValidationState.INVALID
                    error_msg = f"Concept code '{code}' is marked as invalid."
                else:
                    state = CodeValidationState.VALID
        except Exception as e:
            state = CodeValidationState.DEGRADED
            error_msg = f"Upstream service error while resolving concept code '{code}': {str(e)}"

        reports.append(
            ConceptValidationReport(
                concept_code=code,
                state=state,
                decode=decode,
                system=system,
                error_message=error_msg,
                references=[],
            )
        )
    return reports


def validate_study_terminology(
    study_id: str, study_data: Optional[Dict[str, Any]] = None
) -> StudyTerminologyValidationReport:
    """
    Traverses study concept references and aggregates validation outcomes.
    """
    if study_data is None:
        study_data = get_study_projection(study_id)
        if not study_data:
            raise ValueError(f"Study with ID '{study_id}' not found.")

    # Collect all references grouped by concept code
    references_by_code: Dict[str, List[ConceptReference]] = {}

    def add_ref(code: str, ref: ConceptReference):
        if not code:
            return
        if code not in references_by_code:
            references_by_code[code] = []
        references_by_code[code].append(ref)

    # Traverse arms
    for arm in study_data.get("arms", []):
        arm_id = arm.get("arm_id") or "unknown_arm"
        arm_name = arm.get("name") or "Unnamed Arm"

        type_concept_id = arm.get("type_concept_id")
        if type_concept_id:
            add_ref(
                type_concept_id,
                ConceptReference(
                    element_type="arm",
                    element_id=arm_id,
                    element_name=arm_name,
                    attribute="type_concept_id",
                ),
            )

        # Traverse visits
        for visit in arm.get("visits", []):
            visit_id = visit.get("visit_id") or "unknown_visit"
            visit_name = visit.get("name") or "Unnamed Visit"

            visit_type_concept_id = visit.get("visit_type_concept_id")
            if visit_type_concept_id:
                add_ref(
                    visit_type_concept_id,
                    ConceptReference(
                        element_type="visit",
                        element_id=visit_id,
                        element_name=visit_name,
                        attribute="visit_type_concept_id",
                    ),
                )

    # Validate collected codes
    concepts = []
    valid_count = 0
    invalid_count = 0
    degraded_count = 0

    for code in sorted(references_by_code.keys()):
        refs = references_by_code[code]
        state = CodeValidationState.VALID
        decode = None
        system = None
        error_msg = None

        try:
            concept = terminology_cache.get(code)
            if concept is None:
                state = CodeValidationState.INVALID
                error_msg = f"Concept code '{code}' not found in terminology database."
            else:
                decode = concept.get("decode")
                system = concept.get("system")
                is_valid = concept.get("valid")
                if is_valid is False:
                    state = CodeValidationState.INVALID
                    error_msg = f"Concept code '{code}' is marked as invalid."
                else:
                    state = CodeValidationState.VALID
        except Exception as e:
            state = CodeValidationState.DEGRADED
            error_msg = f"Upstream service error while resolving concept code '{code}': {str(e)}"

        if state == CodeValidationState.VALID:
            valid_count += 1
        elif state == CodeValidationState.INVALID:
            invalid_count += 1
        elif state == CodeValidationState.DEGRADED:
            degraded_count += 1

        concepts.append(
            ConceptValidationReport(
                concept_code=code,
                state=state,
                decode=decode,
                system=system,
                error_message=error_msg,
                references=refs,
            )
        )

    # A study structure is considered valid if there are references and none of them are invalid/degraded.
    # If there are 0 concepts, it is trivially valid.
    is_valid = invalid_count == 0 and degraded_count == 0

    return StudyTerminologyValidationReport(
        study_id=study_id,
        is_valid=is_valid,
        total_concepts=len(concepts),
        valid_count=valid_count,
        invalid_count=invalid_count,
        degraded_count=degraded_count,
        concepts=concepts,
    )


class ItemMappingStatus(BaseModel):
    """
    Represents the mapping status of an individual activity item.

    Attributes:
        item_id: The public string identifier of the activity item.
        internal_id: The internal graph database ID of the activity item.
        is_mapped: Boolean indicating whether this item has a corresponding ODM/CRF node mapped to it.
    """

    item_id: Optional[str]
    internal_id: Optional[int]
    is_mapped: bool


class ActivityReport(BaseModel):
    """
    Detailed report of an activity definition mapped within an epoch schedule.

    Attributes:
        epoch_id: The public identifier for the study epoch.
        epoch_internal_id: The internal database ID for the epoch.
        scheduled_event_id: The public identifier for the scheduled event instance.
        scheduled_event_internal_id: The internal database ID for the scheduled event instance.
        activity_def_id: The public identifier for the activity definition.
        activity_def_internal_id: The internal database ID for the activity definition.
        status: Mapping status of this activity ('complete', 'incomplete', or 'unmapped').
        unmapped_items: List of `ItemMappingStatus` for items lacking an operational mapping.
        mapped_items: List of `ItemMappingStatus` for items successfully mapped to operational nodes.
    """

    epoch_id: Optional[str]
    epoch_internal_id: int
    scheduled_event_id: Optional[str]
    scheduled_event_internal_id: int
    activity_def_id: Optional[str]
    activity_def_internal_id: int
    status: str  # 'complete', 'incomplete', 'unmapped'
    unmapped_items: List[ItemMappingStatus]
    mapped_items: List[ItemMappingStatus]


class StudyAlignmentReport(BaseModel):
    """
    Comprehensive alignment report analyzing the mapping between study epochs and CRFs.

    Attributes:
        study_id: The unique identifier of the study being evaluated.
        complete_activities: Activities where all required items are mapped successfully.
        incomplete_activities: Activities with partially mapped items.
        unmapped_activities: Activities completely lacking any mapped items.
        unmapped_odm_items: ODM nodes present but not associated with any active activity item.
        unmapped_crf_item_values: CRF items/values present but not associated with any activity definition.
    """

    study_id: str
    complete_activities: List[ActivityReport]
    incomplete_activities: List[ActivityReport]
    unmapped_activities: List[ActivityReport]
    unmapped_odm_items: List[Dict[str, Any]]
    unmapped_crf_item_values: List[Dict[str, Any]]


async def generate_alignment_report(study_id: str) -> StudyAlignmentReport:
    """
    Orchestrates the entire alignment validation for a given study and builds a final report.

    Fetches the study directly from the OpenStudyBuilder API instead of making direct DB queries,
    then parses the study using the official USDM library to identify unmapped activities.

    Args:
        study_id (str): The string identifier of the study to evaluate.

    Returns:
        StudyAlignmentReport: A comprehensive report model containing structural discrepancies.
    """
    import json

    import defusedxml.ElementTree as ET

    base_url = os.getenv("STUDY_REGISTRY_URL", "http://localhost:8000")

    async with httpx.AsyncClient() as client:
        response = await client.get(
            f"{base_url}/usdm/v4/studies/{study_id}", timeout=5.0
        )
        response.raise_for_status()
        data = response.json()

    # Attempt to locate ODM payload (Requirement 1 & 2)
    odm_data = None
    if isinstance(data, dict):
        if "odm" in data:
            odm_data = data["odm"]
        elif "odm_payload" in data:
            odm_data = data["odm_payload"]

    if odm_data is None:
        try:
            async with httpx.AsyncClient() as client:
                odm_resp = await client.get(
                    f"{base_url}/usdm/v4/studies/{study_id}/odm", timeout=2.0
                )
                if odm_resp.status_code == 200:
                    odm_data = odm_resp.json()
        except Exception:
            pass

    if odm_data is None:
        try:
            async with httpx.AsyncClient() as client:
                odm_resp = await client.get(
                    f"{base_url}/odm/v1/studies/{study_id}", timeout=2.0
                )
                if odm_resp.status_code == 200:
                    odm_data = odm_resp.json()
        except Exception:
            pass

    # Flatten dictionaries recursively (Requirement 2)
    def flatten_dict(d: Any, parent_key: str = "", sep: str = ".") -> Dict[str, Any]:
        items: List[Tuple[str, Any]] = []
        if isinstance(d, dict):
            for k, v in d.items():
                new_key = f"{parent_key}{sep}{k}" if parent_key else k
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        elif isinstance(d, list):
            for i, v in enumerate(d):
                new_key = f"{parent_key}{sep}[{i}]" if parent_key else f"[{i}]"
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        elif hasattr(d, "__dict__"):
            for k, v in d.__dict__.items():
                if not k.startswith("_"):
                    new_key = f"{parent_key}{sep}{k}" if parent_key else k
                    items.extend(flatten_dict(v, new_key, sep=sep).items())
        elif hasattr(d, "dict") and callable(getattr(d, "dict")):
            for k, v in d.dict().items():
                new_key = f"{parent_key}{sep}{k}" if parent_key else k
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        elif hasattr(d, "model_dump") and callable(getattr(d, "model_dump")):
            for k, v in d.model_dump().items():
                new_key = f"{parent_key}{sep}{k}" if parent_key else k
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        else:
            items.append((parent_key, d))
        return dict(items)

    def xml_to_dict(element: Any) -> Dict[str, Any]:
        tag = element.tag.split("}")[-1] if "}" in element.tag else element.tag
        result: Dict[str, Any] = {}
        for k, v in element.attrib.items():
            result[f"@{k}"] = v
        children = list(element)
        if children:
            child_dicts = []
            for child in children:
                child_dicts.append(xml_to_dict(child))
            grouped: Dict[str, List[Any]] = {}
            for cd in child_dicts:
                for ck, cv in cd.items():
                    if ck not in grouped:
                        grouped[ck] = []
                    grouped[ck].append(cv)
            for ck, cv in grouped.items():
                if len(cv) == 1:
                    result[ck] = cv[0]
                else:
                    result[ck] = cv
        else:
            if element.text and element.text.strip():
                result["#text"] = element.text.strip()
        return {tag: result}

    flat_odm = {}
    if odm_data:
        if isinstance(odm_data, str):
            odm_data_str = odm_data.strip()
            if odm_data_str.startswith("<"):
                try:
                    xml_root = ET.fromstring(odm_data_str.encode("utf-8"))
                    nested_odm_dict = xml_to_dict(xml_root)
                    flat_odm = flatten_dict(nested_odm_dict)
                except Exception:
                    pass
            elif odm_data_str.startswith("{") or odm_data_str.startswith("["):
                try:
                    loaded_json = json.loads(odm_data_str)
                    flat_odm = flatten_dict(loaded_json)
                except Exception:
                    pass
        else:
            flat_odm = flatten_dict(odm_data)

    def extract_activity_items(act: Any) -> List[Dict[str, Any]]:
        items = []
        bc_ids = getattr(act, "biomedicalConceptIds", None) or []
        for bc_id in bc_ids:
            if bc_id:
                items.append({"item_id": str(bc_id), "internal_id": 0})
        child_ids = getattr(act, "childIds", None) or []
        for cid in child_ids:
            if cid:
                items.append({"item_id": str(cid), "internal_id": 0})
        procedures = getattr(act, "definedProcedures", None) or []
        for proc in procedures:
            proc_id = getattr(proc, "id", None)
            if proc_id:
                items.append({"item_id": str(proc_id), "internal_id": 0})
        return items

    # Use official USDM python standard package (Requirement 1)
    study = usdm_model.Study(**data)

    complete_activities = []
    incomplete_activities = []
    unmapped_activities = []
    all_usdm_item_ids = set()
    all_usdm_activity_ids = set()

    if study.versions:
        for version in study.versions:
            if version.studyDesigns:
                for design in version.studyDesigns:
                    activities_by_id = {}
                    if design.activities:
                        for act in design.activities:
                            if act.id:
                                activities_by_id[act.id] = act
                                all_usdm_activity_ids.add(act.id)

                    epoch_indices = {}
                    if design.epochs:
                        for idx, ep in enumerate(design.epochs):
                            if ep.id:
                                epoch_indices[ep.id] = idx + 100

                    encounter_indices = {}
                    if design.encounters:
                        for idx, enc in enumerate(design.encounters):
                            if enc.id:
                                encounter_indices[enc.id] = idx + 200

                    if design.scheduleTimelines:
                        for timeline in design.scheduleTimelines:
                            instances = timeline.instances or []
                            for inst in instances:
                                epoch_id = inst.epochId
                                epoch_internal_id = epoch_indices.get(epoch_id, 0)
                                scheduled_event_id = inst.encounterId
                                scheduled_event_internal_id = encounter_indices.get(
                                    scheduled_event_id, 0
                                )

                                activity_ids = inst.activityIds or []
                                for act_id in activity_ids:
                                    act = activities_by_id.get(act_id)
                                    if not act:
                                        continue

                                    items = extract_activity_items(act)
                                    mapped_items = []
                                    unmapped_items = []

                                    if not items:
                                        is_act_mapped = False
                                        for flat_k, flat_v in flat_odm.items():
                                            if str(flat_v) == str(act_id) or str(
                                                act_id
                                            ) in str(flat_k):
                                                is_act_mapped = True
                                                break
                                        item_status = ItemMappingStatus(
                                            item_id=act_id,
                                            internal_id=0,
                                            is_mapped=is_act_mapped,
                                        )
                                        if is_act_mapped:
                                            mapped_items.append(item_status)
                                        else:
                                            unmapped_items.append(item_status)
                                    else:
                                        for item in items:
                                            item_id = item["item_id"]
                                            all_usdm_item_ids.add(item_id)
                                            is_item_mapped = False
                                            for flat_k, flat_v in flat_odm.items():
                                                if str(flat_v) == str(item_id) or str(
                                                    item_id
                                                ) in str(flat_k):
                                                    is_item_mapped = True
                                                    break
                                            item_status = ItemMappingStatus(
                                                item_id=item_id,
                                                internal_id=item["internal_id"],
                                                is_mapped=is_item_mapped,
                                            )
                                            if is_item_mapped:
                                                mapped_items.append(item_status)
                                            else:
                                                unmapped_items.append(item_status)

                                    if not unmapped_items:
                                        status = "complete"
                                    elif not mapped_items:
                                        status = "unmapped"
                                    else:
                                        status = "incomplete"

                                    report_item = ActivityReport(
                                        epoch_id=epoch_id,
                                        epoch_internal_id=epoch_internal_id,
                                        scheduled_event_id=scheduled_event_id,
                                        scheduled_event_internal_id=scheduled_event_internal_id,
                                        activity_def_id=act_id,
                                        activity_def_internal_id=0,
                                        status=status,
                                        unmapped_items=unmapped_items,
                                        mapped_items=mapped_items,
                                    )

                                    if status == "complete":
                                        complete_activities.append(report_item)
                                    elif status == "incomplete":
                                        incomplete_activities.append(report_item)
                                    else:
                                        unmapped_activities.append(report_item)

                    processed_activity_ids = {
                        rep.activity_def_id
                        for rep in complete_activities
                        + incomplete_activities
                        + unmapped_activities
                        if rep.epoch_id is not None
                    }
                    for act in design.activities or []:
                        if act.id not in processed_activity_ids:
                            items = extract_activity_items(act)
                            mapped_items = []
                            unmapped_items = []

                            if not items:
                                is_act_mapped = False
                                for flat_k, flat_v in flat_odm.items():
                                    if str(flat_v) == str(act.id) or str(act.id) in str(
                                        flat_k
                                    ):
                                        is_act_mapped = True
                                        break
                                item_status = ItemMappingStatus(
                                    item_id=act.id,
                                    internal_id=0,
                                    is_mapped=is_act_mapped,
                                )
                                if is_act_mapped:
                                    mapped_items.append(item_status)
                                else:
                                    unmapped_items.append(item_status)
                            else:
                                for item in items:
                                    item_id = item["item_id"]
                                    all_usdm_item_ids.add(item_id)
                                    is_item_mapped = False
                                    for flat_k, flat_v in flat_odm.items():
                                        if str(flat_v) == str(item_id) or str(
                                            item_id
                                        ) in str(flat_k):
                                            is_item_mapped = True
                                            break
                                    item_status = ItemMappingStatus(
                                        item_id=item_id,
                                        internal_id=item["internal_id"],
                                        is_mapped=is_item_mapped,
                                    )
                                    if is_item_mapped:
                                        mapped_items.append(item_status)
                                    else:
                                        unmapped_items.append(item_status)

                            if not unmapped_items:
                                status = "complete"
                            elif not mapped_items:
                                status = "unmapped"
                            else:
                                status = "incomplete"

                            report_item = ActivityReport(
                                epoch_id=None,
                                epoch_internal_id=0,
                                scheduled_event_id=None,
                                scheduled_event_internal_id=0,
                                activity_def_id=act.id,
                                activity_def_internal_id=0,
                                status=status,
                                unmapped_items=unmapped_items,
                                mapped_items=mapped_items,
                            )
                            if status == "complete":
                                complete_activities.append(report_item)
                            elif status == "incomplete":
                                incomplete_activities.append(report_item)
                            else:
                                unmapped_activities.append(report_item)

    unmapped_odm_items = []
    seen_odm_ids = set()
    for flat_k, flat_v in flat_odm.items():
        is_id_key = False
        if (
            flat_k.endswith("OID")
            or flat_k.endswith("@OID")
            or flat_k.endswith(".id")
            or flat_k.endswith("@id")
        ):
            is_id_key = True
        elif "ItemDef" in flat_k and (flat_k.endswith("OID") or flat_k.endswith("id")):
            is_id_key = True

        if is_id_key and flat_v:
            odm_item_id = str(flat_v)
            if odm_item_id not in seen_odm_ids:
                seen_odm_ids.add(odm_item_id)
                if (
                    odm_item_id not in all_usdm_item_ids
                    and odm_item_id not in all_usdm_activity_ids
                ):
                    name_key = (
                        flat_k.replace("OID", "Name")
                        .replace("@OID", "@Name")
                        .replace("id", "name")
                        .replace("@id", "@name")
                    )
                    odm_item_name = flat_odm.get(name_key) or odm_item_id
                    unmapped_odm_items.append(
                        {"item_id": odm_item_id, "name": str(odm_item_name)}
                    )

    return StudyAlignmentReport(
        study_id=str(study.id),
        complete_activities=complete_activities,
        incomplete_activities=incomplete_activities,
        unmapped_activities=unmapped_activities,
        unmapped_odm_items=unmapped_odm_items,
        unmapped_crf_item_values=[],
    )


def validate_usdm_structure(
    raw_text: str, override: Optional[str] = None
) -> USDMValidationReport:
    """
    Validates the structure of a raw USDM payload (JSON or YAML), inferring its version
    (or using an override), normalizes it to the canonical lineage matching usdm_model.Study,
    and returns a typed validation report.

    This implements Task 3 schema validation with typed reports inside apps/designer/validator.py.
    """
    return validate_usdm_payload(raw_text, override=override)
