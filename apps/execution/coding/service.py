"""
Core Medical Coding Service (Hexagonal Decoupled).
"""

# Fully compliant with Hexagonal Port-and-Adapter architecture.

import enum
import logging
from datetime import UTC, datetime
from typing import Any

# Matcher utility
from apps.execution.coding.matcher import match_verbatim_term

# Core Exceptions
from apps.execution.exceptions import (
    DictionaryNotFoundError,
    InvalidCodingActionError,
)

logger = logging.getLogger(__name__)


# Standard Python Enums for Decoupled Domain
class CodingState(enum.StrEnum):
    UNCODED = "UNCODED"
    SUGGESTED = "SUGGESTED"
    CODED = "CODED"
    AUTO_CODED = "AUTO_CODED"
    QUERY_PENDING = "QUERY_PENDING"
    RECODING_REQUIRED = "RECODING_REQUIRED"


class RecodingState(enum.StrEnum):
    NONE = "NONE"
    PENDING = "PENDING"
    COMPLETED = "COMPLETED"
    CANCELLED = "CANCELLED"


class DictionaryType(enum.StrEnum):
    MEDDRA = "MEDDRA"
    WHODRUG = "WHODRUG"
    LOINC = "LOINC"
    SNOMED = "SNOMED"


def _get_repository(repo_or_session: Any) -> Any:
    """Helper to resolve database session or repository adapter."""
    if hasattr(repo_or_session, "execute"):
        from apps.execution.coding.adapters import SQLCodingRepository

        return SQLCodingRepository(repo_or_session)
    return repo_or_session


def _get_value(val: Any) -> str:
    """Helper to get raw string value of an enum or string."""
    if hasattr(val, "value"):
        return str(val.value)
    return str(val)


