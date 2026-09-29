"""XML Layout Rendering Stage for DDF Compiler.

Renders CDISC ODM v1.3.2 and OpenRosa XForms XML layouts.
Note: Template rendering pipeline migrated to C# Fluid.Core Liquid engine (Cadence.Ddf.UsdmCompiler).
"""

from __future__ import annotations

import hashlib
import defusedxml.minidom as minidom

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
        """Renders intermediate representation into ODM and OpenRosa XML artifacts."""
        template_data = study_ir.to_template_dict()
        items_xml = "".join(f"<{item['name']} />" for item in template_data.get("items", []))
        
        odm_str = pretty_print(
            f'<?xml version="1.0"?><ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.{template_data.get("study_id")}"><Study OID="{template_data.get("study_id")}"><GlobalVariables>Cadence Generated Study</GlobalVariables><MetaDataVersion OID="MDV.1" Name="Version 1.0"/></Study></ODM>'
        )
        openrosa_str = pretty_print(
            f'<?xml version="1.0"?><h:html xmlns="http://www.w3.org/2002/xforms"><h:head><h:title>{template_data.get("name")}</h:title><model><instance><data id="{template_data.get("study_id")}">{items_xml}</data></instance></model></h:head><h:body/></h:html>'
        )

        form_hash = hashlib.sha256(openrosa_str.encode("utf-8")).hexdigest()

        return odm_str, openrosa_str, form_hash
