"""
Domain search utilities for the Knowledge & Support Hub.

Provides pure-function algorithms for lexical scoring, term matching,
markdown text stripping, and highlighted snippet extraction per ADR-2188.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, ADR-2188
"""

import html
import json
import re


def strip_markdown(text: str) -> str:
    """
    Strips Markdown formatting elements to yield clean prose text.

    Args:
        text: Raw Markdown text.

    Returns:
        Clean plain text without markdown syntax.
    """
    if not text:
        return ""

    # Remove code blocks
    text = re.sub(r"```[\s\S]*?```", "", text)
    # Remove inline code
    text = re.sub(r"`([^`]+)`", r"\1", text)
    # Remove images
    text = re.sub(r"!\[.*?\]\(.*?\)", "", text)
    # Remove links, keep text
    text = re.sub(r"\[(.*?)\]\(.*?\)", r"\1", text)
    # Remove headings
    text = re.sub(r"^#+\s+", "", text, flags=re.MULTILINE)
    # Remove bold/italics
    text = re.sub(r"(\*\*|__)(.*?)\1", r"\2", text)
    text = re.sub(r"(\*|_)(.*?)\1", r"\2", text)
    # Remove blockquotes
    text = re.sub(r"^>\s+", "", text, flags=re.MULTILINE)
    # Remove list bullets
    text = re.sub(r"^[\s*+-]+\s+", "", text, flags=re.MULTILINE)
    text = re.sub(r"^\d+\.\s+", "", text, flags=re.MULTILINE)
    # Remove horizontal rules
    text = re.sub(r"^-{3,}\s*$", "", text, flags=re.MULTILINE)
    # Collapse consecutive whitespace
    return re.sub(r"\s+", " ", text).strip()


def extract_query_terms(query: str) -> list[str]:
    """
    Extracts alphanumeric search terms from a query string, lowercased.

    Args:
        query: Raw search query string.

    Returns:
        List of distinct non-empty search terms.
    """
    if not query:
        return []
    terms = re.findall(r"\w+", query.lower())
    # Return unique terms in order of first appearance
    seen: set[str] = set()
    result: list[str] = []
    for t in terms:
        if t not in seen and len(t) > 0:
            seen.add(t)
            result.append(t)
    return result


def generate_highlighted_snippet(
    text: str,
    query: str,
    max_words: int = 35,
) -> str:
    """
    Extracts a contextual snippet from body text and wraps matched query terms in <mark>...</mark>.

    Args:
        text: Source markdown or plain text.
        query: User search query.
        max_words: Maximum number of words in the extracted snippet.

    Returns:
        Highlighted HTML snippet string.
    """
    clean_text = strip_markdown(text)
    if not clean_text:
        return ""

    words = clean_text.split()
    if not words:
        return ""

    terms = extract_query_terms(query)
    if not terms:
        truncated = " ".join(words[:max_words])
        if len(words) > max_words:
            truncated += " ..."
        return html.escape(truncated)

    # Find word index of first matching term
    lower_words = [w.lower().strip(".,;:!?\"'()[]{}") for w in words]
    match_indices = [
        i
        for i, lw in enumerate(lower_words)
        if any(term in lw or lw in term for term in terms)
    ]

    if not match_indices:
        # No direct term match in body; return beginning of text
        snippet_words = words[:max_words]
        prefix = ""
        suffix = " ..." if len(words) > max_words else ""
    else:
        first_match = match_indices[0]
        # Center the window around first match
        half = max_words // 2
        start_idx = max(0, first_match - half)
        end_idx = min(len(words), start_idx + max_words)
        # Adjust start if we're near the end
        if end_idx - start_idx < max_words and start_idx > 0:
            start_idx = max(0, end_idx - max_words)

        snippet_words = words[start_idx:end_idx]
        prefix = "... " if start_idx > 0 else ""
        suffix = " ..." if end_idx < len(words) else ""

    snippet_raw = " ".join(snippet_words)

    # Escape HTML special characters before injecting <mark> tags
    escaped_snippet = html.escape(snippet_raw)

    # Wrap matching terms in <mark> tags
    for term in terms:
        pattern = re.compile(re.escape(term), re.IGNORECASE)
        escaped_snippet = pattern.sub(
            lambda m: f"<mark>{m.group(0)}</mark>", escaped_snippet
        )

    return f"{prefix}{escaped_snippet}{suffix}".strip()


def calculate_lexical_score(
    title: str,
    tags: list[str] | str | None,
    body: str,
    query: str,
) -> float:
    """
    Calculates a weighted lexical relevance score for search results.

    Weights:
        - Exact full-query phrase match in Title: +10.0
        - Title individual term matches: +5.0 per term
        - Tags term matches: +3.0 per term
        - Body markdown term matches: +1.0 per term (plus occurrence count)

    Args:
        title: Article title.
        tags: Article tags (list, JSON string, or comma-separated string).
        body: Article markdown or plain body.
        query: User search query.

    Returns:
        Calculated relevance score rounded to 4 decimal places.
    """
    if not query or not query.strip():
        return 0.0

    terms = extract_query_terms(query)
    if not terms:
        return 0.0

    score = 0.0
    norm_query = query.strip().lower()
    norm_title = (title or "").lower()
    norm_body = (body or "").lower()

    # Normalize tags into list of lowercase terms
    tags_list: list[str] = []
    if tags is not None:
        if isinstance(tags, list):
            tags_list = [str(t).strip().lower() for t in tags if str(t).strip()]
        elif isinstance(tags, str):
            t_str = tags.strip()
            if t_str.startswith("[") and t_str.endswith("]"):
                try:
                    parsed = json.loads(t_str)
                    if isinstance(parsed, list):
                        tags_list = [
                            str(t).strip().lower() for t in parsed if str(t).strip()
                        ]
                except Exception:
                    tags_list = [
                        t.strip().lower() for t in t_str.split(",") if t.strip()
                    ]
            else:
                tags_list = [t.strip().lower() for t in t_str.split(",") if t.strip()]

    # 1. Exact phrase match in Title
    if norm_query in norm_title:
        score += 10.0

    # 2. Term matches in Title
    for term in terms:
        if term in norm_title:
            score += 5.0
            # Extra bonus if title begins with term
            if norm_title.startswith(term):
                score += 2.0

    # 3. Term matches in Tags
    for term in terms:
        for tag in tags_list:
            if term == tag:
                score += 3.0
            elif term in tag:
                score += 1.5

    # 4. Term matches in Body
    for term in terms:
        matches = len(re.findall(re.escape(term), norm_body))
        if matches > 0:
            score += 1.0 + min(matches * 0.2, 3.0)

    return round(score, 4)


__all__ = [
    "calculate_lexical_score",
    "extract_query_terms",
    "generate_highlighted_snippet",
    "strip_markdown",
]
