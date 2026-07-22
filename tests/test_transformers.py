import pytest
from fastapi.testclient import TestClient
import time
from apps.designer.main import app
from apps.designer.db import db_query_counts, terminology_cache, MOCK_TERMINOLOGY

client = TestClient(app)

def test_legacy_endpoint_returns_original_schema():
    # Requirement: Requesting resources from versioned legacy endpoints returns original schemas
    response = client.get("/api/v1/studies/study_1")
    assert response.status_code == 200
    data = response.json()
    assert data["study_id"] == "study_1"
    assert "current_version" in data
    # Should not have USDM formatting (like 'arms' with 'visit_type' object instead of 'visit_type_concept_id' string)
    assert "visit_type_concept_id" in data["arms"][0]["visits"][0]

def test_usdm_endpoint_returns_nested_schema_and_fast():
    # Requirement: Requesting a USDM study export constructs and returns the full nested JSON structure within 200ms
    start = time.perf_counter()
    response = client.get("/api/v2/studies/study_1/usdm")
    duration_ms = (time.perf_counter() - start) * 1000
    
    assert response.status_code == 200
    assert duration_ms < 200, f"Latency was {duration_ms}ms, exceeded 200ms"
    
    data = response.json()
    # Validate USDM structural mapping
    assert data["id"] == "study_1"
    assert data["version"] == "2.1"
    
    # Check nesting and concept resolution
    arm = data["arms"][0]
    assert arm["id"] == "arm_1"
    assert arm["arm_type"]["code"] == "C123"
    assert arm["arm_type"]["decode"] == "Treatment Arm"
    
    visit = arm["visits"][0]
    assert visit["visit_type"]["code"] == "C789"
    assert visit["activities"][0]["name"] == "Blood Draw"

def test_terminology_cache_prevents_db_queries():
    # Requirement: Identical Terminology requests run zero additional DB queries
    terminology_cache.clear()
    initial_queries = db_query_counts["terminology_lookups"]
    
    # First request: populates cache
    response = client.get("/api/v2/studies/study_1/usdm")
    assert response.status_code == 200
    
    queries_after_first = db_query_counts["terminology_lookups"]
    assert queries_after_first > initial_queries
    
    # Second request: identical lookups, should hit cache
    response2 = client.get("/api/v2/studies/study_1/usdm")
    assert response2.status_code == 200
    
    queries_after_second = db_query_counts["terminology_lookups"]
    assert queries_after_second == queries_after_first, "Cache failed to prevent additional database queries"

def test_admin_cache_clear_forces_fresh_read():
    # Requirement: Sending an authorized trigger to the administrative endpoint flushes cache
    # First populate cache
    client.get("/api/v2/studies/study_1/usdm")
    queries_before_clear = db_query_counts["terminology_lookups"]
    
    # Trigger clear
    clear_response = client.post("/api/admin/cache/clear")
    assert clear_response.status_code == 200
    
    status_response = client.get("/api/admin/cache/status")
    assert status_response.status_code == 200
    assert status_response.json()["size"] == 0
    
    # Request again, should force new queries
    client.get("/api/v2/studies/study_1/usdm")
    queries_after_clear = db_query_counts["terminology_lookups"]
    assert queries_after_clear > queries_before_clear, "Fresh DB reads were not triggered after cache clear"

def test_usdm_validation_error_on_invalid_data():
    # Insert invalid mock data temporarily
    MOCK_TERMINOLOGY["INVALID_CONCEPT"] = {"code": "INV", "decode": "Invalid"} # missing system
    original_data = db_query_counts["terminology_lookups"]
    
    # We will temporarily modify the mock study to use this invalid concept
    from apps.designer.db import MOCK_STUDIES
    original_concept = MOCK_STUDIES["study_1"]["arms"][0]["type_concept_id"]
    MOCK_STUDIES["study_1"]["arms"][0]["type_concept_id"] = "INVALID_CONCEPT"
    
    try:
        response = client.get("/api/v2/studies/study_1/usdm")
        assert response.status_code == 422
        assert "Validation Error" in response.json()["detail"]
    finally:
        # Restore mock data
        MOCK_STUDIES["study_1"]["arms"][0]["type_concept_id"] = original_concept
