"""Multi-signal DIA TMF Reference Model v3.2.0 classifier and taxonomy mapper."""

from typing import Any

from apps.etmf.domain.intelligence_models import (
    ClassificationConfidenceTier,
    QCRecommendation,
    TaxonomyMatchCandidate,
)
from apps.etmf.domain.services.document_intelligence_parser import (
    ParsedDocumentPayload,
)
from apps.etmf.domain.tmf_reference_model import (
    get_active_catalog,
    get_catalog,
    resolve_artifact,
)

# High-fidelity regulatory form OMB number to DIA artifact mapping
OMB_FORM_MAP: dict[str, tuple[str, str, str]] = {
    "0910-0014": (
        "05.02.01",
        "FDA Form 1572",
        "FDA Form 1572 Statement of Investigator",
    ),
    "0910-0396": (
        "05.02.02",
        "Financial Disclosure",
        "FDA 3454/3455 Financial Disclosure",
    ),
}

# Explicit high-confidence layout markers
LAYOUT_MARKER_MAP: dict[str, tuple[str, float]] = {
    "FDA_FORM_1572": ("05.02.01", 0.98),
    "STATEMENT_OF_INVESTIGATOR": ("05.02.01", 0.96),
    "FINANCIAL_DISCLOSURE": ("05.02.02", 0.96),
    "INVESTIGATOR_CV": ("05.02.03", 0.95),
    "DOA_LOG": ("05.02.04", 0.97),
    "INFORMED_CONSENT_FORM": ("05.02.05", 0.96),
    "MEDICAL_LICENSE": ("05.02.98", 0.95),
    "PROTOCOL_SIGNOFF": ("01.01.03", 0.96),
    "INVESTIGATORS_BROCHURE": ("02.01.01", 0.95),
    "IRB_APPROVAL": ("04.01.01", 0.94),
    "SITE_FEASIBILITY": ("05.01.01", 0.92),
    "SITE_TRAINING": ("05.03.01", 0.93),
    "IP_RECORDS": ("06.01.01", 0.93),
    "LAB_CERTIFICATE": ("08.01.01", 0.94),
    "DATA_MANAGEMENT_PLAN": ("10.01.01", 0.95),
    "STATISTICAL_ANALYSIS_PLAN": ("11.01.01", 0.96),
    "CLINICAL_STUDY_REPORT": ("11.02.01", 0.96),
}

# Cross-system mapping from eTMF DIA artifact code to eISF binder sections (ICH GCP E6(R2) / DIA eISF Reference Model)
ETMF_TO_EISF_MAP: dict[str, dict[str, str]] = {
    "01.01.01": {
        "eisf_section": "01_PROTOCOL",
        "folder_name": "Protocol & Amendments",
        "scope": "SITE",
    },
    "01.01.03": {
        "eisf_section": "01_PROTOCOL",
        "folder_name": "Protocol Signature Pages",
        "scope": "SITE",
    },
    "02.01.01": {
        "eisf_section": "02_INVESTIGATORS_BROCHURE",
        "folder_name": "Investigator's Brochure",
        "scope": "SITE",
    },
    "04.01.01": {
        "eisf_section": "03_IRB_IEC",
        "folder_name": "IRB/IEC Approvals & Correspondence",
        "scope": "SITE",
    },
    "04.02.01": {
        "eisf_section": "03_IRB_IEC",
        "folder_name": "IRB/IEC Notifications",
        "scope": "SITE",
    },
    "05.01.01": {
        "eisf_section": "04_REGULATORY",
        "folder_name": "Feasibility & Pre-Study",
        "scope": "SITE",
    },
    "05.02.01": {
        "eisf_section": "04_REGULATORY",
        "folder_name": "FDA Form 1572",
        "scope": "SITE",
    },
    "05.02.02": {
        "eisf_section": "04_REGULATORY",
        "folder_name": "Financial Disclosures",
        "scope": "SITE",
    },
    "05.02.03": {
        "eisf_section": "05_STAFF_QUALIFICATIONS",
        "folder_name": "Curriculum Vitae & GCP Training",
        "scope": "SITE",
    },
    "05.02.04": {
        "eisf_section": "05_STAFF_QUALIFICATIONS",
        "folder_name": "Delegation of Authority Log",
        "scope": "SITE",
    },
    "05.02.05": {
        "eisf_section": "06_INFORMED_CONSENT",
        "folder_name": "Approved Informed Consent Forms",
        "scope": "SITE",
    },
    "05.02.98": {
        "eisf_section": "05_STAFF_QUALIFICATIONS",
        "folder_name": "Medical Licenses & Certifications",
        "scope": "SITE",
    },
    "05.03.01": {
        "eisf_section": "05_STAFF_QUALIFICATIONS",
        "folder_name": "Site Training Records",
        "scope": "SITE",
    },
    "06.01.01": {
        "eisf_section": "07_IP_ACCOUNTABILITY",
        "folder_name": "Investigational Product Records",
        "scope": "SITE",
    },
    "06.02.01": {
        "eisf_section": "07_IP_ACCOUNTABILITY",
        "folder_name": "IP Shipping & Receipts",
        "scope": "SITE",
    },
    "07.01.01": {
        "eisf_section": "08_SAFETY",
        "folder_name": "Safety Notifications & SAE Reports",
        "scope": "SITE",
    },
    "08.01.01": {
        "eisf_section": "09_LABORATORY",
        "folder_name": "Central & Local Lab Certificates",
        "scope": "SITE",
    },
    "08.02.01": {
        "eisf_section": "09_LABORATORY",
        "folder_name": "Lab Reference Ranges",
        "scope": "SITE",
    },
}


