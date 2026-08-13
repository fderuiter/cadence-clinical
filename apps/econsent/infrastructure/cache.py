import json
import logging
import os
import threading
import time
from typing import Any

import redis

logger = logging.getLogger(__name__)


class ApprovedTranslationCache:
    """
    Thread-safe in-memory cache for composed approved consent template translations.
    Supports TTL, cache hits, invalidation, and stale-on-error fallback.
    """

    def __init__(self, max_size: int = 1000, ttl: float | None = None) -> None:
        self.max_size = max_size
        self._cache: dict[tuple[str, int, str], tuple[dict[str, Any], float]] = {}
        self._lock = threading.Lock()

        if ttl is not None:
            self.ttl = float(ttl)
        else:
            env_ttl = os.getenv("ECONSENT_CACHE_TTL") or os.getenv("CACHE_TTL")
            if env_ttl is not None:
                try:
                    self.ttl = float(env_ttl)
                except ValueError:
                    self.ttl = 3600.0
            else:
                self.ttl = 3600.0

        # Redis configuration for cluster-wide invalidation propagation
        self.redis_host = os.getenv("REDIS_HOST")
        self.redis_port = int(os.getenv("REDIS_PORT", "6379"))
        self.redis_password = os.getenv("REDIS_PASSWORD") or None
        self.redis_channel = os.getenv("REDIS_CHANNEL", "econsent_cache_invalidation")

        self._pub_client = None
        self._sub_thread = None

        if self.redis_host:
            try:
                self._pub_client = redis.Redis(
                    host=self.redis_host,
                    port=self.redis_port,
                    password=self.redis_password,
                    decode_responses=True,
                    socket_timeout=5,
                )
            except Exception as e:
                logger.warning(f"Failed to create Redis publisher client: {e}")

            self._sub_thread = threading.Thread(
                target=self._run_subscriber,
                daemon=True,
                name="EconsentCacheInvalidationSubscriber",
            )
            self._sub_thread.start()

    def _run_subscriber(self) -> None:
        """Background thread logic for listening to Redis invalidation events."""
        while True:
            try:
                r = redis.Redis(
                    host=self.redis_host,
                    port=self.redis_port,
                    password=self.redis_password,
                    decode_responses=True,
                    socket_timeout=5,
                    socket_keepalive=True,
                )
                r.ping()

                pubsub = r.pubsub()
                pubsub.subscribe(self.redis_channel)
                logger.info(
                    f"Subscribed to Redis cache invalidation channel: {self.redis_channel}"
                )

                for message in pubsub.listen():
                    if message["type"] == "message":
                        try:
                            data = json.loads(message["data"])
                            action = data.get("action")
                            if action == "invalidate":
                                template_id = data.get("template_id")
                                version_index = data.get("version_index")
                                language_code = data.get("language_code")
                                if (
                                    template_id is not None
                                    and version_index is not None
                                    and language_code is not None
                                ):
                                    self._local_invalidate(
                                        template_id, int(version_index), language_code
                                    )
                            elif action == "invalidate_template":
                                template_id = data.get("template_id")
                                if template_id is not None:
                                    self._local_invalidate_template(template_id)
                            elif action == "clear":
                                self._local_clear()
                        except Exception as e:
                            logger.warning(
                                f"Failed to process Redis invalidation message: {e}"
                            )
            except Exception as e:
                logger.warning(
                    f"Redis cache invalidation subscriber disconnected or failed to connect: {e}. Retrying in 5 seconds..."
                )
                time.sleep(5)

    def _publish_message(self, payload: dict[str, Any]) -> None:
        """Safely publishes invalidation payload to Redis, ignoring errors with warnings."""
        if not self._pub_client:
            return
        try:
            msg_str = json.dumps(payload)
            self._pub_client.publish(self.redis_channel, msg_str)
        except Exception as e:
            logger.warning(f"Failed to publish invalidation event to Redis: {e}")

    def get_cached(
        self, template_id: str, version_index: int, language_code: str
    ) -> tuple[dict[str, Any] | None, bool]:
        key = (template_id, version_index, language_code)
        now = time.time()
        with self._lock:
            if key in self._cache:
                data, timestamp = self._cache[key]
                if now - timestamp < self.ttl:
                    return data, False
                return data, True
        return None, False

    def set_cached(
        self,
        template_id: str,
        version_index: int,
        language_code: str,
        data: dict[str, Any],
    ) -> None:
        key = (template_id, version_index, language_code)
        with self._lock:
            if len(self._cache) >= self.max_size and key not in self._cache:
                oldest_key = next(iter(self._cache))
                self._cache.pop(oldest_key)
            self._cache[key] = (data, time.time())

    def _local_invalidate(
        self, template_id: str, version_index: int, language_code: str
    ) -> None:
        key = (template_id, version_index, language_code)
        with self._lock:
            if key in self._cache:
                del self._cache[key]

    def invalidate(
        self, template_id: str, version_index: int, language_code: str
    ) -> None:
        self._local_invalidate(template_id, version_index, language_code)
        self._publish_message(
            {
                "action": "invalidate",
                "template_id": template_id,
                "version_index": version_index,
                "language_code": language_code,
            }
        )

    def _local_invalidate_template(self, template_id: str) -> None:
        with self._lock:
            keys_to_del = [k for k in self._cache if k[0] == template_id]
            for k in keys_to_del:
                del self._cache[k]

    def invalidate_template(self, template_id: str) -> None:
        self._local_invalidate_template(template_id)
        self._publish_message(
            {
                "action": "invalidate_template",
                "template_id": template_id,
            }
        )

    def _local_clear(self) -> None:
        with self._lock:
            self._cache.clear()

    def clear(self) -> None:
        self._local_clear()
        self._publish_message(
            {
                "action": "clear",
            }
        )

    def get_status(self) -> dict[str, int]:
        with self._lock:
            return {"size": len(self._cache), "max_size": self.max_size}


async def get_approved_template_translation(
    cache: ApprovedTranslationCache,
    template_id: str,
    version_index: int,
    language_code: str,
    fetch_db_fn,
) -> dict[str, Any]:
    cached_data, is_expired = cache.get_cached(
        template_id, version_index, language_code
    )
    if cached_data is not None and not is_expired:
        return cached_data

    try:
        data = await fetch_db_fn(template_id, version_index, language_code)
    except Exception as e:
        if cached_data is not None:
            return cached_data
        raise e

    if data is not None:
        cache.set_cached(template_id, version_index, language_code, data)
    return data
