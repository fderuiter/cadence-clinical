"""Unit and integration tests for eConsent Readability Harmonization Engine.

Validates Flesch-Kincaid Grade Level, Dale-Chall scoring algorithms, AI Gateway Tier 2 routing,
and 21 CFR Part 11 audit integrity on clause harmonization.

@req:PRD-SYS-051
@req:PRD-SYS-001
"""

import time
from unittest.mock import AsyncMock, patch

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import (
    Base,
    ConsentAuditLog,
    ConsentClause,
)
from apps.econsent.domain.readability import (
    count_syllables_word,
    interpret_dale_chall_grade_level,
    interpret_reading_ease,
    is_dale_chall_familiar,
    tokenize_sentences,
    tokenize_words,
)
from apps.econsent.main import app
from apps.econsent.services.readability import (
    ReadabilityHarmonizerService,
    ReadabilityMetricsService,
)
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup and teardown in-memory SQLite database for test suite."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "designer.user",
    roles: str = "sponsor_designer",
    change_reason: str = "Readability Harmonization Unit Test",
) -> dict[str, str]:
    """Produces authentic HMAC-SHA256 Gateway signature V2 headers."""
    timestamp = str(time.time())
    sig = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


# =========================================================================
# 1. Phonetic Syllables & Dale-Chall Standard Vocabulary Tests
# =========================================================================
def test_syllable_counter_deterministic() -> None:
    """Validate phonetic syllable counting for single, multi-syllabic, and medical terms.

    @req:PRD-SYS-051
    """
    # Single syllable words
    assert count_syllables_word("heart") == 1
    assert count_syllables_word("blood") == 1
    assert count_syllables_word("drug") == 1
    assert count_syllables_word("pain") == 1

    # Multi-syllable standard and medical words
    assert count_syllables_word("table") == 2
    assert count_syllables_word("doctor") == 2
    assert count_syllables_word("hospital") == 3
    assert count_syllables_word("hypertension") == 4
    assert count_syllables_word("randomization") == 5

    # Edge cases
    assert count_syllables_word("") == 0
    assert count_syllables_word("   ") == 0


def test_dale_chall_familiar_vocabulary() -> None:
    """Validate Dale-Chall familiar word classification and English morphological inflection rules.

    @req:PRD-SYS-051
    """
    # Standard familiar words
    assert is_dale_chall_familiar("doctor") is True
    assert is_dale_chall_familiar("hospital") is True
    assert is_dale_chall_familiar("water") is True
    assert is_dale_chall_familiar("family") is True

    # Common inflections
    assert is_dale_chall_familiar("doctors") is True  # plural
    assert is_dale_chall_familiar("walked") is True  # past tense
    assert is_dale_chall_familiar("walking") is True  # gerund
    assert is_dale_chall_familiar("quickly") is True  # adverb

    # Numbers
    assert is_dale_chall_familiar("100") is True
    assert is_dale_chall_familiar("2026") is True

    # Complex clinical jargon not in Dale-Chall familiar list
    assert is_dale_chall_familiar("myocardial") is False
    assert is_dale_chall_familiar("infarction") is False
    assert is_dale_chall_familiar("subcutaneous") is False
    assert is_dale_chall_familiar("pharmacokinetics") is False
    assert is_dale_chall_familiar("hyperglycemia") is False


def test_tokenizers() -> None:
    """Validate sentence and word tokenization with HTML stripping.

    @req:PRD-SYS-051
    """
    html_text = "<p>This is the first sentence. Here is the second sentence! Is this the third?</p>"
    sentences = tokenize_sentences(html_text)
    assert len(sentences) == 3

    words = tokenize_words(html_text)
    assert "This" in words
    assert "sentence" in words
    assert "p" not in words  # HTML tags stripped


def test_qualitative_interpretations() -> None:
    """Validate qualitative readability interpretations for FRE and Dale-Chall.

    @req:PRD-SYS-051
    """
    assert "Easy" in interpret_reading_ease(85.0)
    assert "Difficult" in interpret_reading_ease(40.0)

    assert interpret_dale_chall_grade_level(4.5) == "Grade 4 and below"
    assert interpret_dale_chall_grade_level(5.5) == "Grades 5-6"
    assert interpret_dale_chall_grade_level(6.5) == "Grades 7-8"
    assert interpret_dale_chall_grade_level(7.5) == "Grades 9-10"
    assert interpret_dale_chall_grade_level(11.0) == "Grades 16+ (College Graduate)"


