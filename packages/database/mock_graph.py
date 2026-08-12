import logging
from collections.abc import Callable
from contextlib import asynccontextmanager
from typing import Any

from packages.database.graph import validate_cypher_query

logger = logging.getLogger("packages.database.mock_graph")


class MockRecord:
    """
    Simulates a Neo4j Record.
    """

    def __init__(self, data_dict: dict[str, Any]) -> None:
        self._data = data_dict

    def __getitem__(self, key: str) -> Any:
        return self._data[key]

    def get(self, key: str, default: Any = None) -> Any:
        return self._data.get(key, default)

    def keys(self) -> Any:
        return self._data.keys()

    def values(self) -> Any:
        return self._data.values()

    def items(self) -> Any:
        return self._data.items()

    def __iter__(self) -> Any:
        return iter(self._data)

    def __len__(self) -> int:
        return len(self._data)


class MockGraphResult:
    """
    Simulates a Neo4j Result.
    """

    def __init__(self, records: list[dict[str, Any]] | None = None) -> None:
        self._records = [MockRecord(r) for r in (records or [])]
        self._index = 0

    def __aiter__(self) -> MockGraphResult:
        return self

    async def __anext__(self) -> MockRecord:
        if self._index >= len(self._records):
            raise StopAsyncIteration
        record = self._records[self._index]
        self._index += 1
        return record

    async def single(self) -> MockRecord | None:
        if not self._records:
            return None
        return self._records[0]

    async def data(self) -> list[dict[str, Any]]:
        return [dict(r._data) for r in self._records]

    async def values(self) -> list[list[Any]]:
        return [list(r.values()) for r in self._records]

    async def consume(self) -> None:
        pass


class MockGraphDriver:
    """
    Simulates a Neo4j Graph Database Driver.
    """

    def __init__(self) -> None:
        self.sessions: list[MockGraphSession] = []

    async def __aenter__(self) -> MockGraphDriver:
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> Any:
        pass

    def session(self, **kwargs: Any) -> MockGraphSession:
        s = MockGraphSession()
        self.sessions.append(s)
        return s

    async def close(self) -> None:
        pass

    async def verify_connectivity(self) -> None:
        pass


class MockGraphSession:
    """
    Simulates a Neo4j Session.
    """

    def __init__(self) -> None:
        self.queries: list[tuple[str, dict[str, Any] | None]] = []
        self.transactions: list[MockGraphTransaction] = []
        self._mock_responses: list[list[dict[str, Any]]] = []

    def seed_responses(self, responses: list[list[dict[str, Any]]]) -> None:
        """
        Seed responses to return sequentially on successive query run calls.
        """
        self._mock_responses = responses

    async def __aenter__(self) -> MockGraphSession:
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> Any:
        pass

    async def run(
        self, query: str, parameters: dict[str, Any] | None = None, **kwargs: Any
    ) -> MockGraphResult:
        validate_cypher_query(query)
        self.queries.append((query, parameters))
        logger.info(f"MockGraphSession: Running query: {query}")

        if self._mock_responses:
            return MockGraphResult(self._mock_responses.pop(0))
        return MockGraphResult()

    @asynccontextmanager
    async def begin_transaction(self, **kwargs: Any) -> Any:
        tx = MockGraphTransaction(self)
        self.transactions.append(tx)
        logger.info("MockGraphSession: Beginning transaction")
        try:
            yield tx
            logger.info("MockGraphSession: Transaction block completed successfully")
        except Exception as e:
            logger.error(f"MockGraphSession: Transaction block failed with error: {e}")
            raise

    async def execute_read(
        self, transaction_function: Callable, *args: Any, **kwargs: Any
    ) -> Any:
        tx = MockGraphTransaction(self)
        self.transactions.append(tx)
        return await transaction_function(tx, *args, **kwargs)

    async def execute_write(
        self, transaction_function: Callable, *args: Any, **kwargs: Any
    ) -> Any:
        tx = MockGraphTransaction(self)
        self.transactions.append(tx)
        return await transaction_function(tx, *args, **kwargs)


class MockGraphTransaction:
    """
    Simulates a Neo4j Transaction.
    """

    def __init__(self, session: MockGraphSession) -> None:
        self.session = session
        self.queries: list[tuple[str, dict[str, Any] | None]] = []
        self.committed = False
        self.rolled_back = False

    async def __aenter__(self) -> MockGraphTransaction:
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> Any:
        pass

    async def run(
        self, query: str, parameters: dict[str, Any] | None = None, **kwargs: Any
    ) -> MockGraphResult:
        validate_cypher_query(query)
        self.queries.append((query, parameters))
        self.session.queries.append((query, parameters))
        logger.info(f"MockGraphTransaction: Running query in transaction: {query}")

        if self.session._mock_responses:
            return MockGraphResult(self.session._mock_responses.pop(0))
        return MockGraphResult()

    async def commit(self) -> None:
        self.committed = True
        logger.info("MockGraphTransaction committed")

    async def rollback(self) -> None:
        self.rolled_back = True
        logger.info("MockGraphTransaction rolled back")
