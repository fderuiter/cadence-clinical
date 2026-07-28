"""
Global Library Domain Contracts.

This module contains the Pydantic request and response models for reusable
clinical design objects such as Forms, Data Elements, Arms, and Visits.
It enforces strict schema validation, GxP-compliant audit trails, and
discriminated union payload structures.
"""

from datetime import datetime
from enum import Enum
from typing import List, Literal, Optional, Union

from pydantic import BaseModel, Field, field_validator
from typing_extensions import Annotated


class ObjectType(str, Enum):
    """Supported types of clinical design objects in the Global Library."""

    FORM = "FORM"
    DATA_ELEMENT = "DATA_ELEMENT"
    ARM = "ARM"
    VISIT = "VISIT"


class LibraryStatus(str, Enum):
    """Standard status levels for Global Library objects."""

    DRAFT = "DRAFT"
    IN_REVIEW = "IN_REVIEW"
    APPROVED = "APPROVED"
    PUBLISHED = "PUBLISHED"
    ARCHIVED = "ARCHIVED"
    REJECTED = "REJECTED"


# GxP governance allowed status transition map
ALLOWED_LIBRARY_TRANSITIONS = {
    LibraryStatus.DRAFT: {LibraryStatus.IN_REVIEW},
    LibraryStatus.IN_REVIEW: {LibraryStatus.APPROVED, LibraryStatus.REJECTED},
    LibraryStatus.APPROVED: {LibraryStatus.PUBLISHED},
    LibraryStatus.PUBLISHED: {LibraryStatus.ARCHIVED},
    LibraryStatus.REJECTED: {LibraryStatus.DRAFT},
}


# ==========================================
# Type-Specific Payload Components
# ==========================================


class FormItem(BaseModel):
    """Represents an individual question/field item within a Form."""

    item_id: str = Field(..., description="Unique stable ID for the form item.")
    name: str = Field(
        ..., description="Identifier name conformant with CDASH/SDTM standards."
    )
    question_text: str = Field(..., description="The user-facing prompt text.")
    data_type: str = Field(
        ..., description="Primitive data type (e.g., text, integer, choice, date)."
    )
    required: bool = Field(
        True, description="Indicates if the form item must be filled."
    )


class FormPayload(BaseModel):
    """Form-specific payload validation containing items."""

    items: List[FormItem] = Field(
        ..., description="List of form items/fields defined in this template."
    )


class DataElementPayload(BaseModel):
    """Data-element specific payload validation containing units and format."""

    data_type: str = Field(
        ..., description="Expected value type (e.g., numeric, text)."
    )
    allowable_units: List[str] = Field(
        ..., description="List of standard UCUM unit codes allowed."
    )
    default_unit: Optional[str] = Field(
        None, description="Default unit code from the allowable list."
    )

    @field_validator("default_unit")
    @classmethod
    def validate_default_unit_in_allowable(
        cls, v: Optional[str], info
    ) -> Optional[str]:
        """Ensure the default unit is present in the list of allowable units if specified."""
        allowable = info.data.get("allowable_units")
        if v is not None and allowable is not None and v not in allowable:
            raise ValueError(
                f"default_unit '{v}' must be one of the allowable_units: {allowable}"
            )
        return v


class ArmAttributes(BaseModel):
    """Attributes defining a study arm."""

    arm_type: str = Field(
        ..., description="The classification of arm (e.g., TREATMENT, PLACEBO)."
    )
    target_sample_size: int = Field(
        ..., description="Target number of subjects planned for this arm."
    )
    randomization_ratio: str = Field(
        ..., description="Allocation ratio (e.g., '1:1', '2:1')."
    )


class ArmPayload(BaseModel):
    """Arm-specific payload validation containing arm attributes."""

    attributes: ArmAttributes = Field(..., description="Clinical arm configurations.")


class VisitAttributes(BaseModel):
    """Attributes defining a study visit."""

    visit_type: str = Field(
        ...,
        description="The scheduling type of visit (e.g., SCREENING, SCHEDULED, UNSCHEDULED).",
    )
    planned_day: int = Field(
        ..., description="Target timeline day relative to randomization/enrollment."
    )
    window_days: int = Field(
        ...,
        description="Allowable margin of days around the planned day (e.g., ±3 days).",
    )


class VisitPayload(BaseModel):
    """Visit-specific payload validation containing visit attributes."""

    attributes: VisitAttributes = Field(
        ..., description="Clinical visit configurations."
    )


# ==========================================
# Mutation Input Request Models
# ==========================================


def validate_non_empty_string(v: str) -> str:
    """Helper validator to ensure change reason strings are non-empty and non-blank."""
    if not isinstance(v, str) or not v.strip():
        raise ValueError("Change reason cannot be empty or consist only of whitespace.")
    return v


class CreateLibraryObjectBase(BaseModel):
    """Base model for creating library objects with mandatory change justification."""

    id: str = Field(..., description="Stable, unique global library ID.")
    version: str = Field("1.0.0", description="Initial version code.")
    status: LibraryStatus = Field(
        LibraryStatus.DRAFT, description="Initial library state."
    )
    sponsor_id: str = Field(..., description="Sponsor / Tenant identifier.")
    change_reason: str = Field(
        ..., description="Mandatory reason for change / audit trail justification."
    )

    @field_validator("change_reason")
    @classmethod
    def validate_change_reason_non_empty(cls, v: str) -> str:
        return validate_non_empty_string(v)


