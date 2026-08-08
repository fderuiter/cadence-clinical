"""
Decoupled Domain Facade for Study Designer.
Provides zero direct imports of database drivers or inline query strings.
"""

import asyncio
import functools
import re
from typing import Any

from apps.designer.src.domain.protocol_authoring.models import (
    CommentThread,
    SectionReviewStatus,
    SectionReviewTransition,
    Suggestion,
)


# =========================================================================
# 1. Pure Domain Exception Definitions
# =========================================================================
class ImmutabilityViolationError(PermissionError):
    """Raised when trying to mutate a locked, published, or archived graph or version."""

    pass


class LibraryObjectInUseError(Exception):
    """Raised when trying to directly mutate a library object/version that is currently in use by an active study."""

    pass


class LibraryObjectLockedActiveStudyError(Exception):
    """Raised when trying to directly mutate a library object/version that is referenced by an active recruiting study."""

    def __init__(self, object_id: str):
        self.object_id = object_id
        self.message = f"Library object '{object_id}' is referenced by an active recruiting study and cannot be directly mutated. To perform a modification, please initiate an amendment workflow."


class ConcurrentLockingError(Exception):
    """Raised when a concurrent locking/version conflict occurs."""

    pass


class InvalidSignatureError(Exception):
    """Raised when a study version signature is invalid or missing."""

    pass


# =========================================================================
# 2. Pure Domain Helper Functions
# =========================================================================
def assert_mock_study_version_mutable(study_version_id: str) -> None:
    """Checks if the mock study version is mutable (not LOCKED, PUBLISHED, or ARCHIVED)."""
    from apps.designer.db import MOCK_STUDY_VERSIONS

    for study_id, versions in MOCK_STUDY_VERSIONS.items():
        for ver in versions:
            if ver.get("id") == study_version_id:
                status = ver.get("status")
                if status in ("APPROVED", "SIGNED", "LOCKED", "PUBLISHED", "ARCHIVED"):
                    raise ImmutabilityViolationError("IMMUTABILITY_VIOLATION")
                return


def bump_version(version_tag: str, bump_type: str) -> str:
    """Parses the current version tag and returns the bumped semantic version."""
    match = re.match(r"^([a-zA-Z]*)(\d+(?:\.\d+)*)$", version_tag.strip())
    if not match:
        return version_tag + "-draft"
    prefix, numbers_str = match.groups()
    parts = [int(p) for p in numbers_str.split(".")]
    if len(parts) == 1:
        parts.append(0)
    bump_type_lower = bump_type.lower()
    is_major = "major" in bump_type_lower or "restructuring" in bump_type_lower
    if is_major:
        parts[0] += 1
        for i in range(1, len(parts)):
            parts[i] = 0
    else:
        if len(parts) >= 2:
            parts[1] += 1
            for i in range(2, len(parts)):
                parts[i] = 0
        else:
            parts[0] += 1
    return prefix + ".".join(str(p) for p in parts)


def verify_version_signature(version_props: dict[str, Any]) -> bool:
    """Verifies that the provided study version properties have a valid canonical signature."""
    signature = version_props.get("signature")
    if not signature:
        return False
    created_at = version_props.get("created_at")
    if created_at is not None:
        if hasattr(created_at, "isoformat"):
            created_at_val = created_at.isoformat()
        else:
            created_at_val = str(created_at)
    else:
        created_at_val = None
    import os

    from packages.security.signing import verify_canonical_signature

    secret_env = os.getenv("SIGNING_SECRET")
    if not secret_env:
        raise RuntimeError("SIGNING_SECRET environment variable is missing")
    secret = secret_env.encode("utf-8")
    study_id = version_props.get("study_id")
    version_index = version_props.get("version_index")
    version_tag = version_props.get("version_tag")
    created_by = version_props.get("created_by")
    change_reason = version_props.get("change_reason")
    if all(
        v is not None
        for v in (study_id, version_index, version_tag, created_by, change_reason)
    ):
        payload_new = {
            "study_id": study_id,
            "version_index": int(version_index),
            "version_tag": version_tag,
            "created_by": created_by,
            "created_at": created_at_val,
            "change_reason": change_reason,
        }
        if verify_canonical_signature(payload_new, signature, secret):
            return True
    payload_legacy = {
        "id": version_props.get("id") or "legacy_ver",
        "version_tag": version_props.get("version_tag") or "1.0",
        "status": version_props.get("status") or "DRAFT",
        "version_index": version_props.get("version_index") or 1,
        "created_by": version_props.get("created_by") or "system",
    }
    if created_at_val is not None:
        payload_legacy["created_at"] = created_at_val
    if "parent_version" in version_props:
        payload_legacy["parent_version"] = version_props["parent_version"]
    if "branch_name" in version_props and version_props["branch_name"] is not None:
        payload_legacy["branch_name"] = version_props["branch_name"]
    if "base_version" in version_props and version_props["base_version"] is not None:
        payload_legacy["base_version"] = version_props["base_version"]
    return verify_canonical_signature(payload_legacy, signature, secret)