# =========================================================================
# 2. Readability Metrics Calculation Tests
# =========================================================================
def test_readability_metrics_service_complex_text() -> None:
    """Validate calculation of FKGL, FRE, and Dale-Chall scores on complex medical text.

    @req:PRD-SYS-051
    """
    service = ReadabilityMetricsService()
    complex_text = (
        "Subjects presenting with acute myocardial infarction and severe essential hypertension "
        "will undergo double-blind randomization to evaluate pharmacokinetics and nephrotoxicity."
    )

    metrics = service.compute_metrics(complex_text)

    assert metrics.word_count > 15
    assert metrics.sentence_count == 1
    assert metrics.syllable_count > 30
    assert metrics.difficult_word_count >= 5
    assert "myocardial" in metrics.difficult_words
    assert "infarction" in metrics.difficult_words

    # Complex clinical text should exceed 8th grade reading level
    assert metrics.flesch_kincaid_grade_level > 10.0
    assert metrics.dale_chall_score > 8.0
    assert metrics.is_target_grade_level is False


def test_readability_metrics_service_plain_language_text() -> None:
    """Validate calculation of FKGL, FRE, and Dale-Chall scores on 6th-8th grade plain-language text.

    @req:PRD-SYS-051
    """
    service = ReadabilityMetricsService()
    plain_text = (
        "This study will test a new medicine to lower blood pressure. "
        "You can join if you are over 18 years old. "
        "We will draw a small sample of blood from your arm."
    )

    metrics = service.compute_metrics(plain_text)

    assert metrics.word_count > 20
    assert metrics.sentence_count == 3
    assert metrics.difficult_word_count <= 2

    # Plain language text should be within elementary/middle school grade levels
    assert metrics.flesch_kincaid_grade_level <= 8.0
    assert metrics.flesch_reading_ease >= 70.0
    assert metrics.is_target_grade_level is True


def test_readability_metrics_empty_text() -> None:
    """Validate edge case handling for empty or whitespace text.

    @req:PRD-SYS-051
    """
    service = ReadabilityMetricsService()
    metrics = service.compute_metrics("   ")

    assert metrics.word_count == 0
    assert metrics.flesch_kincaid_grade_level == 0.0
    assert metrics.flesch_reading_ease == 100.0
    assert metrics.dale_chall_score == 0.0


# =========================================================================
# 3. Readability Harmonizer & AI Gateway Integration Tests
# =========================================================================
@pytest.mark.asyncio
async def test_harmonize_text_deterministic_fallback() -> None:
    """Validate jargon harmonization using deterministic clinical dictionary substitutions.

    @req:PRD-SYS-051
    """
    service = ReadabilityHarmonizerService()
    text = (
        "Patients suffering from severe hypertension and acute myocardial infarction "
        "will receive subcutaneous injections and undergo venipuncture."
    )

    result = await service.harmonize_text(text)

    assert "high blood pressure" in result.harmonized_text
    assert "heart attack" in result.harmonized_text
    assert "under the skin" in result.harmonized_text
    assert "blood draw" in result.harmonized_text

    assert len(result.substitutions) >= 4
    assert (
        result.harmonized_metrics.flesch_kincaid_grade_level
        <= result.original_metrics.flesch_kincaid_grade_level
    )
    assert result.grade_level_delta >= 0.0


@pytest.mark.asyncio
async def test_harmonize_text_with_mocked_ai_gateway() -> None:
    """Validate jargon harmonization when AI Gateway Tier 2 returns custom structured suggestions.

    @req:PRD-SYS-051
    """
    mock_ai_suggestions = [
        {
            "original_term": "myocardial infarction",
            "suggested_term": "heart attack",
            "rationale": "Lay term universally understood by patients.",
            "category": "clinical_terminology",
            "confidence_score": 0.99,
        },
        {
            "original_term": "pruritus",
            "suggested_term": "itching",
            "rationale": "Direct plain-language synonym.",
            "category": "clinical_terminology",
            "confidence_score": 0.95,
        },
    ]

    with patch(
        "apps.econsent.adapters.ai_readability_client.AIReadabilityGatewayClient.generate_simplification_suggestions",
        new_callable=AsyncMock,
        return_value=mock_ai_suggestions,
    ):
        service = ReadabilityHarmonizerService()
        text = "The study drug may cause mild pruritus or acute myocardial infarction."

        result = await service.harmonize_text(text)

        assert "itching" in result.harmonized_text
        assert "heart attack" in result.harmonized_text
        terms = [s.original_term.lower() for s in result.substitutions]
        assert "pruritus" in terms
        assert "myocardial infarction" in terms


# =========================================================================
# 4. REST API Endpoint Integration Tests
# =========================================================================
@pytest.mark.asyncio
async def test_api_readability_analyze_endpoint() -> None:
    """Verify POST /api/v1/econsent/readability/analyze returns complete readability indices.

    @req:PRD-SYS-051
    """
    client = TestClient(app)
    headers = get_auth_headers()
    payload = {
        "text": "This is a simple consent clause written for young participants.",
        "study_id": "CADENCE-101",
    }

    res = client.post(
        "/api/v1/econsent/readability/analyze",
        json=payload,
        headers=headers,
    )

    assert res.status_code == 200
    data = res.json()
    assert "metrics" in data
    metrics = data["metrics"]
    assert metrics["word_count"] > 0
    assert "flesch_kincaid_grade_level" in metrics
    assert "dale_chall_score" in metrics
    assert "is_target_grade_level" in metrics


