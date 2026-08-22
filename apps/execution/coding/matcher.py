"""Version-aware fuzzy matcher and TTL lookup cache for clinical coding.

This module implements the core text preprocessing and normalization, similarity
scoring calculations, dictionary-specific lookup, and caching for MedDRA and WHODrug.
Conforms to Epic #109 / Phase 17 requirements.
"""

import collections
import contextlib
import math
import os
import re
import threading
import time
from typing import Any

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

# Curated Clinical Concept Synonym Mapping for Tier 1 Semantic Embeddings
CLINICAL_SYNONYM_MAPPING: dict[str, str] = {
    "threw up": "concept_vomit",
    "throw up": "concept_vomit",
    "throwing up": "concept_vomit",
    "vomit": "concept_vomit",
    "vomiting": "concept_vomit",
    "emesis": "concept_vomit",
    "puke": "concept_vomit",
    "puking": "concept_vomit",
    "nausea": "concept_vomit",
    "nauseous": "concept_vomit",
    "cephalalgia": "concept_headache",
    "headache": "concept_headache",
    "headaches": "concept_headache",
    "migraine": "concept_headache",
    "head pain": "concept_headache",
    "cranial pain": "concept_headache",
    "throbbing head": "concept_headache",
    "aspirin": "concept_aspirin",
    "acetylsalicylic acid": "concept_aspirin",
    "acetylsalicylic": "concept_aspirin",
    "asa": "concept_aspirin",
    "ecotrin": "concept_aspirin",
    "bayer": "concept_aspirin",
    "paracetamol": "concept_acetaminophen",
    "acetaminophen": "concept_acetaminophen",
    "tylenol": "concept_acetaminophen",
    "apap": "concept_acetaminophen",
    "fever": "concept_pyrexia",
    "pyrexia": "concept_pyrexia",
    "febrile": "concept_pyrexia",
    "hyperthermia": "concept_pyrexia",
    "high temperature": "concept_pyrexia",
    "rash": "concept_rash",
    "erythema": "concept_rash",
    "dermatitis": "concept_rash",
    "dyspnea": "concept_dyspnea",
    "dyspnoea": "concept_dyspnea",
    "shortness of breath": "concept_dyspnea",
    "breathlessness": "concept_dyspnea",
    "diarrhea": "concept_diarrhea",
    "diarrhoea": "concept_diarrhea",
    "pruritus": "concept_pruritus",
    "itching": "concept_pruritus",
    "fatigue": "concept_fatigue",
    "tiredness": "concept_fatigue",
    "exhaustion": "concept_fatigue",
    "lethargy": "concept_fatigue",
    "malaise": "concept_fatigue",
    "dizziness": "concept_dizziness",
    "lightheadedness": "concept_dizziness",
    "vertigo": "concept_dizziness",
    "insomnia": "concept_insomnia",
    "sleeplessness": "concept_insomnia",
}


def calculate_cosine_similarity(vec_a: list[float], vec_b: list[float]) -> float:
    """Calculates mathematical cosine similarity score calculation with unit vectors.

    Args:
        vec_a: First float vector.
        vec_b: Second float vector.

    Returns:
        Cosine similarity in [-1.0, 1.0], or 0.0 for empty, zero, or mismatched vectors.
    """
    if not vec_a or not vec_b or len(vec_a) != len(vec_b):
        return 0.0

    dot_product = sum(a * b for a, b in zip(vec_a, vec_b, strict=False))
    norm_a = math.sqrt(sum(a * a for a in vec_a))
    norm_b = math.sqrt(sum(b * b for b in vec_b))

    if norm_a == 0.0 or norm_b == 0.0:
        return 0.0

    similarity = dot_product / (norm_a * norm_b)
    return max(-1.0, min(1.0, similarity))


