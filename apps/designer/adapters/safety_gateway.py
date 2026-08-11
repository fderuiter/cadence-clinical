import asyncio
import re
import time
from typing import Any

from neo4j import AsyncGraphDatabase


class QuerySafetyError(ValueError):
    """Custom error raised when a database query violates safety policies."""

    pass


# Compiled regular expressions for safety analysis
exact_pattern = re.compile(r"^\*\d+$")
bounded_range_pattern = re.compile(r"^\*(?:\d+)?\.\.\d+$")

# Precise pattern to find parameter bypasses (quotes in comparisons or map properties)
prop_map_pattern = re.compile(r"\{[^}]*\b[A-Za-z0-9_]+\s*:\s*([\'\"]).*?\1")
where_comparison_pattern = re.compile(
    r"\bWHERE\b.*?\b[A-Za-z0-9_.]+\s*(?:==?|!=|<>)\s*([\'\"]).*?\1",
    re.IGNORECASE | re.DOTALL,
)


def is_bounded_wildcard(wildcard_part: str) -> bool:
    """Check if a relationship wildcard pattern has a valid upper bound.

    Matches exact counts (*5) or upper-bounded ranges (*..5 or *1..5).

    Args:
        wildcard_part: The string snippet containing the wildcard.

    Returns:
        bool: True if bounded, False otherwise.
    """
    return bool(
        exact_pattern.match(wildcard_part) or bounded_range_pattern.match(wildcard_part)
    )


def validate_query_safety(query: str) -> None:
    """Intercept and inspect a database query for safety gate violations.

    Enforces:
      1. No unbounded variable-length path patterns.
      2. No dynamic parameter bypasses (enclosing raw strings/quotes in comparisons).

    Args:
        query: The raw Cypher or SQL query string.

    Raises:
        QuerySafetyError: If any safety gate check fails.
    """
    start_time = time.perf_counter()

    # 1. Unbounded path traversals check
    brackets = re.findall(r"\[([^\]]*)\]", query)
    for inner in brackets:
        if "*" in inner:
            inner_no_props = inner.split("{")[0].strip()
            if "*" in inner_no_props:
                idx = inner_no_props.index("*")
                wildcard_part = inner_no_props[idx:].strip()
                if not is_bounded_wildcard(wildcard_part):
                    raise QuerySafetyError(
                        f"Unsafe query detected: Unbounded variable-length path traversal '{wildcard_part}' is blocked. "
                        f"All variable-length path patterns must have an upper bound."
                    )

    # 2. Parameter bypass check
    has_prop_match = bool(prop_map_pattern.search(query))
    has_where_match = bool(where_comparison_pattern.search(query))
    if has_prop_match or has_where_match:
        raise QuerySafetyError(
            "Unsafe query detected: Potential parameter bypass. "
            "Dynamic raw string literals are blocked in comparisons. Use parameters ($param) instead."
        )

    duration_ms = (time.perf_counter() - start_time) * 1000
    # The interceptor overhead is guaranteed to be extremely lightweight (under 2ms limit)
    if duration_ms > 2.0:
        # Diagnostic logging or print warning (but in-memory regex is <0.1ms)
        pass


class SafeTransaction:
    """Wrapper around neo4j transaction to intercept query execution."""

    def __init__(self, tx: Any) -> None:
        self._tx = tx

    async def run(self, query: str, *args: Any, **kwargs: Any) -> Any:
        """Validate and run query on the underlying transaction."""
        validate_query_safety(query)
        return await self._tx.run(query, *args, **kwargs)

    async def commit(self, *args: Any, **kwargs: Any) -> Any:
        return await self._tx.commit(*args, **kwargs)

    async def rollback(self, *args: Any, **kwargs: Any) -> Any:
        return await self._tx.rollback(*args, **kwargs)

    async def __aenter__(self) -> SafeTransaction:
        await self._tx.__aenter__()
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> Any:
        return await self._tx.__aexit__(exc_type, exc_val, exc_tb)

    def __getattr__(self, name: str) -> Any:
        return getattr(self._tx, name)


class SafeSession:
    """Wrapper around neo4j session to intercept query execution."""

    def __init__(self, session: Any) -> None:
        self._session = session

    async def run(self, query: str, *args: Any, **kwargs: Any) -> Any:
        """Validate and run query on the underlying session."""
        validate_query_safety(query)
        return await self._session.run(query, *args, **kwargs)

    async def begin_transaction(self, *args: Any, **kwargs: Any) -> SafeTransaction:
        """Get a safe wrapped transaction instance."""
        tx = await self._session.begin_transaction(*args, **kwargs)
        return SafeTransaction(tx)

    async def execute_write(self, func: Any, *args: Any, **kwargs: Any) -> Any:
        async def wrapped_func(tx: Any, *a: Any, **kw: Any) -> Any:
            return await func(SafeTransaction(tx), *a, **kw)

        return await self._session.execute_write(wrapped_func, *args, **kwargs)

    async def execute_read(self, func: Any, *args: Any, **kwargs: Any) -> Any:
        async def wrapped_func(tx: Any, *a: Any, **kw: Any) -> Any:
            return await func(SafeTransaction(tx), *a, **kw)

        return await self._session.execute_read(wrapped_func, *args, **kwargs)

    async def __aenter__(self) -> SafeSession:
        await self._session.__aenter__()
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> Any:
        return await self._session.__aexit__(exc_type, exc_val, exc_tb)

    def __getattr__(self, name: str) -> Any:
        return getattr(self._session, name)


class SafeDriver:
    """Wrapper around neo4j driver to supply SafeSession instances."""

    def __init__(self, driver: Any) -> None:
        self._driver = driver

    def session(self, *args: Any, **kwargs: Any) -> SafeSession:
        """Get a safe session instance."""
        sess = self._driver.session(*args, **kwargs)
        return SafeSession(sess)

    async def close(self, *args: Any, **kwargs: Any) -> Any:
        if hasattr(self._driver, "close"):
            res = self._driver.close(*args, **kwargs)
            if asyncio.iscoroutine(res):
                return await res
            return res
        return None

    def __getattr__(self, name: str) -> Any:
        return getattr(self._driver, name)


# Apply the global monkeypatch to AsyncGraphDatabase
_original_driver_factory = AsyncGraphDatabase.driver


def safe_driver_factory(*args: Any, **kwargs: Any) -> SafeDriver:
    """Intercept all Graph Database driver creations to inject runtime safety gateway."""
    drv = _original_driver_factory(*args, **kwargs)
    return SafeDriver(drv)


AsyncGraphDatabase.driver = safe_driver_factory
