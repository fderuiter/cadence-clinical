import hashlib
import hmac
import json
import time
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest

from apps.designer.db import (
    MOCK_RULES,
)
from apps.designer.delta import (
    create_rule_node,
    delete_rule_node,
    get_rules_from_graph,
    update_rule_node,
)
from apps.designer.main import app
from apps.designer.mapper import map_study_to_usdm
from apps.designer.rules import (
    CreateRuleRequest,
    ExpressionNode,
    FieldReference,
    compile_to_xpath,
    detect_circular_dependencies,
    detect_unknown_fields,
)

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_designer",
    roles="STUDY_DESIGNER",
    change_reason="Adding skip logic rules",
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
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


# =====================================================================
# 1. SCHEMA VALIDATION TESTS
# =====================================================================


def test_valid_skip_logic_schema():
    payload = {
        "type": "skip_logic",
        "condition": {
            "type": "comparison",
            "operator": "==",
            "operands": [
                {"type": "field_ref", "field_ref": {"field_id": "VSPERF"}},
                {"type": "constant", "value": "N"},
            ],
        },
        "action": "hide",
        "target_field": "VSSBP",
    }
    req = CreateRuleRequest(**payload)
    assert req.type == "skip_logic"
    assert req.target_field == "VSSBP"
    assert req.action == "hide"


def test_invalid_skip_logic_schema_missing_fields():
    # Missing action and target_field for skip_logic
    payload = {"type": "skip_logic", "condition": {"type": "constant", "value": True}}
    with pytest.raises(ValueError) as exc:
        CreateRuleRequest(**payload)
    assert "target_field" in str(exc.value) or "action" in str(exc.value)


def test_invalid_logical_not_arity():
    # 'not' logical operator must have exactly 1 operand
    payload = {
        "type": "skip_logic",
        "condition": {
            "type": "logical",
            "operator": "not",
            "operands": [
                {"type": "constant", "value": True},
                {"type": "constant", "value": False},
            ],
        },
        "action": "hide",
        "target_field": "VSSBP",
    }
    with pytest.raises(ValueError) as exc:
        CreateRuleRequest(**payload)
    assert "Logical 'not' operator requires exactly 1 operand" in str(exc.value)


def test_invalid_comparison_arity():
    # comparison must have exactly 2 operands
    payload = {
        "type": "skip_logic",
        "condition": {
            "type": "comparison",
            "operator": "==",
            "operands": [{"type": "constant", "value": 5}],
        },
        "action": "hide",
        "target_field": "VSSBP",
    }
    with pytest.raises(ValueError) as exc:
        CreateRuleRequest(**payload)
    assert "requires exactly 2 operands" in str(exc.value)


# =====================================================================
# 2. XPATH COMPILATION TESTS
# =====================================================================


def test_xpath_compile_simple():
    node = ExpressionNode(
        type="comparison",
        operator="==",
        operands=[
            ExpressionNode(
                type="field_ref", field_ref=FieldReference(field_id="VSSBP")
            ),
            ExpressionNode(type="constant", value=120),
        ],
    )
    xpath = compile_to_xpath(node)
    assert xpath == "(/clinical_data/VSSBP = 120)"


def test_xpath_compile_logical_and_functions():
    node = ExpressionNode(
        type="logical",
        operator="and",
        operands=[
            ExpressionNode(
                type="function",
                operator="is_not_empty",
                operands=[
                    ExpressionNode(
                        type="field_ref", field_ref=FieldReference(field_id="VSSBP")
                    )
                ],
            ),
            ExpressionNode(
                type="comparison",
                operator=">",
                operands=[
                    ExpressionNode(
                        type="field_ref", field_ref=FieldReference(field_id="VSSBP")
                    ),
                    ExpressionNode(type="constant", value=200),
                ],
            ),
        ],
    )
    xpath = compile_to_xpath(node)
    assert "not(empty(/clinical_data/VSSBP))" in xpath
    assert "/clinical_data/VSSBP > 200" in xpath
    assert " AND " in xpath


# =====================================================================
# 3. UNKNOWN FIELDS AND CIRCULAR CYCLES
# =====================================================================


