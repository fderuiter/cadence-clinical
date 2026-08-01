import logging
from datetime import UTC, datetime

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.coding.impact import run_impact_analysis

# Existing coding engine/matching modules
from apps.execution.coding.matcher import match_verbatim_term

# Database models & enums
from apps.execution.database.models import (
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalQuery,
    CodingState,
    MedDRATerm,
    RecodingState,
    WHODrugRecord,
)
from apps.execution.database.models import (
    DictionaryType as DBDictionaryType,
)
from apps.execution.routers.coding_schemas import (
    CodingAssignmentResponse,
    MedDRACodeLookupResponse,
    MedDRACodeMatch,
    WHODrugATCContext,
    WHODrugCodeLookupResponse,
    WHODrugCodeMatch,
    WHODrugIngredientItem,
)

logger = logging.getLogger(__name__)


def map_assignment_to_response(a: ClinicalCodingAssignment) -> CodingAssignmentResponse:
    """Maps a ClinicalCodingAssignment database model to its response schema."""
    return CodingAssignmentResponse(
        id=a.id,
        verbatim_text=a.verbatim_text,
        source_field=a.source_field,
        observation_id=a.observation_id,
        dictionary_type=a.dictionary_type.value
        if hasattr(a.dictionary_type, "value")
        else str(a.dictionary_type),
        dictionary_version=a.dictionary_version,
        coded_code=a.coded_code,
        coded_term=a.coded_term,
        status=a.status.value if hasattr(a.status, "value") else str(a.status),
        recoding_status=a.recoding_status.value
        if hasattr(a.recoding_status, "value")
        else str(a.recoding_status),
        assigned_by=a.assigned_by,
        assigned_at=a.assigned_at,
        score=a.score,
        hierarchy=a.hierarchy,
        suggestions=a.suggestions,
        domain=a.domain,
        version=a.version,
        is_deleted=a.is_deleted,
    )


