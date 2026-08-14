import os
import time
from typing import Any
from unittest.mock import MagicMock, patch

import pytest
import pytest_asyncio
from sqlalchemy import text

from apps.execution.coding import (
    CodingCache,
    coding_cache,
    match_verbatim_term,
    normalize_term,
)
from apps.execution.coding.matcher import (
    calculate_combined_score,
    stem_word,
    token_cosine_similarity,
)
from apps.execution.coding.ports import CodingRepositoryPort
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugATC,
    WHODrugDrugATC,
    WHODrugDrugIngredient,
    WHODrugIngredient,
    WHODrugRecord,
)


@pytest.mark.parametrize(
    "word,expected",
    [
        ("pain", "pain"),  # No stem for short/unmatched
        ("headaches", "headache"),  # -es -> -e (stripping s)
        ("meningitis", "meningitis"),  # -itis -> -itis (retained)
        ("hepatitises", "hepatitis"),  # -itises -> -itis
        ("allergies", "allergy"),  # -ies -> -y
        ("vomiting", "vomit"),  # -ing -> base
        ("infected", "infect"),  # -ed -> base
        ("severely", "severe"),  # -ly -> base
        ("clinical", "clinic"),  # -al -> base
        ("ss", "ss"),  # short/special ending us/is/ss
    ],
)
def test_stem_word(word, expected):
    """Verify stemming of clinical term words.

    Requirements: PRD-SYS-049
    """
    assert stem_word(word) == expected


@pytest.mark.parametrize(
    "term,expected",
    [
        ("Mild headache", "headache"),  # Case folding & clinical stop word
        ("Onset of acute gastritis", "gastritis"),  # Stop phrase, stop words, stemming
        (
            "Severe recurring pain, chronic",
            "pain",
        ),  # Stripping punctuation and stop-words
        ("Clinical history of hepatitises", "clinic hepatitis"),  # phrase & stemming
    ],
)
def test_normalize_term(term, expected):
    """Verify clinical term normalization.

    Requirements: PRD-SYS-049
    """
    assert normalize_term(term) == expected


def test_similarity_computations():
    """Verify weighted similarity distance computation.

    Requirements: PRD-SYS-049
    """
    # Perfect match
    score = calculate_combined_score("headache", "headache")
    assert score == 1.0

    # Non match
    score = calculate_combined_score("headache", "vomit")
    assert score < 0.2

    # Verify weighted math: 0.4 * S_Lev + 0.6 * S_Cos
    v = "acute migraine"
    d = "migraine"
    # v split: "acute migraine" -> "acute migraine" (Wait, "acute" is a stop word in normalize_term!)
    # Let's test with non-stop-words: "severe pain" vs "pain"
    # "severe" is stop-word!
    # Let's use: "cough symptom" vs "cough"
    v = "cough symptom"
    d = "cough"
    # len("cough symptom") = 13, len("cough") = 5.
    # Levenshtein distance: 8.
    # S_Lev = 1 - 8 / 13 = 5/13 = 0.3846
    # v_tokens: ["cough", "symptom"], d_tokens: ["cough"]
    # v_counts: cough:1, symptom:1. Magnitude = sqrt(2) = 1.414
    # d_counts: cough:1. Magnitude = 1.0
    # Dot product = 1
    # S_Cos = 1 / sqrt(2) = 0.7071
    # CS = 0.4 * 0.3846 + 0.6 * 0.7071 = 0.1538 + 0.4242 = 0.578
    combined = calculate_combined_score(v, d)
    assert 0.55 <= combined <= 0.60


def test_token_cosine_similarity_empty():
    """Verify empty string token cosine similarity.

    Requirements: PRD-SYS-049
    """
    assert token_cosine_similarity("", "") == 1.0
    assert token_cosine_similarity("abc", "") == 0.0
    assert token_cosine_similarity("", "abc") == 0.0


def test_cache_ttl_configuration():
    """Verify terminology cache TTL environment variable resolution hierarchy.

    Requirements: PRD-SYS-049
    """
    # Default TTL or environment TTL
    with patch.dict(os.environ, {"CODING_CACHE_TTL": "10"}):
        cache = CodingCache()
        assert cache.ttl == 10.0

    # Test TERMINOLOGY_CACHE_TTL priority
    with patch.dict(
        os.environ, {"TERMINOLOGY_CACHE_TTL": "20", "CODING_CACHE_TTL": "10"}
    ):
        cache = CodingCache()
        assert cache.ttl == 20.0

    # Direct override
    cache = CodingCache(ttl=5.0)
    assert cache.ttl == 5.0