def with_transaction_retry(
    max_retries: int = 5, initial_delay: float = 0.05, backoff_factor: float = 2.0
):
    """Decorator to retry transactions on transient database lock conflicts."""

    def decorator(func):
        @functools.wraps(func)
        async def wrapper(*args, **kwargs):
            retries = 0
            delay = initial_delay
            while True:
                try:
                    return await func(*args, **kwargs)
                except Exception as e:
                    err_name = e.__class__.__name__
                    err_msg = str(e).lower()
                    is_transient = (
                        (
                            err_name == "TransientError"
                            and "neo4j" in getattr(e.__class__, "__module__", "")
                        )
                        or (
                            err_name
                            in ("TransientError", "OperationalError", "LockError")
                        )
                        or "lock" in err_msg
                    )
                    if is_transient:
                        if retries >= max_retries:
                            raise e
                        retries += 1
                        await asyncio.sleep(delay)
                        delay *= backoff_factor
                    else:
                        raise e

        return wrapper

    return decorator


# =========================================================================
# 3. Decoupled In-Memory Shared State
# =========================================================================
MOCK_SOA_DATA: dict[str, dict[str, Any]] = {}
MOCK_COLLABORATION_DATA = {
    "section_statuses": {},
    "transitions": [],
    "threads": {},
    "suggestions": {},
}
MOCK_LIBRARY_INSTANCES: dict[str, list[dict[str, Any]]] = {}


def _init_mock_soa(study_version_id: str):
    if study_version_id not in MOCK_SOA_DATA:
        MOCK_SOA_DATA[study_version_id] = {
            "arms": {},
            "epochs": {},
            "visits": {},
            "procedures": {},
            "forms": {},
            "timing_windows": {},
            "actions": [],
            "links": [],
            "blocks": {},
        }


# =========================================================================
# 4. Dynamic Decoupled Registry and Delegates
# =========================================================================
_registry = {}


def register_implementation(name: str, func: Any) -> None:
    _registry[name] = func


def get_implementation(name: str) -> Any:
    impl = _registry.get(name)
    if not impl:
        raise RuntimeError(f"Implementation for '{name}' is not registered.")
    return impl


async def assert_study_version_mutable(tx, study_version_id: str):
    """
    Ensures that the study version is in a mutable state (DRAFT or ACTIVE).
    Raises ImmutabilityViolationError if the status of the study version is LOCKED, PUBLISHED, or ARCHIVED.
    """
    impl = get_implementation("assert_study_version_mutable")
    return await impl(tx=tx, study_version_id=study_version_id)


async def assert_graph_mutable(
    tx, study_id: str | None = None, object_id: str | None = None
):
    """
    Ensures that the study or library object is in a mutable state (DRAFT or ACTIVE).
    Raises ImmutabilityViolationError if the status of the latest version is LOCKED, PUBLISHED, or ARCHIVED.
    """
    impl = get_implementation("assert_graph_mutable")
    return await impl(tx=tx, study_id=study_id, object_id=object_id)


