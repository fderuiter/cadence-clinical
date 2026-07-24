"""
Unit tests for the canonical, versioned DIA TMF Reference Model catalog in packages/core-models.
Verifies active-version selection, representative hierarchy records, and version isolation
for regulatory compliance.
"""

import pytest
import pydantic

# Injected path allows direct import from the hyphenated packages/core-models folder
from tmf_reference_model import (
    Zone,
    Section,
    Artifact,
    TaxonomyCatalog,
    get_catalog,
    get_active_catalog,
    list_catalog_versions,
    register_catalog,
    set_active_taxonomy_version,
)


def test_active_version_and_selection():
    """
    Test selection of taxonomy catalog by explicit version and active default version.
    """
    # @req:PRD-EDL-001
    active_catalog = get_active_catalog()
    assert active_catalog is not None
    assert active_catalog.version == "v3.2.0"

    # Select by explicit version
    explicit_catalog = get_catalog("v3.2.0")
    assert explicit_catalog is not None
    assert explicit_catalog.version == "v3.2.0"
    assert explicit_catalog == active_catalog


def test_canonical_dia_zones_coverage():
    """
    Test that the active catalog contains exactly the 11 named DIA zones.
    """
    # @req:PRD-EDL-001
    catalog = get_active_catalog()

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

    # Verify count and exact mapping
    assert len(catalog.zones) == 11
    for num, name in expected_zones.items():
        zone = catalog.get_zone(num)
        assert zone is not None
        assert zone.number == num
        assert zone.name == name


def test_hierarchy_relationships_and_lookup():
    """
    Test that each artifact uniquely and deterministically identifies its parent section and zone.
    """
    # @req:PRD-EDL-001
    catalog = get_active_catalog()

    # Retrieve representative artifact: Approved Protocol
    protocol_art = catalog.get_artifact("01.01.01")
    assert protocol_art is not None
    assert protocol_art.name == "Approved Protocol"
    assert protocol_art.section_number == "1.1"
    assert protocol_art.zone_number == 1

    # Verify lookups trace back to correct section and zone
    section = catalog.get_section(protocol_art.section_number)
    assert section is not None
    assert section.name == "Protocol"
    assert section.zone_number == 1

    zone = catalog.get_zone(protocol_art.zone_number)
    assert zone is not None
    assert zone.name == "Trial Management"


def test_case_insensitive_and_code_lookups():
    """
    Verify robust lookups by code or case-insensitive artifact names.
    """
    # @req:PRD-EDL-001
    catalog = get_active_catalog()

    # Look up by lowercase name
    art1 = catalog.get_artifact("approved protocol")
    assert art1 is not None
    assert art1.code == "01.01.01"

    # Look up with extra whitespace
    art2 = catalog.get_artifact("   Define-XML   ")
    assert art2 is not None
    assert art2.code == "10.01.01"


def test_immutability_at_runtime():
    """
    Verify catalog data structure is completely immutable at runtime.
    """
    # @req:PRD-EDL-001
    catalog = get_active_catalog()

    with pytest.raises(pydantic.ValidationError) as exc:
        # Pydantic v2 frozen models prevent modification
        catalog.version = "vNew"

    # Try modifying a zone property
    zone = catalog.get_zone(1)
    with pytest.raises(pydantic.ValidationError):
        zone.name = "Modified Trial Management"


def test_helper_methods_coverage():
    """
    Test additional helper methods in TaxonomyCatalog for maximum coverage.
    """
    # @req:PRD-EDL-001
    catalog = get_active_catalog()

    # Test get_sections_for_zone
    sections = catalog.get_sections_for_zone(1)
    assert len(sections) == 2
    assert [s.number for s in sections] == ["1.1", "1.2"]

    # Test get_artifacts_for_section
    artifacts = catalog.get_artifacts_for_section("1.1")
    assert len(artifacts) == 2
    assert [a.code for a in artifacts] == ["01.01.01", "01.01.02"]


def test_registry_error_paths_coverage():
    """
    Test registry exception handling paths.
    """
    # @req:PRD-EDL-001
    from tmf_reference_model.registry import TaxonomyRegistry

    reg = TaxonomyRegistry()

    # 1. get_catalog when version not found
    with pytest.raises(KeyError) as exc_info:
        reg.get_catalog("v9.9.9")
    assert "not found" in str(exc_info.value)

    # 2. get_active_catalog when none set
    with pytest.raises(ValueError) as exc_info:
        reg.get_active_catalog()
    assert "No active taxonomy catalog version" in str(exc_info.value)

    # 3. set_active_version when not registered
    with pytest.raises(KeyError) as exc_info:
        reg.set_active_version("v9.9.9")
    assert "not registered" in str(exc_info.value)


def test_version_isolation():
    """
    Test version isolation: adding a future catalog version does not alter data returned for an existing version.
    """
    # @req:PRD-EDL-001
    original_catalog = get_catalog("v3.2.0")
    original_versions = list_catalog_versions()

    # Create a future/mock catalog version (e.g. v4.0.0)
    future_catalog = TaxonomyCatalog(
        version="v4.0.0",
        zones={
            1: Zone(number=1, name="Future Trial Management"),
        },
        sections={
            "1.1": Section(number="1.1", name="Future Protocol", zone_number=1),
        },
        artifacts={
            "01.01.01": Artifact(
                code="01.01.01", name="Future Protocol Doc", section_number="1.1", zone_number=1
            )
        },
        artifact_name_map={
            "future protocol doc": Artifact(
                code="01.01.01", name="Future Protocol Doc", section_number="1.1", zone_number=1
            )
        }
    )

    # Register the future catalog
    register_catalog(future_catalog)

    # Verify registry list has updated
    new_versions = list_catalog_versions()
    assert "v4.0.0" in new_versions
    assert "v3.2.0" in new_versions

    # Ensure original catalog's details are absolutely untouched
    v3_2_catalog_after = get_catalog("v3.2.0")
    assert v3_2_catalog_after.version == "v3.2.0"
    assert v3_2_catalog_after.get_zone(1).name == "Trial Management"
    assert v3_2_catalog_after.get_artifact("01.01.01").name == "Approved Protocol"

    # Ensure future catalog returns its correct custom isolated data
    v4_catalog = get_catalog("v4.0.0")
    assert v4_catalog.version == "v4.0.0"
    assert v4_catalog.get_zone(1).name == "Future Trial Management"
    assert v4_catalog.get_artifact("01.01.01").name == "Future Protocol Doc"

    # Switch active version and test active selection isolation
    set_active_taxonomy_version("v4.0.0")
    assert get_active_catalog().version == "v4.0.0"

    # Restore baseline
    set_active_taxonomy_version("v3.2.0")
    assert get_active_catalog().version == "v3.2.0"
