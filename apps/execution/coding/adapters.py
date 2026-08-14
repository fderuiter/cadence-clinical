"""
SQLAlchemy database adapters for the Medical Coding Service (Hexagonal Decoupling).
"""

from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.coding.ports import CodingRepositoryPort
from apps.execution.database.models import (
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalQuery,
    CodingState,
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugATC,
    WHODrugDrugATC,
    WHODrugDrugIngredient,
    WHODrugIngredient,
    WHODrugRecord,
)
from apps.execution.database.models import DictionaryType as DBDictionaryType
from apps.execution.exceptions import CodingAssignmentNotFoundError


async def _get_meddra_hierarchy(
    session: AsyncSession, term: Any, version: str
) -> list[dict[str, Any]]:
    """Retrieves full hierarchy paths for a MedDRA term."""
    term_code = term.get("code") if isinstance(term, dict) else term.code
    term_level = term.get("level") if isinstance(term, dict) else term.level
    term_name = term.get("term_name") if isinstance(term, dict) else term.term_name

    stmt = select(MedDRAHierarchy).where(MedDRAHierarchy.dictionary_version == version)
    if term_level == "LLT":
        stmt = stmt.where(MedDRAHierarchy.llt_code == term_code)
    elif term_level == "PT":
        stmt = stmt.where(MedDRAHierarchy.pt_code == term_code)
    else:
        stmt = stmt.where(
            (MedDRAHierarchy.hlt_code == term_code)
            | (MedDRAHierarchy.hlgt_code == term_code)
            | (MedDRAHierarchy.soc_code == term_code)
        )

    res = await session.execute(stmt)
    hierarchies = res.scalars().all()

    if not hierarchies:
        return []

    unique_codes = set()
    for h in hierarchies:
        unique_codes.add(h.pt_code)
        unique_codes.add(h.hlt_code)
        unique_codes.add(h.hlgt_code)
        unique_codes.add(h.soc_code)
        if h.llt_code and h.llt_code != "NONE":
            unique_codes.add(h.llt_code)

    term_map = {}
    if unique_codes:
        term_stmt = select(MedDRATerm).where(
            MedDRATerm.dictionary_version == version,
            MedDRATerm.code.in_(list(unique_codes)),
        )
        term_res = await session.execute(term_stmt)
        for t in term_res.scalars().all():
            term_map[(t.code, t.level)] = t.term_name

    results = []
    for h in hierarchies:
        results.append(
            {
                "llt_code": h.llt_code,
                "llt_name": term_map.get(
                    (h.llt_code, "LLT"),
                    term_name if h.llt_code == term_code else "",
                ),
                "pt_code": h.pt_code,
                "pt_name": term_map.get((h.pt_code, "PT"), ""),
                "hlt_code": h.hlt_code,
                "hlt_name": term_map.get((h.hlt_code, "HLT"), ""),
                "hlgt_code": h.hlgt_code,
                "hlgt_name": term_map.get((h.hlgt_code, "HLGT"), ""),
                "soc_code": h.soc_code,
                "soc_name": term_map.get((h.soc_code, "SOC"), ""),
                "primary_soc_flag": h.primary_soc_flag,
            }
        )
    return results


