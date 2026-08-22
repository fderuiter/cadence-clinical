"""Readability metrics and patient-friendly terminology harmonization services.

Implements Flesch-Kincaid Grade Level, Flesch Reading Ease, and Dale-Chall scoring algorithms
along with AI Gateway Tier 2 routing and deterministic clinical jargon replacement.
"""

import re
from typing import Any

from apps.econsent.adapters.ai_readability_client import (
    AIReadabilityGatewayClient,
)
from apps.econsent.domain.readability import (
    HarmonizationResult,
    JargonSubstitution,
    ReadabilityMetrics,
    count_syllables_word,
    interpret_dale_chall_grade_level,
    interpret_reading_ease,
    is_dale_chall_familiar,
    tokenize_sentences,
    tokenize_words,
)

# Deterministic clinical terminology dictionary with plain-language patient friendly substitutions
CLINICAL_JARGON_DICTIONARY: list[dict[str, Any]] = [
    {
        "term": "myocardial infarction",
        "replacement": "heart attack",
        "rationale": "Direct plain-language synonym universally understood by lay patients.",
        "category": "clinical_terminology",
        "confidence": 0.99,
    },
    {
        "term": "hypertension",
        "replacement": "high blood pressure",
        "rationale": "Replaces clinical diagnostic term with common everyday condition name.",
        "category": "clinical_terminology",
        "confidence": 0.99,
    },
    {
        "term": "hyperglycemia",
        "replacement": "high blood sugar",
        "rationale": "Clarifies biochemical state into familiar patient terminology.",
        "category": "clinical_terminology",
        "confidence": 0.98,
    },
    {
        "term": "hypoglycemia",
        "replacement": "low blood sugar",
        "rationale": "Clarifies glucose deficiency into plain language.",
        "category": "clinical_terminology",
        "confidence": 0.98,
    },
    {
        "term": "edema",
        "replacement": "swelling from fluid build-up",
        "rationale": "Replaces technical pathological term with clear description of symptom.",
        "category": "clinical_terminology",
        "confidence": 0.97,
    },
    {
        "term": "subcutaneous",
        "replacement": "under the skin",
        "rationale": "Replaces anatomical injection route with simple spatial description.",
        "category": "procedure",
        "confidence": 0.99,
    },
    {
        "term": "intravenous",
        "replacement": "into a vein through a small tube or needle",
        "rationale": "Describes the administration method clearly without medical shorthand.",
        "category": "procedure",
        "confidence": 0.99,
    },
    {
        "term": "venipuncture",
        "replacement": "blood draw",
        "rationale": "Common lay phrase for blood collection procedure.",
        "category": "procedure",
        "confidence": 0.99,
    },
    {
        "term": "randomization",
        "replacement": "assignment by chance (like flipping a coin)",
        "rationale": "Explains the statistical allocation process in an accessible analogy.",
        "category": "clinical_trial_design",
        "confidence": 0.99,
    },
    {
        "term": "placebo",
        "replacement": "inactive dummy treatment (sugar pill)",
        "rationale": "Clarifies control intervention for informed comprehension.",
        "category": "clinical_trial_design",
        "confidence": 0.99,
    },
    {
        "term": "double-blind",
        "replacement": "neither you nor your study doctor will know which treatment you receive",
        "rationale": "Clearly describes blinding methodology without clinical jargon.",
        "category": "clinical_trial_design",
        "confidence": 0.99,
    },
    {
        "term": "contraindication",
        "replacement": "medical reason not to take this treatment",
        "rationale": "Replaces formal regulatory term with straightforward explanation.",
        "category": "risk",
        "confidence": 0.96,
    },
    {
        "term": "adverse event",
        "replacement": "unexpected side effect or medical problem",
        "rationale": "Standard plain-language translation for safety reporting term.",
        "category": "risk",
        "confidence": 0.98,
    },
    {
        "term": "carcinoma",
        "replacement": "cancer",
        "rationale": "Direct plain-language synonym.",
        "category": "clinical_terminology",
        "confidence": 0.99,
    },
    {
        "term": "dyspnea",
        "replacement": "shortness of breath",
        "rationale": "Common descriptive symptom phrasing.",
        "category": "clinical_terminology",
        "confidence": 0.98,
    },
    {
        "term": "analgesic",
        "replacement": "pain-relieving medicine",
        "rationale": "Explains pharmacological class by function.",
        "category": "medication",
        "confidence": 0.97,
    },
    {
        "term": "ambulatory",
        "replacement": "able to walk around",
        "rationale": "Replaces clinical mobility rating with plain description.",
        "category": "clinical_terminology",
        "confidence": 0.96,
    },
    {
        "term": "nephrotoxicity",
        "replacement": "kidney damage",
        "rationale": "Translates organ-specific toxicity term into everyday language.",
        "category": "risk",
        "confidence": 0.98,
    },
    {
        "term": "hepatotoxicity",
        "replacement": "liver damage",
        "rationale": "Translates hepatic toxicity term into everyday language.",
        "category": "risk",
        "confidence": 0.98,
    },
    {
        "term": "prognosis",
        "replacement": "likely future course of your health condition",
        "rationale": "Clarifies medical outlook term.",
        "category": "clinical_terminology",
        "confidence": 0.95,
    },
    {
        "term": "thrombosis",
        "replacement": "blood clot",
        "rationale": "Everyday term for vascular occlusion.",
        "category": "clinical_terminology",
        "confidence": 0.98,
    },
    {
        "term": "pharmacokinetics",
        "replacement": "how your body absorbs, breaks down, and eliminates the drug",
        "rationale": "Decomposes complex pharmacological concept into clear physiological steps.",
        "category": "clinical_trial_design",
        "confidence": 0.98,
    },
    {
        "term": "prophylaxis",
        "replacement": "preventive treatment",
        "rationale": "Simplifies technical clinical term for disease prevention.",
        "category": "clinical_terminology",
        "confidence": 0.97,
    },
    {
        "term": "pruritus",
        "replacement": "itching",
        "rationale": "Direct plain-language symptom equivalent.",
        "category": "clinical_terminology",
        "confidence": 0.99,
    },
    {
        "term": "cerebrovascular accident",
        "replacement": "stroke",
        "rationale": "Common lay phrase for neurological vascular event.",
        "category": "clinical_terminology",
        "confidence": 0.99,
    },
]