async def assert_library_object_mutable(
    driver_or_tx, object_id: str, version: int | None = None
):
    """
    Asserts that a library object/version is not referenced by an active/active-recruiting study
    through an instance/source relationship.
    If it is in use, raises LibraryObjectInUseError.
    """
    impl = get_implementation("assert_library_object_mutable")
    return await impl(driver_or_tx=driver_or_tx, object_id=object_id, version=version)


async def create_study_root(driver, study_id: str):
    """
    Creates a stable root node for a study.
    Requirement 1: Root-to-Value pattern.
    """
    impl = get_implementation("create_study_root")
    return await impl(driver=driver, study_id=study_id)


async def create_study_version(
    driver,
    study_id: str,
    version_id: str,
    version_tag: str,
    status: str,
    version_index: int,
    created_by: str,
    created_at: Any = None,
):
    """
    Creates a new StudyVersion node, links to Study via HAS_VERSION, and links to
    previous version via PREVIOUS_VERSION using pessimistic locks to serialize creation.
    Raises ConcurrentLockingError if version tag or index already exists.
    """
    impl = get_implementation("create_study_version")
    return await impl(
        driver=driver,
        study_id=study_id,
        version_id=version_id,
        version_tag=version_tag,
        status=status,
        version_index=version_index,
        created_by=created_by,
        created_at=created_at,
    )


def serialize_library_props(props: dict[str, Any]) -> dict[str, Any]:
    impl = get_implementation("serialize_library_props")
    return impl(props=props)


def deserialize_library_props(props: dict[str, Any]) -> dict[str, Any]:
    impl = get_implementation("deserialize_library_props")
    return impl(props=props)


async def create_library_object_version(
    driver,
    object_id: str,
    new_properties: dict[str, Any],
    is_amendment: bool = False,
    bypass_immutability: bool = False,
):
    """
    Requirement: Simplistic library objects version successfully without generating complex action nodes.
    Uses PREVIOUS_VERSION relationship.
    """
    impl = get_implementation("create_library_object_version")
    return await impl(
        driver=driver,
        object_id=object_id,
        new_properties=new_properties,
        is_amendment=is_amendment,
        bypass_immutability=bypass_immutability,
    )


async def get_latest_library_object(
    driver, object_id: str, sponsor_id: str, tenant_id: str = "tenant_default"
) -> dict[str, Any] | None:
    """
    Retrieves the latest version of a specific library object under a sponsor and tenant.
    """
    impl = get_implementation("get_latest_library_object")
    return await impl(
        driver=driver, object_id=object_id, sponsor_id=sponsor_id, tenant_id=tenant_id
    )


async def get_library_object_by_version(
    driver,
    object_id: str,
    sponsor_id: str,
    version: int,
    tenant_id: str = "tenant_default",
) -> dict[str, Any] | None:
    """
    Retrieves a specific version of a library object under a sponsor and tenant.
    """
    impl = get_implementation("get_library_object_by_version")
    return await impl(
        driver=driver,
        object_id=object_id,
        sponsor_id=sponsor_id,
        version=version,
        tenant_id=tenant_id,
    )


async def get_library_object_history(
    driver, object_id: str, sponsor_id: str, tenant_id: str = "tenant_default"
) -> list[dict[str, Any]]:
    """
    Retrieves the full version history of a library object under a sponsor and tenant,
    ordered from earliest version to latest version (by version ascending).
    """
    impl = get_implementation("get_library_object_history")
    return await impl(
        driver=driver, object_id=object_id, sponsor_id=sponsor_id, tenant_id=tenant_id
    )


async def list_library_objects(
    driver,
    sponsor_id: str,
    object_type: str | None = None,
    limit: int = 50,
    starting_after: str | None = None,
    tenant_id: str = "tenant_default",
) -> list[dict[str, Any]]:
    """
    Lists the latest version of each library object under a sponsor and tenant,
    supporting optional filtering by object type and Stripe-style cursor-compatible ordering.
    """
    impl = get_implementation("list_library_objects")
    return await impl(
        driver=driver,
        sponsor_id=sponsor_id,
        object_type=object_type,
        limit=limit,
        starting_after=starting_after,
        tenant_id=tenant_id,
    )


