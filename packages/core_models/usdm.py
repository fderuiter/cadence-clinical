from typing import List, Optional

from pydantic import BaseModel, Field


class Concept(BaseModel):
    """Represents a controlled terminology concept."""

    code: str
    decode: str
    system: str


class Activity(BaseModel):
    """Represents a clinical activity."""

    id: str
    name: str


class Visit(BaseModel):
    """Represents a clinical visit within an arm."""

    id: str
    name: str
    visit_type: Optional[Concept] = None
    activities: List[Activity] = Field(default_factory=list)


class Arm(BaseModel):
    """Represents a study arm."""

    id: str
    name: str
    arm_type: Optional[Concept] = None
    visits: List[Visit] = Field(default_factory=list)


class StudyDefinition(BaseModel):
    """Represents a fully compliant USDM Study Definition."""

    id: str
    name: str
    version: str = "1.0.0"
    description: Optional[str] = None
    arms: List[Arm] = Field(default_factory=list)
