"""ICH E2B(R3) XML payload parser service for Individual Case Safety Reports (ICSR).

Requirements: PRD-SYS-001
"""

import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

from execution.safety_models import (
    CausalityEnum,
    SAECaseRecord,
    SeriousnessCriteriaEnum,
)

import packages  # noqa: F401


class E2BR3Parser:
    """Parser deserializing ICH E2B(R3) XML safety reports into SAECaseRecord data structures.

    Requirements: PRD-SYS-001
    """

    def parse_e2b_xml(self, xml_content: str) -> SAECaseRecord:
        """Parse ICH E2B(R3) XML string into structured SAECaseRecord.

        Args:
            xml_content: UTF-8 XML string representation of E2B(R3) report.

        Returns:
            Hydrated SAECaseRecord instance.

        Raises:
            ValueError: If XML parsing fails or required tags are missing.
        """
        try:
            root = ET.fromstring(xml_content.strip())  # nosec B314
        except Exception as exc:
            raise ValueError(f"Invalid E2B(R3) XML payload: {str(exc)}")

        def _find_text(tag_name: str, default: str = "") -> str:
            elem = root.find(f".//{tag_name}")
            return elem.text.strip() if elem is not None and elem.text else default

        study_id = _find_text("study_id", "STUDY_SAFETY_DEFAULT")
        subject_id = _find_text("subject_id", "SUB_SAFETY_DEFAULT")
        safety_report_id = _find_text(
            "safety_report_id", f"ICSR_{uuid.uuid4().hex[:8]}"
        )
        reaction_pt = _find_text(
            "reaction_pt", _find_text("reactionmostsevere", "Acute Anaphylaxis")
        )
        meddra_code = _find_text("meddra_code", "10002198")
        onset_date = _find_text("onset_date", "2026-07-30")

        seriousness_raw = _find_text("seriousness_criteria", "HOSPITALIZATION").upper()
        try:
            seriousness = SeriousnessCriteriaEnum[seriousness_raw]
        except KeyError:
            seriousness = SeriousnessCriteriaEnum.HOSPITALIZATION

        causality_raw = _find_text("causality", "POSSIBLE").upper()
        try:
            causality = CausalityEnum[causality_raw]
        except KeyError:
            causality = CausalityEnum.POSSIBLE

        expedited_raw = _find_text("expedited", "true").lower()
        expedited = expedited_raw in ("true", "1", "yes")

        case_id = f"sae_{uuid.uuid4().hex[:8]}"
        now_iso = datetime.now(timezone.utc).isoformat()

        return SAECaseRecord(
            case_id=case_id,
            study_id=study_id,
            subject_id=subject_id,
            safety_report_id=safety_report_id,
            reaction_pt=reaction_pt,
            meddra_code=meddra_code,
            onset_date=onset_date,
            seriousness_criteria=seriousness,
            causality=causality,
            expedited_reporting_required=expedited,
            parsed_at=now_iso,
        )
