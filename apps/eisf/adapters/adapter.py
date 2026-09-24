"""
eISF-to-eTMF regulatory-artifact mapping and deterministic cross-system deduplication module.
"""

from enum import StrEnum
from typing import Any


class DocumentClassification(StrEnum):
    """
    Standard classification options for incoming documents.
    """

    NEW = "NEW"
    CHANGED = "CHANGED"
    DUPLICATE = "DUPLICATE"


FORWARD_MAPPING: dict[tuple[str, str], tuple[int, str, str, str]] = {
    ("investigator & staff", "investigator cv"): (
        5,
        "05.02",
        "Investigator CV",
        "05.02.03",
    ),
    ("investigator & staff", "delegation of authority log"): (
        5,
        "05.02",
        "Delegation of Authority Log",
        "05.02.04",
    ),
    ("investigator & staff", "financial disclosure"): (
        5,
        "05.02",
        "Financial Disclosure",
        "05.02.02",
    ),
    ("investigator & staff", "medical license"): (
        5,
        "05.02",
        "Medical License",
        "05.02.98",
    ),
    ("protocols & amendments", "approved protocol"): (
        1,
        "01.01",
        "Clinical Trial Protocol",
        "01.01.01",
    ),
    ("protocols & amendments", "protocol sign-off"): (
        1,
        "01.01",
        "Protocol Sign-off",
        "01.01.03",
    ),
    ("regulatory approvals", "irb approval"): (
        4,
        "04.01",
        "IRB/IEC Approval",
        "04.01.01",
    ),
    ("regulatory approvals", "fda form 1572"): (
        5,
        "05.02",
        "FDA Form 1572",
        "05.02.01",
    ),
}

REVERSE_MAPPING: dict[tuple[int, str, str, str], tuple[str, str]] = {
    (5, "05.02", "investigator cv", "05.02.03"): (
        "Investigator & Staff",
        "Investigator CV",
    ),
    (5, "05.02", "delegation of authority log", "05.02.04"): (
        "Investigator & Staff",
        "Delegation of Authority Log",
    ),
    (5, "05.02", "financial disclosure", "05.02.02"): (
        "Investigator & Staff",
        "Financial Disclosure",
    ),
    (5, "05.02", "medical license", "05.02.98"): (
        "Investigator & Staff",
        "Medical License",
    ),
    (1, "01.01", "clinical trial protocol", "01.01.01"): (
        "Protocols & Amendments",
        "Approved Protocol",
    ),
    (1, "01.01", "protocol sign-off", "01.01.03"): (
        "Protocols & Amendments",
        "Protocol Sign-off",
    ),
    (4, "04.01", "irb/iec approval", "04.01.01"): (
        "Regulatory Approvals",
        "IRB Approval",
    ),
    (5, "05.02", "fda form 1572", "05.02.01"): (
        "Regulatory Approvals",
        "FDA Form 1572",
    ),
}


def normalize_string(value: str) -> str:
    if value is None:
        return ""
    return value.strip().lower()


def map_eisf_to_etmf(binder_classification: str, artifact_type: str) -> dict[str, Any]:
    norm_binder = normalize_string(binder_classification)
    norm_art = normalize_string(artifact_type)

    key = (norm_binder, norm_art)
    if key not in FORWARD_MAPPING:
        raise ValueError(
            f"Unsupported eISF mapping for binder_classification='{binder_classification}' "
            f"and artifact_type='{artifact_type}'."
        )

    zone, section, etmf_art_type, etmf_art_code = FORWARD_MAPPING[key]
    return {
        "zone": zone,
        "section": section,
        "artifact_type": etmf_art_type,
        "artifact_code": etmf_art_code,
    }


def map_etmf_to_eisf(
    zone: int,
    section: str,
    artifact_type: str,
    artifact_code: str | None = None,
) -> dict[str, str]:
    norm_section = normalize_string(section)
    norm_art_type = normalize_string(artifact_type)
    norm_code = normalize_string(artifact_code) if artifact_code else ""

    matched = None
    for (z, s, t, c), val in REVERSE_MAPPING.items():
        if (
            z == zone
            and normalize_string(s) == norm_section
            and normalize_string(t) == norm_art_type
        ):
            if not norm_code or normalize_string(c) == norm_code:
                matched = val
                break

    if not matched:
        raise ValueError(
            f"Unsupported eTMF reverse mapping for zone={zone}, section='{section}', "
            f"artifact_type='{artifact_type}', artifact_code='{artifact_code}'."
        )

    binder_class, eisf_art_type = matched
    return {
        "binder_classification": binder_class,
        "artifact_type": eisf_art_type,
    }


