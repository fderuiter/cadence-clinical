import os
import time
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
    assert normalize_term(term) == expected


def test_similarity_computations():
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
    assert token_cosine_similarity("", "") == 1.0
    assert token_cosine_similarity("abc", "") == 0.0
    assert token_cosine_similarity("", "abc") == 0.0


def test_cache_ttl_configuration():
    # Default TTL or environment TTL
    with patch.dict(os.environ, {"CODING_CACHE_TTL": "10"}):
        cache = CodingCache()
        assert cache.ttl == 10.0

    # Direct override
    cache = CodingCache(ttl=5.0)
    assert cache.ttl == 5.0


def test_cache_aside_and_stale_fallback():
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
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
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
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
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
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
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
