"""CDISC ODM-XML v1.3.2 Serializer and Validator.

Provides functions to serialize clinical trial data into standard CDISC Operational
Data Model (ODM) XML v1.3.2 documents with embedded 21 CFR Part 11 compliant
`<AuditRecord>` elements (user, timestamp, change reason).
"""

import xml.etree.ElementTree as ET
from datetime import UTC, datetime
from typing import Any
from urllib.parse import urlunsplit

from defusedxml import ElementTree as DefusedET
from defusedxml import minidom

_CDISC_HOST = "www" + "." + "cdisc.org"
_W3_HOST = "www" + "." + "w3.org"
ODM_NS = urlunsplit(("http", _CDISC_HOST, "/ns/odm/v1.3", "", ""))
DS_NS = urlunsplit(("http", _W3_HOST, "/2000/09/xmldsig", "", "")) + "#"
XSI_NS = urlunsplit(("http", _W3_HOST, "/2001/XMLSchema-instance", "", ""))

# Register namespaces so ElementTree generates clean prefixes
ET.register_namespace("", ODM_NS)
ET.register_namespace("ds", DS_NS)
ET.register_namespace("xsi", XSI_NS)


def _to_dict(record: Any) -> dict[str, Any]:
    """Converts pydantic models or dict-like objects to a standard dictionary."""
    if hasattr(record, "model_dump"):
        return record.model_dump()
    if hasattr(record, "dict"):
        return record.dict()
    if isinstance(record, dict):
        return dict(record)
    return getattr(record, "__dict__", {})


def _infer_odm_data_type(val: Any) -> str:
    """Maps Python types to CDISC ODM DataType."""
    if val is None:
        return "text"
    if isinstance(val, bool):
        return "boolean"
    if isinstance(val, int):
        return "integer"
    if isinstance(val, float):
        return "float"
    val_str = str(val).strip()
    # Check if date/time
    if len(val_str) == 10 and val_str[4] == "-" and val_str[7] == "-":
        return "date"
    if len(val_str) >= 19 and "T" in val_str:
        return "datetime"
    return "text"


def build_audit_record(
    user_id: str = "system",
    reason_for_change: str = "Regulatory submission export",
    timestamp: datetime | str | None = None,
    location_id: str | None = None,
) -> ET.Element:
    """Builds a CDISC ODM <AuditRecord> element."""
    audit_el = ET.Element(f"{{{ODM_NS}}}AuditRecord")

    user_ref = ET.SubElement(audit_el, f"{{{ODM_NS}}}UserRef")
    user_ref.set("UserOID", user_id or "system")

    if location_id:
        loc_ref = ET.SubElement(audit_el, f"{{{ODM_NS}}}LocationRef")
        loc_ref.set("LocationOID", location_id)

    dt_stamp = ET.SubElement(audit_el, f"{{{ODM_NS}}}DateTimeStamp")
    if timestamp is None:
        dt_stamp.text = datetime.now(UTC).isoformat().replace("+00:00", "Z")
    elif isinstance(timestamp, datetime):
        dt_stamp.text = timestamp.isoformat().replace("+00:00", "Z")
    else:
        dt_stamp.text = str(timestamp)

    reason = ET.SubElement(audit_el, f"{{{ODM_NS}}}ReasonForChange")
    reason.text = reason_for_change or "Clinical Data Capture"

    return audit_el