async def update_study_properties(
    driver, study_id: str, user_id: str, change_reason: str, properties: dict[str, Any]
):
    """
    Requirement 2: Discrete action nodes connected to modified fields via BEFORE and AFTER relationships.
    """
    impl = get_implementation("update_study_properties")
    return await impl(
        driver=driver,
        study_id=study_id,
        user_id=user_id,
        change_reason=change_reason,
        properties=properties,
    )


async def get_study_differences(
    driver, study_id: str, action_id1: str, action_id2: str
) -> list[dict[str, Any]]:
    """
    Requirement 3: Compute human-readable field-level differences between any two version actions of a study.
    Also covers: "A study designer can retrieve a flat list of field-level differences between any two version actions of a study."
    """
    impl = get_implementation("get_study_differences")
    return await impl(
        driver=driver, study_id=study_id, action_id1=action_id1, action_id2=action_id2
    )


async def create_rule_node(
    driver,
    study_id: str,
    user_id: str,
    change_reason: str,
    rule_id: str,
    rule_data: dict[str, Any],
):
    """
    Creates a new versioned rule under a study.
    Connects to an Action node via AFTER.
    """
    impl = get_implementation("create_rule_node")
    return await impl(
        driver=driver,
        study_id=study_id,
        user_id=user_id,
        change_reason=change_reason,
        rule_id=rule_id,
        rule_data=rule_data,
    )


async def update_rule_node(
    driver,
    study_id: str,
    rule_id: str,
    user_id: str,
    change_reason: str,
    rule_data: dict[str, Any],
):
    """
    Updates an existing rule by creating a new version.
    Connects to Action via BEFORE/AFTER and uses PREVIOUS_VERSION.
    """
    impl = get_implementation("update_rule_node")
    return await impl(
        driver=driver,
        study_id=study_id,
        rule_id=rule_id,
        user_id=user_id,
        change_reason=change_reason,
        rule_data=rule_data,
    )


async def delete_rule_node(
    driver, study_id: str, rule_id: str, user_id: str, change_reason: str
):
    """
    Soft-deletes a rule by creating a new deleted version.
    """
    impl = get_implementation("delete_rule_node")
    return await impl(
        driver=driver,
        study_id=study_id,
        rule_id=rule_id,
        user_id=user_id,
        change_reason=change_reason,
    )


async def get_rules_from_graph(driver, study_id: str) -> list[dict[str, Any]]:
    """
    Retrieves all active rules (not soft-deleted) for a study.
    """
    impl = get_implementation("get_rules_from_graph")
    return await impl(driver=driver, study_id=study_id)


async def amend_protocol_version(
    driver,
    study_id: str,
    user_id: str,
    change_reason: str,
    bump_type: str,
) -> dict[str, Any]:
    """
    Implements the formal Designer amendment fork operation without altering the source version.
    Returns a dict with:
        new_version: str
        status: str
        parent_version: str
        id: str
    """
    impl = get_implementation("amend_protocol_version")
    return await impl(
        driver=driver,
        study_id=study_id,
        user_id=user_id,
        change_reason=change_reason,
        bump_type=bump_type,
    )


async def create_study_arm(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    arm_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("create_study_arm")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_id=arm_id,
        properties=properties,
    )


async def update_study_arm(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    arm_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("update_study_arm")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_id=arm_id,
        properties=properties,
    )


async def create_epoch(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    epoch_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("create_epoch")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_id=epoch_id,
        properties=properties,
    )


async def update_epoch(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    epoch_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("update_epoch")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_id=epoch_id,
        properties=properties,
    )


async def create_visit(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    visit_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("create_visit")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=visit_id,
        properties=properties,
    )


async def update_visit(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    visit_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("update_visit")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=visit_id,
        properties=properties,
    )


async def create_procedure(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    procedure_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("create_procedure")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        procedure_id=procedure_id,
        properties=properties,
    )


async def update_procedure(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    procedure_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("update_procedure")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        procedure_id=procedure_id,
        properties=properties,
    )


async def create_timing_window(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    timing_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("create_timing_window")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        timing_id=timing_id,
        properties=properties,
    )


async def update_timing_window(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    timing_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("update_timing_window")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        timing_id=timing_id,
        properties=properties,
    )


