import threading
from typing import Dict, Any, Optional

# --- Mock Database Content ---
MOCK_TERMINOLOGY = {
    "C123": {"code": "C123", "decode": "Treatment Arm", "system": "NCI"},
    "C456": {"code": "C456", "decode": "Placebo Arm", "system": "NCI"},
    "C789": {"code": "C789", "decode": "Screening Visit", "system": "NCI"},
    "C012": {"code": "C012", "decode": "Follow-up Visit", "system": "NCI"}
}

MOCK_STUDIES = {
    "study_1": {
        "study_id": "study_1",
        "title": "Oncology Phase II",
        "current_version": "2.1",
        "desc": "A study for solid tumors.",
        "arms": [
            {
                "arm_id": "arm_1",
                "name": "Arm A",
                "type_concept_id": "C123",
                "visits": [
                    {
                        "visit_id": "visit_1",
                        "name": "Visit 1",
                        "visit_type_concept_id": "C789",
                        "activities": [
                            {"activity_id": "act_1", "name": "Blood Draw"},
                            {"activity_id": "act_2", "name": "Vitals"}
                        ]
                    }
                ]
            }
        ]
    }
}

# --- Counters for Acceptance Criteria Tests ---
db_query_counts = {
    "terminology_lookups": 0
}

def get_study_projection(study_id: str) -> Optional[Dict[str, Any]]:
    # Simulates an optimized database projection query returning multi-level relationships
    return MOCK_STUDIES.get(study_id)

def get_terminology_from_db(concept_id: str) -> Optional[Dict[str, Any]]:
    # Increments counter to prove zero additional queries from cache hits
    db_query_counts["terminology_lookups"] += 1
    return MOCK_TERMINOLOGY.get(concept_id)

# --- Controlled Terminology Cache ---
class TerminologyCache:
    def __init__(self, max_size=1000):
        self.max_size = max_size
        self._cache = {}
        self._lock = threading.Lock()
    
    def get(self, concept_id: str) -> Optional[Dict[str, Any]]:
        with self._lock:
            if concept_id in self._cache:
                return self._cache[concept_id]
        
        # Miss - fetch from DB
        data = get_terminology_from_db(concept_id)
        if data:
            with self._lock:
                if len(self._cache) >= self.max_size:
                    # Basic eviction policy
                    self._cache.pop(next(iter(self._cache)))
                self._cache[concept_id] = data
        return data

    def clear(self):
        with self._lock:
            self._cache.clear()
            
    def get_status(self):
        with self._lock:
            return {
                "size": len(self._cache),
                "max_size": self.max_size
            }

terminology_cache = TerminologyCache()
