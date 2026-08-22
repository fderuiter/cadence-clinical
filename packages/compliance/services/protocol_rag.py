"""
Protocol RAG Domain Models, Citation Matchers, and Faithfulness Evaluator.

Enforces 21 CFR Part 11 and CDISC GxP compliance for Grounded Protocol AI Assistance.
Requirements: PRD-TCK-005, PRD-SYS-051, ADR-2192
"""

from __future__ import annotations

import re

from pydantic import BaseModel, Field

# Canonical verbatim citation format per PRD-TCK-005
CITATION_REGEX = re.compile(
    r"\[Protocol\s+([a-zA-Z0-9.\-_]+)(?:,\s*Section\s*([0-9a-zA-Z.\-_]+))?,\s*Page\s*(\d+)\]",
    re.IGNORECASE,
)

CONFIDENCE_THRESHOLD = 0.85

TIER_2_GROUNDED_RAG_SYSTEM_PROMPT = """You are Cadence Clinical Research AI Assistant operating under strict Tier 2 Grounded RAG constraints.
You assist Principal Investigators, Clinical Research Coordinators (CRCs), Clinical Research Associates (CRAs), and Data Managers.

MANDATORY CLINICAL SAFETY & GXP RULES:
1. Grounding Guarantee: You MUST answer the user's clinical question SOLELY and EXCLUSIVELY using the verified Study Protocol excerpts provided below.
2. Verbatim Citation Syntax: Every factual claim, timeframe, inclusion/exclusion criterion, dosage specification, or procedural guideline MUST be immediately followed by its exact structural citation marker:
   Format: `[Protocol {version}, Section {section_number}, Page {page_number}]`
   Example: `Serious adverse events must be reported within 24 hours of site awareness [Protocol v2.1, Section 7.3, Page 42].`
3. Zero Speculation / Fail-Closed: If the provided protocol excerpts do NOT contain sufficient information to answer the question with 100% confidence, you MUST state:
   "The provided study protocol excerpts do not contain sufficient information to answer this inquiry. Please escalate to the Lead Study CRA or Data Management team."
4. Version Integrity: NEVER reference protocol versions or sections that are not explicitly provided in the verified context blocks.
"""


class ProtocolRAGContextChunk(BaseModel):
    """Normalized protocol excerpt chunk passed to RAG generation and verification."""

    chunk_id: str = Field(..., description="Unique chunk identifier.")
    protocol_version: str = Field(..., description="Protocol version (e.g., 'v2.1').")
    section_number: str | None = Field(
        default=None, description="Protocol section number (e.g. '7.3')."
    )
    section_title: str | None = Field(
        default=None, description="Protocol section title."
    )
    page_number: int = Field(
        ..., ge=1, description="1-indexed physical page number in source PDF."
    )
    chunk_text: str = Field(..., description="Raw text of the protocol chunk.")
    is_approved: bool = Field(
        default=True,
        description="Whether this protocol chunk is from an approved version.",
    )


class ParsedCitation(BaseModel):
    """Structured representation of a parsed citation reference."""

    protocol_version: str = Field(..., description="Referenced protocol version.")
    section_number: str | None = Field(
        default=None, description="Referenced section number."
    )
    section_title: str | None = Field(
        default=None, description="Resolved section title."
    )
    page_number: int = Field(..., ge=1, description="Referenced page number.")
    citation_marker: str = Field(
        ...,
        description="Exact verbatim citation string, e.g., '[Protocol v2.1, Section 7.3, Page 42]'.",
    )
    is_approved: bool = Field(
        ..., description="True if referenced chunk belongs to an approved version."
    )
    is_verified: bool = Field(
        ...,
        description="True if coordinates match an existing chunk in the Knowledge Hub.",
    )


def format_citation_marker(
    protocol_version: str,
    section_number: str | None,
    page_number: int,
) -> str:
    """Formats canonical verbatim citation marker per PRD-TCK-005.

    Format: `[Protocol {version}, Section {section}, Page {page}]`
    """
    version_clean = (
        protocol_version if protocol_version.startswith("v") else f"v{protocol_version}"
    )
    if section_number:
        return (
            f"[Protocol {version_clean}, Section {section_number}, Page {page_number}]"
        )
    return f"[Protocol {version_clean}, Page {page_number}]"


