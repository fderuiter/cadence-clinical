"""ICH E2B(R3) XML safety report builder service.

Requirements: PRD-SYS-001
"""

import xml.etree.ElementTree as ET

import packages  # noqa: F401
from apps.execution.domain.safety_models import SAECaseRecord


class E2BR3XMLBuilder:
    """Builder generating valid ICH E2B(R3) XML ICSR safety reports from SAECaseRecord instances.

    Requirements: PRD-SYS-001
    """

    def build_e2b_xml(self, case: SAECaseRecord) -> str:
        """Construct canonical ICH E2B(R3) XML report string from SAECaseRecord.

        Args:
            case: Hydrated SAECaseRecord instance.

        Returns:
            Formatted XML string representation of safety report.
        """
        root = ET.Element("icsr")

        def _add_child(name: str, val: str) -> None:
            child = ET.SubElement(root, name)
            child.text = str(val)

        _add_child("safety_report_id", case.safety_report_id)
        _add_child("study_id", case.study_id)
        _add_child("subject_id", case.subject_id)
        _add_child("reaction_pt", case.reaction_pt)
        _add_child("meddra_code", case.meddra_code)
        _add_child("onset_date", case.onset_date)
        _add_child("seriousness_criteria", case.seriousness_criteria.value)
        _add_child("causality", case.causality.value)
        _add_child(
            "expedited", "true" if case.expedited_reporting_required else "false"
        )

        ET.indent(root, space="  ")
        xml_decl = '<?xml version="1.0" encoding="UTF-8"?>\n'
        return xml_decl + ET.tostring(root, encoding="utf-8").decode("utf-8")