@pytest.mark.asyncio
async def test_api_readability_harmonize_endpoint() -> None:
    """Verify POST /api/v1/econsent/readability/harmonize returns plain-language diffs and metrics.

    @req:PRD-SYS-051
    """
    client = TestClient(app)
    headers = get_auth_headers()
    payload = {
        "text": "Subjects diagnosed with hypertension will receive double-blind treatment.",
        "study_id": "CADENCE-101",
        "target_grade_level": 8.0,
        "protocol_version": "v2.0",
    }

    res = client.post(
        "/api/v1/econsent/readability/harmonize",
        json=payload,
        headers=headers,
    )

    assert res.status_code == 200
    data = res.json()
    assert "original_metrics" in data
    assert "harmonized_metrics" in data
    assert "substitutions" in data
    assert "harmonized_text" in data
    assert len(data["substitutions"]) > 0
    assert "high blood pressure" in data["harmonized_text"]


@pytest.mark.asyncio
async def test_api_clause_harmonization_apply_and_audit() -> None:
    """Verify applying readability harmonization increments clause version and emits 21 CFR Part 11 audit log.

    @req:PRD-SYS-051
    @req:PRD-SYS-001
    """
    # 1. Seed initial clause v1 in database
    async with db_manager.get_session_maker()() as session:
        initial_clause = ConsentClause(
            clause_id="clause-purpose-001",
            study_id="CADENCE-101",
            title="Study Purpose",
            text="The purpose is to treat patients with acute hypertension.",
            version_index=1,
            created_by="designer.user",
            reason_for_change="Initial clause drafting",
        )
        session.add(initial_clause)
        await session.commit()

    # 2. Apply harmonized plain-language version
    client = TestClient(app)
    headers = get_auth_headers(
        change_reason="Readability Harmonization: Replaced medical jargon for Protocol Amendment v2.0"
    )
    payload = {
        "harmonized_text": "The purpose is to treat patients with high blood pressure.",
        "reason_for_change": "Readability Harmonization: Replaced medical jargon for Protocol Amendment v2.0",
        "protocol_version": "v2.0",
    }

    res = client.post(
        "/api/v1/econsent/clauses/clause-purpose-001/harmonize",
        json=payload,
        headers=headers,
    )

    assert res.status_code == 200
    data = res.json()
    assert data["clause_id"] == "clause-purpose-001"
    assert data["version_index"] == 2
    assert "high blood pressure" in data["text"]
    assert data["protocol_version"] == "v2.0"
    assert data["metrics"]["flesch_kincaid_grade_level"] >= 0.0

    # 3. Verify Database State (both v1 and v2 preserved)
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ConsentClause)
            .where(ConsentClause.clause_id == "clause-purpose-001")
            .order_by(ConsentClause.version_index)
        )
        clauses = (await session.execute(stmt)).scalars().all()
        assert len(clauses) == 2
        assert clauses[0].version_index == 1
        assert "acute hypertension" in clauses[0].text
        assert clauses[1].version_index == 2
        assert "high blood pressure" in clauses[1].text

        # 4. Verify 21 CFR Part 11 Audit Trail Entry
        stmt_audit = select(ConsentAuditLog).where(
            ConsentAuditLog.action == "HARMONIZE_READABILITY_CLAUSE"
        )
        audit_entry = (await session.execute(stmt_audit)).scalars().first()
        assert audit_entry is not None
        assert audit_entry.actor_id == "designer.user"
        assert (
            "Harmonized clause 'clause-purpose-001' from v1 to v2"
            in audit_entry.details
        )
        assert "Protocol Amendment: v2.0" in audit_entry.details
        assert "FKGL:" in audit_entry.details


@pytest.mark.asyncio
async def test_api_clause_harmonization_not_found() -> None:
    """Verify 404 is returned when attempting to harmonize non-existent clause.

    @req:PRD-SYS-051
    """
    client = TestClient(app)
    headers = get_auth_headers()
    payload = {
        "harmonized_text": "Plain language text.",
        "reason_for_change": "Testing non-existent clause",
    }

    res = client.post(
        "/api/v1/econsent/clauses/non-existent-clause-999/harmonize",
        json=payload,
        headers=headers,
    )
    assert res.status_code == 404
    assert "not found" in res.json()["detail"].lower()


@pytest.mark.asyncio
async def test_api_clause_harmonization_auditor_forbidden() -> None:
    """Verify 403 is returned when user with auditor role attempts clause mutation.

    @req:PRD-SYS-051
    @req:PRD-SYS-001
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="auditor", user_id="auditor.user")
    payload = {
        "harmonized_text": "Plain language text.",
        "reason_for_change": "Auditor unauthorized mutation",
    }

    res = client.post(
        "/api/v1/econsent/clauses/clause-001/harmonize",
        json=payload,
        headers=headers,
    )
    assert res.status_code == 403
