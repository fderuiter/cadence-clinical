# Medical Coding Parsers package.
from .matcher import CodingCache, coding_cache, match_verbatim_term, normalize_term
from .service import (
    search_dictionary,
    list_coding_assignments,
    get_coding_assignment,
    process_coding_action,
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
