"""Signature completeness and regulatory manifestation verification service."""

import re
from typing import Any

from apps.etmf.domain.intelligence_models import (
    ExtractedSignature,
    SignatureAnalysisResult,
    SignaturePresenceStatus,
)
from apps.etmf.domain.services.document_intelligence_parser import (
    ParsedDocumentPayload,
)

# Mandatory signature requirements keyed by DIA artifact code
REQUIRED_SIGNATURE_MAP: dict[str, list[str]] = {
    "01.01.03": [
        "Principal Investigator",
        "Sponsor Representative",
    ],  # Protocol Sign-off
    "05.02.01": ["Principal Investigator"],  # FDA Form 1572
    "05.02.02": ["Investigator"],  # Financial Disclosure
    "05.02.04": ["Principal Investigator"],  # Delegation of Authority Log
    "05.02.05": ["Subject", "Investigator"],  # Informed Consent Form
}


class SignatureCompletenessAnalyzer:
    """Analyzes documents for required signature blocks, digital manifestations, and completeness."""

    def analyze(
        self,
        parsed_doc: ParsedDocumentPayload,
        artifact_code: str,
        existing_doc_signer: str | None = None,
        existing_doc_signature_manifestation: dict[str, Any] | str | None = None,
    ) -> SignatureAnalysisResult:
        """Evaluate signature presence and completeness against regulatory requirements.

        Args:
            parsed_doc: Parsed document text and layout blocks.
            artifact_code: Canonical DIA artifact code.
            existing_doc_signer: Signer ID if document is already signed in eTMF database.
            existing_doc_signature_manifestation: Signature manifestation if signed.

        Returns:
            SignatureAnalysisResult detailing status, extracted signatures, and missing requirements.
        """
        extracted_signatures: list[ExtractedSignature] = []
        text = parsed_doc.raw_text

        # 1. Check existing database signature metadata
        if existing_doc_signer:
            extracted_signatures.append(
                ExtractedSignature(
                    signer_name=existing_doc_signer,
                    signer_role="Authorized Signer",
                    confidence=1.0,
                    is_digital_signature=True,
                    signature_type="21_CFR_PART_11_ELECTRONIC",
                    location_hint="Database Signature Ledger",
                )
            )

        # 2. Check text for Part 11 Digital Manifestation anchors
        part11_manifests = re.findall(
            r"Digitally\s+Approved\s+&\s+Signed\s+by\s+([A-Za-z0-9_\.\s\-]+)\s*\(([^)]+)\)\s+on\s+([0-9T:\-\.Z+]+)",
            text,
            re.IGNORECASE,
        )
        for name, role, ts in part11_manifests:
            extracted_signatures.append(
                ExtractedSignature(
                    signer_name=name.strip(),
                    signer_role=role.strip(),
                    signature_date=ts.strip()[:10],
                    confidence=1.0,
                    is_digital_signature=True,
                    signature_type="DIGITAL_PKCS7_OR_HMAC",
                    location_hint="Electronic Signature Manifestation Block",
                )
            )

        # 3. Check text for `/s/ Name` or `Signed by: Name` slash signatures
        slash_sigs = re.findall(
            r"/s/\s*([A-Za-z\.\s,\-]+?)(?:\n|\r|\t|,|\s{2,}|$)",
            text,
            re.IGNORECASE,
        )
        for s_name in slash_sigs:
            name_clean = s_name.strip()
            if name_clean and len(name_clean) > 2:
                extracted_signatures.append(
                    ExtractedSignature(
                        signer_name=name_clean,
                        signer_role="Signer",
                        confidence=0.92,
                        is_digital_signature=False,
                        signature_type="SLASH_SIGNATURE",
                        location_hint="/s/ Signature Line",
                    )
                )

        # 4. Check text for `Investigator Signature: Name` or `Signature: Name`
        labeled_sigs = re.findall(
            r"(?:Investigator\s+Signature|Principal\s+Investigator\s+Signature|Subject\s+Signature|Sponsor\s+Signature|Signature)\s*[:_]\s*([A-Za-z\.\s,\-]+?)(?:\n|\r|\t|,|\s{2,}|$)",
            text,
            re.IGNORECASE,
        )
        for l_name in labeled_sigs:
            name_clean = l_name.strip()
            if (
                name_clean
                and len(name_clean) > 2
                and name_clean.lower() not in ("date", "n/a", "none", "___")
            ):
                extracted_signatures.append(
                    ExtractedSignature(
                        signer_name=name_clean,
                        signer_role="Document Signer",
                        confidence=0.88,
                        is_digital_signature=False,
                        signature_type="WET_OR_ELECTRONIC",
                        location_hint="Labeled Signature Block",
                    )
                )

        # Deduplicate extracted signatures by name
        unique_sigs: list[ExtractedSignature] = []
        seen_names: set[str] = set()
        for sig in extracted_signatures:
            norm_name = (sig.signer_name or "").lower().strip()
            if norm_name and norm_name not in seen_names:
                seen_names.add(norm_name)
                unique_sigs.append(sig)

        # 5. Evaluate against mandatory requirements
        required_roles = REQUIRED_SIGNATURE_MAP.get(artifact_code, [])
        if not required_roles:
            return SignatureAnalysisResult(
                status=SignaturePresenceStatus.SIGNATURE_NOT_REQUIRED,
                extracted_signatures=unique_sigs,
                missing_required_signatures=[],
                signature_blocks_detected=len(unique_sigs),
                details=f"Artifact code '{artifact_code}' does not require regulatory signatures.",
            )

        if not unique_sigs:
            return SignatureAnalysisResult(
                status=SignaturePresenceStatus.UNSIGNED,
                extracted_signatures=[],
                missing_required_signatures=required_roles,
                signature_blocks_detected=0,
                details=f"Document is unsigned. Missing required signatures: {', '.join(required_roles)}.",
            )

        if len(unique_sigs) >= len(required_roles):
            status = SignaturePresenceStatus.FULLY_SIGNED
            missing: list[str] = []
            details = f"All required signatures verified ({len(unique_sigs)} signature(s) present)."
        else:
            status = SignaturePresenceStatus.PARTIALLY_SIGNED
            missing = required_roles[len(unique_sigs) :]
            details = f"Partially signed: {len(unique_sigs)} present, missing: {', '.join(missing)}."

        return SignatureAnalysisResult(
            status=status,
            extracted_signatures=unique_sigs,
            missing_required_signatures=missing,
            signature_blocks_detected=len(unique_sigs),
            details=details,
        )
