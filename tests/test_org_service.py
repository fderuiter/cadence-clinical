"""
Comprehensive unit and integration tests for the Organization Directory microservice and models.
"""

import time
from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import select
from sqlalchemy.orm import selectinload

from apps.org.database import db_manager
from apps.org.main import app
from apps.org.models import (
    Base,
    DelegationOfAuthority,
    Organization,
    OrgAuditLog,
    Personnel,
    Site,
    SiteStaff,
)
from packages.security.signing import generate_gateway_signature


@pytest.fixture(name="db_session_fixture")
async def db_session_fixture():
    """
    Initializes a test in-memory SQLite database, creates all tables,
    and yields an active database session.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        yield session

    await db_manager.close()


def test_health_endpoint() -> None:
    """
    Verify that the /health endpoint is available and returns 200 OK.
    """
    with TestClient(app) as client:
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok", "service": "org"}


@pytest.mark.asyncio
async def test_organization_and_site_relationship(db_session_fixture) -> None:
    """
    Verify that an Organization can be created with associated Sites,
    and that back-references operate correctly.
    """
    session = db_session_fixture

    # 1. Create Organization
    org = Organization(
        name="Apex Clinical Inc.",
        org_type="CRO",
        created_by="admin_user",
        reason_for_change="Initial registration of CRO",
    )
    session.add(org)
    await session.flush()

    # 2. Create Site associated with the Organization
    site = Site(
        site_id="site_apex_01",
        name="Apex Boston Research Center",
        organization_id=org.id,
        study_id="study_apex_trial_A",
        created_by="admin_user",
        reason_for_change="Adding primary site",
    )
    session.add(site)
    await session.flush()

    # 3. Refresh and assert using selectinload for async relationships
    stmt = (
        select(Organization)
        .where(Organization.id == org.id)
        .options(selectinload(Organization.sites))
    )
    result = await session.execute(stmt)
    saved_org = result.scalars().first()

    assert saved_org is not None
    assert saved_org.name == "Apex Clinical Inc."
    assert len(saved_org.sites) == 1
    assert saved_org.sites[0].site_id == "site_apex_01"
    assert saved_org.sites[0].name == "Apex Boston Research Center"
    assert saved_org.sites[0].study_id == "study_apex_trial_A"
    assert saved_org.sites[0].organization_id == org.id


@pytest.mark.asyncio
async def test_personnel_and_sitestaff_alias(db_session_fixture) -> None:
    """
    Verify that Personnel and SiteStaff represent the same class,
    can be instantiated successfully, and mapped to organization/study/site scoping.
    """
    session = db_session_fixture

    # Verify alias identity
    assert SiteStaff is Personnel

    # 1. Create Organization and Site
    org = Organization(
        name="Global Trial Sponsor Ltd.",
        org_type="sponsor",
        created_by="sponsor_admin",
        reason_for_change="Sponsor enrollment",
    )
    session.add(org)
    await session.flush()

    site = Site(
        site_id="site_100",
        name="St. Jude Research Hospital",
        organization_id=org.id,
        study_id="STJ-2026",
        created_by="sponsor_admin",
        reason_for_change="St Jude Site Setup",
    )
    session.add(site)
    await session.flush()

    # 2. Create Personnel linked to both organization and site
    person = Personnel(
        keycloak_user_id="kc-user-uuid-999",
        first_name="Jane",
        last_name="Doe",
        email="jane.doe@stjude.org",
        role="Principal Investigator",
        organization_id=org.id,
        site_id=site.site_id,
        study_id="STJ-2026",
        created_by="sponsor_admin",
        reason_for_change="Hiring primary PI",
    )
    session.add(person)
    await session.flush()

    # 3. Query and assert relationships using selectinload
    stmt = (
        select(Personnel)
        .where(Personnel.id == person.id)
        .options(selectinload(Personnel.organization), selectinload(Personnel.site))
    )
    result = await session.execute(stmt)
    saved_person = result.scalars().first()

    assert saved_person is not None
    assert saved_person.keycloak_user_id == "kc-user-uuid-999"
    assert saved_person.role == "Principal Investigator"
    assert saved_person.organization is not None
    assert saved_person.organization.name == "Global Trial Sponsor Ltd."
    assert saved_person.site is not None
    assert saved_person.site.name == "St. Jude Research Hospital"
    assert saved_person.site_id == "site_100"
    assert saved_person.study_id == "STJ-2026"


@pytest.mark.asyncio
async def test_delegation_of_authority_flow(db_session_fixture) -> None:
    """
    Verify that a Delegation of Authority (DOA) record can be successfully created
    detailing delegator, delegatee, target site, study, and specific duties.
    """
    session = db_session_fixture

    # 1. Create Organization and Site
    org = Organization(
        name="Investigator Org",
        org_type="site",
        created_by="sys_admin",
        reason_for_change="Registration of clinical trial site organization",
    )
    session.add(org)
    await session.flush()

    site = Site(
        site_id="site_DOA_002",
        name="Center of Excellence for Oncology",
        organization_id=org.id,
        study_id="ONC-2026",
        created_by="sys_admin",
        reason_for_change="Oncology site setup",
    )
    session.add(site)
    await session.flush()

    # 2. Create Personnel (Delegator & Delegatee)
    delegator = Personnel(
        first_name="Arthur",
        last_name="Pendragon",
        email="arthur@oncology.org",
        role="Principal Investigator",
        organization_id=org.id,
        site_id=site.site_id,
        study_id="ONC-2026",
        created_by="sys_admin",
        reason_for_change="PI onboarding",
    )
    delegatee = Personnel(
        first_name="Gwen",
        last_name="Guinevere",
        email="gwen@oncology.org",
        role="CRC",
        organization_id=org.id,
        site_id=site.site_id,
        study_id="ONC-2026",
        created_by="sys_admin",
        reason_for_change="CRC onboarding",
    )
    session.add_all([delegator, delegatee])
    await session.flush()

    # 3. Create Delegation of Authority
    duties_list = [
        "Informed Consent Process",
        "CRF Completion & Data Entry",
        "Clinical Query & Discrepancy Resolution",
    ]
    now = datetime.now(timezone.utc).replace(tzinfo=None)

    doa = DelegationOfAuthority(
        delegator_id=delegator.id,
        delegatee_id=delegatee.id,
        site_id=site.site_id,
        study_id="ONC-2026",
        duties=duties_list,
        start_date=now,
        end_date=now + timedelta(days=365),
        is_active=True,
        created_by="arthur@oncology.org",
        reason_for_change="Delegating clinical research duties to CRC Gwen",
    )
    session.add(doa)
    await session.flush()

    # 4. Assert and query using selectinload
    stmt = (
        select(DelegationOfAuthority)
        .where(DelegationOfAuthority.id == doa.id)
        .options(
            selectinload(DelegationOfAuthority.delegator),
            selectinload(DelegationOfAuthority.delegatee),
            selectinload(DelegationOfAuthority.site),
        )
    )
    result = await session.execute(stmt)
    saved_doa = result.scalars().first()

    assert saved_doa is not None
    assert saved_doa.delegator.first_name == "Arthur"
    assert saved_doa.delegatee.first_name == "Gwen"
    assert saved_doa.site_id == "site_DOA_002"
    assert saved_doa.study_id == "ONC-2026"
    assert saved_doa.duties == duties_list
    assert saved_doa.is_active is True
    assert saved_doa.site is not None
    assert saved_doa.site.name == "Center of Excellence for Oncology"


@pytest.mark.asyncio
async def test_org_audit_log_append_only(db_session_fixture) -> None:
    """
    Verify that the OrgAuditLog can be written and retains correct Part 11 auditing attributes.
    """
    session = db_session_fixture

    # 1. Write an audit log entry
    log = OrgAuditLog(
        actor_id="user_cra_007",
        actor_role="CRA/Monitor",
        action="UPDATE_SITE_CONFIG",
        record_id="site_100",
        details="Updated principal investigator details for site 100",
        reason_for_change="CRA site validation check",
    )
    session.add(log)
    await session.flush()

    # 2. Query and assert
    stmt = select(OrgAuditLog).where(OrgAuditLog.id == log.id)
    result = await session.execute(stmt)
    saved_log = result.scalars().first()

    assert saved_log is not None
    assert saved_log.actor_id == "user_cra_007"
    assert saved_log.actor_role == "CRA/Monitor"
    assert saved_log.action == "UPDATE_SITE_CONFIG"
    assert saved_log.record_id == "site_100"
    assert saved_log.reason_for_change == "CRA site validation check"
    assert isinstance(saved_log.timestamp, datetime)


def get_auth_headers(
    user_id: str, roles: str, change_reason: str = "Standard Access"
) -> dict:
    """Helper to generate signed gateway authentication headers."""
    timestamp = str(time.time())
    secret = b"internal-gateway-secret-12345"
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


def test_organization_crud_api(db_session_fixture) -> None:
    """
    Test the complete GxP-compliant lifecycle of Organization via the REST API,
    verifying creation, list/filtering, retrieval, soft-updating, and version history.
    """
    with TestClient(app) as client:
        headers = get_auth_headers(
            "admin_user_001", "admin", "Creating new CRO organization"
        )
        payload = {
            "name": "Global Research CRO",
            "org_type": "CRO",
            "reason_for_change": "Initial CRO registration",
        }

        # 1. Create Organization
        create_resp = client.post(
            "/api/v1/org/organizations", json=payload, headers=headers
        )
        assert create_resp.status_code == 201
        org_data = create_resp.json()
        assert org_data["name"] == "Global Research CRO"
        assert org_data["org_type"] == "CRO"
        assert org_data["version_index"] == 1
        org_id = org_data["id"]

        # 2. Retrieve Organization (Latest)
        get_headers = get_auth_headers("viewer_001", "viewer")
        get_resp = client.get(
            f"/api/v1/org/organizations/{org_id}", headers=get_headers
        )
        assert get_resp.status_code == 200
        assert get_resp.json()["name"] == "Global Research CRO"
        assert get_resp.json()["version_index"] == 1

        # 3. Soft-Update Organization
        update_headers = get_auth_headers(
            "admin_user_001", "admin", "Rename organization as CRO expanded"
        )
        update_payload = {
            "name": "Global Research CRO Expanded",
            "org_type": "CRO",
            "reason_for_change": "Updating CRO name to reflect expansion",
        }
        update_resp = client.put(
            f"/api/v1/org/organizations/{org_id}",
            json=update_payload,
            headers=update_headers,
        )
        assert update_resp.status_code == 200
        updated_data = update_resp.json()
        assert updated_data["id"] == org_id
        assert updated_data["name"] == "Global Research CRO Expanded"
        assert updated_data["version_index"] == 2

        # 4. List and Filter Organizations (returns latest version)
        list_resp = client.get("/api/v1/org/organizations", headers=get_headers)
        assert list_resp.status_code == 200
        items = list_resp.json()
        assert len(items) >= 1
        match = [i for i in items if i["id"] == org_id][0]
        assert match["name"] == "Global Research CRO Expanded"
        assert match["version_index"] == 2

        # Filter by name
        filtered_resp = client.get(
            "/api/v1/org/organizations?name=Expanded", headers=get_headers
        )
        assert filtered_resp.status_code == 200
        assert len(filtered_resp.json()) == 1
        assert filtered_resp.json()[0]["id"] == org_id

        # Filter by type
        type_filtered_resp = client.get(
            "/api/v1/org/organizations?org_type=CRO", headers=get_headers
        )
        assert type_filtered_resp.status_code == 200
        assert org_id in [i["id"] for i in type_filtered_resp.json()]

        # 5. Retrieve Specific Version (version 1)
        v1_resp = client.get(
            f"/api/v1/org/organizations/{org_id}?version_index=1", headers=get_headers
        )
        assert v1_resp.status_code == 200
        assert v1_resp.json()["name"] == "Global Research CRO"
        assert v1_resp.json()["version_index"] == 1

        # 6. Retrieve Chronological History
        history_resp = client.get(
            f"/api/v1/org/organizations/{org_id}/history", headers=get_headers
        )
        assert history_resp.status_code == 200
        history_data = history_resp.json()
        assert len(history_data) == 2
        assert history_data[0]["version_index"] == 2
        assert history_data[1]["version_index"] == 1


def test_site_crud_api(db_session_fixture) -> None:
    """
    Test the complete GxP-compliant lifecycle of Site via the REST API,
    verifying creation, list/filtering, retrieval, soft-updating, and version history.
    """
    with TestClient(app) as client:
        # Create an org first
        org_headers = get_auth_headers("admin_user_001", "admin")
        org_resp = client.post(
            "/api/v1/org/organizations",
            json={
                "name": "Site Org",
                "org_type": "site",
                "reason_for_change": "Initial site org setup",
            },
            headers=org_headers,
        )
        org_id = org_resp.json()["id"]

        # 1. Create Site
        site_headers = get_auth_headers("admin_user_001", "admin", "Adding prime site")
        payload = {
            "site_id": "ST_001",
            "name": "St. Jude Hospital",
            "organization_id": org_id,
            "study_id": "STUDY_ST_JUDE",
            "reason_for_change": "Initial Site registration",
        }
        create_resp = client.post(
            "/api/v1/org/sites", json=payload, headers=site_headers
        )
        assert create_resp.status_code == 201
        site_data = create_resp.json()
        assert site_data["site_id"] == "ST_001"
        assert site_data["name"] == "St. Jude Hospital"
        assert site_data["version_index"] == 1
        site_uuid = site_data["id"]

        # 2. Retrieve Site (Latest)
        get_headers = get_auth_headers("viewer_001", "viewer")
        get_resp = client.get(f"/api/v1/org/sites/{site_uuid}", headers=get_headers)
        assert get_resp.status_code == 200
        assert get_resp.json()["name"] == "St. Jude Hospital"

        # 3. Soft-Update Site
        update_headers = get_auth_headers(
            "admin_user_001", "admin", "Update site name to include medical center"
        )
        update_payload = {
            "name": "St. Jude Medical Center",
            "reason_for_change": "Renaming site",
        }
        update_resp = client.put(
            f"/api/v1/org/sites/{site_uuid}",
            json=update_payload,
            headers=update_headers,
        )
        assert update_resp.status_code == 200
        assert update_resp.json()["name"] == "St. Jude Medical Center"
        assert update_resp.json()["version_index"] == 2

        # 4. List and Filter Sites (returns latest version)
        list_resp = client.get("/api/v1/org/sites", headers=get_headers)
        assert list_resp.status_code == 200
        assert len(list_resp.json()) >= 1
        match = [s for s in list_resp.json() if s["id"] == site_uuid][0]
        assert match["name"] == "St. Jude Medical Center"

        # Filter by site_id
        filtered_resp = client.get(
            "/api/v1/org/sites?site_id=ST_001", headers=get_headers
        )
        assert len(filtered_resp.json()) == 1

        # Filter by study_id
        study_filtered_resp = client.get(
            "/api/v1/org/sites?study_id=STUDY_ST_JUDE", headers=get_headers
        )
        assert len(study_filtered_resp.json()) == 1

        # 5. Retrieve Specific Version (version 1)
        v1_resp = client.get(
            f"/api/v1/org/sites/{site_uuid}?version_index=1", headers=get_headers
        )
        assert v1_resp.status_code == 200
        assert v1_resp.json()["name"] == "St. Jude Hospital"
        assert v1_resp.json()["version_index"] == 1

        # 6. Retrieve Chronological History
        history_resp = client.get(
            f"/api/v1/org/sites/{site_uuid}/history", headers=get_headers
        )
        assert history_resp.status_code == 200
        history_data = history_resp.json()
        assert len(history_data) == 2
        assert history_data[0]["version_index"] == 2


def test_personnel_crud_api(db_session_fixture) -> None:
    """
    Test the complete GxP-compliant lifecycle of Personnel via the REST API,
    verifying creation, list/filtering, retrieval, soft-updating, and version history.
    """
    with TestClient(app) as client:
        # Create Organization
        org_headers = get_auth_headers("admin_user_001", "admin")
        org_resp = client.post(
            "/api/v1/org/organizations",
            json={
                "name": "Med Lab",
                "org_type": "central laboratory",
                "reason_for_change": "Initial Lab setup",
            },
            headers=org_headers,
        )
        org_id = org_resp.json()["id"]

        # 1. Create Personnel
        p_headers = get_auth_headers("admin_user_001", "admin", "Hiring lab technician")
        payload = {
            "keycloak_user_id": "kc-user-1234",
            "first_name": "Arthur",
            "last_name": "Pendragon",
            "email": "arthur@medlab.org",
            "role": "CRC",
            "organization_id": org_id,
            "site_id": "site_100",
            "study_id": "study_555",
            "reason_for_change": "Onboarding Arthur",
        }
        create_resp = client.post(
            "/api/v1/org/personnel", json=payload, headers=p_headers
        )
        assert create_resp.status_code == 201
        person_data = create_resp.json()
        assert person_data["first_name"] == "Arthur"
        assert person_data["role"] == "CRC"
        assert person_data["version_index"] == 1
        person_uuid = person_data["id"]

        # 2. Retrieve Personnel (Latest)
        get_headers = get_auth_headers("viewer_001", "viewer")
        get_resp = client.get(
            f"/api/v1/org/personnel/{person_uuid}", headers=get_headers
        )
        assert get_resp.status_code == 200
        assert get_resp.json()["first_name"] == "Arthur"

        # 3. Soft-Update Personnel
        update_headers = get_auth_headers(
            "admin_user_001", "admin", "Promote Arthur to investigator"
        )
        update_payload = {
            "first_name": "Arthur",
            "last_name": "Pendragon",
            "email": "arthur@medlab.org",
            "role": "Principal Investigator",
            "reason_for_change": "Promotion",
        }
        update_resp = client.put(
            f"/api/v1/org/personnel/{person_uuid}",
            json=update_payload,
            headers=update_headers,
        )
        assert update_resp.status_code == 200
        assert update_resp.json()["role"] == "Principal Investigator"
        assert update_resp.json()["version_index"] == 2

        # 4. List and Filter Personnel (returns latest version)
        list_resp = client.get("/api/v1/org/personnel", headers=get_headers)
        assert list_resp.status_code == 200
        assert len(list_resp.json()) >= 1
        match = [p for p in list_resp.json() if p["id"] == person_uuid][0]
        assert match["role"] == "Principal Investigator"

        # Filter by site_id
        filtered_resp = client.get(
            "/api/v1/org/personnel?site_id=site_100", headers=get_headers
        )
        assert len(filtered_resp.json()) == 1

        # Filter by exact role
        role_filtered_resp = client.get(
            "/api/v1/org/personnel?role=Principal Investigator", headers=get_headers
        )
        assert len(role_filtered_resp.json()) == 1

        # Filter by partial email
        email_filtered_resp = client.get(
            "/api/v1/org/personnel?email=arthur", headers=get_headers
        )
        assert len(email_filtered_resp.json()) == 1

        # 5. Retrieve Specific Version (version 1)
        v1_resp = client.get(
            f"/api/v1/org/personnel/{person_uuid}?version_index=1", headers=get_headers
        )
        assert v1_resp.status_code == 200
        assert v1_resp.json()["role"] == "CRC"
        assert v1_resp.json()["version_index"] == 1

        # 6. Retrieve Chronological History
        history_resp = client.get(
            f"/api/v1/org/personnel/{person_uuid}/history", headers=get_headers
        )
        assert history_resp.status_code == 200
        history_data = history_resp.json()
        assert len(history_data) == 2
        assert history_data[0]["version_index"] == 2


@pytest.mark.asyncio
async def test_gxp_audit_logging_and_actor_context(db_session_fixture) -> None:
    """
    Verify that REST API actions correctly register in the append-only OrgAuditLog database,
    recording accurate actor context (actor_id, actor_role), action codes, target record IDs,
    and change justification reasons.
    """
    with TestClient(app) as client:
        headers = get_auth_headers(
            "audit_user_cra", "CRA/Monitor", "System setup auditing test"
        )
        payload = {
            "name": "Audit Test Organization",
            "org_type": "CRO",
            "reason_for_change": "Setup test CRO",
        }

        # Create organization
        resp = client.post("/api/v1/org/organizations", json=payload, headers=headers)
        assert resp.status_code == 201
        org_id = resp.json()["id"]

        # View organization
        get_headers = get_auth_headers(
            "auditor_001", "auditor", "Auditor inspecting CRO details"
        )
        client.get(f"/api/v1/org/organizations/{org_id}", headers=get_headers)

        # Retrieve audit logs via API
        audit_headers = get_auth_headers("auditor_001", "auditor")
        audit_resp = client.get("/api/v1/org/audit-logs", headers=audit_headers)
        assert audit_resp.status_code == 200
        logs = audit_resp.json()

        # The latest log should be the ORGANIZATION_VIEW
        assert len(logs) >= 2
        view_log = logs[0]
        create_log = logs[1]

        # Verify VIEW log
        assert view_log["actor_id"] == "auditor_001"
        assert view_log["actor_role"] == "auditor"
        assert view_log["action"] == "ORGANIZATION_VIEW"
        assert view_log["record_id"] == org_id
        assert "Viewed organization" in view_log["details"]

        # Verify CREATE log
        assert create_log["actor_id"] == "audit_user_cra"
        assert create_log["actor_role"] == "CRA/Monitor"
        assert create_log["action"] == "ORGANIZATION_CREATE"
        assert create_log["record_id"] == org_id
        assert create_log["reason_for_change"] == "System setup auditing test"


# ==========================================
# External Monitor & Personnel Assignments Tests
# ==========================================


@pytest.mark.asyncio
async def test_cro_affiliation_validation(db_session_fixture) -> None:
    """Verify CRO affiliation validation on Personnel creation and Assignment creation."""
    with TestClient(app) as client:
        # Create non-CRO org (e.g., site)
        org_headers = get_auth_headers("admin_user_001", "admin")
        non_cro_resp = client.post(
            "/api/v1/org/organizations",
            json={
                "name": "Site Org",
                "org_type": "site",
                "reason_for_change": "Initial site org setup",
            },
            headers=org_headers,
        )
        non_cro_id = non_cro_resp.json()["id"]

        # Try to create External Monitor with non-CRO organization_id -> Should fail
        p_headers = get_auth_headers(
            "admin_user_001", "admin", "Creating external monitor"
        )
        payload_fail = {
            "keycloak_user_id": "kc-user-mon-fail",
            "first_name": "John",
            "last_name": "Doe",
            "email": "john.doe@cro.org",
            "role": "External Monitor",
            "organization_id": non_cro_id,
            "reason_for_change": "Onboarding external monitor fail",
        }
        fail_resp = client.post(
            "/api/v1/org/personnel", json=payload_fail, headers=p_headers
        )
        assert fail_resp.status_code == 400
        assert "CRO organization" in fail_resp.json()["detail"]

        # Create valid CRO org
        cro_resp = client.post(
            "/api/v1/org/organizations",
            json={
                "name": "CRO Org",
                "org_type": "CRO",
                "reason_for_change": "Initial CRO org setup",
            },
            headers=org_headers,
        )
        cro_id = cro_resp.json()["id"]

        # Create External Monitor with valid CRO organization_id -> Should succeed
        payload_success = {
            "keycloak_user_id": "kc-user-mon-success",
            "first_name": "Jane",
            "last_name": "Smith",
            "email": "jane.smith@cro.org",
            "role": "External Monitor",
            "organization_id": cro_id,
            "reason_for_change": "Onboarding external monitor success",
        }
        success_resp = client.post(
            "/api/v1/org/personnel", json=payload_success, headers=p_headers
        )
        assert success_resp.status_code == 201


@pytest.mark.asyncio
async def test_personnel_assignments_crud(db_session_fixture) -> None:
    """Verify that multiple Personnel assignments can be created, updated, and retrieved with version history."""
    with TestClient(app) as client:
        # Onboard personnel first (e.g. CRC)
        org_headers = get_auth_headers("admin_user_001", "admin")
        org_resp = client.post(
            "/api/v1/org/organizations",
            json={
                "name": "Sponsor Org",
                "org_type": "sponsor",
                "reason_for_change": "Initial sponsor setup",
            },
            headers=org_headers,
        )
        org_id = org_resp.json()["id"]

        p_headers = get_auth_headers("admin_user_001", "admin")
        p_resp = client.post(
            "/api/v1/org/personnel",
            json={
                "first_name": "CRC_Jane",
                "last_name": "Doe",
                "email": "crc_jane@sponsor.com",
                "role": "CRC",
                "organization_id": org_id,
                "reason_for_change": "Onboarding Jane",
            },
            headers=p_headers,
        )
        personnel_id = p_resp.json()["id"]

        # Create assignment 1 (site_A, study_1)
        assign_headers = get_auth_headers("admin_user_001", "admin", "Adding site A")
        create_payload = {
            "site_id": "site_A",
            "study_id": "study_1",
            "is_active": True,
            "reason_for_change": "Assigning to site A and study 1",
        }
        create_resp = client.post(
            f"/api/v1/org/personnel/{personnel_id}/assignments",
            json=create_payload,
            headers=assign_headers,
        )
        assert create_resp.status_code == 201
        assign_data = create_resp.json()
        assert assign_data["site_id"] == "site_A"
        assert assign_data["study_id"] == "study_1"
        assert assign_data["is_active"] is True
        assign_id = assign_data["id"]

        # Create assignment 2 (site_B, study_1)
        create_resp2 = client.post(
            f"/api/v1/org/personnel/{personnel_id}/assignments",
            json={
                "site_id": "site_B",
                "study_id": "study_1",
                "is_active": True,
                "reason_for_change": "Assigning to site B and study 1",
            },
            headers=assign_headers,
        )
        assert create_resp2.status_code == 201

        # List assignments for jane
        list_resp = client.get(
            f"/api/v1/org/personnel/{personnel_id}/assignments",
            headers=get_auth_headers("viewer_001", "viewer"),
        )
        assert list_resp.status_code == 200
        assert len(list_resp.json()) == 2

        # Update assignment 1 to be inactive
        update_resp = client.put(
            f"/api/v1/org/personnel/assignments/{assign_id}",
            json={
                "is_active": False,
                "reason_for_change": "Removing assignment from site A",
            },
            headers=get_auth_headers("admin_user_001", "admin"),
        )
        assert update_resp.status_code == 200
        assert update_resp.json()["is_active"] is False
        assert update_resp.json()["version_index"] == 2

        # History of assignment 1
        history_resp = client.get(
            f"/api/v1/org/personnel/assignments/{assign_id}/history",
            headers=get_auth_headers("viewer_001", "viewer"),
        )
        assert history_resp.status_code == 200
        assert len(history_resp.json()) == 2
        assert history_resp.json()[0]["version_index"] == 2
        assert history_resp.json()[1]["version_index"] == 1


@pytest.mark.asyncio
async def test_resolve_assignments_endpoint(db_session_fixture) -> None:
    """Verify that resolution endpoint accurately resolves active sites/studies by keycloak_user_id."""
    with TestClient(app) as client:
        # Create CRO and External Monitor
        org_headers = get_auth_headers("admin_user_001", "admin")
        cro_resp = client.post(
            "/api/v1/org/organizations",
            json={
                "name": "CRO Team",
                "org_type": "CRO",
                "reason_for_change": "CRO setup",
            },
            headers=org_headers,
        )
        cro_id = cro_resp.json()["id"]

        p_resp = client.post(
            "/api/v1/org/personnel",
            json={
                "keycloak_user_id": "kc-ext-monitor-1",
                "first_name": "Steve",
                "last_name": "Monitor",
                "email": "steve@cro.com",
                "role": "External Monitor",
                "organization_id": cro_id,
                "reason_for_change": "Onboarding Steve",
            },
            headers=org_headers,
        )
        personnel_id = p_resp.json()["id"]

        # Create two assignments
        client.post(
            f"/api/v1/org/personnel/{personnel_id}/assignments",
            json={
                "site_id": "site_alpha",
                "study_id": "study_x",
                "is_active": True,
                "reason_for_change": " Steve study x site alpha",
            },
            headers=org_headers,
        )
        client.post(
            f"/api/v1/org/personnel/{personnel_id}/assignments",
            json={
                "site_id": "site_beta",
                "study_id": "study_y",
                "is_active": True,
                "reason_for_change": "Steve study y site beta",
            },
            headers=org_headers,
        )

        # Call resolve endpoint
        resolve_resp = client.get(
            "/api/v1/org/assignments/resolve?keycloak_user_id=kc-ext-monitor-1",
            headers=get_auth_headers("admin_user_001", "admin"),
        )
        assert resolve_resp.status_code == 200
        data = resolve_resp.json()
        assert data["personnel_id"] == personnel_id
        assert data["roles"] == ["external_monitor"]
        assert sorted(data["assigned_sites"]) == ["site_alpha", "site_beta"]
        assert sorted(data["assigned_studies"]) == ["study_x", "study_y"]