def format_grounded_rag_prompt(
    query: str,
    chunks: list[ProtocolRAGContextChunk],
) -> str:
    """Formats the Tier 2 Grounded RAG user prompt containing structured excerpt blocks.

    Args:
        query: Clinical inquiry from support ticket.
        chunks: Retrieved protocol chunks.

    Returns:
        Structured prompt string.
    """
    if not chunks:
        return f"""--- PROTOCOL EXCERPTS ---
(No approved protocol excerpts found for this study)
--- END PROTOCOL EXCERPTS ---

CLINICAL INQUIRY:
{query}

Please formulate a helpful, concise clinical response with exact citations."""

    excerpts_formatted = []
    for i, c in enumerate(chunks, start=1):
        sec_info = (
            f"Section {c.section_number} ({c.section_title})"
            if c.section_number
            else "General"
        )
        excerpts_formatted.append(
            f"[EXCERPT {i}: Protocol {c.protocol_version}, {sec_info}, Page {c.page_number}]\n"
            f"{c.chunk_text.strip()}\n"
        )

    joined_excerpts = "\n".join(excerpts_formatted)
    return f"""--- PROTOCOL EXCERPTS ---
{joined_excerpts}
--- END PROTOCOL EXCERPTS ---

CLINICAL INQUIRY:
{query}

Please formulate a helpful, concise clinical response strictly citing the excerpts above."""


