"""ICH E2B(R3) XML safety report builder service.

Requirements: PRD-SYS-001
"""

import hashlib
from datetime import UTC, datetime

import packages  # noqa: F401
from apps.execution.domain.safety_models import SAECaseRecord, SeriousnessCriteriaEnum
from apps.safety.domain.sae_icsr import (
    ICSRHeader,
    ICSRPatient,
    ICSRReactionEvent,
    ICSRReportIdentifiers,
    ICSRSuspectDrug,
    IndividualCaseSafetyReport,
    MedDRACoding,
)
from apps.safety.renderer import generate_e2b_xml


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
        transmission_date = datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")
        header = ICSRHeader(
            message_id=case.safety_report_id,
            sender_organization="CLINICAL_EXECUTION_SERVICE",
            receiver_organization="REGULATORY_GATEWAY",
            transmission_date=transmission_date,
            message_type="ICHICSR",
        )

        report_identifiers = ICSRReportIdentifiers(
            worldwide_unique_case_id=case.safety_report_id,
            local_report_id=f"{case.study_id}:{case.case_id}",
            first_sender_type="SPONSOR",
        )

        patient_id = hashlib.sha256(case.subject_id.encode("utf-8")).hexdigest()
        patient = ICSRPatient(
            patient_id=patient_id,
            sex="U",
            birth_date=None,
        )

        meddra_coding = MedDRACoding(
            llt_code=case.meddra_code,
            llt_name=case.reaction_pt,
            pt_code=case.meddra_code,
            pt_name=case.reaction_pt,
            hlt_code=case.meddra_code,
            hlt_name=case.reaction_pt,
            hlgt_code=case.meddra_code,
            hlgt_name=case.reaction_pt,
            soc_code=case.meddra_code,
            soc_name=case.reaction_pt,
            primary_soc_flag="Y",
            score=1.0,
        )

        reaction = ICSRReactionEvent(
            reaction_term=case.reaction_pt,
            meddra_coding=meddra_coding,
            start_date=case.onset_date,
            seriousness_death="Y"
            if case.seriousness_criteria == SeriousnessCriteriaEnum.DEATH
            else "N",
            seriousness_life_threatening="Y"
            if case.seriousness_criteria == SeriousnessCriteriaEnum.LIFE_THREATENING
            else "N",
            seriousness_hospitalization="Y"
            if case.seriousness_criteria == SeriousnessCriteriaEnum.HOSPITALIZATION
            else "N",
            seriousness_disability="Y"
            if case.seriousness_criteria == SeriousnessCriteriaEnum.DISABILITY
            else "N",
            seriousness_congenital_anomaly="Y"
            if case.seriousness_criteria == SeriousnessCriteriaEnum.CONGENITAL_ANOMALY
            else "N",
            seriousness_other_medically_important="Y"
            if case.seriousness_criteria
            == SeriousnessCriteriaEnum.OTHER_MEDICALLY_IMPORTANT
            else "N",
        )

        suspect_drug = ICSRSuspectDrug(
            drug_name="STUDY_DRUG",
            drug_role="SUSPECT",
        )

        icsr = IndividualCaseSafetyReport(
            header=header,
            report_identifiers=report_identifiers,
            patient=patient,
            reactions=[reaction],
            suspect_drugs=[suspect_drug],
            version_index=1,
        )

        return generate_e2b_xml(icsr)
