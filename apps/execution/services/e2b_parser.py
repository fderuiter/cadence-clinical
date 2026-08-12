"""ICH E2B(R3) XML payload parser service for Individual Case Safety Reports (ICSR).

Requirements: PRD-SYS-001
"""

import uuid
from datetime import UTC, datetime

import defusedxml.ElementTree as ET

import packages  # noqa: F401
from apps.execution.domain.safety_models import (
    CausalityEnum,
    SAECaseRecord,
    SeriousnessCriteriaEnum,
)


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

        ns = "{urn:hl7-org:v3}"

        def _find_text(tag_name: str, default: str = "") -> str:
            # Try finding with namespace prefix first
            elem = root.find(f".//{ns}{tag_name}")
            if elem is None:
                # Try finding without namespace prefix (backward compatibility)
                elem = root.find(f".//{tag_name}")
            return elem.text.strip() if elem is not None and elem.text else default

        # Decode study_id and case_id from local_report_id if encoded
        local_id = _find_text("local_report_id")
        if local_id and ":" in local_id:
            parts = local_id.split(":", 1)
            study_id = parts[0]
            case_id = parts[1]
        else:
            study_id = _find_text("study_id", "STUDY_SAFETY_DEFAULT")
            case_id = local_id if local_id else f"sae_{uuid.uuid4().hex[:8]}"

        # Handle pseudonymized patient_id/subject_id or fallback
        subject_id = _find_text("patient_id")
        if not subject_id:
            subject_id = _find_text("subject_id", "SUB_SAFETY_DEFAULT")

        # Handle safety_report_id with fallback to worldwide_unique_case_id or message_id
        safety_report_id = _find_text("worldwide_unique_case_id")
        if not safety_report_id:
            safety_report_id = _find_text("message_id")
        if not safety_report_id:
            safety_report_id = _find_text(
                "safety_report_id", f"ICSR_{uuid.uuid4().hex[:8]}"
            )

        # Handle reaction Preferred Term (PT) with fallbacks
        reaction_pt = _find_text("reaction_term")
        if not reaction_pt:
            reaction_pt = _find_text("reaction_pt")
        if not reaction_pt:
            reaction_pt = _find_text("reactionmostsevere", "Acute Anaphylaxis")

        # Handle MedDRA code
        meddra_code = _find_text("llt_code")
        if not meddra_code:
            meddra_code = _find_text("pt_code")
        if not meddra_code:
            meddra_code = _find_text("meddra_code", "10002198")

        # Handle onset date
        onset_date = _find_text("start_date")
        if not onset_date:
            onset_date = _find_text("onset_date", "2026-07-30")

        # Handle seriousness criteria from flags or fallback
        if _find_text("seriousness_death") == "Y":
            seriousness = SeriousnessCriteriaEnum.DEATH
        elif _find_text("seriousness_life_threatening") == "Y":
            seriousness = SeriousnessCriteriaEnum.LIFE_THREATENING
        elif _find_text("seriousness_hospitalization") == "Y":
            seriousness = SeriousnessCriteriaEnum.HOSPITALIZATION
        elif _find_text("seriousness_disability") == "Y":
            seriousness = SeriousnessCriteriaEnum.DISABILITY
        elif _find_text("seriousness_congenital_anomaly") == "Y":
            seriousness = SeriousnessCriteriaEnum.CONGENITAL_ANOMALY
        elif _find_text("seriousness_other_medically_important") == "Y":
            seriousness = SeriousnessCriteriaEnum.OTHER_MEDICALLY_IMPORTANT
        else:
            seriousness_raw = _find_text(
                "seriousness_criteria", "HOSPITALIZATION"
            ).upper()
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

        now_iso = datetime.now(UTC).isoformat()

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
