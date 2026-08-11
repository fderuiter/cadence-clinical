import pytest

from apps.etmf.infrastructure.lock_client import (
    register_trial_lock_status_resolver,
    register_trial_lock_trigger_handler,
)


@pytest.fixture(autouse=True)
def setup_etmf_test_lock_adapters(request):
    """
    Autouse fixture to register clean Port-and-Adapter hooks during testing,
    avoiding the need for in-memory module injection/sys.modules hacks.
    """
    if "test_etmf_lock_integration.py" in str(request.node.fspath):
        yield
        return

    try:
        from apps.execution.trial_lock import TrialLockManager

        async def resolve_lock_status() -> bool:
            return TrialLockManager.is_locked()

        async def trigger_lock(reason: str) -> None:
            TrialLockManager.lock_trial()

        register_trial_lock_status_resolver(resolve_lock_status)
        register_trial_lock_trigger_handler(trigger_lock)
    except ImportError:
        pass

    yield

    # Reset adapters after each test
    register_trial_lock_status_resolver(None)
    register_trial_lock_trigger_handler(None)