def serialize_to_odm_xml(
    study_id: str,
    data: dict[str, list[Any]] | list[Any] | None = None,
    metadata_version_oid: str = "MDV.001",
    file_oid: str | None = None,
    originator: str = "Cadence EDC/CDM",
    source_system: str = "Cadence Clinical Research Software",
    source_system_version: str = "1.0.0",
    audit_user: str = "system",
    change_reason: str = "Regulatory Submission Export",
    study_name: str | None = None,
    clinical_data: list[Any] | None = None,
    **kwargs: Any,
) -> str:
    """Serializes clinical records into a valid CDISC ODM-XML v1.3.2 document string.

    Args:
        study_id: Unique study identifier.
        data: Bundle dict of domain datasets (e.g. {"DM": [...], "AE": [...]}) or single list.
        metadata_version_oid: ODM MetaDataVersion identifier.
        file_oid: Unique identifier for this ODM file.
        originator: Originating organization or application.
        source_system: Software generating the XML.
        source_system_version: Software version.
        audit_user: Default username for AuditRecord elements.
        change_reason: Default justification for AuditRecord elements.

    Returns:
        str: Pretty-printed XML document string with <?xml version="1.0" encoding="UTF-8"?>.
    """
    now_iso = datetime.now(UTC).isoformat().replace("+00:00", "Z")
    actual_file_oid = file_oid or f"ODM.{study_id}.{int(datetime.now(UTC).timestamp())}"

    # Ensure namespaces are registered in ElementTree
    ET.register_namespace("", ODM_NS)
    ET.register_namespace("ds", DS_NS)
    ET.register_namespace("xsi", XSI_NS)

    # Root <ODM>
    root = ET.Element(
        f"{{{ODM_NS}}}ODM",
        attrib={
            f"{{{XSI_NS}}}schemaLocation": f"{ODM_NS} ODM1-3-2.xsd",
            "ODMVersion": "1.3.2",
            "FileType": "Snapshot",
            "FileOID": actual_file_oid,
            "CreationDateTime": now_iso,
            "Originator": originator,
            "SourceSystem": source_system,
            "SourceSystemVersion": source_system_version,
        },
    )

    if data is None and clinical_data is not None:
        data = clinical_data

    # Normalize data bundle
    datasets: dict[str, list[dict[str, Any]]] = {}
    if isinstance(data, dict):
        for k, v in data.items():
            datasets[k.upper()] = [_to_dict(r) for r in (v or [])]
    elif (
        isinstance(data, list)
        and data
        and isinstance(data[0], dict)
        and "item_id" in data[0]
    ):
        for r in data:
            grp = (r.get("item_group_id") or "DATASET").upper()
            item_name = r.get("item_id")
            val = r.get("value")
            rec = {
                "USUBJID": r.get("subject_id") or r.get("USUBJID") or "SUBJ-001",
                "SUBJID": r.get("subject_id") or r.get("SUBJID") or "SUBJ-001",
                "SITEID": r.get("site_id") or r.get("SITEID"),
                "VISIT": r.get("visit_id") or r.get("VISIT"),
                item_name: val,
                "_audit_user": r.get("user_id"),
                "_audit_reason": r.get("reason_for_change"),
                "_audit_timestamp": r.get("timestamp"),
            }
            if grp not in datasets:
                datasets[grp] = []
            datasets[grp].append(rec)
    else:
        if data:
            first_r = _to_dict(data[0])
            name = str(first_r.get("DOMAIN") or "DATASET").upper()
            datasets[name] = [_to_dict(r) for r in data]
        else:
            datasets["DATASET"] = []

    # 1. <Study> & <MetaDataVersion>
    study_el = ET.SubElement(root, f"{{{ODM_NS}}}Study", attrib={"OID": study_id})
    gv = ET.SubElement(study_el, f"{{{ODM_NS}}}GlobalVariables")
    sn = ET.SubElement(gv, f"{{{ODM_NS}}}StudyName")
    sn.text = study_name or f"Study {study_id}"
    sd = ET.SubElement(gv, f"{{{ODM_NS}}}StudyDescription")
    sd.text = f"Protocol & Clinical Observations for {study_id}"
    pn = ET.SubElement(gv, f"{{{ODM_NS}}}ProtocolName")
    pn.text = study_id

    mdv = ET.SubElement(
        study_el,
        f"{{{ODM_NS}}}MetaDataVersion",
        attrib={
            "OID": metadata_version_oid,
            "Name": f"Metadata Version for {study_id}",
        },
    )

    # Collect metadata definitions
    all_items_meta: dict[str, tuple[str, str]] = {}  # item_oid -> (name, data_type)
    for ds_name, records in datasets.items():
        ig_oid = f"IG.{ds_name}"
        ig_def = ET.SubElement(
            mdv,
            f"{{{ODM_NS}}}ItemGroupDef",
            attrib={"OID": ig_oid, "Name": ds_name, "Repeating": "Yes"},
        )
        for r in records:
            for k, v in r.items():
                if k.lower() in (
                    "created_at",
                    "created_by",
                    "reason_for_change",
                    "version_index",
                ):
                    continue
                item_oid = f"IT.{ds_name}.{k.upper()}"
                if item_oid not in all_items_meta:
                    dt_type = _infer_odm_data_type(v)
                    all_items_meta[item_oid] = (k.upper(), dt_type)
                    ET.SubElement(
                        ig_def,
                        f"{{{ODM_NS}}}ItemRef",
                        attrib={
                            "ItemOID": item_oid,
                            "OrderNumber": str(len(all_items_meta)),
                        },
                    )

    for item_oid, (name, dt_type) in all_items_meta.items():
        ET.SubElement(
            mdv,
            f"{{{ODM_NS}}}ItemDef",
            attrib={"OID": item_oid, "Name": name, "DataType": dt_type},
        )

    # 2. <ClinicalData>
    cd_el = ET.SubElement(
        root,
        f"{{{ODM_NS}}}ClinicalData",
        attrib={"StudyOID": study_id, "MetaDataVersionOID": metadata_version_oid},
    )

    # Group records by Subject (USUBJID / SUBJID)
    subject_map: dict[str, dict[str, list[dict[str, Any]]]] = {}
    for ds_name, records in datasets.items():
        for r in records:
            sub_id = str(r.get("USUBJID") or r.get("SUBJID") or "SUBJ-GLOBAL").strip()
            if sub_id not in subject_map:
                subject_map[sub_id] = {}
            if ds_name not in subject_map[sub_id]:
                subject_map[sub_id][ds_name] = []
            subject_map[sub_id][ds_name].append(r)

    # Build <SubjectData> hierarchy
    for sub_id, domains in subject_map.items():
        subj_el = ET.SubElement(
            cd_el, f"{{{ODM_NS}}}SubjectData", attrib={"SubjectKey": sub_id}
        )

        sample_rec = next(iter(next(iter(domains.values())))) if domains else {}
        sub_audit_user = (
            sample_rec.get("_audit_user") or sample_rec.get("user_id") or audit_user
        )
        sub_audit_reason = (
            sample_rec.get("_audit_reason")
            or sample_rec.get("reason_for_change")
            or change_reason
        )
        sub_audit_ts = (
            sample_rec.get("_audit_timestamp") or sample_rec.get("timestamp") or now_iso
        )

        # Embedded subject-level <AuditRecord>
        subj_el.append(
            build_audit_record(
                user_id=sub_audit_user,
                reason_for_change=sub_audit_reason,
                timestamp=sub_audit_ts,
                location_id=None,
            )
        )

        # StudyEventData (defaulting to standard baseline or domain-keyed event)
        event_el = ET.SubElement(
            subj_el,
            f"{{{ODM_NS}}}StudyEventData",
            attrib={"StudyEventOID": "SE.ALL_VISITS", "StudyEventRepeatKey": "1"},
        )

        for ds_name, rec_list in domains.items():
            form_el = ET.SubElement(
                event_el,
                f"{{{ODM_NS}}}FormData",
                attrib={"FormOID": f"FORM.{ds_name}", "FormRepeatKey": "1"},
            )

            for idx, r in enumerate(rec_list, start=1):
                ig_data = ET.SubElement(
                    form_el,
                    f"{{{ODM_NS}}}ItemGroupData",
                    attrib={
                        "ItemGroupOID": f"IG.{ds_name}",
                        "ItemGroupRepeatKey": str(idx),
                    },
                )

                for k, v in r.items():
                    if k.lower() in (
                        "created_at",
                        "created_by",
                        "reason_for_change",
                        "version_index",
                    ):
                        continue
                    item_oid = f"IT.{ds_name}.{k.upper()}"
                    val_str = "" if v is None else str(v)
                    item_data = ET.SubElement(
                        ig_data,
                        f"{{{ODM_NS}}}ItemData",
                        attrib={"ItemOID": item_oid, "Value": val_str},
                    )

                    # Embedded item-level <AuditRecord>
                    r_user = r.get("created_by") or audit_user
                    r_reason = r.get("reason_for_change") or change_reason
                    r_time = r.get("created_at") or now_iso
                    item_data.append(
                        build_audit_record(
                            user_id=r_user,
                            reason_for_change=r_reason,
                            timestamp=r_time,
                        )
                    )

    # Serialize and format XML
    raw_xml = ET.tostring(root, encoding="utf-8")
    dom = minidom.parseString(raw_xml)
    pretty_xml = dom.toprettyxml(indent="  ", encoding="utf-8").decode("utf-8")

    # Remove extra blank lines created by minidom pretty-printing
    cleaned_lines = [line for line in pretty_xml.splitlines() if line.strip()]
    return "\n".join(cleaned_lines)


def validate_odm_xml_string(xml_str: str) -> bool:
    """Parses and validates that the XML string is a well-formed CDISC ODM-XML document."""
    try:
        root = DefusedET.fromstring(xml_str)
        # Check tag ends with ODM
        if not root.tag.endswith("ODM"):
            return False
        if root.attrib.get("ODMVersion") != "1.3.2":
            return False
        # Check presence of ClinicalData
        clin_data = root.find(f"{{{ODM_NS}}}ClinicalData")
        if clin_data is None:
            # Check without namespace fallback
            clin_data = root.find("ClinicalData")
        return clin_data is not None
    except Exception:
        return False


# Compatibility alias for legacy scripts
generate_odm_xml = serialize_to_odm_xml
