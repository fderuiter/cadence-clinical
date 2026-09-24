from datetime import datetime
from enum import StrEnum
from typing import Any, Literal

from pydantic import BaseModel, ValidationInfo, field_validator, model_validator

from apps.execution.database.models import DictionaryType, ImportState


# Enums
class DictTypeEnum(StrEnum):
    MEDDRA = DictionaryType.MEDDRA.value
    WHODRUG = DictionaryType.WHODRUG.value
    LOINC = DictionaryType.LOINC.value
    SNOMED = DictionaryType.SNOMED.value


class JobStatusEnum(StrEnum):
    PENDING = ImportState.PENDING.value
    PROCESSING = ImportState.PROCESSING.value
    COMPLETED = ImportState.COMPLETED.value
    FAILED = ImportState.FAILED.value


class PrimarySocFlagEnum(StrEnum):
    Y = "Y"
    N = "N"


# Request Models
class DictionaryImportRequest(BaseModel):
    dictionary_type: DictTypeEnum
    version: str
    parse_multilingual: bool = True

    @field_validator("version")
    @classmethod
    def validate_version_non_empty(cls, v: str) -> str:
        if not v or not v.strip():
            raise ValueError("Version must be a non-empty string.")
        return v

    @field_validator("dictionary_type")
    @classmethod
    def validate_dictionary_type(cls, v: DictTypeEnum) -> DictTypeEnum:
        if v not in (DictTypeEnum.MEDDRA, DictTypeEnum.WHODRUG):
            raise ValueError(f"Import not supported for dictionary type: {v.value}")
        return v


class CoderActionRequest(BaseModel):
    action: str  # "ACCEPT" or "OVERRIDE" or "QUERY"
    code: str | None = None  # required for OVERRIDE
    term: str | None = None  # required for OVERRIDE
    suggestion_index: int | None = None  # optional for ACCEPT
    reason_for_change: str | None = None  # required for OVERRIDE

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason_for_change_field(
        cls, v: str | None, info: ValidationInfo
    ) -> str | None:
        action_upper = (info.data.get("action") or "").upper()
        if action_upper == "OVERRIDE" and (not v or not v.strip()):
            raise ValueError(
                "reason_for_change is required for OVERRIDE action and cannot be empty."
            )
        return v

    @model_validator(mode="after")
    def validate_override_fields(self) -> CoderActionRequest:
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


class BatchAssignItem(BaseModel):
    assignment_id: str
    action: str = "ACCEPT"
    code: str | None = None
    term: str | None = None
    suggestion_index: int | None = None
    reason_for_change: str | None = None


class BatchAssignRequest(BaseModel):
    assignment_ids: list[str] | None = None
    items: list[BatchAssignItem] | None = None
    code: str | None = None
    term: str | None = None
    dictionary_type: str | None = None
    dictionary_version: str | None = None
    reason: str | None = None
    reason_for_change: str | None = None
    action: str = "ACCEPT"


class BatchAssignResultItem(BaseModel):
    assignment_id: str
    status: str
    coded_code: str | None = None
    coded_term: str | None = None
    error: str | None = None


class BatchAssignResponse(BaseModel):
    success_count: int
    failed_count: int
    results: list[dict[str, Any]] = []


class RaiseQueryRequest(BaseModel):
    query_text: str | None = None
    message: str | None = None
    explanation: str | None = None
    reason: str | None = None
    reason_for_change: str | None = None


class RaiseQueryResponse(BaseModel):
    query_id: str
    status: str
    assignment_id: str
    explanation: str | None = None


class ImpactAnalysisRequest(BaseModel):
    dictionary_type: DictTypeEnum
    new_version: str

    @field_validator("dictionary_type")
    @classmethod
    def validate_dictionary_type(cls, v: DictTypeEnum) -> DictTypeEnum:
        if v not in (DictTypeEnum.MEDDRA, DictTypeEnum.WHODRUG):
            raise ValueError(f"Unsupported dictionary type: {v.value}")
        return v


# Response Models
class JobStatusResponse(BaseModel):
    job_id: str
    dictionary_type: str
    version: str
    status: JobStatusEnum
    started_at: datetime
    completed_at: datetime | None = None
    progress_percentage: int | None = None
    records_imported: int | None = None
    errors_encountered: int | None = None


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
    primary_soc_flag: str | None = None
    score: float


class MedDRACodeLookupResponse(BaseModel):
    status: Literal["AUTO-CODED", "SUGGESTIONS", "UNCODABLE"]
    matches: list[MedDRAMatch]


# For backward compatibility
MedDRACodeMatch = MedDRAMatch
MedDRACodingResult = MedDRACodeLookupResponse


class WHODrugIngredientItem(BaseModel):
    ingredient_code: str
    ingredient_name: str
    code: str | None = None
    name: str | None = None


class WHODrugATCContext(BaseModel):
    atc_code: str
    description: str
    code: str | None = None
    text: str | None = None


class WHODrugMatch(BaseModel):
    drug_code: str
    preferred_name: str
    drug_name: str | None = None
    score: float
    ingredients: list[WHODrugIngredientItem] = []
    atc_context: list[WHODrugATCContext] = []
    code: str | None = None
    name: str | None = None
    atc: list[WHODrugATCContext] = []


class WHODrugCodeLookupResponse(BaseModel):
    status: Literal["AUTO-CODED", "SUGGESTIONS", "UNCODABLE"]
    matches: list[WHODrugMatch]


# For backward compatibility
WHODrugCodeMatch = WHODrugMatch
WHODrugCodingResult = WHODrugCodeLookupResponse


class CodingAssignmentResponse(BaseModel):
    id: str
    verbatim_text: str
    source_field: str | None = None
    observation_id: str | None = None
    dictionary_type: str
    dictionary_version: str
    coded_code: str | None = None
    coded_term: str | None = None
    status: str
    recoding_status: str
    assigned_by: str | None = None
    assigned_at: datetime
    score: float | None = None
    hierarchy: dict[str, Any] | list[Any] | None = None
    suggestions: list[Any] | dict[str, Any] | None = None
    domain: str | None = None
    version: int
    is_deleted: bool


class ImpactMetrics(BaseModel):
    unchanged: int = 0
    reclassified: int = 0
    deprecated: int = 0
    skipped: int = 0
    verbatim_terms_affected: int | None = None
    coded_terms_affected: int | None = None
    uncodable_terms: int | None = None


class ImpactAnalysisResponse(BaseModel):
    status: Literal["success"]
    dictionary_type: DictTypeEnum
    new_version: str
    metrics: ImpactMetrics
