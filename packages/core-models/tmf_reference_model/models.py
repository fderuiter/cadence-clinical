from typing import Dict, List, Optional

from pydantic import BaseModel, ConfigDict, Field


class Artifact(BaseModel):
    """
    Represents an approved Artifact in the DIA TMF Reference Model.
    """

    model_config = ConfigDict(frozen=True)

    code: str = Field(
        ..., description="Stable, unique code for the artifact (e.g. '01.01.01')"
    )
    name: str = Field(..., description="Canonical display name of the artifact")
    section_code: str = Field(..., description="Stable code of the parent section")
    zone_code: int = Field(..., description="Stable code of the parent zone")
    is_extension: bool = Field(
        default=False,
        description="Whether this artifact is a Cadence-specific extension",
    )


class Section(BaseModel):
    """
    Represents a Section within a DIA TMF Zone.
    """

    model_config = ConfigDict(frozen=True)

    code: str = Field(..., description="Stable code for the section (e.g. '01.01')")
    name: str = Field(..., description="Canonical display name of the section")
    zone_code: int = Field(..., description="Stable code of the parent zone")
    artifacts: List[Artifact] = Field(
        default_factory=list, description="List of approved artifacts in this section"
    )


class Zone(BaseModel):
    """
    Represents a Zone in the DIA TMF Reference Model.
    """

    model_config = ConfigDict(frozen=True)

    code: int = Field(
        ..., description="Stable, unique integer code for the zone (1 to 11)"
    )
    name: str = Field(..., description="Canonical display name of the zone")
    sections: List[Section] = Field(
        default_factory=list, description="List of sections in this zone"
    )


class TaxonomyCatalog(BaseModel):
    """
    An immutable taxonomy catalog representing a specific named version of the DIA TMF Reference Model.
    """

    model_config = ConfigDict(frozen=True)

    version: str = Field(
        ..., description="Unique version name of the catalog (e.g. 'v3.2.0')"
    )
    zones: List[Zone] = Field(
        ..., description="The 11 canonical zones included in this catalog version"
    )

    @property
    def artifact_map(self) -> Dict[str, Artifact]:
        """
        Helper property returning a map of artifact_code -> Artifact for quick lookup.
        """
        mapping = {}
        for zone in self.zones:
            for section in zone.sections:
                for artifact in section.artifacts:
                    mapping[artifact.code] = artifact
        return mapping

    def get_artifact(self, code: str) -> Optional[Artifact]:
        """
        Retrieve an artifact by its code.
        """
        return self.artifact_map.get(code)

    def get_section(self, code: str) -> Optional[Section]:
        """
        Retrieve a section by its code.
        """
        for zone in self.zones:
            for section in zone.sections:
                if section.code == code:
                    return section
        return None

    def get_zone(self, code: int) -> Optional[Zone]:
        """
        Retrieve a zone by its integer code.
        """
        for zone in self.zones:
            if zone.code == code:
                return zone
        return None
