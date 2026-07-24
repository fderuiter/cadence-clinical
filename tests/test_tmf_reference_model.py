import pytest
from pydantic import ValidationError
from tmf_reference_model import (
    build_catalog,
    get_active_catalog,
    get_catalog,
    get_registered_versions,
    register_catalog,
    set_active_version,
)


def test_active_version_selection():
    """
    Test that a consumer can obtain the active default version and get registered versions.
    """
    active_catalog = get_active_catalog()
    assert active_catalog is not None
    assert active_catalog.version == "v3.2.0"

    registered = get_registered_versions()
    assert "v3.2.0" in registered


def test_explicit_version_selection():
    """
    Test that a consumer can select a catalog by explicit version.
    """
    catalog = get_catalog("v3.2.0")
    assert catalog is not None
    assert catalog.version == "v3.2.0"

    with pytest.raises(KeyError):
        get_catalog("non_existent_version")


def test_canonical_11_zones():
    """
    Test that the active catalog includes exactly the 11 named DIA zones in correct order/codes:
    - Trial Management
    - Central Trial Documents
    - Regulatory
    - IRB/IEC & other Approvals
    - Site Management
    - IP & Trial Supplies
    - Safety Reporting
    - Centralized & Local Testing
    - Third Parties
    - Data Management
    - Statistics
    """
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

    for zone in catalog.zones:
        assert zone.code in expected_zones
        assert zone.name == expected_zones[zone.code]


def test_artifact_parent_identification():
    """
    Test that each artifact uniquely and deterministically identifies its parent section and zone.
    """
    catalog = get_active_catalog()

    # Test specific well-known artifacts
    protocol = catalog.get_artifact("01.01.01")
    assert protocol is not None
    assert protocol.name == "Clinical Trial Protocol"
    assert protocol.section_code == "01.01"
    assert protocol.zone_code == 1

    ib = catalog.get_artifact("02.01.01")
    assert ib is not None
    assert ib.name == "Investigator's Brochure"
    assert ib.section_code == "02.01"
    assert ib.zone_code == 2

    # Verify lookups
    assert catalog.get_artifact("invalid_code") is None

    # Verify parent section and zone resolution
    for zone in catalog.zones:
        for section in zone.sections:
            assert section.zone_code == zone.code
            for artifact in section.artifacts:
                assert artifact.section_code == section.code
                assert artifact.zone_code == zone.code


def test_version_isolation():
    """
    Test that adding a future catalog version does not alter data returned for an existing version,
    and the registry rejects duplicate registrations to ensure immutability of registry mappings.
    """
    # Create a new future catalog version
    future_raw = {
        1: (
            "Trial Management",
            {
                "01.01": (
                    "Trial Design",
                    [("01.01.01", "Clinical Trial Protocol Future Edition")],
                )
            },
        )
    }
    future_catalog = build_catalog("v4.0.0", future_raw)

    register_catalog(future_catalog)
    assert "v4.0.0" in get_registered_versions()

    # Verify that the old version still returns its original data
    v3_catalog = get_catalog("v3.2.0")
    protocol_v3 = v3_catalog.get_artifact("01.01.01")
    assert protocol_v3.name == "Clinical Trial Protocol"

    # Verify future version returns the new data
    v4_catalog = get_catalog("v4.0.0")
    protocol_v4 = v4_catalog.get_artifact("01.01.01")
    assert protocol_v4.name == "Clinical Trial Protocol Future Edition"

    # Try setting active to v4.0.0 and switching back to verify setting active works
    original_active = get_active_catalog().version
    set_active_version("v4.0.0")
    assert get_active_catalog().version == "v4.0.0"

    set_active_version(original_active)
    assert get_active_catalog().version == original_active

    # Registering duplicate version must fail
    with pytest.raises(ValueError, match="is already registered"):
        register_catalog(future_catalog)


def test_immutability_properties():
    """
    Verify that Zone, Section, Artifact, and TaxonomyCatalog records are immutable at runtime.
    """
    catalog = get_active_catalog()
    zone = catalog.zones[0]
    section = zone.sections[0]
    artifact = section.artifacts[0]

    # Verify Pydantic frozen configuration raises errors upon mutation
    with pytest.raises(ValidationError):
        # Directly trying to assign/mutate fields on a frozen model throws error
        # Pydantic raises ValidationError or AttributeError
        artifact.name = "Mutated Name"

    with pytest.raises(ValidationError):
        section.name = "Mutated Section"

    with pytest.raises(ValidationError):
        zone.name = "Mutated Zone"

    with pytest.raises(ValidationError):
        catalog.version = "v10.0.0"


