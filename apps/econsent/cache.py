import os
import threading
import time
from typing import Any, Dict, Optional, Tuple


class ApprovedTranslationCache:
    """
    Thread-safe in-memory cache for composed approved consent template translations.
    Supports TTL, cache hits, invalidation, and stale-on-error fallback.
    """

    def __init__(self, max_size: int = 1000, ttl: Optional[float] = None) -> None:
        self.max_size = max_size
        self._cache: Dict[Tuple[str, int, str], Tuple[Dict[str, Any], float]] = {}
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

    def get_cached(
        self, template_id: str, version_index: int, language_code: str
    ) -> Tuple[Optional[Dict[str, Any]], bool]:
        """
        Retrieves the item from cache. Returns (data, is_expired).
        """
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
        data: Dict[str, Any],
    ) -> None:
        key = (template_id, version_index, language_code)
        with self._lock:
            if len(self._cache) >= self.max_size and key not in self._cache:
                # Evict oldest entry
                oldest_key = next(iter(self._cache))
                self._cache.pop(oldest_key)
            self._cache[key] = (data, time.time())

    def invalidate(
        self, template_id: str, version_index: int, language_code: str
    ) -> None:
        key = (template_id, version_index, language_code)
        with self._lock:
            if key in self._cache:
                del self._cache[key]

    def invalidate_template(self, template_id: str) -> None:
        with self._lock:
            keys_to_del = [k for k in self._cache.keys() if k[0] == template_id]
            for k in keys_to_del:
                del self._cache[k]

    def clear(self) -> None:
        with self._lock:
            self._cache.clear()

    def get_status(self) -> Dict[str, int]:
        with self._lock:
            return {"size": len(self._cache), "max_size": self.max_size}


async def get_approved_template_translation(
    cache: ApprovedTranslationCache,
    template_id: str,
    version_index: int,
    language_code: str,
    fetch_db_fn,  # An async function: fetch_db_fn(template_id, version_index, language_code) -> Dict[str, Any]
) -> Dict[str, Any]:
    """
    Read-through cache mechanism that implements stale-on-error fallback.
    """
    # 1. Check cache
    cached_data, is_expired = cache.get_cached(
        template_id, version_index, language_code
    )
    if cached_data is not None and not is_expired:
        return cached_data

    # 2. If miss or expired, fetch from DB
    try:
        data = await fetch_db_fn(template_id, version_index, language_code)
    except Exception as e:
        if cached_data is not None:
            # Stale-on-error fallback: return the expired/stale entry
            return cached_data
        raise e

    if data is not None:
        cache.set_cached(template_id, version_index, language_code, data)
    return data
