from pydantic import BaseModel, Field
from typing import Optional, List

class Concept(BaseModel):
    code: str
    decode: str
    system: str

class Activity(BaseModel):
    id: str
    name: str

class Visit(BaseModel):
    id: str
    name: str
    visit_type: Optional[Concept] = None
    activities: List[Activity] = Field(default_factory=list)

class Arm(BaseModel):
    id: str
    name: str
    arm_type: Optional[Concept] = None
    visits: List[Visit] = Field(default_factory=list)

class StudyDefinition(BaseModel):
    id: str
    name: str
    version: str = "1.0.0"
    description: Optional[str] = None
    arms: List[Arm] = Field(default_factory=list)