def test_no_database_dependencies():
    """
    Verify catalog construction is pure in-memory and requires no external connections or database.
    """
    raw_minimal = {
        1: ("Trial Management", {"01.01": ("Trial Design", [("01.01.01", "Protocol")])})
    }
    pure_catalog = build_catalog("v_pure", raw_minimal)
    assert pure_catalog.version == "v_pure"
    assert len(pure_catalog.zones) == 1
    assert pure_catalog.get_artifact("01.01.01").name == "Protocol"


def test_resolve_artifact_success():
    """
    Test successful scenarios for resolve_artifact function:
    - lookup by exact canonical code
    - lookup by case-insensitive name
    - lookup by both
    """
    from tmf_reference_model import resolve_artifact

    # Lookup by exact canonical code
    res_code = resolve_artifact("v3.2.0", code="01.01.01")
    assert res_code["artifact"].code == "01.01.01"
    assert res_code["artifact"].name == "Clinical Trial Protocol"
    assert res_code["section"].code == "01.01"
    assert res_code["zone"].code == 1
    assert res_code["version"] == "v3.2.0"

    # Lookup by case-insensitive normalized display-name
    res_name = resolve_artifact("v3.2.0", name="  clinical trial protocol  ")
    assert res_name["artifact"].code == "01.01.01"
    assert res_name["artifact"].name == "Clinical Trial Protocol"

    # Lookup by both and ensuring they match
    res_both = resolve_artifact(
        "v3.2.0", code="01.01.01", name="Clinical Trial Protocol"
    )
    assert res_both["artifact"].code == "01.01.01"


def test_resolve_artifact_failures():
    """
    Test failure scenarios for resolve_artifact function:
    - unknown catalog version
    - unknown artifact code
    - unknown artifact name
    - ambiguous artifact name (if multiple artifacts share the same name)
    - mismatched code and name combination
    - neither code nor name provided
    """
    from tmf_reference_model import build_catalog, register_catalog, resolve_artifact

    # Unknown catalog version
    with pytest.raises(ValueError, match="Unknown catalog version 'v999'"):
        resolve_artifact("v999", code="01.01.01")

    # Neither code nor name provided
    with pytest.raises(ValueError, match="Must provide either 'code' or 'name'"):
        resolve_artifact("v3.2.0")

    # Unknown artifact code
    with pytest.raises(ValueError, match="Unknown artifact with code '99.99.99'"):
        resolve_artifact("v3.2.0", code="99.99.99")

    # Unknown artifact name
    with pytest.raises(ValueError, match="Unknown artifact with name 'Invalid Name'"):
        resolve_artifact("v3.2.0", name="Invalid Name")

    # Mismatched code and name combination
    with pytest.raises(ValueError, match="Mismatched artifact combination"):
        resolve_artifact("v3.2.0", code="01.01.01", name="Investigator's Brochure")

    # Ambiguous artifact name
    raw_ambiguous = {
        1: (
            "Zone A",
            {
                "01.01": (
                    "Sec A",
                    [
                        ("01.01.01", "Duplicate Name"),
                        ("01.01.02", "Duplicate Name"),
                    ],
                )
            },
        )
    }
    ambig_cat = build_catalog("v_ambiguous", raw_ambiguous)
    register_catalog(ambig_cat)

    with pytest.raises(
        ValueError, match="Ambiguous artifact input for name 'Duplicate Name'"
    ):
        resolve_artifact("v_ambiguous", name="Duplicate Name")


def test_validate_hierarchy_success():
    """
    Test validate_hierarchy success path with valid combinations.
    """
    from tmf_reference_model import validate_hierarchy

    # Valid: zone 1, section 01.01, artifact 01.01.01 in version v3.2.0
    validate_hierarchy(
        "v3.2.0", zone_code=1, section_code="01.01", artifact_code="01.01.01"
    )


