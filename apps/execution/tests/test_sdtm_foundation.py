from datetime import UTC, datetime

import pytest
from pydantic import ValidationError

from apps.execution.domain.sdtm.enums import (
    AEOutcome,
    AERelationship,
    AESeriousness,
    AESeverity,
    NullFlavor,
    Race,
    SDTMDomain,
    Sex,
)
from apps.execution.domain.sdtm.models import (
    AE,
    CM,
    DM,
    LB,
    SUPPQUAL,
    VS,
    AdverseEvent,
    ConcomitantMedication,
    Demographics,
    Laboratory,
    SUPPQUALRecord,
    VitalSign,
    validate_dtc_format,
)
from apps.execution.domain.sdtm.terminology import (
    normalize_race,
    normalize_seriousness,
    normalize_severity,
    normalize_sex,
)


def test_auditable_model_fields_and_validation():
    """
    Verify that AuditableModel validates its audit fields correctly,
    especially the non-empty change reason and >= 1 version index.
    """
    dt = datetime.now(UTC)
    dm = DM(
        STUDYID="STUDY-1",
        DOMAIN="DM",
        USUBJID="STUDY-1-001",
        SEX="male",
        RACE="white",
        ARM="Placebo",
        created_at=dt,
        created_by="system_user",
        reason_for_change="Initial ingestion of demographics data.",
        version_index=1,
    )
    assert dm.created_by == "system_user"
    assert dm.reason_for_change == "Initial ingestion of demographics data."
    assert dm.version_index == 1
    assert dm.created_at == dt

    # Invalid empty or blank reason_for_change should raise ValidationError
    with pytest.raises(ValidationError) as exc:
        DM(
            STUDYID="STUDY-1",
            DOMAIN="DM",
            USUBJID="STUDY-1-001",
            SEX="M",
            RACE="WHITE",
            ARM="Placebo",
            created_by="system_user",
            reason_for_change="   ",  # whitespace only
            version_index=1,
        )
    assert "Reason for change cannot be empty" in str(exc.value)

    # Invalid version_index (< 1) should raise ValidationError
    with pytest.raises(ValidationError) as exc:
        DM(
            STUDYID="STUDY-1",
            DOMAIN="DM",
            USUBJID="STUDY-1-001",
            SEX="M",
            RACE="WHITE",
            ARM="Placebo",
            created_by="system_user",
            reason_for_change="Reason",
            version_index=0,
        )
    assert "version_index must be greater than or equal to 1" in str(exc.value)


def test_dm_required_and_optional_fields():
    """
    Verify Required SDTM variables result in Pydantic validation errors when missing or empty.
    Check that Expected/Permissible variables are optional.
    """
    dm = DM(
        STUDYID="STUDY-01",
        DOMAIN="DM",
        USUBJID="STUDY-01-001",
        SUBJID="001",
        RFSTDTC="2026-07-29",
        RFENDTC="2026-08-01",
        BRTHDTC="1990-05-15",
        AGE=36,
        AGEU="YEARS",
        SEX="F",
        RACE="ASIAN",
        ARM="Active Treatment",
        created_by="user_1",
        reason_for_change="Create test subject",
    )
    assert dm.STUDYID == "STUDY-01"
    assert dm.SUBJID == "001"
    assert dm.SEX == Sex.F
    assert dm.RACE == Race.ASIAN

    # Alias / descriptive import check
    assert isinstance(dm, Demographics)

    # Missing STUDYID (Required)
    with pytest.raises(ValidationError):
        DM(
            DOMAIN="DM",
            USUBJID="STUDY-01-001",
            SEX="F",
            RACE="ASIAN",
            ARM="Active Treatment",
            created_by="user_1",
            reason_for_change="Create test subject",
        )

    # Empty STUDYID
    with pytest.raises(ValidationError):
        DM(
            STUDYID="  ",
            DOMAIN="DM",
            USUBJID="STUDY-01-001",
            SEX="F",
            RACE="ASIAN",
            ARM="Active Treatment",
            created_by="user_1",
            reason_for_change="Create test subject",
        )

    # Missing SEX
    with pytest.raises(ValidationError):
        DM(
            STUDYID="STUDY-01",
            DOMAIN="DM",
            USUBJID="STUDY-01-001",
            RACE="ASIAN",
            ARM="Active Treatment",
            created_by="user_1",
            reason_for_change="Create test subject",
        )


