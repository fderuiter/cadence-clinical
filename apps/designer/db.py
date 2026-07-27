import threading
from typing import Any, Dict, List, Optional

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

# In-memory rule mock store fallback
MOCK_RULES: Dict[str, List[Dict[str, Any]]] = {}

# In-memory eligibility criteria mock store fallback
MOCK_ELIGIBILITY_CRITERIA: Dict[str, List[Dict[str, Any]]] = {}

# --- Mock Study Version Content ---
MOCK_STUDY_VERSIONS: Dict[str, List[Dict[str, Any]]] = {}
MOCK_STUDY_PROJECTIONS_BY_VERSION: Dict[str, Dict[str, Any]] = {}
MOCK_LIBRARY_OBJECTS: Dict[str, List[Dict[str, Any]]] = {}


def create_mock_study_version(study_id: str, version_data: Dict[str, Any]):
    """Creates a mock StudyVersion in-memory."""
    if study_id not in MOCK_STUDY_VERSIONS:
        MOCK_STUDY_VERSIONS[study_id] = []

    # Check if version_index or version_tag already exists for this study
    for v in MOCK_STUDY_VERSIONS[study_id]:
        if v.get("version_index") == version_data.get("version_index") or v.get(
            "version_tag"
        ) == version_data.get("version_tag"):
            from apps.designer.delta import ConcurrentLockingError

            raise ConcurrentLockingError("Version index or tag already exists")

    # Automatically sign the version payload if signature not present
    if "signature" not in version_data:
        import os

        from packages.security.signing import generate_canonical_signature

        payload = {
            "id": version_data.get("id") or "legacy_ver",
            "version_tag": version_data.get("version_tag") or "1.0",
            "status": version_data.get("status") or "DRAFT",
            "version_index": version_data.get("version_index") or 1,
            "created_by": version_data.get("created_by") or "system",
        }
        if "created_at" in version_data:
            payload["created_at"] = str(version_data["created_at"])
        if "parent_version" in version_data:
            payload["parent_version"] = version_data["parent_version"]

        secret = os.getenv(
            "SIGNING_SECRET", "designer-amendment-secure-key-12345"
        ).encode("utf-8")
        version_data["signature"] = generate_canonical_signature(payload, secret)

    MOCK_STUDY_VERSIONS[study_id].append(version_data)


def assert_mock_study_mutable(study_id: str):
    """Checks if the mock study is mutable (not LOCKED, PUBLISHED, or ARCHIVED)."""
    versions = MOCK_STUDY_VERSIONS.get(study_id, [])
    if versions:
        latest = versions[-1]

        # Verify signature on load!
        from apps.designer.delta import InvalidSignatureError, verify_version_signature

        if not verify_version_signature(latest):
            print(
                f"[AUDIT] [SECURITY_ALERT] Invalid or missing signature on load for StudyVersion: {latest.get('id')}."
            )
            raise InvalidSignatureError("INVALID_OR_MISSING_SIGNATURE")

        status = latest.get("status")
        if status in ("LOCKED", "PUBLISHED", "ARCHIVED"):
            from apps.designer.delta import ImmutabilityViolationError

            raise ImmutabilityViolationError("IMMUTABILITY_VIOLATION")


# --- Counters for Acceptance Criteria Tests ---
db_query_counts = {"terminology_lookups": 0}


def get_study_projection(study_id: str) -> Optional[Dict[str, Any]]:
    """Retrieves a study projection from the database.

    Args:
        study_id (str): The unique identifier of the study.

    Returns:
        Optional[Dict[str, Any]]: The study data dictionary, or None if not found.
    """
    if study_id not in MOCK_STUDIES:
        return None
    # Deep copy to avoid mutating cache/template
    import copy

    study = copy.deepcopy(MOCK_STUDIES[study_id])
    # Dynamically inject non-soft-deleted mock rules
    study["rules"] = [
        r for r in MOCK_RULES.get(study_id, []) if not r.get("is_deleted", False)
    ]
    # Dynamically inject non-soft-deleted mock eligibility criteria
    study["eligibility_criteria"] = [
        c for c in MOCK_ELIGIBILITY_CRITERIA.get(study_id, []) if not c.get("is_deleted", False)
    ]
    return study


