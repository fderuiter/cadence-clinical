# Medical Coding Parsers package.
from .matcher import CodingCache, coding_cache, match_verbatim_term, normalize_term
from .service import (
    get_coding_assignment,
    list_coding_assignments,
    process_coding_action,
    search_dictionary,
    trigger_impact_analysis,
)

__all__ = [
    "normalize_term",
    "CodingCache",
    "coding_cache",
    "match_verbatim_term",
    "search_dictionary",
    "list_coding_assignments",
    "get_coding_assignment",
    "process_coding_action",
    "trigger_impact_analysis",
]