async def link_epoch_to_visit(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    epoch_id: str,
    visit_id: str,
) -> bool:
    impl = get_implementation("link_epoch_to_visit")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_id=epoch_id,
        visit_id=visit_id,
    )


async def reorder_arms(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    arm_ids_ordered: list[str],
) -> bool:
    """
    Reorders study arms by updating their sequence sequentially.
    """
    impl = get_implementation("reorder_arms")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_ids_ordered=arm_ids_ordered,
    )


async def reorder_epochs(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    epoch_ids_ordered: list[str],
) -> bool:
    """
    Reorders study epochs by updating their sequence sequentially.
    """
    impl = get_implementation("reorder_epochs")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_ids_ordered=epoch_ids_ordered,
    )


async def reorder_procedures(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    procedure_ids_ordered: list[str],
) -> bool:
    """
    Reorders study procedures by updating their sequence sequentially.
    """
    impl = get_implementation("reorder_procedures")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        procedure_ids_ordered=procedure_ids_ordered,
    )


async def assign_visits_to_arm(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    arm_id: str,
    visit_ids: list[str],
) -> bool:
    """
    Assigns multiple visits to an arm. Bumps version_index of the arm and of target visits.
    """
    impl = get_implementation("assign_visits_to_arm")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_id=arm_id,
        visit_ids=visit_ids,
    )


async def assign_visits_to_epoch(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    epoch_id: str,
    visit_ids: list[str],
) -> bool:
    """
    Assigns multiple visits to an epoch. Bumps version_index of the epoch and of target visits.
    """
    impl = get_implementation("assign_visits_to_epoch")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_id=epoch_id,
        visit_ids=visit_ids,
    )


async def get_section_status(
    driver, study_version_id: str, section_id: str
) -> SectionReviewStatus:
    """
    Retrieves the current review status of an ICH section.
    """
    impl = get_implementation("get_section_status")
    return await impl(
        driver=driver, study_version_id=study_version_id, section_id=section_id
    )


async def assert_section_not_locked(
    driver, study_version_id: str, section_id: str | None
):
    """
    Raises ImmutabilityViolationError if the target section is in a locked or approved state.
    """
    impl = get_implementation("assert_section_not_locked")
    return await impl(
        driver=driver, study_version_id=study_version_id, section_id=section_id
    )


async def transition_section_status(
    driver,
    study_version_id: str,
    section_id: str,
    to_status: SectionReviewStatus,
    actor_id: str,
    actor_role: str,
    reason_for_change: str,
    signature_manifestation: dict[str, Any] | None = None,
) -> SectionReviewTransition:
    """
    Transitions the review status of an ICH section, complying with Part 11 and logging history.
    """
    impl = get_implementation("transition_section_status")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        section_id=section_id,
        to_status=to_status,
        actor_id=actor_id,
        actor_role=actor_role,
        reason_for_change=reason_for_change,
        signature_manifestation=signature_manifestation,
    )


async def get_section_transitions(
    driver, study_version_id: str, section_id: str
) -> list[SectionReviewTransition]:
    """
    Retrieves the chronological audit log of all transitions for a section.
    """
    impl = get_implementation("get_section_transitions")
    return await impl(
        driver=driver, study_version_id=study_version_id, section_id=section_id
    )


async def create_comment_thread(
    driver,
    study_version_id: str,
    section_id: str,
    block_id: str,
    text: str,
    created_by: str,
) -> CommentThread:
    """
    Creates a new block-anchored comment thread with the initial comment.
    """
    impl = get_implementation("create_comment_thread")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        section_id=section_id,
        block_id=block_id,
        text=text,
        created_by=created_by,
    )


async def get_comment_threads(
    driver, study_version_id: str, section_id: str
) -> list[CommentThread]:
    """
    Lists all comment threads and comments for a specific section.
    """
    impl = get_implementation("get_comment_threads")
    return await impl(
        driver=driver, study_version_id=study_version_id, section_id=section_id
    )