def get_mock_rules(study_id: str) -> List[Dict[str, Any]]:
    """Retrieves all non-soft-deleted mock rules for a study."""
    return [r for r in MOCK_RULES.get(study_id, []) if not r.get("is_deleted", False)]


def get_mock_rule_by_id(study_id: str, rule_id: str) -> Optional[Dict[str, Any]]:
    """Retrieves a specific non-soft-deleted mock rule by ID."""
    for r in MOCK_RULES.get(study_id, []):
        if r["id"] == rule_id and not r.get("is_deleted", False):
            return r
    return None


def create_mock_rule(study_id: str, rule_data: Dict[str, Any]) -> Dict[str, Any]:
    """Creates and saves a mock rule under a study."""
    import uuid

    rule_id = f"rule_{uuid.uuid4().hex[:12]}"
    rule = {
        "id": rule_id,
        "study_id": study_id,
        "version_index": 1,
        "is_deleted": False,
        **rule_data,
    }
    if study_id not in MOCK_RULES:
        MOCK_RULES[study_id] = []
    MOCK_RULES[study_id].append(rule)
    return rule


def update_mock_rule(
    study_id: str, rule_id: str, rule_data: Dict[str, Any]
) -> Optional[Dict[str, Any]]:
    """Updates a mock rule and increments version index."""
    for r in MOCK_RULES.get(study_id, []):
        if r["id"] == rule_id and not r.get("is_deleted", False):
            r.update(rule_data)
            r["version_index"] += 1
            return r
    return None


def delete_mock_rule(study_id: str, rule_id: str) -> bool:
    """Soft-deletes a mock rule."""
    for r in MOCK_RULES.get(study_id, []):
        if r["id"] == rule_id and not r.get("is_deleted", False):
            r["is_deleted"] = True
            r["version_index"] += 1
            return True
    return False


def run_async(coro):
    """Runs an async coroutine synchronously, handling cases where an event loop is already running."""
    import asyncio
    import concurrent.futures

    try:
        asyncio.get_running_loop()
    except RuntimeError:
        # No running event loop in this thread, we can safely use asyncio.run
        return asyncio.run(coro)
    else:
        # An event loop is already running in this thread (e.g. FastAPI / ASGI context).
        # We run the coroutine in a separate thread using ThreadPoolExecutor to avoid blocking/nesting issues.
        with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
            return executor.submit(asyncio.run, coro).result()


def get_terminology_from_db(concept_id: str) -> Optional[Dict[str, Any]]:
    """Retrieves controlled terminology data from the database.

    Args:
        concept_id (str): The unique identifier of the terminology concept.

    Returns:
        Optional[Dict[str, Any]]: The terminology data dictionary, or None if not found.
    """
    # Increments counter to prove zero additional queries from cache hits
    db_query_counts["terminology_lookups"] += 1

    import os

    is_offline = os.getenv("TERMINOLOGY_OFFLINE", "").lower() in (
        "true",
        "1",
    ) or os.getenv("NCI_EVS_OFFLINE", "").lower() in ("true", "1")
    if is_offline:
        return MOCK_TERMINOLOGY.get(concept_id)

    from apps.designer.evs_client import EVSNotFoundError, NCIEVSClient

    client = NCIEVSClient()
    try:
        return run_async(client.get_concept(concept_id))
    except EVSNotFoundError:
        if concept_id in MOCK_TERMINOLOGY:
            return MOCK_TERMINOLOGY[concept_id]
        return None
    except Exception as e:
        if concept_id in MOCK_TERMINOLOGY:
            return MOCK_TERMINOLOGY[concept_id]
        raise e