def test_ae_required_optional_and_date_order():
    """
    Verify Required and optional variables for AE.
    Verify that start date and end date sequence validation behaves correctly.
    """
    ae = AE(
        STUDYID="STUDY-01",
        DOMAIN="AE",
        USUBJID="STUDY-01-001",
        AESEQ=1,
        AETERM="Headache",
        AELOC="HEAD",
        AELDTC="2026-07-30T10:00:00",
        AESTDTC="2026-07-30",
        AEENDTC="2026-07-31",
        AESEV="MILD",
        AESER="N",
        AEREL="RELATED",
        AEOUT="RECOVERED/RESOLVED",
        created_by="doc_1",
        reason_for_change="Log mild headache",
    )
    assert ae.AESEQ == 1
    assert ae.AESER == AESeriousness.N
    assert ae.AESEV == AESeverity.MILD
    assert ae.AEREL == AERelationship.RELATED
    assert ae.AEOUT == AEOutcome.RECOVERED_RESOLVED

    # Alias check
    assert isinstance(ae, AdverseEvent)

    # Invalid sequence (< 1)
    with pytest.raises(ValidationError):
        AE(
            STUDYID="STUDY-01",
            DOMAIN="AE",
            USUBJID="STUDY-01-001",
            AESEQ=0,
            AETERM="Headache",
            AESER="N",
            created_by="doc_1",
            reason_for_change="Log headache",
        )

    # Chronologically invalid dates (resolution before start)
    with pytest.raises(ValidationError) as exc:
        AE(
            STUDYID="STUDY-01",
            DOMAIN="AE",
            USUBJID="STUDY-01-001",
            AESEQ=1,
            AETERM="Headache",
            AESTDTC="2026-07-31",
            AEENDTC="2026-07-30",  # earlier
            AESER="N",
            created_by="doc_1",
            reason_for_change="Log headache",
        )
    assert "cannot be earlier than" in str(exc.value)


def test_vs_required_and_optional_fields():
    """
    Verify VS model required and optional fields.
    """
    vs = VS(
        STUDYID="STUDY-01",
        DOMAIN="VS",
        USUBJID="STUDY-01-001",
        VSSEQ=1,
        VSTESTCD="SYSBP",
        VSTEST="Systolic Blood Pressure",
        VSORRES=120.0,
        VSORRESU="mmHg",
        VSSTRESC="120",
        VSSTRESN=120.0,
        VSSTRESU="mmHg",
        VSPOS="SITTING",
        VSDTC="2026-07-30T09:00:00",
        VSBLFL="Y",
        created_by="nurse_1",
        reason_for_change="Log baseline blood pressure",
    )
    assert vs.VSSEQ == 1
    assert vs.VSORRES == 120.0
    assert vs.VSSTRESU == "mmHg"

    # Alias check
    assert isinstance(vs, VitalSign)

    # Missing VSTESTCD
    with pytest.raises(ValidationError):
        VS(
            STUDYID="STUDY-01",
            DOMAIN="VS",
            USUBJID="STUDY-01-001",
            VSSEQ=1,
            VSTEST="Systolic Blood Pressure",
            created_by="nurse_1",
            reason_for_change="Log baseline blood pressure",
        )

    # Invalid sequence (< 1)
    with pytest.raises(ValidationError):
        VS(
            STUDYID="STUDY-01",
            DOMAIN="VS",
            USUBJID="STUDY-01-001",
            VSSEQ=0,
            VSTESTCD="SYSBP",
            VSTEST="Systolic Blood Pressure",
            created_by="nurse_1",
            reason_for_change="Log BP",
        )


