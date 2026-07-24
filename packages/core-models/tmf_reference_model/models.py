"""
Models for the canonical, versioned DIA TMF Reference Model catalog.
These models represent Zones, Sections, Artifacts, and their versioned collections
following standard Pydantic v2 conventions and 21 CFR Part 11 style principles.
"""

from typing import Dict, List, Optional
from pydantic import BaseModel, Field


class Zone(BaseModel):
    """
    Represents a DIA TMF Reference Model Zone.
    Examples include: Zone 1 (Trial Management), Zone 2 (Central Trial Documents), etc.
    """
    model_config = {"frozen": True}

    number: int = Field(..., description="The unique canonical zone number (e.g., 1 to 11)")
    name: str = Field(..., description="The official name of the TMF Zone")


class Section(BaseModel):
    """
    Represents a DIA TMF Reference Model Section within a specific Zone.
    Examples include: Section 1.1 (Protocol) under Zone 1.
    """
    model_config = {"frozen": True}

    number: str = Field(..., description="The hierarchical section number (e.g., '1.1')")
    name: str = Field(..., description="The descriptive name of the section")
    zone_number: int = Field(..., description="The unique number of the parent Zone")


class Artifact(BaseModel):
    """
    Represents an approved, standard DIA TMF Reference Model Artifact.
    Each artifact uniquely maps to its parent section and zone.
    """
    model_config = {"frozen": True}

    code: str = Field(..., description="The unique, stable identifier code of the artifact (e.g., '01.01.01')")
    name: str = Field(..., description="The canonical display name of the artifact (e.g., 'Approved Protocol')")
    section_number: str = Field(..., description="The number of the parent Section")
    zone_number: int = Field(..., description="The number of the parent Zone")


class TaxonomyCatalog(BaseModel):
    """
    Represents a named, immutable version of the DIA TMF Reference Model taxonomy.
    Avoids runtime database dependencies while ensuring high-performance static lookups.
    """
    model_config = {"frozen": True}

    version: str = Field(..., description="The unique version tag of this taxonomy catalog (e.g., 'v3.2.0')")
    zones: Dict[int, Zone] = Field(default_factory=dict, description="Zones keyed by their integer zone numbers")
    sections: Dict[str, Section] = Field(default_factory=dict, description="Sections keyed by their section numbers")
    artifacts: Dict[str, Artifact] = Field(default_factory=dict, description="Artifacts keyed by their unique stable codes")
    artifact_name_map: Dict[str, Artifact] = Field(
        default_factory=dict,
        description="Artifacts keyed by lowercase name for robust, case-insensitive lookups"
    )

    def get_artifact(self, code_or_name: str) -> Optional[Artifact]:
        """
        Retrieves a TMF Artifact from the catalog by its stable code or case-insensitive name.
        """
        # Attempt direct lookup by stable code
        art = self.artifacts.get(code_or_name)
        if art:
            return art
        # Fallback to case-insensitive name lookup
        return self.artifact_name_map.get(code_or_name.strip().lower())

    def get_zone(self, zone_number: int) -> Optional[Zone]:
        """
        Retrieves a TMF Zone by its unique zone number.
        """
        return self.zones.get(zone_number)

    def get_section(self, section_number: str) -> Optional[Section]:
        """
        Retrieves a TMF Section by its section number.
        """
        return self.sections.get(section_number)

    def get_sections_for_zone(self, zone_number: int) -> List[Section]:
        """
        Retrieves all TMF Sections belonging to a specific Zone.
        """
        return sorted(
            [sec for sec in self.sections.values() if sec.zone_number == zone_number],
            key=lambda s: s.number
        )

    def get_artifacts_for_section(self, section_number: str) -> List[Artifact]:
        """
        Retrieves all TMF Artifacts belonging to a specific Section.
        """
        return sorted(
            [art for art in self.artifacts.values() if art.section_number == section_number],
            key=lambda a: a.code
        )
