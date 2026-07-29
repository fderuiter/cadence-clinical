"""
USDM-to-render-model content assembly engine.

This module consumes canonical mapped/validated USDM study representations and produces
ordered narrative sections, synopsis data, and an SoA matrix using the shared render view models.
It resolves usdm:ref markup recursively, validates display metadata, and enforces strict uniqueness.
"""

from typing import Any, Dict, List, Set

import usdm_model
from bs4 import BeautifulSoup
from protocol_render import (
    ExportMetadata,
    NarrativeItemView,
    NarrativeSectionView,
    RenderedProtocolDocument,
    SoACellView,
    SoAHeaderEncounter,
    SoAHeaderEpoch,
    SoAMatrixView,
    SoARowView,
    SynopsisView,
)
from pydantic import BaseModel


def find_object_by_id(obj: Any, target_id: str, target_klass: str = None) -> Any:
    """
    Recursively search the USDM Pydantic model tree to find an object
    with the specified ID and optionally matching class name.
    """
    # Check if the object itself has an 'id' field matching the target
    if hasattr(obj, "id") and obj.id is not None:
        if str(obj.id) == str(target_id):
            if target_klass is None or obj.__class__.__name__ == target_klass:
                return obj

    # If the object is a Pydantic model, traverse its fields
    if isinstance(obj, BaseModel):
        for field_name in obj.__class__.model_fields:
            val = getattr(obj, field_name, None)
            if val is not None:
                res = find_object_by_id(val, target_id, target_klass)
                if res is not None:
                    return res

    # If the object is a list or tuple, traverse its items
    elif isinstance(obj, (list, tuple)):
        for item in obj:
            res = find_object_by_id(item, target_id, target_klass)
            if res is not None:
                return res

    # If the object is a dictionary, traverse its values
    elif isinstance(obj, dict):
        for val in obj.values():
            res = find_object_by_id(val, target_id, target_klass)
            if res is not None:
                return res

    return None


def stringify_attribute_value(val: Any) -> str:
    """
    Converts a USDM attribute value (possibly complex like Code or AliasCode)
    to a human-readable string.
    """
    if val is None:
        return ""
    if isinstance(val, str):
        return val

    # Handle Pydantic models
    if isinstance(val, BaseModel):
        klass_name = val.__class__.__name__
        if klass_name == "Code":
            return getattr(val, "decode", getattr(val, "code", ""))
        if klass_name == "AliasCode" and hasattr(val, "standardCode"):
            std_code = getattr(val, "standardCode")
            if std_code:
                return getattr(std_code, "decode", getattr(std_code, "code", ""))
        if klass_name == "Quantity":
            unit_str = ""
            if hasattr(val, "unit") and val.unit:
                unit_str = " " + stringify_attribute_value(val.unit)
            return f"{getattr(val, 'value')}{unit_str}"

        # Fallback fields
        for attr in ("text", "name", "value", "decode", "code", "description"):
            if hasattr(val, attr):
                cand = getattr(val, attr)
                if cand is not None:
                    return str(cand)
        return str(val)

    return str(val)


def resolve_text_references(text: str, study: usdm_model.Study) -> str:
    """
    Parses narrative text, identifies <usdm:ref> markup elements,
    and replaces them with resolved attribute values from the referenced entities.
    Raises ValueError for unresolved references.
    """
    if not text:
        return ""

    soup = BeautifulSoup(text, "html.parser")
    ref_tags = soup.find_all("usdm:ref")

    for tag in ref_tags:
        klass = tag.get("klass")
        ref_id = tag.get("id")
        attribute = tag.get("attribute")

        if not ref_id or not attribute:
            raise ValueError(
                "Every <usdm:ref> tag must specify 'id' and 'attribute' attributes."
            )

        resolved_obj = find_object_by_id(study, ref_id, klass)
        if resolved_obj is None:
            raise ValueError(
                f"Unresolved USDM reference: Object with ID '{ref_id}'"
                f"{f' of class {klass}' if klass else ''} could not be found in the Study."
            )

        if not hasattr(resolved_obj, attribute):
            raise ValueError(
                f"Unresolved USDM reference attribute: Object '{ref_id}' of class "
                f"'{resolved_obj.__class__.__name__}' does not have the attribute '{attribute}'."
            )

        raw_val = getattr(resolved_obj, attribute)
        resolved_str = stringify_attribute_value(raw_val)

        tag.replace_with(resolved_str)

    return str(soup)