def test_detect_unknown_fields():
    study_projection = {
        "arms": [
            {
                "visits": [
                    {
                        "visit_id": "visit_1",
                        "activities": [{"activity_id": "act_1", "name": "Vitals"}],
                    }
                ]
            }
        ]
    }

    # Valid field
    node_valid = ExpressionNode(
        type="field_ref", field_ref=FieldReference(field_id="act_1")
    )
    assert len(detect_unknown_fields(node_valid, study_projection)) == 0

    # Invalid field
    node_invalid = ExpressionNode(
        type="field_ref", field_ref=FieldReference(field_id="nonexistent_field")
    )
    failures = detect_unknown_fields(node_invalid, study_projection)
    assert len(failures) == 1
    assert "Unknown field reference: 'nonexistent_field'" in failures[0]


def test_detect_circular_dependencies():
    # Let's create a cycle: A depends on B, B depends on A
    rules = [
        {
            "id": "rule_a",
            "type": "skip_logic",
            "target_field": "field_A",
            "condition": {"type": "field_ref", "field_ref": {"field_id": "field_B"}},
        },
        {
            "id": "rule_b",
            "type": "skip_logic",
            "target_field": "field_B",
            "condition": {"type": "field_ref", "field_ref": {"field_id": "field_A"}},
        },
    ]
    cycles = detect_circular_dependencies(rules)
    assert len(cycles) > 0
    assert (
        "field_A -> field_B -> field_A" in cycles[0]
        or "field_B -> field_A -> field_B" in cycles[0]
    )


# =====================================================================
# 4. REST API CRUD & PREVIEW TESTS
# =====================================================================


@pytest.mark.asyncio
async def test_rules_crud_endpoints():
    """
    Test rules CRUD endpoints.
    @req: Trace-11
    """
    # Clear rules first
    MOCK_RULES.clear()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a rule
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

        create_res = await client.post(
            "/api/v1/studies/study_1/rules",
            json=rule_payload,
            headers=get_auth_headers(),
        )
        assert create_res.status_code == 201
        created_data = create_res.json()
        assert "id" in created_data
        rule_id = created_data["id"]
        assert created_data["version_index"] == 1
        assert created_data["target_field"] == "act_2"

        # 2. Get rules list
        list_res = await client.get(
            "/api/v1/studies/study_1/rules", headers=get_auth_headers()
        )
        assert list_res.status_code == 200
        assert len(list_res.json()) == 1
        assert list_res.json()[0]["id"] == rule_id

        # 3. Get rule by ID
        get_res = await client.get(
            f"/api/v1/studies/study_1/rules/{rule_id}", headers=get_auth_headers()
        )
        assert get_res.status_code == 200
        assert get_res.json()["target_field"] == "act_2"

        # 4. Update the rule
        update_payload = rule_payload.copy()
        update_payload["action"] = "show"
        update_res = await client.put(
            f"/api/v1/studies/study_1/rules/{rule_id}",
            json=update_payload,
            headers=get_auth_headers(),
        )
        assert update_res.status_code == 200
        updated_data = update_res.json()
        assert updated_data["action"] == "show"
        assert updated_data["version_index"] == 2

        # 5. Preview endpoint
        preview_payload = {
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
            "target_field": "act_1",  # Causes circle since act_1 depends on act_1
        }
        preview_res = await client.post(
            "/api/v1/studies/study_1/rules/preview",
            json=preview_payload,
            headers=get_auth_headers(),
        )
        assert preview_res.status_code == 200
        preview_data = preview_res.json()
        assert "xpath" in preview_data
        assert "failures" in preview_data
        assert len(preview_data["circular_cycles"]) > 0

        # 6. Delete the rule
        del_res = await client.delete(
            f"/api/v1/studies/study_1/rules/{rule_id}", headers=get_auth_headers()
        )
        assert del_res.status_code == 200
        assert del_res.json()["status"] == "success"

        # Verify soft-deleted rule is no longer in active list
        list_res_after = await client.get(
            "/api/v1/studies/study_1/rules", headers=get_auth_headers()
        )
        assert len(list_res_after.json()) == 0