def test_validate_hierarchy_failures():
    """
    Test validate_hierarchy failure scenarios:
    - unknown version
    - unknown zone
    - unknown section
    - unknown artifact
    - mismatched section in zone
    - mismatched artifact in section
    - mismatched artifact in zone
    """
    from tmf_reference_model import validate_hierarchy

    # Unknown version
    with pytest.raises(ValueError, match="Unknown catalog version 'v999'"):
        validate_hierarchy(
            "v999", zone_code=1, section_code="01.01", artifact_code="01.01.01"
        )

    # Unknown zone code
    with pytest.raises(ValueError, match="Unknown zone code 999"):
        validate_hierarchy(
            "v3.2.0", zone_code=999, section_code="01.01", artifact_code="01.01.01"
        )

    # Unknown section code
    with pytest.raises(ValueError, match="Unknown section code '99.99'"):
        validate_hierarchy(
            "v3.2.0", zone_code=1, section_code="99.99", artifact_code="01.01.01"
        )

    # Unknown artifact code
    with pytest.raises(ValueError, match="Unknown artifact code '99.99.99'"):
        validate_hierarchy(
            "v3.2.0", zone_code=1, section_code="01.01", artifact_code="99.99.99"
        )

    # Mismatched section in zone (e.g., section 02.01 belongs to zone 2, not zone 1)
    with pytest.raises(
        ValueError, match="Mismatched hierarchy: section '02.01' belongs to zone 2"
    ):
        validate_hierarchy(
            "v3.2.0", zone_code=1, section_code="02.01", artifact_code="02.01.01"
        )

    # Mismatched artifact in section (e.g., artifact 02.01.01 belongs to section 02.01, not 01.01)
    with pytest.raises(
        ValueError,
        match="Mismatched hierarchy: artifact '02.01.01' belongs to section '02.01'",
    ):
        validate_hierarchy(
            "v3.2.0", zone_code=1, section_code="01.01", artifact_code="02.01.01"
        )


def test_get_mandatory_artifacts_success():
    """
    Test get_mandatory_artifacts for milestones:
    - INITIATION / STUDY START
    - CONDUCT / DATA COLLECTION
    - CLOSEOUT / LOCK
    """
    from tmf_reference_model import get_mandatory_artifacts

    # INITIATION
    init_arts = get_mandatory_artifacts("INITIATION", "v3.2.0")
    assert len(init_arts) == 1
    assert init_arts[0].code == "01.01.01"

    init_alias = get_mandatory_artifacts("STUDY START", "v3.2.0")
    assert len(init_alias) == 1
    assert init_alias[0].code == "01.01.01"

    # CONDUCT
    conduct_arts = get_mandatory_artifacts("CONDUCT", "v3.2.0")
    assert len(conduct_arts) == 3
    codes = {art.code for art in conduct_arts}
    assert codes == {"01.01.01", "10.01.02", "10.02.01"}

    # CLOSEOUT
    closeout_arts = get_mandatory_artifacts("CLOSEOUT", "v3.2.0")
    assert len(closeout_arts) == 4
    closeout_codes = {art.code for art in closeout_arts}
    assert closeout_codes == {"01.01.01", "10.01.02", "10.02.01", "11.01.02"}


def test_get_mandatory_artifacts_failures():
    """
    Test get_mandatory_artifacts failure scenarios:
    - unknown version
    - unknown milestone
    - mandatory artifact not in the requested version
    """
    from tmf_reference_model import (
        build_catalog,
        get_mandatory_artifacts,
        register_catalog,
    )

    # Unknown version
    with pytest.raises(ValueError, match="Unknown catalog version 'v999'"):
        get_mandatory_artifacts("INITIATION", "v999")

    # Unknown milestone
    with pytest.raises(ValueError, match="Unknown milestone 'INVALID_MILESTONE'"):
        get_mandatory_artifacts("INVALID_MILESTONE", "v3.2.0")

    # Mandatory artifact missing from version
    raw_minimal = {
        1: (
            "Trial Management",
            {"01.01": ("Trial Design", [("01.01.02", "Other Document")])},
        )
    }
    min_cat = build_catalog("v_minimal_missing", raw_minimal)
    register_catalog(min_cat)

    with pytest.raises(
        ValueError,
        match="Mandatory artifact code '01.01.01' for milestone 'INITIATION' not found",
    ):
        get_mandatory_artifacts("INITIATION", "v_minimal_missing")