def test_lb_required_and_optional_fields():
    """
    Verify LB model required and optional fields.
    """
    lb = LB(
        STUDYID="STUDY-01",
        DOMAIN="LB",
        USUBJID="STUDY-01-001",
        LBSEQ=1,
        LBTESTCD="ALT",
        LBTEST="Alanine Aminotransferase",
        LBORRES="45",
        LBORRESU="U/L",
        LBSTRESC="45",
        LBSTRESN=45.0,
        LBSTRESU="U/L",
        LBNRIND="NORMAL",
        LBDTC="2026-07-30T08:00:00",
        LBLOINC="26464-8",
        created_by="lab_tech_1",
        reason_for_change="Log ALT result",
    )
    assert lb.LBSEQ == 1
    assert lb.LBTESTCD == "ALT"

    # Alias check
    assert isinstance(lb, Laboratory)

    # Missing LBSEQ
    with pytest.raises(ValidationError):
        LB(
            STUDYID="STUDY-01",
            DOMAIN="LB",
            USUBJID="STUDY-01-001",
            LBTESTCD="ALT",
            LBTEST="Alanine Aminotransferase",
            created_by="lab_tech_1",
            reason_for_change="Log ALT",
        )

    # Invalid sequence (< 1)
    with pytest.raises(ValidationError):
        LB(
            STUDYID="STUDY-01",
            DOMAIN="LB",
            USUBJID="STUDY-01-001",
            LBSEQ=0,
            LBTESTCD="ALT",
            LBTEST="Alanine Aminotransferase",
            created_by="lab_tech_1",
            reason_for_change="Log ALT",
        )


def test_cm_required_optional_and_date_order():
    """
    Verify CM model required, optional fields, and end date not earlier than start date.
    """
    cm = CM(
        STUDYID="STUDY-01",
        DOMAIN="CM",
        USUBJID="STUDY-01-001",
        CMSEQ=1,
        CMTRT="ASPIRIN",
        CMDECOD="ASPIRIN",
        CMCLAS="ANALGESIC",
        CMDOSE=100.0,
        CMDOSEU="mg",
        CMDOSFRQ="QD",
        CMROUTE="ORAL",
        CMSTDTC="2026-07-29",
        CMENDTC="2026-08-01",
        created_by="doctor_2",
        reason_for_change="Concomitant Aspirin",
    )
    assert cm.CMSEQ == 1
    assert cm.CMTRT == "ASPIRIN"

    # Alias check
    assert isinstance(cm, ConcomitantMedication)

    # Missing required CMTRT
    with pytest.raises(ValidationError):
        CM(
            STUDYID="STUDY-01",
            DOMAIN="CM",
            USUBJID="STUDY-01-001",
            CMSEQ=1,
            created_by="doctor_2",
            reason_for_change="Concomitant Medication",
        )

    # Invalid sequence (< 1)
    with pytest.raises(ValidationError):
        CM(
            STUDYID="STUDY-01",
            DOMAIN="CM",
            USUBJID="STUDY-01-001",
            CMSEQ=0,
            CMTRT="ASPIRIN",
            created_by="doctor_2",
            reason_for_change="Concomitant Medication",
        )

    # Chronologically invalid CM dates (end before start)
    with pytest.raises(ValidationError) as exc:
        CM(
            STUDYID="STUDY-01",
            DOMAIN="CM",
            USUBJID="STUDY-01-001",
            CMSEQ=1,
            CMTRT="ASPIRIN",
            CMSTDTC="2026-08-01",
            CMENDTC="2026-07-29",  # earlier
            created_by="doctor_2",
            reason_for_change="Concomitant Aspirin",
        )
    assert "cannot be earlier than" in str(exc.value)