async def search_dictionary(
    session: Any,
    term: str,
    dictionary_type: str,
    version: str,
    target_level: str | None = None,
) -> dict[str, Any]:
    """Delegates interactive terminology search or auto-complete lookup to match_verbatim_term."""
    if not term or not term.strip():
        raise ValueError("Term must be a non-empty string")
    if not version or not version.strip():
        raise ValueError("Version must be a non-empty string")

    res = await match_verbatim_term(
        session=session,
        verbatim=term.strip(),
        dictionary_type=dictionary_type.upper(),
        version=version.strip(),
        target_level=target_level,
    )

    matches = []
    dict_type_upper = dictionary_type.upper()

    if dict_type_upper == "MEDDRA":
        if res.get("match"):
            parent_match = res["match"]
            score = parent_match.get("score", 0.0)
            if parent_match.get("hierarchies"):
                for h in parent_match.get("hierarchies", []):
                    matches.append(
                        {
                            "llt_code": h.get("llt_code") or "",
                            "llt_name": h.get("llt_name") or "",
                            "pt_code": h.get("pt_code") or "",
                            "pt_name": h.get("pt_name") or "",
                            "hlt_code": h.get("hlt_code") or "",
                            "hlt_name": h.get("hlt_name") or "",
                            "hlgt_code": h.get("hlgt_code") or "",
                            "hlgt_name": h.get("hlgt_name") or "",
                            "soc_code": h.get("soc_code") or "",
                            "soc_name": h.get("soc_name") or "",
                            "primary_soc_flag": h.get("primary_soc_flag"),
                            "score": score,
                        }
                    )
            else:
                is_llt = parent_match.get("level") == "LLT"
                matches.append(
                    {
                        "llt_code": parent_match.get("code") if is_llt else "",
                        "llt_name": parent_match.get("term_name") if is_llt else "",
                        "pt_code": parent_match.get("code") if not is_llt else "",
                        "pt_name": parent_match.get("term_name") if not is_llt else "",
                        "hlt_code": "",
                        "hlt_name": "",
                        "hlgt_code": "",
                        "hlgt_name": "",
                        "soc_code": "",
                        "soc_name": "",
                        "primary_soc_flag": None,
                        "score": score,
                    }
                )
        elif res.get("suggestions"):
            for sug in res["suggestions"]:
                score = sug.get("score", 0.0)
                if sug.get("hierarchies"):
                    for h in sug.get("hierarchies", []):
                        matches.append(
                            {
                                "llt_code": h.get("llt_code") or "",
                                "llt_name": h.get("llt_name") or "",
                                "pt_code": h.get("pt_code") or "",
                                "pt_name": h.get("pt_name") or "",
                                "hlt_code": h.get("hlt_code") or "",
                                "hlt_name": h.get("hlt_name") or "",
                                "hlgt_code": h.get("hlgt_code") or "",
                                "hlgt_name": h.get("hlgt_name") or "",
                                "soc_code": h.get("soc_code") or "",
                                "soc_name": h.get("soc_name") or "",
                                "primary_soc_flag": h.get("primary_soc_flag"),
                                "score": score,
                            }
                        )
                else:
                    is_llt = sug.get("level") == "LLT"
                    matches.append(
                        {
                            "llt_code": sug.get("code") if is_llt else "",
                            "llt_name": sug.get("term_name") if is_llt else "",
                            "pt_code": sug.get("code") if not is_llt else "",
                            "pt_name": sug.get("term_name") if not is_llt else "",
                            "hlt_code": "",
                            "hlt_name": "",
                            "hlgt_code": "",
                            "hlgt_name": "",
                            "soc_code": "",
                            "soc_name": "",
                            "primary_soc_flag": None,
                            "score": score,
                        }
                    )
        return {
            "status": res.get("status", "UNCODABLE"),
            "matches": matches,
        }

    if dict_type_upper == "WHODRUG":
        whodrug_matches = []
        if res.get("match"):
            m = res["match"]
            whodrug_matches.append(
                {
                    "drug_code": m.get("drug_code") or "",
                    "preferred_name": m.get("preferred_name") or "",
                    "drug_name": m.get("drug_name"),
                    "score": m.get("score", 0.0),
                    "atc_context": [
                        {
                            "atc_code": a.get("atc_code") or "",
                            "description": a.get("description") or "",
                        }
                        for a in m.get("atc_context", [])
                    ],
                    "ingredients": [
                        {
                            "ingredient_code": i.get("ingredient_code") or "",
                            "ingredient_name": i.get("ingredient_name") or "",
                        }
                        for i in m.get("ingredients", [])
                    ],
                }
            )
        elif res.get("suggestions"):
            for sug in res["suggestions"]:
                whodrug_matches.append(
                    {
                        "drug_code": sug.get("drug_code") or "",
                        "preferred_name": sug.get("preferred_name") or "",
                        "drug_name": sug.get("drug_name"),
                        "score": sug.get("score", 0.0),
                        "atc_context": [
                            {
                                "atc_code": a.get("atc_code") or "",
                                "description": a.get("description") or "",
                            }
                            for a in sug.get("atc_context", [])
                        ],
                        "ingredients": [
                            {
                                "ingredient_code": i.get("ingredient_code") or "",
                                "ingredient_name": i.get("ingredient_name") or "",
                            }
                            for i in sug.get("ingredients", [])
                        ],
                    }
                )

        return {
            "status": res.get("status", "UNCODABLE"),
            "matches": whodrug_matches,
        }

    raise DictionaryNotFoundError(f"Unsupported dictionary type: {dictionary_type}")


async def list_coding_assignments(
    session: Any,
    observation_id: str | None = None,
    status: str | None = None,
    verbatim_text: str | None = None,
    dictionary_type: str | None = None,
) -> list[Any]:
    """Retrieves and filters active, non-deleted medical coding assignments."""
    repo = _get_repository(session)
    return await repo.list_assignments(
        observation_id=observation_id,
        status=status,
        verbatim_text=verbatim_text,
        dictionary_type=dictionary_type,
    )


