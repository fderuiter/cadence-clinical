from typing import Any, Dict, List, Optional

from tmf_reference_model.models import Artifact, Section, TaxonomyCatalog, Zone

# Stable public API imports and exports for eTMF and other taxonomy consumers.
__all__ = [
    "Artifact",
    "Section",
    "Zone",
    "TaxonomyCatalog",
    "get_catalog",
    "get_active_catalog",
    "register_catalog",
    "set_active_version",
    "get_registered_versions",
    "resolve_artifact",
    "validate_hierarchy",
    "get_mandatory_artifacts",
    "MILESTONE_MANDATORY_ARTIFACTS",
]

# Raw DIA TMF Reference Model structure for the seeded v3.2.0 version
DIA_V3_2_0_RAW = {
    1: (
        "Trial Management",
        {
            "01.01": (
                "Trial Design",
                [
                    ("01.01.01", "Clinical Trial Protocol"),
                    ("01.01.02", "Clinical Trial Protocol Amendment"),
                ],
            )
        },
    ),
    2: (
        "Central Trial Documents",
        {"02.01": ("Product Information", [("02.01.01", "Investigator's Brochure")])},
    ),
    3: (
        "Regulatory",
        {
            "03.01": (
                "Regulatory Submissions",
                [("03.01.01", "Regulatory Authority Submission")],
            )
        },
    ),
    4: (
        "IRB/IEC & other Approvals",
        {"04.01": ("IRB/IEC Submissions", [("04.01.01", "IRB/IEC Approval")])},
    ),
    5: (
        "Site Management",
        {"05.01": ("Site Selection", [("05.01.01", "Site Feasibility Survey")])},
    ),
    6: (
        "IP & Trial Supplies",
        {
            "06.01": (
                "IP Documentation",
                [("06.01.01", "Investigational Product Records")],
            )
        },
    ),
    7: (
        "Safety Reporting",
        {
            "07.01": (
                "Safety Notifications",
                [("07.01.01", "Serious Adverse Event Report")],
            )
        },
    ),
    8: (
        "Centralized & Local Testing",
        {
            "08.01": (
                "Lab Documentation",
                [("08.01.01", "Central Laboratory Certificate")],
            )
        },
    ),
    9: (
        "Third Parties",
        {"09.01": ("Vendor Management", [("09.01.01", "Vendor Service Agreement")])},
    ),
    10: (
        "Data Management",
        {
            "10.01": (
                "Data Management Specifications",
                [
                    ("10.01.01", "Data Management Plan"),
                    ("10.01.02", "Define-XML Specifications"),
                ],
            ),
            "10.02": ("Case Report Forms", [("10.02.01", "Blank CRF")]),
        },
    ),
    11: (
        "Statistics",
        {
            "11.01": (
                "Statistical Analysis",
                [
                    ("11.01.01", "Statistical Analysis Plan"),
                    ("11.01.02", "Data Lock Certificate"),
                ],
            )
        },
    ),
}


def build_catalog(version: str, raw_data: dict) -> TaxonomyCatalog:
    """
    Build a TaxonomyCatalog from structured raw dictionary data.
    """
    zones = []
    for zone_code, (zone_name, sections_dict) in raw_data.items():
        sections = []
        for sec_code, (sec_name, artifacts_list) in sections_dict.items():
            artifacts = []
            for art_code, art_name in artifacts_list:
                artifacts.append(
                    Artifact(
                        code=art_code,
                        name=art_name,
                        section_code=sec_code,
                        zone_code=zone_code,
                    )
                )
            sections.append(
                Section(
                    code=sec_code,
                    name=sec_name,
                    zone_code=zone_code,
                    artifacts=artifacts,
                )
            )
        zones.append(Zone(code=zone_code, name=zone_name, sections=sections))
    return TaxonomyCatalog(version=version, zones=zones)


