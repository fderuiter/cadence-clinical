import hashlib
import hmac
import json
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
)
from apps.execution.main import app
from apps.execution.migration_rules import reconcile_observations

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
    """Generate Gateway signature-compliant authentication headers."""
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_protocol_capture_and_reconciliation_lifecycle() -> None:
    """Verify that:

    1. Subject consent controls write capability (consent/re-consent precondition).
    2. Captured visits and observations are automatically stamped with subject's active protocol version identity.
    3. Running reconciliation does not mutate historical source observations.
    4. Reconciled data is deterministic and carries source/target provenance (rename, add, remove).
    5. Multi-hop recursive migration path transitions correctly.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a subject
        subject_payload = {
            "subject_id": "SUBJ-M",
            "study_id": "STUDY-M",
            "demographics": {
                "name": "Jane Doe",
                "birthdate": "1994-04-04",
                "gender": "F",
                "race": "Asian",
            },
        }
        res_subj = await client.post(
            "/api/v1/execution/subjects",
            json=subject_payload,
            headers=get_auth_headers(),
        )
        assert res_subj.status_code == 200

        # Record initial protocol version 1.0 consent
        initial_consent_payload = {
            "protocol_version": {
                "study_id": "STUDY-M",
                "version_tag": "1.0",
                "version_index": 1,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        res_consent_1 = await client.post(
            "/api/v1/execution/subjects/SUBJ-M/consent",
            json=initial_consent_payload,
            headers=get_auth_headers(),
        )
        assert res_consent_1.status_code == 200

        # 2. Capture a visit and observation under version 1.0 (should be automatically stamped)
        visit_payload = {
            "subject_id": "SUBJ-M",
            "visit_name": "Screening",
            "study_id": "STUDY-M",
        }
        res_visit = await client.post(
            "/api/v1/execution/visits",
            json=visit_payload,
            headers=get_auth_headers(),
        )
        assert res_visit.status_code == 200
        visit_data = res_visit.json()
        assert visit_data["protocol_version_tag"] == "1.0"
        assert visit_data["protocol_version_index"] == 1

        obs_payload = {
            "subject_id": "SUBJ-M",
            "domain": "VS",
            "test_code": "VSSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 118.0,
            "unit": "mmHg",
        }
        res_obs = await client.post(
            "/api/v1/execution/observations",
            json=obs_payload,
            headers=get_auth_headers(),
        )
        assert res_obs.status_code == 200
        obs_data = res_obs.json()
        assert obs_data["protocol_version_tag"] == "1.0"
        assert obs_data["protocol_version_index"] == 1

        # 3. Register non-destructive Migration Rules:
        # Rule 1 (1.0 -> 2.0): Rename field "VSSBP" to "SYSBP"
        rule_1_payload = {
            "study_id": "STUDY-M",
            "source_version": "1.0",
            "target_version": "2.0",
            "rule_type": "rename",
            "source_field": "VSSBP",
            "target_field": "SYSBP",
        }
        res_rule_1 = await client.post(
            "/api/v1/execution/migration-rules",
            json=rule_1_payload,
            headers=get_auth_headers(roles="data manager"),
        )
        assert res_rule_1.status_code == 201

        # Rule 2 (2.0 -> 3.0): Add field "DIABP" with default value 80.0
        rule_2_payload = {
            "study_id": "STUDY-M",
            "source_version": "2.0",
            "target_version": "3.0",
            "rule_type": "add",
            "target_field": "DIABP",
            "default_value_string": "80.0",
            "default_value_float": 80.0,
        }
        res_rule_2 = await client.post(
            "/api/v1/execution/migration-rules",
            json=rule_2_payload,
            headers=get_auth_headers(roles="data manager"),
        )
        assert res_rule_2.status_code == 201

        # 4. Sign-off consent for version 2.0 and version 3.0 to establish the study's target version
        consent_v2_payload = {
            "protocol_version": {
                "study_id": "STUDY-M",
                "version_tag": "2.0",
                "version_index": 2,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        await client.post(
            "/api/v1/execution/subjects/SUBJ-M/consent",
            json=consent_v2_payload,
            headers=get_auth_headers(),
        )

        consent_v3_payload = {
            "protocol_version": {
                "study_id": "STUDY-M",
                "version_tag": "3.0",
                "version_index": 3,
                "status": "PUBLISHED",
            },
            "icf_signed": True,
            "requires_reconsent": False,
        }
        await client.post(
            "/api/v1/execution/subjects/SUBJ-M/consent",
            json=consent_v3_payload,
            headers=get_auth_headers(),
        )

        # 5. Execute reconciliation and verify on-the-fly multi-hop migration without mutating database
        async with db_manager.get_session_maker()() as session:
            # Query the database to verify initial observation is indeed captured at 1.0 with "VSSBP"
            stmt_query_obs = select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-M"
            )
            res_query = await session.execute(stmt_query_obs)
            db_observations = list(res_query.scalars().all())
            assert len(db_observations) == 1
            assert db_observations[0].test_code == "VSSBP"
            assert db_observations[0].protocol_version_tag == "1.0"

            # Execute the reconcile_observations function for target version "3.0"
            reconciled = await reconcile_observations(session, db_observations, "3.0")

            # We expect 2 reconciled observations:
            # 1. The renamed "SYSBP" (from "VSSBP")
            # 2. The added "DIABP" with default value 80.0
            assert len(reconciled) == 2

            sysbp_obs = next(o for o in reconciled if o.test_code == "SYSBP")
            diabp_obs = next(o for o in reconciled if o.test_code == "DIABP")

            assert sysbp_obs.value == 118.0
            assert sysbp_obs.protocol_version_tag == "3.0"

            # Provenance steps should show the rename and carry-overs
            prov_sysbp = sysbp_obs.provenance
            assert len(prov_sysbp) >= 2
            rename_step = next(
                step for step in prov_sysbp if step["action"] == "rename"
            )
            assert rename_step["source_field"] == "VSSBP"
            assert rename_step["target_field"] == "SYSBP"
            assert rename_step["source_version"] == "1.0"
            assert rename_step["target_version"] == "2.0"

            # Verify the added DIABP has appropriate default values and provenance
            assert diabp_obs.value == 80.0
            assert diabp_obs.protocol_version_tag == "3.0"
            prov_diabp = diabp_obs.provenance
            assert len(prov_diabp) == 1
            add_step = prov_diabp[0]
            assert add_step["action"] == "add"
            assert add_step["target_field"] == "DIABP"
            assert add_step["source_version"] == "2.0"
            assert add_step["target_version"] == "3.0"

            # Ensure the database remains completely pristine and unmutated (non-destructive)
            stmt_pristine = select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-M"
            )
            res_pristine = await session.execute(stmt_pristine)
            pristine_obs = list(res_pristine.scalars().all())
            assert len(pristine_obs) == 1
            assert pristine_obs[0].test_code == "VSSBP"
            assert pristine_obs[0].protocol_version_tag == "1.0"
            assert pristine_obs[0].value == 118.0

        # 6. Verify that SDTM/Dataset-JSON export returns the reconciled data
        res_sdtm = await client.get(
            "/api/v1/execution/biostat/sdtm/vs?study_id=STUDY-M",
            headers=get_auth_headers(roles="data manager"),
        )
        assert res_sdtm.status_code == 200
        sdtm_json = res_sdtm.json()

        # The serialized sdtm dataset-json contains the items we transformed
        item_group_data = sdtm_json.get("clinicalData", {}).get("itemGroupData", {})
        assert "IG.VS" in item_group_data
        item_group = item_group_data["IG.VS"]

        # ItemData should contain the rows
        rows = item_group.get("itemData", [])
        # We should find two rows (SYSBP and DIABP)
        assert len(rows) == 2
        # Let's extract test codes from the rows
        # The fields/variables are mapped in standard sequence
        variables = item_group.get("items", [])
        vstestcd_idx = next(
            i for i, v in enumerate(variables) if v["name"] == "VSTESTCD"
        )

        test_codes_exported = [r[vstestcd_idx] for r in rows]
        assert "SYSBP" in test_codes_exported
        assert "DIABP" in test_codes_exported