class ReadabilityMetricsService:
    """Calculates deterministic readability metrics (FKGL, FRE, Dale-Chall) for consent texts."""

    def compute_metrics(self, text: str) -> ReadabilityMetrics:
        """Computes comprehensive readability scores for an input narrative or clause text.

        Args:
            text: Raw or HTML narrative text.

        Returns:
            ReadabilityMetrics containing FKGL, FRE, Dale-Chall indices, and interpretation.
        """
        words = tokenize_words(text)
        sentences = tokenize_sentences(text)

        word_count = len(words)
        sentence_count = max(1, len(sentences))
        syllable_count = sum(count_syllables_word(w) for w in words)

        # 1. Flesch Reading Ease (FRE) & Flesch-Kincaid Grade Level (FKGL)
        if word_count > 0:
            asl = word_count / sentence_count  # Average Sentence Length
            asw = syllable_count / word_count  # Average Syllables per Word

            fre = 206.835 - (1.015 * asl) - (84.6 * asw)
            fkgl = (0.39 * asl) + (11.8 * asw) - 15.59

            fre = max(0.0, min(100.0, fre))
            fkgl = max(0.0, min(20.0, fkgl))
        else:
            fre = 100.0
            fkgl = 0.0

        # 2. Dale-Chall Readability Index
        difficult_words = [w for w in words if not is_dale_chall_familiar(w)]
        difficult_word_count = len(difficult_words)

        if word_count > 0:
            diff_percentage = (difficult_word_count / word_count) * 100.0
            asl = word_count / sentence_count
            raw_dale_chall = (0.1579 * diff_percentage) + (0.0496 * asl)
            if diff_percentage > 5.0:
                dale_chall_score = raw_dale_chall + 3.6365
            else:
                dale_chall_score = raw_dale_chall
            dale_chall_score = max(0.0, min(16.0, dale_chall_score))
        else:
            dale_chall_score = 0.0

        dale_chall_grade_str = interpret_dale_chall_grade_level(dale_chall_score)
        interpretation = interpret_reading_ease(fre)

        # Target grade level for ICFs is 6th to 8th grade (FKGL <= 8.0 and Dale-Chall score <= 6.9, or FKGL <= 8.5)
        is_target = (fkgl <= 8.5) and (dale_chall_score <= 7.2)

        return ReadabilityMetrics(
            word_count=word_count,
            sentence_count=sentence_count,
            syllable_count=syllable_count,
            difficult_word_count=difficult_word_count,
            difficult_words=sorted(list(set(w.lower() for w in difficult_words))),
            flesch_reading_ease=round(fre, 1),
            flesch_kincaid_grade_level=round(fkgl, 1),
            dale_chall_score=round(dale_chall_score, 2),
            dale_chall_grade_level=dale_chall_grade_str,
            is_target_grade_level=is_target,
            interpretation=interpretation,
        )