async def search_dictionary(
    session: AsyncSession,
    term: str,
    dictionary_type: str,
    version: str,
    target_level: str | None = None,
) -> MedDRACodeLookupResponse | WHODrugCodeLookupResponse:
    """Delegates interactive terminology search or auto-complete lookup to match_verbatim_term.

    Reshapes the returned dict into the MedDRACodeLookupResponse/WHODrugCodeLookupResponse structures.
    """
    if not term or not term.strip():
        raise HTTPException(
            status_code=400,
            detail="Term must be a non-empty string",
        )
    if not version or not version.strip():
        raise HTTPException(
            status_code=400,
            detail="Version must be a non-empty string",
        )

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
                        MedDRACodeMatch(
                            llt_code=h.get("llt_code") or "",
                            llt_name=h.get("llt_name") or "",
                            pt_code=h.get("pt_code") or "",
                            pt_name=h.get("pt_name") or "",
                            hlt_code=h.get("hlt_code") or "",
                            hlt_name=h.get("hlt_name") or "",
                            hlgt_code=h.get("hlgt_code") or "",
                            hlgt_name=h.get("hlgt_name") or "",
                            soc_code=h.get("soc_code") or "",
                            soc_name=h.get("soc_name") or "",
                            primary_soc_flag=h.get("primary_soc_flag"),
                            score=score,
                        )
                    )
            else:
                is_llt = parent_match.get("level") == "LLT"
                matches.append(
                    MedDRACodeMatch(
                        llt_code=parent_match.get("code") if is_llt else "",
                        llt_name=parent_match.get("term_name") if is_llt else "",
                        pt_code=parent_match.get("code") if not is_llt else "",
                        pt_name=parent_match.get("term_name") if not is_llt else "",
                        hlt_code="",
                        hlt_name="",
                        hlgt_code="",
                        hlgt_name="",
                        soc_code="",
                        soc_name="",
                        primary_soc_flag=None,
                        score=score,
                    )
                )
        elif res.get("suggestions"):
            for sug in res["suggestions"]:
                score = sug.get("score", 0.0)
                if sug.get("hierarchies"):
                    for h in sug.get("hierarchies", []):
                        matches.append(
                            MedDRACodeMatch(
                                llt_code=h.get("llt_code") or "",
                                llt_name=h.get("llt_name") or "",
                                pt_code=h.get("pt_code") or "",
                                pt_name=h.get("pt_name") or "",
                                hlt_code=h.get("hlt_code") or "",
                                hlt_name=h.get("hlt_name") or "",
                                hlgt_code=h.get("hlgt_code") or "",
                                hlgt_name=h.get("hlgt_name") or "",
                                soc_code=h.get("soc_code") or "",
                                soc_name=h.get("soc_name") or "",
                                primary_soc_flag=h.get("primary_soc_flag"),
                                score=score,
                            )
                        )
                else:
                    is_llt = sug.get("level") == "LLT"
                    matches.append(
                        MedDRACodeMatch(
                            llt_code=sug.get("code") if is_llt else "",
                            llt_name=sug.get("term_name") if is_llt else "",
                            pt_code=sug.get("code") if not is_llt else "",
                            pt_name=sug.get("term_name") if not is_llt else "",
                            hlt_code="",
                            hlt_name="",
                            hlgt_code="",
                            hlgt_name="",
                            soc_code="",
                            soc_name="",
                            primary_soc_flag=None,
                            score=score,
                        )
                    )
        return MedDRACodeLookupResponse(
            status=res.get("status", "UNCODABLE"),
            matches=matches,
        )

    if dict_type_upper == "WHODRUG":
        if res.get("match"):
            m = res["match"]
            matches.append(
                WHODrugCodeMatch(
                    drug_code=m.get("drug_code") or "",
                    preferred_name=m.get("preferred_name") or "",
                    drug_name=m.get("drug_name"),
                    score=m.get("score", 0.0),
                    atc_context=[
                        WHODrugATCContext(
                            atc_code=a.get("atc_code") or "",
                            description=a.get("description") or "",
                        )
                        for a in m.get("atc_context", [])
                    ],
                    ingredients=[
                        WHODrugIngredientItem(
                            ingredient_code=i.get("ingredient_code") or "",
                            ingredient_name=i.get("ingredient_name") or "",
                        )
                        for i in m.get("ingredients", [])
                    ],
                )
            )
        elif res.get("suggestions"):
            for sug in res["suggestions"]:
                matches.append(
                    WHODrugCodeMatch(
                        drug_code=sug.get("drug_code") or "",
                        preferred_name=sug.get("preferred_name") or "",
                        drug_name=sug.get("drug_name"),
                        score=sug.get("score", 0.0),
                        atc_context=[
                            WHODrugATCContext(
                                atc_code=a.get("atc_code") or "",
                                description=a.get("description") or "",
                            )
                            for a in sug.get("atc_context", [])
                        ],
                        ingredients=[
                            WHODrugIngredientItem(
                                ingredient_code=i.get("ingredient_code") or "",
                                ingredient_name=i.get("ingredient_name") or "",
                            )
                            for i in sug.get("ingredients", [])
                        ],
                    )
                )
        return WHODrugCodeLookupResponse(
            status=res.get("status", "UNCODABLE"),
            matches=matches,
        )

    raise ValueError(f"Unsupported dictionary type: {dictionary_type}")


async def list_coding_assignments(
    session: AsyncSession,
    observation_id: str | None = None,
    status: str | None = None,
    verbatim_text: str | None = None,
    dictionary_type: str | None = None,
) -> list[ClinicalCodingAssignment]:
    """Retrieves and filters active, non-deleted medical coding assignments."""
    stmt = select(ClinicalCodingAssignment).where(
        ClinicalCodingAssignment.is_deleted.is_(False)
    )
    if observation_id:
        stmt = stmt.where(ClinicalCodingAssignment.observation_id == observation_id)
    if status:
        stmt = stmt.where(ClinicalCodingAssignment.status == status.upper())
    if verbatim_text:
        stmt = stmt.where(ClinicalCodingAssignment.verbatim_text == verbatim_text)
    if dictionary_type:
        stmt = stmt.where(
            ClinicalCodingAssignment.dictionary_type == dictionary_type.upper()
        )

    res = await session.execute(stmt)
    assignments = res.scalars().all()
    return list(assignments)


