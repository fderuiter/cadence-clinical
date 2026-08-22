"""
Pure domain route pattern matching and specificity resolution engine.

Supports exact matches, parameterized routes (:param), prefix wildcards (*),
and persona fallback scoping with hierarchical specificity tie-breaking.

Requirements: PRD-SYS-KH-001, ADR-2188
"""

from __future__ import annotations

import re
from datetime import datetime
from enum import IntEnum
from typing import Protocol


class PatternSpecificity(IntEnum):
    """
    Categorical specificity hierarchy for route pattern matching.

    Higher numeric value indicates higher pattern specificity.
    """

    GLOBAL_WILDCARD = 0  # e.g. "/*", "*"
    PREFIX_WILDCARD = 1  # e.g. "/ecrf/*", "/mdr/*"
    PARAMETERIZED_WILDCARD = 2  # e.g. "/mdr/:studyId/*"
    PARAMETERIZED = 3  # e.g. "/mdr/:studyId/designer"
    EXACT = 4  # e.g. "/ecrf/subjects", "/dashboard"


class ContextualHelpMappingProtocol(Protocol):
    """Protocol defining the structural attributes required for contextual help matching."""

    id: str
    route_pattern: str
    persona: str | None
    article_id: str
    section_anchor: str | None
    priority: int
    is_active: bool
    created_at: datetime | None


def normalize_route(route: str) -> str:
    """
    Normalizes a URL path route for deterministic matching.

    Ensures leading slash and strips trailing slashes (except root '/').

    Args:
        route: Raw route string.

    Returns:
        Canonical route string (e.g. '/ecrf/subjects').
    """
    if not route:
        return "/"

    r = route.strip()
    if not r.startswith("/"):
        r = "/" + r
    if len(r) > 1 and r.endswith("/"):
        r = r.rstrip("/")
    return r


def classify_pattern(pattern: str) -> PatternSpecificity:
    """
    Classifies a route pattern into its specificity tier.

    Args:
        pattern: Canonical route pattern string.

    Returns:
        PatternSpecificity enum member.
    """
    p = normalize_route(pattern)
    has_param = ":" in p
    has_wildcard = "*" in p

    if p in ("/*", "*", "/"):
        if has_wildcard:
            return PatternSpecificity.GLOBAL_WILDCARD
        return PatternSpecificity.EXACT

    if has_param and has_wildcard:
        return PatternSpecificity.PARAMETERIZED_WILDCARD
    if has_param:
        return PatternSpecificity.PARAMETERIZED
    if has_wildcard:
        return PatternSpecificity.PREFIX_WILDCARD
    return PatternSpecificity.EXACT


def compile_route_pattern(
    pattern: str,
) -> tuple[re.Pattern[str], PatternSpecificity, int]:
    """
    Compiles a route pattern into a regular expression and calculates its specificity.

    Supported syntax:
    - Literal segments: e.g. `/ecrf`, `/designer`
    - Named parameters: `:studyId` (matches single segment `[^/]+`)
    - Suffix wildcard: `*` at end of path (matches sub-paths `(?:/.*)?`)
    - Universal wildcard: `/*` or `*` (matches any path)

    Args:
        pattern: Route pattern string.

    Returns:
        Tuple of (compiled regex, PatternSpecificity, normalized pattern length).
    """
    norm_pattern = normalize_route(pattern)
    specificity = classify_pattern(norm_pattern)
    pattern_len = len(norm_pattern)

    if norm_pattern in ("/*", "*"):
        return re.compile(r"^/.*$"), specificity, pattern_len

    if norm_pattern == "/":
        return re.compile(r"^/$"), specificity, pattern_len

    segments = norm_pattern.strip("/").split("/")
    regex_parts: list[str] = []

    for i, seg in enumerate(segments):
        if seg == "*":
            if i == len(segments) - 1:
                # Trailing wildcard matches either slash+subpath or nothing if base matches
                regex_parts.append(r"(?:/.*)?")
            else:
                regex_parts.append(r"/.*")
        elif seg.startswith(":"):
            # Single parameter segment
            regex_parts.append(r"/[^/]+")
        else:
            regex_parts.append(f"/{re.escape(seg)}")

    raw_regex = "".join(regex_parts)
    full_regex = f"^{raw_regex}$"

    return re.compile(full_regex), specificity, pattern_len


