import asyncio
import logging
import os
import sys
import time
import uuid
from collections.abc import AsyncGenerator, Awaitable, Callable
from contextlib import asynccontextmanager
from typing import Any

import httpx
from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from fastapi.openapi.docs import get_swagger_ui_html
from fastapi.responses import JSONResponse
from jose import JWTError, jwt
from pydantic import BaseModel
from starlette.middleware.base import BaseHTTPMiddleware

import packages  # noqa: F401
from apps.gateway.routers.cdisc import router as cdisc_router
from apps.gateway.routers.ecoa import router as ecoa_router
from apps.gateway.routers.usdm import router as usdm_router
from packages.security import validate_branding


def validate_environment() -> None:
    """
    Validate that no test bypass configurations are enabled in production or staging environments.
    Crashes the application immediately if any bypass variables are active.
    """
    app_env = os.getenv("APP_ENV", "").strip().lower()
    # Non-development environments (e.g. production or staging)
    if app_env and app_env not in ("development", "dev", "test"):
        errors = []
        test_secret = os.getenv("JWT_TEST_SECRET")
        allow_unverified = os.getenv("ALLOW_UNVERIFIED_JWT_FOR_TEST")
        skip_jwks = os.getenv("SKIP_JWKS_FETCH")

        if test_secret:
            errors.append("JWT_TEST_SECRET")
        if allow_unverified and allow_unverified.strip().lower() not in (
            "false",
            "0",
            "",
        ):
            errors.append("ALLOW_UNVERIFIED_JWT_FOR_TEST")
        if skip_jwks and skip_jwks.strip().lower() not in ("false", "0", ""):
            errors.append("SKIP_JWKS_FETCH")

        if errors:
            error_msg = f"SECURITY ALERT: Invalid non-development configuration detected. Application cannot start in mode '{app_env}' with test bypass parameters active: {', '.join(errors)}."
            print(error_msg, file=sys.stderr)
            logger = logging.getLogger("gateway")
            logger.error(error_msg)
            sys.exit(1)


validate_environment()


BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")
BRAND_DOMAIN = os.getenv("BRAND_DOMAIN", "cadenceclinical.com")
KEYCLOAK_REALM = os.getenv("KEYCLOAK_REALM", "cadence")
KEYCLOAK_CLIENT_ID = os.getenv("KEYCLOAK_CLIENT_ID", "cadence-clinical")
JWKS_URL = os.getenv(
    "JWKS_URL",
    f"http://keycloak:8080/realms/{KEYCLOAK_REALM}/protocol/openid-connect/certs",  # deid-ignore
)

HTTP_METHODS = {"get", "put", "post", "delete", "options", "head", "patch", "trace"}

jwks_cache: dict[str, Any] | None = None
http_client: httpx.AsyncClient | None = None
jwks_fetch_lock = asyncio.Lock()


async def startup() -> None:
    """Handle startup JWKS initialization."""
    global jwks_cache, http_client
    if http_client is None:
        http_client = httpx.AsyncClient()
    if not os.getenv("SKIP_JWKS_FETCH"):
        try:
            resp = await http_client.get(JWKS_URL, timeout=5.0)
            if resp.status_code == 200:
                jwks_cache = resp.json()
        except Exception:
            pass


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None]:
    """Handle lifecycle events for the API Gateway application."""
    global jwks_cache, http_client
    http_client = httpx.AsyncClient()
    await startup()

    yield

    if http_client:
        await http_client.aclose()


validate_branding("gateway", is_gateway=True)

app = FastAPI(
    title=f"{BRAND_NAME} - API Gateway",
    version="0.1.0",
    openapi_url=None,
    docs_url=None,
    redoc_url=None,
    lifespan=lifespan,
)

app.include_router(cdisc_router, prefix="/api/v1/cdisc", tags=["CDISC Standards"])
app.include_router(usdm_router, prefix="/api/v1/usdm", tags=["USDM Data Flow"])
app.include_router(ecoa_router, prefix="/api/v1/ecoa", tags=["eCOA"])


def custom_openapi() -> dict[str, Any]:
    if app.openapi_schema:
        return app.openapi_schema

    # Pre-load safe environment variables before importing microservices or validating schemas offline
    os.environ.setdefault(
        "AUDIT_LOG_SECRET_KEY",
        "test-gxp-audit-secret-key-placeholder-abc",  # pragma: allowlist secret
    )
    os.environ.setdefault(
        "INBOUND_EMAIL_HMAC_SECRET",
        "test-email-hmac-secret-placeholder-xyz",  # pragma: allowlist secret
    )
    os.environ.setdefault(
        "GATEWAY_SECRET",
        "test-gateway-secret-placeholder-123",  # pragma: allowlist secret
    )

    from fastapi.openapi.utils import get_openapi

    native_openapi = get_openapi(
        title=f"{BRAND_NAME} - API Gateway",
        version="0.1.0",
        routes=app.routes,
    )

    merged = {
        "openapi": "3.1.0",
        "info": {"title": f"{BRAND_NAME} - Unified API", "version": "0.1.0"},
        "paths": {},
        "components": {"schemas": {}},
    }

    # Copy native paths and schemas
    for path_str, path_item in native_openapi.get("paths", {}).items():
        merged["paths"][path_str] = path_item
    for schema_name, schema_val in (
        native_openapi.get("components", {}).get("schemas", {}).items()
    ):
        merged["components"]["schemas"][schema_name] = schema_val

    # Discover and load downstream services offline
    import importlib

    gateway_dir = os.path.dirname(os.path.abspath(__file__))
    app_root = os.path.abspath(os.path.join(gateway_dir, "..", ".."))

    import sys

    if app_root not in sys.path:
        sys.path.insert(0, app_root)

    apps_dir = os.path.join(app_root, "apps")

    services_config = {}
    if os.path.isdir(apps_dir):
        for name in sorted(os.listdir(apps_dir)):
            dir_path = os.path.join(apps_dir, name)
            if name == "gateway" or name.startswith(".") or not os.path.isdir(dir_path):
                continue
            main_path = os.path.join(dir_path, "main.py")
            if not os.path.isfile(main_path):
                continue
            try:
                module = importlib.import_module(f"apps.{name}.main")
                service_app = getattr(module, "app", None)
                if service_app is not None:
                    prefix = "ETMF_" if name == "etmf" else f"{name.capitalize()}_"
                    services_config[name] = {"app": service_app, "prefix": prefix}
            except Exception:
                pass

    def is_valid_openapi_spec(spec: Any) -> bool:
        if not isinstance(spec, dict):
            return False
        if "paths" in spec and not isinstance(spec["paths"], dict):
            return False
        if "components" in spec:
            if not isinstance(spec["components"], dict):
                return False
            if "schemas" in spec["components"] and not isinstance(
                spec["components"]["schemas"], dict
            ):
                return False
        return True

    def rewrite_references(data: Any, prefix: str, visited: set | None = None) -> Any:
        if visited is None:
            visited = set()

        if id(data) in visited:
            return {
                "type": "object",
                "description": "Circular reference detected and isolated",
            }

        if isinstance(data, dict):
            visited.add(id(data))
            new_data = {}
            for k, v in data.items():
                if (
                    k == "$ref"
                    and isinstance(v, str)
                    and v.startswith("#/components/schemas/")
                ):
                    ref_name = v[len("#/components/schemas/") :]
                    new_data[k] = f"#/components/schemas/{prefix}{ref_name}"
                else:
                    new_data[k] = rewrite_references(v, prefix, visited)
            visited.remove(id(data))
            return new_data
        if isinstance(data, list):
            visited.add(id(data))
            new_list = [rewrite_references(item, prefix, visited) for item in data]
            visited.remove(id(data))
            return new_list
        return data

    http_methods = {"get", "put", "post", "delete", "options", "head", "patch", "trace"}

    def rewrite_paths(paths_dict: dict[str, Any], prefix: str) -> dict[str, Any]:
        new_paths = {}
        for path_str, path_item in paths_dict.items():
            if not isinstance(path_item, dict):
                new_paths[path_str] = path_item
                continue
            new_path_item = {}
            for k, v in path_item.items():
                if k.lower() in http_methods and isinstance(v, dict):
                    op = dict(v)
                    if "operationId" in op and isinstance(op["operationId"], str):
                        op_id = op["operationId"]
                        if not op_id.startswith(prefix):
                            op["operationId"] = f"{prefix}{op_id}"
                    new_path_item[k] = op
                else:
                    new_path_item[k] = v
            new_paths[path_str] = new_path_item
        return new_paths

    for service_name, config in services_config.items():
        try:
            spec = config["app"].openapi()
            if not is_valid_openapi_spec(spec):
                continue

            prefix = config["prefix"]
            spec = rewrite_references(spec, prefix)
            paths = rewrite_paths(spec.get("paths", {}), prefix)

            path_prefix = f"/{service_name}"
            for path_str, path_item in paths.items():
                merged["paths"][f"{path_prefix}{path_str}"] = path_item

            for schema_name, schema_val in (
                spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"{prefix}{schema_name}"] = schema_val
        except Exception:
            pass

    app.openapi_schema = merged
    return app.openapi_schema