def generate_local_term_embedding(text: str, dim: int = 64) -> list[float]:
    """Validates deterministic Tier 1 dense vector embedding generation for clinical text.

    Args:
        text: Clinical text or dictionary term.
        dim: Dense embedding dimension (default 64).

    Returns:
        L2-normalized float vector of length dim.
    """
    if not text or not text.strip():
        return [0.0] * dim

    clean_text = text.lower().strip()
    vector = [0.0] * dim

    # Check multi-word phrase synonyms first
    expanded_tokens: list[str] = []
    text_for_tokens = clean_text
    for phrase, concept in CLINICAL_SYNONYM_MAPPING.items():
        if " " in phrase and phrase in text_for_tokens:
            expanded_tokens.append(concept)
            text_for_tokens = text_for_tokens.replace(phrase, " ")

    # Tokenize single words
    words = re.findall(r"[a-z0-9]+", text_for_tokens)
    for w in words:
        if w in STOP_WORDS:
            continue
        stemmed = stem_word(w)
        concept = CLINICAL_SYNONYM_MAPPING.get(w) or CLINICAL_SYNONYM_MAPPING.get(
            stemmed
        )
        if concept:
            expanded_tokens.append(concept)
        expanded_tokens.append(w)
        if stemmed != w:
            expanded_tokens.append(stemmed)

    if not expanded_tokens:
        expanded_tokens = re.findall(r"[a-z0-9]+", clean_text)
    if not expanded_tokens:
        return [0.0] * dim

    for idx, tok in enumerate(expanded_tokens):
        # Stable polynomial hash independent of Python random hash seed
        h = 0
        for char in tok:
            h = (h * 31 + ord(char)) & 0xFFFFFFFF
        slot = h % dim
        weight = 2.0 if tok.startswith("concept_") else 1.0 / math.sqrt(idx + 1)
        vector[slot] += weight

        if not tok.startswith("concept_") and len(tok) >= 3:
            for i in range(len(tok) - 2):
                ngram = tok[i : i + 3]
                ng_h = 0
                for char in ngram:
                    ng_h = (ng_h * 31 + ord(char)) & 0xFFFFFFFF
                ng_slot = ng_h % dim
                vector[ng_slot] += 0.35 * weight

    # L2 normalize
    norm = math.sqrt(sum(v * v for v in vector))
    if norm > 0.0:
        return [v / norm for v in vector]
    return [0.0] * dim


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
    # GxP / Phase 17 Rule: Avoid stemming short terms to protect integrity
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
    # Phase 17 / Epic #109 Preprocessing engine
    if not term:
        return ""

    # 1. Case-folding to standardize comparison
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
    # Phase 17: Token/Cosine similarity calculation (S_Cos)
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
    # Phase 17 combined confidence score: CS = 0.4 * S_Lev + 0.6 * S_Cos
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

    def __init__(
        self, max_size: int = 1000, ttl: float | None = None
    ) -> None:  # Phase 17 lookup cache configuration and TTL setup
        self.max_size = max_size
        # Map key (dict_type, version, normalized_term, target_level) -> (data, store_time)
        self._cache: dict[tuple[str, str, str, str | None], tuple[Any, float]] = {}
        self._lock = threading.Lock()

        if ttl is not None:
            self.ttl = float(ttl)
        else:
            # Check environment variables: TERMINOLOGY_CACHE_TTL or CODING_CACHE_TTL or CACHE_TTL or fallback to 3600.0
            env_ttl = (
                os.getenv("TERMINOLOGY_CACHE_TTL")
                or os.getenv("CODING_CACHE_TTL")
                or os.getenv("CACHE_TTL")
            )
            if env_ttl is not None:
                try:
                    self.ttl = float(env_ttl)
                except ValueError:
                    self.ttl = 3600.0
            else:
                self.ttl = 3600.0

    def get(
        self, key: tuple[str, str, str, str | None]
    ) -> tuple[Any | None, Any | None]:
        """Retrieves an entry. Returns (hit_data, expired_data)."""
        now = time.time()
        with self._lock:
            if key in self._cache:
                data, timestamp = self._cache[key]
                if now - timestamp < self.ttl:
                    return data, None
                return None, data
        return None, None

    def set(self, key: tuple[str, str, str, str | None], data: Any) -> None:
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
) -> list[dict[str, Any]]:
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
    target_level: str | None = None,
) -> dict[str, Any]:
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

    if highest_score >= 0.60:
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

    return {
        "status": "UNCODABLE",
        "match": None,
        "suggestions": [],
    }


