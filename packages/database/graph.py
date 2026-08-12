import asyncio
import functools
import logging
import os
import re
import time
from collections.abc import Callable
from contextlib import asynccontextmanager
from typing import Any

logger = logging.getLogger("packages.database.graph")

try:
    from neo4j import AsyncGraphDatabase

    NEO4J_AVAILABLE = True
except ImportError:
    NEO4J_AVAILABLE = False


def validate_cypher_query(query: str) -> None:
    """
    Validates a Cypher query string to prevent unbounded variable-length traversals.
    Raises ValueError if an unsafe pattern is detected.
    """
    # Matches relationship bracket content: -[...] or <-[...] or [...]--> etc.
    # We find all matches for content inside square brackets
    for match in re.finditer(r"\[([^\]]+)\]", query):
        content = match.group(1)
        if "*" in content:
            # Extract everything after the asterisk and remove all whitespace for predictable matching
            star_part = content.split("*", 1)[1]
            star_part_clean = "".join(star_part.split())
            # Unbounded cases:
            # 1. Empty (e.g. [*], [:REL*], [r*])
            # 2. Number followed by .. and nothing (e.g. [*1..], [:REL*0..])
            # 3. Just .. (e.g. [*..])
            if star_part_clean == "" or re.match(r"^\d*\.\.$", star_part_clean):
                raise ValueError(
                    f"Unsafe Cypher query: Unbounded variable-length traversal pattern detected in '{match.group(0)}'. "
                    f"All variable-length relationships must specify an upper bound (e.g., *1..5 or *..10)."
                )


def with_transaction_retry(
    max_retries: int = 5, initial_delay: float = 0.05, backoff_factor: float = 2.0
) -> Callable:
    """
    Decorator to retry transactions on transient database lock conflicts with exponential backoff.
    """

    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        async def wrapper(*args: Any, **kwargs: Any) -> Any:
            retries = 0
            delay = initial_delay
            while True:
                try:
                    return await func(*args, **kwargs)
                except Exception as e:
                    err_name = e.__class__.__name__
                    err_msg = str(e).lower()
                    is_transient = (
                        (
                            err_name == "TransientError"
                            and "neo4j" in getattr(e.__class__, "__module__", "")
                        )
                        or (
                            err_name
                            in ("TransientError", "OperationalError", "LockError")
                        )
                        or "lock" in err_msg
                    )
                    if is_transient:
                        if retries >= max_retries:
                            logger.error(
                                f"Transaction failed after {retries} retries due to transient error: {e}"
                            )
                            raise e
                        retries += 1
                        logger.warning(
                            f"Transient database error detected ({err_name}): {e}. "
                            f"Retrying transaction (attempt {retries}/{max_retries}) in {delay:.3f}s."
                        )
                        await asyncio.sleep(delay)
                        delay *= backoff_factor
                    else:
                        raise e

        return wrapper

    return decorator


class WrappedGraphTransaction:
    """
    Wraps a Neo4j transaction to enforce query validation, timing metrics, and auditing logs.
    """

    def __init__(self, tx: Any) -> None:
        self._tx = tx

    async def run(
        self, query: str, parameters: dict[str, Any] | None = None, **kwargs: Any
    ) -> Any:
        validation_start = time.perf_counter()
        try:
            validate_cypher_query(query)
        except ValueError as e:
            logger.error(f"Transaction query validation failed: {e}")
            raise
        validation_duration = (time.perf_counter() - validation_start) * 1000
        logger.debug(
            f"Transaction query validation latency: {validation_duration:.3f} ms"
        )

        start_time = time.perf_counter()
        logger.info(f"Executing Cypher query in transaction: {query}")
        try:
            result = await self._tx.run(query, parameters, **kwargs)
            duration = (time.perf_counter() - start_time) * 1000
            logger.info(f"Transaction query executed successfully in {duration:.2f} ms")
            return result
        except Exception as e:
            duration = (time.perf_counter() - start_time) * 1000
            logger.error(
                f"Transaction query execution failed after {duration:.2f} ms: {e}"
            )
            raise

    async def commit(self) -> None:
        logger.info("Committing graph transaction")
        try:
            await self._tx.commit()
            logger.info("Graph transaction committed successfully")
        except Exception as e:
            logger.error(f"Graph transaction commit failed: {e}")
            raise

    async def rollback(self) -> None:
        logger.info("Rolling back graph transaction")
        try:
            await self._tx.rollback()
            logger.info("Graph transaction rolled back successfully")
        except Exception as e:
            logger.error(f"Graph transaction rollback failed: {e}")
            raise

    def __getattr__(self, name: str) -> Any:
        return getattr(self._tx, name)

    async def __aenter__(self) -> WrappedGraphTransaction:
        await self._tx.__aenter__()
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> Any:
        return await self._tx.__aexit__(exc_type, exc_val, exc_tb)


