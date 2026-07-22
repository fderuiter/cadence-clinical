from fastapi import FastAPI, HTTPException
import os
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

driver = None

@app.on_event("startup")
async def startup():
    global driver
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    user = os.getenv("NEO4J_USER", "neo4j")
    password = os.getenv("NEO4J_PASSWORD", "cadence_password")
    try:
        driver = AsyncGraphDatabase.driver(uri, auth=(user, password))
    except Exception as e:
        print(f"Failed to connect to Neo4j: {e}")

@app.on_event("shutdown")
async def shutdown():
    global driver
    if driver:
        await driver.close()

@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "designer"}

@app.get("/api/v1/studies/{study_id}/alignment-validation", response_model=StudyAlignmentReport)
async def validate_study_alignment(study_id: str):
    if not driver:
        raise HTTPException(status_code=503, detail="Database connection not initialized")
    return await generate_alignment_report(driver, study_id)

@app.get("/api/v1/studies/{study_id}/differences", response_model=List[DifferenceResult])
async def study_differences(study_id: str, action_id1: str, action_id2: str):
    if not driver:
        raise HTTPException(status_code=503, detail="Database connection not initialized")
    
    diffs = await get_study_differences(driver, study_id, action_id1, action_id2)
    return diffs