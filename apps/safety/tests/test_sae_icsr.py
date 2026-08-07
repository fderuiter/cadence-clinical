import pytest
from pydantic import ValidationError
from sae_icsr import (
    ICSRHeader,
    ICSRPatient,
    ICSRReactionEvent,
    ICSRReportIdentifiers,
    ICSRSuspectDrug,
    IndividualCaseSafetyReport,
    MedDRACoding,
    SeriousAdverseEvent,
)


def test_valid_meddra_coding():
    coding = MedDRACoding(
        llt_code="10019211",
        llt_name="Headache",
        pt_code="10019211",
        pt_name="Headache",
        hlt_code="10019231",
        hlt_name="Headaches NEC",
        hlgt_code="10029214",
        hlgt_name="Headache and facial pain",
        soc_code="10029205",
        soc_name="Nervous system disorders",
        primary_soc_flag="yes",
        score=1.0,
    )
    assert coding.llt_code == "10019211"
    assert coding.primary_soc_flag == "Y"


def test_invalid_meddra_coding_primary_soc():
    with pytest.raises(ValidationError) as exc:
        MedDRACoding(
            llt_code="10019211",
            llt_name="Headache",
            pt_code="10019211",
            pt_name="Headache",
            hlt_code="10019231",
            hlt_name="Headaches NEC",
            hlgt_code="10029214",
            hlgt_name="Headache and facial pain",
            soc_code="10029205",
            soc_name="Nervous system disorders",
            primary_soc_flag="invalid_flag",
        )
    assert "primary_soc_flag" in str(exc.value)


def test_valid_sae_minimum():
    sae = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="Severe Headache",
        AESTDTC="2026-07-25",
        AESEV="severe",
        AESER="Y",
    )
    assert sae.subject_key == "SUBJ-001"
    assert sae.AESEV == "SEVERE"
    assert sae.AESER == "Y"
    assert sae.version_index == 1
    assert sae.reason_for_change is None


def test_valid_sae_full_normalization():
    coding = MedDRACoding(
        llt_code="10019211",
        llt_name="Headache",
        pt_code="10019211",
        pt_name="Headache",
        hlt_code="10019231",
        hlt_name="Headaches NEC",
        hlgt_code="10029214",
        hlgt_name="Headache and facial pain",
        soc_code="10029205",
        soc_name="Nervous system disorders",
    )

    sae = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="Severe Headache",
        AESTDTC="2026-07-25T14:30:00Z",
        AEENDTC="2026-07-26",
        AESEV="3",  # Normalizes to SEVERE
        AESER="yes",  # Normalizes to Y
        AEREL="RELATED",
        AEOUT="RECOVERED",
        AESEQ=1,
        meddra_coding=coding,
    )
    assert sae.AESEV == "SEVERE"
    assert sae.AESER == "Y"
    assert sae.AESEQ == 1
    assert sae.meddra_coding is not None
    assert sae.meddra_coding.llt_name == "Headache"


def test_invalid_sae_date_chronology():
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026-07-25",
            AEENDTC="2026-07-24",  # Earlier
            AESEV="mild",
            AESER="N",
        )
    assert "AEENDTC" in str(exc.value) and "cannot be earlier than AESTDTC" in str(
        exc.value
    )


def test_invalid_sae_date_format():
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026/07/25",  # Invalid separator
            AESEV="mild",
            AESER="N",
        )
    assert "does not conform to CDISC DTC or ISO 8601" in str(exc.value)


def test_invalid_sae_severity():
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026-07-25",
            AESEV="EXTREME",  # Invalid
            AESER="N",
        )
    assert "is not a valid or normalizable AE Severity value" in str(exc.value)


def test_invalid_sae_seriousness():
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026-07-25",
            AESEV="mild",
            AESER="MAYBE",  # Invalid
        )
    assert "is not a valid or normalizable seriousness code" in str(exc.value)


def test_invalid_sae_seq():
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026-07-25",
            AESEV="mild",
            AESER="N",
            AESEQ=0,  # Must be >= 1
        )
    assert "AESEQ must be greater than or equal to 1" in str(exc.value)