def assemble_narrative_sections(
    study: usdm_model.Study,
) -> List[NarrativeSectionView]:
    """
    Assembles narrative sections and subsections from the USDM Study object.
    Enforces narrative display rules and unique section numbering.
    """
    narrative_content_map: Dict[str, usdm_model.NarrativeContent] = {}
    narrative_item_map: Dict[str, usdm_model.NarrativeContentItem] = {}

    # 1. Collect all NarrativeContent from documentedBy
    if study.documentedBy:
        for doc in study.documentedBy:
            if doc.versions:
                for doc_ver in doc.versions:
                    if doc_ver.contents:
                        for nc in doc_ver.contents:
                            narrative_content_map[nc.id] = nc

    # 2. Collect all NarrativeContentItem from Study Versions
    if study.versions:
        for std_ver in study.versions:
            if std_ver.narrativeContentItems:
                for item in std_ver.narrativeContentItems:
                    narrative_item_map[item.id] = item

    # 3. Identify Root Sections (NarrativeContent not nested inside any other childIds)
    child_ids_set: Set[str] = set()
    for nc in narrative_content_map.values():
        if nc.childIds:
            child_ids_set.update(nc.childIds)

    # Roots should preserve their original documentedBy ordering
    root_sections_ordered: List[usdm_model.NarrativeContent] = []
    if study.documentedBy:
        for doc in study.documentedBy:
            if doc.versions:
                for doc_ver in doc.versions:
                    if doc_ver.contents:
                        for nc in doc_ver.contents:
                            if nc.id not in child_ids_set:
                                root_sections_ordered.append(nc)

    displayed_section_numbers: Set[str] = set()

    def build_section(
        nc: usdm_model.NarrativeContent, order_in_parent: int
    ) -> NarrativeSectionView:
        # Enforce Display Rules
        if nc.displaySectionNumber:
            sec_num = nc.sectionNumber
            if not sec_num or not sec_num.strip():
                raise ValueError(
                    f"Section '{nc.id}' ({nc.name}) is configured to display its section number, "
                    "but 'sectionNumber' is missing or empty."
                )
            if sec_num in displayed_section_numbers:
                raise ValueError(
                    f"Duplicate displayed section number: '{sec_num}' found on section '{nc.id}'."
                )
            displayed_section_numbers.add(sec_num)

        subsections: List[NarrativeSectionView] = []
        items: List[NarrativeItemView] = []
        child_order = 1

        # Process direct content item if contentItemId exists
        if nc.contentItemId:
            cid = nc.contentItemId
            if cid in narrative_item_map:
                item_obj = narrative_item_map[cid]
                resolved_text = resolve_text_references(item_obj.text, study)
                items.append(
                    NarrativeItemView(
                        id=item_obj.id,
                        name=item_obj.name,
                        text=resolved_text,
                        order=child_order,
                    )
                )
                child_order += 1

        # Process nested children ordered deterministically
        if nc.childIds:
            for cid in nc.childIds:
                if cid in narrative_content_map:
                    sub_nc = narrative_content_map[cid]
                    subsections.append(build_section(sub_nc, child_order))
                    child_order += 1
                elif cid in narrative_item_map:
                    item_obj = narrative_item_map[cid]
                    resolved_text = resolve_text_references(item_obj.text, study)
                    items.append(
                        NarrativeItemView(
                            id=item_obj.id,
                            name=item_obj.name,
                            text=resolved_text,
                            order=child_order,
                        )
                    )
                    child_order += 1
                else:
                    raise ValueError(
                        f"Unresolvable child ID '{cid}' specified in section '{nc.id}'."
                    )

        title = resolve_text_references(nc.sectionTitle or nc.name or "", study)
        derived_from_soa = getattr(nc, "derived_from_soa", False) or getattr(
            nc, "_derived_from_soa", False
        )
        if isinstance(derived_from_soa, str):
            derived_from_soa = derived_from_soa.lower() in ("true", "1")
        return NarrativeSectionView(
            section_id=nc.id,
            section_number=nc.sectionNumber,
            title=title,
            items=items,
            subsections=subsections,
            order=order_in_parent,
            derived_from_soa=bool(derived_from_soa),
        )

    assembled_roots: List[NarrativeSectionView] = []
    for idx, root_nc in enumerate(root_sections_ordered):
        assembled_roots.append(build_section(root_nc, idx + 1))

    return assembled_roots


