"""Version-aware fuzzy matcher and TTL lookup cache for clinical coding.

This module implements the core text preprocessing and normalization, similarity
scoring calculations, dictionary-specific lookup, and caching for MedDRA and WHODrug.
"""

import collections
import math
import os
import re
import threading
import time
from typing import Any, Dict, List, Optional, Tuple

from rapidfuzz.distance import Levenshtein
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import (
    MedDRAHierarchy,
    MedDRATerm,
    WHODrugATC,
    WHODrugDrugATC,
    WHODrugDrugIngredient,
    WHODrugIngredient,
    WHODrugRecord,
)

# Clinical Stop Phrases (multi-word, case-insensitive)
STOP_PHRASES = [
    "onset of",
    "history of",
    "episode of",
    "episodes of",
    "due to",
    "secondary to",
    "associated with",
    "associated",
]

# Clinical and grammatical Stop Words
STOP_WORDS = {
    "mild",
    "moderate",
    "severe",
    "acute",
    "chronic",
    "recurrent",
    "recurring",
    "history",
    "onset",
    "episode",
    "episodes",
    "with",
    "without",
    "of",
    "and",
    "the",
    "a",
    "an",
    "to",
    "in",
    "on",
    "for",
    "at",
    "by",
    "from",
}


def stem_word(word: str) -> str:
    """Documented clinical word stemming/normalization.

    Rules applied:
    1. If a word is 3 characters or less, do not stem.
    2. Map plurals or inflammation markers specifically:
       - 'itises' -> 'itis' (retains diagnostic inflammation root)
       - 'itis' is kept intact.
       - 'ies' (not ending in 'eies') -> 'y' (e.g., 'allergies' -> 'allergy')
       - 'es' (not ending in vocal prefixes) -> strip 'es' (e.g., 'headaches' -> 'headache')
       - 's' (excluding standard endings like 'us', 'is', 'as', 'os', 'ss') -> strip 's'
    3. Strip common verbal / adjective suffixes if remaining is at least 3 characters:
       - 'ing' -> strip (e.g., 'vomiting' -> 'vomit')
       - 'ed' -> strip (e.g., 'infected' -> 'infect')
       - 'ly' -> strip (e.g., 'severely' -> 'severe')
       - 'al' (excluding 'eal') -> strip (e.g., 'clinical' -> 'clinic')
    """
    if not word:
        return ""
    if len(word) <= 3:
        return word

    # Plural and diagnostic endings
    if word.endswith("itises"):
        return word[:-6] + "itis"
    if word.endswith("itis"):
        return word
    if word.endswith("ies") and not word.endswith("eies"):
        return word[:-3] + "y"
    if word.endswith("es") and not any(
        word.endswith(suffix) for suffix in ["aes", "ees", "oes"]
    ):
        return word[:-1]
    if word.endswith("s") and not any(
        word.endswith(suffix) for suffix in ["ss", "us", "is", "as", "os"]
    ):
        return word[:-1]

    # Verbal / adjective suffix stripping
    if word.endswith("ing"):
        stem = word[:-3]
        return stem if len(stem) >= 3 else word
    if word.endswith("ed"):
        stem = word[:-2]
        return stem if len(stem) >= 3 else word
    if word.endswith("ly"):
        stem = word[:-2]
        return stem if len(stem) >= 3 else word
    if word.endswith("al") and not word.endswith("eal"):
        stem = word[:-2]
        return stem if len(stem) >= 3 else word

    return word


def normalize_term(term: str) -> str:
    """Normalizes a verbatim clinical term according to SDLC/04 standards.

    Performs case folding, clinical stop-phrase/word removal, punctuation stripping,
    and documented suffix-stripping stemming.
    """
    if not term:
        return ""

    # 1. Case-folding
    text = term.lower()

    # 2. Clinical multi-word stop phrase removal
    for phrase in STOP_PHRASES:
        text = re.sub(rf"\b{phrase}\b", " ", text)

    # 3. Punctuation removal: replace non-alphanumeric characters with space
    text = re.sub(r"[^a-z0-9\s]", " ", text)

    # 4. Tokenization and stop-word elimination
    raw_tokens = text.split()
    filtered_tokens = [tok for tok in raw_tokens if tok not in STOP_WORDS]

    # 5. Documented word-level stemming
    stemmed_tokens = [stem_word(tok) for tok in filtered_tokens]

    # 6. Recombine tokens into a normalized string
    return " ".join(stemmed_tokens)