def test_sae_version_metadata():
    # version_index = 1, reason_for_change can be None
    sae_v1 = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="Severe Headache",
        AESTDTC="2026-07-25",
        AESEV="mild",
        AESER="N",
        version_index=1,
    )
    assert sae_v1.version_index == 1

    # version_index > 1, reason_for_change is required
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026-07-25",
            AESEV="mild",
            AESER="N",
            version_index=2,
            reason_for_change=None,
        )
    assert "reason_for_change is required" in str(exc.value)

    # version_index > 1, reason_for_change empty string
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026-07-25",
            AESEV="mild",
            AESER="N",
            version_index=2,
            reason_for_change="   ",
        )
    assert "reason_for_change is required" in str(exc.value)

    # version_index > 1, reason_for_change valid
    sae_v2 = SeriousAdverseEvent(
        subject_key="SUBJ-001",
        AETERM="Severe Headache",
        AESTDTC="2026-07-25",
        AESEV="mild",
        AESER="N",
        version_index=2,
        reason_for_change="Correction of onset date",
    )
    assert sae_v2.version_index == 2
    assert sae_v2.reason_for_change == "Correction of onset date"

    # version_index < 1
    with pytest.raises(ValidationError) as exc:
        SeriousAdverseEvent(
            subject_key="SUBJ-001",
            AETERM="Severe Headache",
            AESTDTC="2026-07-25",
            AESEV="mild",
            AESER="N",
            version_index=0,
        )
    assert "version_index must be greater than or equal to 1" in str(exc.value)


def test_valid_icsr_full():
    header = ICSRHeader(
        sender_organization="SPONSOR_A",
        receiver_organization="FDA",
        transmission_date="2026-07-25T15:00:00Z",
        message_id="MSG-20260725-001",
    )
    report_identifiers = ICSRReportIdentifiers(
        worldwide_unique_case_id="US-SPONSOR_A-2026000001",
        local_report_id="LOC-1234",
        first_sender_type="SPONSOR",
    )
    patient = ICSRPatient(
        patient_id="SUBJ-001",
        sex="female",  # Normalizes to F
        age=45.5,
        age_unit="years",  # Normalizes to YEAR
        birth_date="1981-01-15",
    )
    reactions = [
        ICSRReactionEvent(
            reaction_term="Anaphylactic shock",
            seriousness_death=False,
            seriousness_life_threatening=True,
            seriousness_hospitalization="yes",
        )
    ]
    suspect_drugs = [
        ICSRSuspectDrug(
            drug_name="Cadence-Trial-Drug",
            active_substance_name="Cadencium",
            dosage_text="10mg QD",
            route_of_administration="ORAL",
            action_taken_with_drug="DRUG WITHDRAWN",
            drug_role="suspect",  # Normalizes to SUSPECT
        )
    ]

    icsr = IndividualCaseSafetyReport(
        header=header,
        report_identifiers=report_identifiers,
        patient=patient,
        reactions=reactions,
        suspect_drugs=suspect_drugs,
        version_index=1,
    )

    assert icsr.header.sender_organization == "SPONSOR_A"
    assert icsr.patient.sex == "F"
    assert icsr.patient.age_unit == "YEAR"
    assert icsr.reactions[0].seriousness_death == "N"
    assert icsr.reactions[0].seriousness_life_threatening == "Y"
    assert icsr.reactions[0].seriousness_hospitalization == "Y"
    assert icsr.suspect_drugs[0].drug_role == "SUSPECT"


def test_invalid_icsr_patient_age_negative():
    with pytest.raises(ValidationError) as exc:
        ICSRPatient(
            patient_id="SUBJ-001",
            sex="1",
            age=-5,
        )
    assert "Age cannot be negative" in str(exc.value)


def test_invalid_icsr_patient_age_unit():
    with pytest.raises(ValidationError) as exc:
        ICSRPatient(
            patient_id="SUBJ-001",
            sex="1",
            age_unit="LIGHT_YEARS",
        )
    assert "Invalid age unit" in str(exc.value)


def test_invalid_icsr_drug_role():
    with pytest.raises(ValidationError) as exc:
        ICSRSuspectDrug(
            drug_name="DrugX",
            drug_role="PLACEBO",
        )
    assert "Invalid drug role" in str(exc.value)


def test_icsr_version_metadata():
    header = ICSRHeader(
        sender_organization="SPONSOR_A",
        receiver_organization="FDA",
        transmission_date="2026-07-25T15:00:00Z",
        message_id="MSG-20260725-001",
    )
    report_identifiers = ICSRReportIdentifiers(
        worldwide_unique_case_id="US-SPONSOR_A-2026000001",
    )
    patient = ICSRPatient(
        patient_id="SUBJ-001",
        sex="1",
    )

    # version_index > 1, reason_for_change is required
    with pytest.raises(ValidationError) as exc:
        IndividualCaseSafetyReport(
            header=header,
            report_identifiers=report_identifiers,
            patient=patient,
            version_index=2,
            reason_for_change=None,
        )
    assert "reason_for_change is required" in str(exc.value)

    # version_index > 1, reason_for_change valid
    icsr_v2 = IndividualCaseSafetyReport(
        header=header,
        report_identifiers=report_identifiers,
        patient=patient,
        version_index=2,
        reason_for_change="Follow-up report with updated lab results",
    )
    assert icsr_v2.version_index == 2
    assert icsr_v2.reason_for_change == "Follow-up report with updated lab results"
