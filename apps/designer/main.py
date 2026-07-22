from fastapi import FastAPI, HTTPException

from apps.designer.validator import generate_alignment_report, StudyAlignmentReport
from pydantic import BaseModel
from typing import Any, List

class DifferenceResult(BaseModel):
    field: str
    old_value: Any
    new_value: Any

app = FastAPI(title="Cadence Clinical - Designer (MDR/SDR)", version="0.1.0")

@app.on_event("startup")
async def startup():
    pass

@app.on_event("shutdown")
async def shutdown():
    pass

@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "designer"}

@app.get("/api/v1/studies/{study_id}/alignment-validation", response_model=StudyAlignmentReport)
async def validate_study_alignment(study_id: str):
    return await generate_alignment_report(study_id)

@app.get("/api/v1/studies/{study_id}/differences", response_model=List[DifferenceResult])
async def study_differences(study_id: str, action_id1: str, action_id2: str):
    raise HTTPException(status_code=503, detail="Database connection not initialized")
