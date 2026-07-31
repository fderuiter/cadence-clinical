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
from apps.eisf.database import EISFDatabaseManager, db_manager

__all__ = [
    "DocumentClassification",
    "map_eisf_to_etmf",
    "map_etmf_to_eisf",
    "derive_correlation_key",
    "classify_incoming_document",
    "EISFDatabaseManager",
    "db_manager",
]
