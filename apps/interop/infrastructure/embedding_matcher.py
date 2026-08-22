"""Vector embedding semantic similarity matcher for CDISC concept resolution.

Requirements: PRD-CRF-007, PRD-SYS-051
"""

import math
import re
from collections import Counter

from apps.interop.domain.concept_maps import ALL_CONCEPT_MAPS
from apps.interop.domain.ports import EmbeddingMatcherPort
from apps.interop.domain.semantic_mapping_models import (
    ConceptMapElement,
)

# Curated clinical synonyms index enriching concept matching space
CLINICAL_SYNONYMS: dict[str, list[str]] = {
    "8480-6": [
        "systolic bp",
        "sbp",
        "systolic pressure",
        "sys bp",
        "blood pressure systolic",
        "systolic",
    ],
    "8462-4": [
        "diastolic bp",
        "dbp",
        "diastolic pressure",
        "dia bp",
        "blood pressure diastolic",
        "diastolic",
    ],
    "8867-4": [
        "pulse",
        "heart rate",
        "hr",
        "pulse rate",
        "cardiac rate",
        "beats per minute",
        "bpm",
        "heart rate beats per min",
    ],
    "8310-5": [
        "temperature",
        "temp",
        "body temp",
        "core temperature",
        "patient temperature",
        "body temperature",
    ],
    "29463-7": [
        "weight",
        "body weight",
        "wt",
        "patient weight",
        "mass",
    ],  # deid: ignore
    "8302-2": [
        "height",
        "body height",
        "ht",
        "patient height",
        "stature",
        "length",
    ],
    "9279-1": [
        "respiratory rate",
        "rr",
        "resp rate",
        "breathing rate",
        "respiration",
    ],
    "59408-5": [
        "oxygen saturation",
        "spo2",
        "pulse ox",
        "o2 sat",
        "blood oxygen",
        "pulse oximetry",
    ],
    "39156-5": ["bmi", "body mass index", "quetelet index"],
    "8478-0": ["mean arterial pressure", "map", "mean blood pressure"],
    "2339-0": [
        "glucose",
        "blood sugar",
        "fasting blood glucose",
        "fbg",
        "random blood sugar",
        "glu",
    ],
    "718-7": ["hemoglobin", "hgb", "hb", "blood hemoglobin"],
    "6690-2": [
        "white blood cell count",
        "wbc",
        "leukocytes",
        "total leukocyte count",
    ],
    "789-8": ["red blood cell count", "rbc", "erythrocytes"],
    "777-3": ["platelet count", "platelets", "plt", "thrombocytes"],
    "1742-6": [
        "alanine aminotransferase",
        "alt",
        "sgpt",
        "serum glutamic pyruvic transaminase",
        "serum alanine aminotransferase",
    ],
    "1920-8": [
        "aspartate aminotransferase",
        "ast",
        "sgot",
        "serum glutamic oxaloacetic transaminase",
        "serum aspartate aminotransferase",
    ],
    "2160-0": ["creatinine", "serum creatinine", "creat", "cr"],
    "1975-2": ["bilirubin", "total bilirubin", "bili", "t-bili"],
    "1751-7": ["albumin", "serum albumin", "alb"],
    "2823-3": ["potassium", "serum potassium", "k+", "k"],
    "2951-2": ["sodium", "serum sodium", "na+", "na"],
    "2093-3": ["cholesterol", "total cholesterol", "chol"],
    "2571-8": ["triglycerides", "triglyceride", "trig", "tg"],
    "4548-4": ["hba1c", "glycated hemoglobin", "a1c", "hemoglobin a1c"],
    "3094-0": ["blood urea nitrogen", "bun", "urea"],
    "33914-3": ["egfr", "estimated gfr", "glomerular filtration rate"],
    "38341003": [
        "hypertension",
        "high blood pressure",
        "htn",
        "essential hypertension",
    ],
    "44054006": [
        "type 2 diabetes",
        "t2d",
        "t2dm",
        "diabetes mellitus type 2",
        "non-insulin dependent diabetes",
    ],
    "195967001": ["asthma", "bronchial asthma", "reactive airway disease"],
    "37796009": ["migraine", "migraine headache", "hemicrania"],
    "56265001": [
        "heart disease",
        "cardiac condition",
        "cardiovascular disease",
        "coronary disease",
    ],
    "6809": [
        "metformin",
        "glucophage",
        "metformin hcl",
        "dimethylbiguanide",
        "metformin antidiabetic",
    ],
    "29046": ["lisinopril", "prinivil", "zestril"],
    "435": ["albuterol", "salbutamol", "proventil", "ventolin"],
    "1191": ["aspirin", "acetylsalicylic acid", "asa", "ecotrin"],
    "161": ["acetaminophen", "paracetamol", "tylenol", "apap"],
    "80146002": ["appendectomy", "appendix removal", "excision of appendix"],
    "232717009": [
        "cabg",
        "coronary artery bypass",
        "heart bypass surgery",
        "coronary bypass",
    ],
    "93000": ["ecg", "ekg", "electrocardiogram", "12-lead ecg"],
}


