"""
Unit and integration tests for Knowledge category hierarchy and retrieval.

Covers:
1. Root category creation with GxP audit metadata (21 CFR Part 11).
2. Child category creation with self-referential parent_id.
3. Validation rejecting non-existent parent_id (CategoryNotFoundError).
4. Validation rejecting duplicate category name or slug (CategoryConflictError).
5. Single category detail lookup by ID (get_category_by_id).
6. Single category lookup by slug (get_category_by_slug).
7. End-to-end REST API integration for POST /categories and GET /categories/{id}.

Requirements: PRD-SYS-KH-001
"""

from datetime import datetime

import httpx
import pytest
from httpx import ASGITransport
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.application.article_service import ArticleLifecycleService
from apps.knowledge.domain.models import CategoryConflictError, CategoryNotFoundError
from apps.knowledge.main import app
from packages.testing.security import create_test_auth_headers

ACTOR_ADMIN = "admin@cadence.clinical"  # deid-ignore
ACTOR_CRC = "crc@cadence.clinical"  # deid-ignore


@pytest.fixture
def auth_headers_admin() -> dict[str, str]:
    """Generates authentic gateway headers for super_admin."""
    return create_test_auth_headers(
        user_id=ACTOR_ADMIN,
        roles=["super_admin"],
        tenant_id="tenant_clinical_01",
    )


@pytest.fixture
def auth_headers_crc() -> dict[str, str]:
    """Generates authentic gateway headers for site CRC."""
    return create_test_auth_headers(
        user_id=ACTOR_CRC,
        roles=["site_crc", "crc"],
        tenant_id="tenant_clinical_01",
    )


# ---------------------------------------------------------------------------
# Unit tests — Service layer
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_create_root_category(db_session: AsyncSession):
    """
    Validate root category creation with complete GxP audit fields.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    category = await svc.create_category(
        name="Clinical SOPs",
        slug="clinical-sops",
        description="Standard operating procedures for clinical operations",
        persona_visibility="site_crc,cra_monitor",
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Initial root category setup",
    )

    assert category.id is not None
    assert category.name == "Clinical SOPs"
    assert category.slug == "clinical-sops"
    assert (
        category.description == "Standard operating procedures for clinical operations"
    )
    assert category.persona_visibility == "site_crc,cra_monitor"
    assert category.parent_id is None
    assert category.is_deleted is False
    assert category.created_by == ACTOR_ADMIN
    assert category.reason_for_change == "Initial root category setup"
    assert category.version_index == 1
    assert isinstance(category.created_at, datetime)


@pytest.mark.asyncio
async def test_create_child_category_with_parent(db_session: AsyncSession):
    """
    Validate nested category creation referencing a valid parent category.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    parent = await svc.create_category(
        name="Data Management",
        slug="data-management",
        description="Data management guidelines",
        persona_visibility="data_manager",
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Root parent category",
    )

    child = await svc.create_category(
        name="Medical Coding",
        slug="medical-coding",
        description="MedDRA and WHO Drug coding rules",
        persona_visibility="data_manager",
        parent_id=parent.id,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Subcategory for medical coding",
    )

    assert child.parent_id == parent.id
    assert child.slug == "medical-coding"