def test_suppqual_fields_and_validation():
    """
    Verify SUPPQUAL record fields and validation.
    """
    supp = SUPPQUAL(
        STUDYID="STUDY-01",
        RDOMAIN="AE",
        USUBJID="STUDY-01-001",
        IDVAR="AESEQ",
        IDVARVAL="1",
        QNAM="AELOC",
        QLABEL="Anatomical Location",
        QVAL="HEAD",
        QEVAL="INVESTIGATOR",
        created_by="system",
        reason_for_change="Add supplemental qualifier",
    )
    assert supp.RDOMAIN == "AE"
    assert supp.QNAM == "AELOC"
    assert supp.QVAL == "HEAD"
    assert supp.QEVAL == "INVESTIGATOR"

    # Alias check
    assert isinstance(supp, SUPPQUALRecord)

    # Missing mandatory QNAM
    with pytest.raises(ValidationError):
        SUPPQUAL(
            STUDYID="STUDY-01",
            RDOMAIN="AE",
            USUBJID="STUDY-01-001",
            QLABEL="Anatomical Location",
            QVAL="HEAD",
            created_by="system",
            reason_for_change="Add suppqual",
        )

    # Empty string validation
    with pytest.raises(ValidationError) as exc:
        SUPPQUAL(
            STUDYID="STUDY-01",
            RDOMAIN="AE",
            USUBJID="STUDY-01-001",
            QNAM="  ",  # empty/whitespace
            QLABEL="Anatomical Location",
            QVAL="HEAD",
            created_by="system",
            reason_for_change="Add suppqual",
        )
    assert "cannot be empty" in str(exc.value)


def test_terminology_normalization_and_enums():
    """
    Test CDISC terminology normalization and enum validation.
    """
    # SEX normalizations
    assert normalize_sex("male") == Sex.M.value
    assert normalize_sex("Female") == Sex.F.value
    assert normalize_sex("unknown") == Sex.U.value
    assert normalize_sex("not reported") == Sex.U.value
    assert normalize_sex("M") == Sex.M.value
    assert normalize_sex("F") == Sex.F.value
    assert normalize_sex("U") == Sex.U.value

    # Invalid sex inputs
    with pytest.raises(ValueError):
        normalize_sex("alien")
    with pytest.raises(ValueError):
        normalize_sex(None)

    # RACE normalizations
    assert normalize_race("white") == Race.WHITE.value
    assert normalize_race("caucasian") == Race.WHITE.value
    assert normalize_race("black") == Race.BLACK_OR_AFRICAN_AMERICAN.value
    assert normalize_race("asian") == Race.ASIAN.value
    assert (
        normalize_race("AMERICAN INDIAN") == Race.AMERICAN_INDIAN_OR_ALASKA_NATIVE.value
    )
    assert normalize_race(["White", "Asian"]) == Race.MULTIPLE.value
    assert normalize_race("White, Black") == Race.MULTIPLE.value
    assert normalize_race("WHITE; BLACK") == Race.MULTIPLE.value
    assert normalize_race("White and Black") == Race.MULTIPLE.value
    assert normalize_race(["WHITE"]) == Race.WHITE.value
    assert normalize_race("mixed") == Race.MULTIPLE.value
    assert normalize_race("declined") == Race.OTHER.value
    assert normalize_race("multiple") == Race.MULTIPLE.value
    assert normalize_race("unknown") == Race.OTHER.value

    # Invalid race inputs
    with pytest.raises(ValueError):
        normalize_race("kryptonian")
    with pytest.raises(ValueError):
        normalize_race(None)
    with pytest.raises(ValueError):
        normalize_race([])
    with pytest.raises(ValueError):
        normalize_race("   ")

    # Severity normalizations
    assert normalize_severity("mild") == AESeverity.MILD.value
    assert normalize_severity("grade 1") == AESeverity.MILD.value
    assert normalize_severity("Moderate") == AESeverity.MODERATE.value
    assert normalize_severity("severe") == AESeverity.SEVERE.value
    assert normalize_severity("3") == AESeverity.SEVERE.value
    assert normalize_severity("2") == AESeverity.MODERATE.value
    assert normalize_severity("4") == AESeverity.SEVERE.value

    # Invalid severity inputs
    with pytest.raises(ValueError):
        normalize_severity("ultra")
    with pytest.raises(ValueError):
        normalize_severity(None)

    # Seriousness normalizations
    assert normalize_seriousness(True) == "Y"
    assert normalize_seriousness("Yes") == "Y"
    assert normalize_seriousness(False) == "N"
    assert normalize_seriousness("NO") == "N"

    with pytest.raises(ValueError):
        normalize_seriousness("maybe")
    with pytest.raises(ValueError):
        normalize_seriousness(None)


