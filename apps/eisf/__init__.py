"""
eISF service package.
Exposes the core adapter mapping and deduplication functions.
"""

from apps.eisf.adapter import (
    DocumentClassification,
    classify_incoming_document,
    derive_correlation_key,
    map_eisf_to_etmf,
    map_etmf_to_eisf,
)

__all__ = [
    "DocumentClassification",
    "map_eisf_to_etmf",
    "map_etmf_to_eisf",
    "derive_correlation_key",
    "classify_incoming_document",
]