@pytest.mark.asyncio
async def test_create_category_with_nonexistent_parent_fails(
    db_session: AsyncSession,
):
    """
    Validate that providing an invalid parent_id raises CategoryNotFoundError.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    non_existent_id = "00000000-0000-0000-0000-000000000000"

    with pytest.raises(
        CategoryNotFoundError, match="does not exist or has been deleted"
    ):
        await svc.create_category(
            name="Invalid Category",
            slug="invalid-category",
            description="Testing missing parent",
            persona_visibility=None,
            parent_id=non_existent_id,
            actor_user_id=ACTOR_ADMIN,
            reason_for_change="Attempting invalid parent",
        )


@pytest.mark.asyncio
async def test_create_category_duplicate_name_or_slug_fails(
    db_session: AsyncSession,
):
    """
    Validate that category name and slug uniqueness are strictly enforced.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    await svc.create_category(
        name="Safety Reporting",
        slug="safety-reporting",
        description="Safety reporting SOPs",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Initial category",
    )

    # Duplicate name
    with pytest.raises(CategoryConflictError, match="already exists"):
        await svc.create_category(
            name="Safety Reporting",
            slug="safety-reporting-unique",
            description="Different slug but duplicate name",
            persona_visibility=None,
            parent_id=None,
            actor_user_id=ACTOR_ADMIN,
            reason_for_change="Duplicate name test",
        )

    # Duplicate slug
    with pytest.raises(CategoryConflictError, match="already exists"):
        await svc.create_category(
            name="Safety Reporting Alternate",
            slug="safety-reporting",
            description="Different name but duplicate slug",
            persona_visibility=None,
            parent_id=None,
            actor_user_id=ACTOR_ADMIN,
            reason_for_change="Duplicate slug test",
        )


@pytest.mark.asyncio
async def test_get_category_by_id_and_slug(db_session: AsyncSession):
    """
    Validate category lookup by ID and slug.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    created = await svc.create_category(
        name="Regulatory Submissions",
        slug="regulatory-submissions",
        description="Regulatory guidance",
        persona_visibility="auditor,sponsor_designer",
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Category lookup test",
    )

    by_id = await svc.get_category_by_id(created.id)
    assert by_id is not None
    assert by_id.id == created.id
    assert by_id.slug == "regulatory-submissions"

    by_slug = await svc.get_category_by_slug("regulatory-submissions")
    assert by_slug is not None
    assert by_slug.id == created.id

    missing = await svc.get_category_by_id("00000000-0000-0000-0000-000000000000")
    assert missing is None


# ---------------------------------------------------------------------------
# Integration tests — REST API Endpoints
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_api_create_and_get_category(
    db_session: AsyncSession,
    auth_headers_admin: dict[str, str],
    auth_headers_crc: dict[str, str],
):
    """
    Validate REST API category creation and retrieval with authentication.

    @req:PRD-SYS-KH-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Create root category as admin
        root_payload = {
            "name": "Platform Administration",
            "slug": "platform-admin",
            "description": "Admin manuals and guides",
            "persona_visibility": "super_admin,sysadmin",
            "parent_id": None,
            "reason_for_change": "Initial platform administration category",
        }
        create_resp = await client.post(
            "/api/v1/knowledge/categories",
            json=root_payload,
            headers=auth_headers_admin,
        )
        assert create_resp.status_code == 201
        root_data = create_resp.json()
        root_id = root_data["id"]
        assert root_data["name"] == "Platform Administration"
        assert root_data["slug"] == "platform-admin"
        assert root_data["parent_id"] is None
        assert root_data["version_index"] == 1

        # 2. Create child category referencing root
        child_payload = {
            "name": "User Access Provisioning",
            "slug": "user-access-provisioning",
            "description": "Step-by-step user onboarding",
            "persona_visibility": "super_admin",
            "parent_id": root_id,
            "reason_for_change": "Subcategory for user access",
        }
        child_resp = await client.post(
            "/api/v1/knowledge/categories",
            json=child_payload,
            headers=auth_headers_admin,
        )
        assert child_resp.status_code == 201
        child_data = child_resp.json()
        child_id = child_data["id"]
        assert child_data["parent_id"] == root_id

        # 3. Retrieve single category detail by ID
        get_resp = await client.get(
            f"/api/v1/knowledge/categories/{child_id}",
            headers=auth_headers_crc,
        )
        assert get_resp.status_code == 200
        get_data = get_resp.json()
        assert get_data["id"] == child_id
        assert get_data["name"] == "User Access Provisioning"
        assert get_data["parent_id"] == root_id

        # 4. Lookup non-existent category returns 404
        not_found_resp = await client.get(
            "/api/v1/knowledge/categories/00000000-0000-0000-0000-000000000000",
            headers=auth_headers_crc,
        )
        assert not_found_resp.status_code == 404
        assert "Category not found" in not_found_resp.json()["detail"]

        # 5. Creation with invalid parent_id returns 404
        invalid_parent_payload = {
            "name": "Orphaned Guide",
            "slug": "orphaned-guide",
            "description": "Invalid parent test",
            "persona_visibility": None,
            "parent_id": "00000000-0000-0000-0000-000000000000",
            "reason_for_change": "Testing invalid parent rejection",
        }
        bad_resp = await client.post(
            "/api/v1/knowledge/categories",
            json=invalid_parent_payload,
            headers=auth_headers_admin,
        )
        assert bad_resp.status_code == 404

        # 6. Duplicate category creation returns 409 Conflict
        dup_resp = await client.post(
            "/api/v1/knowledge/categories",
            json=root_payload,
            headers=auth_headers_admin,
        )
        assert dup_resp.status_code == 409