async def add_comment_to_thread(
    driver,
    study_version_id: str,
    thread_id: str,
    text: str,
    created_by: str,
) -> CommentThread:
    """
    Appends a new comment to an active thread, subject to review locking.
    """
    impl = get_implementation("add_comment_to_thread")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        thread_id=thread_id,
        text=text,
        created_by=created_by,
    )


async def resolve_comment_thread(
    driver,
    study_version_id: str,
    thread_id: str,
) -> CommentThread:
    """
    Sets a thread status to resolved.
    """
    impl = get_implementation("resolve_comment_thread")
    return await impl(
        driver=driver, study_version_id=study_version_id, thread_id=thread_id
    )


async def create_suggestion(
    driver,
    study_version_id: str,
    block_id: str,
    suggested_text: str,
    reason: str,
    created_by: str,
) -> Suggestion:
    """
    Proposes a suggested text edit anchored to a block.
    """
    impl = get_implementation("create_suggestion")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        block_id=block_id,
        suggested_text=suggested_text,
        reason=reason,
        created_by=created_by,
    )


async def get_suggestions(
    driver, study_version_id: str, block_id: str
) -> list[Suggestion]:
    """
    Retrieves all suggestions anchored to a block.
    """
    impl = get_implementation("get_suggestions")
    return await impl(
        driver=driver, study_version_id=study_version_id, block_id=block_id
    )


async def decide_suggestion(
    driver,
    study_version_id: str,
    suggestion_id: str,
    decision: str,
    decided_by: str,
    decision_reason: str,
) -> Suggestion:
    """
    Accepts or Rejects a suggestion. If accepted, verifies block freshness and updates text.
    """
    impl = get_implementation("decide_suggestion")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        suggestion_id=suggestion_id,
        decision=decision,
        decided_by=decided_by,
        decision_reason=decision_reason,
    )


async def approve_study_version_delta(
    driver,
    study_id: str,
    version_id: str,
    user_id: str,
    change_reason: str,
    signature_manifestation_payload: dict[str, Any],
) -> dict[str, Any]:
    """
    Saves the APPROVED status and the signature manifestation in the study version record
    and records an Action in the append-only history.
    """
    impl = get_implementation("approve_study_version_delta")
    return await impl(
        driver=driver,
        study_id=study_id,
        version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        signature_manifestation_payload=signature_manifestation_payload,
    )


async def link_visit_to_procedure(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    visit_id: str,
    procedure_id: str,
) -> bool:
    impl = get_implementation("link_visit_to_procedure")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=visit_id,
        procedure_id=procedure_id,
    )


async def assign_activities_to_visit(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    visit_id: str,
    procedure_ids: list[str],
) -> bool:
    """
    Links multiple procedures (activities) to a visit, reusing link_visit_to_procedure.
    """
    impl = get_implementation("assign_activities_to_visit")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=visit_id,
        procedure_ids=procedure_ids,
    )


async def link_visit_or_procedure_to_timing(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    source_id: str,
    timing_id: str,
    source_type: str = "visit",
) -> bool:
    impl = get_implementation("link_visit_or_procedure_to_timing")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        source_id=source_id,
        timing_id=timing_id,
        source_type=source_type,
    )


async def link_arm_applicability(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    arm_id: str,
    target_id: str,
    target_type: str = "visit",
) -> bool:
    impl = get_implementation("link_arm_applicability")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_id=arm_id,
        target_id=target_id,
        target_type=target_type,
    )


async def get_soa_matrix_projection(driver, study_version_id: str) -> dict[str, Any]:
    """
    Returns a read-only projection representing the complete matrix shape (SoAMatrixView).
    Consistently handles both real Neo4j driver or the mock/in-memory fallback, and
    excludes retired/deleted data from the projection.
    """
    impl = get_implementation("get_soa_matrix_projection")
    return await impl(driver=driver, study_version_id=study_version_id)


async def create_block(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    block_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("create_block")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        block_id=block_id,
        properties=properties,
    )


async def update_block(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    block_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("update_block")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        block_id=block_id,
        properties=properties,
    )


async def delete_block(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    block_id: str,
) -> str:
    impl = get_implementation("delete_block")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        block_id=block_id,
    )


