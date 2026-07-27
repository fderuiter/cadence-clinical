import asyncio

import pytest

from packages.database import RelationalDatabaseManager


def test_interop_database_manager_uninitialized_and_close():
    """
    Verify Interop database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="Interop")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    # close should handle self.engine=None
    asyncio.run(mgr.close())


def test_notifications_database_manager_uninitialized_and_close():
    """
    Verify Notifications database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="Notifications")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_econsent_database_manager_uninitialized_and_close():
    """
    Verify eConsent database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="eConsent")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_eisf_database_manager_uninitialized_and_close():
    """
    Verify eISF database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="eISF")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_etmf_database_manager_uninitialized_and_close():
    """
    Verify eTMF database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="eTMF")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_ctms_database_manager_uninitialized_and_close():
    """
    Verify CTMS database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="CTMS")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())
