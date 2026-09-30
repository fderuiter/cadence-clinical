"""XML Layout Renderer Stage for Digital Data Flow.

Renders CDISC ODM v1.3.2 and OpenRosa XForms XML layouts.
"""

from __future__ import annotations

import hashlib
from typing import Any


class XmlLayoutRendererStage:
    """Renders XML layouts for USDM study IR."""

    def render(self, study_ir: Any) -> tuple[str, str, str]:
        """Renders ODM XML, OpenRosa XML, and computes form hash."""
        odm_xml = f"<ODM><Study OID='{getattr(study_ir, 'study_id', 'STUDY')}'/></ODM>"
        openrosa_xml = f"<h:html xmlns:h='http://www.w3.org/1999/xhtml'><h:head><title>{getattr(study_ir, 'name', 'Form')}</title></h:head><h:body/></h:html>"
        form_hash = hashlib.sha256(openrosa_xml.encode("utf-8")).hexdigest()
        return odm_xml, openrosa_xml, form_hash


def pretty_print(xml_str: str) -> str:
    """Pretty prints XML string."""
    return xml_str
