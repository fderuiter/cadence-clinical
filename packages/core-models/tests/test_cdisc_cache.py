"""Unit & integration test suite for CDISC terminology SQLite cache.

Requirements: PRD-SYS-001
"""

import asyncio
from pathlib import Path

import pytest
from cdisc.cdisc_library_client import CodelistDefinition, CodelistTerm
from cdisc.terminology_cache import CdiscTerminologyCache

import packages  # noqa: F401


@pytest.mark.asyncio
async def test_cdisc_cache_save_and_get() -> None:
    """Validate saving and retrieving codelists from cache.

    Requirements: PRD-SYS-001
    """
    cache = CdiscTerminologyCache(db_path=Path(":memory:"))

    terms = [
        CodelistTerm(
            concept_id="C49487",
            submission_value="Y",
            preferred_term="Yes",
            definition="Yes response",
        ),
        CodelistTerm(
            concept_id="C49488",
            submission_value="N",
            preferred_term="No",
            definition="No response",
        ),
    ]
    codelist = CodelistDefinition(
        codelist_code="C66742",
        name="No Yes Response",
        extensible=False,
        terms=terms,
    )

    await cache.save_codelist("cdashct-2024-09-27", codelist)

    retrieved = await cache.get_codelist("cdashct-2024-09-27", "C66742")
    assert retrieved is not None
    assert retrieved.codelist_code == "C66742"
    assert retrieved.name == "No Yes Response"
    assert len(retrieved.terms) == 2
    assert retrieved.terms[0].submission_value == "Y"


@pytest.mark.asyncio
async def test_cdisc_cache_ttl_expiration() -> None:
    """Validate cache entries expire after configured TTL.

    Requirements: PRD-SYS-001
    """
    cache = CdiscTerminologyCache(db_path=Path(":memory:"))

    codelist = CodelistDefinition(
        codelist_code="C12345",
        name="Test Short TTL",
        extensible=True,
        terms=[],
    )

    # Save with 1 second TTL
    await cache.save_codelist("cdashct-2024-09-27", codelist, ttl_seconds=1)

    # Immediately should not be expired
    assert not await cache.is_expired("C12345")
    retrieved = await cache.get_codelist("cdashct-2024-09-27", "C12345")
    assert retrieved is not None

    # Wait for TTL to pass
    await asyncio.sleep(1.1)

    assert await cache.is_expired("C12345")
    expired_retrieved = await cache.get_codelist("cdashct-2024-09-27", "C12345")
    assert expired_retrieved is None


@pytest.mark.asyncio
async def test_cdisc_cache_purge_expired() -> None:
    """Validate purging expired items removes them from the database.

    Requirements: PRD-SYS-001
    """
    cache = CdiscTerminologyCache(db_path=Path(":memory:"))

    cl1 = CodelistDefinition(codelist_code="C100", name="Item 1", terms=[])
    cl2 = CodelistDefinition(codelist_code="C200", name="Item 2", terms=[])

    await cache.save_codelist("package1", cl1, ttl_seconds=1)
    await cache.save_codelist("package1", cl2, ttl_seconds=3600)

    await asyncio.sleep(1.1)

    purged_count = await cache.purge_expired()
    assert purged_count == 1

    stats = await cache.get_cache_stats()
    assert stats["total_cached_codelists"] == 1
