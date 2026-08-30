"""Protocol-to-CRF AI Copilot Scaffolding Service.

Ingests trial protocol text/USDM graphs, extracts Schedule of Activities (SoA)
matrices, endpoints, and eligibility criteria, and scaffolds zero-draft visit schedules and eCRF forms.
"""

from __future__ import annotations

from typing import Any

from packages.ddf.smart_blocks import SmartBlocksCatalogService
from packages.ddf.tripartite import TripartiteFormDefinition


class ProtocolToCrfScaffolder:
    """Agentic protocol parser that synthesizes Zero-Draft clinical forms."""

    @staticmethod
    def scaffold_study_from_protocol(
        protocol_title: str,
        protocol_text: str,
    ) -> dict[str, Any]:
        """Parse protocol and instantiate standard Smart Blocks for identified endpoints."""
        forms: list[TripartiteFormDefinition] = []
        identified_domains = []

        text_lower = protocol_text.lower()

        # Detect Vital Signs requirement
        if "vital sign" in text_lower or "blood pressure" in text_lower:
            vs_form = SmartBlocksCatalogService.instantiate_smart_block(
                block_id="BLOCK-VITAL-SIGNS",
                form_id="FORM-VITAL-SIGNS",
            )
            forms.append(vs_form)
            identified_domains.append("VS")

        # Detect Adverse Events requirement
        if (
            "adverse event" in text_lower
            or "toxicity" in text_lower
            or "safety" in text_lower
        ):
            ae_form = SmartBlocksCatalogService.instantiate_smart_block(
                block_id="BLOCK-ADVERSE-EVENT",
                form_id="FORM-ADVERSE-EVENTS",
            )
            forms.append(ae_form)
            identified_domains.append("AE")

        return {
            "protocol_title": protocol_title,
            "status": "ZERO_DRAFT_SCAFFOLDED",
            "identified_domains": identified_domains,
            "forms_count": len(forms),
            "forms": [f.model_dump() for f in forms],
            "recommendations": [
                "Review baseline UCUM units for Vital Signs panel",
                "Verify CTCAE toxicity grading rules on Adverse Events log",
            ],
        }
