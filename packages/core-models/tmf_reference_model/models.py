"""
Pydantic v2 domain models for the versioned DIA TMF Reference Model taxonomy.
All records are designed to be immutable at runtime to ensure GxP data integrity.
"""

from typing import Dict, List, Optional
from pydantic import BaseModel, Field


class Zone(BaseModel):
    """
    Represents a DIA TMF Reference Model Zone (1 through 11).
    """

    number: int = Field(..., description="DIA TMF Zone Number (1-11)")
    name: str = Field(..., description="DIA TMF Zone Name")

    model_config = {
        "frozen": True,
        "json_schema_extra": {
            "example": {"number": 1, "name": "Trial Management"}
        },
    }


class Section(BaseModel):
    """
    Represents a specific Section within a TMF Zone.
    """

    code: str = Field(..., description="Section Code (e.g., '1.1')")
    name: str = Field(..., description="Section Name")
    zone_number: int = Field(..., description="Zone number of the parent zone")

    model_config = {
        "frozen": True,
        "json_schema_extra": {
            "example": {"code": "1.1", "name": "Protocol", "zone_number": 1}
        },
    }


class Artifact(BaseModel):
    """
    Represents a TMF Artifact, identifying its parent section and zone.
    """

    code: str = Field(..., description="Stable Artifact Code (e.g., '01.01.01')")
    name: str = Field(..., description="Canonical Display Name")
    section_code: str = Field(..., description="Section code of the parent section")
    zone_number: int = Field(..., description="Zone number of the parent zone")

    model_config = {
        "frozen": True,
        "json_schema_extra": {
            "example": {
                "code": "01.01.01",
                "name": "Approved Protocol",
                "section_code": "1.1",
                "zone_number": 1,
            }
        },
    }


class Catalog(BaseModel):
    """
    Represents a named taxonomy catalog version of the DIA TMF Reference Model,
    serving as an immutable shared source of truth.
    """

    version: str = Field(..., description="Taxonomy catalog version (e.g., 'v3.2.0')")
    zones: Dict[int, Zone] = Field(
        default_factory=dict, description="Zones indexed by number"
    )
    sections: Dict[str, Section] = Field(
        default_factory=dict, description="Sections indexed by code"
    )
    artifacts: Dict[str, Artifact] = Field(
        default_factory=dict, description="Artifacts indexed by code"
    )

    model_config = {"frozen": True}

    def get_zone(self, number: int) -> Optional[Zone]:
        """
        Retrieve a Zone by its number.
        """
        return self.zones.get(number)

    def get_section(self, code: str) -> Optional[Section]:
        """
        Retrieve a Section by its section code.
        """
        return self.sections.get(code)

    def get_artifact(self, code: str) -> Optional[Artifact]:
        """
        Retrieve an Artifact by its stable code.
        """
        return self.artifacts.get(code)

    def get_sections_by_zone(self, zone_number: int) -> List[Section]:
        """
        Retrieve all Sections belonging to a specific Zone.
        """
        return [
            sec for sec in self.sections.values() if sec.zone_number == zone_number
        ]

    def get_artifacts_by_zone(self, zone_number: int) -> List[Artifact]:
        """
        Retrieve all Artifacts belonging to a specific Zone.
        """
        return [
            art for art in self.artifacts.values() if art.zone_number == zone_number
        ]

    def get_artifacts_by_section(self, section_code: str) -> List[Artifact]:
        """
        Retrieve all Artifacts belonging to a specific Section.
        """
        return [
            art
            for art in self.artifacts.values()
            if art.section_code == section_code
        ]
