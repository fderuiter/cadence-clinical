"""
Registry for versioned DIA TMF Reference Model catalogs.
Includes the canonical, pre-seeded active v3.2.0 catalog.
"""

from typing import Dict, List, Optional

from .models import Artifact, Catalog, Section, Zone


class CatalogRegistry:
    """
    Registry holding immutable, versioned DIA TMF Reference Model catalogs.
    """

    def __init__(self) -> None:
        self._catalogs: Dict[str, Catalog] = {}
        self._active_version: Optional[str] = None

    def register_catalog(self, catalog: Catalog, active: bool = False) -> None:
        """
        Register a new catalog version in the registry.
        """
        self._catalogs[catalog.version] = catalog
        if active or self._active_version is None:
            self._active_version = catalog.version

    def get_catalog(self, version: str) -> Catalog:
        """
        Retrieve a registered catalog by its version.
        """
        if version not in self._catalogs:
            raise ValueError(f"Catalog version '{version}' is not registered.")
        return self._catalogs[version]

    def get_active_catalog(self) -> Catalog:
        """
        Retrieve the default active catalog.
        """
        if not self._active_version:
            raise ValueError("No active catalog has been registered.")
        return self._catalogs[self._active_version]

    def get_active_version(self) -> str:
        """
        Retrieve the version identifier of the active catalog.
        """
        if not self._active_version:
            raise ValueError("No active catalog has been registered.")
        return self._active_version

    def list_versions(self) -> List[str]:
        """
        List all registered catalog versions.
        """
        return list(self._catalogs.keys())


# Global registry instance
registry = CatalogRegistry()

# ---------------------------------------------------------------------------
# Seed the Default Active Catalog (v3.2.0)
# ---------------------------------------------------------------------------

_zones = {
    1: Zone(number=1, name="Trial Management"),
    2: Zone(number=2, name="Central Trial Documents"),
    3: Zone(number=3, name="Regulatory"),
    4: Zone(number=4, name="IRB/IEC & other Approvals"),
    5: Zone(number=5, name="Site Management"),
    6: Zone(number=6, name="IP & Trial Supplies"),
    7: Zone(number=7, name="Safety Reporting"),
    8: Zone(number=8, name="Centralized & Local Testing"),
    9: Zone(number=9, name="Third Parties"),
    10: Zone(number=10, name="Data Management"),
    11: Zone(number=11, name="Statistics"),
}

_sections = {
    # Zone 1
    "1.1": Section(code="1.1", name="Protocol", zone_number=1),
    "1.2": Section(code="1.2", name="Trial Design & Strategy", zone_number=1),
    "1.3": Section(code="1.3", name="Trial Committees & Oversight", zone_number=1),
    # Zone 2
    "2.1": Section(code="2.1", name="Study Files", zone_number=2),
    "2.2": Section(code="2.2", name="Master Files", zone_number=2),
    # Zone 3
    "3.1": Section(code="3.1", name="Regulatory Filings", zone_number=3),
    # Zone 4
    "4.1": Section(code="4.1", name="IRB/IEC Approval", zone_number=4),
    # Zone 5
    "5.1": Section(code="5.1", name="Site Contacts", zone_number=5),
    "5.2": Section(code="5.2", name="Site Recruitment & Selection", zone_number=5),
    # Zone 6
    "6.1": Section(code="6.1", name="Investigational Product", zone_number=6),
    # Zone 7
    "7.1": Section(code="7.1", name="Safety Monitoring", zone_number=7),
    # Zone 8
    "8.1": Section(code="8.1", name="Laboratory Testing", zone_number=8),
    # Zone 9
    "9.1": Section(code="9.1", name="Vendors & Third Parties", zone_number=9),
    # Zone 10
    "10.1": Section(code="10.1", name="Data Management Specifications", zone_number=10),
    "10.2": Section(code="10.2", name="Case Report Forms", zone_number=10),
    # Zone 11
    "11.1": Section(code="11.1", name="Statistical Analysis", zone_number=11),
}

