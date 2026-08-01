# Medical Coding Parsers package.
from .matcher import CodingCache, coding_cache, match_verbatim_term, normalize_term
from .service import (
    get_coding_assignment,
    list_coding_assignments,
    map_assignment_to_response,
    process_coding_action,
    search_dictionary,
    trigger_impact_analysis,
)

__all__ = [
    "CodingCache",
    "coding_cache",
    "get_coding_assignment",
    "list_coding_assignments",
    "map_assignment_to_response",
    "match_verbatim_term",
    "normalize_term",
    "process_coding_action",
    "search_dictionary",
    "trigger_impact_analysis",
]