def test_cache_aside_and_stale_fallback():
    """Verify cache-aside and stale fallback behavior.

    Requirements: PRD-SYS-049
    """
    cache = CodingCache(ttl=0.1)  # tiny TTL
    key = ("MEDDRA", "26.0", "headache", "LLT")
    data = {"status": "AUTO-CODED", "match": "Headache"}

    # Initially empty
    hit, expired = cache.get(key)
    assert hit is None
    assert expired is None

    # Set and hit
    cache.set(key, data)
    hit, expired = cache.get(key)
    assert hit == data
    assert expired is None

    # Wait for TTL to expire
    time.sleep(0.15)
    hit, expired = cache.get(key)
    assert hit is None
    assert expired == data


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> None:
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        ),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    coding_cache.clear()


@pytest.mark.asyncio
async def test_meddra_matching_integration():
    # Seed MedDRA terminology
    async with db_manager.get_session_maker()() as session, session.begin():
        # Term 1: LLT Headache
        session.add(
            MedDRATerm(
                dictionary_version="26.0",
                code="10019211",
                term_name="Headache",
                level="LLT",
            )
        )
        # Term 2: PT Headache (same code)
        session.add(
            MedDRATerm(
                dictionary_version="26.0",
                code="10019211",
                term_name="Headache",
                level="PT",
            )
        )
        # Term 3: Migraine
        session.add(
            MedDRATerm(
                dictionary_version="26.0",
                code="10029300",
                term_name="Migraine",
                level="LLT",
            )
        )
        # Term 4: Stomach ache
        session.add(
            MedDRATerm(
                dictionary_version="26.0",
                code="10034500",
                term_name="Stomach ache",
                level="LLT",
            )
        )

        # Hierarchies
        session.add(
            MedDRAHierarchy(
                dictionary_version="26.0",
                llt_code="10019211",
                pt_code="10019211",
                hlt_code="10019231",
                hlgt_code="10029214",
                soc_code="10029205",
                primary_soc_flag="Y",
            )
        )

    async with db_manager.get_session_maker()() as session:
        # Case 1: Exact Match -> Auto-coded with hierarchy
        result = await match_verbatim_term(
            session, "headache", "MEDDRA", "26.0", target_level="LLT"
        )
        assert result["status"] == "AUTO-CODED"
        assert result["match"]["code"] == "10019211"
        assert len(result["match"]["hierarchies"]) == 1
        assert result["match"]["hierarchies"][0]["soc_code"] == "10029205"

        # Case 2: Fuzzy Suggestion -> Score in [0.60, 0.85)
        # "headaches symptom"
        result2 = await match_verbatim_term(
            session, "headaches symptom", "MEDDRA", "26.0", target_level="LLT"
        )
        assert result2["status"] == "SUGGESTIONS"
        assert result2["match"] is None
        assert len(result2["suggestions"]) >= 1
        assert result2["suggestions"][0]["code"] == "10019211"

        # Case 3: Uncodable -> Score < 0.60
        result3 = await match_verbatim_term(
            session, "broken leg fracture", "MEDDRA", "26.0", target_level="LLT"
        )
        assert result3["status"] == "UNCODABLE"
        assert result3["match"] is None
        assert result3["suggestions"] == []


@pytest.mark.asyncio
async def test_whodrug_matching_integration():
    # Seed WHODrug
    async with db_manager.get_session_maker()() as session, session.begin():
        session.add(
            WHODrugRecord(
                dictionary_version="2024-03",
                drug_code="00010101001",
                preferred_name="ASPIRIN",
                drug_name="ASPIRIN TABLET",
            )
        )
        session.add(
            WHODrugATC(
                dictionary_version="2024-03",
                atc_code="N02BA01",
                description="acetylsalicylic acid",
            )
        )
        session.add(
            WHODrugDrugATC(
                dictionary_version="2024-03",
                drug_code="00010101001",
                atc_code="N02BA01",
            )
        )
        session.add(
            WHODrugIngredient(
                dictionary_version="2024-03",
                ingredient_code="0000000001",
                ingredient_name="ACETYLSALICYLIC ACID",
            )
        )
        session.add(
            WHODrugDrugIngredient(
                dictionary_version="2024-03",
                drug_code="00010101001",
                ingredient_code="0000000001",
            )
        )

    async with db_manager.get_session_maker()() as session:
        # Match using Preferred Name
        result = await match_verbatim_term(session, "ASPIRIN", "WHODRUG", "2024-03")
        assert result["status"] == "AUTO-CODED"
        assert result["match"]["drug_code"] == "00010101001"
        assert len(result["match"]["atc_context"]) == 1
        assert result["match"]["atc_context"][0]["atc_code"] == "N02BA01"
        assert len(result["match"]["ingredients"]) == 1
        assert (
            result["match"]["ingredients"][0]["ingredient_name"]
            == "ACETYLSALICYLIC ACID"
        )


