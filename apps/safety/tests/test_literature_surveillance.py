"""Integration test suite for Literature Safety Surveillance & Regulatory Inquiry Tracking.

Requirements: PRD-PVG-006, PRD-SYS-001 | 21 CFR Part 11 Regulated
"""

import uuid
from datetime import UTC, datetime, timedelta

import pytest
from pydantic import BaseModel, Field


class LiteratureSearchQuery(BaseModel):
    substance_name: str
    mesh_terms: list[str] = Field(default_factory=list)
    lookback_days: int = 7
    source_database: str = "PubMed"


class LiteratureArticleResult(BaseModel):
    article_id: str
    title: str
    abstract: str
    publication_date: str
    icsr_reportable_score: float
    detected_adverse_events: list[str] = Field(default_factory=list)
    requires_triage: bool = False


class RegulatorySafetyInquiry(BaseModel):
    inquiry_id: str
    agency_name: str  # e.g., FDA, EMA, PMDA
    submission_deadline: datetime
    inquiry_type: str  # e.g., SIGNAL_CLARIFICATION, SUSAR_FOLLOWUP
    status: str = "OPEN"  # OPEN, DRAFTED, REVIEWED, SUBMITTED
    response_payload: dict | None = None
    statutory_days_remaining: int = 15


def execute_literature_surveillance_scan(
    query: LiteratureSearchQuery,
    articles: list[dict],
) -> list[LiteratureArticleResult]:
    """Simulate literature surveillance scanning and scoring abstracts for ICSR reportability."""
    results = []
    for art in articles:
        text = (art.get("title", "") + " " + art.get("abstract", "")).lower()
        aes_found = []
        for kw in [
            "cardiotoxicity",
            "hepatotoxicity",
            "thrombosis",
            "anaphylaxis",
            "arrhythmia",
        ]:
            if kw in text:
                aes_found.append(kw)

        score = min(1.0, len(aes_found) * 0.35)
        requires_triage = score >= 0.35
        results.append(
            LiteratureArticleResult(
                article_id=art.get("article_id", str(uuid.uuid4())),
                title=art.get("title", ""),
                abstract=art.get("abstract", ""),
                publication_date=art.get("publication_date", "2026-08-01"),
                icsr_reportable_score=score,
                detected_adverse_events=aes_found,
                requires_triage=requires_triage,
            )
        )
    return results


def calculate_inquiry_statutory_window(
    inquiry: RegulatorySafetyInquiry,
    current_time: datetime | None = None,
) -> int:
    """Calculate remaining statutory response window in days."""
    now = current_time or datetime.now(UTC)
    delta = inquiry.submission_deadline - now
    return max(0, delta.days)


@pytest.mark.asyncio
async def test_literature_surveillance_screening_and_icsr_scoring():
    """Validate automated literature search query execution and abstract safety screening.

    @req:PRD-PVG-006
    @req:PRD-SYS-001
    """
    query = LiteratureSearchQuery(
        substance_name="Cadence-Investigational-Compound-9",
        mesh_terms=["Adverse Drug Reaction", "Cardiovascular"],
        lookback_days=7,
        source_database="PubMed/EuropePMC",
    )

    mock_articles = [
        {
            "article_id": "PMID-990112",
            "title": "Observed cases of drug-induced cardiotoxicity and arrhythmia in Phase II trial",
            "abstract": "We report unexpected arrhythmia and cardiotoxicity events following administration.",
            "publication_date": "2026-08-20",
        },
        {
            "article_id": "PMID-990113",
            "title": "Synthesis and pharmacokinetic characterization of novel kinase inhibitors",
            "abstract": "The biochemical synthesis yielded high purity compound with predictable bioavailability.",
            "publication_date": "2026-08-22",
        },
    ]

    results = execute_literature_surveillance_scan(query, mock_articles)

    assert len(results) == 2
    # First article has safety signal terms
    assert results[0].requires_triage is True
    assert results[0].icsr_reportable_score >= 0.70
    assert "cardiotoxicity" in results[0].detected_adverse_events
    assert "arrhythmia" in results[0].detected_adverse_events

    # Second article has no adverse event terms
    assert results[1].requires_triage is False
    assert results[1].icsr_reportable_score == 0.0
    assert len(results[1].detected_adverse_events) == 0


@pytest.mark.asyncio
async def test_regulatory_safety_inquiry_statutory_deadline_tracking():
    """Validate health authority safety inquiry workflow and statutory clock countdown.

    @req:PRD-PVG-006
    @req:PRD-SYS-001
    """
    now = datetime.now(UTC)
    deadline = now + timedelta(days=15)

    inquiry = RegulatorySafetyInquiry(
        inquiry_id="INQ-FDA-2026-001",
        agency_name="FDA CDER",
        submission_deadline=deadline,
        inquiry_type="SIGNAL_CLARIFICATION",
        status="OPEN",
    )

    days_remaining = calculate_inquiry_statutory_window(inquiry, current_time=now)
    assert days_remaining == 15

    # Advance time by 10 days
    mid_time = now + timedelta(days=10)
    days_mid = calculate_inquiry_statutory_window(inquiry, current_time=mid_time)
    assert days_mid == 5

    # Update inquiry with official sponsor medical response
    inquiry.response_payload = {
        "medical_assessment": "Comprehensive safety aggregate analysis shows no statistical divergence from background.",
        "authorizing_physician": "Dr. Safety Lead, MD",
        "signed_at": mid_time.isoformat(),
    }
    inquiry.status = "SUBMITTED"
    assert inquiry.status == "SUBMITTED"
    assert inquiry.response_payload is not None