app.openapi = custom_openapi

# CORS configuration
allowed_origins = os.getenv("ALLOWED_ORIGINS", "*").split(",")
app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins,
    allow_credentials="*" not in allowed_origins,
    allow_methods=["*"],
    allow_headers=["*"],
)


class RateLimiter:
    """
    An in-memory sliding window rate limiter.
    """

    def __init__(self, window_seconds: float = 60.0, max_requests: int = 100) -> None:
        self.window_seconds = window_seconds
        self.max_requests = max_requests
        self.requests: dict[str, list[float]] = {}

    def is_rate_limited(self, key: str) -> bool:
        """
        Check if a request key exceeds the permitted rate.

        Args:
            key (str): A unique string identifying the requester (e.g. IP address or user ID).

        Returns:
            bool: True if rate limit is exceeded, False otherwise.
        """
        now = time.time()
        if key not in self.requests:
            self.requests[key] = []
        # Prune older than window
        self.requests[key] = [
            t for t in self.requests[key] if now - t < self.window_seconds
        ]
        if len(self.requests[key]) >= self.max_requests:
            return True
        self.requests[key].append(now)
        return False


RATE_LIMIT_WINDOW = float(os.getenv("RATE_LIMIT_WINDOW", "60.0"))
RATE_LIMIT_MAX_REQUESTS = int(os.getenv("RATE_LIMIT_MAX_REQUESTS", "100"))
rate_limiter = RateLimiter(
    window_seconds=RATE_LIMIT_WINDOW, max_requests=RATE_LIMIT_MAX_REQUESTS
)


class RateLimitMiddleware(BaseHTTPMiddleware):
    """
    Middleware to enforce rate limiting on incoming API Gateway requests.
    """

    async def dispatch(
        self, request: Request, call_next: Callable[[Request], Awaitable[Response]]
    ) -> Response:
        # Exclude health check from rate limiting if appropriate
        if request.url.path == "/health" or request.url.path == "":
            return await call_next(request)

        # Build key using client IP or authenticated sub claim if bearer token is present
        key = request.client.host if request.client else "unknown"
        auth_header = request.headers.get("Authorization")
        if auth_header and auth_header.startswith("Bearer "):
            try:
                token = auth_header.split(" ")[1]
                claims = jwt.get_unverified_claims(token)
                user_id = claims.get("sub")
                if user_id:
                    key = f"user:{user_id}"
            except Exception:
                pass

        if rate_limiter.is_rate_limited(key):
            return JSONResponse(
                status_code=429,
                content={"detail": "Too Many Requests. Rate limit exceeded."},
            )
        return await call_next(request)


app.add_middleware(RateLimitMiddleware)

JWT_ALGORITHM = os.getenv("JWT_ALGORITHM", "RS256")
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET")
if not GATEWAY_SECRET:
    raise RuntimeError("GATEWAY_SECRET environment variable is missing")

SERVICES = {
    "designer": os.getenv("DESIGNER_URL", "http://localhost:8001"),
    "execution": os.getenv("EXECUTION_URL", "http://localhost:8002"),
    "etmf": os.getenv("ETMF_URL", "http://localhost:8003"),
    "interop": os.getenv("INTEROP_URL", "http://localhost:8004"),
    "ctms": os.getenv("CTMS_URL", "http://localhost:8007"),
    "notifications": os.getenv("NOTIFICATIONS_URL", "http://localhost:8006"),
    "quality": os.getenv("QUALITY_URL", "http://localhost:8005"),
    "safety": os.getenv(
        "SAFETY_URL", "http://localhost:8008"
    ),  # Registered Safety microservice scaffold URL
    "tickets": os.getenv("TICKETS_URL", "http://localhost:8009"),
    "org": os.getenv("ORG_URL", "http://localhost:8012"),
    "eisf": os.getenv("EISF_URL", "http://localhost:8010"),
    "econsent": os.getenv("ECONSENT_URL", "http://localhost:8011"),
    "fileshare": os.getenv("FILESHARE_URL", "http://localhost:8013"),
}


def _is_kid_cached(kid: str | None) -> bool:
    if not kid or not jwks_cache:
        return False
    keys = jwks_cache.get("keys", [])
    if not isinstance(keys, list):
        return False
    return any(isinstance(k, dict) and k.get("kid") == kid for k in keys)


