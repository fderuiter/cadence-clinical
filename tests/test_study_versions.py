import hashlib
import hmac
import json
import time
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest

from apps.designer.db import (
    MOCK_RULES,
    MOCK_STUDY_VERSIONS,
    assert_mock_study_mutable,
    create_mock_study_version,
)
from apps.designer.delta import (
    ConcurrentLockingError,
    ImmutabilityViolationError,
    assert_graph_mutable,
    create_library_object_version,
    create_study_version,
    update_study_properties,
)
from apps.designer.main import app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_designer",
    roles="STUDY_DESIGNER",
    change_reason="Study versioning operations",
    action_path=None,
    sig_token_custom=None,
):
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
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if sig_token_custom:
        headers["X-Sig-Token"] = sig_token_custom
    elif action_path:
        from jose import jwt

        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action_path,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
            "jti": f"jti-{time.time()}-{hash(action_path)}-{time.process_time()}",
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token
    return headers


# =====================================================================
# FAKE STRUCTURES TO TEST assert_graph_mutable WITHOUT TRIGGERING BYPASS
# =====================================================================


class FakeRecord:
    def __init__(self, data):
        self.data = data

    def get(self, key, default=None):
        return self.data.get(key, default)

    def __getitem__(self, key):
        return self.data[key]


class FakeResult:
    def __init__(self, record_data):
        self.record_data = record_data

    async def single(self):
        return FakeRecord(self.record_data) if self.record_data else None


class FakeTransaction:
    def __init__(self, record_data=None):
        self.record_data = record_data
        self.queries = []

    async def run(self, query, **kwargs):
        self.queries.append((query, kwargs))
        return FakeResult(self.record_data)

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        pass


class FakeSession:
    def __init__(self, tx):
        self.tx = tx

    async def begin_transaction(self):
        return self.tx

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        pass


class FakeDriver:
    def __init__(self, tx):
        self.tx = tx

    def session(self):
        return FakeSession(self.tx)


# =====================================================================
# 1. UNIT TESTS FOR CUSTOM EXCEPTIONS AND DELTA FUNCTIONS (MOCKED NEO4J)
# =====================================================================


@pytest.mark.asyncio
async def test_neo4j_create_study_version_success():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    # Side effects for tx.run:
    # 1. Lock study query
    lock_res = AsyncMock()
    # 2. Check existing versions (none found)
    check_res = AsyncMock()
    check_res.single.return_value = None
    # 3. Create version query
    create_res = AsyncMock()
    create_res.single.return_value = {"id": "version_123"}

    tx_mock.run.side_effect = [lock_res, check_res, create_res]

    version_id = await create_study_version(
        driver_mock,
        study_id="study_1",
        version_id="version_123",
        version_tag="1.0",
        status="DRAFT",
        version_index=1,
        created_by="user_designer",
    )

    assert version_id == "version_123"
    assert tx_mock.run.call_count == 3


@pytest.mark.asyncio
async def test_neo4j_create_study_version_duplicate_raises_conflict():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    # Side effects for tx.run:
    # 1. Lock study query
    lock_res = AsyncMock()
    # 2. Check existing versions (finds an existing version)
    check_res = AsyncMock()
    check_res.single.return_value = {"id": "version_123"}

    tx_mock.run.side_effect = [lock_res, check_res]

    with pytest.raises(ConcurrentLockingError) as exc_info:
        await create_study_version(
            driver_mock,
            study_id="study_1",
            version_id="version_124",
            version_tag="1.0",
            status="DRAFT",
            version_index=1,
            created_by="user_designer",
        )

    assert "Version index or tag already exists" in str(exc_info.value)
    assert tx_mock.run.call_count == 2


@pytest.mark.asyncio
async def test_assert_graph_mutable_permits_draft_active():
    tx = FakeTransaction({"status": "DRAFT"})
    # Should succeed without raising exception
    await assert_graph_mutable(tx, study_id="study_1")

    tx = FakeTransaction({"status": "ACTIVE"})
    await assert_graph_mutable(tx, study_id="study_1")