async def get_coding_assignment(
    session: Any,
    assignment_id: str,
) -> Any:
    """Retrieves a single active, non-deleted coding assignment by ID."""
    repo = _get_repository(session)
    return await repo.get_assignment(assignment_id)


async def process_coding_action(
    session: Any,
    assignment_id: str,
    action: str,
    code: str | None = None,
    term: str | None = None,
    suggestion_index: int | None = None,
    reason_for_change: str | None = None,
    actor: str = "system",
) -> Any:
    """Processes a data manager coding action (ACCEPT, OVERRIDE, or QUERY).

    Accepts a suggestion or submits a manual override, persisting results and updating the ledger.
    """
    repo = _get_repository(session)
    action_upper = action.upper()
    if action_upper not in ("ACCEPT", "OVERRIDE", "QUERY"):
        raise InvalidCodingActionError(
            f"Invalid action '{action}'. Allowed actions: ACCEPT, OVERRIDE, QUERY."
        )

    # Resolve actor from context or parameter
    resolved_actor = actor
    if not resolved_actor or resolved_actor == "system":
        try:
            from apps.execution.database.context import current_user_id

            resolved_actor = current_user_id.get() or "system"
        except (LookupError, ValueError):  # fmt: skip
            resolved_actor = "system"

    # 1. Fetch existing assignment
    assignment = await repo.get_assignment(assignment_id)

    old_code = assignment.coded_code
    old_term = assignment.coded_term
    old_version = assignment.dictionary_version
    dict_type = assignment.dictionary_type
    version = assignment.dictionary_version
    old_hierarchy = assignment.hierarchy or {}

    status = assignment.status
    coded_code = assignment.coded_code
    coded_term = assignment.coded_term
    score = assignment.score
    hierarchy = assignment.hierarchy

    if action_upper == "ACCEPT":
        sug_list = assignment.suggestions or []
        if not isinstance(sug_list, list):
            sug_list = []

        sug = None
        if suggestion_index is not None:
            if suggestion_index < 0 or suggestion_index >= len(sug_list):
                raise InvalidCodingActionError("Invalid suggestion_index")
            sug = sug_list[suggestion_index]
        elif code:
            # Look for suggestion with matching code
            for s in sug_list:
                s_code = s.get("code") or s.get("drug_code")
                if s_code == code:
                    sug = s
                    break
            if sug is None:
                raise InvalidCodingActionError(
                    f"The provided code '{code}' does not match any available suggestions. Use OVERRIDE for manual coding."
                )
        else:
            if len(sug_list) > 0:
                sug = sug_list[0]
            else:
                raise InvalidCodingActionError(
                    "No suggestions available to ACCEPT. Use OVERRIDE instead."
                )

        # Resolve suggestion fields
        coded_code = sug.get("code") or sug.get("drug_code")
        coded_term = (
            sug.get("term_name") or sug.get("preferred_name") or sug.get("drug_name")
        )
        score = sug.get("score")
        if _get_value(dict_type) == "MEDDRA":
            hierarchy = {"hierarchies": sug.get("hierarchies") or []}
        else:
            hierarchy = {
                "atc_context": sug.get("atc_context") or [],
                "ingredients": sug.get("ingredients") or [],
            }

        # Validate existence of chosen code
        if _get_value(dict_type) == "MEDDRA":
            term_rec = await repo.validate_meddra_term(version, coded_code)
            if not term_rec:
                raise InvalidCodingActionError(
                    f"Invalid code '{coded_code}' for MedDRA version '{version}'."
                )
        elif _get_value(dict_type) == "WHODRUG":
            rec_rec = await repo.validate_whodrug_record(version, coded_code)
            if not rec_rec:
                raise InvalidCodingActionError(
                    f"Invalid drug code '{coded_code}' for WHODrug version '{version}'."
                )

        status = CodingState.CODED
        assignment.recoding_status = RecodingState.NONE

    elif action_upper == "OVERRIDE":
        if not reason_for_change or not reason_for_change.strip():
            raise InvalidCodingActionError(
                "reason_for_change is required for OVERRIDE action and cannot be empty."
            )
        if not code or not code.strip():
            raise InvalidCodingActionError("code is required for OVERRIDE action.")
        if not term or not term.strip():
            raise InvalidCodingActionError("term is required for OVERRIDE action.")

        coded_code = code.strip()
        coded_term = term.strip()

        # Validate existence and retrieve hierarchy
        if _get_value(dict_type) == "MEDDRA":
            term_record = await repo.validate_meddra_term(version, coded_code)
            if not term_record:
                raise InvalidCodingActionError(
                    f"Invalid code '{coded_code}' for MedDRA version '{version}'."
                )

            # Re-derive hierarchy
            hierarchy_list = await repo.get_meddra_hierarchy(term_record, version)
            hierarchy = {"hierarchies": hierarchy_list}

        elif _get_value(dict_type) == "WHODRUG":
            rec_record = await repo.validate_whodrug_record(version, coded_code)
            if not rec_record:
                raise InvalidCodingActionError(
                    f"Invalid drug code '{coded_code}' for WHODrug version '{version}'."
                )

            # Re-derive context
            atc_context, ingredients = await repo.get_whodrug_context(
                rec_record, version
            )
            hierarchy = {"atc_context": atc_context, "ingredients": ingredients}

        score = 1.0  # Perfect manual certainty
        status = CodingState.CODED
        assignment.recoding_status = RecodingState.NONE

    elif action_upper == "QUERY":
        status = CodingState.QUERY_PENDING
        coded_code = None
        coded_term = None
        score = None
        hierarchy = None
        assignment.recoding_status = RecodingState.NONE

        study_id = "STUDY-001"
        site_id = None
        subject_id = "SUBJ-001"
        visit_id = None
        domain = assignment.domain or "AE"
        test_code = assignment.source_field or "AETERM"

        if assignment.observation_id:
            obs = await repo.get_observation(assignment.observation_id)
            if obs:
                study_id = obs.study_id or study_id
                site_id = getattr(obs, "site_id", None)
                subject_id = obs.subject_id or subject_id
                visit_id = obs.visit_id
                domain = obs.domain or domain
                test_code = obs.test_code or test_code

        from apps.execution.database.models import ClinicalQuery

        q_explanation = (
            reason_for_change
            or f"The verbatim term '{assignment.verbatim_text}' in field {test_code} is uncodable. Please clarify spelling or clinical term."
        )
        query = ClinicalQuery(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            domain=domain,
            test_code=test_code,
            status="OPEN",
            explanation=q_explanation,
            message=q_explanation,
            observation_id=assignment.observation_id,
            origin="SYSTEM_CODING",
            query_type="SYSTEM_CODING",
            form_id=f"{domain.upper()}_FORM",
            field_id=test_code,
            action_required="CLARIFY_VERBATIM",
            priority="HIGH",
            created_by=resolved_actor,
        )
        await repo.save_query(query)

    # 2. Update assignment state
    assignment.status = status
    assignment.coded_code = coded_code
    assignment.coded_term = coded_term
    assignment.score = score
    assignment.hierarchy = hierarchy
    assignment.assigned_by = resolved_actor
    assignment.assigned_at = datetime.now(UTC).replace(tzinfo=None)

    await repo.save_assignment(assignment)

    # 3. Create a ledger record for ACCEPT or OVERRIDE
    if action_upper in ("ACCEPT", "OVERRIDE"):
        await repo.add_ledger(
            {
                "assignment_id": assignment.id,
                "verbatim_text": assignment.verbatim_text,
                "observation_id": assignment.observation_id,
                "dictionary_type": dict_type,
                "old_dictionary_version": old_version if old_code else None,
                "old_coded_code": old_code,
                "old_coded_term": old_term,
                "new_dictionary_version": version,
                "new_coded_code": coded_code,
                "new_coded_term": coded_term,
                "recoding_reason": reason_for_change
                or f"Manual decision: {action_upper}",
                "decision_by": resolved_actor,
                "decision_at": datetime.now(UTC).replace(tzinfo=None),
                "old_hierarchy": old_hierarchy,
                "new_hierarchy": hierarchy,
                "recoding_status": assignment.recoding_status,
            }
        )

        # Transition query closure to an Asynchronous EDC Query Closure via the transactional outbox
        active_queries = await repo.get_active_queries(assignment.observation_id)
        for active_q in active_queries:
            import uuid

            from apps.execution.database.models import IntegrationOutbox

            payload = {
                "actor": resolved_actor,
                "timestamp": datetime.now(UTC).isoformat(),
                "observation_id": assignment.observation_id,
                "query_id": active_q.id,
                "justification": reason_for_change
                or f"Manual decision: {action_upper}",
                "action": action_upper,
                "coded_code": coded_code,
            }

            outbox_entry = IntegrationOutbox(
                id=str(uuid.uuid4()),
                event_type="EDC_QUERY_RESOLVE",
                payload=payload,
                status="PENDING",
                attempts=0,
                correlation_id=f"query-resolve-{active_q.id}-{uuid.uuid4().hex[:8]}",
                created_by=resolved_actor,
                reason_for_change=reason_for_change
                or f"Manual decision: {action_upper}",
            )
            await repo.add_outbox_entry(outbox_entry)

    return assignment


