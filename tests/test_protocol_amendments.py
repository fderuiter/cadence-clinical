import pytest
import httpx
from datetime import datetime
from sqlalchemy import select, text

from apps.execution.main import app
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
    ClinicalVisit,
    ClinicalObservation,
    SubjectConsent,
    MigrationRule
)
from tests.test_clinical_queries import get_v2_auth_headers

@pytest.fixture(autouse=True, scope="module")
async def setup_database():
    from apps.execution.database.migrate import deploy_database_triggers, upgrade_existing_tables

    # Initialize the db_manager with an in-memory SQLite database
    db_manager.init_db("sqlite+aiosqlite:///:memory:")

    # Run migrations on the active engine/connection
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await upgrade_existing_tables(conn)
        await deploy_database_triggers(conn, "sqlite")

    yield
    await db_manager.close()

@pytest.mark.asyncio
async def test_protocol_amendments_end_to_end():
    # Setup test identifiers
    study_id = "STUDY-AMEND-E2E"
    subject_id = "SUBJ-AMEND-E2E"
    site_id = "SITE-AMEND-E2E"

    # 1. Populate initial database state
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Bypass trigger auditing to set up initial test state directly
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )

            # Create subject
            subj = ClinicalSubject(
                subject_id=subject_id,
                study_id=study_id,
                site_id=site_id
            )
            session.add(subj)

            # Create SubjectConsent for version 1
            consent1 = SubjectConsent(
                subject_id=subject_id,
                study_id=study_id,
                version_tag="v1.0",
                version_index=1,
                icf_signed=True,
                icf_signed_date=datetime.utcnow()
            )
            session.add(consent1)

    # 2. Capture clinical visit and observation under active protocol version 1
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create visit under version 1
        visit_resp = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": subject_id,
                "visit_name": "Screening",
                "study_id": study_id,
            },
            headers=get_v2_auth_headers(roles="CRA")
        )
        assert visit_resp.status_code == 200
        v_data = visit_resp.json()
        assert v_data["protocol_version_tag"] == "v1.0"
        assert v_data["protocol_version_index"] == 1
        visit_id = v_data["id"]

        # Create observation under version 1
        obs_resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": subject_id,
                "study_id": study_id,
                "visit_id": visit_id,
                "domain": "VS",
                "test_code": "SYSBP",
                "test_name": "Systolic Blood Pressure",
                "value": 120.0,
                "value_string": "120"
            },
            headers=get_v2_auth_headers(roles="CRA")
        )
        assert obs_resp.status_code == 200
        o_data = obs_resp.json()
        assert o_data["protocol_version_tag"] == "v1.0"
        assert o_data["protocol_version_index"] == 1
        obs1_id = o_data["id"]

    # 3. Subject signs ICF/consents to a newer version index 2 ("v2.0")
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            consent2 = SubjectConsent(
                subject_id=subject_id,
                study_id=study_id,
                version_tag="v2.0",
                version_index=2,
                icf_signed=True,
                icf_signed_date=datetime.utcnow()
            )
            session.add(consent2)

    # Capture clinical visit and observation under active protocol version 2
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create visit under version 2
        visit_resp2 = await client.post(
            "/api/v1/execution/visits",
            json={
                "subject_id": subject_id,
                "visit_name": "Week 4",
                "study_id": study_id,
            },
            headers=get_v2_auth_headers(roles="CRA")
        )
        assert visit_resp2.status_code == 200
        v_data2 = visit_resp2.json()
        assert v_data2["protocol_version_tag"] == "v2.0"
        assert v_data2["protocol_version_index"] == 2
        visit_id2 = v_data2["id"]

        # Create observation under version 2. The field has been renamed to SYSBP_V2 in the new protocol!
        obs_resp2 = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": subject_id,
                "study_id": study_id,
                "visit_id": visit_id2,
                "domain": "VS",
                "test_code": "SYSBP_V2",
                "test_name": "Systolic Blood Pressure v2",
                "value": 115.0,
                "value_string": "115"
            },
            headers=get_v2_auth_headers(roles="CRA")
        )
        assert obs_resp2.status_code == 200
        o_data2 = obs_resp2.json()
        assert o_data2["protocol_version_tag"] == "v2.0"
        assert o_data2["protocol_version_index"] == 2
        obs2_id = o_data2["id"]

    # 4. Register Migration Rules representing the Designer form diff
    # Version 1 -> Version 2: SYSBP is renamed to SYSBP_V2
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        rule_resp = await client.post(
            "/api/v1/execution/migration-rules",
            json={
                "study_id": study_id,
                "source_version_index": 1,
                "target_version_index": 2,
                "rules": {
                    "renamed_fields": {
                        "SYSBP": "SYSBP_V2"
                    },
                    "removed_fields": [],
                    "added_fields": ["SYSBP_V2"]
                }
            },
            headers=get_v2_auth_headers(roles="Data Manager")
        )
        assert rule_resp.status_code == 201
        r_data = rule_resp.json()
        assert r_data["source_version_index"] == 1
        assert r_data["target_version_index"] == 2
        assert r_data["rules"]["renamed_fields"]["SYSBP"] == "SYSBP_V2"

    # 5. Fetch reconciled observations for the subject up to target version 2
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        recon_resp = await client.get(
            f"/api/v1/execution/subjects/{subject_id}/observations?target_version_index=2",
            headers=get_v2_auth_headers(roles="CRA")
        )
        assert recon_resp.status_code == 200
        recon_data = recon_resp.json()
        assert len(recon_data) == 2

        obs1_recon = next(o for o in recon_data if o["id"] == obs1_id)
        obs2_recon = next(o for o in recon_data if o["id"] == obs2_id)

        # Verification of obs1_recon (original code was SYSBP, captured under v1.0)
        assert obs1_recon["test_code"] == "SYSBP_V2"  # Mapped to target version code!
        assert obs1_recon["protocol_version_index"] == 1  # Retains historical capture-time protocol index
        assert obs1_recon["protocol_version_tag"] == "v1.0"  # Retains historical capture-time protocol tag
        assert obs1_recon["provenance"]["action"] == "RENAMED"
        assert obs1_recon["provenance"]["original_test_code"] == "SYSBP"
        assert obs1_recon["provenance"]["target_protocol_version_index"] == 2

        # Verification of obs2_recon (original code was SYSBP_V2, captured under v2.0)
        assert obs2_recon["test_code"] == "SYSBP_V2"
        assert obs2_recon["protocol_version_index"] == 2
        assert obs2_recon["provenance"]["action"] == "ORIGINAL"

    # 6. Verify that source database rows are completely untouched (immutability)
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalObservation).where(ClinicalObservation.id == obs1_id)
        res = await session.execute(stmt)
        obs1_db = res.scalar_one()
        # Source row in DB retains original test_code completely unmodified!
        assert obs1_db.test_code == "SYSBP"

    # 7. Multi-hop Transitive Reconciliation Test (v1 -> v2, v2 -> v3)
    # Register v2 -> v3 rule: SYSBP_V2 is renamed to SYSBP_V3
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        rule_resp2 = await client.post(
            "/api/v1/execution/migration-rules",
            json={
                "study_id": study_id,
                "source_version_index": 2,
                "target_version_index": 3,
                "rules": {
                    "renamed_fields": {
                        "SYSBP_V2": "SYSBP_V3"
                    },
                    "removed_fields": [],
                    "added_fields": []
                }
            },
            headers=get_v2_auth_headers(roles="Data Manager")
        )
        assert rule_resp2.status_code == 201

        # Retrieve reconciled up to version 3
        recon_resp3 = await client.get(
            f"/api/v1/execution/subjects/{subject_id}/observations?target_version_index=3",
            headers=get_v2_auth_headers(roles="CRA")
        )
        assert recon_resp3.status_code == 200
        recon_data3 = recon_resp3.json()
        assert len(recon_data3) == 2

        obs1_recon3 = next(o for o in recon_data3 if o["id"] == obs1_id)
        obs2_recon3 = next(o for o in recon_data3 if o["id"] == obs2_id)

        # Transitive trace path verification for observation 1 (SYSBP -> SYSBP_V2 -> SYSBP_V3)
        assert obs1_recon3["test_code"] == "SYSBP_V3"
        assert obs1_recon3["provenance"]["action"] == "RENAMED"
        assert obs1_recon3["provenance"]["original_test_code"] == "SYSBP"
        assert obs1_recon3["provenance"]["target_protocol_version_index"] == 3
        # Should have trace steps!
        assert len(obs1_recon3["provenance"]["steps"]) == 2
        assert "SYSBP -> SYSBP_V2" in obs1_recon3["provenance"]["steps"][0]
        assert "SYSBP_V2 -> SYSBP_V3" in obs1_recon3["provenance"]["steps"][1]

        # Verification of observation 2 (SYSBP_V2 -> SYSBP_V3)
        assert obs2_recon3["test_code"] == "SYSBP_V3"
        assert obs2_recon3["provenance"]["action"] == "RENAMED"
        assert obs2_recon3["provenance"]["original_test_code"] == "SYSBP_V2"
