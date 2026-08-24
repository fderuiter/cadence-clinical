"""SMART on FHIR Backend Services & App Launch Authentication Service.

Implements asymmetric private key JWT OAuth2 client credentials authentication,
SMART on FHIR App Launch flows (EHR & Standalone Launch), OAuth2 PKCE (RFC 7636),
and multi-tenant EHR scoping per HL7 SMART specifications.

Requirements: PRD-ESR-001, PRD-SYS-122, PRD-SYS-185, PRD-SYS-186, ADR-2199, FDA Electronic Source Data Guidance
"""

from __future__ import annotations

import base64
import hashlib
import secrets
import threading
import time
import urllib.parse
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

    @staticmethod
    def generate_pkce_pair(length: int = 64) -> tuple[str, str]:
        """Generate cryptographically secure PKCE code_verifier and S256 code_challenge per RFC 7636.

        Args:
            length: Length of verifier string (between 43 and 128 characters).

        Returns:
            Tuple of (code_verifier, code_challenge).
        """
        clamped_length = max(43, min(length, 128))
        verifier = secrets.token_urlsafe(clamped_length)[:clamped_length]
        digest = hashlib.sha256(verifier.encode("ascii")).digest()
        challenge = base64.urlsafe_b64encode(digest).decode("ascii").rstrip("=")
        return verifier, challenge

    def build_smart_launch_authorize_url(
        self,
        authorize_endpoint: str,
        client_id: str,
        redirect_uri: str,
        scope: str,
        state: str,
        code_challenge: str,
        launch_context: str | None = None,
        aud: str | None = None,
        code_challenge_method: str = "S256",
    ) -> str:
        """Construct SMART on FHIR authorization URL supporting EHR Launch & Standalone Launch flows.

        Args:
            authorize_endpoint: EHR OAuth2 authorization endpoint URL.
            client_id: Registered client identifier.
            redirect_uri: Callback redirect URI.
            scope: Requested FHIR / SMART scopes (e.g. 'launch launch/patient openid fhirUser patient/*.read').
            state: Cryptographic state nonce for CSRF defense.
            code_challenge: Base64url-encoded SHA-256 PKCE code challenge.
            launch_context: Optional launch token provided by EHR in EHR Launch flow.
            aud: FHIR server base URL (required for Standalone Launch flow).
            code_challenge_method: PKCE method ('S256').

        Returns:
            Fully constructed authorization redirect URL string.
        """
        params: dict[str, str] = {
            "response_type": "code",
            "client_id": client_id,
            "redirect_uri": redirect_uri,
            "scope": scope,
            "state": state,
            "code_challenge": code_challenge,
            "code_challenge_method": code_challenge_method,
        }
        if launch_context:
            params["launch"] = launch_context
        if aud:
            params["aud"] = aud

        query_str = urllib.parse.urlencode(params)
        delimiter = "&" if "?" in authorize_endpoint else "?"
        return f"{authorize_endpoint}{delimiter}{query_str}"

    async def exchange_pkce_token(
        self,
        token_endpoint: str,
        client_id: str,
        code: str,
        code_verifier: str,
        redirect_uri: str,
        client_assertion: str | None = None,
        client: httpx.AsyncClient | None = None,
    ) -> SmartTokenResponse:
        """Exchange OAuth2 authorization code with PKCE verifier for SMART access token and launch context.

        Args:
            token_endpoint: EHR OAuth2 token endpoint URL.
            client_id: Registered OAuth2 client identifier.
            code: Authorization code received from authorization callback.
            code_verifier: PKCE code verifier matching the initial code_challenge.
            redirect_uri: Exact redirect URI used in the authorization request.
            client_assertion: Optional asymmetric client assertion JWT (if confidential client).
            client: Optional httpx async client.

        Returns:
            SmartTokenResponse with access token, scopes, and SMART launch context (patient, encounter, tenant_id).
        """
        form_data: dict[str, str] = {
            "grant_type": "authorization_code",
            "code": code,
            "code_verifier": code_verifier,
            "redirect_uri": redirect_uri,
            "client_id": client_id,
        }
        if client_assertion:
            form_data["client_assertion_type"] = (
                "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
            )
            form_data["client_assertion"] = client_assertion

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
                scope=data.get("scope", "patient/*.read"),
                patient=data.get("patient"),
                encounter=data.get("encounter"),
                tenant_id=data.get("tenant_id") or data.get("tenant"),
                need_patient_banner=data.get("need_patient_banner"),
                smart_style_url=data.get("smart_style_url"),
                id_token=data.get("id_token"),
            )

        if client is not None:
            return await _do_post(client)

        async with httpx.AsyncClient() as default_client:
            return await _do_post(default_client)

    @staticmethod
    def validate_ehr_tenant_scoping(
        issuer_url: str,
        allowed_tenants: list[str] | None = None,
        required_tenant_id: str | None = None,
    ) -> bool:
        """Verify EHR tenant identifier or issuer URL against allowed tenant scopes.

        Args:
            issuer_url: Target EHR FHIR server URL or OAuth issuer.
            allowed_tenants: Whitelist of allowed EHR tenant URLs or IDs.
            required_tenant_id: Specific expected tenant ID for strict pinning.

        Returns:
            True if tenant validation passes, raises SmartAuthError otherwise.
        """
        if not issuer_url:
            raise SmartAuthError(
                "EHR issuer URL is missing for tenant scoping validation",
                status_code=400,
            )

        norm_iss = issuer_url.strip().rstrip("/")
        if allowed_tenants:
            norm_allowed = [t.strip().rstrip("/") for t in allowed_tenants]
            if norm_iss not in norm_allowed:
                raise SmartAuthError(
                    f"EHR issuer '{issuer_url}' not authorized in tenant whitelist: {allowed_tenants}",
                    status_code=403,
                )

        if required_tenant_id and required_tenant_id not in norm_iss:
            raise SmartAuthError(
                f"EHR issuer '{issuer_url}' does not match required tenant ID '{required_tenant_id}'",
                status_code=403,
            )

        return True

    def clear_cache(self) -> None:
        """Clear all cached access tokens and consumed JTIs."""
        with self._lock:
            self._token_cache.clear()
            self._consumed_jtis.clear()
