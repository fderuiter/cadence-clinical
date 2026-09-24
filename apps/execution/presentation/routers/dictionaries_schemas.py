from enum import StrEnum

from pydantic import BaseModel

from apps.execution.presentation.routers.coding_schemas import *  # noqa: F403


class UCUMConvertRequest(BaseModel):
    value: float
    source_unit: str
    target_unit: str


class UCUMUnitValue(BaseModel):
    value: float
    unit: str


class UCUMConvertResponse(BaseModel):
    source: UCUMUnitValue
    target: UCUMUnitValue
    is_compatible: bool
    scale_factor: float
    offset: float | None = None


class InvalidParam(BaseModel):
    field: str | None = None
    reason: str | None = None
    value: str | None = None


class ProblemDetails(BaseModel):
    type: str
    title: str
    status: int
    detail: str
    instance: str
    code: str
    invalid_params: list[InvalidParam] | None = None


class MedDRATargetLevelEnum(StrEnum):
    LLT = "LLT"
    PT = "PT"
