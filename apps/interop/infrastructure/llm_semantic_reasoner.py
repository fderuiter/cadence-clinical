"""LLM-based semantic reasoning and extraction engine for unstructured EHR clinical narratives.

Requirements: PRD-CRF-007, PRD-SYS-051
"""

import json
import logging
import os
import re
from typing import Any

import httpx

from apps.interop.domain.ports import LLMSemanticReasonerPort
from apps.interop.domain.semantic_mapping_models import (
    CDISCDomain,
    MappingStatus,
    MappingTier,
    SemanticMappedItem,
)
from apps.interop.infrastructure.embedding_matcher import EmbeddingMatcher
from packages.deid.detector import DeidDetector
from packages.deid.models import ComplianceProfile
from packages.deid.transforms import apply_deid_transforms
from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("interop-llm-reasoner")


class LLMSemanticReasoner(LLMSemanticReasonerPort):
    """Semantic reasoner extracting structured CDISC items from unstructured EHR narrative text."""

    def __init__(
        self,
        ai_gateway_url: str | None = None,
        embedding_matcher: EmbeddingMatcher | None = None,
    ) -> None:
        self.ai_gateway_url = ai_gateway_url or os.getenv("AI_GATEWAY_URL")
        self.embedding_matcher = embedding_matcher or EmbeddingMatcher()
        self.deid_detector = DeidDetector()

    def _deidentify_text(self, text: str, custom_terms: list[str] | None = None) -> str:
        """Apply strict in-flight HIPAA/GDPR PHI sanitization before inference."""
        results = self.deid_detector.detect(
            text, profile=ComplianceProfile.HIPAA, custom_terms=custom_terms
        )
        redacted_text, _ = apply_deid_transforms(text, results, default_strategy="mask")
        return redacted_text

    async def extract_concepts_from_narrative(
        self,
        narrative_text: str,
        study_id: str = "DEFAULT_STUDY",
        custom_terms: list[str] | None = None,
    ) -> list[SemanticMappedItem]:
        """Extract structured CDISC clinical items from unstructured clinical narrative text."""
        if not narrative_text or not narrative_text.strip():
            return []

        # 1. Sanitize text for PHI air-gap compliance
        sanitized_narrative = self._deidentify_text(narrative_text, custom_terms)

        # 2. If AI Gateway URL is active, attempt remote gateway structured generation
        if self.ai_gateway_url:
            try:
                remote_items = await self._query_ai_gateway(
                    sanitized_narrative, study_id
                )
                if remote_items:
                    return remote_items
            except Exception as e:
                logger.warning(
                    f"Remote AI Gateway invocation failed, falling back to local reasoning: {e}"
                )

        # 3. Deterministic / heuristic semantic extraction fallback
        return await self._local_semantic_extraction(sanitized_narrative, study_id)

    async def _query_ai_gateway(
        self, text: str, study_id: str
    ) -> list[SemanticMappedItem]:
        """Call AI Gateway structured generation endpoint with gateway authentication."""
        gateway_secret = os.getenv(
            "GATEWAY_SECRET", default="internal-gateway-secret-12345"
        )
        headers = generate_gateway_signature(
            user_id="interop-service",
            roles=["sponsor_dm", "sysadmin"],
            secret_key=gateway_secret,
        )

        prompt = f"""You are a clinical informatics expert. Extract all CDISC SDTM/CDASH clinical concepts from this EHR clinical narrative.
Return a JSON array of objects with fields:
- domain: "VS", "LB", "MH", "CM", "AE", "PE", or "PR"
- target_variable: e.g. "eCRF.VS.SYSBP", "eCRF.MH.MHTERM", "eCRF.CM.CMTRT"
- cdash_testcd: short code (SYSBP, DIABP, PULSE, TEMP, GLUC, etc.)
- cdash_test: full test/item name
- extracted_value: parsed numeric value or string term
- extracted_unit: parsed unit of measure (e.g. mmHg, mg, C, bpm)
- confidence_score: float from 0.0 to 1.0
- provenance: rationale explaining the extraction

Clinical Narrative:
{text}
"""
        payload = {
            "prompt": prompt,
            "tier": "tier_2_fast",
            "study_id": study_id,
            "enable_deid": True,
            "temperature": 0.0,
        }

        async with httpx.AsyncClient(timeout=10.0) as client:
            resp = await client.post(
                f"{self.ai_gateway_url}/api/v1/ai/generate",
                json=payload,
                headers=headers,
            )
            if resp.status_code == 200:
                data = resp.json()
                content = data.get("content", "")
                parsed_json = self._parse_json_from_response(content)
                if isinstance(parsed_json, list):
                    items: list[SemanticMappedItem] = []
                    for item_data in parsed_json:
                        conf = float(item_data.get("confidence_score", 0.85))
                        items.append(
                            SemanticMappedItem(
                                source_resource_type="DocumentReference",
                                source_display=text[:100],
                                target_domain=CDISCDomain(item_data["domain"]),
                                target_variable=item_data["target_variable"],
                                cdash_testcd=item_data.get("cdash_testcd"),
                                cdash_test=item_data.get("cdash_test"),
                                extracted_value=item_data.get("extracted_value"),
                                extracted_unit=item_data.get("extracted_unit"),
                                mapping_tier=MappingTier.LLM_FALLBACK,
                                confidence_score=conf,
                                provenance=item_data.get(
                                    "provenance", "Extracted via AI Gateway LLM"
                                ),
                                needs_human_review=(conf < 0.75),
                                status=MappingStatus.MAPPED,
                            )
                        )
                    return items
        return []

    def _parse_json_from_response(self, content: str) -> Any:
        """Safely extract and parse JSON array or object from LLM response text."""
        try:
            return json.loads(content)
        except Exception:
            json_match = re.search(r"(\[.*\]|\{.*\})", content, re.DOTALL)
            if json_match:
                try:
                    return json.loads(json_match.group(1))
                except Exception:
                    pass
        return None

    async def _local_semantic_extraction(
        self, text: str, study_id: str
    ) -> list[SemanticMappedItem]:
        """Local heuristic parser extracting clinical concepts from free text."""
        items: list[SemanticMappedItem] = []

        # 1. Blood pressure extraction (e.g. BP 120/80 mmHg or 135/85)
        bp_match = re.search(
            r"(?:bp|blood pressure|b/p)?\s*[:=]?\s*(\d{2,3})\s*/\s*(\d{2,3})\s*(?:mmhg)?",
            text,
            re.IGNORECASE,
        )
        if bp_match:
            sys_val = int(bp_match.group(1))
            dia_val = int(bp_match.group(2))
            items.append(
                SemanticMappedItem(
                    source_resource_type="DocumentReference",
                    source_display=bp_match.group(0),
                    target_domain=CDISCDomain.VS,
                    target_variable="eCRF.VS.SYSBP",
                    cdash_testcd="SYSBP",
                    cdash_test="Systolic Blood Pressure",
                    extracted_value=sys_val,
                    extracted_unit="mmHg",
                    mapping_tier=MappingTier.LLM_FALLBACK,
                    confidence_score=0.90,
                    provenance=f"Parsed systolic blood pressure {sys_val} mmHg from clinical note",
                    needs_human_review=False,
                    status=MappingStatus.MAPPED,
                )
            )
            items.append(
                SemanticMappedItem(
                    source_resource_type="DocumentReference",
                    source_display=bp_match.group(0),
                    target_domain=CDISCDomain.VS,
                    target_variable="eCRF.VS.DIABP",
                    cdash_testcd="DIABP",
                    cdash_test="Diastolic Blood Pressure",
                    extracted_value=dia_val,
                    extracted_unit="mmHg",
                    mapping_tier=MappingTier.LLM_FALLBACK,
                    confidence_score=0.90,
                    provenance=f"Parsed diastolic blood pressure {dia_val} mmHg from clinical note",
                    needs_human_review=False,
                    status=MappingStatus.MAPPED,
                )
            )

        # 2. Heart rate / pulse extraction (e.g. HR 72 bpm, pulse 80)
        hr_match = re.search(
            r"(?:hr|pulse|heart rate)\s*[:=]?\s*(\d{2,3})\s*(?:bpm|beats/min)?",
            text,
            re.IGNORECASE,
        )
        if hr_match:
            hr_val = int(hr_match.group(1))
            items.append(
                SemanticMappedItem(
                    source_resource_type="DocumentReference",
                    source_display=hr_match.group(0),
                    target_domain=CDISCDomain.VS,
                    target_variable="eCRF.VS.PULSE",
                    cdash_testcd="PULSE",
                    cdash_test="Pulse Rate",
                    extracted_value=hr_val,
                    extracted_unit="beats/min",
                    mapping_tier=MappingTier.LLM_FALLBACK,
                    confidence_score=0.90,
                    provenance=f"Parsed pulse rate {hr_val} bpm from clinical note",
                    needs_human_review=False,
                    status=MappingStatus.MAPPED,
                )
            )

        # 3. Temperature extraction (e.g. Temp: 37.2 C or 98.6 F)
        temp_match = re.search(
            r"(?:temp|temperature)\s*[:=]?\s*(\d{2}(?:\.\d+)?)\s*(?:°?\s*([cf]))?",
            text,
            re.IGNORECASE,
        )
        if temp_match:
            temp_val = float(temp_match.group(1))
            unit = "C"
            if temp_match.group(2) and temp_match.group(2).upper() == "F":
                # Convert Fahrenheit to Celsius
                temp_val = round((temp_val - 32) * 5 / 9, 1)
            items.append(
                SemanticMappedItem(
                    source_resource_type="DocumentReference",
                    source_display=temp_match.group(0),
                    target_domain=CDISCDomain.VS,
                    target_variable="eCRF.VS.TEMP",
                    cdash_testcd="TEMP",
                    cdash_test="Temperature",
                    extracted_value=temp_val,
                    extracted_unit=unit,
                    mapping_tier=MappingTier.LLM_FALLBACK,
                    confidence_score=0.88,
                    provenance=f"Parsed body temperature {temp_val} {unit} from clinical note",
                    needs_human_review=False,
                    status=MappingStatus.MAPPED,
                )
            )

        # 4. Medication mentions (e.g. Prescribed Metformin 500mg, taking Lisinopril)
        med_patterns = [
            (
                r"(?:prescribed|taking|on|administered)\s+([A-Za-z]+)\s*(\d+\s*(?:mg|mcg|g|ml))?",
                "CM",
            ),
            (
                r"(?:diagnosis of|history of|dx:?|h/o)\s+([A-Za-z\s]+?)(?:\.|\,|$|\n)",
                "MH",
            ),
        ]

        for pat, domain_type in med_patterns:
            matches = re.finditer(pat, text, re.IGNORECASE)
            for m in matches:
                entity_name = m.group(1).strip()
                if len(entity_name) < 3:
                    continue

                matched_elem, conf = await self.embedding_matcher.match_concept(
                    entity_name, min_confidence=0.50
                )
                if matched_elem and (
                    matched_elem.target_domain == CDISCDomain(domain_type)
                ):
                    items.append(
                        SemanticMappedItem(
                            source_resource_type="DocumentReference",
                            source_display=entity_name,
                            target_domain=matched_elem.target_domain,
                            target_variable=matched_elem.target_variable,
                            cdash_testcd=matched_elem.cdash_testcd,
                            cdash_test=matched_elem.cdash_test,
                            extracted_value=matched_elem.source_display,
                            mapping_tier=MappingTier.LLM_FALLBACK,
                            confidence_score=max(0.70, conf),
                            provenance=f"Inferred {domain_type} concept '{matched_elem.source_display}' from clinical narrative verbatim '{entity_name}'",
                            needs_human_review=(conf < 0.75),
                            status=MappingStatus.MAPPED,
                        )
                    )

        return items
