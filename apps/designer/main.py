from fastapi import FastAPI, HTTPException, status
import time

from apps.designer.db import get_study_projection, terminology_cache
from apps.designer.mapper import map_study_to_usdm

app = FastAPI(title="Cadence Clinical - Designer (MDR/SDR)", version="0.1.0")

@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "designer"}

@app.get("/api/v1/studies/{study_id}")
async def get_legacy_study(study_id: str):
    """Returns the legacy internal projection with no USDM formatting."""
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")
    return study_data

@app.get("/api/v2/studies/{study_id}/usdm")
async def get_usdm_study(study_id: str):
    """Dynamically processes the internal projection and returns a compliant USDM structure."""
    start_time = time.perf_counter()
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")
    
    try:
        usdm_study = map_study_to_usdm(study_data)
    except Exception as e:
        raise HTTPException(status_code=422, detail=f"Validation Error mapping USDM: {str(e)}")
        
    duration = (time.perf_counter() - start_time) * 1000
    # Simulate processing overhead check - we want this under 200ms
    if duration > 200:
        pass # In a real app we might log a warning
        
    return usdm_study

@app.post("/api/admin/cache/clear", status_code=status.HTTP_200_OK)
async def clear_cache():
    """Flushes the controlled terminology cache."""
    terminology_cache.clear()
    return {"status": "success", "message": "Cache cleared successfully"}

@app.get("/api/admin/cache/status")
async def cache_status():
    """Returns the current size and status of the terminology cache."""
    return terminology_cache.get_status()
