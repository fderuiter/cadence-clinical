import os
import hmac
import hashlib
import time
from typing import AsyncGenerator, Callable, Awaitable
from contextlib import asynccontextmanager
from fastapi import FastAPI, Request, Response
from fastapi.responses import JSONResponse
from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:")

@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Handle the lifespan events for the FastAPI application.

    Initializes the database session manager on startup and securely
    cleans up connections on shutdown.

    Args:
        app (FastAPI): The FastAPI application instance.

    Yields:
        None
    """
    # Initialize shared database library
    db_manager.init_db(DATABASE_URL)
    yield
    # Cleanup database connection
    await db_manager.close()

app = FastAPI(
    title="Cadence Clinical - EDC Execution Engine",
    version="0.1.0",
    lifespan=lifespan
)

app.add_middleware(ContextResetMiddleware)

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")

@app.middleware("http")
async def gateway_auth_middleware(request: Request, call_next: Callable[[Request], Awaitable[Response]]) -> Response:
    """
    Middleware to verify internal gateway authentication.

    Extracts identity headers injected by the API gateway and cryptographic 
    signatures. If missing or invalid, blocks the request to prevent 
    unauthorized direct access to the microservice.

    Args:
        request (Request): The incoming HTTP request.
        call_next (Callable): The next middleware or route handler in the chain.

    Returns:
        Response: The HTTP response from the downstream handler, or a 401 
                  unauthorized JSON response if validation fails.
    """
    if request.url.path == "/health":
        return await call_next(request)
    
    user_id = request.headers.get("X-User-Id")
    roles = request.headers.get("X-User-Roles")
    timestamp = request.headers.get("X-Gateway-Timestamp")
    signature = request.headers.get("X-Gateway-Signature")
    
    if not all([user_id, roles, timestamp, signature]):
        return JSONResponse(status_code=401, content={"detail": "Missing gateway authentication headers"})
    
    try:
        ts = float(timestamp)
        if abs(time.time() - ts) > 300:
            return JSONResponse(status_code=401, content={"detail": "Gateway signature expired"})
    except ValueError:
        return JSONResponse(status_code=401, content={"detail": "Invalid gateway timestamp"})

    message = f"{user_id}:{roles}:{timestamp}"
    expected_signature = hmac.new(GATEWAY_SECRET.encode(), message.encode(), hashlib.sha256).hexdigest()
    
    if not hmac.compare_digest(expected_signature, signature):
        return JSONResponse(status_code=401, content={"detail": "Invalid gateway signature"})
    
    request.state.user_id = user_id
    request.state.roles = roles

    return await call_next(request)

@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.

    Returns a basic JSON payload indicating the service is operational.

    Returns:
        dict[str, str]: The health status payload.
    """
    return {"status": "ok", "service": "execution"}