class DIAReferenceModelClassifier:
    """Multi-signal DIA TMF Reference Model classifier combining layout, OMB, keywords, and hints."""

    def __init__(self, default_catalog_version: str = "v3.2.0-extended") -> None:
        self.default_catalog_version = default_catalog_version

    def classify(
        self,
        parsed_doc: ParsedDocumentPayload,
        filename: str,
        artifact_hint: str | None = None,
        free_text: str | None = None,
        taxonomy_version: str | None = None,
        ai_structured_suggestion: dict[str, Any] | None = None,
    ) -> tuple[
        TaxonomyMatchCandidate,
        list[TaxonomyMatchCandidate],
        ClassificationConfidenceTier,
        QCRecommendation,
        dict[str, Any] | None,
    ]:
        """Execute multi-signal classification and return ranked candidates and recommendations."""
        tax_version = taxonomy_version or self.default_catalog_version
        try:
            catalog = get_catalog(tax_version)
        except KeyError:
            try:
                catalog = get_catalog("v3.2.0-extended")
                tax_version = "v3.2.0-extended"
            except Exception:
                catalog = get_active_catalog()
                tax_version = catalog.version

        extended_catalog = get_catalog("v3.2.0-extended")
        scored_candidates: dict[str, dict[str, Any]] = {}

        def record_signal(code: str, score: float, signal: str) -> None:
            if code not in scored_candidates:
                scored_candidates[code] = {
                    "score": 0.0,
                    "signals": [],
                }
            scored_candidates[code]["score"] = max(
                scored_candidates[code]["score"], score
            )
            if signal not in scored_candidates[code]["signals"]:
                scored_candidates[code]["signals"].append(signal)

        # Signal 1: OMB Form Number Match (Confidence: 0.99)
        for omb in parsed_doc.detected_omb_numbers:
            if omb in OMB_FORM_MAP:
                code, name, desc = OMB_FORM_MAP[omb]
                record_signal(code, 0.99, f"omb_number:{omb}")

        # Signal 2: Detected Form Layout Markers (Confidence: 0.92 - 0.98)
        for marker in parsed_doc.detected_form_markers:
            if marker in LAYOUT_MARKER_MAP:
                code, conf = LAYOUT_MARKER_MAP[marker]
                record_signal(code, conf, f"layout_marker:{marker}")

        # Signal 3: Artifact Hint Match
        if artifact_hint:
            cleaned_hint = artifact_hint.strip()
            # If input is direct artifact code
            if cleaned_hint in extended_catalog.artifact_map:
                record_signal(cleaned_hint, 0.95, f"artifact_code_hint:{cleaned_hint}")
            else:
                try:
                    resolved = resolve_artifact("v3.2.0-extended", name=cleaned_hint)
                    record_signal(
                        resolved["artifact"].code,
                        0.92,
                        f"artifact_type_hint:{cleaned_hint}",
                    )
                except Exception:
                    pass

        # Signal 4: Filename Substring Matches
        fn_lower = filename.lower().replace("_", " ").replace("-", " ")
        for code, art in extended_catalog.artifact_map.items():
            if code.lower() in fn_lower:
                record_signal(code, 0.88, f"filename_code:{code}")
            elif art.name.lower() in fn_lower:
                record_signal(code, 0.85, f"filename_name:{art.name}")

        # Signal 5: AI Gateway Structured Output Integration
        if ai_structured_suggestion:
            suggested_code = ai_structured_suggestion.get("artifact_code")
            suggested_conf = float(ai_structured_suggestion.get("confidence", 0.90))
            if suggested_code and suggested_code in extended_catalog.artifact_map:
                record_signal(
                    suggested_code,
                    min(0.98, suggested_conf),
                    f"ai_gateway_inference:{suggested_code}",
                )

        # Signal 6: Keyword Substring Scoring in Text
        text_lower = parsed_doc.normalized_text
        if free_text:
            text_lower += " " + free_text.lower()

        # Score matching artifact names in body text
        for code, art in extended_catalog.artifact_map.items():
            art_name_lower = art.name.lower()
            if len(art_name_lower) > 5 and art_name_lower in text_lower:
                record_signal(code, 0.75, f"text_keyword:{art.name}")

        # If no candidates scored, fall back to default general document code 01.01.01 with low confidence
        if not scored_candidates:
            record_signal("01.01.01", 0.20, "fallback_default")

        # Resolve candidate objects
        candidate_list: list[TaxonomyMatchCandidate] = []
        for code, data in scored_candidates.items():
            resolved = None
            for version_to_try in [
                tax_version,
                "v3.2.0-extended",
                "v3.2.0-complete",
                "v3.2.0",
            ]:
                try:
                    resolved = resolve_artifact(version_to_try, code=code)
                    break
                except Exception:
                    continue

            if resolved:
                candidate_list.append(
                    TaxonomyMatchCandidate(
                        zone_code=resolved["zone"].code,
                        zone_name=resolved["zone"].name,
                        section_code=resolved["section"].code,
                        section_name=resolved["section"].name,
                        artifact_code=resolved["artifact"].code,
                        artifact_name=resolved["artifact"].name,
                        confidence=round(data["score"], 3),
                        matched_signals=data["signals"],
                        is_extension=getattr(
                            resolved["artifact"], "is_extension", False
                        ),
                    )
                )

        if not candidate_list:
            # Fallback safe candidate
            res = resolve_artifact("v3.2.0-extended", code="01.01.01")
            candidate_list.append(
                TaxonomyMatchCandidate(
                    zone_code=res["zone"].code,
                    zone_name=res["zone"].name,
                    section_code=res["section"].code,
                    section_name=res["section"].name,
                    artifact_code=res["artifact"].code,
                    artifact_name=res["artifact"].name,
                    confidence=0.20,
                    matched_signals=["fallback_default"],
                )
            )

        # Sort by confidence descending
        candidate_list.sort(key=lambda c: c.confidence, reverse=True)
        primary = candidate_list[0]
        alternatives = candidate_list[1:5]

        # Determine confidence tier
        if primary.confidence >= 0.85:
            confidence_tier = ClassificationConfidenceTier.HIGH
            qc_rec = QCRecommendation.AUTO_CLASSIFY
        elif primary.confidence >= 0.50:
            confidence_tier = ClassificationConfidenceTier.MEDIUM
            qc_rec = QCRecommendation.FLAG_FOR_QC_REVIEW
        else:
            confidence_tier = ClassificationConfidenceTier.LOW
            qc_rec = QCRecommendation.MANUAL_RECLASSIFICATION_REQUIRED

        # Resolve eISF Cross-System Mapping
        eisf_mapping = ETMF_TO_EISF_MAP.get(primary.artifact_code)

        return primary, alternatives, confidence_tier, qc_rec, eisf_mapping