@pytest.mark.asyncio
async def test_rules_auth_gateways():
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Missing gateway headers entirely
        res = await client.get("/api/v1/studies/study_1/rules")
        assert res.status_code == 401


# =====================================================================
# 5. NEO4J CYPHER INTEGRATION TESTS
# =====================================================================


@pytest.mark.asyncio
async def test_neo4j_create_rule():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    result_mock = AsyncMock()
    result_mock.single.return_value = {"rule_id": "rule_1"}
    tx_mock.run.return_value = result_mock

    rule_data = {
        "type": "skip_logic",
        "condition": {"type": "constant", "value": True},
        "action": "hide",
        "target_field": "VSSBP",
    }

    res = await create_rule_node(
        driver_mock, "study_1", "user_123", "Creating first rule", "rule_1", rule_data
    )
    assert res == "rule_1"
    assert tx_mock.run.call_count == 2
    assert "CREATE (r:Rule" in tx_mock.run.call_args_list[1][0][0]


@pytest.mark.asyncio
async def test_neo4j_update_rule():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    result_mock = AsyncMock()
    result_mock.single.return_value = {"version_index": 2}
    tx_mock.run.return_value = result_mock

    rule_data = {
        "type": "skip_logic",
        "condition": {"type": "constant", "value": True},
        "action": "show",
        "target_field": "VSSBP",
    }

    res = await update_rule_node(
        driver_mock, "study_1", "rule_1", "user_123", "Updating rule", rule_data
    )
    assert res == 2
    assert tx_mock.run.call_count == 2
    assert "CREATE (new_rv:RuleVersion" in tx_mock.run.call_args_list[1][0][0]


@pytest.mark.asyncio
async def test_neo4j_delete_rule():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    result_mock = AsyncMock()
    result_mock.single.return_value = {"version_index": 3}
    tx_mock.run.return_value = result_mock

    res = await delete_rule_node(
        driver_mock, "study_1", "rule_1", "user_123", "Deleting rule"
    )
    assert res == 3
    assert tx_mock.run.call_count == 2
    assert "is_deleted: true" in tx_mock.run.call_args_list[1][0][0]


@pytest.mark.asyncio
async def test_neo4j_get_rules():
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = MagicMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    record_mock = MagicMock()
    record_mock.__getitem__.return_value = {
        "id": "rule_1",
        "type": "skip_logic",
        "condition_json": '{"type": "constant", "value": true}',
        "action": "show",
        "target_field": "VSSBP",
    }

    result_mock = AsyncMock()
    result_mock.all.return_value = [record_mock]
    session_mock.run.return_value = result_mock

    rules = await get_rules_from_graph(driver_mock, "study_1")
    assert len(rules) == 1
    assert rules[0]["id"] == "rule_1"
    assert rules[0]["condition"] == {"type": "constant", "value": True}


# =====================================================================
# 6. DDF / USDM PROJECTION MAPPING TESTS
# =====================================================================


def test_map_study_to_usdm_with_rules():
    study_data = {
        "study_id": "study_1",
        "title": "Oncology Phase II",
        "current_version": "2.1",
        "desc": "A study for solid tumors.",
        "arms": [
            {
                "arm_id": "arm_1",
                "name": "Arm A",
                "visits": [
                    {
                        "visit_id": "visit_1",
                        "name": "Visit 1",
                        "activities": [{"activity_id": "act_1", "name": "Vitals"}],
                    }
                ],
            }
        ],
        "rules": [
            {
                "id": "rule_vssbp_constraint",
                "type": "constraint",
                "condition": {"type": "constant", "value": True},
                "target_field": "act_1",
                "query_message": "Invalid value",
                "is_deleted": False,
                "version_index": 1,
            }
        ],
    }

    mapped = map_study_to_usdm(study_data)
    assert "rules" in mapped
    assert len(mapped["rules"]) == 1
    assert mapped["rules"][0]["id"] == "rule_vssbp_constraint"

    # Per-item checks
    mapped_activities = mapped["arms"][0]["visits"][0]["activities"]
    assert len(mapped_activities) == 1
    assert "rules" in mapped_activities[0]
    assert mapped_activities[0]["rules"][0]["id"] == "rule_vssbp_constraint"