class TaxonomyRegistry:
    """
    Thread-safe or basic registry managing available versions of the DIA TMF Taxonomy Catalog.
    """

    def __init__(self):
        self._catalogs: Dict[str, TaxonomyCatalog] = {}
        self._active_version: Optional[str] = None

    def register_catalog(self, catalog: TaxonomyCatalog) -> None:
        """
        Register a TaxonomyCatalog. If the version already exists, raise an error to prevent mutability.
        """
        if catalog.version in self._catalogs:
            raise ValueError(
                f"Catalog version '{catalog.version}' is already registered and cannot be modified."
            )
        self._catalogs[catalog.version] = catalog

    def set_active_version(self, version: str) -> None:
        """
        Set the active default catalog version.
        """
        if version not in self._catalogs:
            raise KeyError(
                f"Cannot set active version to unregistered version '{version}'."
            )
        self._active_version = version

    def get_catalog(self, version: str) -> TaxonomyCatalog:
        """
        Retrieve a specific catalog by version.
        """
        if version not in self._catalogs:
            raise KeyError(f"Taxonomy catalog version '{version}' not found.")
        return self._catalogs[version]

    def get_active_catalog(self) -> TaxonomyCatalog:
        """
        Retrieve the active/default catalog.
        """
        if not self._active_version:
            raise RuntimeError("No active taxonomy catalog version is set.")
        return self._catalogs[self._active_version]

    def get_registered_versions(self) -> List[str]:
        """
        Get all registered catalog versions.
        """
        return list(self._catalogs.keys())


# Singleton registry instance for global use
_registry = TaxonomyRegistry()

# Initialize registry with the default v3.2.0 DIA TMF Reference Model
_v3_2_0_catalog = build_catalog("v3.2.0", DIA_V3_2_0_RAW)
_registry.register_catalog(_v3_2_0_catalog)
_registry.set_active_version("v3.2.0")


# Public API wrapper functions
def get_catalog(version: str) -> TaxonomyCatalog:
    """
    Retrieve a taxonomy catalog by version.
    """
    return _registry.get_catalog(version)


def get_active_catalog() -> TaxonomyCatalog:
    """
    Retrieve the active/default taxonomy catalog.
    """
    return _registry.get_active_catalog()


def register_catalog(catalog: TaxonomyCatalog) -> None:
    """
    Register a new taxonomy catalog version.
    """
    _registry.register_catalog(catalog)


def set_active_version(version: str) -> None:
    """
    Set the active catalog version.
    """
    _registry.set_active_version(version)


def get_registered_versions() -> List[str]:
    """
    List all currently registered catalog versions.
    """
    return _registry.get_registered_versions()


# Centralized milestone-to-mandatory-artifact mappings keyed by canonical artifact code (identity).
MILESTONE_MANDATORY_ARTIFACTS = {
    "INITIATION": [
        "01.01.01",  # Clinical Trial Protocol
    ],
    "CONDUCT": [
        "01.01.01",  # Clinical Trial Protocol
        "10.01.02",  # Define-XML Specifications
        "10.02.01",  # Blank CRF
    ],
    "CLOSEOUT": [
        "01.01.01",  # Clinical Trial Protocol
        "10.01.02",  # Define-XML Specifications
        "10.02.01",  # Blank CRF
        "11.01.02",  # Data Lock Certificate
    ],
}


def resolve_artifact(
    version: str, code: Optional[str] = None, name: Optional[str] = None
) -> Dict[str, Any]:
    """
    Resolves an artifact by canonical code, display name, or both in the requested version.

    Lookup Semantics:
    - If 'code' is provided, performs an exact match lookup.
    - If 'name' is provided, performs a case-insensitive, stripped match against the artifact display name.
    - If both are provided, both are checked and must resolve to the same canonical artifact.

    Raises ValueError for:
    - Unknown catalog version
    - No query arguments (neither code nor name is provided)
    - Unknown artifact
    - Ambiguous artifact input (multiple name matches)
    - Mismatched code and name combination
    """
    try:
        catalog = get_catalog(version)
    except KeyError:
        raise ValueError(f"Unknown catalog version '{version}'.")

    if code is None and name is None:
        raise ValueError(
            "Must provide either 'code' or 'name' (or both) to resolve an artifact."
        )

    artifact_by_code = None
    if code is not None:
        artifact_by_code = catalog.get_artifact(code)
        if artifact_by_code is None:
            raise ValueError(
                f"Unknown artifact with code '{code}' in version '{version}'."
            )

    artifact_by_name = None
    if name is not None:
        normalized_name = name.strip().lower()
        matches = []
        for art in catalog.artifact_map.values():
            if art.name.strip().lower() == normalized_name:
                matches.append(art)

        if not matches:
            raise ValueError(
                f"Unknown artifact with name '{name}' in version '{version}'."
            )
        elif len(matches) > 1:
            raise ValueError(
                f"Ambiguous artifact input for name '{name}' in version '{version}': multiple matches found."
            )
        else:
            artifact_by_name = matches[0]

    # Resolve final artifact and check for mismatches
    if artifact_by_code and artifact_by_name:
        if artifact_by_code.code != artifact_by_name.code:
            raise ValueError(
                f"Mismatched artifact combination: code '{code}' and name '{name}' resolve to different artifacts."
            )
        final_artifact = artifact_by_code
    elif artifact_by_code:
        final_artifact = artifact_by_code
    else:
        final_artifact = artifact_by_name

    # Resolve parent section and zone
    section = catalog.get_section(final_artifact.section_code)
    zone = catalog.get_zone(final_artifact.zone_code)

    if not section or not zone:
        raise ValueError(
            f"Mismatched zone/section combination inside catalog for artifact '{final_artifact.code}'."
        )

    return {
        "artifact": final_artifact,
        "section": section,
        "zone": zone,
        "version": version,
    }