def _tokenize(text: str) -> list[str]:
    """Tokenize and normalize text into word and character n-grams."""
    cleaned = re.sub(r"[^a-zA-Z0-9\s]", " ", text.lower()).strip()
    words = [w for w in cleaned.split() if w]
    ngrams = list(words)
    # Include 3-character character ngrams for fuzzy word boundary matching
    for word in words:
        if len(word) >= 3:
            for i in range(len(word) - 2):
                ngrams.append(word[i : i + 3])
    return ngrams


def _compute_cosine_similarity(vec1: Counter[str], vec2: Counter[str]) -> float:
    """Compute cosine similarity between two frequency vector counters."""
    intersection = set(vec1.keys()) & set(vec2.keys())
    dot_product = sum(vec1[x] * vec2[x] for x in intersection)

    sum1 = sum(val**2 for val in vec1.values())
    sum2 = sum(val**2 for val in vec2.values())
    denominator = math.sqrt(sum1) * math.sqrt(sum2)

    if not denominator:
        return 0.0
    return float(dot_product) / denominator


class EmbeddingMatcher(EmbeddingMatcherPort):
    """Semantic vector embedding and similarity matcher for CDISC concepts."""

    def __init__(
        self,
        concept_catalog: list[ConceptMapElement] | None = None,
        custom_synonyms: dict[str, list[str]] | None = None,
    ) -> None:
        self.catalog = concept_catalog or ALL_CONCEPT_MAPS
        self.synonyms: dict[str, list[str]] = dict(CLINICAL_SYNONYMS)
        if custom_synonyms:
            for k, v in custom_synonyms.items():
                self.synonyms.setdefault(k, []).extend(v)

        # Pre-build vector representations for each phrase/synonym of each concept
        self._concept_phrase_vectors: list[tuple[ConceptMapElement, Counter[str]]] = []
        self._build_index()

    def _build_index(self) -> None:
        """Build searchable phrase token vectors over catalog elements and synonyms."""
        self._concept_phrase_vectors.clear()
        for elem in self.catalog:
            phrases = [
                elem.source_display,
                elem.cdash_test,
                elem.cdash_testcd,
            ]
            if elem.description:
                phrases.append(elem.description)
            if elem.source_code in self.synonyms:
                phrases.extend(self.synonyms[elem.source_code])

            for phrase in phrases:
                if phrase and phrase.strip():
                    vec = Counter(_tokenize(phrase))
                    self._concept_phrase_vectors.append((elem, vec))

    async def match_concept(
        self,
        query_text: str,
        candidates: list[ConceptMapElement] | None = None,
        min_confidence: float = 0.82,
    ) -> tuple[ConceptMapElement | None, float]:
        """Find best matching ConceptMap element using cosine similarity against indexed concepts."""
        if not query_text or not query_text.strip():
            return None, 0.0

        query_tokens = _tokenize(query_text)
        query_vector = Counter(query_tokens)
        query_norm = query_text.strip().lower()

        best_elem: ConceptMapElement | None = None
        best_score = 0.0

        candidate_set = set(candidates) if candidates else None

        for elem, phrase_vec in self._concept_phrase_vectors:
            if candidate_set and elem not in candidate_set:
                continue

            similarity = _compute_cosine_similarity(query_vector, phrase_vec)

            # Direct string equality checks
            if (
                query_norm == elem.source_display.lower()
                or query_norm == elem.cdash_testcd.lower()
                or query_norm == elem.cdash_test.lower()
            ):
                similarity = max(similarity, 0.98)
            elif any(
                query_norm == syn.lower()
                for syn in self.synonyms.get(elem.source_code, [])
            ):
                similarity = max(similarity, 0.96)

            if similarity > best_score:
                best_score = similarity
                best_elem = elem

        confidence = round(min(1.0, max(0.0, best_score)), 4)

        if best_elem and confidence >= min_confidence:
            return best_elem, confidence

        return None, confidence
