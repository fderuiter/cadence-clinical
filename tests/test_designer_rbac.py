"""Unit tests for the centralized RBAC matrix configurations of designer resources.

Specifically verifies permissions for global_library, mdr_concept, protocol_export,
and designer_cache across various roles, including sysadmin, sponsor_designer,
sponsor_dm, auditor, subject, and site-scoped roles.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import pytest

from packages.security.rbac import (
    ROLE_AUDITOR_CANONICAL,
    ROLE_CRA_CANONICAL,
    ROLE_CRC,
    ROLE_INVESTIGATOR,
    ROLE_SPONSOR_DESIGNER,
    ROLE_SPONSOR_DM,
    ROLE_SUBJECT,
    ROLE_SYSADMIN,
    Principal,
    has_permission,
)


@pytest.fixture
def principals():
    """Returns initialized Principal objects for distinct roles."""
    return {
        "sysadmin": Principal(user_id="sys1", roles=[ROLE_SYSADMIN]),
        "designer": Principal(user_id="des1", roles=[ROLE_SPONSOR_DESIGNER]),
        "dm": Principal(user_id="dm1", roles=[ROLE_SPONSOR_DM]),
        "admin_role": Principal(user_id="adm1", roles=["admin"]),
        "auditor": Principal(user_id="aud1", roles=[ROLE_AUDITOR_CANONICAL]),
        "subject": Principal(user_id="sub1", roles=[ROLE_SUBJECT]),
        "cra": Principal(user_id="cra1", roles=[ROLE_CRA_CANONICAL]),
        "crc": Principal(user_id="crc1", roles=[ROLE_CRC]),
        "investigator": Principal(user_id="inv1", roles=[ROLE_INVESTIGATOR]),
    }


def test_sysadmin_permissions(principals):
    """Verify ROLE_SYSADMIN has full administrative rights over all designer resources."""
    sysadmin = principals["sysadmin"]

    # global_library permissions
    assert has_permission(sysadmin, "global_library:create") is True
    assert has_permission(sysadmin, "global_library:update") is True
    assert has_permission(sysadmin, "global_library:amend") is True
    assert has_permission(sysadmin, "global_library:transition") is True
    assert has_permission(sysadmin, "global_library:instantiate") is True
    assert has_permission(sysadmin, "global_library:read") is True

    # mdr_concept permissions
    assert has_permission(sysadmin, "mdr_concept:create") is True
    assert has_permission(sysadmin, "mdr_concept:update") is True
    assert has_permission(sysadmin, "mdr_concept:rename") is True
    assert has_permission(sysadmin, "mdr_concept:delete") is True
    assert has_permission(sysadmin, "mdr_concept:read") is True

    # protocol_export permissions
    assert has_permission(sysadmin, "protocol_export:generate") is True
    assert has_permission(sysadmin, "protocol_export:read") is True

    # designer_cache permissions
    assert has_permission(sysadmin, "designer_cache:admin") is True

    # study_design:approve permission
    assert has_permission(sysadmin, "study_design:approve") is True


def test_sponsor_designer_permissions(principals):
    """Verify ROLE_SPONSOR_DESIGNER has full authoring rights over all designer resources."""
    designer = principals["designer"]

    # global_library permissions
    assert has_permission(designer, "global_library:create") is True
    assert has_permission(designer, "global_library:update") is True
    assert has_permission(designer, "global_library:amend") is True
    assert has_permission(designer, "global_library:transition") is True
    assert has_permission(designer, "global_library:instantiate") is True
    assert has_permission(designer, "global_library:read") is True

    # mdr_concept permissions
    assert has_permission(designer, "mdr_concept:create") is True
    assert has_permission(designer, "mdr_concept:update") is True
    assert has_permission(designer, "mdr_concept:rename") is True
    assert has_permission(designer, "mdr_concept:delete") is True
    assert has_permission(designer, "mdr_concept:read") is True

    # protocol_export permissions
    assert has_permission(designer, "protocol_export:generate") is True
    assert has_permission(designer, "protocol_export:read") is True

    # designer_cache permissions
    assert has_permission(designer, "designer_cache:admin") is True

    # study_design:approve permission
    assert has_permission(designer, "study_design:approve") is True


def test_sponsor_dm_and_admin_permissions(principals):
    """Verify ROLE_SPONSOR_DM and 'admin' roles have specific library-transition, concept read, and approve permissions."""
    for role_name in ("dm", "admin_role"):
        principal = principals[role_name]

        # global_library transition and read should be allowed
        assert has_permission(principal, "global_library:transition") is True
        assert has_permission(principal, "global_library:read") is True

        # other global_library mutations should be denied
        assert has_permission(principal, "global_library:create") is False
        assert has_permission(principal, "global_library:update") is False
        assert has_permission(principal, "global_library:amend") is False
        assert has_permission(principal, "global_library:instantiate") is False

        # mdr_concept read should be allowed, mutations denied
        assert has_permission(principal, "mdr_concept:read") is True
        assert has_permission(principal, "mdr_concept:create") is False
        assert has_permission(principal, "mdr_concept:update") is False
        assert has_permission(principal, "mdr_concept:rename") is False
        assert has_permission(principal, "mdr_concept:delete") is False

        # protocol_export generate and read should be allowed
        assert has_permission(principal, "protocol_export:generate") is True
        assert has_permission(principal, "protocol_export:read") is True

        # designer_cache admin should be denied
        assert has_permission(principal, "designer_cache:admin") is False

        # study_design:approve should be allowed
        assert has_permission(principal, "study_design:approve") is True


def test_restricted_roles_denied_designer_mutations(principals):
    """Verify read-only, subject, and site-scoped roles cannot perform any designer mutations or cache administration."""
    restricted_keys = ["auditor", "subject", "cra", "crc", "investigator"]

    for r_key in restricted_keys:
        principal = principals[r_key]

        # global_library writes denied
        assert has_permission(principal, "global_library:create") is False
        assert has_permission(principal, "global_library:update") is False
        assert has_permission(principal, "global_library:amend") is False
        assert has_permission(principal, "global_library:transition") is False
        assert has_permission(principal, "global_library:instantiate") is False

        # mdr_concept writes denied
        assert has_permission(principal, "mdr_concept:create") is False
        assert has_permission(principal, "mdr_concept:update") is False
        assert has_permission(principal, "mdr_concept:rename") is False
        assert has_permission(principal, "mdr_concept:delete") is False

        # protocol_export generate denied
        assert has_permission(principal, "protocol_export:generate") is False

        # designer_cache admin denied
        assert has_permission(principal, "designer_cache:admin") is False

        # study_design:approve denied
        assert has_permission(principal, "study_design:approve") is False
