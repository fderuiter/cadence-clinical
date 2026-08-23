"""DDF Semantic Intermediate Representation (IR) Builder Stage.

Transforms normalized CDISC USDM payloads into strongly-typed DDFStudyIR
and DDFItemIR intermediate representation models.
"""

from __future__ import annotations

from typing import Any

from packages.ddf.models import DDFItemIR, DDFStudyIR
from packages.ddf.rules.compiler import (
    compile_condition_to_xpath,
    extract_appearance,
    sanitize_identifier,
)


class DDFSemanticIRBuilderStage:
    """Builds typed Intermediate Representation (IR) from normalized USDM study definitions."""

    def build_ir(self, study_id: str, payload: dict[str, Any]) -> DDFStudyIR:
        """Transforms a normalized USDM payload into a strongly-typed DDFStudyIR.

        Args:
            study_id: The unique identifier of the source study.
            payload: The normalized USDM protocol dictionary.

        Returns:
            DDFStudyIR: The typed intermediate representation.
        """
        # 1. Parse study definition documents from all valid locations
        docs: list[dict[str, Any]] = []
        if "documentedBy" in payload and isinstance(payload["documentedBy"], list):
            docs.extend(payload["documentedBy"])
        if "study" in payload and isinstance(payload.get("study"), dict):
            if "documentedBy" in payload["study"] and isinstance(
                payload["study"]["documentedBy"], list
            ):
                docs.extend(payload["study"]["documentedBy"])
        if (
            "protocol" in payload
            and isinstance(payload.get("protocol"), dict)
            and "documentedBy" in payload["protocol"]
            and isinstance(payload["protocol"]["documentedBy"], list)
        ):
            docs.extend(payload["protocol"]["documentedBy"])

        # 2. Process items for templates
        raw_items = payload.get("protocol", {}).get("items", [])
        processed_items: list[DDFItemIR] = []

        for item in raw_items:
            item_id = sanitize_identifier(item.get("id"))
            item_name = item.get("name", "Unknown Field")
            item_type = item.get("type", "string")
            appearance = extract_appearance(item)

            # Parse and compile rules
            relevants: list[str] = []
            constraints: list[str] = []
            constraint_messages: list[str] = []
            item_rules = item.get("rules", [])

            for r in item_rules:
                r_type = r.get("type")
                condition = r.get("condition")
                if not condition:
                    continue

                compiled_xpath = compile_condition_to_xpath(condition)
                if not compiled_xpath:
                    continue

                if r_type == "skip_logic":
                    action = r.get("action", "show")
                    if action == "hide":
                        expr = f"not({compiled_xpath})"
                    else:
                        expr = compiled_xpath
                    relevants.append(expr)

                elif r_type == "constraint":
                    constraints.append(compiled_xpath)
                    msg = r.get("query_message")
                    if msg:
                        constraint_messages.append(msg)

            relevant_expr = (
                " and ".join(f"({r})" if len(relevants) > 1 else r for r in relevants)
                if relevants
                else None
            )
            constraint_expr = (
                " and ".join(
                    f"({c})" if len(constraints) > 1 else c for c in constraints
                )
                if constraints
                else None
            )
            constraint_msg = (
                "; ".join(constraint_messages) if constraint_messages else None
            )

            # Gather localized properties across all languages
            item_localizations: dict[str, dict[str, Any]] = {}
            for doc in docs:
                lang_code = "en"
                lang_obj = doc.get("language")
                if isinstance(lang_obj, dict):
                    lang_code = lang_obj.get("code") or lang_obj.get("decode") or "en"
                elif isinstance(lang_obj, str):
                    lang_code = lang_obj
                lang_code = str(lang_code).lower().strip()

                # Gather narrative_contents and narrative_items for this doc
                narrative_contents = []
                narrative_items = {}
                for ver in doc.get("versions", []):
                    if isinstance(ver, dict):
                        for nc in ver.get("contents", []):
                            if isinstance(nc, dict):
                                narrative_contents.append(nc)
                        for nci in ver.get("narrativeContentItems", []):
                            if isinstance(nci, dict):
                                narrative_items[nci.get("id")] = nci

                # Also collect global/top-level items as fallback
                for ver in payload.get("versions", []) or []:
                    if isinstance(ver, dict):
                        for nci in ver.get("narrativeContentItems", []):
                            if isinstance(nci, dict):
                                narrative_items[nci.get("id")] = nci
                for ver in payload.get("study", {}).get("versions", []) or []:
                    if isinstance(ver, dict):
                        for nci in ver.get("narrativeContentItems", []):
                            if isinstance(nci, dict):
                                narrative_items[nci.get("id")] = nci

                orig_id = item.get("id")
                associated_ncs = []
                for nc in narrative_contents:
                    nc_id = nc.get("id")
                    nc_name = nc.get("name")
                    if (
                        nc_name in (orig_id, item_id)
                        or nc_id in (orig_id, item_id)
                        or (nc_id and orig_id and nc_id.lower() == orig_id.lower())
                        or (nc_name and orig_id and nc_name.lower() == orig_id.lower())
                        or nc_id == f"nc_{orig_id}"
                        or nc_name == f"nc_{orig_id}"
                        or (nc_id and nc_id.lower() == item_id.lower())
                        or (nc_name and nc_name.lower() == item_id.lower())
                    ):
                        associated_ncs.append(nc)

                lbl = None
                desc = None
                hint = None

                hint_ncs = []
                desc_ncs = []
                for nc in narrative_contents:
                    nc_id = nc.get("id", "")
                    nc_name = nc.get("name", "")
                    if ("hint" in nc_id.lower() or "hint" in nc_name.lower()) and (
                        (orig_id and orig_id.lower() in nc_id.lower())
                        or (orig_id and orig_id.lower() in nc_name.lower())
                    ):
                        hint_ncs.append(nc)
                    if ("desc" in nc_id.lower() or "desc" in nc_name.lower()) and (
                        (orig_id and orig_id.lower() in nc_id.lower())
                        or (orig_id and orig_id.lower() in nc_name.lower())
                    ):
                        desc_ncs.append(nc)

                for nc in associated_ncs:
                    if nc.get("sectionTitle"):
                        lbl = nc.get("sectionTitle")

                    content_item_id = nc.get("contentItemId")
                    if content_item_id and content_item_id in narrative_items:
                        desc = narrative_items[content_item_id].get("text")

                    for child_id in nc.get("childIds", []):
                        child_nc = next(
                            (x for x in narrative_contents if x.get("id") == child_id),
                            None,
                        )
                        if child_nc:
                            child_name = child_nc.get("name") or ""
                            child_id_str = child_nc.get("id") or ""
                            c_item_id = child_nc.get("contentItemId")
                            c_text = None
                            if c_item_id and c_item_id in narrative_items:
                                c_text = narrative_items[c_item_id].get("text")

                            val = c_text or child_nc.get("sectionTitle")

                            if (
                                "hint" in child_name.lower()
                                or "hint" in child_id_str.lower()
                            ):
                                hint = val
                            elif (
                                "desc" in child_name.lower()
                                or "desc" in child_id_str.lower()
                                or not desc
                            ):
                                desc = val
                        else:
                            if child_id in narrative_items:
                                c_item = narrative_items[child_id]
                                c_name = c_item.get("name") or ""
                                c_text = c_item.get("text")
                                if (
                                    "hint" in c_name.lower()
                                    or "hint" in child_id.lower()
                                ):
                                    hint = c_text
                                elif (
                                    "desc" in c_name.lower()
                                    or "desc" in child_id.lower()
                                    or not desc
                                ):
                                    desc = c_text

                if not hint and hint_ncs:
                    first_hint_nc = hint_ncs[0]
                    c_item_id = first_hint_nc.get("contentItemId")
                    hint = (
                        narrative_items[c_item_id].get("text")
                        if c_item_id and c_item_id in narrative_items
                        else None
                    ) or first_hint_nc.get("sectionTitle")

                if not desc and desc_ncs:
                    first_desc_nc = desc_ncs[0]
                    c_item_id = first_desc_nc.get("contentItemId")
                    desc = (
                        narrative_items[c_item_id].get("text")
                        if c_item_id and c_item_id in narrative_items
                        else None
                    ) or first_desc_nc.get("sectionTitle")

                if not lbl:
                    lbl = item_name

                if lbl or desc or hint:
                    item_localizations[lang_code] = {
                        "label": lbl,
                        "description": desc,
                        "hint": hint,
                    }

            if not item_localizations:
                item_localizations["en"] = {
                    "label": item_name,
                    "description": item_name,
                    "hint": None,
                }

            processed_items.append(
                DDFItemIR(
                    id=item_id,
                    name=item_name,
                    type=item_type,
                    appearance=appearance,
                    relevant=relevant_expr,
                    constraint=constraint_expr,
                    constraint_message=constraint_msg,
                    localizations=item_localizations,
                )
            )

        return DDFStudyIR(
            study_id=study_id,
            name=payload.get("name", f"Study {study_id}"),
            items=processed_items,
        )