def validate_hierarchy(
    version: str, zone_code: int, section_code: str, artifact_code: str
) -> None:
    """
    Validates a supplied zone/section/artifact combination against a requested version.

    Raises ValueError with actionable details if:
    - The catalog version is unknown.
    - Any of the codes (zone, section, or artifact) do not exist in the version.
    - There is a mismatch (e.g., artifact is not in section, section is not in zone).
    """
    try:
        catalog = get_catalog(version)
    except KeyError:
        raise ValueError(f"Unknown catalog version '{version}'.")

    zone = catalog.get_zone(zone_code)
    if not zone:
        raise ValueError(f"Unknown zone code {zone_code} in version '{version}'.")

    section = catalog.get_section(section_code)
    if not section:
        raise ValueError(
            f"Unknown section code '{section_code}' in version '{version}'."
        )

    artifact = catalog.get_artifact(artifact_code)
    if not artifact:
        raise ValueError(
            f"Unknown artifact code '{artifact_code}' in version '{version}'."
        )

    # Check hierarchy
    if section.zone_code != zone_code:
        raise ValueError(
            f"Mismatched hierarchy: section '{section_code}' belongs to zone {section.zone_code}, not zone {zone_code}."
        )

    if artifact.section_code != section_code:
        raise ValueError(
            f"Mismatched hierarchy: artifact '{artifact_code}' belongs to section '{artifact.section_code}', not section '{section_code}'."
        )

    if artifact.zone_code != zone_code:
        raise ValueError(
            f"Mismatched hierarchy: artifact '{artifact_code}' belongs to zone {artifact.zone_code}, not zone {zone_code}."
        )


def get_mandatory_artifacts(milestone: str, version: str) -> List[Artifact]:
    """
    Returns the mandatory artifact set for the supported milestones (INITIATION, CONDUCT, and CLOSEOUT)
    in the requested catalog version.

    Raises ValueError for:
    - Unknown catalog version
    - Unknown/unsupported milestone
    - Mandatory artifact not found in the catalog version
    """
    try:
        catalog = get_catalog(version)
    except KeyError:
        raise ValueError(f"Unknown catalog version '{version}'.")

    # Support milestone case-insensitive lookup & common normalization aliases
    milestone_normalized = milestone.strip().upper()
    if milestone_normalized in ("INITIATION", "STUDY START"):
        canonical_milestone = "INITIATION"
    elif milestone_normalized in ("CONDUCT", "DATA COLLECTION"):
        canonical_milestone = "CONDUCT"
    elif milestone_normalized in ("CLOSEOUT", "STUDY CLOSED", "LOCK"):
        canonical_milestone = "CLOSEOUT"
    else:
        raise ValueError(
            f"Unknown milestone '{milestone}'. Supported milestones are: INITIATION, CONDUCT, CLOSEOUT."
        )

    codes = MILESTONE_MANDATORY_ARTIFACTS[canonical_milestone]
    artifacts = []
    for code in codes:
        art = catalog.get_artifact(code)
        if not art:
            raise ValueError(
                f"Mandatory artifact code '{code}' for milestone '{canonical_milestone}' not found in catalog version '{version}'."
            )
        artifacts.append(art)

    return artifacts