async def batch_assign_codes(
    session: Any,
    assignment_ids: list[str] | None = None,
    items: list[dict[str, Any]] | None = None,
    code: str | None = None,
    term: str | None = None,
    dictionary_type: str | None = None,
    dictionary_version: str | None = None,
    reason: str | None = None,
    action: str = "ACCEPT",
    actor: str = "system",
) -> dict[str, Any]:
    """Performs batch medical coding assignment across multiple assignments with GxP audit logging."""
    repo = _get_repository(session)

    # Resolve actor from context or parameter
    resolved_actor = actor
    if not resolved_actor or resolved_actor == "system":
        try:
            from apps.execution.database.context import current_user_id

            resolved_actor = current_user_id.get() or "system"
        except (LookupError, ValueError):  # fmt: skip
            resolved_actor = "system"

    work_items: list[dict[str, Any]] = []
    if items:
        for it in items:
            work_items.append(
                {
                    "assignment_id": it.get("assignment_id") or it.get("id"),
                    "code": it.get("code") or code,
                    "term": it.get("term") or term,
                    "action": it.get("action") or action or "ACCEPT",
                    "suggestion_index": it.get("suggestion_index"),
                    "reason_for_change": it.get("reason_for_change")
                    or it.get("reason")
                    or reason,
                    "dictionary_version": it.get("dictionary_version")
                    or dictionary_version,
                }
            )
    elif assignment_ids:
        for aid in assignment_ids:
            work_items.append(
                {
                    "assignment_id": aid,
                    "code": code,
                    "term": term,
                    "action": action or "ACCEPT",
                    "suggestion_index": None,
                    "reason_for_change": reason,
                    "dictionary_version": dictionary_version,
                }
            )

    results: list[dict[str, Any]] = []
    success_count = 0
    failed_count = 0

    for item in work_items:
        aid = item["assignment_id"]
        if not aid:
            failed_count += 1
            results.append(
                {
                    "assignment_id": "",
                    "status": "FAILED",
                    "error": "Missing assignment_id",
                }
            )
            continue

        try:
            assignment = await repo.get_assignment(aid)
            dict_type_str = _get_value(assignment.dictionary_type)
            version = item["dictionary_version"] or assignment.dictionary_version
            item_action = (item["action"] or "ACCEPT").upper()
            item_code = item["code"]
            item_term = item["term"]
            item_reason = (
                item["reason_for_change"]
                or reason
                or f"Batch assignment action: {item_action}"
            )

            old_code = assignment.coded_code
            old_term = assignment.coded_term
            old_version = assignment.dictionary_version
            old_hierarchy = assignment.hierarchy or {}

            coded_code = None
            coded_term = None
            score = 1.0
            hierarchy = None

            if item_action == "ACCEPT":
                if item_code:
                    coded_code = item_code.strip()
                    if item_term:
                        coded_term = item_term.strip()
                    if dict_type_str == "MEDDRA":
                        term_rec = await repo.validate_meddra_term(version, coded_code)
                        if term_rec:
                            if not coded_term:
                                coded_term = term_rec.term_name
                            hier_list = await repo.get_meddra_hierarchy(
                                term_rec, version
                            )
                            hierarchy = {"hierarchies": hier_list}
                        else:
                            coded_term = coded_term or "Coded Term"
                    elif dict_type_str == "WHODRUG":
                        rec_rec = await repo.validate_whodrug_record(
                            version, coded_code
                        )
                        if rec_rec:
                            if not coded_term:
                                coded_term = rec_rec.preferred_name
                            atc_ctx, ings = await repo.get_whodrug_context(
                                rec_rec, version
                            )
                            hierarchy = {"atc_context": atc_ctx, "ingredients": ings}
                        else:
                            coded_term = coded_term or "Coded Drug"
                else:
                    # Pick from suggestions
                    sug_list = assignment.suggestions or []
                    if not isinstance(sug_list, list) or len(sug_list) == 0:
                        raise InvalidCodingActionError(
                            f"No suggestions available for assignment '{aid}'. Specify code for batch assignment."
                        )
                    sug_idx = item.get("suggestion_index") or 0
                    if sug_idx < 0 or sug_idx >= len(sug_list):
                        sug_idx = 0
                    sug = sug_list[sug_idx]
                    coded_code = sug.get("code") or sug.get("drug_code")
                    coded_term = (
                        sug.get("term_name")
                        or sug.get("preferred_name")
                        or sug.get("drug_name")
                    )
                    score = sug.get("score", 1.0)
                    if dict_type_str == "MEDDRA":
                        hierarchy = {"hierarchies": sug.get("hierarchies") or []}
                    else:
                        hierarchy = {
                            "atc_context": sug.get("atc_context") or [],
                            "ingredients": sug.get("ingredients") or [],
                        }

            elif item_action == "OVERRIDE":
                if not item_code:
                    raise InvalidCodingActionError(
                        f"Code is required for OVERRIDE action on assignment '{aid}'."
                    )
                coded_code = item_code.strip()
                if item_term:
                    coded_term = item_term.strip()
                if dict_type_str == "MEDDRA":
                    term_rec = await repo.validate_meddra_term(version, coded_code)
                    if term_rec:
                        if not coded_term:
                            coded_term = term_rec.term_name
                        hier_list = await repo.get_meddra_hierarchy(term_rec, version)
                        hierarchy = {"hierarchies": hier_list}
                    else:
                        coded_term = coded_term or "Coded Term"
                elif dict_type_str == "WHODRUG":
                    rec_rec = await repo.validate_whodrug_record(version, coded_code)
                    if rec_rec:
                        if not coded_term:
                            coded_term = rec_rec.preferred_name
                        atc_ctx, ings = await repo.get_whodrug_context(rec_rec, version)
                        hierarchy = {"atc_context": atc_ctx, "ingredients": ings}
                    else:
                        coded_term = coded_term or "Coded Drug"

            assignment.status = CodingState.CODED
            assignment.recoding_status = RecodingState.NONE
            assignment.coded_code = coded_code
            assignment.coded_term = coded_term
            assignment.score = score
            assignment.hierarchy = hierarchy
            assignment.assigned_by = resolved_actor
            assignment.assigned_at = datetime.now(UTC).replace(tzinfo=None)

            await repo.save_assignment(assignment)

            # Record in clinical coding ledger
            await repo.add_ledger(
                {
                    "assignment_id": assignment.id,
                    "verbatim_text": assignment.verbatim_text,
                    "observation_id": assignment.observation_id,
                    "dictionary_type": assignment.dictionary_type,
                    "old_dictionary_version": old_version if old_code else None,
                    "old_coded_code": old_code,
                    "old_coded_term": old_term,
                    "new_dictionary_version": version,
                    "new_coded_code": coded_code,
                    "new_coded_term": coded_term,
                    "recoding_reason": item_reason,
                    "decision_by": resolved_actor,
                    "decision_at": datetime.now(UTC).replace(tzinfo=None),
                    "old_hierarchy": old_hierarchy,
                    "new_hierarchy": hierarchy,
                    "recoding_status": assignment.recoding_status,
                }
            )

            # Resolve active queries
            if assignment.observation_id:
                active_queries = await repo.get_active_queries(
                    assignment.observation_id
                )
                for active_q in active_queries:
                    import uuid

                    from apps.execution.database.models import IntegrationOutbox

                    payload = {
                        "actor": resolved_actor,
                        "timestamp": datetime.now(UTC).isoformat(),
                        "observation_id": assignment.observation_id,
                        "query_id": active_q.id,
                        "justification": item_reason,
                        "action": item_action,
                        "coded_code": coded_code,
                    }
                    outbox_entry = IntegrationOutbox(
                        id=str(uuid.uuid4()),
                        event_type="EDC_QUERY_RESOLVE",
                        payload=payload,
                        status="PENDING",
                        attempts=0,
                        correlation_id=f"query-resolve-{active_q.id}-{uuid.uuid4().hex[:8]}",
                        created_by=resolved_actor,
                        reason_for_change=item_reason,
                    )
                    await repo.add_outbox_entry(outbox_entry)

            success_count += 1
            results.append(
                {
                    "assignment_id": aid,
                    "status": "SUCCESS",
                    "coded_code": coded_code,
                    "coded_term": coded_term,
                }
            )
        except Exception as e:
            failed_count += 1
            results.append(
                {
                    "assignment_id": aid,
                    "status": "FAILED",
                    "error": str(e),
                }
            )

    return {
        "success_count": success_count,
        "failed_count": failed_count,
        "results": results,
    }


