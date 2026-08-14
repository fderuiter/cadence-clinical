from typing import Any

from .models import Artifact, Section, TaxonomyCatalog, Zone

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
    "normalize_milestone",
    "MILESTONE_MANDATORY_ARTIFACTS",
]

DIA_V3_2_0_RAW = {
    1: (
        "Trial Management",
        {
            "01.01": (
                "Trial Design",
                [
                    ("01.01.01", "Clinical Trial Protocol"),
                    ("01.01.02", "Clinical Trial Protocol Amendment"),
                    ("01.01.03", "Protocol Sign-off"),
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
        {
            "05.01": ("Site Selection", [("05.01.01", "Site Feasibility Survey")]),
            "05.02": (
                "Investigator Qualification",
                [
                    ("05.02.01", "FDA Form 1572"),
                    ("05.02.02", "Financial Disclosure"),
                ],
            ),
        },
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

DIA_V3_2_0_COMPLETE_RAW = {
    1: (
        "Trial Management",
        {
            "01.01": (
                "Trial Design",
                [
                    ("01.01.01", "Clinical Trial Protocol"),
                    ("01.01.02", "Clinical Trial Protocol Amendment"),
                    ("01.01.03", "Protocol Sign-off"),
                ],
            ),
            "01.02": (
                "Trial Oversight",
                [
                    ("01.02.01", "Trial Oversight Committee Charter"),
                ],
            ),
            "01.03": (
                "Trial Monitoring",
                [
                    ("01.03.01", "Trial Monitoring Plan"),
                ],
            ),
            "01.04": (
                "Trial Close-out",
                [
                    ("01.04.01", "Trial Close-out Report"),
                ],
            ),
        },
    ),
    2: (
        "Central Trial Documents",
        {
            "02.01": (
                "Product Information",
                [
                    ("02.01.01", "Investigator's Brochure"),
                ],
            ),
            "02.02": (
                "Clinical Trial Materials",
                [
                    ("02.02.01", "Clinical Trial Material Specifications"),
                ],
            ),
        },
    ),
    3: (
        "Regulatory",
        {
            "03.01": (
                "Regulatory Submissions",
                [
                    ("03.01.01", "Regulatory Authority Submission"),
                ],
            ),
            "03.02": (
                "Regulatory Approvals",
                [
                    ("03.02.01", "Regulatory Authority Approval"),
                ],
            ),
        },
    ),
    4: (
        "IRB/IEC & other Approvals",
        {
            "04.01": (
                "IRB/IEC Submissions",
                [
                    ("04.01.01", "IRB/IEC Approval"),
                ],
            ),
            "04.02": (
                "IRB/IEC Approvals",
                [
                    ("04.02.01", "IRB/IEC Approval Notification"),
                ],
            ),
        },
    ),
    5: (
        "Site Management",
        {
            "05.01": (
                "Site Selection",
                [
                    ("05.01.01", "Site Feasibility Survey"),
                ],
            ),
            "05.02": (
                "Investigator Qualification",
                [
                    ("05.02.01", "FDA Form 1572"),
                    ("05.02.02", "Financial Disclosure"),
                    ("05.02.03", "Investigator CV"),
                    ("05.02.04", "Delegation of Authority Log"),
                    ("05.02.05", "Informed Consent Form"),
                ],
            ),
            "05.03": (
                "Site Training",
                [
                    ("05.03.01", "Site Training Records"),
                ],
            ),
            "05.04": (
                "Site Communication",
                [
                    ("05.04.01", "Site Communication Log"),
                ],
            ),
        },
    ),
    6: (
        "IP & Trial Supplies",
        {
            "06.01": (
                "IP Documentation",
                [
                    ("06.01.01", "Investigational Product Records"),
                ],
            ),
            "06.02": (
                "IP Logistics",
                [
                    ("06.02.01", "IP Shipping Records"),
                ],
            ),
        },
    ),
    7: (
        "Safety Reporting",
        {
            "07.01": (
                "Safety Notifications",
                [
                    ("07.01.01", "Serious Adverse Event Report"),
                ],
            ),
            "07.02": (
                "Safety Operations",
                [
                    ("07.02.01", "Safety Management Plan"),
                ],
            ),
        },
    ),
    8: (
        "Centralized & Local Testing",
        {
            "08.01": (
                "Lab Documentation",
                [
                    ("08.01.01", "Central Laboratory Certificate"),
                ],
            ),
            "08.02": (
                "Lab Operations",
                [
                    ("08.02.01", "Laboratory Reference Ranges"),
                ],
            ),
        },
    ),
    9: (
        "Third Parties",
        {
            "09.01": (
                "Vendor Management",
                [
                    ("09.01.01", "Vendor Service Agreement"),
                ],
            ),
            "09.02": (
                "Vendor Operations",
                [
                    ("09.02.01", "Vendor Audit Report"),
                ],
            ),
        },
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
            "10.02": (
                "Case Report Forms",
                [
                    ("10.02.01", "Blank CRF"),
                ],
            ),
            "10.03": (
                "Data Operations",
                [
                    ("10.03.01", "Data Review Guidelines"),
                ],
            ),
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
            ),
            "11.02": (
                "Data Analysis and Reports",
                [
                    ("11.02.01", "Clinical Study Report"),
                ],
            ),
        },
    ),
}

CADENCE_EXTENSIONS_RAW = {
    "05.02.98": ("Medical License", "05.02", 5),
    "05.02.99": ("Cadence Investigator Portal Training Certificate", "05.02", 5),
    "10.01.99": ("Cadence Custom Data Integrity Log", "10.01", 10),
}


def build_catalog(
    version: str,
    raw_data: dict,
    extensions: dict[str, tuple[str, str, int]] | None = None,
) -> TaxonomyCatalog:
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
                        is_extension=False,
                    )
                )

            if extensions:
                for ext_code, (ext_name, ext_sec, ext_zone) in extensions.items():
                    if ext_sec == sec_code and ext_zone == zone_code:
                        artifacts.append(
                            Artifact(
                                code=ext_code,
                                name=ext_name,
                                section_code=ext_sec,
                                zone_code=ext_zone,
                                is_extension=True,
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
    def __init__(self):
        self._catalogs: dict[str, TaxonomyCatalog] = {}
        self._active_version: str | None = None

    def register_catalog(self, catalog: TaxonomyCatalog) -> None:
        if catalog.version in self._catalogs:
            raise ValueError(
                f"Catalog version '{catalog.version}' is already registered and cannot be modified."
            )
        self._catalogs[catalog.version] = catalog

    def set_active_version(self, version: str) -> None:
        if version not in self._catalogs:
            raise KeyError(
                f"Cannot set active version to unregistered version '{version}'."
            )
        self._active_version = version

    def get_catalog(self, version: str) -> TaxonomyCatalog:
        if version not in self._catalogs:
            raise KeyError(f"Taxonomy catalog version '{version}' not found.")
        return self._catalogs[version]

    def get_active_catalog(self) -> TaxonomyCatalog:
        if not self._active_version:
            raise RuntimeError("No active taxonomy catalog version is set.")
        return self._catalogs[self._active_version]

    def get_registered_versions(self) -> list[str]:
        return list(self._catalogs.keys())


_registry = TaxonomyRegistry()

_v3_2_0_catalog = build_catalog("v3.2.0", DIA_V3_2_0_RAW)
_registry.register_catalog(_v3_2_0_catalog)

_v3_2_0_complete_catalog = build_catalog("v3.2.0-complete", DIA_V3_2_0_COMPLETE_RAW)
_registry.register_catalog(_v3_2_0_complete_catalog)

_v3_2_0_extended_catalog = build_catalog(
    "v3.2.0-extended", DIA_V3_2_0_COMPLETE_RAW, CADENCE_EXTENSIONS_RAW
)
_registry.register_catalog(_v3_2_0_extended_catalog)

_registry.set_active_version("v3.2.0-complete")


def get_catalog(version: str) -> TaxonomyCatalog:
    return _registry.get_catalog(version)


def get_active_catalog() -> TaxonomyCatalog:
    return _registry.get_active_catalog()


def register_catalog(catalog: TaxonomyCatalog) -> None:
    _registry.register_catalog(catalog)


def set_active_version(version: str) -> None:
    _registry.set_active_version(version)


def get_registered_versions() -> list[str]:
    return _registry.get_registered_versions()


MILESTONE_MANDATORY_ARTIFACTS: dict[str, list[str]] = {
    "INITIATION": [
        "01.01.01",
    ],
    "CONDUCT": [
        "01.01.01",
        "10.01.02",
        "10.02.01",
    ],
    "CLOSEOUT": [
        "01.01.01",
        "10.01.02",
        "10.02.01",
        "11.01.02",
    ],
    "STUDY_INITIATION": [
        "01.01.01",  # Zone 1: Clinical Trial Protocol
        "01.01.03",  # Zone 1: Protocol Sign-off
        "01.03.01",  # Zone 1: Trial Monitoring Plan
        "02.01.01",  # Zone 2: Investigator's Brochure
        "07.02.01",  # Zone 7: Safety Management Plan
        "10.01.01",  # Zone 10: Data Management Plan
        "10.01.02",  # Zone 10: Define-XML Specifications
        "10.02.01",  # Zone 10: Blank CRF
        "11.01.01",  # Zone 11: Statistical Analysis Plan
    ],
    "ETHICS_SUBMISSION": [
        "01.01.01",  # Zone 1: Clinical Trial Protocol
        "02.01.01",  # Zone 2: Investigator's Brochure
        "03.01.01",  # Zone 3: Regulatory Authority Submission
        "04.01.01",  # Zone 4: IRB/IEC Approval
        "05.02.05",  # Zone 5: Informed Consent Form
    ],
    "SITE_ACTIVATION": [
        "04.01.01",  # Zone 4: IRB/IEC Approval
        "04.02.01",  # Zone 4: IRB/IEC Approval Notification
        "05.01.01",  # Zone 5: Site Feasibility Survey
        "05.02.01",  # Zone 5: FDA Form 1572
        "05.02.02",  # Zone 5: Financial Disclosure
        "05.02.03",  # Zone 5: Investigator CV
        "05.02.04",  # Zone 5: Delegation of Authority Log
        "05.02.05",  # Zone 5: Informed Consent Form
        "05.03.01",  # Zone 5: Site Training Records
        "08.01.01",  # Zone 8: Central Laboratory Certificate
        "08.02.01",  # Zone 8: Laboratory Reference Ranges
        "09.01.01",  # Zone 9: Vendor Service Agreement
    ],
    "FSI": [
        "01.01.01",  # Zone 1: Clinical Trial Protocol
        "05.02.05",  # Zone 5: Informed Consent Form
        "06.01.01",  # Zone 6: Investigational Product Records
        "06.02.01",  # Zone 6: IP Shipping Records
        "07.01.01",  # Zone 7: Serious Adverse Event Report
        "10.02.01",  # Zone 10: Blank CRF
    ],
}


def normalize_milestone(milestone: str) -> str:
    norm = milestone.strip().upper().replace(" ", "_").replace("-", "_")
    if norm in ("INITIATION", "STUDY_START", "START"):
        return "INITIATION"
    if norm in ("CONDUCT", "DATA_COLLECTION"):
        return "CONDUCT"
    if norm in ("CLOSEOUT", "STUDY_CLOSED", "LOCK", "STUDY_LOCK"):
        return "CLOSEOUT"
    if norm in ("STUDY_INITIATION", "STUDYINITIATION"):
        return "STUDY_INITIATION"
    if norm in (
        "ETHICS_SUBMISSION",
        "ETHICSSUBMISSION",
        "ETHICS",
        "REGULATORY_SUBMISSION",
    ):
        return "ETHICS_SUBMISSION"
    if norm in ("SITE_ACTIVATION", "SITEACTIVATION", "ACTIVATION"):
        return "SITE_ACTIVATION"
    if norm in ("FSI", "FIRST_SUBJECT_IN", "FIRST_PATIENT_IN", "FPI"):
        return "FSI"
    return norm


def resolve_artifact(
    version: str, code: str | None = None, name: str | None = None
) -> dict[str, Any]:
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
        if len(matches) > 1:
            raise ValueError(
                f"Ambiguous artifact input for name '{name}' in version '{version}': multiple matches found."
            )
        artifact_by_name = matches[0]

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


def get_mandatory_artifacts(milestone: str, version: str) -> list[Artifact]:
    try:
        catalog = get_catalog(version)
    except KeyError:
        raise ValueError(f"Unknown catalog version '{version}'.")

    canonical_milestone = normalize_milestone(milestone)
    if canonical_milestone not in MILESTONE_MANDATORY_ARTIFACTS:
        raise ValueError(
            f"Unknown milestone '{milestone}'. Supported milestones are: {', '.join(sorted(MILESTONE_MANDATORY_ARTIFACTS.keys()))}."
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