def derive_correlation_key(
    study_id: str,
    site_id: str,
    binder_classification: str,
    artifact_type: str,
) -> str:
    norm_study = normalize_string(study_id)
    norm_site = normalize_string(site_id)
    norm_binder = normalize_string(binder_classification)
    norm_art = normalize_string(artifact_type)

    return f"corr:{norm_study}:{norm_site}:{norm_binder}:{norm_art}"


def classify_incoming_document(
    incoming_checksum: str,
    existing_documents_for_key: list[Any],
) -> tuple[DocumentClassification, Any | None]:
    if not existing_documents_for_key:
        return DocumentClassification.NEW, None

    for doc in existing_documents_for_key:
        doc_checksum = getattr(doc, "content_checksum", None)
        if doc_checksum is None and isinstance(doc, dict):
            doc_checksum = doc.get("content_checksum")

        if doc_checksum == incoming_checksum:
            return DocumentClassification.DUPLICATE, doc

    latest_doc = None
    max_version = -1
    for doc in existing_documents_for_key:
        version = getattr(doc, "version_index", None)
        if version is None and isinstance(doc, dict):
            version = doc.get("version_index")

        if version is not None:
            if version > max_version:
                max_version = version
                latest_doc = doc
        else:
            latest_doc = doc

    return DocumentClassification.CHANGED, latest_doc


def classify_eisf_document_local(
    filename: str,
    content: str | None = None,
    binder_hint: str | None = None,
) -> dict[str, Any]:
    """Fallback local classifier for eISF document intelligence when eTMF REST API is unavailable."""
    text_corpus = f"{filename or ''} {content or ''} {binder_hint or ''}".lower()

    has_sig = any(
        term in text_corpus
        for term in ["signature", "/s/", "signed", "signed by", "certification"]
    )
    sig_info = {
        "status": "FULLY_SIGNED" if has_sig else "UNSIGNED",
        "is_complete": has_sig,
        "detected_signatures": 1 if has_sig else 0,
        "missing_roles": [] if has_sig else ["Principal Investigator"],
    }

    if "1572" in text_corpus or "statement of investigator" in text_corpus:
        return {
            "section": "04_REGULATORY",
            "folder": "Regulatory Documents",
            "code": "05.02.01",
            "name": "FDA Form 1572",
            "confidence": 0.95,
            "signature_completeness": sig_info,
        }
    if "license" in text_corpus or "medical board" in text_corpus:
        return {
            "section": "05_STAFF_QUALIFICATIONS",
            "folder": "Staff Qualifications",
            "code": "05.02.98",
            "name": "Medical License",
            "confidence": 0.95,
            "signature_completeness": sig_info,
        }
    if "cv" in text_corpus or "curriculum vitae" in text_corpus:
        return {
            "section": "05_STAFF_QUALIFICATIONS",
            "folder": "Staff Qualifications",
            "code": "05.02.03",
            "name": "Investigator CV",
            "confidence": 0.95,
            "signature_completeness": sig_info,
        }
    if "financial" in text_corpus or "disclosure" in text_corpus:
        return {
            "section": "05_STAFF_QUALIFICATIONS",
            "folder": "Staff Qualifications",
            "code": "05.02.02",
            "name": "Financial Disclosure",
            "confidence": 0.95,
            "signature_completeness": sig_info,
        }
    if "delegation" in text_corpus or "doa" in text_corpus:
        return {
            "section": "05_STAFF_QUALIFICATIONS",
            "folder": "Staff Qualifications",
            "code": "05.02.04",
            "name": "Delegation of Authority Log",
            "confidence": 0.95,
            "signature_completeness": sig_info,
        }
    if "irb" in text_corpus or "iec" in text_corpus or "ethics" in text_corpus:
        return {
            "section": "04_REGULATORY",
            "folder": "Regulatory Documents",
            "code": "04.01.01",
            "name": "IRB/IEC Approval",
            "confidence": 0.95,
            "signature_completeness": sig_info,
        }
    if "protocol" in text_corpus:
        return {
            "section": "01_TRIAL_MANAGEMENT",
            "folder": "Protocols & Amendments",
            "code": "01.01.01",
            "name": "Clinical Trial Protocol",
            "confidence": 0.95,
            "signature_completeness": sig_info,
        }

    return {
        "section": binder_hint or "04_REGULATORY",
        "folder": "Regulatory Documents",
        "code": "04.01.01",
        "name": "Regulatory Document",
        "confidence": 0.85,
        "signature_completeness": sig_info,
    }