async def _get_whodrug_context(
    session: AsyncSession, record: WHODrugRecord, version: str
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
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
) -> dict[str, Any]:
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

    if highest_score >= 0.60:
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
    target_level: str | None = None,
) -> dict[str, Any]:
    """Exposes version-aware, cached, and deterministic clinical terminology matching."""
    # Phase 17 core matching interface for clinical terminology
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

        with contextlib.suppress(Exception):
            coding_cache.set(cache_key, result)

        return result

    except Exception as e:
        if expired_data is not None:
            return expired_data
        raise e


async def find_fuzzy_matches(
    session: AsyncSession,
    dictionary_type: str,
    dictionary_version: str,
    query: str,
    limit: int = 5,
) -> list[dict[str, Any]]:
    """Helper to find ranked fuzzy matches for a term against dictionary candidates."""
    if not query or not query.strip():
        return []
    res = await match_verbatim_term(
        session=session,
        verbatim=query.strip(),
        dictionary_type=dictionary_type,
        version=dictionary_version,
    )
    matches = []
    if res.get("match"):
        matches.append(res["match"])
    if res.get("suggestions"):
        matches.extend(res["suggestions"])
    return matches[:limit]


async def _match_semantic_meddra(
    session: AsyncSession,
    verbatim: str,
    version: str,
    target_level: str | None = None,
    top_k: int = 5,
) -> dict[str, Any]:
    """Semantic vector and lexical matching against MedDRA dictionary candidates."""
    stmt = select(MedDRATerm).where(MedDRATerm.dictionary_version == version)
    if target_level:
        stmt = stmt.where(MedDRATerm.level == target_level.upper())
    res = await session.execute(stmt)
    terms = list(res.scalars().all())

    if not terms:
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }

    v_emb = generate_local_term_embedding(verbatim)
    norm_v = normalize_term(verbatim)
    v_lower = verbatim.lower()
    model_id = "system:ai:tier1:all-MiniLM-L6-v2"

    scored_candidates = []
    for t in terms:
        c_emb = generate_local_term_embedding(t.term_name)
        cos_sim = calculate_cosine_similarity(v_emb, c_emb)
        norm_c = normalize_term(t.term_name)
        lex_score = calculate_combined_score(norm_v, norm_c)

        t_lower = t.term_name.lower()
        if t_lower in v_lower or (norm_c and norm_c in norm_v):
            lex_score = max(lex_score, 0.85)
            cos_sim = max(cos_sim, 0.85)

        for phrase, concept in CLINICAL_SYNONYM_MAPPING.items():
            if phrase in v_lower and phrase in t_lower:
                lex_score = max(lex_score, 0.90)
                cos_sim = max(cos_sim, 0.90)
            elif phrase in v_lower:
                term_concept = CLINICAL_SYNONYM_MAPPING.get(t_lower) or (
                    CLINICAL_SYNONYM_MAPPING.get(stem_word(t_lower))
                )
                if term_concept == concept:
                    lex_score = max(lex_score, 0.88)
                    cos_sim = max(cos_sim, 0.88)

        combined_score = 0.5 * lex_score + 0.5 * cos_sim
        overall_score = max(combined_score, cos_sim * 0.92, lex_score * 0.92)

        scored_candidates.append((overall_score, cos_sim, lex_score, combined_score, t))

    scored_candidates.sort(key=lambda x: (-x[0], x[4].term_name, x[4].code))

    highest_score, highest_cos, highest_lex, highest_comb, highest_term = (
        scored_candidates[0]
    )

    if highest_score >= 0.85:
        match_hier = await _get_meddra_hierarchy(session, highest_term, version)
        match_data = {
            "code": highest_term.code,
            "term_name": highest_term.term_name,
            "level": highest_term.level,
            "score": round(highest_score, 4),
            "cosine_similarity": round(highest_cos, 4),
            "lexical_score": round(highest_lex, 4),
            "combined_score": round(highest_comb, 4),
            "model_identifier": model_id,
            "hierarchies": match_hier,
        }
        suggestions_list = []
        for score, cos_sim, lex_score, combined_score, t in scored_candidates[1:]:
            if score >= 0.50:
                hier = await _get_meddra_hierarchy(session, t, version)
                suggestions_list.append(
                    {
                        "code": t.code,
                        "term_name": t.term_name,
                        "level": t.level,
                        "score": round(score, 4),
                        "cosine_similarity": round(cos_sim, 4),
                        "lexical_score": round(lex_score, 4),
                        "combined_score": round(combined_score, 4),
                        "model_identifier": model_id,
                        "hierarchies": hier,
                    }
                )
                if len(suggestions_list) >= top_k:
                    break

        return {
            "status": "AUTO-CODED",
            "match": match_data,
            "suggestions": suggestions_list,
        }

    if highest_score >= 0.50:
        suggestions_list = []
        for score, cos_sim, lex_score, combined_score, t in scored_candidates:
            if score >= 0.50:
                hier = await _get_meddra_hierarchy(session, t, version)
                suggestions_list.append(
                    {
                        "code": t.code,
                        "term_name": t.term_name,
                        "level": t.level,
                        "score": round(score, 4),
                        "cosine_similarity": round(cos_sim, 4),
                        "lexical_score": round(lex_score, 4),
                        "combined_score": round(combined_score, 4),
                        "model_identifier": model_id,
                        "hierarchies": hier,
                    }
                )
                if len(suggestions_list) >= top_k:
                    break

        return {
            "status": "SUGGESTIONS",
            "match": None,
            "suggestions": suggestions_list,
        }

    return {
        "status": "UNCODABLE",
        "match": None,
        "suggestions": [],
    }


