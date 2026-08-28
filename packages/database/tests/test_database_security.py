"""Unit test suite for database security and session isolation.

Requirements: PRD-SYS-053, Trace-15
"""

import inspect

from packages.database import DatabaseSessionDependency, RelationalDatabaseManager


def test_relational_database_manager_pool_recycle():
    """Verify that RelationalDatabaseManager configures pool_recycle to 1800 seconds.

    @req:PRD-SYS-053
    @req:Trace-15
    """
    source = inspect.getsource(RelationalDatabaseManager.init_db)
    assert '"pool_recycle": 1800' in source or "'pool_recycle': 1800" in source


def test_database_session_dependency_does_not_set_app_writing():
    """Verify that DatabaseSessionDependency never injects cadence.app_writing bypass into sessions.

    @req:PRD-SYS-053
    @req:Trace-15
    """
    dep_src = inspect.getsource(DatabaseSessionDependency.__call__)
    assert "cadence.app_writing" not in dep_src

    from packages.database import create_transactional_decorator

    tx_src = inspect.getsource(create_transactional_decorator)
    assert "cadence.app_writing" not in tx_src
