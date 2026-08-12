"""
Abstract Ports for the Medical Coding Service (Hexagonal Decoupling).
"""

from typing import Any

from packages.hexagonal import RepositoryPort


class CodingRepositoryPort(RepositoryPort[Any]):
    """Port defining persistence and dictionary validation operations for Medical Coding."""

    async def get_assignment(self, assignment_id: str) -> Any:
        """Retrieve a single active coding assignment by ID."""
        ...

    async def list_assignments(
        self,
        observation_id: str | None = None,
        status: str | None = None,
        verbatim_text: str | None = None,
        dictionary_type: str | None = None,
    ) -> list[Any]:
        """List active coding assignments with filters."""
        ...

    async def save_assignment(self, assignment: Any) -> None:
        """Persist/update a coding assignment."""
        ...

    async def add_ledger(self, ledger_data: dict) -> None:
        """Create and add a coding ledger entry."""
        ...

    async def get_active_queries(self, observation_id: str) -> list[Any]:
        """Retrieve active SYSTEM_CODING queries for an observation."""
        ...

    async def save_query(self, query: Any) -> None:
        """Persist/update a clinical query."""
        ...

    async def add_outbox_entry(self, entry: Any) -> None:
        """Add an outbox entry to the repository."""
        ...

    async def validate_meddra_term(self, version: str, code: str) -> Any:
        """Validate code and version in MedDRA dictionary."""
        ...

    async def validate_whodrug_record(self, version: str, code: str) -> Any:
        """Validate code and version in WHODrug dictionary."""
        ...

    async def get_meddra_hierarchy(self, term_record: Any, version: str) -> list[Any]:
        """Retrieve MedDRA hierarchy path for a term record."""
        ...

    async def get_whodrug_context(
        self, rec_record: Any, version: str
    ) -> tuple[list[Any], list[Any]]:
        """Retrieve ATC context and ingredients for a WHODrug record."""
        ...
