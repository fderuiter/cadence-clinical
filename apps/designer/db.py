import threading
from typing import Any, Dict, Optional

# --- Mock Database Content ---
MOCK_TERMINOLOGY = {
    "C123": {"code": "C123", "decode": "Treatment Arm", "system": "NCI"},
    "C456": {"code": "C456", "decode": "Placebo Arm", "system": "NCI"},
    "C789": {"code": "C789", "decode": "Screening Visit", "system": "NCI"},
    "C012": {"code": "C012", "decode": "Follow-up Visit", "system": "NCI"},
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
                            {"activity_id": "act_2", "name": "Vitals"},
                        ],
                    }
                ],
            }
        ],
    }
}

# --- Counters for Acceptance Criteria Tests ---
db_query_counts = {"terminology_lookups": 0}


def get_study_projection(study_id: str) -> Optional[Dict[str, Any]]:
    """Retrieves a study projection from the database.

    Args:
        study_id (str): The unique identifier of the study.

    Returns:
        Optional[Dict[str, Any]]: The study data dictionary, or None if not found.
    """
    # Simulates an optimized database projection query returning multi-level relationships
    return MOCK_STUDIES.get(study_id)


def get_terminology_from_db(concept_id: str) -> Optional[Dict[str, Any]]:
    """Retrieves controlled terminology data from the database.

    Args:
        concept_id (str): The unique identifier of the terminology concept.

    Returns:
        Optional[Dict[str, Any]]: The terminology data dictionary, or None if not found.
    """
    # Increments counter to prove zero additional queries from cache hits
    db_query_counts["terminology_lookups"] += 1
    return MOCK_TERMINOLOGY.get(concept_id)


# --- Controlled Terminology Cache ---
class TerminologyCache:
    """Thread-safe in-memory cache for controlled terminology lookups."""

    def __init__(self, max_size: int = 1000) -> None:
        """Initializes the terminology cache.

        Args:
            max_size (int, optional): The maximum number of items to cache. Defaults to 1000.
        """
        self.max_size: int = max_size
        self._cache: Dict[str, Dict[str, Any]] = {}
        self._lock: threading.Lock = threading.Lock()

    def get(self, concept_id: str) -> Optional[Dict[str, Any]]:
        """Retrieves a terminology concept from the cache or database.

        Args:
            concept_id (str): The unique identifier of the concept.

        Returns:
            Optional[Dict[str, Any]]: The terminology data, or None if not found.
        """
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

    def clear(self) -> None:
        """Clears all items from the cache."""
        with self._lock:
            self._cache.clear()

    def get_status(self) -> Dict[str, int]:
        """Retrieves the current status of the cache.

        Returns:
            Dict[str, int]: A dictionary containing 'size' and 'max_size'.
        """
        with self._lock:
            return {"size": len(self._cache), "max_size": self.max_size}


terminology_cache = TerminologyCache()
