"""
Comprehensive unit and integration tests for the Organization Directory microservice and models.
"""

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
