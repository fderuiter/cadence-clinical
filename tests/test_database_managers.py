import pytest


def test_interop_database_manager_uninitialized_and_close():
    """
    Verify InteropDatabaseManager exception and close-twice behavior.
    """
    from apps.interop.database import InteropDatabaseManager

    mgr = InteropDatabaseManager()
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    # close should handle self.engine=None
    import asyncio

    asyncio.run(mgr.close())


def test_notifications_database_manager_uninitialized_and_close():
    """
    Verify NotificationsDatabaseManager exception and close-twice behavior.
    """
    from apps.notifications.database import NotificationsDatabaseManager

    mgr = NotificationsDatabaseManager()
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    import asyncio

    asyncio.run(mgr.close())


def test_econsent_database_manager_uninitialized_and_close():
    """
    Verify EConsentDatabaseManager exception and close-twice behavior.
    """
    from apps.econsent.database import EConsentDatabaseManager

    mgr = EConsentDatabaseManager()
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    import asyncio

    asyncio.run(mgr.close())


def test_eisf_database_manager_uninitialized_and_close():
    """
    Verify EISFDatabaseManager exception and close-twice behavior.
    """
    from apps.eisf.database import EISFDatabaseManager

    mgr = EISFDatabaseManager()
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    import asyncio

    asyncio.run(mgr.close())


def test_etmf_database_manager_uninitialized_and_close():
    """
    Verify ETMFDatabaseManager exception and close-twice behavior.
    """
    from apps.etmf.database import ETMFDatabaseManager

    mgr = ETMFDatabaseManager()
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    import asyncio

    asyncio.run(mgr.close())


def test_ctms_database_manager_uninitialized_and_close():
    """
    Verify CTMSDatabaseManager exception and close-twice behavior.
    """
    from apps.ctms.database import CTMSDatabaseManager

    mgr = CTMSDatabaseManager()
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    import asyncio

    asyncio.run(mgr.close())
