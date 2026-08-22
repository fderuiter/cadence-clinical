"""Domain utilities for FHIR patient de-identification and HMAC-SHA256 pseudonymization.

Requirements: PRD-CRF-007, PRD-SYS-001
"""

import hashlib
import hmac
import os
from typing import Any

from packages.deid.detector import DeidDetector
from packages.deid.models import ComplianceProfile
from packages.deid.transforms import apply_deid_transforms


def deidentify_free_text(
    text: str,
    profile: ComplianceProfile = ComplianceProfile.HIPAA,
    custom_terms: list[str] | None = None,
) -> str:
    """De-identify free text using HIPAA/GDPR compliance profiles and custom literal terms."""
    detector = DeidDetector()
    results = detector.detect(text, profile=profile, custom_terms=custom_terms)
    redacted_text, _ = apply_deid_transforms(text, results, default_strategy="mask")
    return redacted_text


def pseudonymize_identifier(identifier: str) -> str:
    """Create deterministic irreversible HMAC-SHA256 pseudonym for an identifier."""
    salt = os.getenv("PSEUDONYMIZATION_SALT", default="secure-clinical-salt-98765")
    return hmac.new(salt.encode(), identifier.encode(), hashlib.sha256).hexdigest()


def strip_pii_from_patient(patient_resource: dict[str, Any]) -> dict[str, Any]:
    """Strip direct identifiers from a FHIR Patient resource and pseudonymize IDs."""
    custom_terms = []
    names = patient_resource.get("name", [])
    if isinstance(names, list):
        for name in names:
            if isinstance(name, dict):
                givens = name.get("given", [])
                if isinstance(givens, list):
                    for g in givens:
                        if isinstance(g, str) and g:
                            custom_terms.append(g)
                family = name.get("family", "")
                if isinstance(family, str) and family:
                    custom_terms.append(family)
                if family and givens:
                    full_name = " ".join([str(g) for g in givens if g]) + " " + family
                    custom_terms.append(full_name)

    stripped = patient_resource.copy()
    pii_keys = [
        "name",
        "telecom",
        "address",
        "photo",
        "contact",
        "multipleBirthBoolean",
        "multipleBirthInteger",
        "communication",
    ]
    for key in pii_keys:
        stripped.pop(key, None)

    if "text" in stripped and isinstance(stripped["text"], dict):
        div_text = stripped["text"].get("div", "")
        if div_text:
            stripped["text"]["div"] = deidentify_free_text(
                div_text, ComplianceProfile.HIPAA, custom_terms=custom_terms
            )

    if "note" in stripped:
        if isinstance(stripped["note"], list):
            new_notes = []
            for n in stripped["note"]:
                if isinstance(n, dict) and "text" in n:
                    n_copy = n.copy()
                    n_copy["text"] = deidentify_free_text(
                        n["text"], ComplianceProfile.HIPAA, custom_terms=custom_terms
                    )
                    new_notes.append(n_copy)
                elif isinstance(n, str):
                    new_notes.append(
                        deidentify_free_text(
                            n, ComplianceProfile.HIPAA, custom_terms=custom_terms
                        )
                    )
                else:
                    new_notes.append(n)
            stripped["note"] = new_notes
        elif isinstance(stripped["note"], str):
            stripped["note"] = deidentify_free_text(
                stripped["note"], ComplianceProfile.HIPAA, custom_terms=custom_terms
            )

    orig_id = stripped.get("id", "unknown_id")
    stripped["id"] = pseudonymize_identifier(orig_id)

    if "identifier" in stripped and isinstance(stripped["identifier"], list):
        new_identifiers = []
        for ident in stripped["identifier"]:
            if isinstance(ident, dict):
                ident_copy = ident.copy()
                if "value" in ident_copy:
                    ident_copy["value"] = pseudonymize_identifier(
                        str(ident_copy["value"])
                    )
                new_identifiers.append(ident_copy)
        stripped["identifier"] = new_identifiers

    return stripped