@pytest.mark.asyncio
async def test_assert_graph_mutable_rejects_frozen_states():
    for frozen_state in ("LOCKED", "PUBLISHED", "ARCHIVED"):
        tx = FakeTransaction({"status": frozen_state})
        with pytest.raises(ImmutabilityViolationError):
            await assert_graph_mutable(tx, study_id="study_1")


@pytest.mark.asyncio
async def test_assert_graph_mutable_library_object_permits_active():
    tx = FakeTransaction({"status": "DRAFT"})
    # Should succeed without raising exception
    await assert_graph_mutable(tx, object_id="lib_1")


@pytest.mark.asyncio
async def test_assert_graph_mutable_library_object_rejects_frozen():
    tx = FakeTransaction({"status": "LOCKED"})
    with pytest.raises(ImmutabilityViolationError):
        await assert_graph_mutable(tx, object_id="lib_1")


@pytest.mark.asyncio
async def test_update_study_properties_guards():
    tx = FakeTransaction({"status": "LOCKED"})
    driver = FakeDriver(tx)

    with pytest.raises(ImmutabilityViolationError):
        await update_study_properties(
            driver, "study_1", "user_1", "change", {"title": "New Title"}
        )


@pytest.mark.asyncio
async def test_create_library_object_version_guards():
    tx = FakeTransaction({"status": "ARCHIVED"})
    driver = FakeDriver(tx)

    with pytest.raises(ImmutabilityViolationError):
        await create_library_object_version(
            driver, "lib_obj_1", {"name": "New Version"}
        )


# =====================================================================
# 2. IN-MEMORY FALLBACK MUTABLE TESTS (MOCK SYSTEM)
# =====================================================================


def test_mock_study_version_creation_and_immutability():
    study_id = "test_mock_study"
    MOCK_STUDY_VERSIONS[study_id] = []

    # 1. Initially mutable (no versions exist)
    assert_mock_study_mutable(study_id)

    # 2. Draft is mutable
    create_mock_study_version(
        study_id, {"version_tag": "1.0", "version_index": 1, "status": "DRAFT"}
    )
    assert_mock_study_mutable(study_id)

    # 3. Cannot create duplicate index/tag -> raises ConcurrentLockingError
    with pytest.raises(ConcurrentLockingError):
        create_mock_study_version(
            study_id, {"version_tag": "1.0", "version_index": 2, "status": "DRAFT"}
        )

    with pytest.raises(ConcurrentLockingError):
        create_mock_study_version(
            study_id, {"version_tag": "1.1", "version_index": 1, "status": "DRAFT"}
        )

    # 4. Create frozen version
    create_mock_study_version(
        study_id, {"version_tag": "2.0", "version_index": 2, "status": "LOCKED"}
    )

    # 5. Now immutable -> assert_mock_study_mutable raises ImmutabilityViolationError
    with pytest.raises(ImmutabilityViolationError):
        assert_mock_study_mutable(study_id)


# =====================================================================
# 3. REST API ENDPOINT INTEGRATION TESTS
# =====================================================================