class CreateFormRequest(CreateLibraryObjectBase):
    """Request model for creating a Form library object."""

    object_type: Literal[ObjectType.FORM] = ObjectType.FORM
    payload: FormPayload


class CreateDataElementRequest(CreateLibraryObjectBase):
    """Request model for creating a Data Element library object."""

    object_type: Literal[ObjectType.DATA_ELEMENT] = ObjectType.DATA_ELEMENT
    payload: DataElementPayload


class CreateArmRequest(CreateLibraryObjectBase):
    """Request model for creating an Arm library object."""

    object_type: Literal[ObjectType.ARM] = ObjectType.ARM
    payload: ArmPayload


class CreateVisitRequest(CreateLibraryObjectBase):
    """Request model for creating a Visit library object."""

    object_type: Literal[ObjectType.VISIT] = ObjectType.VISIT
    payload: VisitPayload


# Discriminated Union for Creation Requests
CreateLibraryObjectRequest = Annotated[
    Union[
        CreateFormRequest,
        CreateDataElementRequest,
        CreateArmRequest,
        CreateVisitRequest,
    ],
    Field(discriminator="object_type"),
]


class UpdateLibraryObjectBase(BaseModel):
    """Base model for updating library objects with mandatory change justification."""

    reason_for_change: str = Field(
        ..., description="Mandatory reason for change / audit trail justification."
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason_for_change_non_empty(cls, v: str) -> str:
        return validate_non_empty_string(v)


class UpdateFormRequest(UpdateLibraryObjectBase):
    """Request model for updating a Form library object."""

    object_type: Literal[ObjectType.FORM] = ObjectType.FORM
    payload: FormPayload


class UpdateDataElementRequest(UpdateLibraryObjectBase):
    """Request model for updating a Data Element library object."""

    object_type: Literal[ObjectType.DATA_ELEMENT] = ObjectType.DATA_ELEMENT
    payload: DataElementPayload


class UpdateArmRequest(UpdateLibraryObjectBase):
    """Request model for updating an Arm library object."""

    object_type: Literal[ObjectType.ARM] = ObjectType.ARM
    payload: ArmPayload


class UpdateVisitRequest(UpdateLibraryObjectBase):
    """Request model for updating a Visit library object."""

    object_type: Literal[ObjectType.VISIT] = ObjectType.VISIT
    payload: VisitPayload


# Discriminated Union for Update Requests
UpdateLibraryObjectRequest = Annotated[
    Union[
        UpdateFormRequest,
        UpdateDataElementRequest,
        UpdateArmRequest,
        UpdateVisitRequest,
    ],
    Field(discriminator="object_type"),
]


class LibraryObjectTransitionRequest(BaseModel):
    """Request model for transitioning library object lifecycle status."""

    status: LibraryStatus = Field(
        ..., description="Target status level for transition."
    )
    change_reason: str = Field(
        ..., description="Mandatory reason for change / audit trail justification."
    )

    @field_validator("change_reason")
    @classmethod
    def validate_change_reason_non_empty(cls, v: str) -> str:
        return validate_non_empty_string(v)


# ==========================================
# Response / Output Models
# ==========================================


class LibraryObjectBase(BaseModel):
    """Base response model exposing audit trail, tenant, version, and status metadata."""

    id: str = Field(..., description="Stable, unique global library ID.")
    version: str = Field(..., description="Semantic version of the library object.")
    status: LibraryStatus = Field(
        ..., description="Workflow review status of the object."
    )
    sponsor_id: str = Field(..., description="Sponsor identifier.")
    tenant_id: str = Field(..., description="Tenant / Partition identifier.")
    created_at: datetime = Field(..., description="Audit timestamp of creation.")
    created_by: str = Field(..., description="User ID who created this object.")
    updated_at: Optional[datetime] = Field(
        None, description="Audit timestamp of last update."
    )
    updated_by: Optional[str] = Field(None, description="User ID of last updater.")
    reason_for_change: Optional[str] = Field(
        None, description="Detailed explanation of changes applied."
    )
    prior_status: Optional[str] = Field(
        None, description="Previous status before transition."
    )


class FormLibraryObjectDetail(LibraryObjectBase):
    """Response model for a Form library object."""

    object_type: Literal[ObjectType.FORM] = ObjectType.FORM
    payload: FormPayload


class DataElementLibraryObjectDetail(LibraryObjectBase):
    """Response model for a Data Element library object."""

    object_type: Literal[ObjectType.DATA_ELEMENT] = ObjectType.DATA_ELEMENT
    payload: DataElementPayload


class ArmLibraryObjectDetail(LibraryObjectBase):
    """Response model for an Arm library object."""

    object_type: Literal[ObjectType.ARM] = ObjectType.ARM
    payload: ArmPayload


class VisitLibraryObjectDetail(LibraryObjectBase):
    """Response model for a Visit library object."""

    object_type: Literal[ObjectType.VISIT] = ObjectType.VISIT
    payload: VisitPayload


# Discriminated Union for Responses exposing type-specific details
LibraryObjectDetail = Annotated[
    Union[
        FormLibraryObjectDetail,
        DataElementLibraryObjectDetail,
        ArmLibraryObjectDetail,
        VisitLibraryObjectDetail,
    ],
    Field(discriminator="object_type"),
]
