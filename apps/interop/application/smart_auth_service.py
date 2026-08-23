"""SMART on FHIR Backend Services Authentication Service.

Implements asymmetric private key JWT OAuth2 client credentials authentication
per HL7 SMART on FHIR Backend Services Authorization specification and RFC 7523.

Requirements: PRD-ESR-001, PRD-SYS-122, ADR-2199, FDA Electronic Source Data Guidance
"""

from __future__ import annotations

import threading
import time
import uuid
from typing import Any

import httpx
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from jose import JWTError, jwt
from pydantic import BaseModel

from apps.interop.domain.esource_models import (
    EhrEndpointConfig,
    SmartTokenResponse,
)


class SmartAuthError(Exception):
    """Base exception for SMART Backend Services authentication errors."""

    def __init__(self, message: str, status_code: int = 400) -> None:
        super().__init__(message)
        self.message = message
        self.status_code = status_code


class SmartTokenCacheEntry(BaseModel):
    """In-memory cache entry for an active SMART OAuth2 access token."""

    access_token: str
    token_type: str = "Bearer"
    expires_at: float
    scope: str


class SmartAuthService:
    """Orchestrates asymmetric private key JWT generation, token exchange, and validation for SMART Backend Services."""

    def __init__(self) -> None:
        self._token_cache: dict[str, SmartTokenCacheEntry] = {}
        self._consumed_jtis: dict[str, float] = {}
        self._lock = threading.Lock()

    @staticmethod
    def generate_rsa_key_pair(key_size: int = 2048) -> tuple[str, str]:
        """Generate a new RSA private/public key pair in PEM format.

        Returns:
            Tuple of (private_key_pem, public_key_pem)
        """
        private_key = rsa.generate_private_key(
            public_exponent=65537,
            key_size=key_size,
        )
        private_pem = private_key.private_bytes(
            encoding=serialization.Encoding.PEM,
            format=serialization.PrivateFormat.PKCS8,
            encryption_algorithm=serialization.NoEncryption(),
        ).decode("utf-8")

        public_pem = (
            private_key.public_key()
            .public_bytes(
                encoding=serialization.Encoding.PEM,
                format=serialization.PublicFormat.SubjectPublicKeyInfo,
            )
            .decode("utf-8")
        )

        return private_pem, public_pem

    def create_client_assertion(
        self,
        client_id: str,
        token_endpoint: str,
        private_key_pem: str,
        algorithm: str = "RS384",
        key_id: str | None = None,
        expiration_seconds: int = 300,
    ) -> str:
        """Create an asymmetric signed client assertion JWT for OAuth2 token exchange.

        Args:
            client_id: Registered OAuth2 client identifier.
            token_endpoint: URL of the target EHR token endpoint (aud claim).
            private_key_pem: RSA private key PEM string.
            algorithm: Asymmetric signing algorithm (RS384 or RS256).
            key_id: Optional key identifier (kid) for JWKS resolution.
            expiration_seconds: Lifetime in seconds (max 300s / 5min per SMART spec).

        Returns:
            Encoded and signed client assertion JWT.
        """
        now = int(time.time())
        exp_bounded = min(expiration_seconds, 300)
        exp = now + exp_bounded
        jti = str(uuid.uuid4())

        headers: dict[str, Any] = {"alg": algorithm, "typ": "JWT"}
        if key_id:
            headers["kid"] = key_id

        payload = {
            "iss": client_id,
            "sub": client_id,
            "aud": token_endpoint,
            "jti": jti,
            "exp": exp,
            "iat": now,
        }

        try:
            return jwt.encode(
                payload, private_key_pem, algorithm=algorithm, headers=headers
            )
        except Exception as exc:
            raise SmartAuthError(
                f"Failed to generate SMART client assertion: {exc}"
            ) from exc

    def verify_client_assertion(
        self,
        assertion_jwt: str,
        public_key_pem: str,
        expected_audience: str | None = None,
        expected_client_id: str | None = None,
        allowed_algorithms: list[str] | None = None,
    ) -> dict[str, Any]:
        """Verify an asymmetric client assertion JWT.

        Validates signature, expiration, audience, issuer-subject equality, and prevents replay attacks via JTI.

        Args:
            assertion_jwt: Raw signed JWT assertion string.
            public_key_pem: RSA public key PEM string.
            expected_audience: Target token endpoint URL.
            expected_client_id: Expected client_id if pinning.
            allowed_algorithms: Permitted asymmetric signing algorithms.

        Returns:
            Decoded claims dict if valid.
        """
        algs = allowed_algorithms or ["RS384", "RS256", "ES384", "ES256"]
        options = {
            "verify_aud": False
        }  # Manual verification with exact or normalized URL

        try:
            claims = jwt.decode(
                assertion_jwt,
                public_key_pem,
                algorithms=algs,
                options=options,
            )
        except JWTError as exc:
            raise SmartAuthError(
                f"SMART assertion signature or decoding validation failed: {exc}",
                status_code=401,
            ) from exc

        # 1. Verify iss == sub
        iss = claims.get("iss")
        sub = claims.get("sub")
        if not iss or not sub or iss != sub:
            raise SmartAuthError(
                "Invalid assertion: 'iss' must equal 'sub' (client_id)", status_code=400
            )

        if expected_client_id and iss != expected_client_id:
            raise SmartAuthError(
                f"Client ID mismatch: expected '{expected_client_id}', got '{iss}'",
                status_code=403,
            )

        # 2. Verify audience
        aud = claims.get("aud")
        if expected_audience:
            if isinstance(aud, list):
                if expected_audience not in aud:
                    raise SmartAuthError(
                        f"Invalid audience: '{expected_audience}' not in {aud}",
                        status_code=400,
                    )
            elif aud != expected_audience:
                raise SmartAuthError(
                    f"Invalid audience: expected '{expected_audience}', got '{aud}'",
                    status_code=400,
                )

        # 3. Verify expiration lifetime <= 5 minutes
        now = time.time()
        iat = claims.get("iat", now)
        exp = claims.get("exp", now)
        if exp < now:
            raise SmartAuthError("SMART client assertion has expired", status_code=401)
        if (exp - iat) > 305:  # 5 min + 5s clock skew
            raise SmartAuthError(
                "SMART client assertion lifetime exceeds maximum 300 seconds",
                status_code=400,
            )

        # 4. Check JTI for replay attack
        jti = claims.get("jti")
        if not jti:
            raise SmartAuthError(
                "Missing required 'jti' claim in SMART client assertion",
                status_code=400,
            )

        with self._lock:
            # Prune expired JTIs
            self._consumed_jtis = {
                k: v for k, v in self._consumed_jtis.items() if v > now
            }
            if jti in self._consumed_jtis:
                raise SmartAuthError(
                    f"Replay attack detected: assertion 'jti' '{jti}' already consumed",
                    status_code=401,
                )
            self._consumed_jtis[jti] = float(exp)

        return claims

    async def exchange_token(
        self,
        token_endpoint: str,
        client_assertion: str,
        scopes: list[str] | None = None,
        client: httpx.AsyncClient | None = None,
    ) -> SmartTokenResponse:
        """Execute OAuth2 client credentials token request using client assertion.

        Args:
            token_endpoint: Target EHR OAuth2 token URL.
            client_assertion: Signed JWT assertion.
            scopes: Requested FHIR scopes.
            client: Optional httpx.AsyncClient instance.

        Returns:
            SmartTokenResponse with access token and metadata.
        """
        requested_scopes = " ".join(scopes) if scopes else "system/*.read"
        form_data = {
            "grant_type": "client_credentials",
            "client_assertion_type": "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            "client_assertion": client_assertion,
            "scope": requested_scopes,
        }

        async def _do_post(http_client: httpx.AsyncClient) -> SmartTokenResponse:
            try:
                response = await http_client.post(
                    token_endpoint,
                    data=form_data,
                    headers={"Accept": "application/json"},
                    timeout=15.0,
                )
            except httpx.RequestError as exc:
                raise SmartAuthError(
                    f"Network error contacting SMART token endpoint '{token_endpoint}': {exc}"
                ) from exc

            if response.status_code != 200:
                error_detail = response.text
                try:
                    error_json = response.json()
                    error_detail = (
                        error_json.get("error_description")
                        or error_json.get("error")
                        or response.text
                    )
                except Exception:
                    pass
                raise SmartAuthError(
                    f"SMART token endpoint returned HTTP {response.status_code}: {error_detail}",
                    status_code=response.status_code,
                )

            data = response.json()
            return SmartTokenResponse(
                access_token=data["access_token"],
                token_type=data.get("token_type", "Bearer"),
                expires_in=data.get("expires_in", 3600),
                scope=data.get("scope", requested_scopes),
            )

        if client is not None:
            return await _do_post(client)

        async with httpx.AsyncClient() as default_client:
            return await _do_post(default_client)

    async def get_authenticated_access_token(
        self,
        endpoint_config: EhrEndpointConfig,
        private_key_pem: str,
        client: httpx.AsyncClient | None = None,
        force_refresh: bool = False,
    ) -> str:
        """Retrieve a valid cached Bearer access token or perform an automated token exchange.

        Args:
            endpoint_config: Hospital EHR endpoint configuration.
            private_key_pem: Private key PEM used to sign client assertions.
            client: Optional httpx async client.
            force_refresh: If True, bypasses token cache.

        Returns:
            Bearer access token string.
        """
        cache_key = f"{endpoint_config.client_id}:{endpoint_config.token_url}"
        now = time.time()

        if not force_refresh:
            with self._lock:
                cached = self._token_cache.get(cache_key)
                if cached and cached.expires_at > (
                    now + 60
                ):  # 60s buffer before expiration
                    return cached.access_token

        # Generate assertion and exchange
        assertion = self.create_client_assertion(
            client_id=endpoint_config.client_id,
            token_endpoint=endpoint_config.token_url,
            private_key_pem=private_key_pem,
            algorithm="RS384",
            expiration_seconds=300,
        )

        token_resp = await self.exchange_token(
            token_endpoint=endpoint_config.token_url,
            client_assertion=assertion,
            scopes=endpoint_config.scopes,
            client=client,
        )

        expires_at = now + token_resp.expires_in
        with self._lock:
            self._token_cache[cache_key] = SmartTokenCacheEntry(
                access_token=token_resp.access_token,
                token_type=token_resp.token_type,
                expires_at=expires_at,
                scope=token_resp.scope,
            )

        return token_resp.access_token

    def clear_cache(self) -> None:
        """Clear all cached access tokens and consumed JTIs."""
        with self._lock:
            self._token_cache.clear()
            self._consumed_jtis.clear()