@pytest.mark.asyncio
async def test_api_study_version_creation_and_guards():
    # Use a completely unique study ID to avoid contaminating other tests (like study_1)
    study_id = "study_versions_isolated_test"
    MOCK_STUDY_VERSIONS[study_id] = []
    MOCK_RULES.clear()

    # Seed mock study projection so that the endpoints can find it
    import copy

    from apps.designer.db import MOCK_STUDIES

    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Put study in a DRAFT version
        res_v1 = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_1",
                "version_tag": "1.0",
                "status": "DRAFT",
                "version_index": 1,
            },
            headers=get_auth_headers(),
        )
        assert res_v1.status_code == 201

        # 2. Rule creation works on DRAFT
        rule_payload = {
            "type": "skip_logic",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "act_1"}},
                    {"type": "constant", "value": "N"},
                ],
            },
            "action": "hide",
            "target_field": "act_2",
        }
        res_create_rule = await client.post(
            f"/api/v1/studies/{study_id}/rules",
            json=rule_payload,
            headers=get_auth_headers(),
        )
        assert res_create_rule.status_code == 201
        rule_id = res_create_rule.json()["id"]

        # 3. Create a duplicate study version -> 409 CONCURRENT_LOCKING_CONFLICT
        res_dup = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_1_dup",
                "version_tag": "1.0",
                "status": "DRAFT",
                "version_index": 1,
            },
            headers=get_auth_headers(),
        )
        assert res_dup.status_code == 409
        assert "CONCURRENT_LOCKING_CONFLICT" in res_dup.json()["detail"]

        # 4. Advance study to LOCKED state
        res_v2 = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_2",
                "version_tag": "2.0",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_auth_headers(),
        )
        assert res_v2.status_code == 201

        # 5. Rule mutations fail with 403 IMMUTABILITY_VIOLATION
        res_fail_create = await client.post(
            f"/api/v1/studies/{study_id}/rules",
            json=rule_payload,
            headers=get_auth_headers(),
        )
        assert res_fail_create.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_create.json()["detail"]

        # 6. Rule update fails with 403
        res_fail_update_put = await client.put(
            f"/api/v1/studies/{study_id}/rules/{rule_id}",
            json=rule_payload,
            headers=get_auth_headers(),
        )
        assert res_fail_update_put.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_update_put.json()["detail"]

        res_fail_delete = await client.delete(
            f"/api/v1/studies/{study_id}/rules/{rule_id}",
            headers=get_auth_headers(),
        )
        assert res_fail_delete.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_delete.json()["detail"]


# =====================================================================
# 4. DESIGNER AMENDMENT FORK OPERATION TESTS
# =====================================================================


@pytest.mark.asyncio
async def test_api_protocol_amendment_minor_and_major_bumps():
    """Validates minor clinical-amendment and major design-restructuring bumps.

    Requirements: PRD-MDR-002
    """
    study_id = "amend_bump_test_study"

    import copy

    from apps.designer.db import (
        MOCK_STUDIES,
        MOCK_STUDY_PROJECTIONS_BY_VERSION,
        MOCK_STUDY_VERSIONS,
    )

    MOCK_STUDY_VERSIONS[study_id] = []
    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id
    MOCK_STUDIES[study_id]["current_version"] = "2.1"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Establish parent locked version (2.1)
        res_parent = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_parent",
                "version_tag": "2.1",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_auth_headers(),
        )
        assert res_parent.status_code == 201

        # 2. Perform a minor clinical-amendment bump
        res_minor = await client.post(
            f"/api/designer/protocols/{study_id}/amend",
            json={"amendment_type": "clinical-amendment"},
            headers=get_auth_headers(change_reason="Minor clinical tweak"),
        )
        assert res_minor.status_code == 201
        data_minor = res_minor.json()
        assert data_minor["new_version"] == "2.2"
        assert data_minor["status"] == "DRAFT"
        assert data_minor["parent_version"] == "2.1"

        # Check that parent projection remains unchanged
        parent_projection = MOCK_STUDY_PROJECTIONS_BY_VERSION.get(f"{study_id}:2.1")
        assert parent_projection is not None
        assert parent_projection["current_version"] == "2.1"

        # Check that new draft version is active
        current_projection = MOCK_STUDIES[study_id]
        assert current_projection["current_version"] == "2.2"
        assert len(current_projection["actions"]) > 0
        latest_action = current_projection["actions"][-1]
        assert latest_action["change_reason"] == "Minor clinical tweak"
        assert latest_action["parent_version"] == "2.1"
        assert latest_action["new_version"] == "2.2"

        # 3. Perform a major design-restructuring bump
        # Advance the 2.2 to LOCKED state first to allow amending it
        latest_ver = MOCK_STUDY_VERSIONS[study_id][-1]
        latest_ver["status"] = "LOCKED"
        # Re-sign
        import os

        from packages.security.signing import generate_canonical_signature

        payload = {
            "id": latest_ver["id"],
            "version_tag": latest_ver["version_tag"],
            "status": "LOCKED",
            "version_index": latest_ver["version_index"],
            "created_by": latest_ver["created_by"],
        }
        if "created_at" in latest_ver:
            payload["created_at"] = str(latest_ver["created_at"])
        if "parent_version" in latest_ver:
            payload["parent_version"] = latest_ver["parent_version"]
        secret = os.getenv(
            "SIGNING_SECRET", "designer-amendment-secure-key-12345"
        ).encode("utf-8")
        latest_ver["signature"] = generate_canonical_signature(payload, secret)

        res_major = await client.post(
            f"/api/designer/protocols/{study_id}/amend",
            json={"type": "design-restructuring"},
            headers=get_auth_headers(change_reason="Major restructuring"),
        )
        assert res_major.status_code == 201
        data_major = res_major.json()
        assert data_major["new_version"] == "3.0"
        assert data_major["status"] == "DRAFT"
        assert data_major["parent_version"] == "2.2"