async def raise_coding_query(
    session: Any,
    assignment_id: str,
    query_text: str | None = None,
    reason: str | None = None,
    actor: str = "system",
) -> dict[str, Any]:
    """Escalates a coding discrepancy into a ClinicalQuery on the associated observation/eCRF record."""
    from apps.execution.database.models import ClinicalQuery

    repo = _get_repository(session)

    # Resolve actor
    resolved_actor = actor
    if not resolved_actor or resolved_actor == "system":
        try:
            from apps.execution.database.context import current_user_id

            resolved_actor = current_user_id.get() or "system"
        except (LookupError, ValueError):  # fmt: skip
            resolved_actor = "system"

    assignment = await repo.get_assignment(assignment_id)

    # Update assignment state
    assignment.status = CodingState.QUERY_PENDING
    assignment.recoding_status = RecodingState.NONE
    assignment.assigned_by = resolved_actor
    assignment.assigned_at = datetime.now(UTC).replace(tzinfo=None)
    await repo.save_assignment(assignment)

    study_id = "STUDY-001"
    site_id = None
    subject_id = "SUBJ-001"
    visit_id = None
    domain = assignment.domain or "AE"
    test_code = assignment.source_field or "AETERM"

    if assignment.observation_id:
        obs = await repo.get_observation(assignment.observation_id)
        if obs:
            study_id = obs.study_id or study_id
            site_id = getattr(obs, "site_id", None)
            subject_id = obs.subject_id or subject_id
            visit_id = obs.visit_id
            domain = obs.domain or domain
            test_code = obs.test_code or test_code

    explanation_text = (
        query_text
        or f"The verbatim term '{assignment.verbatim_text}' in field {test_code} requires clinical clarification."
    )

    query = ClinicalQuery(
        study_id=study_id,
        site_id=site_id,
        subject_id=subject_id,
        visit_id=visit_id,
        domain=domain,
        test_code=test_code,
        status="OPEN",
        explanation=explanation_text,
        message=explanation_text,
        observation_id=assignment.observation_id,
        origin="SYSTEM_CODING",
        query_type="SYSTEM_CODING",
        form_id=f"{domain.upper()}_FORM",
        field_id=test_code,
        action_required="CLARIFY_VERBATIM",
        priority="HIGH",
        created_by=resolved_actor,
    )
    await repo.save_query(query)
    if hasattr(session, "flush"):
        await session.flush()

    return {
        "query_id": str(query.id),
        "status": query.status,
        "assignment_id": str(assignment.id),
        "explanation": query.explanation,
    }