class WrappedGraphSession:
    """
    Wraps a Neo4j session to enforce query validation, transaction auditing, and performance logs.
    """

    def __init__(self, session: Any) -> None:
        self._session = session

    async def run(
        self, query: str, parameters: dict[str, Any] | None = None, **kwargs: Any
    ) -> Any:
        validation_start = time.perf_counter()
        try:
            validate_cypher_query(query)
        except ValueError as e:
            logger.error(f"Query validation failed: {e}")
            raise
        validation_duration = (time.perf_counter() - validation_start) * 1000
        logger.debug(f"Query validation latency: {validation_duration:.3f} ms")

        start_time = time.perf_counter()
        logger.info(f"Executing Cypher query: {query}")
        try:
            result = await self._session.run(query, parameters, **kwargs)
            duration = (time.perf_counter() - start_time) * 1000
            logger.info(f"Query executed successfully in {duration:.2f} ms")
            return result
        except Exception as e:
            duration = (time.perf_counter() - start_time) * 1000
            logger.error(f"Query execution failed after {duration:.2f} ms: {e}")
            raise

    @asynccontextmanager
    async def begin_transaction(self, **kwargs: Any) -> Any:
        logger.info("Beginning graph transaction")
        async with self._session.begin_transaction(**kwargs) as tx:
            wrapped_tx = WrappedGraphTransaction(tx)
            try:
                yield wrapped_tx
                logger.info("Graph transaction completed context successfully")
            except Exception as e:
                logger.error(f"Graph transaction context aborted due to error: {e}")
                raise

    async def execute_read(
        self, transaction_function: Callable, *args: Any, **kwargs: Any
    ) -> Any:
        async def wrapped_tx_func(tx: Any, *tx_args: Any, **tx_kwargs: Any) -> Any:
            wrapped_tx = WrappedGraphTransaction(tx)
            return await transaction_function(wrapped_tx, *tx_args, **tx_kwargs)

        return await self._session.execute_read(wrapped_tx_func, *args, **kwargs)

    async def execute_write(
        self, transaction_function: Callable, *args: Any, **kwargs: Any
    ) -> Any:
        async def wrapped_tx_func(tx: Any, *tx_args: Any, **tx_kwargs: Any) -> Any:
            wrapped_tx = WrappedGraphTransaction(tx)
            return await transaction_function(wrapped_tx, *tx_args, **tx_kwargs)

        return await self._session.execute_write(wrapped_tx_func, *args, **kwargs)

    def __getattr__(self, name: str) -> Any:
        return getattr(self._session, name)

    async def __aenter__(self) -> WrappedGraphSession:
        await self._session.__aenter__()
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> Any:
        return await self._session.__aexit__(exc_type, exc_val, exc_tb)


class GraphDatabaseManager:
    """
    Standardized graph database manager providing connection pooling, session wrappers,
    query safety validations, and optional mock mode fallbacks.
    """

    def __init__(self, service_name: str) -> None:
        self.service_name = service_name
        self._driver: Any = None
        self._mock_mode: bool = False

    def init_db(
        self,
        uri: str | None = None,
        user: str | None = None,
        password: str | None = None,
        mock: bool = False,
    ) -> None:
        """
        Initialize the graph database connection.
        If mock=True, uses mock fallback.
        """
        if mock:
            self._mock_mode = True
            logger.info(
                f"Initialized Mock GraphDatabaseManager for {self.service_name}"
            )
            return

        if self._driver is not None:
            return

        uri = uri or os.getenv("NEO4J_URI")
        user = user or os.getenv("NEO4J_USER")
        password = password or os.getenv("NEO4J_PASSWORD")

        if not uri:
            logger.warning(
                f"Neo4j URI is not configured for {self.service_name}. "
                "Graph database manager initialized in unconfigured state."
            )
            return

        if not NEO4J_AVAILABLE:
            raise RuntimeError(
                f"Neo4j library is not installed, but live connection was requested for {self.service_name}."
            )

        auth = (user, password) if user and password else None
        self._driver = AsyncGraphDatabase.driver(uri, auth=auth)
        logger.info(
            f"Initialized live GraphDatabaseManager for {self.service_name} at {uri}"
        )

    async def close(self) -> None:
        if self._driver:
            await self._driver.close()
            self._driver = None
        self._mock_mode = False

    def get_driver(self) -> Any:
        if self._mock_mode:
            from packages.database.mock_graph import MockGraphDriver

            return MockGraphDriver()

        if not self._driver:
            raise RuntimeError(
                f"Graph database driver is not initialized or configured for {self.service_name}."
            )
        return self._driver

    @asynccontextmanager
    async def get_session(self, **kwargs: Any) -> Any:
        if self._mock_mode:
            from packages.database.mock_graph import MockGraphSession

            yield MockGraphSession()
            return

        driver = self.get_driver()
        async with driver.session(**kwargs) as session:
            yield WrappedGraphSession(session)
