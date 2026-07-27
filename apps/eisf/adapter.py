"""
eISF-to-eTMF regulatory-artifact mapping and deterministic cross-system deduplication module.

This module maps eISF binder classifications and artifact types to corresponding eTMF DIA Reference
Model (v3.2.0) zones, sections, artifact types, and codes, and supports reverse mapping. It also
provides a stable mechanism for generating deterministic, cross-system correlation keys and
classifying incoming documents for deduplication and update tracking.
"""

from enum import Enum
from typing import Any, Dict, List, Optional, Tuple


class DocumentClassification(str, Enum):
    """
    Standard classification options for incoming documents.
    """

    NEW = "NEW"
    CHANGED = "CHANGED"
    DUPLICATE = "DUPLICATE"


# Bidirectional mapping definitions for supported eISF required binder artifacts
# Format: (eisf_binder_classification, eisf_artifact_type) -> (etmf_zone, etmf_section, etmf_artifact_type, etmf_artifact_code)
# Inputs are normalized to lowercase and stripped during lookup.
FORWARD_MAPPING: Dict[Tuple[str, str], Tuple[int, str, str, str]] = {
    ("investigator & staff", "investigator cv"): (
        5,
        "05.02",
        "Investigator CV",
        "05.02.03",  # Custom/Extended DIA Reference Model Code for CV under Investigator Qualification
    ),
    ("investigator & staff", "delegation of authority log"): (
        5,
        "05.02",
        "Delegation of Authority Log",
        "05.02.04",  # Custom/Extended DIA Reference Model Code for DOA log
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

# Reverse mapping structure derived to ensure perfect bidirectional consistency.
# Maps (etmf_zone, etmf_section, etmf_artifact_type, etmf_artifact_code) to (eisf_binder_classification, eisf_artifact_type)
# Lookups normalize string components.
REVERSE_MAPPING: Dict[Tuple[int, str, str, str], Tuple[str, str]] = {
    (5, "05.02", "investigator cv", "05.02.03"): (
        "Investigator & Staff",
        "Investigator CV",
    ),
    (5, "05.02", "delegation of authority log", "05.02.04"): (
        "Investigator & Staff",
        "Delegation of Authority Log",
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
    """
    Helper function to normalize input strings for robust case-insensitive lookup.

    Args:
        value (str): The input string to normalize.

    Returns:
        str: Lowercased and stripped string, or empty string if input is null.
    """
    if value is None:
        return ""
    return value.strip().lower()


def map_eisf_to_etmf(binder_classification: str, artifact_type: str) -> Dict[str, Any]:
    """
    Map eISF binder classification and artifact type to eTMF zone, section, artifact type, and code.

    Args:
        binder_classification (str): The eISF binder section / classification.
        artifact_type (str): The eISF artifact type.

    Returns:
        Dict[str, Any]: A dictionary containing:
            - 'zone' (int): eTMF Zone code.
            - 'section' (str): eTMF Section code.
            - 'artifact_type' (str): eTMF Artifact type description.
            - 'artifact_code' (str): eTMF Artifact code.

    Raises:
        ValueError: If the combination is not supported.
    """
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
    artifact_code: Optional[str] = None,
) -> Dict[str, str]:
    """
    Map eTMF zone, section, artifact type, and artifact code back to eISF.

    Args:
        zone (int): The eTMF zone code.
        section (str): The eTMF section code.
        artifact_type (str): The eTMF artifact type description.
        artifact_code (Optional[str]): The eTMF artifact code.

    Returns:
        Dict[str, str]: A dictionary containing:
            - 'binder_classification' (str): eISF binder classification / section.
            - 'artifact_type' (str): eISF artifact type.

    Raises:
        ValueError: If the combination is not supported.
    """
    norm_section = normalize_string(section)
    norm_art_type = normalize_string(artifact_type)
    norm_code = normalize_string(artifact_code) if artifact_code else ""

    # Attempt to locate the reverse mapping key
    matched = None
    for (z, s, t, c), val in REVERSE_MAPPING.items():
        if (
            z == zone
            and normalize_string(s) == norm_section
            and normalize_string(t) == norm_art_type
        ):
            # If artifact_code is specified, ensure it matches; if not, treat as match
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
    """
    Derive a stable, deterministic cross-system correlation key from site, study, and artifact identity.

    Normalizes components (lowercased, stripped) to ensure absolute cross-system alignment
    and robustness against formatting deviations.

    Args:
        study_id (str): Clinical study ID.
        site_id (str): Clinical site ID.
        binder_classification (str): eISF binder classification / section.
        artifact_type (str): eISF artifact type.

    Returns:
        str: Stable, human-readable correlation key string in canonical format.
    """
    norm_study = normalize_string(study_id)
    norm_site = normalize_string(site_id)
    norm_binder = normalize_string(binder_classification)
    norm_art = normalize_string(artifact_type)

    return f"corr:{norm_study}:{norm_site}:{norm_binder}:{norm_art}"


def classify_incoming_document(
    incoming_checksum: str,
    existing_documents_for_key: List[Any],
) -> Tuple[DocumentClassification, Optional[Any]]:
    """
    Classify an incoming document compared to existing documents sharing the same correlation key.

    Classification semantics:
    - DUPLICATE: if there is an existing document with the exact same content checksum.
    - CHANGED: if there are existing documents for this correlation key, but none have the same checksum.
    - NEW: if there are no existing documents for this correlation key.

    Args:
        incoming_checksum (str): Checksum of the incoming document content.
        existing_documents_for_key (List[Any]): List of existing document objects (or dictionaries)
                                                with the same correlation key.

    Returns:
        Tuple[DocumentClassification, Optional[Any]]:
            - The classification (NEW, CHANGED, or DUPLICATE)
            - The matching duplicate document if DUPLICATE, or the latest version document
              if CHANGED, or None.
    """
    if not existing_documents_for_key:
        return DocumentClassification.NEW, None

    # Check for an exact duplicate (matching checksum)
    for doc in existing_documents_for_key:
        doc_checksum = getattr(doc, "content_checksum", None)
        if doc_checksum is None and isinstance(doc, dict):
            doc_checksum = doc.get("content_checksum")

        if doc_checksum == incoming_checksum:
            return DocumentClassification.DUPLICATE, doc

    # If we have existing documents for this key but none matched the checksum,
    # it's a CHANGED document (new version). Resolve the latest version.
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