def assemble_synopsis(study: usdm_model.Study) -> SynopsisView:
    """
    Assembles SynopsisView from the canonical USDM Study object.
    """
    study_id = str(study.id) if study.id else "unknown"
    protocol_title = ""
    protocol_number = None
    sponsor_name = None
    phase = None
    objectives = []
    study_design_type = None
    population = None
    sample_size = None
    duration = None
    interventions = []

    if study.versions:
        # We can extract details from versions, fallback to first matching or combination
        for v in study.versions:
            # Protocol Title
            if v.titles and not protocol_title:
                for t in v.titles:
                    if hasattr(t, "text") and t.text:
                        protocol_title = t.text
                        break

            # Protocol Number (from StudyIdentifier)
            if v.studyIdentifiers and not protocol_number:
                for ident in v.studyIdentifiers:
                    if hasattr(ident, "text") and ident.text:
                        protocol_number = ident.text
                        break

            # Sponsor Name (from Organization)
            if v.organizations and not sponsor_name:
                for org in v.organizations:
                    org_type = getattr(org, "type", None)
                    if org_type:
                        type_decode = getattr(org_type, "decode", "").lower()
                        type_code = getattr(org_type, "code", "").lower()
                        if "sponsor" in type_decode or "sponsor" in type_code:
                            sponsor_name = org.name
                            break
                if not sponsor_name and v.organizations:
                    sponsor_name = v.organizations[0].name

            # Study Design Specific Fields
            if v.studyDesigns:
                for design in v.studyDesigns:
                    # Phase
                    if (
                        hasattr(design, "studyPhase")
                        and design.studyPhase
                        and not phase
                    ):
                        phase_obj = design.studyPhase
                        if (
                            hasattr(phase_obj, "standardCode")
                            and phase_obj.standardCode
                        ):
                            phase = getattr(
                                phase_obj.standardCode,
                                "decode",
                                getattr(phase_obj.standardCode, "code", None),
                            )
                        else:
                            phase = getattr(
                                phase_obj, "decode", getattr(phase_obj, "code", None)
                            )

                    # Objectives
                    if design.objectives:
                        for obj in design.objectives:
                            if hasattr(obj, "text") and obj.text:
                                objectives.append(obj.text)
                            elif hasattr(obj, "description") and obj.description:
                                objectives.append(obj.description)

                    # Study Design Type
                    if (
                        hasattr(design, "studyType")
                        and design.studyType
                        and not study_design_type
                    ):
                        st_obj = design.studyType
                        study_design_type = getattr(
                            st_obj, "decode", getattr(st_obj, "code", None)
                        )

                    # Population Description
                    if (
                        hasattr(design, "population")
                        and design.population
                        and not population
                    ):
                        pop_obj = design.population
                        population = getattr(
                            pop_obj, "description", getattr(pop_obj, "name", None)
                        )

                    # Sample Size
                    if (
                        hasattr(design, "population")
                        and design.population
                        and sample_size is None
                    ):
                        pop_obj = design.population
                        if (
                            hasattr(pop_obj, "plannedEnrollmentNumber")
                            and pop_obj.plannedEnrollmentNumber
                        ):
                            pen = pop_obj.plannedEnrollmentNumber
                            if pen.__class__.__name__ == "Quantity":
                                sample_size = int(getattr(pen, "value", 0))
                            elif pen.__class__.__name__ == "Range":
                                max_v = getattr(pen, "maxValue", None)
                                if max_v:
                                    sample_size = int(getattr(max_v, "value", 0))

                    # Duration
                    if (
                        hasattr(design, "scheduleTimelines")
                        and design.scheduleTimelines
                        and not duration
                    ):
                        for st in design.scheduleTimelines:
                            pd = getattr(st, "plannedDuration", None)
                            if pd:
                                qty_obj = getattr(pd, "quantity", None) or pd
                                val = getattr(qty_obj, "value", None)
                                unit_obj = getattr(qty_obj, "unit", None)
                                unit_str = ""
                                if unit_obj:
                                    if (
                                        hasattr(unit_obj, "standardCode")
                                        and unit_obj.standardCode
                                    ):
                                        unit_str = getattr(
                                            unit_obj.standardCode,
                                            "decode",
                                            getattr(unit_obj.standardCode, "code", ""),
                                        )
                                    else:
                                        unit_str = getattr(
                                            unit_obj,
                                            "decode",
                                            getattr(unit_obj, "code", ""),
                                        )
                                if val is not None:
                                    duration = f"{val} {unit_str}".strip()
                                    break

            # Interventions
            if v.studyInterventions:
                for inter in v.studyInterventions:
                    if hasattr(inter, "name") and inter.name:
                        interventions.append(inter.name)

    if not protocol_title:
        protocol_title = study.name or "Untitled Study"

    return SynopsisView(
        study_id=study_id,
        protocol_title=protocol_title,
        protocol_number=protocol_number,
        sponsor_name=sponsor_name,
        phase=phase,
        objectives=objectives,
        study_design_type=study_design_type,
        population=population,
        sample_size=sample_size,
        duration=duration,
        interventions=interventions,
    )