async def _get_whodrug_context(
    session: AsyncSession, record: Any, version: str
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    """Retrieves ATC context and ingredients for a WHODrug record."""
    drug_code = (
        record.get("drug_code") if isinstance(record, dict) else record.drug_code
    )

    atc_links_stmt = select(WHODrugDrugATC).where(
        WHODrugDrugATC.dictionary_version == version,
        WHODrugDrugATC.drug_code == drug_code,
    )
    atc_links_res = await session.execute(atc_links_stmt)
    atc_codes = [link.atc_code for link in atc_links_res.scalars().all()]

    atc_details = []
    if atc_codes:
        atc_stmt = select(WHODrugATC).where(
            WHODrugATC.dictionary_version == version,
            WHODrugATC.atc_code.in_(atc_codes),
        )
        atc_res = await session.execute(atc_stmt)
        atc_details = [
            {"atc_code": a.atc_code, "description": a.description}
            for a in atc_res.scalars().all()
        ]

    ing_links_stmt = select(WHODrugDrugIngredient).where(
        WHODrugDrugIngredient.dictionary_version == version,
        WHODrugDrugIngredient.drug_code == drug_code,
    )
    ing_links_res = await session.execute(ing_links_stmt)
    ing_codes = [link.ingredient_code for link in ing_links_res.scalars().all()]

    ing_details = []
    if ing_codes:
        ing_stmt = select(WHODrugIngredient).where(
            WHODrugIngredient.dictionary_version == version,
            WHODrugIngredient.ingredient_code.in_(ing_codes),
        )
        ing_res = await session.execute(ing_stmt)
        ing_details = [
            {"ingredient_code": i.ingredient_code, "ingredient_name": i.ingredient_name}
            for i in ing_res.scalars().all()
        ]

    return atc_details, ing_details


class SQLCodingRepository(CodingRepositoryPort):
    """SQLAlchemy implementation of the CodingRepositoryPort.

    Provides concrete persistence actions for medical coding assignments, clinical coding ledgers,
    and associated queries over active database sessions, decoupling core logic from SQLAlchemy.
    """

    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_by_id(self, entity_id: str) -> Any | None:
        """Retrieve a coding assignment by its ID."""
        try:
            return await self.get_assignment(entity_id)
        except Exception:
            return None

    async def save(self, entity: Any) -> Any:
        """Save a coding assignment."""
        await self.save_assignment(entity)
        return entity

    async def get_assignment(self, assignment_id: str) -> ClinicalCodingAssignment:
        """Retrieve a single active coding assignment by ID."""
        stmt = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.id == assignment_id,
            ClinicalCodingAssignment.is_deleted.is_(False),
        )
        res = await self.session.execute(stmt)
        assignment = res.scalars().first()
        if not assignment:
            raise CodingAssignmentNotFoundError(
                f"Coding assignment '{assignment_id}' not found or has been deleted."
            )
        return assignment

    async def list_assignments(
        self,
        observation_id: str | None = None,
        status: str | None = None,
        verbatim_text: str | None = None,
        dictionary_type: str | None = None,
    ) -> list[ClinicalCodingAssignment]:
        """List active coding assignments with filters."""
        stmt = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.is_deleted.is_(False)
        )
        if observation_id:
            stmt = stmt.where(ClinicalCodingAssignment.observation_id == observation_id)
        if status:
            try:
                status_enum = CodingState(status.upper())
                stmt = stmt.where(ClinicalCodingAssignment.status == status_enum)
            except ValueError:
                try:
                    status_enum = CodingState[status.upper()]
                    stmt = stmt.where(ClinicalCodingAssignment.status == status_enum)
                except (KeyError, ValueError):  # fmt: skip
                    stmt = stmt.where(ClinicalCodingAssignment.status == status)
        if verbatim_text:
            stmt = stmt.where(ClinicalCodingAssignment.verbatim_text == verbatim_text)
        if dictionary_type:
            try:
                dict_type_enum = DBDictionaryType(dictionary_type.upper())
                stmt = stmt.where(
                    ClinicalCodingAssignment.dictionary_type == dict_type_enum
                )
            except ValueError:
                try:
                    dict_type_enum = DBDictionaryType[dictionary_type.upper()]
                    stmt = stmt.where(
                        ClinicalCodingAssignment.dictionary_type == dict_type_enum
                    )
                except (KeyError, ValueError):  # fmt: skip
                    stmt = stmt.where(
                        ClinicalCodingAssignment.dictionary_type == dictionary_type
                    )

        res = await self.session.execute(stmt)
        return list(res.scalars().all())

    async def save_assignment(self, assignment: ClinicalCodingAssignment) -> None:
        """Persist/update a coding assignment."""
        self.session.add(assignment)

    async def add_ledger(self, ledger_data: dict) -> None:
        """Create and add a coding ledger entry."""
        ledger = ClinicalCodingLedger(
            assignment_id=ledger_data["assignment_id"],
            verbatim_text=ledger_data["verbatim_text"],
            observation_id=ledger_data["observation_id"],
            dictionary_type=ledger_data["dictionary_type"],
            old_dictionary_version=ledger_data["old_dictionary_version"],
            old_coded_code=ledger_data["old_coded_code"],
            old_coded_term=ledger_data["old_coded_term"],
            new_dictionary_version=ledger_data["new_dictionary_version"],
            new_coded_code=ledger_data["new_coded_code"],
            new_coded_term=ledger_data["new_coded_term"],
            recoding_reason=ledger_data["recoding_reason"],
            decision_by=ledger_data["decision_by"],
            decision_at=ledger_data["decision_at"],
            old_hierarchy=ledger_data["old_hierarchy"],
            new_hierarchy=ledger_data["new_hierarchy"],
            recoding_status=ledger_data["recoding_status"],
        )
        self.session.add(ledger)

    async def get_active_queries(self, observation_id: str) -> list[ClinicalQuery]:
        """Retrieve active SYSTEM_CODING queries for an observation."""
        stmt_active_q = select(ClinicalQuery).where(
            ClinicalQuery.observation_id == observation_id,
            ClinicalQuery.origin == "SYSTEM_CODING",
            ClinicalQuery.status.in_(["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]),
            ClinicalQuery.is_deleted.is_(False),
        )
        res_active_q = await self.session.execute(stmt_active_q)
        return list(res_active_q.scalars().all())

    async def save_query(self, query: ClinicalQuery) -> None:
        """Persist/update a clinical query."""
        self.session.add(query)

    async def add_outbox_entry(self, entry: Any) -> None:
        """Add an outbox entry to the repository."""
        self.session.add(entry)

    async def add_query_resolve_outbox_entry(
        self,
        query_id: str,
        observation_id: str,
        resolved_actor: str,
        reason_for_change: str | None,
        action_upper: str,
        coded_code: str | None,
    ) -> None:
        """Create and persist an outbox entry to resolve clinical coding queries."""
        import uuid
        from datetime import UTC, datetime

        from apps.execution.database.models import IntegrationOutbox

        payload = {
            "actor": resolved_actor,
            "timestamp": datetime.now(UTC).isoformat(),
            "observation_id": observation_id,
            "query_id": query_id,
            "justification": reason_for_change or f"Manual decision: {action_upper}",
            "action": action_upper,
            "coded_code": coded_code,
        }

        outbox_entry = IntegrationOutbox(
            id=str(uuid.uuid4()),
            event_type="EDC_QUERY_RESOLVE",
            payload=payload,
            status="PENDING",
            attempts=0,
            correlation_id=f"query-resolve-{query_id}-{uuid.uuid4().hex[:8]}",
            created_by=resolved_actor,
            reason_for_change=reason_for_change or f"Manual decision: {action_upper}",
        )
        await self.add_outbox_entry(outbox_entry)

    async def validate_meddra_term(self, version: str, code: str) -> Any:
        """Validate code and version in MedDRA dictionary."""
        stmt_valid = select(MedDRATerm).where(
            MedDRATerm.dictionary_version == version,
            MedDRATerm.code == code,
        )
        res_valid = await self.session.execute(stmt_valid)
        return res_valid.scalars().first()

    async def validate_whodrug_record(self, version: str, code: str) -> Any:
        """Validate code and version in WHODrug dictionary."""
        stmt_valid = select(WHODrugRecord).where(
            WHODrugRecord.dictionary_version == version,
            WHODrugRecord.drug_code == code,
        )
        res_valid = await self.session.execute(stmt_valid)
        return res_valid.scalars().first()

    async def get_meddra_hierarchy(self, term_record: Any, version: str) -> list[Any]:
        """Retrieve MedDRA hierarchy path for a term record."""
        return await _get_meddra_hierarchy(self.session, term_record, version)

    async def get_whodrug_context(
        self, rec_record: Any, version: str
    ) -> tuple[list[Any], list[Any]]:
        """Retrieve ATC context and ingredients for a WHODrug record."""
        return await _get_whodrug_context(self.session, rec_record, version)

    async def list_meddra_terms(
        self, version: str, target_level: str | None = None
    ) -> list[dict[str, Any]]:
        """Retrieve list of MedDRA terms for a given version and level."""
        stmt = select(MedDRATerm).where(MedDRATerm.dictionary_version == version)
        if target_level:
            stmt = stmt.where(MedDRATerm.level == target_level.upper())
        res = await self.session.execute(stmt)
        return [
            {
                "code": t.code,
                "term_name": t.term_name,
                "level": t.level,
            }
            for t in res.scalars().all()
        ]

    async def list_whodrug_records(self, version: str) -> list[dict[str, Any]]:
        """Retrieve list of WHODrug records for a given version."""
        stmt = select(WHODrugRecord).where(WHODrugRecord.dictionary_version == version)
        res = await self.session.execute(stmt)
        return [
            {
                "drug_code": r.drug_code,
                "preferred_name": r.preferred_name,
                "drug_name": r.drug_name,
            }
            for r in res.scalars().all()
        ]