_artifacts = {
    # Zone 1
    "01.01.01": Artifact(
        code="01.01.01",
        name="Approved Protocol",
        section_code="1.1",
        zone_number=1,
    ),
    "01.01.02": Artifact(
        code="01.01.02",
        name="Protocol Amendment",
        section_code="1.1",
        zone_number=1,
    ),
    "01.01.03": Artifact(
        code="01.01.03",
        name="Protocol Signature Page",
        section_code="1.1",
        zone_number=1,
    ),
    "01.02.01": Artifact(
        code="01.02.01",
        name="Target Product Profile",
        section_code="1.2",
        zone_number=1,
    ),
    "01.02.02": Artifact(
        code="01.02.02",
        name="Investigator Brochure",
        section_code="1.2",
        zone_number=1,
    ),
    "01.03.01": Artifact(
        code="01.03.01",
        name="Committee Charter",
        section_code="1.3",
        zone_number=1,
    ),
    # Zone 2
    "02.01.01": Artifact(
        code="02.01.01", name="Ad-hoc document", section_code="2.1", zone_number=2
    ),
    "02.01.02": Artifact(
        code="02.01.02", name="Master File Index", section_code="2.1", zone_number=2
    ),
    "02.01.03": Artifact(
        code="02.01.03",
        name="Feasibility Questionnaire",
        section_code="2.1",
        zone_number=2,
    ),
    "02.02.01": Artifact(
        code="02.02.01",
        name="Confidentiality Agreement",
        section_code="2.2",
        zone_number=2,
    ),
    "02.02.02": Artifact(
        code="02.02.02",
        name="Insurance Certificate",
        section_code="2.2",
        zone_number=2,
    ),
    # Zone 3
    "03.01.01": Artifact(
        code="03.01.01",
        name="CTA/IND Submission",
        section_code="3.1",
        zone_number=3,
    ),
    "03.01.02": Artifact(
        code="03.01.02", name="CTA/IND Approval", section_code="3.1", zone_number=3
    ),
    # Zone 4
    "04.01.01": Artifact(
        code="04.01.01", name="IRB Submission", section_code="4.1", zone_number=4
    ),
    "04.01.02": Artifact(
        code="04.01.02", name="IRB Approval", section_code="4.1", zone_number=4
    ),
    "04.01.03": Artifact(
        code="04.01.03",
        name="IRB Approved Informed Consent Form",
        section_code="4.1",
        zone_number=4,
    ),
    # Zone 5
    "05.01.01": Artifact(
        code="05.01.01", name="Investigator CV", section_code="5.1", zone_number=5
    ),
    "05.01.02": Artifact(
        code="05.01.02", name="Medical License", section_code="5.1", zone_number=5
    ),
    "05.01.03": Artifact(
        code="05.01.03",
        name="Financial Disclosure",
        section_code="5.1",
        zone_number=5,
    ),
    "05.02.01": Artifact(
        code="05.02.01",
        name="Site Feasibility Report",
        section_code="5.2",
        zone_number=5,
    ),
    "05.02.02": Artifact(
        code="05.02.02",
        name="Site Initiation Visit Report",
        section_code="5.2",
        zone_number=5,
    ),
    # Zone 6
    "06.01.01": Artifact(
        code="06.01.01", name="IP Release Form", section_code="6.1", zone_number=6
    ),
    "06.01.02": Artifact(
        code="06.01.02", name="IP Label", section_code="6.1", zone_number=6
    ),
    "06.01.03": Artifact(
        code="06.01.03", name="Shipment Record", section_code="6.1", zone_number=6
    ),
    "06.01.04": Artifact(
        code="06.01.04",
        name="IP Destruction/Return Record",
        section_code="6.1",
        zone_number=6,
    ),
    # Zone 7
    "07.01.01": Artifact(
        code="07.01.01", name="Safety Plan", section_code="7.1", zone_number=7
    ),
    "07.01.02": Artifact(
        code="07.01.02", name="SUSAR Report", section_code="7.1", zone_number=7
    ),
    "07.01.03": Artifact(
        code="07.01.03", name="DSUR", section_code="7.1", zone_number=7
    ),
    "07.01.04": Artifact(
        code="07.01.04",
        name="Annual Safety Report",
        section_code="7.1",
        zone_number=7,
    ),
    # Zone 8
    "08.01.01": Artifact(
        code="08.01.01",
        name="Lab Certification",
        section_code="8.1",
        zone_number=8,
    ),
    "08.01.02": Artifact(
        code="08.01.02",
        name="Lab Normal Ranges",
        section_code="8.1",
        zone_number=8,
    ),
    "08.01.03": Artifact(
        code="08.01.03",
        name="Lab Requisition Form",
        section_code="8.1",
        zone_number=8,
    ),
    "08.01.04": Artifact(
        code="08.01.04", name="Lab Report", section_code="8.1", zone_number=8
    ),
    # Zone 9
    "09.01.01": Artifact(
        code="09.01.01", name="Vendor Agreement", section_code="9.1", zone_number=9
    ),
    "09.01.02": Artifact(
        code="09.01.02",
        name="Vendor Assessment Report",
        section_code="9.1",
        zone_number=9,
    ),
    # Zone 10
    "10.01.01": Artifact(
        code="10.01.01",
        name="Data Management Plan",
        section_code="10.1",
        zone_number=10,
    ),
    "10.01.02": Artifact(
        code="10.01.02",
        name="Data Validation Specifications",
        section_code="10.1",
        zone_number=10,
    ),
    "10.01.03": Artifact(
        code="10.01.03", name="Define-XML", section_code="10.1", zone_number=10
    ),
    "10.02.01": Artifact(
        code="10.02.01", name="Blank CRF", section_code="10.2", zone_number=10
    ),
    "10.02.02": Artifact(
        code="10.02.02",
        name="CRF Completion Guidelines",
        section_code="10.2",
        zone_number=10,
    ),
    # Zone 11
    "11.01.01": Artifact(
        code="11.01.01",
        name="Statistical Analysis Plan",
        section_code="11.1",
        zone_number=11,
    ),
    "11.01.02": Artifact(
        code="11.01.02", name="Mock Shells", section_code="11.1", zone_number=11
    ),
    "11.01.03": Artifact(
        code="11.01.03",
        name="Data Lock Certificate",
        section_code="11.1",
        zone_number=11,
    ),
    "11.01.04": Artifact(
        code="11.01.04", name="Statistical Report", section_code="11.1", zone_number=11
    ),
}

# Construct and register default catalog
default_catalog = Catalog(
    version="v3.2.0", zones=_zones, sections=_sections, artifacts=_artifacts
)

registry.register_catalog(default_catalog, active=True)
