import pytest

from packages.database import (
    GraphDatabaseManager,
    validate_cypher_query,
    with_transaction_retry,
)


def test_validate_cypher_query_success() -> None:
    # Bounded variable-length patterns
    validate_cypher_query("MATCH (a)-[*1..5]->(b)")
    validate_cypher_query("MATCH (a)-[:REL*..3]->(b)")
    validate_cypher_query("MATCH (a)-[:REL]->(b)")
    validate_cypher_query("MATCH (a)-[*5]->(b)")
    # Non-relationship square brackets should not raise errors
    validate_cypher_query("RETURN [1, 2, 3]")


def test_validate_cypher_query_failures() -> None:
    unbounded_queries = [
        "MATCH (a)-[*]->(b)",
        "MATCH (a)-[*1..]->(b)",
        "MATCH (a)-[:REL*]->(b)",
        "MATCH (a)-[:REL*0..]->(b)",
        "MATCH (a)-[r*]->(b)",
        "MATCH (a)-[ :REL * ]->(b)",
        "MATCH (a)-[ * 1 .. ]->(b)",
    ]
    for q in unbounded_queries:
        with pytest.raises(ValueError) as exc_info:
            validate_cypher_query(q)
        assert "Unbounded variable-length traversal pattern detected" in str(
            exc_info.value
        )


@pytest.mark.asyncio
async def test_mock_graph_database_manager() -> None:
    manager = GraphDatabaseManager("test_service")
    manager.init_db(mock=True)

    assert manager._mock_mode is True

    async with manager.get_session() as session:
        # Seed mock session with dummy record responses
        session.seed_responses([[{"name": "Trial A"}, {"name": "Trial B"}]])

        result = await session.run("MATCH (n:ClinicalTrial) RETURN n")
        assert result is not None

        records = []
        async for r in result:
            records.append(r)

        assert len(records) == 2
        assert records[0]["name"] == "Trial A"
        assert records[1]["name"] == "Trial B"

        # Test transaction
        async with session.begin_transaction() as tx:
            tx_result = await tx.run("MATCH (a)-[:REL*1..5]->(b) RETURN a")
            assert tx_result is not None
            await tx.commit()
            assert tx.committed is True


@pytest.mark.asyncio
async def test_transaction_retry_decorator() -> None:
    class TransientTestError(Exception):
        pass

    call_count = 0

    @with_transaction_retry(max_retries=3, initial_delay=0.001)
    async def flaky_operation():
        nonlocal call_count
        call_count += 1
        if call_count < 3:
            raise TransientTestError("Database lock acquisition timed out")
        return "success"

    res = await flaky_operation()
    assert res == "success"
    assert call_count == 3


@pytest.mark.asyncio
async def test_transaction_retry_failure() -> None:
    class LockTestError(Exception):
        pass

    call_count = 0

    @with_transaction_retry(max_retries=2, initial_delay=0.001)
    async def failing_operation():
        nonlocal call_count
        call_count += 1
        raise LockTestError("concurrency conflict - lock failed")

    with pytest.raises(LockTestError):
        await failing_operation()

    assert call_count == 3  # Initial try + 2 retries
