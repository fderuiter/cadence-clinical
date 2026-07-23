import time
from typing import List

"""
Module for managing automated trial locks and security notifications.
This module intercepts write operations globally or per trial when a security compromise
is detected, while allowing read operations, ensuring data integrity without blocking safety queries.
"""


class NotificationRouter:
    """Routes alerts to designated safety leads and security representatives."""

    def send_email(self, recipients: List[str], message: str):
        """Sends an email notification to the specified recipients."""
        # Simulate email sending
        pass

    def send_sms(self, phone_numbers: List[str], message: str):
        """Sends an SMS notification to the specified phone numbers."""
        # Simulate SMS sending
        pass

    def send_webhook(self, url: str, payload: dict):
        """Sends a webhook payload to the specified URL."""
        # Simulate webhook
        pass


class TrialLockManager:
    """
    Manages the global or trial-specific freeze state and routes alerts.
    """

    _is_locked = False
    _locked_at = None
    _router = NotificationRouter()
    _locked_sites = set()
    _locked_visits = set()

    @classmethod
    def _save_lock_status_to_db(
        cls, lock_type: str, is_locked: bool, target_id: str = None, reason: str = None
    ):
        """Helper to persist lock status to the database synchronously."""
        # Update our in-memory cache as well
        if lock_type == "TRIAL":
            cls._is_locked = is_locked
            if is_locked:
                cls._locked_at = time.time()
            else:
                cls._locked_at = None
        elif lock_type == "SITE":
            if is_locked:
                cls._locked_sites.add(str(target_id))
            else:
                cls._locked_sites.discard(str(target_id))
        elif lock_type == "VISIT":
            if is_locked:
                cls._locked_visits.add(str(target_id))
            else:
                cls._locked_visits.discard(str(target_id))

        # Persist to database
        from sqlalchemy import select

        from apps.execution.database.core import db_manager
        from apps.execution.database.models import TrialLockStatus

        if not db_manager or not db_manager.engine:
            return

        url_str = str(db_manager.engine.url)
        is_memory_sqlite = "sqlite" in url_str and ":memory:" in url_str

        async def _async_save():
            if is_memory_sqlite:
                temp_engine = None
                async_session = db_manager.get_session_maker()()
            else:
                from sqlalchemy.ext.asyncio import (
                    AsyncSession,
                    async_sessionmaker,
                    create_async_engine,
                )
                from sqlalchemy.pool import NullPool

                temp_engine = create_async_engine(
                    db_manager.engine.url, poolclass=NullPool
                )
                temp_session_maker = async_sessionmaker(
                    bind=temp_engine, class_=AsyncSession, expire_on_commit=False
                )
                async_session = temp_session_maker()
            try:
                stmt = select(TrialLockStatus).where(
                    TrialLockStatus.lock_type == lock_type,
                    TrialLockStatus.target_id == target_id,
                )
                res = await async_session.execute(stmt)
                existing = res.scalars().first()
                if existing:
                    existing.is_locked = is_locked
                    if reason:
                        existing.reason = reason
                    from datetime import datetime

                    existing.locked_at = datetime.now()
                else:
                    new_lock = TrialLockStatus(
                        lock_type=lock_type,
                        target_id=target_id,
                        is_locked=is_locked,
                        reason=reason,
                    )
                    async_session.add(new_lock)
                await async_session.commit()
            except Exception as e:
                await async_session.rollback()
                raise e
            finally:
                await async_session.close()
                if temp_engine:
                    await temp_engine.dispose()

        import asyncio
        import threading

        err = []

        def target():
            try:
                loop = asyncio.new_event_loop()
                asyncio.set_event_loop(loop)
                loop.run_until_complete(_async_save())
            except Exception as e:
                err.append(e)
            finally:
                loop.close()

        t = threading.Thread(target=target)
        t.start()
        t.join()
        if err:
            raise err[0]

    @classmethod
    def _sync_cache_from_db(cls):
        """Synchronizes the in-memory lock cache with the database state."""
        from sqlalchemy import select

        from apps.execution.database.core import db_manager
        from apps.execution.database.models import TrialLockStatus

        if not db_manager or not db_manager.engine:
            return

        url_str = str(db_manager.engine.url)
        is_memory_sqlite = "sqlite" in url_str and ":memory:" in url_str

        async def _async_load():
            if is_memory_sqlite:
                temp_engine = None
                async_session = db_manager.get_session_maker()()
            else:
                from sqlalchemy.ext.asyncio import (
                    AsyncSession,
                    async_sessionmaker,
                    create_async_engine,
                )
                from sqlalchemy.pool import NullPool

                temp_engine = create_async_engine(
                    db_manager.engine.url, poolclass=NullPool
                )
                temp_session_maker = async_sessionmaker(
                    bind=temp_engine, class_=AsyncSession, expire_on_commit=False
                )
                async_session = temp_session_maker()
            try:
                stmt = select(TrialLockStatus).where(TrialLockStatus.is_locked)
                res = await async_session.execute(stmt)
                locks = res.scalars().all()
                return locks
            except Exception:
                return None
            finally:
                await async_session.close()
                if temp_engine:
                    await temp_engine.dispose()

        import asyncio
        import threading

        res = []

        def target():
            try:
                loop = asyncio.new_event_loop()
                asyncio.set_event_loop(loop)
                val = loop.run_until_complete(_async_load())
                res.append(val)
            except Exception:
                res.append(None)
            finally:
                loop.close()

        t = threading.Thread(target=target)
        t.start()
        t.join()

        locks = res[0]
        if locks is not None:
            cls._is_locked = False
            cls._locked_at = None
            cls._locked_sites.clear()
            cls._locked_visits.clear()
            for lock in locks:
                if lock.lock_type == "TRIAL":
                    cls._is_locked = True
                    import datetime

                    if isinstance(lock.locked_at, datetime.datetime):
                        cls._locked_at = lock.locked_at.timestamp()
                elif lock.lock_type == "SITE":
                    cls._locked_sites.add(str(lock.target_id))
                elif lock.lock_type == "VISIT":
                    cls._locked_visits.add(str(lock.target_id))

    @classmethod
    def sync_from_session(cls, session):
        """Query persistent database lock status synchronously using the active session."""
        from sqlalchemy import select

        from apps.execution.database.models import TrialLockStatus

        with session.no_autoflush:
            try:
                stmt = select(TrialLockStatus).where(TrialLockStatus.is_locked)
                locks = session.execute(stmt).scalars().all()

                cls._is_locked = False
                cls._locked_at = None
                cls._locked_sites.clear()
                cls._locked_visits.clear()
                for lock in locks:
                    if lock.lock_type == "TRIAL":
                        cls._is_locked = True
                        import datetime

                        if isinstance(lock.locked_at, datetime.datetime):
                            cls._locked_at = lock.locked_at.timestamp()
                    elif lock.lock_type == "SITE":
                        cls._locked_sites.add(str(lock.target_id))
                    elif lock.lock_type == "VISIT":
                        cls._locked_visits.add(str(lock.target_id))
            except Exception:
                pass

    @classmethod
    def lock_site(cls, site_id: str):
        """Locks a specific site by site_id."""
        cls._save_lock_status_to_db("SITE", True, target_id=str(site_id))

    @classmethod
    def unlock_site(cls, site_id: str):
        """Unlocks a specific site by site_id."""
        cls._save_lock_status_to_db("SITE", False, target_id=str(site_id))

    @classmethod
    def is_site_locked(cls, site_id: str) -> bool:
        """Checks if a site is locked."""
        cls._sync_cache_from_db()
        return str(site_id) in cls._locked_sites

    @classmethod
    def lock_visit(cls, visit_id: str):
        """Locks a specific visit by visit_id."""
        cls._save_lock_status_to_db("VISIT", True, target_id=str(visit_id))

    @classmethod
    def unlock_visit(cls, visit_id: str):
        """Unlocks a specific visit by visit_id."""
        cls._save_lock_status_to_db("VISIT", False, target_id=str(visit_id))

    @classmethod
    def is_visit_locked(cls, visit_id: str) -> bool:
        """Checks if a visit is locked."""
        cls._sync_cache_from_db()
        return str(visit_id) in cls._locked_visits

    @classmethod
    def lock_trial(cls, reason: str = "Security violation detected"):
        """Freezes the trial into a read-only state and dispatches alerts."""
        if not cls.is_locked():
            cls._save_lock_status_to_db("TRIAL", True, reason=reason)

            # Dispatch high-priority notifications to designated contacts
            message = f"URGENT: Trial locked. Reason: {reason}"

            cls._router.send_email(
                ["security@cadence.clinical", "safety@cadence.clinical"], message
            )
            cls._router.send_sms(["+1234567890", "+0987654321"], message)
            cls._router.send_webhook(
                "https://hooks.cadence.clinical/alerts", {"text": message}
            )

    @classmethod
    def is_locked(cls) -> bool:
        """Returns True if the trial is currently locked."""
        cls._sync_cache_from_db()
        return cls._is_locked

    @classmethod
    def reset(cls):
        """Resets lock (mostly for testing)."""
        cls._is_locked = False
        cls._locked_at = None
        cls._locked_sites.clear()
        cls._locked_visits.clear()

        from apps.execution.database.core import db_manager
        from apps.execution.database.models import TrialLockStatus

        if not db_manager or not db_manager.engine:
            return

        url_str = str(db_manager.engine.url)
        is_memory_sqlite = "sqlite" in url_str and ":memory:" in url_str

        async def _async_reset():
            if is_memory_sqlite:
                temp_engine = None
                async_session = db_manager.get_session_maker()()
            else:
                from sqlalchemy.ext.asyncio import (
                    AsyncSession,
                    async_sessionmaker,
                    create_async_engine,
                )
                from sqlalchemy.pool import NullPool

                temp_engine = create_async_engine(
                    db_manager.engine.url, poolclass=NullPool
                )
                temp_session_maker = async_sessionmaker(
                    bind=temp_engine, class_=AsyncSession, expire_on_commit=False
                )
                async_session = temp_session_maker()
            try:
                from sqlalchemy import delete

                await async_session.execute(delete(TrialLockStatus))
                await async_session.commit()
            except Exception:
                await async_session.rollback()
            finally:
                await async_session.close()
                if temp_engine:
                    await temp_engine.dispose()

        import asyncio
        import threading

        def target():
            try:
                loop = asyncio.new_event_loop()
                asyncio.set_event_loop(loop)
                loop.run_until_complete(_async_reset())
            except Exception:
                pass
            finally:
                loop.close()

        t = threading.Thread(target=target)
        t.start()
        t.join()
