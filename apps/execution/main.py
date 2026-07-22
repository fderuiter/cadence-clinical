from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
import os
import hmac
import hashlib
import time

app = FastAPI(title="Cadence Clinical - EDC Execution Engine", version="0.1.0")

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")

@app.middleware("http")
async def gateway_auth_middleware(request: Request, call_next):
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
async def health_check():
    return {"status": "ok", "service": "execution"}