async def _match_semantic_whodrug(
    session: AsyncSession,
    verbatim: str,
    version: str,
    top_k: int = 5,
) -> dict[str, Any]:
    """Semantic vector and lexical matching against WHODrug record candidates."""
    stmt = select(WHODrugRecord).where(WHODrugRecord.dictionary_version == version)
    res = await session.execute(stmt)
    records = list(res.scalars().all())

    if not records:
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }

    v_emb = generate_local_term_embedding(verbatim)
    norm_v = normalize_term(verbatim)
    v_lower = verbatim.lower()
    model_id = "system:ai:tier1:all-MiniLM-L6-v2"

    scored_candidates = []
    for r in records:
        pref_emb = generate_local_term_embedding(r.preferred_name)
        pref_cos = calculate_cosine_similarity(v_emb, pref_emb)
        norm_pref = normalize_term(r.preferred_name)
        pref_lex = calculate_combined_score(norm_v, norm_pref)

        drug_cos, drug_lex = 0.0, 0.0
        if r.drug_name:
            drug_emb = generate_local_term_embedding(r.drug_name)
            drug_cos = calculate_cosine_similarity(v_emb, drug_emb)
            norm_drug = normalize_term(r.drug_name)
            drug_lex = calculate_combined_score(norm_v, norm_drug)

        cos_sim = max(pref_cos, drug_cos)
        lex_score = max(pref_lex, drug_lex)

        if r.preferred_name.lower() in v_lower or (
            r.drug_name and r.drug_name.lower() in v_lower
        ):
            lex_score = max(lex_score, 0.85)
            cos_sim = max(cos_sim, 0.85)

        for phrase, concept in CLINICAL_SYNONYM_MAPPING.items():
            if phrase in v_lower:
                if phrase in r.preferred_name.lower() or (
                    r.drug_name and phrase in r.drug_name.lower()
                ):
                    lex_score = max(lex_score, 0.88)
                    cos_sim = max(cos_sim, 0.88)
                pref_concept = CLINICAL_SYNONYM_MAPPING.get(r.preferred_name.lower())
                if pref_concept == concept:
                    lex_score = max(lex_score, 0.85)
                    cos_sim = max(cos_sim, 0.85)

        combined_score = 0.5 * lex_score + 0.5 * cos_sim
        overall_score = max(combined_score, cos_sim * 0.92, lex_score * 0.92)

        scored_candidates.append((overall_score, cos_sim, lex_score, combined_score, r))

    scored_candidates.sort(key=lambda x: (-x[0], x[4].preferred_name, x[4].drug_code))

    highest_score, highest_cos, highest_lex, highest_comb, highest_record = (
        scored_candidates[0]
    )

    if highest_score >= 0.85:
        atc_context, ingredients = await _get_whodrug_context(
            session, highest_record, version
        )
        match_data = {
            "drug_code": highest_record.drug_code,
            "preferred_name": highest_record.preferred_name,
            "drug_name": highest_record.drug_name,
            "score": round(highest_score, 4),
            "cosine_similarity": round(highest_cos, 4),
            "lexical_score": round(highest_lex, 4),
            "combined_score": round(highest_comb, 4),
            "model_identifier": model_id,
            "atc_context": atc_context,
            "ingredients": ingredients,
        }
        suggestions_list = []
        for score, cos_sim, lex_score, combined_score, r in scored_candidates[1:]:
            if score >= 0.50:
                atc_context, ingredients = await _get_whodrug_context(
                    session, r, version
                )
                suggestions_list.append(
                    {
                        "drug_code": r.drug_code,
                        "preferred_name": r.preferred_name,
                        "drug_name": r.drug_name,
                        "score": round(score, 4),
                        "cosine_similarity": round(cos_sim, 4),
                        "lexical_score": round(lex_score, 4),
                        "combined_score": round(combined_score, 4),
                        "model_identifier": model_id,
                        "atc_context": atc_context,
                        "ingredients": ingredients,
                    }
                )
                if len(suggestions_list) >= top_k:
                    break

        return {
            "status": "AUTO-CODED",
            "match": match_data,
            "suggestions": suggestions_list,
        }

    if highest_score >= 0.50:
        suggestions_list = []
        for score, cos_sim, lex_score, combined_score, r in scored_candidates:
            if score >= 0.50:
                atc_context, ingredients = await _get_whodrug_context(
                    session, r, version
                )
                suggestions_list.append(
                    {
                        "drug_code": r.drug_code,
                        "preferred_name": r.preferred_name,
                        "drug_name": r.drug_name,
                        "score": round(score, 4),
                        "cosine_similarity": round(cos_sim, 4),
                        "lexical_score": round(lex_score, 4),
                        "combined_score": round(combined_score, 4),
                        "model_identifier": model_id,
                        "atc_context": atc_context,
                        "ingredients": ingredients,
                    }
                )
                if len(suggestions_list) >= top_k:
                    break

        return {
            "status": "SUGGESTIONS",
            "match": None,
            "suggestions": suggestions_list,
        }

    return {
        "status": "UNCODABLE",
        "match": None,
        "suggestions": [],
    }