def assemble_soa_matrix(study: usdm_model.Study) -> SoAMatrixView:
    """
    Assembles Schedule of Activities (SoAMatrixView) from the USDM Study object.
    """
    epochs_list: List[SoAHeaderEpoch] = []
    encounters_list: List[SoAHeaderEncounter] = []
    rows_list: List[SoARowView] = []

    # Map for encounter -> epoch relationship and cell-applicability mappings
    encounter_to_epoch: Dict[str, str] = {}
    encounter_epoch_activities: Dict[tuple, Set[str]] = {}
    encounter_epoch_details: Dict[tuple, str] = {}

    # Unique identifiers trackers to avoid duplicates
    epoch_ids_added: Set[str] = set()
    encounter_ids_added: Set[str] = set()
    activity_ids_added: Set[str] = set()

    # 1. First build mappings from schedule timelines & activity instances
    if study.versions:
        for v in study.versions:
            if v.studyDesigns:
                for design in v.studyDesigns:
                    if design.scheduleTimelines:
                        for timeline in design.scheduleTimelines:
                            if timeline.instances:
                                for inst in timeline.instances:
                                    if (
                                        inst.__class__.__name__
                                        == "ScheduledActivityInstance"
                                    ):
                                        enc_id = getattr(inst, "encounterId", None)
                                        ep_id = getattr(inst, "epochId", None)
                                        act_ids = getattr(inst, "activityIds", [])
                                        details_str = getattr(
                                            inst,
                                            "description",
                                            getattr(inst, "label", None),
                                        )

                                        if enc_id and ep_id:
                                            encounter_to_epoch[enc_id] = ep_id
                                            key = (enc_id, ep_id)
                                            if key not in encounter_epoch_activities:
                                                encounter_epoch_activities[key] = set()
                                            for aid in act_ids:
                                                encounter_epoch_activities[key].add(aid)
                                                if details_str:
                                                    encounter_epoch_details[
                                                        (enc_id, ep_id, aid)
                                                    ] = details_str

    # 2. Extract and format Study Epochs
    if study.versions:
        for v in study.versions:
            if v.studyDesigns:
                for design in v.studyDesigns:
                    if design.epochs:
                        for idx, ep in enumerate(design.epochs):
                            if ep.id not in epoch_ids_added:
                                epochs_list.append(
                                    SoAHeaderEpoch(
                                        epoch_id=ep.id,
                                        epoch_name=ep.name
                                        or ep.label
                                        or f"Epoch {idx + 1}",
                                        sequence=idx + 1,
                                    )
                                )
                                epoch_ids_added.add(ep.id)

    default_epoch_id = epochs_list[0].epoch_id if epochs_list else ""

    # 3. Extract and format Encounters
    if study.versions:
        for v in study.versions:
            if v.studyDesigns:
                for design in v.studyDesigns:
                    if design.encounters:
                        for idx, enc in enumerate(design.encounters):
                            if enc.id not in encounter_ids_added:
                                ep_id = (
                                    encounter_to_epoch.get(enc.id) or default_epoch_id
                                )
                                encounters_list.append(
                                    SoAHeaderEncounter(
                                        encounter_id=enc.id,
                                        encounter_name=enc.name
                                        or enc.label
                                        or f"Visit {idx + 1}",
                                        epoch_id=ep_id,
                                        sequence=idx + 1,
                                    )
                                )
                                encounter_ids_added.add(enc.id)

    # 4. Extract Row Activities and build applicable cells
    if study.versions:
        for v in study.versions:
            if v.studyDesigns:
                for design in v.studyDesigns:
                    if design.activities:
                        for act in design.activities:
                            if act.id not in activity_ids_added:
                                act_id = act.id
                                act_name = act.name or act.label or f"Activity {act_id}"

                                cells = []
                                for enc_header in encounters_list:
                                    enc_id = enc_header.encounter_id
                                    ep_id = enc_header.epoch_id

                                    is_applicable = False
                                    details_val = None

                                    # Check applicability
                                    key = (enc_id, ep_id)
                                    if key in encounter_epoch_activities:
                                        if act_id in encounter_epoch_activities[key]:
                                            is_applicable = True
                                            details_val = encounter_epoch_details.get(
                                                (enc_id, ep_id, act_id)
                                            )

                                    derived_flag = getattr(
                                        act, "derived_from_soa", False
                                    ) or getattr(act, "_derived_from_soa", False)
                                    cells.append(
                                        SoACellView(
                                            activity_id=act_id,
                                            encounter_id=enc_id,
                                            epoch_id=ep_id,
                                            is_applicable=is_applicable,
                                            details=details_val,
                                            derived_from_soa=bool(derived_flag),
                                        )
                                    )

                                rows_list.append(
                                    SoARowView(
                                        activity_id=act_id,
                                        activity_name=act_name,
                                        cells=cells,
                                        derived_from_soa=bool(derived_flag),
                                    )
                                )
                                activity_ids_added.add(act_id)

    return SoAMatrixView(
        epochs=epochs_list,
        encounters=encounters_list,
        rows=rows_list,
    )


def assemble_rendered_protocol_document(
    study: usdm_model.Study,
    creator: str,
    change_reason: str = None,
    version_index: int = 1,
) -> RenderedProtocolDocument:
    """
    Main orchestrator to assemble the full RenderedProtocolDocument from a USDM study representation.
    """
    metadata = ExportMetadata(
        creator=creator,
        change_reason=change_reason,
        version_index=version_index,
    )
    synopsis = assemble_synopsis(study)
    narrative_sections = assemble_narrative_sections(study)
    soa_matrix = assemble_soa_matrix(study)

    return RenderedProtocolDocument(
        metadata=metadata,
        synopsis=synopsis,
        narrative_sections=narrative_sections,
        soa_matrix=soa_matrix,
        source_study=study,
    )
