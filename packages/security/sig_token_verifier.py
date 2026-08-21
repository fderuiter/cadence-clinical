import os
import threading
import time

import redis
from fastapi import HTTPException
from jose import JWTError, jwt

_redis_client: redis.Redis | None = None
_redis_client_lock = threading.Lock()


def get_redis_client() -> redis.Redis | None:
    """Lazily initialize and return the Redis client if REDIS_URL is configured."""
    global _redis_client
    redis_url = os.getenv("REDIS_URL")
    if not redis_url:
        return None
    if _redis_client is None:
        with _redis_client_lock:
            if _redis_client is None:
                _redis_client = redis.Redis.from_url(redis_url, decode_responses=True)
    return _redis_client


class TokenConsumptionCache:
    """Thread-safe cache to prevent 21 CFR Part 11 signature token replay attacks.

    Stores consumed token keys (JTI or raw token) mapped to their expiration timestamps,
    and automatically prunes expired keys to bound memory footprint.
    """

    def __init__(self) -> None:
        self._consumed: dict[str, float] = {}
        self._lock = threading.Lock()

    def consume(self, token: str, jti: str | None, exp: float) -> bool:
        """Atomically verify and consume a token.

        Args:
            token: The raw JWT token string.
            jti: The unique token JTI UUID claim.
            exp: The token expiration timestamp.

        Returns:
            bool: True if the token is successfully consumed (first time),
                  or False if it has already been consumed (replay blocked).
        """
        key = jti if jti else token
        client = get_redis_client()
        if client is not None:
            redis_key = f"esign_replay:{key}"
            ttl = int(exp - time.time())
            if ttl <= 0:
                ttl = 1
            try:
                res = client.set(redis_key, "1", ex=ttl, nx=True)
                return bool(res)
            except Exception:
                pass

        now = time.time()
        with self._lock:
            # Clean up expired tokens
            self._consumed = {k: e for k, e in self._consumed.items() if e > now}
            if key in self._consumed:
                return False
            self._consumed[key] = exp
            return True

    def reset(self) -> None:
        """Clear the cache.

        Useful for maintaining clean/isolated test state.
        """
        client = get_redis_client()
        if client is not None:
            try:
                keys = client.keys("esign_replay:*")
                if keys:
                    client.delete(*keys)
            except Exception:
                pass
        with self._lock:
            self._consumed.clear()

    clear = reset


token_consumption_cache = TokenConsumptionCache()


def verify_and_consume_sig_token(
    sig_token: str | None,
    expected_user_id: str,
    secret: bytes | None = None,
) -> dict:
    """Centralized 21 CFR Part 11 signature token verifier and single-use consumer.

    Decrypts and validates the signature token against active re-authentication,
    expiration bounds, user identity binding, and single-use constraints.

    Args:
        sig_token: The raw JWT signature token received from headers.
        expected_user_id: The authenticated user ID of the current request session.
        secret: Optional custom HMAC signing secret. Defaults to GATEWAY_SECRET env.

    Returns:
        dict: The decoded signature token JWT payload.

    Raises:
        HTTPException(401): On any signature, expiration, user binding, or replay violation.
    """
    if not sig_token:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    if secret is None:
        secret = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode(  # pragma: allowlist secret
            "utf-8"
        )

    try:
        payload = jwt.decode(sig_token, secret, algorithms=["HS256"])
    except JWTError:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # 1. Temporal Validity (must be unexpired and issued within valid window)
    now = time.time()
    exp = payload.get("exp", 0)
    iat = payload.get("iat", 0)
    if exp < now or (iat > 0 and (now - iat) > 300.5):
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # 2. Signer Identity Binding
    if expected_user_id != "system" and payload.get("sub") != expected_user_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # 3. Single-use Consumption (Replay Prevention)
    jti = payload.get("jti")
    exp = payload.get("exp", 0)
    if not token_consumption_cache.consume(sig_token, jti, exp):
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    return payload