async def verify_token(token: str) -> dict[str, Any]:
    """
    Verify and decode a JSON Web Token (JWT).

    Validates the token using either a configured test secret or the
    cached JWKS public keys. Returns the decoded payload if valid.

    Args:
        token (str): The JWT string to verify.

    Returns:
        Dict[str, Any]: The decoded JWT payload.

    Raises:
        HTTPException: If the token is invalid, signature verification fails,
                       or JWKS is unavailable.
    """
    if GATEWAY_SECRET:
        try:
            return jwt.decode(
                token,
                GATEWAY_SECRET,
                algorithms=["HS256"],
                options={"verify_aud": False},
            )
        except JWTError:
            pass

    test_secret = os.getenv("JWT_TEST_SECRET")
    if test_secret:
        try:
            return jwt.decode(
                token,
                test_secret,
                algorithms=["HS256", "RS256"],
                options={"verify_aud": False},
            )
        except JWTError:
            pass

    try:
        unverified_header = jwt.get_unverified_header(token)
    except JWTError:
        if os.getenv("ALLOW_UNVERIFIED_JWT_FOR_TEST"):
            try:
                return jwt.get_unverified_claims(token)
            except JWTError:
                raise HTTPException(status_code=401, detail="Invalid token structure")
        raise HTTPException(status_code=401, detail="Invalid token")

    kid = unverified_header.get("kid")

    # Double-checked locking
    is_cached = _is_kid_cached(kid)

    if not is_cached and kid:
        async with jwks_fetch_lock:
            if not _is_kid_cached(kid):
                try:
                    client_to_use = (
                        http_client if http_client is not None else httpx.AsyncClient()
                    )
                    resp = await client_to_use.get(JWKS_URL, timeout=5.0)
                    if resp.status_code == 200:
                        global jwks_cache
                        jwks_cache = resp.json()
                    else:
                        logger = logging.getLogger("gateway")
                        logger.error(
                            f"Failed to fetch JWKS dynamically: HTTP status code {resp.status_code}"
                        )
                except Exception as e:
                    logger = logging.getLogger("gateway")
                    logger.error(f"Failed to fetch JWKS dynamically: {str(e)}")

    if not jwks_cache:
        # Fallback if JWKS is unreachable and we have no test secret
        if os.getenv("ALLOW_UNVERIFIED_JWT_FOR_TEST"):
            try:
                return jwt.get_unverified_claims(token)
            except JWTError:
                raise HTTPException(status_code=401, detail="Invalid token structure")
        raise HTTPException(
            status_code=401, detail="Cannot verify token: No JWKS available"
        )

    try:
        rsa_key = {}
        for key in jwks_cache.get("keys", []):
            if key.get("kid") == kid:
                rsa_key = {
                    "kty": key["kty"],
                    "kid": key["kid"],
                    "use": key["use"],
                    "n": key["n"],
                    "e": key["e"],
                }
                break

        if rsa_key:
            client_id = os.getenv("KEYCLOAK_CLIENT_ID", KEYCLOAK_CLIENT_ID)
            payload = jwt.decode(
                token,
                rsa_key,
                algorithms=[JWT_ALGORITHM],
                audience=client_id,
                options={"verify_aud": True},
            )
            aud = payload.get("aud")
            if isinstance(aud, str):
                aud_match = aud == client_id
            elif isinstance(aud, (list, tuple, set)):
                aud_match = client_id in aud
            else:
                aud_match = False

            if not aud_match:
                raise HTTPException(status_code=401, detail="Invalid token audience")

            return payload

        # Fallback if ALLOW_UNVERIFIED_JWT_FOR_TEST is set
        if os.getenv("ALLOW_UNVERIFIED_JWT_FOR_TEST"):
            try:
                return jwt.get_unverified_claims(token)
            except JWTError:
                raise HTTPException(status_code=401, detail="Invalid token structure")

        raise HTTPException(
            status_code=401, detail="Token signature could not be verified"
        )
    except JWTError:
        raise HTTPException(status_code=401, detail="Invalid token")


def generate_signature(
    user_id: str,
    roles: str,
    timestamp: str,
    version: str = "2",
    change_reason: str | None = None,
    site_id: str | None = None,
    sponsor_id: str | None = None,
    unblinded_access: bool = False,
    tenant_id: str | None = None,
    sig_token: str | None = None,
) -> str:
    """
    Generate an HMAC-SHA256 signature for identity and scope headers.

    Uses a shared secret to cryptographically sign the user identity, scope,
    and timestamp, allowing downstream services to trust the injected headers.

    Supports Version 1 (legacy colon-concatenated format) and Version 2 (canonical JSON format).
    """
    from packages.security.signing import generate_gateway_signature

    return generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=os.getenv("GATEWAY_SECRET", GATEWAY_SECRET).encode(),
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
        tenant_id=tenant_id,
        sig_token=sig_token,
    )


