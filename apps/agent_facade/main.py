"""
Agent Facade Microservice.

Provides a dedicated, public-facing facade service API contract for automated developer agents
interacting with clinical execution capabilities without direct raw database table access.

Note: In strict compliance with safety, security, and licensing constraints, this microservice
does NOT copy, import, or re-implement any proprietary clinical algorithms, scoring, matching,
or range-evaluation algorithms. All calculations and operations are securely delegated
downstream using verified cryptographically signed gateway headers.
"""

import os
import time
from typing import Any, Dict, List, Optional

import httpx
from fastapi import FastAPI, HTTPException, Request
from pydantic import BaseModel, Field

from packages.security.middleware import GatewayAuthMiddleware
from packages.security.signing import generate_gateway_signature

app = FastAPI(
    title="Cadence Clinical - Agent Facade Microservice",
    version="0.1.0",
    description="Isolated Facade Microservice for Automated Agent Operations. This service does not copy, import, or re-implement proprietary scoring, matching, or range-evaluation algorithms.",
)

app.add_middleware(GatewayAuthMiddleware)

EXECUTION_URL = os.getenv("EXECUTION_URL", "http://localhost:8002")
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")

# Global HTTP client
http_client: Optional[httpx.AsyncClient] = None


@app.on_event("startup")
async def startup() -> None:
    """Initialize global HTTP client on startup."""
    global http_client
    http_client = httpx.AsyncClient()


@app.on_event("shutdown")
async def shutdown() -> None:
    """Close global HTTP client on shutdown."""
    global http_client
    if http_client:
        await http_client.aclose()


@app.get("/health")
async def health_check() -> Dict[str, str]:
    """Simple health check endpoint."""
    return {"status": "ok", "service": "agent-facade"}


class UnitConversionRequest(BaseModel):
    """Pydantic schema for unit conversion requests."""

    value: float = Field(..., description="The value to convert")
    from_unit: str = Field(..., description="The source unit")
    to_unit: str = Field(..., description="The target unit")


class UnitConversionResponse(BaseModel):
    """Pydantic schema for unit conversion responses."""

    value: float
    from_unit: str
    to_unit: str
    converted_value: float


class QueryCreateRequest(BaseModel):
    """Pydantic schema for creating clinical queries."""

    site_id: str
    subject_id: str
    observation_id: str
    query_text: str
    assigned_to_role: str


class QueryRespondRequest(BaseModel):
    """Pydantic schema for responding to clinical queries."""

    response_text: str


def get_downstream_headers(request: Request) -> Dict[str, str]:
    """
    Extract current gateway state and sign a fresh downstream request.

    Guarantees that downstream microservices accept the facade's request
    only if they verify a cryptographically valid gateway signature.
    """
    user_id = getattr(request.state, "user_id", "automated_agent")
    roles = getattr(request.state, "roles", "agent")
    change_reason = getattr(request.state, "change_reason", "Agent operation")
    site_id = getattr(request.state, "site_id", None)
    sponsor_id = getattr(request.state, "sponsor_id", None)
    unblinded_access = getattr(request.state, "unblinded_access", False)

    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sponsor_id:
        headers["X-Sponsor-Id"] = sponsor_id
    if unblinded_access:
        headers["X-Unblinded-Access"] = "true"

    return headers


@app.post("/api/v1/agent-facade/unit-conversion", response_model=UnitConversionResponse)
async def agent_unit_conversion(
    request: Request, payload: UnitConversionRequest
) -> Any:
    """
    Validate and execute unit conversion via the execution service.

    Note: This service does NOT copy, import, or re-implement the unit-conversion algorithm,
    it delegates the calculation exclusively to downstream execution service.
    """
    headers = get_downstream_headers(request)
    if http_client is None:
        raise HTTPException(status_code=500, detail="HTTP client is not initialized")
    try:
        resp = await http_client.post(
            f"{EXECUTION_URL}/api/v1/execution/unit-conversion",
            json=payload.model_dump(),
            headers=headers,
            timeout=10.0,
        )
        if resp.status_code != 200:
            raise HTTPException(status_code=resp.status_code, detail=resp.text)
        return resp.json()
    except httpx.RequestError as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )


@app.get("/api/v1/agent-facade/queries", response_model=List[Any])
async def agent_list_queries(request: Request) -> Any:
    """Retrieve clinical queries via the execution service."""
    headers = get_downstream_headers(request)
    if http_client is None:
        raise HTTPException(status_code=500, detail="HTTP client is not initialized")
    try:
        resp = await http_client.get(
            f"{EXECUTION_URL}/api/v1/execution/queries",
            headers=headers,
            params=dict(request.query_params),
            timeout=10.0,
        )
        if resp.status_code != 200:
            raise HTTPException(status_code=resp.status_code, detail=resp.text)
        return resp.json()
    except httpx.RequestError as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )


@app.post("/api/v1/agent-facade/queries", response_model=Any)
async def agent_create_query(request: Request, payload: QueryCreateRequest) -> Any:
    """Create a new clinical query via the execution service."""
    headers = get_downstream_headers(request)
    if http_client is None:
        raise HTTPException(status_code=500, detail="HTTP client is not initialized")
    try:
        resp = await http_client.post(
            f"{EXECUTION_URL}/api/v1/execution/queries",
            json=payload.model_dump(),
            headers=headers,
            timeout=10.0,
        )
        if resp.status_code != 200:
            raise HTTPException(status_code=resp.status_code, detail=resp.text)
        return resp.json()
    except httpx.RequestError as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )


@app.post("/api/v1/agent-facade/queries/{query_id}/respond", response_model=Any)
async def agent_respond_query(
    request: Request, query_id: str, payload: QueryRespondRequest
) -> Any:
    """Respond to an existing clinical query via the execution service."""
    headers = get_downstream_headers(request)
    if http_client is None:
        raise HTTPException(status_code=500, detail="HTTP client is not initialized")
    try:
        resp = await http_client.post(
            f"{EXECUTION_URL}/api/v1/execution/queries/{query_id}/respond",
            json=payload.model_dump(),
            headers=headers,
            timeout=10.0,
        )
        if resp.status_code != 200:
            raise HTTPException(status_code=resp.status_code, detail=resp.text)
        return resp.json()
    except httpx.RequestError as e:
        raise HTTPException(
            status_code=502,
            detail=f"Failed to communicate with downstream execution service: {str(e)}",
        )