@pytest.mark.asyncio
async def test_soft_delete_category(db_session: AsyncSession):
    """
    Validate soft deletion of a category sets is_deleted=True and updates reason_for_change.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    category = await svc.create_category(
        name="Obsolete SOPs",
        slug="obsolete-sops",
        description="Deprecated guidance",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Initial setup",
    )

    assert category.is_deleted is False

    deleted = await svc.delete_category(
        category_id=category.id,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Decommissioning obsolete category",
    )

    assert deleted.is_deleted is True
    assert deleted.reason_for_change == "Decommissioning obsolete category"

    # get_category_by_id should now return None
    lookup = await svc.get_category_by_id(category.id)
    assert lookup is None


@pytest.mark.asyncio
async def test_delete_nonexistent_or_already_deleted_category_raises(
    db_session: AsyncSession,
):
    """
    Validate that deleting a non-existent or already deleted category raises CategoryNotFoundError.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)
    non_existent_id = "00000000-0000-0000-0000-000000000000"

    with pytest.raises(
        CategoryNotFoundError, match="does not exist or has already been deleted"
    ):
        await svc.delete_category(
            category_id=non_existent_id,
            actor_user_id=ACTOR_ADMIN,
            reason_for_change="Attempting to delete non-existent",
        )

    # Create and delete once
    category = await svc.create_category(
        name="Temporary Category",
        slug="temp-category",
        description=None,
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Setup",
    )
    await svc.delete_category(
        category_id=category.id,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="First delete",
    )

    # Deleting again should raise CategoryNotFoundError
    with pytest.raises(
        CategoryNotFoundError, match="does not exist or has already been deleted"
    ):
        await svc.delete_category(
            category_id=category.id,
            actor_user_id=ACTOR_ADMIN,
            reason_for_change="Second delete attempt",
        )


@pytest.mark.asyncio
async def test_list_categories_persona_filtering(db_session: AsyncSession):
    """
    Validate that list_categories filters categories based on the user's persona visibility.

    @req:PRD-SYS-KH-001
    """
    svc = ArticleLifecycleService(db_session)

    # 1. Public category (persona_visibility=None)
    await svc.create_category(
        name="General Platform Help",
        slug="general-help",
        description="Public help",
        persona_visibility=None,
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Public category",
    )

    # 2. CRC category
    await svc.create_category(
        name="Site CRC Guidelines",
        slug="crc-guidelines",
        description="CRC SOPs",
        persona_visibility="site_crc,cra_monitor",
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="CRC category",
    )

    # 3. Data Manager category
    await svc.create_category(
        name="Data Management Guidelines",
        slug="dm-guidelines",
        description="DM SOPs",
        persona_visibility="data_manager",
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="DM category",
    )

    # 4. Admin category
    await svc.create_category(
        name="Admin Secrets",
        slug="admin-secrets",
        description="Admin only",
        persona_visibility="super_admin",
        parent_id=None,
        actor_user_id=ACTOR_ADMIN,
        reason_for_change="Admin category",
    )

    # Admin sees all 4 categories
    admin_cats = await svc.list_categories(user_roles=["super_admin"])
    assert len(admin_cats) == 4

    # CRC user sees General Platform Help and Site CRC Guidelines (2 categories)
    crc_cats = await svc.list_categories(user_roles=["site_crc", "crc"])
    crc_slugs = {c.slug for c in crc_cats}
    assert crc_slugs == {"general-help", "crc-guidelines"}

    # Data Manager sees General Platform Help and Data Management Guidelines (2 categories)
    dm_cats = await svc.list_categories(user_roles=["data_manager"])
    dm_slugs = {c.slug for c in dm_cats}
    assert dm_slugs == {"general-help", "dm-guidelines"}