def matches_route(pattern: str, route: str) -> bool:
    """
    Evaluates whether a given URL route matches a route pattern.

    Args:
        pattern: Route pattern string (e.g. '/ecrf/*', '/mdr/:studyId/*').
        route: Actual client route string (e.g. '/ecrf/subjects', '/mdr/ST-001/designer').

    Returns:
        True if route matches pattern, False otherwise.
    """
    norm_route = normalize_route(route)
    compiled_re, _, _ = compile_route_pattern(pattern)
    return bool(compiled_re.match(norm_route))


def matches_persona(
    mapping_persona: str | None, requested_persona: str | None
) -> tuple[bool, int]:
    """
    Evaluates persona matching and assigns a persona specificity score.

    - If mapping_persona is None or empty: matches any persona as universal fallback (score 0).
    - If mapping_persona is specified and matches requested_persona: exact match (score 1).
    - Otherwise: mismatch (returns False, 0).

    Args:
        mapping_persona: Persona role on the mapping, or None for universal.
        requested_persona: Active persona role from client request.

    Returns:
        Tuple of (is_match: bool, persona_score: int).
    """
    if not mapping_persona or not mapping_persona.strip():
        return True, 0

    if not requested_persona or not requested_persona.strip():
        return False, 0

    from packages.security.rbac import ROLE_EXPANSIONS, normalize_role

    norm_requested = normalize_role(requested_persona.strip())
    raw_requested = requested_persona.strip().lower()

    allowed_personas = [
        p.strip().lower() for p in mapping_persona.split(",") if p.strip()
    ]

    for p in allowed_personas:
        norm_p = normalize_role(p)
        if raw_requested in (p, norm_p) or norm_requested == norm_p:
            return True, 1
        if p in ROLE_EXPANSIONS and (
            raw_requested in ROLE_EXPANSIONS[p] or norm_requested in ROLE_EXPANSIONS[p]
        ):
            return True, 1

    return False, 0


def compute_match_score(
    mapping: ContextualHelpMappingProtocol,
    route: str,
    requested_persona: str | None = None,
) -> tuple[int, int, int, int, float] | None:
    """
    Calculates the hierarchical specificity sort key for a mapping against a route and persona.

    Resolution order (lower tuple = higher rank / best match):
    1. priority ASC (lowest integer = highest administrator priority)
    2. -persona_score (1 for exact persona match, 0 for universal fallback)
    3. -pattern_specificity (4=Exact, 3=Parameterized, 2=ParamWildcard, 1=PrefixWildcard, 0=Global)
    4. -pattern_length (LENGTH(route_pattern) DESC tie-breaker)
    5. -created_at_timestamp (recency tie-breaker)

    Args:
        mapping: The contextual help mapping entity.
        route: The client route path.
        requested_persona: The user's active persona role.

    Returns:
        Sort key tuple if matched, None if route or persona does not match.
    """
    if not mapping.is_active:
        return None

    compiled_re, specificity, pattern_len = compile_route_pattern(mapping.route_pattern)
    norm_route = normalize_route(route)

    if not compiled_re.match(norm_route):
        return None

    is_persona_matched, persona_score = matches_persona(
        mapping.persona, requested_persona
    )
    if not is_persona_matched:
        return None

    created_ts = (
        mapping.created_at.timestamp() if mapping.created_at is not None else 0.0
    )

    return (
        mapping.priority,
        -persona_score,
        -int(specificity),
        -pattern_len,
        -created_ts,
    )


def rank_matching_mappings[T: ContextualHelpMappingProtocol](
    mappings: list[T],
    route: str,
    persona: str | None = None,
) -> list[T]:
    """
    Filters and ranks a list of candidate mappings for a route and persona in descending priority.

    Args:
        mappings: List of ContextualHelpMapping instances.
        route: Incoming URL route.
        persona: Incoming active persona role.

    Returns:
        Ranked list of matching mappings, best match first.
    """
    scored: list[tuple[tuple[int, int, int, int, float], T]] = []

    for m in mappings:
        score = compute_match_score(m, route, persona)
        if score is not None:
            scored.append((score, m))

    scored.sort(key=lambda item: item[0])
    return [item[1] for item in scored]


__all__ = [
    "ContextualHelpMappingProtocol",
    "PatternSpecificity",
    "classify_pattern",
    "compile_route_pattern",
    "compute_match_score",
    "matches_persona",
    "matches_route",
    "normalize_route",
    "rank_matching_mappings",
]
