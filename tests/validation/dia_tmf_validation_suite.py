"""
DIA TMF validation suite.

This module contains GxP qualification and validation tests for the
DIA TMF reference model, catalog-invariant consistency, site-level classification,
and milestone mandatory-artifact validation.
"""

import pytest
from apps.etmf.models import is_site_level_artifact
from tmf_reference_model import (
    MILESTONE_MANDATORY_ARTIFACTS,
    get_active_catalog,
    get_catalog,
    get_mandatory_artifacts,
    validate_hierarchy,
)

# Legacy allow-list for site-level artifacts.
# "site signature page" is included because it is not present in any registered catalog version,
# but remains supported for backward compatibility with older legacy metadata schemas.
LEGACY_ALLOW_LIST = {"site signature page"}


def test_catalog_invariants_uniqueness_integrity_and_extensions():
    """
    Verify catalog uniqueness, hierarchy integrity, and extension-flag correctness.

    @req:PRD-TMF-001
    @req:PRD-TMF-002
    """
    versions = ["v3.2.0", "v3.2.0-complete", "v3.2.0-extended"]

    for version in versions:
        catalog = get_catalog(version)
        assert catalog is not None

        # Collect all artifact codes to verify uniqueness invariant
        artifact_codes = []
        for zone in catalog.zones:
            for section in zone.sections:
                for artifact in section.artifacts:
                    artifact_codes.append(artifact.code)
                    # For every artifact, call validate_hierarchy and assert it does not raise
                    validate_hierarchy(
                        version=version,
                        zone_code=zone.code,
                        section_code=section.code,
                        artifact_code=artifact.code,
                    )

        # Assert uniqueness invariant
        assert len(artifact_codes) == len(set(artifact_codes)), (
            f"Artifact codes in catalog version '{version}' are not unique."
        )

    # Extension-flag correctness
    extended_catalog = get_catalog("v3.2.0-extended")
    complete_catalog = get_catalog("v3.2.0-complete")
    extension_codes = ["05.02.98", "05.02.99", "10.01.99"]

    for ext_code in extension_codes:
        # Each has is_extension=True in v3.2.0-extended
        art_extended = extended_catalog.get_artifact(ext_code)
        assert art_extended is not None
        assert art_extended.is_extension is True

        # Each is absent from v3.2.0-complete
        art_complete = complete_catalog.get_artifact(ext_code)
        assert art_complete is None

    # Assert no standard artifact code in any version equals one of the three extension codes
    for version in versions:
        catalog = get_catalog(version)
        for zone in catalog.zones:
            for section in zone.sections:
                for artifact in section.artifacts:
                    if not artifact.is_extension:
                        assert artifact.code not in extension_codes, (
                            f"Standard artifact code '{artifact.code}' collides with custom extensions."
                        )


def test_site_level_classification_drift_protection():
    """
    Guard is_site_level_artifact against silent drift from the active catalog.

    @req:PRD-TMF-003
    """
    site_codes_prefix = {"05.02", "04.01", "05.01"}
    site_artifacts = {
        "fda form 1572",
        "financial disclosure",
        "investigator cv",
        "delegation of authority log",
        "site signature page",
        "site feasibility survey",
        "informed consent form",
    }

    catalog = get_active_catalog()

    # Assert each prefix resolves to a real section in the active catalog
    for prefix in site_codes_prefix:
        section = catalog.get_section(prefix)
        assert section is not None, f"Prefix '{prefix}' does not resolve to a section in the active catalog."

    # Assert each name in site_artifacts resolves to a catalog artifact by name,
    # except for a documented legacy allow-list.
    for name in site_artifacts:
        if name in LEGACY_ALLOW_LIST:
            continue

        resolved = False
        for art in catalog.artifact_map.values():
            if art.name.strip().lower() == name.strip().lower():
                resolved = True
                break
        assert resolved, f"Site-level artifact name '{name}' did not resolve to any active catalog artifact."

    # Assert that a known site-level code classifies as site-level, and study-level does not
    assert is_site_level_artifact("Delegation of Authority Log", "05.02.04") is True
    assert is_site_level_artifact("Clinical Trial Protocol", "01.01.01") is False


def test_milestone_mandatory_artifact_validation():
    """
    Verify get_mandatory_artifacts and CLOSEOUT coverage.

    @req:PRD-TMF-004
    """
    active_catalog = get_active_catalog()
    active_version = active_catalog.version

    # For INITIATION, CONDUCT, and CLOSEOUT, call get_mandatory_artifacts
    for milestone in ["INITIATION", "CONDUCT", "CLOSEOUT"]:
        artifacts = get_mandatory_artifacts(milestone, active_version)
        returned_codes = [art.code for art in artifacts]
        expected_codes = MILESTONE_MANDATORY_ARTIFACTS[milestone]
        assert returned_codes == expected_codes, (
            f"Returned mandatory artifacts for '{milestone}' do not match expectation."
        )

    # Assert CLOSEOUT result includes 11.01.02
    closeout_artifacts = get_mandatory_artifacts("CLOSEOUT", active_version)
    closeout_codes = [art.code for art in closeout_artifacts]
    assert "11.01.02" in closeout_codes, "CLOSEOUT mandatory artifacts do not include code '11.01.02'."

    # Assert every code in every list of MILESTONE_MANDATORY_ARTIFACTS resolves against active catalog
    for milestone_name, codes in MILESTONE_MANDATORY_ARTIFACTS.items():
        for code in codes:
            assert active_catalog.get_artifact(code) is not None, (
                f"Mandatory code '{code}' for milestone '{milestone_name}' does not resolve against the active catalog."
            )

    # Assert get_mandatory_artifacts raises ValueError for an unknown milestone
    with pytest.raises(ValueError):
        get_mandatory_artifacts("NOT_A_MILESTONE", active_version)

    # Assert get_mandatory_artifacts raises ValueError for an unknown version
    with pytest.raises(ValueError):
        get_mandatory_artifacts("INITIATION", "v9.9.9")
