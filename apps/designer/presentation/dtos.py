"""
Pydantic DTO schemas for Designer presentation layer.
"""

from typing import Any

from pydantic import BaseModel

from apps.designer.validator import CodeValidationState


class TerminologyConcept(BaseModel):
    """
    Normalized terminology concept details.
    """

    code: str
    decode: str
    system: str
    valid: bool


class TerminologySearchResponse(BaseModel):
    """
    Response model for search and autocomplete queries.
    """

    query: str
    state: CodeValidationState
    results: list[TerminologyConcept]
    total_results: int
    error_message: str | None = None


class DifferenceResult(BaseModel):
    """
    Represents a field-level difference between two versions.
    """

    field: str
    old_value: Any
    new_value: Any


class VersionDiffResponse(BaseModel):
    """
    Response model for version diff comparison.
    """

    added_nodes: list[DifferenceResult]
    modified_nodes: list[DifferenceResult]
    deleted_nodes: list[DifferenceResult]


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
