# Medical Coding Parsers package.
from .matcher import (
    CodingCache,
    calculate_cosine_similarity,
    coding_cache,
    generate_local_term_embedding,
    match_semantic_verbatim_term,
    match_verbatim_term,
    normalize_term,
)
from .service import (
    batch_assign_codes,
    get_coding_assignment,
    list_coding_assignments,
    process_coding_action,
    raise_coding_query,
    search_dictionary,
    suggest_semantic_coding,
    trigger_impact_analysis,
)

__all__ = [
    "CodingCache",
    "batch_assign_codes",
    "calculate_cosine_similarity",
    "coding_cache",
    "generate_local_term_embedding",
    "get_coding_assignment",
    "list_coding_assignments",
    "match_semantic_verbatim_term",
    "match_verbatim_term",
    "normalize_term",
    "process_coding_action",
    "raise_coding_query",
    "search_dictionary",
    "suggest_semantic_coding",
    "trigger_impact_analysis",
]