def token_cosine_similarity(v_normalized: str, d_normalized: str) -> float:
    """Calculates the Token Cosine Similarity (S_Cos) between two normalized terms."""
    if not v_normalized and not d_normalized:
        return 1.0
    if not v_normalized or not d_normalized:
        return 0.0

    v_tokens = v_normalized.split()
    d_tokens = d_normalized.split()

    if not v_tokens or not d_tokens:
        return 0.0

    v_counts = collections.Counter(v_tokens)
    d_counts = collections.Counter(d_tokens)

    # Dot product
    dot_product = sum(v_counts[t] * d_counts[t] for t in v_counts if t in d_counts)

    # Magnitudes
    norm_v = math.sqrt(sum(count**2 for count in v_counts.values()))
    norm_d = math.sqrt(sum(count**2 for count in d_counts.values()))

    if norm_v == 0.0 or norm_d == 0.0:
        return 0.0

    return dot_product / (norm_v * norm_d)


def calculate_combined_score(v_normalized: str, d_normalized: str) -> float:
    """Combines normalized Levenshtein similarity and Token Cosine Similarity.

    Formula: CS = 0.4 * S_Lev + 0.6 * S_Cos
    """
    # 1. Levenshtein Similarity
    if not v_normalized and not d_normalized:
        s_lev = 1.0
    elif not v_normalized or not d_normalized:
        s_lev = 0.0
    else:
        dist = Levenshtein.distance(v_normalized, d_normalized)
        max_len = max(len(v_normalized), len(d_normalized))
        s_lev = 1.0 - (dist / max_len) if max_len > 0 else 0.0

    # 2. Token Cosine Similarity
    s_cos = token_cosine_similarity(v_normalized, d_normalized)

    # 3. Combined Score
    return 0.4 * s_lev + 0.6 * s_cos


class CodingCache:
    """Thread-safe in-memory cache for version-aware medical coding lookups."""

    def __init__(self, max_size: int = 1000, ttl: Optional[float] = None) -> None:
        self.max_size = max_size
        # Map key (dict_type, version, normalized_term, target_level) -> (data, store_time)
        self._cache: Dict[Tuple[str, str, str, Optional[str]], Tuple[Any, float]] = {}
        self._lock = threading.Lock()

        if ttl is not None:
            self.ttl = float(ttl)
        else:
            # Check environment variables: CODING_CACHE_TTL or CACHE_TTL or fallback to 3600.0
            env_ttl = os.getenv("CODING_CACHE_TTL") or os.getenv("CACHE_TTL")
            if env_ttl is not None:
                try:
                    self.ttl = float(env_ttl)
                except ValueError:
                    self.ttl = 3600.0
            else:
                self.ttl = 3600.0

    def get(
        self, key: Tuple[str, str, str, Optional[str]]
    ) -> Tuple[Optional[Any], Optional[Any]]:
        """Retrieves an entry. Returns (hit_data, expired_data)."""
        now = time.time()
        with self._lock:
            if key in self._cache:
                data, timestamp = self._cache[key]
                if now - timestamp < self.ttl:
                    return data, None
                return None, data
        return None, None

    def set(self, key: Tuple[str, str, str, Optional[str]], data: Any) -> None:
        """Stores an entry in the cache, enforcing max_size eviction."""
        with self._lock:
            store_time = time.time()
            if key in self._cache:
                self._cache[key] = (data, store_time)
            else:
                if len(self._cache) >= self.max_size:
                    # Evict first element (FIFO)
                    self._cache.pop(next(iter(self._cache)))
                self._cache[key] = (data, store_time)

    def clear(self) -> None:
        """Clears all cached lookups."""
        with self._lock:
            self._cache.clear()


# Global cache instance
coding_cache = CodingCache()