@pytest.mark.asyncio
async def test_api_list_categories_persona_scoping(
    db_session: AsyncSession,
    auth_headers_admin: dict[str, str],
    auth_headers_crc: dict[str, str],
):
    """
    Validate that GET /categories filters results based on the caller's persona.

    @req:PRD-SYS-KH-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Create categories with different visibility
        await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Global Protocols",
                "slug": "global-protocols",
                "persona_visibility": None,
                "reason_for_change": "Public",
            },
            headers=auth_headers_admin,
        )
        await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Site CRC SOPs",
                "slug": "site-crc-sops",
                "persona_visibility": "site_crc",
                "reason_for_change": "CRC SOPs",
            },
            headers=auth_headers_admin,
        )
        await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "System Audit Logs Guide",
                "slug": "sys-audit-guide",
                "persona_visibility": "super_admin,auditor",
                "reason_for_change": "Admin SOPs",
            },
            headers=auth_headers_admin,
        )

        # Admin lists categories -> receives all 3
        admin_resp = await client.get(
            "/api/v1/knowledge/categories",
            headers=auth_headers_admin,
        )
        assert admin_resp.status_code == 200
        admin_data = admin_resp.json()
        assert len(admin_data) == 3

        # CRC lists categories -> receives 2 (Global Protocols & Site CRC SOPs)
        crc_resp = await client.get(
            "/api/v1/knowledge/categories",
            headers=auth_headers_crc,
        )
        assert crc_resp.status_code == 200
        crc_data = crc_resp.json()
        crc_slugs = {c["slug"] for c in crc_data}
        assert crc_slugs == {"global-protocols", "site-crc-sops"}
        assert "sys-audit-guide" not in crc_slugs


@pytest.mark.asyncio
async def test_api_delete_category_lifecycle(
    db_session: AsyncSession,
    auth_headers_admin: dict[str, str],
    auth_headers_crc: dict[str, str],
):
    """
    Validate DELETE /categories/{id} soft-deletion, RBAC protection, and 404 on missing/deleted.

    @req:PRD-SYS-KH-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Create category
        create_resp = await client.post(
            "/api/v1/knowledge/categories",
            json={
                "name": "Retirement Target",
                "slug": "retirement-target",
                "persona_visibility": None,
                "reason_for_change": "Test category for deletion",
            },
            headers=auth_headers_admin,
        )
        assert create_resp.status_code == 201
        cat_id = create_resp.json()["id"]

        # Non-admin attempting to delete -> 403 Forbidden
        forbidden_resp = await client.delete(
            f"/api/v1/knowledge/categories/{cat_id}",
            headers=auth_headers_crc,
        )
        assert forbidden_resp.status_code == 403

        # Admin deletes category -> 200 OK with is_deleted=True
        delete_resp = await client.delete(
            f"/api/v1/knowledge/categories/{cat_id}?reason_for_change=Retiring+category+per+CR-104",
            headers=auth_headers_admin,
        )
        assert delete_resp.status_code == 200
        del_data = delete_resp.json()
        assert del_data["id"] == cat_id
        assert del_data["is_deleted"] is True
        assert del_data["reason_for_change"] == "Retiring category per CR-104"

        # Subsequent GET by ID -> 404 Not Found
        get_resp = await client.get(
            f"/api/v1/knowledge/categories/{cat_id}",
            headers=auth_headers_admin,
        )
        assert get_resp.status_code == 404

        # Subsequent DELETE -> 404 Not Found
        second_del_resp = await client.delete(
            f"/api/v1/knowledge/categories/{cat_id}",
            headers=auth_headers_admin,
        )
        assert second_del_resp.status_code == 404

