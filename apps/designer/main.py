from typing import Callable, Dict, Optional, Awaitable
from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.responses import JSONResponse
import os
import hmac
import hashlib
import time
from neo4j import AsyncGraphDatabase

from apps.designer.validator import generate_alignment_report, StudyAlignmentReport
from apps.designer.delta import get_study_differences
from pydantic import BaseModel
from typing import Any, List

class DifferenceResult(BaseModel):
    field: str
    old_value: Any
    new_value: Any

app = FastAPI(title="Cadence Clinical - Designer (MDR/SDR)", version="0.1.0")

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

driver: Optional[AsyncGraphDatabase] = None

@app.on_event("startup")
async def startup() -> None:
    """
    Initialize resources on designer startup.

    Establishes an asynchronous connection pool to the Neo4j graph database.
    """
    global driver
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    user = os.getenv("NEO4J_USER", "neo4j")
    password = os.getenv("NEO4J_PASSWORD", "cadence_password")
    try:
        driver = AsyncGraphDatabase.driver(uri, auth=(user, password))
    except Exception as e:
        print(f"Failed to connect to Neo4j: {e}")

@app.on_event("shutdown")
async def shutdown() -> None:
    """
    Clean up resources on designer shutdown.

    Closes the connection pool to the Neo4j graph database.
    """
    global driver
    if driver:
        await driver.close()

@app.get("/health")
async def health_check() -> Dict[str, str]:
    """
    Service health check endpoint.

    Returns a basic JSON payload indicating the service is operational.

    Returns:
        Dict[str, str]: The health status payload.
    """
    return {"status": "ok", "service": "designer"}

@app.get("/api/v1/studies/{study_id}/alignment-validation", response_model=StudyAlignmentReport)
async def validate_study_alignment(study_id: str) -> StudyAlignmentReport:
    """
    Generate an alignment validation report for a specific clinical study.

    Analyzes trace links within the graph database to ensure the 
    Study Data Requirements (SDR) align with Metadata Requirements (MDR).

    Args:
        study_id (str): The unique identifier of the study to validate.

    Returns:
        StudyAlignmentReport: The structured validation report.

    Raises:
        HTTPException: If the database is unreachable.
    """
    if not driver:
        raise HTTPException(status_code=503, detail="Database connection not initialized")
    return await generate_alignment_report(driver, study_id)

@app.get("/api/v1/studies/{study_id}/differences", response_model=List[DifferenceResult])
async def study_differences(study_id: str, action_id1: str, action_id2: str):
    if not driver:
        raise HTTPException(status_code=503, detail="Database connection not initialized")
    
    diffs = await get_study_differences(driver, study_id, action_id1, action_id2)
    return diffs