async def _get_meddra_hierarchy(
    session: AsyncSession, term: MedDRATerm, version: str
) -> List[Dict[str, Any]]:
    """Retrieves full hierarchy paths for a MedDRA term."""
    stmt = select(MedDRAHierarchy).where(MedDRAHierarchy.dictionary_version == version)
    if term.level == "LLT":
        stmt = stmt.where(MedDRAHierarchy.llt_code == term.code)
    elif term.level == "PT":
        stmt = stmt.where(MedDRAHierarchy.pt_code == term.code)
    else:
        stmt = stmt.where(
            (MedDRAHierarchy.hlt_code == term.code)
            | (MedDRAHierarchy.hlgt_code == term.code)
            | (MedDRAHierarchy.soc_code == term.code)
        )

    res = await session.execute(stmt)
    hierarchies = res.scalars().all()

    if not hierarchies:
        return []

    unique_codes = set()
    for h in hierarchies:
        unique_codes.add(h.pt_code)
        unique_codes.add(h.hlt_code)
        unique_codes.add(h.hlgt_code)
        unique_codes.add(h.soc_code)
        if h.llt_code and h.llt_code != "NONE":
            unique_codes.add(h.llt_code)

    term_map = {}
    if unique_codes:
        term_stmt = select(MedDRATerm).where(
            MedDRATerm.dictionary_version == version,
            MedDRATerm.code.in_(list(unique_codes)),
        )
        term_res = await session.execute(term_stmt)
        for t in term_res.scalars().all():
            term_map[(t.code, t.level)] = t.term_name

    results = []
    for h in hierarchies:
        results.append(
            {
                "llt_code": h.llt_code,
                "llt_name": term_map.get(
                    (h.llt_code, "LLT"),
                    term.term_name if h.llt_code == term.code else "",
                ),
                "pt_code": h.pt_code,
                "pt_name": term_map.get((h.pt_code, "PT"), ""),
                "hlt_code": h.hlt_code,
                "hlt_name": term_map.get((h.hlt_code, "HLT"), ""),
                "hlgt_code": h.hlgt_code,
                "hlgt_name": term_map.get((h.hlgt_code, "HLGT"), ""),
                "soc_code": h.soc_code,
                "soc_name": term_map.get((h.soc_code, "SOC"), ""),
                "primary_soc_flag": h.primary_soc_flag,
            }
        )
    return results


async def _match_meddra(
    session: AsyncSession,
    verbatim: str,
    norm_verbatim: str,
    version: str,
    target_level: Optional[str] = None,
) -> Dict[str, Any]:
    """Matches a verbatim term against MedDRA dictionary candidates."""
    stmt = select(MedDRATerm).where(MedDRATerm.dictionary_version == version)
    if target_level:
        stmt = stmt.where(MedDRATerm.level == target_level.upper())
    res = await session.execute(stmt)
    terms = res.scalars().all()

    if not terms:
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }

    scored_candidates = []
    for t in terms:
        norm_candidate = normalize_term(t.term_name)
        score = calculate_combined_score(norm_verbatim, norm_candidate)
        scored_candidates.append((score, t))

    # Sort deterministically
    scored_candidates.sort(key=lambda x: (-x[0], x[1].term_name, x[1].code))

    highest_score, highest_term = scored_candidates[0]

    if highest_score >= 0.85:
        match_hierarchy = await _get_meddra_hierarchy(session, highest_term, version)
        match_data = {
            "code": highest_term.code,
            "term_name": highest_term.term_name,
            "level": highest_term.level,
            "score": highest_score,
            "hierarchies": match_hierarchy,
        }
        return {
            "status": "AUTO-CODED",
            "match": match_data,
            "suggestions": [],
        }

    elif highest_score >= 0.60:
        suggestions_list = []
        for score, t in scored_candidates:
            if 0.60 <= score < 0.85:
                hierarchy = await _get_meddra_hierarchy(session, t, version)
                suggestions_list.append(
                    {
                        "code": t.code,
                        "term_name": t.term_name,
                        "level": t.level,
                        "score": score,
                        "hierarchies": hierarchy,
                    }
                )
                if len(suggestions_list) == 3:
                    break
        return {
            "status": "SUGGESTIONS",
            "match": None,
            "suggestions": suggestions_list,
        }

    else:
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }


async def _get_whodrug_context(
    session: AsyncSession, record: WHODrugRecord, version: str
) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    """Retrieves ATC context and ingredients for a WHODrug record."""
    atc_links_stmt = select(WHODrugDrugATC).where(
        WHODrugDrugATC.dictionary_version == version,
        WHODrugDrugATC.drug_code == record.drug_code,
    )
    atc_links_res = await session.execute(atc_links_stmt)
    atc_codes = [link.atc_code for link in atc_links_res.scalars().all()]

    atc_details = []
    if atc_codes:
        atc_stmt = select(WHODrugATC).where(
            WHODrugATC.dictionary_version == version,
            WHODrugATC.atc_code.in_(atc_codes),
        )
        atc_res = await session.execute(atc_stmt)
        atc_details = [
            {"atc_code": a.atc_code, "description": a.description}
            for a in atc_res.scalars().all()
        ]

    ing_links_stmt = select(WHODrugDrugIngredient).where(
        WHODrugDrugIngredient.dictionary_version == version,
        WHODrugDrugIngredient.drug_code == record.drug_code,
    )
    ing_links_res = await session.execute(ing_links_stmt)
    ing_codes = [link.ingredient_code for link in ing_links_res.scalars().all()]

    ing_details = []
    if ing_codes:
        ing_stmt = select(WHODrugIngredient).where(
            WHODrugIngredient.dictionary_version == version,
            WHODrugIngredient.ingredient_code.in_(ing_codes),
        )
        ing_res = await session.execute(ing_stmt)
        ing_details = [
            {"ingredient_code": i.ingredient_code, "ingredient_name": i.ingredient_name}
            for i in ing_res.scalars().all()
        ]

    return atc_details, ing_details


async def _match_whodrug(
    session: AsyncSession,
    verbatim: str,
    norm_verbatim: str,
    version: str,
) -> Dict[str, Any]:
    """Matches a verbatim term against WHODrug record candidates."""
    stmt = select(WHODrugRecord).where(WHODrugRecord.dictionary_version == version)
    res = await session.execute(stmt)
    records = res.scalars().all()

    if not records:
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }

    scored_candidates = []
    for r in records:
        norm_pref = normalize_term(r.preferred_name)
        score_pref = calculate_combined_score(norm_verbatim, norm_pref)

        norm_drug = normalize_term(r.drug_name) if r.drug_name else ""
        score_drug = (
            calculate_combined_score(norm_verbatim, norm_drug) if norm_drug else 0.0
        )

        score = max(score_pref, score_drug)
        scored_candidates.append((score, r))

    # Sort deterministically
    scored_candidates.sort(key=lambda x: (-x[0], x[1].preferred_name, x[1].drug_code))

    highest_score, highest_record = scored_candidates[0]

    if highest_score >= 0.85:
        atc_context, ingredients = await _get_whodrug_context(
            session, highest_record, version
        )
        match_data = {
            "drug_code": highest_record.drug_code,
            "preferred_name": highest_record.preferred_name,
            "drug_name": highest_record.drug_name,
            "score": highest_score,
            "atc_context": atc_context,
            "ingredients": ingredients,
        }
        return {
            "status": "AUTO-CODED",
            "match": match_data,
            "suggestions": [],
        }

    elif highest_score >= 0.60:
        suggestions_list = []
        for score, r in scored_candidates:
            if 0.60 <= score < 0.85:
                atc_context, ingredients = await _get_whodrug_context(
                    session, r, version
                )
                suggestions_list.append(
                    {
                        "drug_code": r.drug_code,
                        "preferred_name": r.preferred_name,
                        "drug_name": r.drug_name,
                        "score": score,
                        "atc_context": atc_context,
                        "ingredients": ingredients,
                    }
                )
                if len(suggestions_list) == 3:
                    break
        return {
            "status": "SUGGESTIONS",
            "match": None,
            "suggestions": suggestions_list,
        }

    else:
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }


async def match_verbatim_term(
    session: AsyncSession,
    verbatim: str,
    dictionary_type: str,
    version: str,
    target_level: Optional[str] = None,
) -> Dict[str, Any]:
    """Exposes version-aware, cached, and deterministic clinical terminology matching."""
    if not verbatim:
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }

    dict_upper = dictionary_type.upper()
    if dict_upper not in {"MEDDRA", "WHODRUG"}:
        raise ValueError(f"Unsupported dictionary type: {dictionary_type}")

    norm_verbatim = normalize_term(verbatim)
    cache_key = (dict_upper, version, norm_verbatim, target_level)

    hit_data, expired_data = None, None
    try:
        hit_data, expired_data = coding_cache.get(cache_key)
        if hit_data is not None:
            return hit_data
    except Exception:
        pass

    try:
        if dict_upper == "MEDDRA":
            result = await _match_meddra(
                session, verbatim, norm_verbatim, version, target_level
            )
        else:
            result = await _match_whodrug(session, verbatim, norm_verbatim, version)

        try:
            coding_cache.set(cache_key, result)
        except Exception:
            pass

        return result

    except Exception as e:
        if expired_data is not None:
            return expired_data
        raise e