def test_date_format_validation():
    """
    Verify that correct date-time formats are accepted and malformed ones are rejected.
    """
    assert validate_dtc_format(None) is None
    assert validate_dtc_format("2026-07-29") == "2026-07-29"

    with pytest.raises(ValueError):
        validate_dtc_format("invalid-date")

    # Valid dates / partial dates
    dm1 = DM(
        STUDYID="STUDY-01",
        DOMAIN="DM",
        USUBJID="STUDY-01-001",
        RFSTDTC="2026-07-29",
        RFENDTC="2026-08",
        BRTHDTC="1990",
        SEX="F",
        RACE="WHITE",
        ARM="Placebo",
        created_by="system",
        reason_for_change="Check date validation",
    )
    assert dm1.RFSTDTC == "2026-07-29"
    assert dm1.RFENDTC == "2026-08"
    assert dm1.BRTHDTC == "1990"

    # Invalid date formats
    with pytest.raises(ValidationError):
        DM(
            STUDYID="STUDY-01",
            DOMAIN="DM",
            USUBJID="STUDY-01-001",
            RFSTDTC="29-07-2026",  # wrong format
            SEX="F",
            RACE="WHITE",
            ARM="Placebo",
            created_by="system",
            reason_for_change="Check date validation",
        )

    with pytest.raises(ValidationError):
        DM(
            STUDYID="STUDY-01",
            DOMAIN="DM",
            USUBJID="STUDY-01-001",
            RFSTDTC="2026/07/29",  # slash is not allowed
            SEX="F",
            RACE="WHITE",
            ARM="Placebo",
            created_by="system",
            reason_for_change="Check date validation",
        )


def test_null_flavor_enum_membership():
    """
    Verify standard HL7/CDISC Null Flavor enum membership and values.
    """
    assert NullFlavor.NI.value == "NI"
    assert NullFlavor.NA.value == "NA"
    assert NullFlavor.UNK.value == "UNK"
    assert NullFlavor.ASKU.value == "ASKU"
    assert NullFlavor.NASK.value == "NASK"
    assert NullFlavor.MSNG.value == "MSNG"


def test_sdtm_domain_enum_membership():
    """
    Verify SDTMDomain enum membership and values.
    """
    assert SDTMDomain.DM.value == "DM"
    assert SDTMDomain.AE.value == "AE"
    assert SDTMDomain.VS.value == "VS"
    assert SDTMDomain.LB.value == "LB"
    assert SDTMDomain.CM.value == "CM"


def test_models_optional_nones():
    """
    Verify that passing explicit None or missing expected/permissible fields works perfectly.
    """
    dm = DM(
        STUDYID="STUDY-01",
        USUBJID="STUDY-01-001",
        SUBJID=None,
        RFSTDTC=None,
        SEX="F",
        RACE="WHITE",
        ARM="Placebo",
        created_by="system",
        reason_for_change="Check Nones",
    )
    assert dm.SUBJID is None
    assert dm.RFSTDTC is None

    ae = AE(
        STUDYID="STUDY-01",
        USUBJID="STUDY-01-001",
        AESEQ=1,
        AETERM="Headache",
        AESEV=None,
        AESER="N",
        created_by="system",
        reason_for_change="Check Nones",
    )
    assert ae.AESEV is None
    assert ae.AESER == AESeriousness.N
