from enum import Enum
from datetime import datetime
from typing import Optional, List, Union, Any
from pydantic import BaseModel, model_validator, field_validator
from apps.execution.database.models import DictionaryType, ImportState

class DictTypeEnum(str, Enum):
    MEDDRA = DictionaryType.MEDDRA.value
    WHODRUG = DictionaryType.WHODRUG.value
    LOINC = DictionaryType.LOINC.value
    SNOMED = DictionaryType.SNOMED.value

class JobStatusEnum(str, Enum):
    PENDING = ImportState.PENDING.value
    PROCESSING = ImportState.PROCESSING.value
    COMPLETED = ImportState.COMPLETED.value
    FAILED = ImportState.FAILED.value

class PrimarySocFlagEnum(str, Enum):
    Y = "Y"
    N = "N"

class JobStatusResponse(BaseModel):
    job_id: str
    dictionary_type: str
    version: str
    status: JobStatusEnum
    started_at: datetime
    completed_at: Optional[datetime] = None
    progress_percentage: Optional[int] = None
    records_imported: Optional[int] = None
    errors_encountered: Optional[int] = None

class WHODrugATCContext(BaseModel):
    atc_code: str
    description: str

class WHODrugIngredientItem(BaseModel):
    ingredient_code: str
    ingredient_name: str

class ImpactAnalysisRequest(BaseModel):
    dictionary_type: str
    new_version: str

    @field_validator("new_version")
    @classmethod
    def check_new_version(cls, v: str) -> str:
        return validate_non_blank_version(v)

class ImpactAnalysisResponse(BaseModel):
    status: str
    dictionary_type: str
    new_version: str
    metrics: dict[str, int]

class CodingAssignmentResponse(BaseModel):
    id: str
    verbatim_text: str
    source_field: Optional[str] = None
    observation_id: Optional[str] = None
    dictionary_type: str
    dictionary_version: str
    coded_code: Optional[str] = None
    coded_term: Optional[str] = None
    status: str
    recoding_status: str
    assigned_by: Optional[str] = None
    assigned_at: datetime
    score: Optional[float] = None
    hierarchy: Optional[Union[dict[str, Any], list[Any]]] = None
    suggestions: Optional[Union[list[Any], dict[str, Any]]] = None
    domain: Optional[str] = None
    version: int
    is_deleted: bool

class CoderActionRequest(BaseModel):
    action: str  # "ACCEPT" or "OVERRIDE" or "QUERY"
    code: Optional[str] = None  # required for OVERRIDE
    term: Optional[str] = None  # required for OVERRIDE
    suggestion_index: Optional[int] = None  # optional for ACCEPT
    reason_for_change: Optional[str] = None  # required for OVERRIDE

    @model_validator(mode="after")
    def validate_override(self) -> "CoderActionRequest":
        action_upper = self.action.upper() if self.action else ""
        if action_upper == "OVERRIDE":
            if not self.code or not self.code.strip():
                raise ValueError("code is required and cannot be blank for OVERRIDE action")
            if not self.term or not self.term.strip():
                raise ValueError("term is required and cannot be blank for OVERRIDE action")
            if not self.reason_for_change or not self.reason_for_change.strip():
                raise ValueError("reason_for_change is required and cannot be blank for OVERRIDE action")
        return self

def validate_non_blank_version(v: str) -> str:
    if not v or not v.strip():
        raise ValueError("Version must be a non-empty string.")
    return v

class DictionaryImportRequest(BaseModel):
    dictionary_type: DictTypeEnum
    version: str
    parse_multilingual: bool = True

    @field_validator("version")
    @classmethod
    def check_version(cls, v: str) -> str:
        return validate_non_blank_version(v)

class MedDRAMatch(BaseModel):
    llt_code: str
    llt_name: str
    pt_code: str
    pt_name: str
    hlt_code: str
    hlt_name: str
    hlgt_code: str
    hlgt_name: str
    soc_code: str
    soc_name: str
    primary_soc_flag: Optional[PrimarySocFlagEnum] = None
    score: float

class MedDRACodeLookupResponse(BaseModel):
    status: str
    matches: List[MedDRAMatch]

class WHODrugMatch(BaseModel):
    drug_code: str
    preferred_name: str
    drug_name: Optional[str] = None
    score: float
    atc_context: List[WHODrugATCContext] = []
    ingredients: List[WHODrugIngredientItem] = []

class WHODrugCodeLookupResponse(BaseModel):
    status: str
    matches: List[WHODrugMatch]

# Backward compatibility aliases
MedDRACodeMatch = MedDRAMatch
MedDRACodingResult = MedDRACodeLookupResponse
WHODrugCodeMatch = WHODrugMatch
WHODrugCodingResult = WHODrugCodeLookupResponse
