"""XML Layout Rendering Stage for DDF Compiler.

Renders CDISC ODM v1.3.2 and OpenRosa XForms XML layouts safely using ElementTree DOM generation.
"""

from __future__ import annotations

import hashlib
import xml.etree.ElementTree as ET

try:
    import defusedxml.minidom as minidom
except ImportError:
    import xml.dom.minidom as minidom

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from packages.ddf.models import DDFStudyIR


def pretty_print(xml_string: str) -> str:
    """Format an XML string with clean indentation.

    Removes whitespace-only text nodes before applying standard formatting via minidom.
    """
    dom = minidom.parseString(xml_string)
    for node in dom.getElementsByTagName("*"):
        for child in list(node.childNodes):
            if child.nodeType == 3 and not child.data.strip():
                node.removeChild(child)
    return dom.toprettyxml(indent="  ")


class XmlLayoutRendererStage:
    """Renders strongly-typed DDFStudyIR into formatted CDISC ODM and OpenRosa XML artifacts."""

    def __init__(self, template_dir: str | None = None) -> None:
        pass

    def render(self, study_ir: DDFStudyIR) -> tuple[str, str, str]:
        """Renders intermediate representation into ODM and OpenRosa XML artifacts securely."""
        template_data = study_ir.to_template_dict()
        study_id = str(template_data.get("study_id", ""))
        study_name = str(template_data.get("name", ""))
        items = template_data.get("items", [])

        # 1. Build CDISC ODM XML Layout
        odm = ET.Element("ODM", {
            "xmlns": "http://www.cdisc.org/ns/odm/v1.3",
            "FileOID": f"ODM.{study_id}",
        })
        study = ET.SubElement(odm, "Study", {"OID": study_id})
        gv = ET.SubElement(study, "GlobalVariables")
        gv.text = "Cadence Generated Study"
        mdv = ET.SubElement(study, "MetaDataVersion", {"OID": "MDV.1", "Name": "Version 1.0"})

        for item in items:
            item_id = str(item.get("id", ""))
            item_name = str(item.get("name", ""))
            item_type = str(item.get("type", "string"))
            localizations = item.get("localizations") or {}
            relevant = item.get("relevant")
            constraint = item.get("constraint")

            item_def = ET.SubElement(mdv, "ItemDef", {
                "OID": item_id,
                "Name": item_name,
                "DataType": item_type,
            })

            if localizations:
                desc_elem = ET.SubElement(item_def, "Description")
                for loc_key, loc_val in localizations.items():
                    if isinstance(loc_val, dict):
                        desc_text = loc_val.get("description") or loc_val.get("label")
                        if desc_text:
                            tt = ET.SubElement(desc_elem, "TranslatedText", {"Language": str(loc_key)})
                            tt.text = str(desc_text)

            if relevant:
                ET.SubElement(item_def, "Alias", {"Context": "relevant", "Name": str(relevant)})

            if constraint:
                ET.SubElement(item_def, "Alias", {"Context": "constraint", "Name": str(constraint)})

        odm_raw = ET.tostring(odm, encoding="unicode")
        odm_str = pretty_print(odm_raw)

        # 2. Build OpenRosa XForms XML Layout
        ET.register_namespace("h", "http://www.w3.org/1999/xhtml")
        ET.register_namespace("ev", "http://www.w3.org/2001/xml-events")
        ET.register_namespace("or", "http://openrosa.org/xforms")
        ET.register_namespace("orx", "http://openrosa.org/xforms")
        ET.register_namespace("odk", "http://www.opendatakit.org/xforms")
        ET.register_namespace("jr", "http://openrosa.org/javarosa")

        html = ET.Element("{http://www.w3.org/1999/xhtml}html", {
            "xmlns": "http://www.w3.org/2002/xforms",
            "xmlns:ev": "http://www.w3.org/2001/xml-events",
            "xmlns:or": "http://openrosa.org/xforms",
            "xmlns:orx": "http://openrosa.org/xforms",
            "xmlns:odk": "http://www.opendatakit.org/xforms",
            "xmlns:jr": "http://openrosa.org/javarosa",
        })

        head = ET.SubElement(html, "{http://www.w3.org/1999/xhtml}head")
        title = ET.SubElement(head, "{http://www.w3.org/1999/xhtml}title")
        title.text = study_name

        model = ET.SubElement(head, "model")
        instance = ET.SubElement(model, "instance")
        data = ET.SubElement(instance, "data", {"id": study_id})

        for item in items:
            raw_tag = str(item.get("name", "item"))
            safe_tag = "".join(c for c in raw_tag if c.isalnum() or c in ("_", "-")) or "item"
            if safe_tag[0].isdigit():
                safe_tag = f"item_{safe_tag}"
            ET.SubElement(data, safe_tag)

        for item in items:
            raw_tag = str(item.get("name", "item"))
            safe_tag = "".join(c for c in raw_tag if c.isalnum() or c in ("_", "-")) or "item"
            if safe_tag[0].isdigit():
                safe_tag = f"item_{safe_tag}"

            bind_attrs = {
                "nodeset": f"/data/{safe_tag}",
                "type": str(item.get("type", "string")),
            }
            if item.get("relevant"):
                bind_attrs["relevant"] = str(item["relevant"])
            if item.get("constraint"):
                bind_attrs["constraint"] = str(item["constraint"])
            if item.get("constraint_message"):
                bind_attrs["{http://openrosa.org/javarosa}constraintMsg"] = str(item["constraint_message"])

            ET.SubElement(model, "bind", bind_attrs)

        body = ET.SubElement(html, "{http://www.w3.org/1999/xhtml}body")
        for item in items:
            raw_tag = str(item.get("name", "item"))
            safe_tag = "".join(c for c in raw_tag if c.isalnum() or c in ("_", "-")) or "item"
            if safe_tag[0].isdigit():
                safe_tag = f"item_{safe_tag}"

            input_attrs = {"ref": f"/data/{safe_tag}"}
            if item.get("appearance"):
                input_attrs["appearance"] = str(item["appearance"])

            input_elem = ET.SubElement(body, "input", input_attrs)
            localizations = item.get("localizations") or {}

            if localizations:
                for loc_key, loc_val in localizations.items():
                    if isinstance(loc_val, dict):
                        label_text = loc_val.get("label")
                        if label_text:
                            lbl = ET.SubElement(input_elem, "label", {"{http://www.w3.org/XML/1998/namespace}lang": str(loc_key)})
                            lbl.text = str(label_text)
                        hint_text = loc_val.get("hint")
                        if hint_text:
                            hnt = ET.SubElement(input_elem, "hint", {"{http://www.w3.org/XML/1998/namespace}lang": str(loc_key)})
                            hnt.text = str(hint_text)
            else:
                lbl = ET.SubElement(input_elem, "label")
                lbl.text = raw_tag

        openrosa_raw = ET.tostring(html, encoding="unicode")
        openrosa_str = pretty_print(openrosa_raw)

        form_hash = hashlib.sha256(openrosa_str.encode("utf-8")).hexdigest()

        return odm_str, openrosa_str, form_hash