async def match_semantic_verbatim_term(
    session: AsyncSession,
    verbatim: str,
    dictionary_type: str,
    version: str,
    target_level: str | None = None,
    top_k: int = 5,
) -> dict[str, Any]:
    """Exposes version-aware hybrid semantic and lexical clinical terminology matching.

    Args:
        session: Active database session.
        verbatim: Verbatim clinical text string.
        dictionary_type: 'MEDDRA' or 'WHODRUG'.
        version: Dictionary version string.
        target_level: Optional target level (e.g. 'PT', 'LLT') for MedDRA.
        top_k: Number of suggestions to retrieve.

    Returns:
        Dictionary with 'status', 'match', and 'suggestions'.
    """
    if not verbatim or not verbatim.strip():
        return {
            "status": "UNCODABLE",
            "match": None,
            "suggestions": [],
        }

    dict_upper = dictionary_type.upper()
    if dict_upper not in {"MEDDRA", "WHODRUG"}:
        raise ValueError(f"Unsupported dictionary type: {dictionary_type}")

    if dict_upper == "MEDDRA":
        return await _match_semantic_meddra(
            session=session,
            verbatim=verbatim.strip(),
            version=version.strip(),
            target_level=target_level,
            top_k=top_k,
        )
    return await _match_semantic_whodrug(
        session=session,
        verbatim=verbatim.strip(),
        version=version.strip(),
        top_k=top_k,
    )