async def get_coding_assignment(
    session: AsyncSession,
    assignment_id: str,
) -> ClinicalCodingAssignment:
    """Retrieves a single active, non-deleted coding assignment by ID."""
    stmt = select(ClinicalCodingAssignment).where(
        ClinicalCodingAssignment.id == assignment_id,
        ClinicalCodingAssignment.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    assignment = res.scalars().first()
    if not assignment:
        raise HTTPException(
            status_code=404,
            detail=f"Coding assignment '{assignment_id}' not found or has been deleted.",
        )
    return assignment


async def process_coding_action(
    session: AsyncSession,
    assignment_id: str,
    action: str,
    code: str | None = None,
    term: str | None = None,
    suggestion_index: int | None = None,
    reason_for_change: str | None = None,
    actor: str = "system",
) -> ClinicalCodingAssignment:
    """Processes a data manager coding action (ACCEPT, OVERRIDE, or QUERY).

    Accepts a suggestion or submits a manual override, persisting results and updating the ledger.
    """
    action_upper = action.upper()
    if action_upper not in ("ACCEPT", "OVERRIDE", "QUERY"):
        raise HTTPException(
            status_code=400,
            detail=f"Invalid action '{action}'. Allowed actions: ACCEPT, OVERRIDE, QUERY."
        )

    # 1. Fetch existing assignment
    stmt = select(ClinicalCodingAssignment).where(
        ClinicalCodingAssignment.id == assignment_id,
        ClinicalCodingAssignment.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    assignment = res.scalars().first()
    if not assignment:
        raise HTTPException(
            status_code=404,
            detail=f"Coding assignment '{assignment_id}' not found or has been deleted."
        )

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

    # Resolve actor with fallback to current_user_id.get() or parameter actor or "system"
    try:
        from apps.execution.database.context import current_user_id

        resolved_actor = current_user_id.get() or actor
    except Exception:
        resolved_actor = actor
    if not resolved_actor:
        resolved_actor = "system"

    if action_upper == "ACCEPT":
        # Must find a suggestion to accept
        if suggestion_index is not None:
            sug_list = assignment.suggestions
            if (
                not sug_list
                or not isinstance(sug_list, list)
                or suggestion_index < 0
                or suggestion_index >= len(sug_list)
            ):
                raise HTTPException(status_code=400, detail="Invalid suggestion_index")
            sug = sug_list[suggestion_index]
            coded_code = sug.get("code") or sug.get("drug_code")
            coded_term = sug.get("term_name") or sug.get("preferred_name")
            score = sug.get("score")
            if dict_type == DBDictionaryType.MEDDRA:
                hierarchy = sug.get("hierarchies")
            else:
                hierarchy = {
                    "atc_context": sug.get("atc_context", []),
                    "ingredients": sug.get("ingredients", []),
                }
        elif code and term:
            # Direct accept of specified code/term if it matches one of the suggestions
            sug_list = assignment.suggestions or []
            found = False
            for sug in sug_list:
                s_code = sug.get("code") or sug.get("drug_code")
                if s_code == code:
                    coded_code = s_code
                    coded_term = sug.get("term_name") or sug.get("preferred_name")
                    score = sug.get("score")
                    if dict_type == DBDictionaryType.MEDDRA:
                        hierarchy = sug.get("hierarchies")
                    else:
                        hierarchy = {
                            "atc_context": sug.get("atc_context", []),
                            "ingredients": sug.get("ingredients", []),
                        }
                    found = True
                    break
            if not found:
                raise HTTPException(
                    status_code=400,
                    detail="The provided code does not match any available suggestions. Use OVERRIDE for manual coding."
                )
        else:
            # Accept highest suggestion if available
            sug_list = assignment.suggestions
            if sug_list and isinstance(sug_list, list) and len(sug_list) > 0:
                sug = sug_list[0]
                coded_code = sug.get("code") or sug.get("drug_code")
                coded_term = sug.get("term_name") or sug.get("preferred_name")
                score = sug.get("score")
                if dict_type == DBDictionaryType.MEDDRA:
                    hierarchy = sug.get("hierarchies")
                else:
                    hierarchy = {
                        "atc_context": sug.get("atc_context", []),
                        "ingredients": sug.get("ingredients", []),
                    }
            else:
                raise HTTPException(
                    status_code=400,
                    detail="No suggestions available to ACCEPT. Use OVERRIDE instead."
                )

        # Double check existence of the code/version in DB
        if dict_type == DBDictionaryType.MEDDRA:
            stmt_valid = select(MedDRATerm).where(
                MedDRATerm.dictionary_version == version,
                MedDRATerm.code == coded_code,
            )
            res_valid = await session.execute(stmt_valid)
            if not res_valid.scalars().first():
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid code '{coded_code}' for MedDRA version '{version}'."
                )
        elif dict_type == DBDictionaryType.WHODRUG:
            stmt_valid = select(WHODrugRecord).where(
                WHODrugRecord.dictionary_version == version,
                WHODrugRecord.drug_code == coded_code,
            )
            res_valid = await session.execute(stmt_valid)
            if not res_valid.scalars().first():
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid drug code '{coded_code}' for WHODrug version '{version}'."
                )

        status = CodingState.CODED
        assignment.recoding_status = RecodingState.NONE

    elif action_upper == "OVERRIDE":
        # Override requires reason_for_change, code, and term
        if not reason_for_change or not reason_for_change.strip():
            raise HTTPException(
                status_code=400,
                detail="reason_for_change is required for OVERRIDE action and cannot be empty."
            )
        if not code or not code.strip():
            raise HTTPException(status_code=400, detail="code is required for OVERRIDE action.")
        if not term or not term.strip():
            raise HTTPException(status_code=400, detail="term is required for OVERRIDE action.")

        coded_code = code.strip()
        coded_term = term.strip()

        # Validate target code/version and retrieve hierarchy
        if dict_type == DBDictionaryType.MEDDRA:
            stmt_valid = select(MedDRATerm).where(
                MedDRATerm.dictionary_version == version,
                MedDRATerm.code == coded_code,
            )
            res_valid = await session.execute(stmt_valid)
            term_record = res_valid.scalars().first()
            if not term_record:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid code '{coded_code}' for MedDRA version '{version}'.",
                )

            # Fetch hierarchy for the overridden term if possible
            from apps.execution.coding.matcher import _get_meddra_hierarchy

            hierarchy_list = await _get_meddra_hierarchy(session, term_record, version)
            hierarchy = {"hierarchies": hierarchy_list} if hierarchy_list else {}

        elif dict_type == DBDictionaryType.WHODRUG:
            stmt_valid = select(WHODrugRecord).where(
                WHODrugRecord.dictionary_version == version,
                WHODrugRecord.drug_code == coded_code,
            )
            res_valid = await session.execute(stmt_valid)
            rec_record = res_valid.scalars().first()
            if not rec_record:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid drug code '{coded_code}' for WHODrug version '{version}'.",
                )

            # Fetch ATC context and ingredients for WHODrug override
            from apps.execution.coding.matcher import _get_whodrug_context

            atc_context, ingredients = await _get_whodrug_context(
                session, rec_record, version
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

    # 2. Update assignment state
    assignment.status = status
    assignment.coded_code = coded_code
    assignment.coded_term = coded_term
    assignment.score = score
    assignment.hierarchy = hierarchy
    assignment.assigned_by = resolved_actor
    assignment.assigned_at = datetime.now(UTC).replace(tzinfo=None)

    # 3. Create a ledger record for ACCEPT or OVERRIDE
    if action_upper in ("ACCEPT", "OVERRIDE"):
        ledger = ClinicalCodingLedger(
            assignment_id=assignment.id,
            verbatim_text=assignment.verbatim_text,
            observation_id=assignment.observation_id,
            dictionary_type=dict_type,
            old_dictionary_version=old_version,
            old_coded_code=old_code,
            old_coded_term=old_term,
            new_dictionary_version=version,
            new_coded_code=coded_code,
            new_coded_term=coded_term,
            recoding_reason=reason_for_change or f"Manual decision: {action_upper}",
            decision_by=resolved_actor,
            decision_at=datetime.now(UTC).replace(tzinfo=None),
            old_hierarchy=old_hierarchy,
            new_hierarchy=hierarchy or {},
            recoding_status=RecodingState.NONE,
        )
        session.add(ledger)

        # Close any open/active SYSTEM_CODING queries for this observation
        stmt_active_q = select(ClinicalQuery).where(
            ClinicalQuery.observation_id == assignment.observation_id,
            ClinicalQuery.origin == "SYSTEM_CODING",
            ClinicalQuery.status.in_(["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]),
            ClinicalQuery.is_deleted.is_(False),
        )
        res_active_q = await session.execute(stmt_active_q)
        active_queries = res_active_q.scalars().all()
        for active_q in active_queries:
            active_q.status = "CLOSED"
            active_q.resolver = resolved_actor
            active_q.resolved_at = datetime.now(UTC).replace(tzinfo=None)
            active_q.response = f"Resolved via manual coding action: {action_upper} on code {coded_code}."
            session.add(active_q)

    await session.flush()
    return assignment


async def trigger_impact_analysis(
    session: AsyncSession,
    dictionary_type: str,
    new_version: str,
    actor: str = "system",
) -> dict[str, int]:
    """Manually triggers up-versioning impact analysis on existing coded assignments."""
    return await run_impact_analysis(
        session=session,
        dictionary_type=dictionary_type,
        new_version=new_version,
        actor=actor,
    )
