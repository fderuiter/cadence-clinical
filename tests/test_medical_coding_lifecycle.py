import hashlib
import hmac
import json
import time

import httpx
import pytest
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalCodingLedger,
    ClinicalSubject,
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugRecord,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id: str = "coder_bob",
    roles: str = "Data Manager",
    change_reason: str = "Clinical coding review",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
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


@pytest.fixture(autouse=True)
async def setup_test_db():
    TrialLockManager.reset()
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    await db_manager.close()


async def seed_dictionaries():
    """Seed dictionaries in the database for version 26.0 of MedDRA and 2024-03 of WHODrug."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # 1. MedDRA Headache
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="10019211",
                    term_name="Headache",
                    level="LLT",
                )
            )
            # MedDRA Migraine
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="10029300",
                    term_name="Migraine",
                    level="LLT",
                )
            )
            # MedDRA Hierarchy
            session.add(
                MedDRAHierarchy(
                    dictionary_version="26.0",
                    llt_code="10019211",
                    pt_code="10019211",
                    hlt_code="10019231",
                    hlgt_code="10029214",
                    soc_code="10029205",
                    primary_soc_flag="Y",
                )
            )

            # 2. WHODrug Aspirin
            session.add(
                WHODrugRecord(
                    dictionary_version="2024-03",
                    drug_code="00010101001",
                    preferred_name="ASPIRIN",
                    drug_name="ASPIRIN TABLET",
                )
            )
            # WHODrug Ibuprofen
            session.add(
                WHODrugRecord(
                    dictionary_version="2024-03",
                    drug_code="00020202002",
                    preferred_name="IBUPROFEN",
                    drug_name="IBUPROFEN TABLET",
                )
            )

            # Seed a Clinical Subject
            session.add(
                ClinicalSubject(
                    id="SUBJ-UUID-1",
                    subject_id="SUBJ-001",
                    study_id="STUDY-001",
                )
            )


# =========================================================================
# Integration Tests for Medical Coding Engine Lifecycle
# =========================================================================


@pytest.mark.asyncio
async def test_auto_coding_on_observation_creation():
    """Verify that observations under AE, MH, or CM domains automatically trigger medical coding."""
    await seed_dictionaries()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Post AE observation that yields perfect high-confidence match (Headache)
        resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "Headache",
            },
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200

        # Retrieve coding assignments
        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assert resp_list.status_code == 200
        assignments = resp_list.json()
        assert len(assignments) == 1
        a = assignments[0]
        assert a["status"] == "AUTO_CODED"
        assert a["coded_code"] == "10019211"
        assert a["coded_term"] == "Headache"
        assert a["score"] == 1.0
        assert a["assigned_by"] == "system"

        # Check ledger
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalCodingLedger)
            res = await session.execute(stmt)
            ledgers = res.scalars().all()
            assert len(ledgers) == 1
            ledger_rec = ledgers[0]
            assert ledger_rec.new_coded_code == "10019211"
            assert ledger_rec.new_coded_term == "Headache"
            assert ledger_rec.decision_by == "system"


@pytest.mark.asyncio
async def test_mid_confidence_persists_as_suggestions():
    """Verify that mid-confidence matches (between 0.60 and 0.85) persist as SUGGESTED and do not silently code."""
    await seed_dictionaries()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a term that has moderate resemblance to Migraine (score 0.60 to 0.85)
        # "migraine severe onset" -> "migraine onset severe" after stop word removal: "migraine" vs "migraine" (score high)
        # Let's use: "Mild Migraines" -> stop-word Mild, plural Migraines -> stem: "migraine". Exact!
        # Let's use: "Migraine pain" -> cosine similarity gets 0.4*s_lev + 0.6*s_cos. S_Lev: 8/13 = 0.615. S_Cos: 1/sqrt(2) = 0.707. Score around ~0.67
        resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "Migraine pain",
            },
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200

        # Retrieve assignment
        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assert resp_list.status_code == 200
        assignments = resp_list.json()
        assert len(assignments) == 1
        a = assignments[0]
        assert a["status"] == "SUGGESTED"
        assert a["coded_code"] is None  # Should not be silently coded!
        assert len(a["suggestions"]) >= 1
        assert a["suggestions"][0]["code"] == "10029300"


@pytest.mark.asyncio
async def test_coder_action_accept_and_override_lifecycle():
    """Verify coder actions (Accepting a suggestion, manual overrides) with role gates and Part 11 auditing."""
    await seed_dictionaries()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a suggested assignment
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "Migraine pain",
            },
            headers=get_auth_headers(),
        )

        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        a_id = resp_list.json()[0]["id"]

        # 2. Reject actions without Data Manager / Coder role synonyms
        resp_fail = await client.post(
            f"/api/v1/execution/coding/assignments/{a_id}/action",
            json={"action": "ACCEPT", "suggestion_index": 0},
            headers=get_auth_headers(roles="Site Investigator"),
        )
        assert resp_fail.status_code == 403

        # 3. Reject actions without verified X-Change-Reason headers
        headers_no_reason = get_auth_headers()
        headers_no_reason.pop("X-Change-Reason")
        resp_fail = await client.post(
            f"/api/v1/execution/coding/assignments/{a_id}/action",
            json={"action": "ACCEPT", "suggestion_index": 0},
            headers=headers_no_reason,
        )
        assert resp_fail.status_code == 403

        # 4. Accept suggestion index 0
        resp_ok = await client.post(
            f"/api/v1/execution/coding/assignments/{a_id}/action",
            json={"action": "ACCEPT", "suggestion_index": 0},
            headers=get_auth_headers(user_id="alice_coder"),
        )
        assert resp_ok.status_code == 200
        data = resp_ok.json()
        assert data["status"] == "CODED"
        assert data["coded_code"] == "10029300"
        assert data["coded_term"] == "Migraine"
        assert data["assigned_by"] == "alice_coder"

        # Check ledger
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalCodingLedger).where(
                ClinicalCodingLedger.assignment_id == a_id
            )
            res = await session.execute(stmt)
            ledgers = res.scalars().all()
            assert len(ledgers) == 1
            assert ledgers[0].new_coded_code == "10029300"
            assert ledgers[0].decision_by == "alice_coder"

        # 5. Try Manual Override with nonexistent code -> Rejected
        resp_fail = await client.post(
            f"/api/v1/execution/coding/assignments/{a_id}/action",
            json={
                "action": "OVERRIDE",
                "code": "99999999",
                "term": "Super Headache",
                "reason_for_change": "Manual review correction",
            },
            headers=get_auth_headers(),
        )
        assert resp_fail.status_code == 400

        # 6. Override with valid code (10019211) -> Success
        resp_override = await client.post(
            f"/api/v1/execution/coding/assignments/{a_id}/action",
            json={
                "action": "OVERRIDE",
                "code": "10019211",
                "term": "Headache",
                "reason_for_change": "Doctor override clinical decision",
            },
            headers=get_auth_headers(user_id="bob_coder"),
        )
        assert resp_override.status_code == 200
        data = resp_override.json()
        assert data["status"] == "CODED"
        assert data["coded_code"] == "10019211"
        assert data["coded_term"] == "Headache"
        assert data["assigned_by"] == "bob_coder"