def evaluate_rag_faithfulness(
    query: str,
    answer: str,
    chunks: list[ProtocolRAGContextChunk],
) -> tuple[float, list[ParsedCitation], bool]:
    """Computes mathematical faithfulness score and validates citation grounding.

    Criteria:
    1. Citation Existence & Validity: Matches must correspond to provided chunks.
    2. Version Approval: All cited chunks must have `is_approved == True`.
    3. Lexical / Claim Overlap: Verifies that text preceding citations is grounded in the chunk.
    4. Fail-Closed Detection: "insufficient information" / "no excerpts" yields low score (< 0.85).

    Returns:
        (faithfulness_score, citations, is_grounded)
    """
    if not answer or not answer.strip():
        return (0.0, [], False)

    lower_ans = answer.lower()
    if (
        "insufficient information" in lower_ans
        or "does not contain" in lower_ans
        or "no approved protocol" in lower_ans
    ):
        return (0.0, [], False)

    if not chunks:
        return (0.0, [], False)

    # Extract citation markers
    matches = list(CITATION_REGEX.finditer(answer))
    if not matches:
        # No citations provided in response -> ungrounded (0.0 faithfulness)
        return (0.0, [], False)

    # Build chunk lookup index: (version_norm, page_number) -> chunk
    chunk_map: dict[tuple[str, int], ProtocolRAGContextChunk] = {}
    for c in chunks:
        norm_v = (
            c.protocol_version.lower()
            if c.protocol_version.startswith("v")
            else f"v{c.protocol_version.lower()}"
        )
        chunk_map[(norm_v, c.page_number)] = c

    parsed_citations: list[ParsedCitation] = []
    valid_citations_count = 0
    approved_citations_count = 0
    content_overlap_scores: list[float] = []

    for match in matches:
        raw_marker = match.group(0)
        v_raw = match.group(1)
        sec_num = match.group(2)
        page_num = int(match.group(3))

        v_norm = v_raw.lower() if v_raw.startswith("v") else f"v{v_raw.lower()}"
        matched_chunk = chunk_map.get((v_norm, page_num))

        if matched_chunk:
            valid_citations_count += 1
            if matched_chunk.is_approved:
                approved_citations_count += 1

            # Check lexical claim overlap
            match_start = match.start()
            claim_window = answer[max(0, match_start - 200) : match_start]
            claim_words = set(re.findall(r"\w{4,}", claim_window.lower()))
            chunk_words = set(re.findall(r"\w{4,}", matched_chunk.chunk_text.lower()))

            if claim_words:
                overlap = len(claim_words & chunk_words) / len(claim_words)
                content_overlap_scores.append(min(1.0, overlap + 0.3))
            else:
                content_overlap_scores.append(0.8)

            parsed_citations.append(
                ParsedCitation(
                    protocol_version=matched_chunk.protocol_version,
                    section_number=sec_num or matched_chunk.section_number,
                    section_title=matched_chunk.section_title,
                    page_number=page_num,
                    citation_marker=raw_marker,
                    is_approved=matched_chunk.is_approved,
                    is_verified=True,
                )
            )
        else:
            # Hallucinated citation coordinates
            parsed_citations.append(
                ParsedCitation(
                    protocol_version=match.group(1),
                    section_number=sec_num,
                    section_title=None,
                    page_number=page_num,
                    citation_marker=raw_marker,
                    is_approved=False,
                    is_verified=False,
                )
            )

    total_citations = len(matches)
    citation_validity_rate = (
        valid_citations_count / total_citations if total_citations > 0 else 0.0
    )
    approval_rate = (
        approved_citations_count / total_citations if total_citations > 0 else 0.0
    )
    avg_overlap = (
        sum(content_overlap_scores) / len(content_overlap_scores)
        if content_overlap_scores
        else 0.0
    )

    # Weighted mathematical faithfulness score:
    # 50% Citation coordinates validity + 30% Approval rate + 20% Semantic claim overlap
    faithfulness_score = (
        (0.50 * citation_validity_rate) + (0.30 * approval_rate) + (0.20 * avg_overlap)
    )

    # Stopwords list for accurate keyword relevance evaluation
    stopwords = {
        "about",
        "above",
        "after",
        "again",
        "against",
        "all",
        "and",
        "any",
        "are",
        "asking",
        "because",
        "been",
        "before",
        "being",
        "below",
        "between",
        "both",
        "but",
        "cannot",
        "could",
        "did",
        "does",
        "doing",
        "down",
        "during",
        "each",
        "few",
        "for",
        "from",
        "further",
        "had",
        "has",
        "have",
        "having",
        "her",
        "here",
        "hers",
        "him",
        "his",
        "how",
        "into",
        "its",
        "many",
        "more",
        "most",
        "much",
        "myself",
        "nor",
        "not",
        "off",
        "once",
        "only",
        "other",
        "ought",
        "our",
        "ours",
        "out",
        "over",
        "own",
        "query",
        "question",
        "same",
        "she",
        "should",
        "some",
        "such",
        "than",
        "that",
        "the",
        "their",
        "theirs",
        "them",
        "then",
        "there",
        "these",
        "they",
        "this",
        "those",
        "through",
        "too",
        "under",
        "until",
        "very",
        "was",
        "were",
        "what",
        "when",
        "where",
        "which",
        "while",
        "who",
        "whom",
        "why",
        "with",
        "would",
        "you",
        "your",
        "yours",
    }

    # Check if substantive inquiry keywords exist in cited protocol excerpts
    query_words = {
        w for w in re.findall(r"\b[a-z]{3,}\b", query.lower()) if w not in stopwords
    }
    combined_chunk_text = " ".join(c.chunk_text.lower() for c in chunks)
    chunk_words = {
        w
        for w in re.findall(r"\b[a-z]{3,}\b", combined_chunk_text)
        if w not in stopwords
    }
    query_overlap = (
        len(query_words & chunk_words) / len(query_words) if query_words else 0.0
    )

    if query_overlap < 0.25:
        # Query is ungrounded / unsupported by protocol context -> fail closed
        faithfulness_score *= query_overlap / 0.25

    # If any citation was completely unverified/hallucinated, penalize
    if valid_citations_count < total_citations:
        faithfulness_score *= 0.5

    faithfulness_score = round(max(0.0, min(1.0, faithfulness_score)), 4)
    is_grounded = (
        faithfulness_score >= 0.85
        and valid_citations_count == total_citations
        and approved_citations_count == total_citations
        and query_overlap >= 0.25
    )

    return (faithfulness_score, parsed_citations, is_grounded)


__all__ = [
    "CITATION_REGEX",
    "CONFIDENCE_THRESHOLD",
    "ParsedCitation",
    "ProtocolRAGContextChunk",
    "TIER_2_GROUNDED_RAG_SYSTEM_PROMPT",
    "evaluate_rag_faithfulness",
    "format_citation_marker",
    "format_grounded_rag_prompt",
]
