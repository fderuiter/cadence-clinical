import asyncio
import logging
import os
from typing import Any, Optional

logger = logging.getLogger("dispatcher")

_dispatch_task: Optional[asyncio.Task] = None
_should_run: bool = False


async def start_background_dispatcher(
    session_maker: Any, interval: Optional[float] = None
) -> None:
    """
    Starts the asynchronous background notifications dispatcher loop.
    Reads interval from NOTIFICATION_DISPATCH_INTERVAL_SECONDS env var if not specified.
    """
    global _dispatch_task, _should_run
    if _dispatch_task:
        return

    if interval is None:
        interval = float(os.getenv("NOTIFICATION_DISPATCH_INTERVAL_SECONDS", "5.0"))

    _should_run = True

    async def dispatcher_loop():
        logger.info(
            "Background notifications dispatcher started with interval %s seconds.",
            interval,
        )
        from apps.notifications.main import poll_and_dispatch

        while _should_run:
            try:
                await poll_and_dispatch()
            except Exception as e:
                logger.error(
                    "Error in background notifications dispatcher cycle: %s",
                    e,
                    exc_info=True,
                )

            # Cancellation-friendly incremental sleep modeled on apps/execution/database/sealer.py
            for _ in range(int(interval * 10)):
                if not _should_run:
                    break
                await asyncio.sleep(0.1)

    _dispatch_task = asyncio.create_task(dispatcher_loop())


async def stop_background_dispatcher() -> None:
    """
    Stops the background notifications dispatcher loop.
    """
    global _dispatch_task, _should_run
    _should_run = False
    if _dispatch_task:
        _dispatch_task.cancel()
        try:
            await _dispatch_task
        except asyncio.CancelledError:
            pass
        _dispatch_task = None
    logger.info("Background notifications dispatcher stopped.")
