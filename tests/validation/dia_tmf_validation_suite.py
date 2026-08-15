import ast
from pathlib import Path

import pytest

from apps.etmf.domain.tmf_reference_model import (
    MILESTONE_MANDATORY_ARTIFACTS,
    get_active_catalog,
    get_catalog,
    get_mandatory_artifacts,
    resolve_artifact,
    validate_hierarchy,
)
from apps.etmf.models import is_site_level_artifact

# Documented legacy allow-list constant for site-level artifacts
# "site signature page" is a legacy/unassigned artifact name that is not
# present in the modern DIA TMF reference model catalog versions.
LEGACY_SITE_ARTIFACTS_ALLOW_LIST = {
    "site signature page",
}


def test_catalog_cross_version_integrity():
    """
    Validation Suite - Cross-version catalog integrity and hierarchy check.
    Validates artifact-code uniqueness, correct zone/section mapping,
    and extension policies across all registered versions.
    @req:PRD-TMF-001
    """
    versions = ["v3.2.0", "v3.2.0-complete", "v3.2.0-extended"]
    extension_codes = {"05.02.98", "05.02.99", "10.01.99"}

    for version in versions:
        catalog = get_catalog(version)
        assert catalog is not None, (
            f"Catalog version '{version}' could not be retrieved."
        )

        # Walk every zone, section, and artifact, and collect artifact codes
        artifact_codes = []
        for zone in catalog.zones:
            for section in zone.sections:
                for artifact in section.artifacts:
                    # Validate hierarchy: assert it returns without raising
                    validate_hierarchy(
                        version,
                        zone_code=zone.code,
                        section_code=section.code,
                        artifact_code=artifact.code,
                    )
                    artifact_codes.append(artifact.code)

                    # Assert that no standard artifact code equals any extension code
                    if not artifact.is_extension:
                        assert artifact.code not in extension_codes, (
                            f"Standard artifact '{artifact.code}' matches an extension code!"
                        )

        # Assert artifact-code uniqueness across the whole catalog
        unique_codes = set(artifact_codes)
        assert len(artifact_codes) == len(unique_codes), (
            f"Duplicate artifact codes detected in version '{version}'!"
        )

    # Extension policy checks
    extended_catalog = get_catalog("v3.2.0-extended")
    complete_catalog = get_catalog("v3.2.0-complete")

    for ext_code in extension_codes:
        # Assert the artifact exists in v3.2.0-extended with is_extension=True
        ext_art = extended_catalog.get_artifact(ext_code)
        assert ext_art is not None, (
            f"Extension artifact '{ext_code}' not found in v3.2.0-extended."
        )
        assert ext_art.is_extension is True, (
            f"Artifact '{ext_code}' in extended catalog is not marked as extension."
        )

        # Assert v3.2.0-complete returns None from get_artifact() for the same code
        comp_art = complete_catalog.get_artifact(ext_code)
        assert comp_art is None, (
            f"Extension artifact '{ext_code}' should not exist in v3.2.0-complete."
        )


def test_site_level_classification_drift():
    """
    Validation Suite - Site-level classification drift protection.
    Guards is_site_level_artifact against silent drift from the active catalog.
    @req:PRD-TMF-001
    @req:PRD-TMF-003
    """
    # Read the active catalog
    active_catalog = get_active_catalog()
    assert active_catalog is not None

    # Dynamically extract helper's local variables (site_codes_prefix and site_artifacts) using AST parser
    models_file_path = (
        Path(__file__).resolve().parent.parent.parent / "apps" / "etmf" / "models.py"
    )
    assert models_file_path.is_file(), f"Models file not found at {models_file_path}"

    tree = ast.parse(models_file_path.read_text())
    site_artifacts = None
    site_codes_prefix = None

    for node in ast.walk(tree):
        if isinstance(node, ast.FunctionDef) and node.name == "is_site_level_artifact":
            for stmt in node.body:
                if isinstance(stmt, ast.Assign):
                    for target in stmt.targets:
                        if isinstance(target, ast.Name):
                            if target.id == "site_artifacts" and isinstance(
                                stmt.value, ast.Set
                            ):
                                site_artifacts = {elt.value for elt in stmt.value.elts}
                            elif target.id == "site_codes_prefix" and isinstance(
                                stmt.value, ast.Set
                            ):
                                site_codes_prefix = {
                                    elt.value for elt in stmt.value.elts
                                }

    assert site_artifacts is not None, (
        "Failed to dynamically parse 'site_artifacts' from apps/etmf/models.py"
    )
    assert site_codes_prefix is not None, (
        "Failed to dynamically parse 'site_codes_prefix' from apps/etmf/models.py"
    )

    # For every prefix in site_codes_prefix, assert the prefix resolves to a real section in the active catalog
    for prefix in site_codes_prefix:
        section = active_catalog.get_section(prefix)
        assert section is not None, (
            f"Prefix '{prefix}' does not resolve to a real section in active catalog."
        )

    # For every name in site_artifacts that is not in the legacy allow-list, assert it resolves to a catalog artifact by name
    for name in site_artifacts:
        if name not in LEGACY_SITE_ARTIFACTS_ALLOW_LIST:
            resolved = resolve_artifact(active_catalog.version, name=name)
            assert resolved is not None
            assert resolved["artifact"] is not None
            assert resolved["artifact"].name.strip().lower() == name.strip().lower()

    # Assert known site-level code returns True, and a study-level code returns False
    assert is_site_level_artifact("some type", "05.02.04") is True
    assert is_site_level_artifact("some type", "01.01.01") is False


def test_milestone_mandatory_artifacts():
    """
    Validation Suite - Milestone mandatory-artifact validation.
    Verifies milestone compliance requirements, closeout codes, and exception routes.
    @req:PRD-TMF-004
    """
    active_catalog = get_active_catalog()
    assert active_catalog is not None
    version = active_catalog.version

    milestones = ["INITIATION", "CONDUCT", "CLOSEOUT"]
    for ms in milestones:
        artifacts = get_mandatory_artifacts(ms, version)
        assert isinstance(artifacts, list)
        assert len(artifacts) > 0

        # Assert the CLOSEOUT result includes the artifact with code 11.01.02
        if ms == "CLOSEOUT":
            codes = {art.code for art in artifacts}
            assert "11.01.02" in codes, (
                "CLOSEOUT mandatory artifacts do not include code 11.01.02."
            )

    # Iterate every code in every list of MILESTONE_MANDATORY_ARTIFACTS and assert each resolves against the active catalog
    for ms_name, code_list in MILESTONE_MANDATORY_ARTIFACTS.items():
        for code in code_list:
            art = active_catalog.get_artifact(code)
            assert art is not None, (
                f"Mandatory code '{code}' under milestone '{ms_name}' does not exist in active catalog."
            )

    # Assert get_mandatory_artifacts raises ValueError for an unknown milestone name
    with pytest.raises(ValueError, match="Unknown milestone"):
        get_mandatory_artifacts("UNKNOWN_MILESTONE", version)

    # Assert get_mandatory_artifacts raises ValueError for an unknown version string
    with pytest.raises(ValueError, match="Unknown catalog version"):
        get_mandatory_artifacts("INITIATION", "vUNKNOWN_VERSION")
