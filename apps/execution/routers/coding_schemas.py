from datetime import datetime
from enum import Enum
from typing import Any, List, Literal, Optional, Union

from pydantic import BaseModel, ValidationInfo, field_validator, model_validator


# Enums
class DictTypeEnum(str, Enum):
    MEDDRA = "MEDDRA"
    WHODRUG = "WHODRUG"
    LOINC = "LOINC"
    SNOMED = "SNOMED"


class JobStatusEnum(str, Enum):
    PENDING = "PENDING"
    PROCESSING = "PROCESSING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"


class PrimarySocFlagEnum(str, Enum):
    Y = "Y"
    N = "N"


# Request Models
class DictionaryImportRequest(BaseModel):
    dictionary_type: DictTypeEnum
    version: str
    parse_multilingual: bool = True

    @model_validator(mode="after")
    def validate_dictionary_type(self) -> "DictionaryImportRequest":
        if self.dictionary_type not in (DictTypeEnum.MEDDRA, DictTypeEnum.WHODRUG):
            raise ValueError(
                f"Unsupported dictionary type: {self.dictionary_type.value}"
            )
        return self


class CoderActionRequest(BaseModel):
    action: str  # "ACCEPT" or "OVERRIDE" or "QUERY"
    code: Optional[str] = None  # required for OVERRIDE
    term: Optional[str] = None  # required for OVERRIDE
    suggestion_index: Optional[int] = None  # optional for ACCEPT
    reason_for_change: Optional[str] = None  # required for OVERRIDE

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason_for_change_field(
        cls, v: Optional[str], info: ValidationInfo
    ) -> Optional[str]:
        action_upper = (info.data.get("action") or "").upper()
        if action_upper == "OVERRIDE":
            if not v or not v.strip():
                raise ValueError(
                    "reason_for_change is required for OVERRIDE action and cannot be empty."
                )
        return v

    @model_validator(mode="after")
    def validate_override_fields(self) -> "CoderActionRequest":
        action_upper = (self.action or "").upper()
        if action_upper == "OVERRIDE":
            if not self.reason_for_change or not self.reason_for_change.strip():
                raise ValueError(
                    "reason_for_change is required for OVERRIDE action and cannot be empty."
                )
            if not self.code or not self.code.strip():
                raise ValueError("code is required for OVERRIDE action.")
            if not self.term or not self.term.strip():
                raise ValueError("term is required for OVERRIDE action.")
        return self


class ImpactAnalysisRequest(BaseModel):
    dictionary_type: DictTypeEnum
    new_version: str

    @model_validator(mode="after")
    def validate_dictionary_type(self) -> "ImpactAnalysisRequest":
        if self.dictionary_type not in (DictTypeEnum.MEDDRA, DictTypeEnum.WHODRUG):
            raise ValueError(
                f"Unsupported dictionary type: {self.dictionary_type.value}"
            )
        return self


# Response Models
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
    status: Literal["AUTO-CODED", "SUGGESTIONS", "UNCODABLE"]
    matches: List[MedDRAMatch]


# For backward compatibility
MedDRACodeMatch = MedDRAMatch
MedDRACodingResult = MedDRACodeLookupResponse


class WHODrugIngredientItem(BaseModel):
    code: Optional[str] = None
    name: Optional[str] = None
    ingredient_code: Optional[str] = None
    ingredient_name: Optional[str] = None


class WHODrugATCContext(BaseModel):
    code: Optional[str] = None
    text: Optional[str] = None
    atc_code: Optional[str] = None
    description: Optional[str] = None


class WHODrugMatch(BaseModel):
    code: Optional[str] = None
    name: Optional[str] = None
    drug_code: Optional[str] = None
    preferred_name: Optional[str] = None
    drug_name: Optional[str] = None
    score: float
    ingredients: List[WHODrugIngredientItem] = []
    atc: List[WHODrugATCContext] = []
    atc_context: List[WHODrugATCContext] = []


class WHODrugCodeLookupResponse(BaseModel):
    status: Literal["AUTO-CODED", "SUGGESTIONS", "UNCODABLE"]
    matches: List[WHODrugMatch]


# For backward compatibility
WHODrugCodeMatch = WHODrugMatch
WHODrugCodingResult = WHODrugCodeLookupResponse


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


class ImpactMetrics(BaseModel):
    unchanged: int = 0
    reclassified: int = 0
    deprecated: int = 0
    skipped: int = 0
    verbatim_terms_affected: Optional[int] = None
    coded_terms_affected: Optional[int] = None
    uncodable_terms: Optional[int] = None


class ImpactAnalysisResponse(BaseModel):
    status: Literal["success"]
    dictionary_type: DictTypeEnum
    new_version: str
    metrics: ImpactMetrics