@pytest.mark.asyncio
async def test_api_protocol_amendment_invalid_signature_rejected():
    """Validates that missing or tampered signatures are rejected on load.

    Requirements: PRD-MDR-002
    """
    study_id = "signature_rejection_test_study"

    import copy

    from apps.designer.db import MOCK_STUDIES, MOCK_STUDY_VERSIONS

    MOCK_STUDY_VERSIONS[study_id] = []
    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Establish parent version
        res_parent = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_parent",
                "version_tag": "2.1",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_auth_headers(),
        )
        assert res_parent.status_code == 201

        # Tamper with the signature of the parent version
        MOCK_STUDY_VERSIONS[study_id][0]["signature"] = "invalid-tampered-signature"

        # Attempt to amend should fail with 400 detail="INVALID_OR_MISSING_SIGNATURE"
        res_amend = await client.post(
            f"/api/designer/protocols/{study_id}/amend",
            json={"amendment_type": "clinical-amendment"},
            headers=get_auth_headers(),
        )
        assert res_amend.status_code == 400
        assert res_amend.json()["detail"] == "INVALID_OR_MISSING_SIGNATURE"


@pytest.mark.asyncio
async def test_api_protocol_amendment_concurrency_race():
    """Validates that concurrent amendments on the same study/protocol produce a conflict.

    Requirements: PRD-MDR-002
    """
    study_id = "concurrency_race_test_study"

    import asyncio
    import copy

    from apps.designer.db import MOCK_STUDIES, MOCK_STUDY_VERSIONS

    MOCK_STUDY_VERSIONS[study_id] = []
    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Establish parent version
        res_parent = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_parent",
                "version_tag": "2.1",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_auth_headers(),
        )
        assert res_parent.status_code == 201

        # Issue multiple concurrent amendment requests
        tasks = [
            client.post(
                f"/api/designer/protocols/{study_id}/amend",
                json={"amendment_type": "clinical-amendment"},
                headers=get_auth_headers(),
            )
            for _ in range(3)
        ]

        responses = await asyncio.gather(*tasks)

        # One of them should succeed (201), others should fail due to race safety (409 Conflict)
        success_count = sum(1 for r in responses if r.status_code == 201)
        conflict_count = sum(1 for r in responses if r.status_code == 409)

        assert success_count == 1
        assert conflict_count == 2


def test_bump_version_edge_cases():
    """
    Directly tests the bump_version helper with various semantic inputs.
    """
    from apps.designer.delta import bump_version

    # Minor bumps
    assert bump_version("1.0", "minor") == "1.1"
    assert bump_version("v2.1", "clinical-amendment") == "v2.2"
    assert bump_version("1", "minor") == "1.1"

    # Major bumps
    assert bump_version("1.0", "major") == "2.0"
    assert bump_version("v2.1.3", "design-restructuring") == "v3.0.0"

    # Invalid tag format fallback
    assert bump_version("invalid_tag", "minor") == "invalid_tag-draft"


