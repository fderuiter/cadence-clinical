from unittest.mock import AsyncMock, MagicMock

import pytest

from apps.designer.adapter.safety_gateway import (
    QuerySafetyError,
    SafeDriver,
    validate_query_safety,
)


def test_unbounded_wildcards_validation() -> None:
    """Verify that wildcard path bounds are correctly checked and blocked/allowed."""
    # Bounded - Safe
    validate_query_safety("MATCH (s)-[:HAS_VERSION*1..5]->(v)")
    validate_query_safety("MATCH (s)-[:HAS_VERSION*..5]->(v)")
    validate_query_safety("MATCH (s)-[:HAS_VERSION*5]->(v)")
    validate_query_safety("MATCH (s)-[*1..2 {prop: $prop_val}]->(v)")

    # Unbounded - Unsafe
    with pytest.raises(QuerySafetyError) as exc_info:
        validate_query_safety("MATCH (s)-[:HAS_VERSION*1..]->(v)")
    assert "Unbounded variable-length path traversal" in str(exc_info.value)

    with pytest.raises(QuerySafetyError):
        validate_query_safety("MATCH (s)-[:HAS_VERSION*]->(v)")

    with pytest.raises(QuerySafetyError):
        validate_query_safety("MATCH (s)-[*]->(v)")


def test_parameter_bypass_validation() -> None:
    """Verify that dynamic raw string comparison is blocked but parameters are allowed."""
    # Parameterized / Safe Assignments
    validate_query_safety("MATCH (s:Study {id: $study_id})")
    validate_query_safety("MATCH (s:Study) WHERE s.name = $name")
    validate_query_safety("MATCH (s:Study) SET s.status = 'APPROVED'")
    validate_query_safety(
        "MATCH (th:CommentThread {id: $id}) SET th.status = 'resolved'"
    )
    validate_query_safety("WHERE s.status IN ['Active', 'Active-Recruiting']")

    # Unsafe Bypasses
    with pytest.raises(QuerySafetyError) as exc_info:
        validate_query_safety("MATCH (s:Study {id: 'some-uuid'})")
    assert "Potential parameter bypass" in str(exc_info.value)

    with pytest.raises(QuerySafetyError):
        validate_query_safety('MATCH (s:Study {id: "some-uuid"})')

    with pytest.raises(QuerySafetyError):
        validate_query_safety("MATCH (s:Study) WHERE s.name = 'some-name'")

    with pytest.raises(QuerySafetyError):
        validate_query_safety('MATCH (s:Study) WHERE s.name == "some-name"')


@pytest.mark.asyncio
async def test_driver_session_transaction_wrappers() -> None:
    """Verify that safe wrappers correctly intercept run calls and raise QuerySafetyError."""
    # Setup mock neo4j driver components
    mock_tx = AsyncMock()
    mock_tx.run = AsyncMock(return_value="tx_result")

    mock_session = AsyncMock()
    mock_session.run = AsyncMock(return_value="session_result")
    mock_session.begin_transaction = AsyncMock(return_value=mock_tx)

    mock_driver = MagicMock()
    mock_driver.session = MagicMock(return_value=mock_session)

    # Wrap them
    safe_driver = SafeDriver(mock_driver)
    safe_session = safe_driver.session()

    # 1. Test run on safe session
    # Safe query should proceed
    res = await safe_session.run("MATCH (n:Study {id: $id}) RETURN n", id="123")
    assert res == "session_result"
    mock_session.run.assert_called_once_with(
        "MATCH (n:Study {id: $id}) RETURN n", id="123"
    )

    # Unsafe query should be blocked
    with pytest.raises(QuerySafetyError):
        await safe_session.run("MATCH (n:Study {id: '123'}) RETURN n")

    # 2. Test run on safe transaction
    safe_tx = await safe_session.begin_transaction()
    res_tx = await safe_tx.run("MATCH (n:Study)-[:HAS_VERSION*1..5]->(v)")
    assert res_tx == "tx_result"

    with pytest.raises(QuerySafetyError):
        await safe_tx.run("MATCH (n:Study)-[:HAS_VERSION*1..]->(v)")