# --- Controlled Terminology Cache ---
class TerminologyCache:
    """Thread-safe in-memory cache for controlled terminology lookups."""

    def __init__(self, max_size: int = 1000, ttl: Optional[float] = None) -> None:
        """Initializes the terminology cache.

        Args:
            max_size (int, optional): The maximum number of items to cache. Defaults to 1000.
            ttl (float, optional): Time-to-live in seconds. If not provided, looks up from environment.
        """
        self.max_size: int = max_size
        self._cache: Dict[str, tuple[Dict[str, Any], float]] = {}
        self._lock: threading.Lock = threading.Lock()

        if ttl is not None:
            self.ttl = float(ttl)
        else:
            import os

            env_ttl = os.getenv("TERMINOLOGY_CACHE_TTL") or os.getenv("CACHE_TTL")
            if env_ttl is not None:
                try:
                    self.ttl = float(env_ttl)
                except ValueError:
                    self.ttl = 3600.0
            else:
                self.ttl = 3600.0

    def get(self, concept_id: str) -> Optional[Dict[str, Any]]:
        """Retrieves a terminology concept from the cache or database.

        Args:
            concept_id (str): The unique identifier of the concept.

        Returns:
            Optional[Dict[str, Any]]: The terminology data, or None if not found.
        """
        import time

        now = time.time()
        expired_data = None

        with self._lock:
            if concept_id in self._cache:
                data, timestamp = self._cache[concept_id]
                if now - timestamp < self.ttl:
                    return data
                expired_data = data

        # Miss or Expired - fetch from DB
        try:
            data = get_terminology_from_db(concept_id)
        except Exception as e:
            if expired_data is not None:
                # DB is temporarily unreachable, fallback to expired data
                return expired_data
            raise e

        if data:
            with self._lock:
                store_time = time.time()
                if concept_id in self._cache:
                    self._cache[concept_id] = (data, store_time)
                else:
                    if len(self._cache) >= self.max_size:
                        # Basic eviction policy
                        self._cache.pop(next(iter(self._cache)))
                    self._cache[concept_id] = (data, store_time)
            return data
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


def check_dict_for_value(d: Any, target: str) -> bool:
    """
    Recursively scan nested dictionaries, lists, or custom objects to find if
    any value matches the target concept ID or code.
    """
    if isinstance(d, dict):
        for k, v in d.items():
            if v == target:
                return True
            if check_dict_for_value(v, target):
                return True
    elif isinstance(d, list):
        for item in d:
            if item == target:
                return True
            if check_dict_for_value(item, target):
                return True
    elif hasattr(d, "__dict__"):
        for k, v in d.__dict__.items():
            if not k.startswith("_"):
                if v == target:
                    return True
                if check_dict_for_value(v, target):
                    return True
    return False


async def is_concept_referenced_by_active_recruiting_study(
    concept_id: str, driver=None
) -> bool:
    """
    Check if a concept code/ID is referenced by any active recruiting study.
    Looks across both in-memory MOCK_STUDIES / MOCK_STUDY_VERSIONS and Neo4j database (if driver is active).
    """
    # 1. Check in-memory mock data
    for study_id, study_data in MOCK_STUDIES.items():
        is_active_recruiting = study_data.get("status") == "Active-Recruiting"

        # Check versions of this study
        versions = MOCK_STUDY_VERSIONS.get(study_id, [])
        for v in versions:
            if v.get("status") == "Active-Recruiting":
                is_active_recruiting = True
                break

        if is_active_recruiting:
            if check_dict_for_value(study_data, concept_id):
                return True

    # 2. Check Neo4j if driver is provided
    if driver is not None:
        try:
            query = """
            MATCH (s)
            WHERE (s:Study OR s:StudyVersion) AND s.status = 'Active-Recruiting'
            MATCH (s)-[*0..5]->(n)
            WHERE any(key IN keys(n) WHERE n[key] = $concept_id)
            RETURN count(n) > 0 AS is_referenced
            """
            async with driver.session() as session:
                result = await session.run(query, concept_id=concept_id)
                record = await result.single()
                if record:
                    return bool(record["is_referenced"])
        except Exception:
            # Gracefully ignore Neo4j query errors in offline/test mode
            pass

    return False