@pytest.mark.asyncio
async def test_cache_degradation_and_stale_on_error():
    # Verify stale-on-error fallback: if DB raises exception but cache has stale (expired) entry, it serves the stale entry.
    cache = CodingCache(ttl=-1.0)  # Always expired
    key = ("MEDDRA", "26.0", "headache", None)
    stale_data = {"status": "AUTO-CODED", "match": "stale-headache"}
    cache.set(key, stale_data)

    with patch("apps.execution.coding.matcher.coding_cache", cache):
        mock_session = MagicMock()
        # Mocking match to raise a DB exception
        with patch(
            "apps.execution.coding.matcher._match_meddra",
            side_effect=Exception("Database down"),
        ):
            # Should fallback to stale_data instead of raising!
            res = await match_verbatim_term(mock_session, "headache", "MEDDRA", "26.0")
            assert res == stale_data


@pytest.mark.asyncio
async def test_cache_unavailability_graceful_degradation():
    # If cache get/set raises exceptions, lookup should still complete normally from DB.
    async with db_manager.get_session_maker()() as session, session.begin():
        session.add(
            MedDRATerm(
                dictionary_version="26.0",
                code="10019211",
                term_name="Headache",
                level="LLT",
            )
        )

    broken_cache = MagicMock()
    broken_cache.get.side_effect = Exception("Cache disconnected")
    broken_cache.set.side_effect = Exception("Cache full/broken")

    with patch("apps.execution.coding.matcher.coding_cache", broken_cache):
        async with db_manager.get_session_maker()() as session:
            # Should still run successfully and return DB match!
            result = await match_verbatim_term(
                session, "headache", "MEDDRA", "26.0", target_level="LLT"
            )
            assert result["status"] == "AUTO-CODED"
            assert result["match"]["code"] == "10019211"


class DummyMockCodingRepository(CodingRepositoryPort):
    """A database-less mock repository implementing CodingRepositoryPort."""

    def __init__(
        self,
        meddra_terms=None,
        whodrug_records=None,
        hierarchies=None,
        whodrug_context=None,
    ):
        self.meddra_terms = meddra_terms or []
        self.whodrug_records = whodrug_records or []
        self.hierarchies = hierarchies or []
        self.whodrug_context = whodrug_context or ([], [])

    async def get_by_id(self, entity_id: str) -> Any:
        return None

    async def save(self, entity: Any) -> Any:
        return entity

    async def get_assignment(self, assignment_id: str) -> Any:
        return None

    async def list_assignments(self, **kwargs) -> list[Any]:
        return []

    async def save_assignment(self, assignment: Any) -> None:
        pass

    async def add_ledger(self, ledger_data: dict) -> None:
        pass

    async def get_active_queries(self, observation_id: str) -> list[Any]:
        return []

    async def save_query(self, query: Any) -> None:
        pass

    async def add_outbox_entry(self, entry: Any) -> None:
        pass

    async def add_query_resolve_outbox_entry(self, **kwargs) -> None:
        pass

    async def validate_meddra_term(self, version: str, code: str) -> Any:
        return None

    async def validate_whodrug_record(self, version: str, code: str) -> Any:
        return None

    async def get_meddra_hierarchy(self, term_record: Any, version: str) -> list[Any]:
        return self.hierarchies

    async def get_whodrug_context(
        self, rec_record: Any, version: str
    ) -> tuple[list[Any], list[Any]]:
        return self.whodrug_context

    async def list_meddra_terms(
        self, version: str, target_level: str | None = None
    ) -> list[Any]:
        return self.meddra_terms

    async def list_whodrug_records(self, version: str) -> list[Any]:
        return self.whodrug_records


@pytest.mark.asyncio
async def test_matcher_database_less_with_mock_port():
    """Verify that the fuzzy matcher runs in a database-less environment using dummy mock data on the ports.

    @req:PRD-SYS-049
    """

    mock_terms = [
        {"code": "1111", "term_name": "Hypotension", "level": "LLT"},
        {"code": "2222", "term_name": "Hypertension", "level": "LLT"},
    ]
    mock_hierarchies = [
        {
            "llt_code": "1111",
            "llt_name": "Hypotension",
            "pt_code": "1001",
            "pt_name": "Hypotension PT",
            "hlt_code": "2001",
            "hlt_name": "Vascular HLT",
            "hlgt_code": "3001",
            "hlgt_name": "Vascular HLGT",
            "soc_code": "4001",
            "soc_name": "Cardiac SOC",
            "primary_soc_flag": "Y",
        }
    ]
    repo = DummyMockCodingRepository(
        meddra_terms=mock_terms, hierarchies=mock_hierarchies
    )

    result = await match_verbatim_term(
        repo, "hypotension", "MEDDRA", "26.0", target_level="LLT"
    )
    assert result["status"] == "AUTO-CODED"
    assert result["match"]["code"] == "1111"
    assert result["match"]["hierarchies"][0]["soc_name"] == "Cardiac SOC"