async def get_block(
    driver,
    study_version_id: str,
    block_id: str,
) -> dict[str, Any] | None:
    impl = get_implementation("get_block")
    return await impl(
        driver=driver, study_version_id=study_version_id, block_id=block_id
    )


async def list_blocks(
    driver,
    study_version_id: str,
) -> list[dict[str, Any]]:
    impl = get_implementation("list_blocks")
    return await impl(driver=driver, study_version_id=study_version_id)


async def reorder_blocks(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    block_ids_ordered: list[str],
) -> bool:
    impl = get_implementation("reorder_blocks")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        block_ids_ordered=block_ids_ordered,
    )


async def reorder_visits(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    visit_ids_ordered: list[str],
) -> bool:
    """
    Reorders visits by updating their sequence sequentially.
    """
    impl = get_implementation("reorder_visits")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_ids_ordered=visit_ids_ordered,
    )


async def create_form(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    form_id: str,
    properties: dict[str, Any],
) -> str:
    impl = get_implementation("create_form")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        form_id=form_id,
        properties=properties,
    )


async def link_visit_to_form(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    visit_id: str,
    form_id: str,
) -> bool:
    impl = get_implementation("link_visit_to_form")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=visit_id,
        form_id=form_id,
    )


async def compute_graph_diff(
    driver, study_id: str, version_id1: str, version_id2: str
) -> dict:
    """
    Traverses study tree levels: StudyVersion -> Epoch -> Visit -> Form.
    Identifies additions, modifications, and deletions.
    Keys forms by form_key and compares xform_definition_xml.
    """
    impl = get_implementation("compute_graph_diff")
    return await impl(
        driver=driver,
        study_id=study_id,
        version_id1=version_id1,
        version_id2=version_id2,
    )


async def check_library_object_exists_any_sponsor(
    driver,
    object_id: str,
    version: int | None = None,
    tenant_id: str = "tenant_default",
) -> dict[str, Any] | None:
    """
    Looks up a library object across all sponsors under the same tenant to verify its existence
    and retrieve its metadata (including sponsor_id).
    """
    impl = get_implementation("check_library_object_exists_any_sponsor")
    return await impl(
        driver=driver, object_id=object_id, version=version, tenant_id=tenant_id
    )


async def check_study_exists_any_sponsor(
    driver, study_id: str, tenant_id: str = "tenant_default"
) -> dict[str, Any] | None:
    """
    Looks up a Study across all sponsors under the same tenant to verify existence and check sponsor ownership.
    """
    impl = get_implementation("check_study_exists_any_sponsor")
    return await impl(driver=driver, study_id=study_id, tenant_id=tenant_id)


async def instantiate_library_object_in_study(
    driver,
    study_id: str,
    library_object_id: str,
    version: int | None,
    sponsor_id: str,
    user_id: str,
    tenant_id: str = "tenant_default",
) -> dict[str, Any]:
    """
    Clones a selected library object/version into a study as a distinct study-scoped object.
    Records an INSTANTIATED_FROM relationship containing source linkage for traceability.
    """
    impl = get_implementation("instantiate_library_object_in_study")
    return await impl(
        driver=driver,
        study_id=study_id,
        library_object_id=library_object_id,
        version=version,
        sponsor_id=sponsor_id,
        user_id=user_id,
        tenant_id=tenant_id,
    )


async def update_library_instance_in_study(
    driver,
    study_id: str,
    instance_id: str,
    payload: dict[str, Any],
    sponsor_id: str,
    user_id: str,
    tenant_id: str = "tenant_default",
) -> dict[str, Any]:
    """
    Updates the payload of a study-scoped library instance.
    Leaves the parent library object completely immutable.
    """
    impl = get_implementation("update_library_instance_in_study")
    return await impl(
        driver=driver,
        study_id=study_id,
        instance_id=instance_id,
        payload=payload,
        sponsor_id=sponsor_id,
        user_id=user_id,
        tenant_id=tenant_id,
    )