async def trigger_impact_analysis(
    session: Any,
    dictionary_type: str,
    new_version: str,
    actor: str = "system",
) -> dict[str, Any]:
    """Manually triggers up-versioning impact analysis on existing coded assignments."""
    from apps.execution.coding.impact import run_impact_analysis

    try:
        return await run_impact_analysis(
            session=session,
            dictionary_type=dictionary_type,
            new_version=new_version,
            actor=actor,
        )
    except ValueError as e:
        logger.error(f"ValueError in up-versioning impact analysis: {e}", exc_info=True)
        # Raise InvalidCodingActionError or standard exceptions rather than HTTPException
        raise InvalidCodingActionError(str(e))


class MedicalCodingService:
    """Service wrapper for medical coding operations."""

    @staticmethod
    async def raise_coding_query(
        session: Any,
        observation_id: str | None = None,
        assignment_id: str | None = None,
        query_text: str | None = None,
        user_id: str = "system",
        reason: str | None = None,
        actor: str | None = None,
    ) -> Any:
        from sqlalchemy import select

        from apps.execution.database.models import ClinicalQuery

        repo = _get_repository(session)
        resolved_actor = actor or user_id or "system"

        if not assignment_id and observation_id:
            assignments = await repo.list_assignments(observation_id=observation_id)
            if assignments:
                assignment_id = str(assignments[0].id)
            else:
                obs = await repo.get_observation(observation_id)
                study_id = obs.study_id if obs else "STUDY-001"
                site_id = getattr(obs, "site_id", None) if obs else None
                subject_id = obs.subject_id if obs else "SUBJ-001"
                visit_id = getattr(obs, "visit_id", None) if obs else None
                domain = getattr(obs, "domain", "AE") if obs else "AE"
                test_code = getattr(obs, "test_code", "AETERM") if obs else "AETERM"
                explanation_text = query_text or "Coding clarification required."
                query = ClinicalQuery(
                    study_id=study_id,
                    site_id=site_id,
                    subject_id=subject_id,
                    visit_id=visit_id,
                    domain=domain,
                    test_code=test_code,
                    status="OPEN",
                    explanation=explanation_text,
                    message=explanation_text,
                    observation_id=observation_id,
                    origin="SYSTEM_CODING",
                    query_type="SYSTEM_CODING",
                    form_id=f"{domain.upper()}_FORM",
                    field_id=test_code,
                    action_required="CLARIFY_VERBATIM",
                    priority="HIGH",
                    created_by=resolved_actor,
                )
                await repo.save_query(query)
                if hasattr(session, "flush"):
                    await session.flush()
                return query

        res = await raise_coding_query(
            session=session,
            assignment_id=assignment_id,
            query_text=query_text,
            reason=reason,
            actor=resolved_actor,
        )
        query_id = res["query_id"]
        stmt = select(ClinicalQuery).where(ClinicalQuery.id == query_id)
        q_res = await session.execute(stmt)
        return q_res.scalar_one_or_none() or query_id
