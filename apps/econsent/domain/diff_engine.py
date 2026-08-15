"""Domain diff engine for eConsent template and clause amendments.

Performs visual and semantic delta analysis between template versions,
determining substantive changes and calculating re-consent requirements.
"""

import difflib
from dataclasses import dataclass
from typing import Any

SUBSTANTIVE_KEYWORDS = {
    "risk",
    "benefit",
    "adverse",
    "safety",
    "procedure",
    "dose",
    "treatment",
    "alternative",
    "confidentiality",
    "withdraw",
    "voluntary",
    "compensation",
    "injury",
    "dna",
    "genetic",
    "sample",
    "biobank",
}


@dataclass
class ClauseDiffResult:
    """Represents the diff of an individual clause between versions."""

    clause_id: str
    change_type: str  # "ADDED", "REMOVED", "MODIFIED", "UNCHANGED"
    old_title: str | None
    new_title: str | None
    old_text: str | None
    new_text: str | None
    text_diff: str | None
    is_substantive: bool


@dataclass
class TemplateDiffReport:
    """Represents a complete comparison between two versions of a consent template."""

    template_id: str
    base_version_index: int
    target_version_index: int
    clause_diffs: list[ClauseDiffResult]
    total_added: int
    total_removed: int
    total_modified: int
    total_unchanged: int
    requires_reconsent: bool
    substantive_summary: list[str]


def compute_text_diff(old_text: str, new_text: str) -> str:
    """Generates unified visual diff string between two text blocks."""
    old_lines = old_text.splitlines(keepends=True)
    new_lines = new_text.splitlines(keepends=True)
    diff = difflib.unified_diff(
        old_lines,
        new_lines,
        fromfile="Base Version",
        tofile="Amended Version",
        lineterm="",
    )
    return "\n".join(diff)


def is_text_change_substantive(old_text: str, new_text: str) -> bool:
    """Determines whether a textual change involves core clinical risk/procedure terms."""
    old_lower = old_text.lower()
    new_lower = new_text.lower()

    # Significant length change (>15% alteration) is substantive
    len_diff = abs(len(new_text) - len(old_text))
    if len_diff > 30 and (len_diff / max(len(old_text), 1)) > 0.15:
        return True

    # Keyword inspection
    for kw in SUBSTANTIVE_KEYWORDS:
        in_old = kw in old_lower
        in_new = kw in new_lower
        if in_old != in_new:
            return True

    # Check for substantive word replacements
    matcher = difflib.SequenceMatcher(None, old_lower.split(), new_lower.split())
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag in ("replace", "insert", "delete"):
            changed_words = set(old_lower.split()[i1:i2] + new_lower.split()[j1:j2])
            if changed_words & SUBSTANTIVE_KEYWORDS:
                return True

    return False


def compare_templates(
    template_id: str,
    base_version_index: int,
    target_version_index: int,
    base_clauses: list[dict[str, Any]],
    target_clauses: list[dict[str, Any]],
    force_reconsent: bool = False,
) -> TemplateDiffReport:
    """Compares two composed template states and produces a full delta report."""
    base_map = {c["clause_id"]: c for c in base_clauses if "clause_id" in c}
    target_map = {c["clause_id"]: c for c in target_clauses if "clause_id" in c}

    all_clause_ids = list(
        dict.fromkeys(
            [c["clause_id"] for c in base_clauses if "clause_id" in c]
            + [c["clause_id"] for c in target_clauses if "clause_id" in c]
        )
    )

    diffs: list[ClauseDiffResult] = []
    total_added = 0
    total_removed = 0
    total_modified = 0
    total_unchanged = 0
    substantive_summary: list[str] = []

    for cid in all_clause_ids:
        in_base = cid in base_map
        in_target = cid in target_map

        if in_target and not in_base:
            t_clause = target_map[cid]
            is_sub = True  # New clause added is always substantive
            diffs.append(
                ClauseDiffResult(
                    clause_id=cid,
                    change_type="ADDED",
                    old_title=None,
                    new_title=t_clause.get("title"),
                    old_text=None,
                    new_text=t_clause.get("text"),
                    text_diff=compute_text_diff("", t_clause.get("text", "")),
                    is_substantive=is_sub,
                )
            )
            total_added += 1
            substantive_summary.append(f"Added clause '{t_clause.get('title', cid)}'")

        elif in_base and not in_target:
            b_clause = base_map[cid]
            is_sub = True  # Removed clause is always substantive
            diffs.append(
                ClauseDiffResult(
                    clause_id=cid,
                    change_type="REMOVED",
                    old_title=b_clause.get("title"),
                    new_title=None,
                    old_text=b_clause.get("text"),
                    new_text=None,
                    text_diff=compute_text_diff(b_clause.get("text", ""), ""),
                    is_substantive=is_sub,
                )
            )
            total_removed += 1
            substantive_summary.append(f"Removed clause '{b_clause.get('title', cid)}'")

        else:
            b_clause = base_map[cid]
            t_clause = target_map[cid]
            b_text = b_clause.get("text", "")
            t_text = t_clause.get("text", "")
            b_title = b_clause.get("title", "")
            t_title = t_clause.get("title", "")

            if b_text == t_text and b_title == t_title:
                diffs.append(
                    ClauseDiffResult(
                        clause_id=cid,
                        change_type="UNCHANGED",
                        old_title=b_title,
                        new_title=t_title,
                        old_text=b_text,
                        new_text=t_text,
                        text_diff=None,
                        is_substantive=False,
                    )
                )
                total_unchanged += 1
            else:
                is_sub = is_text_change_substantive(b_text, t_text) or (
                    b_title != t_title
                )
                text_diff = compute_text_diff(b_text, t_text)
                diffs.append(
                    ClauseDiffResult(
                        clause_id=cid,
                        change_type="MODIFIED",
                        old_title=b_title,
                        new_title=t_title,
                        old_text=b_text,
                        new_text=t_text,
                        text_diff=text_diff,
                        is_substantive=is_sub,
                    )
                )
                total_modified += 1
                if is_sub:
                    substantive_summary.append(
                        f"Substantive update to clause '{t_title or cid}'"
                    )

    has_substantive = any(d.is_substantive for d in diffs) or force_reconsent
    requires_reconsent = (
        has_substantive or total_added > 0 or total_removed > 0 or force_reconsent
    )

    return TemplateDiffReport(
        template_id=template_id,
        base_version_index=base_version_index,
        target_version_index=target_version_index,
        clause_diffs=diffs,
        total_added=total_added,
        total_removed=total_removed,
        total_modified=total_modified,
        total_unchanged=total_unchanged,
        requires_reconsent=requires_reconsent,
        substantive_summary=substantive_summary,
    )
