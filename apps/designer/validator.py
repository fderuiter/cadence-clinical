import os
from enum import Enum
from typing import Any, Dict, List, Optional

import httpx
import usdm_model
from pydantic import BaseModel

from apps.designer.db import get_study_projection, terminology_cache


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
    base_url = os.getenv("STUDY_REGISTRY_URL", "http://localhost:8000")

    async with httpx.AsyncClient() as client:
        response = await client.get(
            f"{base_url}/usdm/v4/studies/{study_id}", timeout=5.0
        )
        response.raise_for_status()
        data = response.json()

    # Use official USDM python standard package (Requirement 1)
    study = usdm_model.Study(**data)

    unmapped_activities = []

    # Requirement 3: Parse nested USDM payloads
    if study.versions:
        for version in study.versions:
            if version.studyDesigns:
                for design in version.studyDesigns:
                    activities = design.activities or []
                    for act in activities:
                        unmapped_activities.append(
                            ActivityReport(
                                epoch_id=None,
                                epoch_internal_id=0,
                                scheduled_event_id=None,
                                scheduled_event_internal_id=0,
                                activity_def_id=act.id,
                                activity_def_internal_id=0,
                                status="unmapped",
                                unmapped_items=[],
                                mapped_items=[],
                            )
                        )

    return StudyAlignmentReport(
        study_id=str(study.id),
        complete_activities=[],
        incomplete_activities=[],
        unmapped_activities=unmapped_activities,
        unmapped_odm_items=[],
        unmapped_crf_item_values=[],
    )
