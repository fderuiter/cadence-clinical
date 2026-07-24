"""
Seeded catalog of DIA TMF Reference Model v3.2.0.
Contains all 11 canonical TMF Zones, their sections, and a standard approved artifact inventory
packaged statically for zero-dependency local use.
"""

from .models import Zone, Section, Artifact, TaxonomyCatalog

# 1. Seed all 11 named canonical DIA TMF Reference Model Zones
ZONES = [
    Zone(number=1, name="Trial Management"),
    Zone(number=2, name="Central Trial Documents"),
    Zone(number=3, name="Regulatory"),
    Zone(number=4, name="IRB/IEC & other Approvals"),
    Zone(number=5, name="Site Management"),
    Zone(number=6, name="IP & Trial Supplies"),
    Zone(number=7, name="Safety Reporting"),
    Zone(number=8, name="Centralized & Local Testing"),
    Zone(number=9, name="Third Parties"),
    Zone(number=10, name="Data Management"),
    Zone(number=11, name="Statistics"),
]

# 2. Seed representative sections for each of the 11 zones
SECTIONS = [
    Section(number="1.1", name="Protocol", zone_number=1),
    Section(number="1.2", name="Trial Planning", zone_number=1),
    Section(number="2.1", name="Study Files", zone_number=2),
    Section(number="3.1", name="Regulatory Authority Submission & Approval", zone_number=3),
    Section(number="4.1", name="IRB/IEC Submission & Approval", zone_number=4),
    Section(number="5.1", name="Site Contacts", zone_number=5),
    Section(number="5.2", name="Site Initiation", zone_number=5),
    Section(number="6.1", name="IP Product Documentation", zone_number=6),
    Section(number="7.1", name="Safety Notifications", zone_number=7),
    Section(number="8.1", name="Laboratory Quality Control", zone_number=8),
    Section(number="9.1", name="Third Party Agreements", zone_number=9),
    Section(number="10.1", name="Data Management Specifications", zone_number=10),
    Section(number="10.2", name="Case Report Forms", zone_number=10),
    Section(number="11.1", name="Statistical Analysis", zone_number=11),
]

# 3. Seed canonical approved artifact inventory with stable codes
ARTIFACTS = [
    Artifact(code="01.01.01", name="Approved Protocol", section_number="1.1", zone_number=1),
    Artifact(code="01.01.02", name="Protocol Amendment", section_number="1.1", zone_number=1),
    Artifact(code="01.02.01", name="Trial Feasibility Report", section_number="1.2", zone_number=1),
    Artifact(code="02.01.01", name="Investigator Brochure", section_number="2.1", zone_number=2),
    Artifact(code="02.01.02", name="Ad-hoc document", section_number="2.1", zone_number=2),
    Artifact(code="03.01.01", name="Regulatory Approval", section_number="3.1", zone_number=3),
    Artifact(code="04.01.01", name="IRB Approval", section_number="4.1", zone_number=4),
    Artifact(code="05.01.01", name="Site Contacts List", section_number="5.1", zone_number=5),
    Artifact(code="05.02.01", name="Site Signature Page", section_number="5.2", zone_number=5),
    Artifact(code="06.01.01", name="IP Release Form", section_number="6.1", zone_number=6),
    Artifact(code="07.01.01", name="Safety Notification", section_number="7.1", zone_number=7),
    Artifact(code="08.01.01", name="Lab Certification", section_number="8.1", zone_number=8),
    Artifact(code="09.01.01", name="Service Level Agreement", section_number="9.1", zone_number=9),
    Artifact(code="10.01.01", name="Define-XML", section_number="10.1", zone_number=10),
    Artifact(code="10.02.01", name="Blank CRF", section_number="10.2", zone_number=10),
    Artifact(code="11.01.01", name="Data Lock Certificate", section_number="11.1", zone_number=11),
]

# 4. Construct the immutable versioned v3.2.0 catalog
V3_2_0_CATALOG = TaxonomyCatalog(
    version="v3.2.0",
    zones={z.number: z for z in ZONES},
    sections={s.number: s for s in SECTIONS},
    artifacts={a.code: a for a in ARTIFACTS},
    artifact_name_map={a.name.strip().lower(): a for a in ARTIFACTS},
)
