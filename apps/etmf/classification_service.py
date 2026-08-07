from pydantic import BaseModel

from apps.etmf.src.domain.tmf_reference_model import (
    get_active_catalog,
    get_catalog,
    resolve_artifact,
    validate_hierarchy,
)

# Unified Alias Mapping
ALIAS_MAP: dict[str, tuple[str, str]] = {
    "FORM_1572": ("05.02.01", "FDA Form 1572"),
    "FDA Form 1572": ("05.02.01", "FDA Form 1572"),
    "form 1572": ("05.02.01", "FDA Form 1572"),
    "FINANCIAL_DISCLOSURE": ("05.02.02", "Financial Disclosure"),
    "Financial Disclosure": ("05.02.02", "Financial Disclosure"),
    "financial disclosure": ("05.02.02", "Financial Disclosure"),
    "PROTOCOL_SIGNOFF": ("01.01.03", "Protocol Sign-off"),
    "Protocol Sign-off": ("01.01.03", "Protocol Sign-off"),
    "protocol sign-off": ("01.01.03", "Protocol Sign-off"),
    "protocol signoff": ("01.01.03", "Protocol Sign-off"),
}

_LEGACY_MAPPING = {
    "FORM_1572": "FORM_1572",
    "FDA Form 1572": "FORM_1572",
    "form 1572": "FORM_1572",
    "FINANCIAL_DISCLOSURE": "FINANCIAL_DISCLOSURE",
    "Financial Disclosure": "FINANCIAL_DISCLOSURE",
    "financial disclosure": "FINANCIAL_DISCLOSURE",
    "PROTOCOL_SIGNOFF": "PROTOCOL_SIGNOFF",
    "Protocol Sign-off": "PROTOCOL_SIGNOFF",
    "protocol sign-off": "PROTOCOL_SIGNOFF",
    "protocol signoff": "PROTOCOL_SIGNOFF",
}

REVERSE_DOCUMENT_TYPE_MAP: dict[str, str] = {
    code: _LEGACY_MAPPING[tag]
    for tag, (code, _) in ALIAS_MAP.items()
    if tag in _LEGACY_MAPPING
}


def resolve_document_type(artifact_code: str) -> str | None:
    """
    Returns the legacy tag ("FORM_1572", "FINANCIAL_DISCLOSURE", "PROTOCOL_SIGNOFF")
    for a resolved artifact code, and None when no legacy tag applies.
    """
    return REVERSE_DOCUMENT_TYPE_MAP.get(artifact_code)


class ClassificationResult(BaseModel):
    """
    Unified result of a classification/auto-filing attempt.
    """

    resolved_zone: int
    resolved_section: str
    artifact_code: str
    artifact_type: str
    match_basis: str


def normalize_for_comparison(s: str) -> str:
    """
    Normalizes a string by converting to lowercase, replacing underscores and hyphens with spaces.
    """
    return s.lower().replace("_", " ").replace("-", " ")


def classify_tmf_document(
    filename: str,
    artifact_type: str | None = None,
    free_text: str | None = None,
    version: str | None = None,
) -> ClassificationResult | None:
    """
    Side-effect-free classifier that resolves a document to its DIA TMF taxonomy.

    Checks in sequence:
    1. If artifact_type is provided, resolves by code, name, or alias.
    2. If filename is provided, checks for code, alias, or name matches.
    3. If free_text is provided, checks for code, alias, or name matches.

    Returns ClassificationResult if resolved, or None if unresolved.
    """
    # 1. Resolve active catalog version
    tax_version = version or get_active_catalog().version
    try:
        catalog = get_catalog(tax_version)
    except KeyError:
        return None

    # Helper to resolve artifact safely and return a ClassificationResult
    def create_result(art_code: str, basis: str) -> ClassificationResult | None:
        try:
            res = resolve_artifact(tax_version, code=art_code)
            # Perform defensive hierarchy validation
            validate_hierarchy(
                version=tax_version,
                zone_code=res["zone"].code,
                section_code=res["section"].code,
                artifact_code=res["artifact"].code,
            )
            return ClassificationResult(
                resolved_zone=res["zone"].code,
                resolved_section=res["section"].code,
                artifact_code=res["artifact"].code,
                artifact_type=res["artifact"].name,
                match_basis=basis,
            )
        except Exception:
            return None

    # Step 1: Resolve by artifact_type hint
    if artifact_type:
        cleaned_type = artifact_type.strip()
        # Check alias map
        if cleaned_type in ALIAS_MAP:
            code_alias, _ = ALIAS_MAP[cleaned_type]
            res = create_result(code_alias, "artifact_type_hint")
            if res:
                return res
        for alias_key, (c_code, _) in ALIAS_MAP.items():
            if normalize_for_comparison(alias_key) == normalize_for_comparison(
                cleaned_type
            ):
                res = create_result(c_code, "artifact_type_hint")
                if res:
                    return res

        # Check if is a code input
        is_code = cleaned_type.replace(".", "").isdigit()
        if is_code:
            res = create_result(cleaned_type, "artifact_type_hint")
            if res:
                return res
        else:
            try:
                resolved = resolve_artifact(tax_version, name=cleaned_type)
                res = create_result(resolved["artifact"].code, "artifact_type_hint")
                if res:
                    return res
            except Exception:
                pass

    # Step 2: Resolve by filename matches
    if filename:
        filename_norm = normalize_for_comparison(filename)
        # Code match in filename
        for code in catalog.artifact_map:
            if code in filename_norm:
                res = create_result(code, "filename_match")
                if res:
                    return res
        # Alias match in filename
        for alias_key, (c_code, _) in ALIAS_MAP.items():
            if normalize_for_comparison(alias_key) in filename_norm:
                res = create_result(c_code, "filename_match")
                if res:
                    return res
        # Substring match on artifact names (longer first to be specific)
        sorted_artifacts = sorted(
            catalog.artifact_map.values(), key=lambda a: len(a.name), reverse=True
        )
        for art in sorted_artifacts:
            if normalize_for_comparison(art.name) in filename_norm:
                res = create_result(art.code, "filename_match")
                if res:
                    return res

    # Step 3: Resolve by free_text hint matches
    if free_text:
        text_norm = normalize_for_comparison(free_text)
        # Code match in free_text
        for code in catalog.artifact_map:
            if code in text_norm:
                res = create_result(code, "free_text_match")
                if res:
                    return res
        # Alias match in free_text
        for alias_key, (c_code, _) in ALIAS_MAP.items():
            if normalize_for_comparison(alias_key) in text_norm:
                res = create_result(c_code, "free_text_match")
                if res:
                    return res
        # Substring match on artifact names (longer first)
        sorted_artifacts = sorted(
            catalog.artifact_map.values(), key=lambda a: len(a.name), reverse=True
        )
        for art in sorted_artifacts:
            if normalize_for_comparison(art.name) in text_norm:
                res = create_result(art.code, "free_text_match")
                if res:
                    return res

    return None
