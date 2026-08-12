"""
SQLAlchemy database adapters for the Medical Coding Service (Hexagonal Decoupling).
"""

from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.coding.matcher import _get_meddra_hierarchy, _get_whodrug_context
from apps.execution.database.models import (
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalQuery,
    CodingState,
    MedDRATerm,
    WHODrugRecord,
)
from apps.execution.database.models import DictionaryType as DBDictionaryType
from apps.execution.exceptions import CodingAssignmentNotFoundError


class SQLCodingRepository:
    """SQLAlchemy implementation of the CodingRepositoryPort.

    Provides concrete persistence actions for medical coding assignments, clinical coding ledgers,
    and associated queries over active database sessions, decoupling core logic from SQLAlchemy.
    """

    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_assignment(self, assignment_id: str) -> ClinicalCodingAssignment:
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
        self.session.add(assignment)

    async def add_ledger(self, ledger_data: dict) -> None:
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
        stmt_active_q = select(ClinicalQuery).where(
            ClinicalQuery.observation_id == observation_id,
            ClinicalQuery.origin == "SYSTEM_CODING",
            ClinicalQuery.status.in_(["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]),
            ClinicalQuery.is_deleted.is_(False),
        )
        res_active_q = await self.session.execute(stmt_active_q)
        return list(res_active_q.scalars().all())

    async def save_query(self, query: ClinicalQuery) -> None:
        self.session.add(query)

    async def add_outbox_entry(self, entry: Any) -> None:
        self.session.add(entry)

    async def validate_meddra_term(self, version: str, code: str) -> Any:
        stmt_valid = select(MedDRATerm).where(
            MedDRATerm.dictionary_version == version,
            MedDRATerm.code == code,
        )
        res_valid = await self.session.execute(stmt_valid)
        return res_valid.scalars().first()

    async def validate_whodrug_record(self, version: str, code: str) -> Any:
        stmt_valid = select(WHODrugRecord).where(
            WHODrugRecord.dictionary_version == version,
            WHODrugRecord.drug_code == code,
        )
        res_valid = await self.session.execute(stmt_valid)
        return res_valid.scalars().first()

    async def get_meddra_hierarchy(self, term_record: Any, version: str) -> list[Any]:
        return await _get_meddra_hierarchy(self.session, term_record, version)

    async def get_whodrug_context(
        self, rec_record: Any, version: str
    ) -> tuple[list[Any], list[Any]]:
        return await _get_whodrug_context(self.session, rec_record, version)
