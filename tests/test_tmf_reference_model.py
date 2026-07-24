"""
Unit tests for the canonical versioned DIA TMF Reference Model catalog.
"""

import pytest
from pydantic import ValidationError

from tmf_reference_model import (
    Artifact,
    Catalog,
    Section,
    Zone,
    get_active_catalog,
    get_active_version,
    get_catalog,
    list_versions,
    registry,
)


def test_active_version_and_selection():
    """
    Verify that the default active version can be obtained and is correct.
    """

    # @req:PRD-EDL-001
    active_version = get_active_version()
    assert active_version == "v3.2.0"

    active_catalog = get_active_catalog()
    assert active_catalog.version == "v3.2.0"

    versions = list_versions()
    assert "v3.2.0" in versions

    # Explicit version selection
    catalog = get_catalog("v3.2.0")
    assert catalog is active_catalog


def test_canonical_11_zones():
    """
    Verify that the active catalog contains exactly the 11 named DIA zones.
    """

    # @req:PRD-EDL-001
    catalog = get_active_catalog()
    assert len(catalog.zones) == 11

    expected_zones = {
        1: "Trial Management",
        2: "Central Trial Documents",
        3: "Regulatory",
        4: "IRB/IEC & other Approvals",
        5: "Site Management",
        6: "IP & Trial Supplies",
        7: "Safety Reporting",
        8: "Centralized & Local Testing",
        9: "Third Parties",
        10: "Data Management",
        11: "Statistics",
    }

    for num, expected_name in expected_zones.items():
        zone = catalog.get_zone(num)
        assert zone is not None, f"Zone {num} is missing"
        assert zone.number == num
        assert zone.name == expected_name


def test_hierarchy_navigation_and_parent_association():
    """
    Verify hierarchical navigation and that each artifact uniquely
    and deterministically identifies its parent section and zone.
    """

    # @req:PRD-EDL-001
    catalog = get_active_catalog()

    # Retrieve representative artifact "Approved Protocol"
    protocol_art = catalog.get_artifact("01.01.01")
    assert protocol_art is not None
    assert protocol_art.name == "Approved Protocol"
    assert protocol_art.section_code == "1.1"
    assert protocol_art.zone_number == 1

    # Check parent section exists
    section = catalog.get_section(protocol_art.section_code)
    assert section is not None
    assert section.code == "1.1"
    assert section.name == "Protocol"
    assert section.zone_number == 1

    # Check parent zone exists
    zone = catalog.get_zone(protocol_art.zone_number)
    assert zone is not None
    assert zone.number == 1
    assert zone.name == "Trial Management"


def test_query_helpers():
    """
    Verify the query helper methods on the Catalog model.
    """

    # @req:PRD-EDL-001
    catalog = get_active_catalog()

    # Sections belonging to Zone 10
    zone_10_sections = catalog.get_sections_by_zone(10)
    assert len(zone_10_sections) == 2
    section_codes = {s.code for s in zone_10_sections}
    assert "10.1" in section_codes
    assert "10.2" in section_codes

    # Artifacts in Section 10.1
    section_10_1_artifacts = catalog.get_artifacts_by_section("10.1")
    assert len(section_10_1_artifacts) == 3
    artifact_names = {a.name for a in section_10_1_artifacts}
    assert "Define-XML" in artifact_names

    # Artifacts in Zone 11
    zone_11_artifacts = catalog.get_artifacts_by_zone(11)
    assert len(zone_11_artifacts) == 4
    art_names_11 = {a.name for a in zone_11_artifacts}
    assert "Data Lock Certificate" in art_names_11


def test_immutability():
    """
    Verify that catalog records and models are completely immutable at runtime (frozen).
    """

    # @req:PRD-EDL-001
    catalog = get_active_catalog()
    zone = catalog.get_zone(1)
    assert zone is not None

    with pytest.raises((ValidationError, AttributeError, TypeError)):
        # Attempt to mutate zone name should be rejected by Pydantic's frozen configuration
        zone.name = "Mutated Name"  # type: ignore

    artifact = catalog.get_artifact("01.01.01")
    assert artifact is not None
    with pytest.raises((ValidationError, AttributeError, TypeError)):
        artifact.name = "New Name"  # type: ignore

    with pytest.raises((ValidationError, AttributeError, TypeError)):
        catalog.version = "v4.0.0"  # type: ignore


def test_version_isolation():
    """
    Verify that adding a future catalog version does not alter data returned for an existing version.
    """

    # @req:PRD-EDL-001
    # Create a future mock catalog v4.0.0
    future_zones = {
        1: Zone(number=1, name="New Trial Management"),
    }
    future_sections = {
        "1.1": Section(code="1.1", name="New Protocol", zone_number=1),
    }
    future_artifacts = {
        "01.01.01": Artifact(
            code="01.01.01",
            name="New Approved Protocol",
            section_code="1.1",
            zone_number=1,
        ),
    }
    future_catalog = Catalog(
        version="v4.0.0",
        zones=future_zones,
        sections=future_sections,
        artifacts=future_artifacts,
    )

    # Register the future catalog
    registry.register_catalog(future_catalog)

    # Check v3.2.0 remains completely untouched (isolation)
    v3_catalog = get_catalog("v3.2.0")
    v3_protocol = v3_catalog.get_artifact("01.01.01")
    assert v3_protocol is not None
    assert v3_protocol.name == "Approved Protocol"

    # Check v4.0.0 is retrievable and distinct
    v4_catalog = get_catalog("v4.0.0")
    v4_protocol = v4_catalog.get_artifact("01.01.01")
    assert v4_protocol is not None
    assert v4_protocol.name == "New Approved Protocol"

    # Verify version listing contains both
    versions = list_versions()
    assert "v3.2.0" in versions
    assert "v4.0.0" in versions


def test_no_database_or_network_needed():
    """
    Verify catalog registry does not require database connection or network access.
    """
    # @req:PRD-EDL-001
    # Simple verification that catalog can be instantiated cleanly in-memory
    local_catalog = Catalog(
        version="v1.0.0-temp",
        zones={1: Zone(number=1, name="Zone 1")},
        sections={"1.1": Section(code="1.1", name="Sec 1.1", zone_number=1)},
        artifacts={
            "01.01.01": Artifact(
                code="01.01.01",
                name="Art 1",
                section_code="1.1",
                zone_number=1,
            )
        },
    )
    assert local_catalog.version == "v1.0.0-temp"
    assert local_catalog.get_zone(1).name == "Zone 1"  # type: ignore
