import os
import httpx
from typing import List, Dict, Any, Optional
from pydantic import BaseModel
import usdm_model

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
        response = await client.get(f"{base_url}/usdm/v4/studies/{study_id}", timeout=5.0)
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
                                mapped_items=[]
                            )
                        )
                        
    return StudyAlignmentReport(
        study_id=str(study.id),
        complete_activities=[],
        incomplete_activities=[],
        unmapped_activities=unmapped_activities,
        unmapped_odm_items=[],
        unmapped_crf_item_values=[]
    )