async def get_library_instance_in_study(
    driver,
    study_id: str,
    instance_id: str,
    sponsor_id: str,
    tenant_id: str = "tenant_default",
) -> dict[str, Any]:
    """
    Retrieves a study-scoped library instance and its linked source metadata.
    """
    impl = get_implementation("get_library_instance_in_study")
    return await impl(
        driver=driver,
        study_id=study_id,
        instance_id=instance_id,
        sponsor_id=sponsor_id,
        tenant_id=tenant_id,
    )


async def create_eligibility_criterion(
    driver,
    study_id: str,
    user_id: str,
    change_reason: str,
    criterion_id: str,
    criterion_data: dict[str, Any],
) -> str:
    """
    Creates a new stable EligibilityCriterion root node and its first version EligibilityCriterionVersion.
    """
    impl = get_implementation("create_eligibility_criterion")
    return await impl(
        driver=driver,
        study_id=study_id,
        user_id=user_id,
        change_reason=change_reason,
        criterion_id=criterion_id,
        criterion_data=criterion_data,
    )


async def update_eligibility_criterion(
    driver,
    study_id: str,
    criterion_id: str,
    user_id: str,
    change_reason: str,
    criterion_data: dict[str, Any],
) -> int:
    """
    Bumps version index and creates a new EligibilityCriterionVersion node connected to previous one.
    """
    impl = get_implementation("update_eligibility_criterion")
    return await impl(
        driver=driver,
        study_id=study_id,
        criterion_id=criterion_id,
        user_id=user_id,
        change_reason=change_reason,
        criterion_data=criterion_data,
    )


async def get_eligibility_criteria_from_graph(
    driver, study_id: str
) -> list[dict[str, Any]]:
    """
    Retrieves all non-deleted active eligibility criteria for a specific clinical study.
    """
    impl = get_implementation("get_eligibility_criteria_from_graph")
    return await impl(driver=driver, study_id=study_id)


async def propagate_soa_mutation(
    driver, study_version_id: str, entity_id: str, user_id: str, change_reason: str
):
    """
    Finds and updates any blocks that are derived from the specified SoA entity,
    marking them as derived_from_soa = True and writing an audit trail.
    """
    impl = get_implementation("propagate_soa_mutation")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        entity_id=entity_id,
        user_id=user_id,
        change_reason=change_reason,
    )


async def retire_soa_entity(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    entity_id: str,
    entity_type: str,  # "arms", "epochs", "visits", "procedures", "timing_windows"
) -> str:
    """
    Soft-retires/deletes an active SoA entity by creating a new version of the node
    with version_index bumped and is_retired/is_deleted set to True, and disconnects
    it from the StudyVersion root non-destructively, preserving the history.
    """
    impl = get_implementation("retire_soa_entity")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        entity_id=entity_id,
        entity_type=entity_type,
    )


async def retire_epoch_visit_link(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    epoch_id: str,
    visit_id: str,
) -> bool:
    """
    Retires/deletes an Epoch-to-Visit link non-destructively, logging the deletion action.
    """
    impl = get_implementation("retire_epoch_visit_link")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_id=epoch_id,
        visit_id=visit_id,
    )


async def retire_visit_procedure_link(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    visit_id: str,
    procedure_id: str,
) -> bool:
    """
    Retires/deletes a Visit-to-Procedure link non-destructively, logging the deletion action.
    """
    impl = get_implementation("retire_visit_procedure_link")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=visit_id,
        procedure_id=procedure_id,
    )


async def retire_timing_link(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    source_id: str,
    timing_id: str,
    source_type: str = "visit",
) -> bool:
    """
    Retires/deletes a Timing Window link non-destructively, logging the deletion action.
    """
    impl = get_implementation("retire_timing_link")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        source_id=source_id,
        timing_id=timing_id,
        source_type=source_type,
    )


async def retire_arm_applicability_link(
    driver,
    study_version_id: str,
    user_id: str,
    change_reason: str,
    arm_id: str,
    target_id: str,
    target_type: str = "visit",
) -> bool:
    """
    Retires/deletes an Arm Applicability link non-destructively, logging the deletion action.
    """
    impl = get_implementation("retire_arm_applicability_link")
    return await impl(
        driver=driver,
        study_version_id=study_version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_id=arm_id,
        target_id=target_id,
        target_type=target_type,
    )