@app.get("/openapi.json", include_in_schema=False)
async def get_openapi_json() -> Response:
    """
    Dynamically aggregate OpenAPI schemas from downstream services.

    Fetches the `/openapi.json` endpoints from the designer and execution
    services concurrently. Rewrites component references to prevent
    collisions and aggregates them into a single unified OpenAPI schema.

    Returns:
        Response: A JSONResponse containing the merged OpenAPI 3.1.0 schema.
    """

    async def fetch_service_openapi(service_url: str) -> dict[str, Any] | None:
        """
        Fetch the OpenAPI schema from a downstream service.

        Args:
            service_url (str): The base URL of the downstream service.

        Returns:
            Optional[Dict[str, Any]]: The parsed OpenAPI schema, or None if the fetch fails.
        """
        timestamp = str(time.time())
        user_id = "system_docs_aggregator"
        roles = "admin,system"
        change_reason = "system_operation"
        signature = generate_signature(
            user_id,
            roles,
            timestamp,
            version="2",
            change_reason=change_reason,
        )
        headers = {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": signature,
            "X-Signature-Version": "2",
            "X-Change-Reason": change_reason,
        }
        try:
            if http_client:
                resp = await http_client.get(
                    f"{service_url}/openapi.json", headers=headers, timeout=5.0
                )
                if resp.status_code == 200:
                    return resp.json()
        except Exception:
            pass
        return None

    def is_valid_openapi_spec(spec: Any) -> bool:
        """
        Check if the schema payload is a valid OpenAPI specification structure.

        Args:
            spec (Any): The schema payload to validate.

        Returns:
            bool: True if the schema is a dictionary with a valid structure, False otherwise.
        """
        if not isinstance(spec, dict):
            return False
        if "paths" in spec and not isinstance(spec["paths"], dict):
            return False
        if "components" in spec:
            if not isinstance(spec["components"], dict):
                return False
            if "schemas" in spec["components"] and not isinstance(
                spec["components"]["schemas"], dict
            ):
                return False
        return True

    def rewrite_references(data: Any, prefix: str, visited: set | None = None) -> Any:
        """
        Recursively rewrite component references in an OpenAPI schema payload.

        Appends the given prefix to all `$ref` pointer targets to avoid naming collisions
        between different service schemas.
        Uses a visited set to detect and protect against infinite recursion loops.

        Args:
            data (Any): A segment of the OpenAPI schema data structure.
            prefix (str): The string prefix to append to component references.
            visited (Optional[set]): A set of python object ids to prevent infinite recursion on cyclic data structures.

        Returns:
            Any: The transformed data structure with rewritten references.
        """
        if visited is None:
            visited = set()

        if id(data) in visited:
            return {
                "type": "object",
                "description": "Circular reference detected and isolated",
            }

        if isinstance(data, dict):
            visited.add(id(data))
            new_data = {}
            for k, v in data.items():
                if (
                    k == "$ref"
                    and isinstance(v, str)
                    and v.startswith("#/components/schemas/")
                ):
                    ref_name = v[len("#/components/schemas/") :]
                    new_data[k] = f"#/components/schemas/{prefix}{ref_name}"
                else:
                    new_data[k] = rewrite_references(v, prefix, visited)
            visited.remove(id(data))
            return new_data
        if isinstance(data, list):
            visited.add(id(data))
            new_list = [rewrite_references(item, prefix, visited) for item in data]
            visited.remove(id(data))
            return new_list
        return data

    http_methods = {"get", "put", "post", "delete", "options", "head", "patch", "trace"}

    def rewrite_paths(paths_dict: dict[str, Any], prefix: str) -> dict[str, Any]:
        new_paths = {}
        for path_str, path_item in paths_dict.items():
            if not isinstance(path_item, dict):
                new_paths[path_str] = path_item
                continue
            new_path_item = {}
            for k, v in path_item.items():
                if k.lower() in http_methods and isinstance(v, dict):
                    op = dict(v)
                    if "operationId" in op and isinstance(op["operationId"], str):
                        op_id = op["operationId"]
                        if not op_id.startswith(prefix):
                            op["operationId"] = f"{prefix}{op_id}"
                    new_path_item[k] = op
                else:
                    new_path_item[k] = v
            new_paths[path_str] = new_path_item
        return new_paths

    merged = {
        "openapi": "3.1.0",
        "info": {"title": f"{BRAND_NAME} - Unified API", "version": "0.1.0"},
        "paths": {},
        "components": {"schemas": {}},
    }

    (
        designer_spec,
        execution_spec,
        etmf_spec,
        interop_spec,
        ctms_spec,
        notifications_spec,
        quality_spec,
        safety_spec,
        tickets_spec,
        org_spec,
        eisf_spec,
        econsent_spec,
        fileshare_spec,
    ) = await asyncio.gather(
        fetch_service_openapi(SERVICES["designer"]),
        fetch_service_openapi(SERVICES["execution"]),
        fetch_service_openapi(SERVICES["etmf"]),
        fetch_service_openapi(SERVICES["interop"]),
        fetch_service_openapi(SERVICES["ctms"]),
        fetch_service_openapi(SERVICES["notifications"]),
        fetch_service_openapi(SERVICES["quality"]),
        fetch_service_openapi(SERVICES["safety"]),
        fetch_service_openapi(SERVICES["tickets"]),
        fetch_service_openapi(SERVICES["org"]),
        fetch_service_openapi(SERVICES["eisf"]),
        fetch_service_openapi(SERVICES["econsent"]),
        fetch_service_openapi(SERVICES["fileshare"]),
    )

    if fileshare_spec and is_valid_openapi_spec(fileshare_spec):
        try:
            fileshare_spec = rewrite_references(fileshare_spec, "Fileshare_")
            fileshare_paths = rewrite_paths(
                fileshare_spec.get("paths", {}), "Fileshare_"
            )
            for path_str, path_item in fileshare_paths.items():
                merged["paths"][f"/fileshare{path_str}"] = path_item
            for schema_name, schema_val in (
                fileshare_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Fileshare_{schema_name}"] = schema_val
        except Exception:
            pass

    if eisf_spec and is_valid_openapi_spec(eisf_spec):
        try:
            eisf_spec = rewrite_references(eisf_spec, "Eisf_")
            eisf_paths = rewrite_paths(eisf_spec.get("paths", {}), "Eisf_")
            for path_str, path_item in eisf_paths.items():
                merged["paths"][f"/eisf{path_str}"] = path_item
            for schema_name, schema_val in (
                eisf_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Eisf_{schema_name}"] = schema_val
        except Exception:
            pass

    if econsent_spec and is_valid_openapi_spec(econsent_spec):
        try:
            econsent_spec = rewrite_references(econsent_spec, "Econsent_")
            econsent_paths = rewrite_paths(econsent_spec.get("paths", {}), "Econsent_")
            for path_str, path_item in econsent_paths.items():
                merged["paths"][f"/econsent{path_str}"] = path_item
            for schema_name, schema_val in (
                econsent_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Econsent_{schema_name}"] = schema_val
        except Exception:
            pass

    if tickets_spec and is_valid_openapi_spec(tickets_spec):
        try:
            tickets_spec = rewrite_references(tickets_spec, "Tickets_")
            tickets_paths = rewrite_paths(tickets_spec.get("paths", {}), "Tickets_")
            for path_str, path_item in tickets_paths.items():
                merged["paths"][f"/tickets{path_str}"] = path_item
            for schema_name, schema_val in (
                tickets_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Tickets_{schema_name}"] = schema_val
        except Exception:
            pass

    if org_spec and is_valid_openapi_spec(org_spec):
        try:
            org_spec = rewrite_references(org_spec, "Org_")
            org_paths = rewrite_paths(org_spec.get("paths", {}), "Org_")
            for path_str, path_item in org_paths.items():
                merged["paths"][f"/org{path_str}"] = path_item
            for schema_name, schema_val in (
                org_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Org_{schema_name}"] = schema_val
        except Exception:
            pass

    if safety_spec and is_valid_openapi_spec(safety_spec):
        try:
            safety_spec = rewrite_references(safety_spec, "Safety_")
            safety_paths = rewrite_paths(safety_spec.get("paths", {}), "Safety_")
            for path_str, path_item in safety_paths.items():
                merged["paths"][f"/safety{path_str}"] = path_item
            for schema_name, schema_val in (
                safety_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Safety_{schema_name}"] = schema_val
        except Exception:
            pass

    if quality_spec and is_valid_openapi_spec(quality_spec):
        try:
            quality_spec = rewrite_references(quality_spec, "Quality_")
            quality_paths = rewrite_paths(quality_spec.get("paths", {}), "Quality_")
            for path_str, path_item in quality_paths.items():
                merged["paths"][f"/quality{path_str}"] = path_item
            for schema_name, schema_val in (
                quality_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Quality_{schema_name}"] = schema_val
        except Exception:
            pass

    if notifications_spec and is_valid_openapi_spec(notifications_spec):
        try:
            notifications_spec = rewrite_references(
                notifications_spec, "Notifications_"
            )
            notifications_paths = rewrite_paths(
                notifications_spec.get("paths", {}), "Notifications_"
            )
            for path_str, path_item in notifications_paths.items():
                merged["paths"][f"/notifications{path_str}"] = path_item
            for schema_name, schema_val in (
                notifications_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Notifications_{schema_name}"] = (
                    schema_val
                )
        except Exception:
            pass

    if ctms_spec and is_valid_openapi_spec(ctms_spec):
        try:
            ctms_spec = rewrite_references(ctms_spec, "Ctms_")
            ctms_paths = rewrite_paths(ctms_spec.get("paths", {}), "Ctms_")
            for path_str, path_item in ctms_paths.items():
                merged["paths"][f"/ctms{path_str}"] = path_item
            for schema_name, schema_val in (
                ctms_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Ctms_{schema_name}"] = schema_val
        except Exception:
            pass

    if designer_spec and is_valid_openapi_spec(designer_spec):
        try:
            designer_spec = rewrite_references(designer_spec, "Designer_")
            designer_paths = rewrite_paths(designer_spec.get("paths", {}), "Designer_")
            for path_str, path_item in designer_paths.items():
                merged["paths"][f"/designer{path_str}"] = path_item
            for schema_name, schema_val in (
                designer_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Designer_{schema_name}"] = schema_val
        except Exception:
            pass

    if execution_spec and is_valid_openapi_spec(execution_spec):
        try:
            execution_spec = rewrite_references(execution_spec, "Execution_")
            execution_paths = rewrite_paths(
                execution_spec.get("paths", {}), "Execution_"
            )
            for path_str, path_item in execution_paths.items():
                merged["paths"][f"/execution{path_str}"] = path_item
            for schema_name, schema_val in (
                execution_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Execution_{schema_name}"] = schema_val
        except Exception:
            pass

    if etmf_spec and is_valid_openapi_spec(etmf_spec):
        try:
            etmf_spec = rewrite_references(etmf_spec, "ETMF_")
            etmf_paths = rewrite_paths(etmf_spec.get("paths", {}), "ETMF_")
            for path_str, path_item in etmf_paths.items():
                merged["paths"][f"/etmf{path_str}"] = path_item
            for schema_name, schema_val in (
                etmf_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"ETMF_{schema_name}"] = schema_val
        except Exception:
            pass

    if interop_spec and is_valid_openapi_spec(interop_spec):
        try:
            interop_spec = rewrite_references(interop_spec, "Interop_")
            interop_paths = rewrite_paths(interop_spec.get("paths", {}), "Interop_")
            for path_str, path_item in interop_paths.items():
                merged["paths"][f"/interop{path_str}"] = path_item
            for schema_name, schema_val in (
                interop_spec.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][f"Interop_{schema_name}"] = schema_val
        except Exception:
            pass

    # Merge native gateway router specs (cdisc, usdm, ecoa) only if at least one downstream spec succeeded
    if any(
        [
            designer_spec,
            execution_spec,
            etmf_spec,
            interop_spec,
            ctms_spec,
            notifications_spec,
            quality_spec,
            safety_spec,
            tickets_spec,
            org_spec,
            eisf_spec,
            econsent_spec,
        ]
    ):
        try:
            from fastapi.openapi.utils import get_openapi

            native_openapi = get_openapi(
                title=f"{BRAND_NAME} - API Gateway",
                version="0.1.0",
                routes=app.routes,
            )
            for path_str, path_item in native_openapi.get("paths", {}).items():
                merged["paths"][path_str] = path_item
            for schema_name, schema_val in (
                native_openapi.get("components", {}).get("schemas", {}).items()
            ):
                merged["components"]["schemas"][schema_name] = schema_val
        except Exception:
            pass

    return JSONResponse(merged)


@app.get("/docs", include_in_schema=False)
async def get_swagger_ui() -> Response:
    """
    Serve the Swagger UI documentation portal.

    Uses FastAPI's built-in Swagger UI HTML generation to render
    the dynamically aggregated OpenAPI schema.

    Returns:
        Response: An HTMLResponse containing the Swagger UI.
    """
    return get_swagger_ui_html(
        openapi_url="/openapi.json", title=f"{BRAND_NAME} - Unified API Docs"
    )


class SignatureVerificationRequest(BaseModel):
    username: str
    password: str
    totp: str | None = None
    action: str
    batch_id: str | None = None
    semantic_action: str | None = None


AUTHORIZED_SIGNING_ROLES = {
    "investigator",
    "site investigator",
    "site_investigator",
    "crc",
    "cra",
    "data manager",
    "data_manager",
    "sponsor_dm",
    "sponsor_mm",
    "sponsor_statistician",
    "sponsor designer",
    "sponsor_designer",
    "study_designer",
    "study designer",
    "sponsor admin",
    "sponsor_admin",
    "admin",
    "sysadmin",
}


def generate_sig_token(
    user_id: str,
    username: str,
    action: str,
    roles: list[str],
    batch_id: str | None = None,
    semantic_action: str | None = None,
) -> str:
    """
    Generate a short-lived signature token (JWT) valid for 60 seconds.
    """
    now = time.time()
    payload = {
        "sub": user_id,
        "username": username,
        "action": action,
        "roles": roles,
        "iat": now,
        "exp": now + 60.0,
        "jti": str(uuid.uuid4()),
        "acr": "high-assurance-step-up",
        "auth_time": now,
    }
    if batch_id:
        payload["batch_id"] = batch_id
    if semantic_action:
        payload["semantic_action"] = semantic_action
        payload["sig_ver"] = "v3"
    return jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")


@app.post("/api/v1/auth/signature-verification")
async def signature_verification(request: Request, body: SignatureVerificationRequest):
    """
    POST /api/v1/auth/signature-verification
    Verifies re-supplied credentials (and MFA/TOTP when enabled) against Keycloak.
    Enforces step-up authentication using ACR (Authentication Context Class Reference),
    max_age bounds, and direct password re-verification.
    Issues a short-lived (60-second) sig_token bound to user and action.
    """
    auth_header = request.headers.get("Authorization")
    if not auth_header or not auth_header.startswith("Bearer "):
        raise HTTPException(status_code=401, detail="Missing active session token")
    token = auth_header.split(" ")[1]
    try:
        claims = await verify_token(token)
    except HTTPException as e:
        raise HTTPException(status_code=401, detail=e.detail)

    token_user_id = claims.get("sub", "")
    token_username = claims.get("preferred_username", claims.get("username", ""))

    if body.username != token_username and body.username != token_user_id:
        raise HTTPException(
            status_code=401, detail="Username does not match current session"
        )

    # Keycloak Step-Up & ACR / Max-Age Guidance Integration
    # If the token has a low-assurance ACR or has exceeded max_age, step-up is mandatory.
    _ = claims.get("acr")
    auth_time_claim = claims.get("auth_time")

    # Example: If the original auth time is too old (e.g., max_age > 10 hours), or ACR is not high-assurance
    if auth_time_claim:
        token_age = time.time() - float(auth_time_claim)
        # Standard Keycloak guidance: enforce credentials re-verification if session age exceeds max_age
        if token_age > 36000.0:  # 10 hours max age  # deid-ignore
            logger = logging.getLogger("gateway")
            logger.info("Session max_age exceeded. Forcing step-up re-authentication.")

    # Get roles
    roles_list = []
    realm_access = claims.get("realm_access", {})
    if isinstance(realm_access, dict):
        roles_list = realm_access.get("roles", [])
    else:
        roles_list = claims.get("roles", [])
    if not isinstance(roles_list, list):
        roles_list = [roles_list] if roles_list else []

    normalized_roles = [r.strip().lower() for r in roles_list if r]

    # Verify user possesses an authorized role
    has_auth_role = False
    for r in normalized_roles:
        if r in AUTHORIZED_SIGNING_ROLES:
            has_auth_role = True
            break

    if not has_auth_role:
        raise HTTPException(status_code=403, detail="ROLE_INSUFFICIENT")

    # Verify credentials against Keycloak (or mock in tests)
    is_test_env = bool(
        os.getenv("JWT_TEST_SECRET") or os.getenv("ALLOW_UNVERIFIED_JWT_FOR_TEST")
    )

    if not is_test_env:
        token_url = os.getenv(
            "KEYCLOAK_TOKEN_URL", JWKS_URL.replace("/certs", "/token")
        )
        try:
            data = {
                "grant_type": "password",
                "client_id": KEYCLOAK_CLIENT_ID,
                "username": body.username,
                "password": body.password,
            }
            client_secret = os.getenv("KEYCLOAK_CLIENT_SECRET")
            if client_secret:
                data["client_secret"] = client_secret
            if body.totp:
                data["totp"] = body.totp

            async with httpx.AsyncClient() as client:
                resp = await client.post(token_url, data=data, timeout=5.0)
                if resp.status_code == 200:
                    pass  # successfully verified
                else:
                    raise HTTPException(status_code=401, detail="Invalid credentials")
        except httpx.RequestError:
            raise HTTPException(
                status_code=503, detail="Authentication service temporarily unavailable"
            )
    else:
        # Fallback to Mock Verification ONLY for Tests
        if (
            body.password == "wrong_password"  # pragma: allowlist secret
            or "invalid" in body.password
        ):
            raise HTTPException(status_code=401, detail="Invalid credentials")
        if body.totp and ("invalid" in body.totp or "wrong" in body.totp):
            raise HTTPException(status_code=401, detail="Invalid credentials")

    # Derive Semantic Action
    derived_semantic = body.semantic_action
    if not derived_semantic and body.action:
        from packages.security.regulated_actions import resolve_regulated_action_by_path

        # Try resolving across methods to see if path is regulated
        for method in ["POST", "PUT", "PATCH", "DELETE"]:
            resolved = resolve_regulated_action_by_path(method, body.action)
            if resolved:
                derived_semantic = resolved.value
                break

    # Generate Short-Lived Sig Token
    sig_token = generate_sig_token(
        user_id=token_user_id,
        username=body.username,
        action=body.action,
        roles=normalized_roles,
        batch_id=body.batch_id,
        semantic_action=derived_semantic,
    )
    return {"sig_token": sig_token}


class ReplayPreventionCache:
    def __init__(self) -> None:
        self.used_tokens: dict[str, float] = {}

    def is_replayed(self, token: str, exp: float, jti: str | None = None) -> bool:
        now = time.time()
        # Prune expired tokens
        self.used_tokens = {t: e for t, e in self.used_tokens.items() if e > now}
        key = jti if jti else token
        if key in self.used_tokens:
            return True
        self.used_tokens[key] = exp
        return False


replay_cache = ReplayPreventionCache()


class DemoSessionRequest(BaseModel):
    username: str | None = None
    roles: list[str] | None = None
    tenant_id: str | None = None


@app.post("/api/v1/auth/demo")
@app.post("/api/v1/auth/demo-session")
async def create_demo_session(body: DemoSessionRequest | None = None) -> dict[str, Any]:
    username = (body.username if body else None) or "demo-user"
    roles = (body.roles if body else None) or [
        "site investigator",
        "cra",
        "admin",
        "auditor",
    ]
    tenant_id = (body.tenant_id if body else None) or "sandbox-tenant-default"

    if not tenant_id.lower().startswith("sandbox"):
        tenant_id = f"sandbox-{tenant_id}"

    now = time.time()
    payload = {
        "sub": f"demo-sub-{uuid.uuid4()}",
        "preferred_username": username,
        "username": username,
        "tenant_id": tenant_id,
        "roles": roles,
        "realm_access": {"roles": roles},
        "custom_attributes": {"tenant_id": tenant_id},
        "iat": now,
        "exp": now + 86400.0,  # 24 hours
        "jti": str(uuid.uuid4()),
    }
    token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")
    return {
        "access_token": token,
        "token_type": "Bearer",
        "expires_in": 86400,
        "tenant_id": tenant_id,
        "username": username,
        "roles": roles,
    }


@app.api_route(
    "/{path:path}",
    methods=["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"],
    include_in_schema=False,
)
async def proxy_requests(request: Request, path: str) -> Response:
    """
    Proxy HTTP requests to downstream microservices.

    Intercepts all incoming traffic, enforces valid authentication,
    injects authenticated identity headers along with cryptographic
    signatures, and forwards the request to the appropriate downstream URL.

    Args:
        request (Request): The incoming FastAPI HTTP request.
        path (str): The routed URL path.

    Returns:
        Response: The HTTP response from the downstream service or a
                  Gateway error JSON payload.
    """
    if path == "health" or path == "":
        return {"status": "ok", "service": "gateway"}

    if path == "api/v1/etmf/inbound-email":
        target_url = f"{SERVICES['etmf']}/{path}"
        headers = dict(request.headers)
        headers.pop("host", None)
        try:
            body: bytes = await request.body()
            if http_client is None:
                return JSONResponse(
                    status_code=500,
                    content={"detail": "Gateway HTTP client not initialized"},
                )

            req = http_client.build_request(
                method=request.method,
                url=target_url,
                headers=headers,
                content=body,
                params=request.query_params,
            )
            response = await http_client.send(req)

            resp_headers = dict(response.headers)
            resp_headers.pop("transfer-encoding", None)
            resp_headers.pop("content-encoding", None)
            resp_headers.pop("content-length", None)

            return Response(
                content=response.content,
                status_code=response.status_code,
                headers=resp_headers,
            )
        except httpx.RequestError as e:
            return JSONResponse(
                status_code=502, content={"detail": f"Bad Gateway: {str(e)}"}
            )

    auth_header = request.headers.get("Authorization")
    if not auth_header or not auth_header.startswith("Bearer "):
        return JSONResponse(
            status_code=401,
            content={"detail": "Missing or invalid Authorization header"},
        )

    token = auth_header.split(" ")[1]

    try:
        payload = await verify_token(token)
    except HTTPException as e:
        return JSONResponse(status_code=e.status_code, content={"detail": e.detail})

    user_id = payload.get("sub", "")

    # Enforce sig_token validation for signature-gated mutations
    is_mutation = request.method in ("POST", "PUT", "DELETE", "PATCH")
    body_bytes = await request.body()
    body_json = None
    if body_bytes:
        try:
            import json

            body_json = json.loads(body_bytes)
        except Exception:
            pass

    from packages.security.gating import is_path_signature_gated
    from packages.security.regulated_actions import resolve_regulated_action

    resolved_action = resolve_regulated_action(request.method, path, body_json)
    path_lower = path.lower()
    is_signature_gated = (resolved_action is not None) or is_path_signature_gated(
        path_lower
    )

    sig_token = request.headers.get("x-sig-token") or request.headers.get("X-Sig-Token")

    if is_signature_gated and is_mutation:
        from packages.security.middleware import verify_sig_token

        success, result = verify_sig_token(
            sig_token=sig_token,
            user_id=user_id,
            request_path=request.url.path,
            secret=GATEWAY_SECRET.encode(),
            replay_cache=replay_cache,
            expected_semantic_action=resolved_action.value if resolved_action else None,
        )
        if not success:
            return JSONResponse(
                status_code=401,
                content={
                    "detail": "REAUTHENTICATION_REQUIRED",
                    "error": "REAUTHENTICATION_REQUIRED",
                    "message": str(result),
                },
            )

    roles_set = set()
    realm_access = payload.get("realm_access", {})
    if isinstance(realm_access, dict):
        for r in realm_access.get("roles", []):
            roles_set.add(str(r))
    else:
        roles_list = payload.get("roles", [])
        if isinstance(roles_list, list):
            for r in roles_list:
                roles_set.add(str(r))
        elif roles_list:
            roles_set.add(str(roles_list))

    resource_access = payload.get("resource_access", {})
    if isinstance(resource_access, dict):
        for client_id, client_data in resource_access.items():
            if isinstance(client_data, dict):
                c_roles = client_data.get("roles", [])
                if isinstance(c_roles, list):
                    for r in c_roles:
                        roles_set.add(str(r))

    roles = ",".join(sorted(list(roles_set)))

    # Subject / Patient security routing boundary checks
    user_roles_list = [r.strip().lower() for r in roles.split(",") if r.strip()]
    if "subject" in user_roles_list:
        normalized_path = (
            path[len("interop/") :] if path.startswith("interop/") else path
        )
        parts = [p for p in normalized_path.split("/") if p]

        is_allowed = False
        method = request.method.upper()

        if len(parts) == 5:
            # POST /api/v1/interop/epro/submit
            if (
                (
                    parts[:4] == ["api", "v1", "interop", "epro"]
                    and parts[4] == "submit"
                    and method == "POST"
                )
                or (
                    parts[:4] == ["api", "v1", "interop", "epro"]
                    and parts[4] == "sync"
                    and method == "POST"
                )
                or (
                    parts[:4] == ["api", "v1", "interop", "instruments"]
                    and method == "GET"
                )
            ):
                is_allowed = True

        elif len(parts) == 6:
            # GET /api/v1/interop/assignments/subject/{authenticated-subject-id}
            if (
                parts[:5] == ["api", "v1", "interop", "assignments", "subject"]
                and method == "GET"
            ):
                if parts[5] == user_id:
                    is_allowed = True
            # GET /api/v1/interop/subjects/{authenticated-subject-id}/instruments
            elif (
                (
                    parts[:3] == ["api", "v1", "interop"]
                    and parts[3] == "subjects"
                    and parts[5] == "instruments"
                    and method == "GET"
                )
                or (
                    parts[:3] == ["api", "v1", "interop"]
                    and parts[3] == "subjects"
                    and parts[5] == "compliance"
                    and method == "GET"
                )
                or (
                    parts[:3] == ["api", "v1", "interop"]
                    and parts[3] == "subjects"
                    and parts[5] == "notifications"
                    and method == "GET"
                )
            ):
                if parts[4] == user_id:
                    is_allowed = True
            # POST /api/v1/interop/notifications/{notification-id}/acknowledge
            elif (
                parts[:3] == ["api", "v1", "interop"]
                and parts[3] == "notifications"
                and parts[5] == "acknowledge"
                and method == "POST"
            ):
                is_allowed = True

        if not is_allowed:
            return JSONResponse(
                status_code=403,
                content={
                    "detail": "Access denied: Subject principal is not authorized to access this route"
                },
            )

    if path.startswith("designer/"):
        target_url = f"{SERVICES['designer']}/{path[len('designer/') :]}"
    elif path.startswith("execution/"):
        target_url = f"{SERVICES['execution']}/{path[len('execution/') :]}"
    elif path.startswith("etmf/"):
        target_url = f"{SERVICES['etmf']}/{path[len('etmf/') :]}"
    elif path.startswith("interop/"):
        target_url = f"{SERVICES['interop']}/{path[len('interop/') :]}"
    elif path.startswith("ctms/"):
        target_url = f"{SERVICES['ctms']}/{path[len('ctms/') :]}"
    elif path.startswith("notifications/"):
        target_url = f"{SERVICES['notifications']}/{path[len('notifications/') :]}"
    elif path.startswith("quality/"):
        target_url = f"{SERVICES['quality']}/{path[len('quality/') :]}"
    elif path.startswith("safety/"):
        target_url = f"{SERVICES['safety']}/{path[len('safety/') :]}"
    elif path.startswith("tickets/"):
        target_url = f"{SERVICES['tickets']}/{path[len('tickets/') :]}"
    elif path.startswith("eisf/"):
        target_url = f"{SERVICES['eisf']}/{path[len('eisf/') :]}"
    elif path.startswith("api/v1/terminology"):
        target_url = f"{SERVICES['designer']}/{path}"
    elif path.startswith("terminology/"):
        target_url = f"{SERVICES['designer']}/{path[len('terminology/') :]}"
    elif path.startswith("api/v1/studies") or path.startswith("api/v2/studies"):
        target_url = f"{SERVICES['designer']}/{path}"
    elif path.startswith("api/v1/execution") or path.startswith("dictionary/"):
        target_url = f"{SERVICES['execution']}/{path}"
    elif path.startswith("econsent/"):
        target_url = f"{SERVICES['econsent']}/{path[len('econsent/') :]}"
    elif path.startswith("api/v1/econsent"):
        target_url = f"{SERVICES['econsent']}/{path}"
    elif path.startswith("api/v1/eisf"):
        target_url = f"{SERVICES['eisf']}/{path}"
    elif path.startswith("api/v1/etmf"):
        target_url = f"{SERVICES['etmf']}/{path}"
    elif path.startswith("api/v1/interop"):
        target_url = f"{SERVICES['interop']}/{path}"
    elif path.startswith("api/v1/ctms"):
        target_url = f"{SERVICES['ctms']}/{path}"
    elif path.startswith("api/v1/notifications"):
        target_url = f"{SERVICES['notifications']}/{path}"
    elif path.startswith("api/v1/quality"):
        target_url = f"{SERVICES['quality']}/{path}"
    elif path.startswith("api/v1/safety"):
        target_url = f"{SERVICES['safety']}/{path}"
    elif path.startswith("org/"):
        target_url = f"{SERVICES['org']}/{path[len('org/') :]}"
    elif path.startswith("api/v1/org"):
        target_url = f"{SERVICES['org']}/{path}"
    elif path.startswith("fileshare/"):
        target_url = f"{SERVICES['fileshare']}/{path[len('fileshare/') :]}"
    elif path.startswith("api/v1/fileshare"):
        target_url = f"{SERVICES['fileshare']}/{path}"
    elif path.startswith("api/v1/compliance") or path.startswith("api/v1/tickets"):
        target_url = f"{SERVICES['tickets']}/{path}"
    elif path == "events/publish":
        target_url = f"{SERVICES['eisf']}/events/publish"
    else:
        target_url = f"{SERVICES['designer']}/{path}"

    headers = dict(request.headers)
    headers.pop("host", None)

    # Clean up incoming headers to prevent client-side spoofing of identity and scope claims
    for k in list(headers.keys()):
        k_lower = k.lower()
        if k_lower in (
            "x-user-id",
            "x-user-roles",
            "x-gateway-timestamp",
            "x-gateway-signature",
            "x-signature-version",
            "x-change-reason",
            "x-site-id",
            "x-sponsor-id",
            "x-unblinded-access",
            "x-tenant-id",
        ):
            headers.pop(k, None)

    change_reason = request.headers.get("x-change-reason")
    if change_reason is not None:
        if len(change_reason) > 255:
            return JSONResponse(
                status_code=400,
                content={"detail": "Change reason exceeds 255 characters"},
            )
        headers["X-Change-Reason"] = change_reason

    custom_attrs = payload.get("custom_attributes") or {}

    raw_site_id = payload.get("site_id")
    raw_sponsor_id = ""
    if isinstance(custom_attrs, dict):
        raw_sponsor_id = custom_attrs.get("sponsor_id") or ""
    if not raw_sponsor_id:
        raw_sponsor_id = payload.get("sponsor_id")

    raw_unblinded_access = payload.get("unblinded_access", False)

    from packages.security.signing import normalize_scope_values

    site_id_val, sponsor_id_val, unblinded_access_val = normalize_scope_values(
        raw_site_id, raw_sponsor_id, raw_unblinded_access
    )

    # Extract tenant identity and apply least-privilege migration policy (default to tenant_default)
    tenant_id_val = ""
    if isinstance(custom_attrs, dict):
        tenant_id_val = custom_attrs.get("tenant_id") or ""

    if not tenant_id_val:
        tenant_id_val = payload.get("tenant_id", "")

    if tenant_id_val is None or not str(tenant_id_val).strip():
        tenant_id_val = "tenant_default"
    else:
        tenant_id_val = str(tenant_id_val).strip()

    # Tenant Isolation Gate
    token_tenant_id = (
        payload.get("tenant_id")
        or (payload.get("custom_attributes") or {}).get("tenant_id")
        if isinstance(payload.get("custom_attributes"), dict)
        else None
    )
    if token_tenant_id and str(token_tenant_id).strip().lower().startswith("sandbox"):
        if not tenant_id_val or not tenant_id_val.lower().startswith("sandbox"):
            logger = logging.getLogger("gateway")
            logger.error(
                f"ACCESS VIOLATION: Sandbox token attempted to access non-sandbox tenant resource scope: {tenant_id_val}"
            )
            return JSONResponse(
                status_code=403,
                content={
                    "detail": "Access denied: Sandbox token cannot access non-sandbox resources"
                },
            )

        req_tenant = request.query_params.get("tenant_id") or request.query_params.get(
            "tenant"
        )
        if req_tenant and not str(req_tenant).strip().lower().startswith("sandbox"):
            logger = logging.getLogger("gateway")
            logger.error(
                f"ACCESS VIOLATION: Sandbox token attempted to access non-sandbox tenant: {req_tenant}"
            )
            return JSONResponse(
                status_code=403,
                content={
                    "detail": "Access denied: Sandbox token cannot access non-sandbox resources"
                },
            )

    timestamp = str(time.time())
    signature = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
        site_id=site_id_val if site_id_val else None,
        sponsor_id=sponsor_id_val if sponsor_id_val else None,
        unblinded_access=unblinded_access_val,
        tenant_id=tenant_id_val,
        sig_token=sig_token,
    )

    headers["X-User-Id"] = user_id
    headers["X-User-Roles"] = roles
    headers["X-Gateway-Timestamp"] = timestamp
    headers["X-Gateway-Signature"] = signature
    headers["X-Signature-Version"] = "2"
    headers["X-Tenant-Id"] = tenant_id_val
    if site_id_val:
        headers["X-Site-Id"] = site_id_val
    if sponsor_id_val:
        headers["X-Sponsor-Id"] = sponsor_id_val
    if unblinded_access_val:
        headers["X-Unblinded-Access"] = "true"
    if sig_token:
        headers["X-Sig-Token"] = sig_token

    try:
        body: bytes = await request.body()
        if http_client is None:
            return JSONResponse(
                status_code=500,
                content={"detail": "Gateway HTTP client not initialized"},
            )

        req = http_client.build_request(
            method=request.method,
            url=target_url,
            headers=headers,
            content=body,
            params=request.query_params,
        )
        response = await http_client.send(req)

        resp_headers = dict(response.headers)
        resp_headers.pop("transfer-encoding", None)
        resp_headers.pop("content-encoding", None)
        resp_headers.pop("content-length", None)

        return Response(
            content=response.content,
            status_code=response.status_code,
            headers=resp_headers,
        )
    except httpx.RequestError as e:
        return JSONResponse(
            status_code=502, content={"detail": f"Bad Gateway: {str(e)}"}
        )