@pytest.mark.asyncio
async def test_api_protocol_amendment_invalid_study_404():
    """
    Validates that amending an invalid study ID returns a 404.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/designer/protocols/non_existent_study_id/amend",
            json={"amendment_type": "minor"},
            headers=get_auth_headers(),
        )
        assert res.status_code == 404


def test_verify_version_signature_edge_cases():
    """
    Validates verify_version_signature with empty or invalid structures.
    """
    from apps.designer.delta import verify_version_signature

    # Empty dictionary
    assert not verify_version_signature({})

    # Dict without signature
    assert not verify_version_signature({"id": "v_123", "status": "DRAFT"})


@pytest.mark.asyncio
async def test_api_protocol_approval_and_immutability():
    """Comprehensive integration test verifying approval, immutability, and signatures.

    Requirements: PRD-MDR-002
    """
    study_id = "approval_test_study"
    version_id = "v_approval"

    import copy

    from apps.designer.db import MOCK_RULES, MOCK_STUDIES, MOCK_STUDY_VERSIONS

    MOCK_STUDY_VERSIONS[study_id] = []
    MOCK_RULES[study_id] = []
    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Establish DRAFT version
        res_v1 = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": version_id,
                "version_tag": "1.0",
                "status": "DRAFT",
                "version_index": 1,
            },
            headers=get_auth_headers(),
        )
        assert res_v1.status_code == 201

        # 2. Try approval without X-Sig-Token -> Should fail with 401
        res_no_token = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/approve",
            json={"signing_reason": "APPROVAL"},
            headers=get_auth_headers(
                roles="STUDY_DESIGNER", change_reason="Try approve"
            ),
        )
        assert res_no_token.status_code == 401
        assert (
            "reauthentication" in res_no_token.json()["detail"].lower()
            or "re-authentication" in res_no_token.json()["message"].lower()
        )

        # 3. Call with valid X-Sig-Token
        action_path = f"/api/v1/studies/{study_id}/versions/{version_id}/approve"
        sig_headers = get_auth_headers(
            roles="STUDY_DESIGNER",
            change_reason="Approve oncology protocol",
            action_path=action_path,
        )

        res_approve = await client.post(
            action_path,
            json={"signing_reason": "APPROVAL"},
            headers=sig_headers,
        )
        assert res_approve.status_code == 200
        approve_data = res_approve.json()
        assert approve_data["status"] == "APPROVED"
        assert approve_data["version_id"] == version_id
        assert "signature_manifestation" in approve_data
        assert approve_data["signature_manifestation"]["signer_id"] == "test_designer"

        # 4. Immutability checks - Subsequent modifications must be strictly rejected with 403
        rule_payload = {
            "type": "skip_logic",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "act_1"}},
                    {"type": "constant", "value": "N"},
                ],
            },
            "action": "hide",
            "target_field": "act_2",
        }
        res_fail_rule = await client.post(
            f"/api/v1/studies/{study_id}/rules",
            json=rule_payload,
            headers=get_auth_headers(),
        )
        assert res_fail_rule.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_rule.json()["detail"]

        # Attempt to add blocks -> 403
        block_payload = {
            "id": "blk_1",
            "block_type": "narrative",
            "order": 1,
            "properties": {"content": "Test block"},
            "change_reason": "Adding block on approved protocol",
        }
        res_fail_block = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/blocks",
            json=block_payload,
            headers=get_auth_headers(roles="STUDY_DESIGNER"),
        )
        assert res_fail_block.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_block.json()["detail"]

        # Attempt to re-approve -> 403
        res_fail_reapprove = await client.post(
            action_path,
            json={"signing_reason": "APPROVAL"},
            headers=get_auth_headers(
                roles="STUDY_DESIGNER",
                change_reason="Re-approve",
                action_path=action_path,
            ),
        )
        assert res_fail_reapprove.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_reapprove.json()["detail"]

        # 5. Tracer Action History Verification
        actions = MOCK_STUDIES[study_id].get("actions", [])
        assert len(actions) > 0
        approval_action = next(a for a in actions if a.get("type") == "APPROVAL")
        assert approval_action["user_id"] == "test_designer"
        assert approval_action["change_reason"] == "Approve oncology protocol"
        assert (
            approval_action["signature_manifestation"]["signer_id"] == "test_designer"
        )