class ReadabilityHarmonizerService:
    """Harmonizes complex clinical clauses into patient-friendly plain language."""

    def __init__(
        self,
        metrics_service: ReadabilityMetricsService | None = None,
        ai_client: AIReadabilityGatewayClient | None = None,
    ) -> None:
        self.metrics_service = metrics_service or ReadabilityMetricsService()
        self.ai_client = ai_client or AIReadabilityGatewayClient()

    async def harmonize_text(
        self,
        text: str,
        target_grade_level: float = 8.0,
        study_id: str | None = None,
        tenant_id: str = "tenant_default",
    ) -> HarmonizationResult:
        """Analyzes text, suggests medical jargon replacements via AI Gateway / dictionary, and computes improvements.

        Args:
            text: Original consent text.
            target_grade_level: Target reading grade level.
            study_id: Optional study identifier scope.
            tenant_id: Multi-tenant scope.

        Returns:
            HarmonizationResult with original & post-harmonization metrics and substitution diffs.
        """
        original_metrics = self.metrics_service.compute_metrics(text)
        substitutions: list[JargonSubstitution] = []
        seen_terms: set[str] = set()

        # 1. Attempt AI Gateway Tier 2 extraction
        if self.ai_client:
            ai_suggestions = await self.ai_client.generate_simplification_suggestions(
                text=text,
                target_grade_level=target_grade_level,
                study_id=study_id,
                tenant_id=tenant_id,
            )
            for item in ai_suggestions:
                orig = item.get("original_term", "").strip()
                sugg = item.get("suggested_term", "").strip()
                if orig and sugg and orig.lower() not in seen_terms:
                    # Verify term exists in text
                    pattern = re.compile(re.escape(orig), re.IGNORECASE)
                    match = pattern.search(text)
                    if match:
                        seen_terms.add(orig.lower())
                        substitutions.append(
                            JargonSubstitution(
                                original_term=orig,
                                suggested_term=sugg,
                                rationale=item.get(
                                    "rationale",
                                    "Plain-language substitution for patient comprehension.",
                                ),
                                category=item.get("category", "clinical_terminology"),
                                confidence_score=float(
                                    item.get("confidence_score", 0.90)
                                ),
                                start_offset=match.start(),
                                end_offset=match.end(),
                            )
                        )

        # 2. Complement with deterministic clinical dictionary matching
        for entry in CLINICAL_JARGON_DICTIONARY:
            term = entry["term"]
            if term.lower() not in seen_terms:
                pattern = re.compile(rf"\b{re.escape(term)}\b", re.IGNORECASE)
                match = pattern.search(text)
                if match:
                    seen_terms.add(term.lower())
                    substitutions.append(
                        JargonSubstitution(
                            original_term=match.group(0),
                            suggested_term=entry["replacement"],
                            rationale=entry["rationale"],
                            category=entry["category"],
                            confidence_score=entry["confidence"],
                            start_offset=match.start(),
                            end_offset=match.end(),
                        )
                    )

        # 3. Apply substitutions to produce harmonized text
        harmonized_text = text
        for sub in substitutions:
            pattern = re.compile(rf"\b{re.escape(sub.original_term)}\b", re.IGNORECASE)
            harmonized_text = pattern.sub(sub.suggested_term, harmonized_text)

        # 4. Compute post-harmonization readability metrics
        harmonized_metrics = self.metrics_service.compute_metrics(harmonized_text)
        grade_delta = round(
            original_metrics.flesch_kincaid_grade_level
            - harmonized_metrics.flesch_kincaid_grade_level,
            2,
        )

        return HarmonizationResult(
            original_metrics=original_metrics,
            harmonized_metrics=harmonized_metrics,
            substitutions=substitutions,
            harmonized_text=harmonized_text,
            grade_level_delta=grade_delta,
            model_identifier="cadence-tier2-readability-harmonizer",
        )
