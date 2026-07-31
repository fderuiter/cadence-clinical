from typing import Dict, List

from pydantic import BaseModel, Field


class VariableMapping(BaseModel):
    """Represents a declarative mapping rule for an SDTM variable from CDASH sources."""

    domain: str = Field(..., description="Target SDTM domain (e.g., 'DM', 'AE')")
    variable_name: str = Field(
        ..., description="Target SDTM variable name (e.g., 'USUBJID')"
    )
    source_field: str = Field(
        ..., description="CDASH source field path or logic (e.g., 'Subject.UID')"
    )
    data_type: str = Field(..., description="Data type ('Char' or 'Num')")
    transformation_kind: str = Field(
        ...,
        description="Kind of mapping transformation (e.g., 'DIRECT', 'CONCATENATION', 'COMPUTED', 'FIXED', 'CONTROLLED_TERMINOLOGY')",
    )
    rule_description: str = Field(
        ..., description="Detailed text explanation of the transformation logic"
    )


# The canonical collection of SDTM variable mappings for DM, AE, VS, LB, and MH domains.
SDTM_MAPPINGS: List[VariableMapping] = [
    # --- DM (Demographics) ---
    VariableMapping(
        domain="DM",
        variable_name="STUDYID",
        source_field="Metadata.StudyID",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Direct copy of the unique protocol identifier.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="USUBJID",
        source_field="Subject.UID",
        data_type="Char",
        transformation_kind="CONCATENATION",
        rule_description="Concatenation of STUDYID, Site ID (SITEID), and Subject ID (SUBJID): STUDYID-SITEID-SUBJID.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="SUBJID",
        source_field="Subject.ID",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Direct copy of the site-specific subject number.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="RFSTDTC",
        source_field="EX.EXSTDTC",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Date/Time of first study treatment exposure. Imputed as ISO 8601 string.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="RFENDTC",
        source_field="DS.DSSTDTC",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Date/Time of last study exposure or study completion/withdrawal.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="BRTHDTC",
        source_field="DM.BRTHDTC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Date of birth in ISO 8601 format (YYYY-MM-DD). Partial dates allowed.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="AGE",
        source_field="Computed",
        data_type="Num",
        transformation_kind="COMPUTED",
        rule_description="Computed as floor((RFSTDTC - BRTHDTC) / 365.25).",
    ),
    VariableMapping(
        domain="DM",
        variable_name="AGEU",
        source_field="Fixed Value",
        data_type="Char",
        transformation_kind="FIXED",
        rule_description="Static value set to 'YEARS'.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="SEX",
        source_field="DM.SEX",
        data_type="Char",
        transformation_kind="CONTROLLED_TERMINOLOGY",
        rule_description="Checked against CDISC Controlled Terminology ('M', 'F', 'U').",
    ),
    VariableMapping(
        domain="DM",
        variable_name="RACE",
        source_field="DM.RACE",
        data_type="Char",
        transformation_kind="CONTROLLED_TERMINOLOGY",
        rule_description="Checked against CDISC Controlled Terminology. If multiple checked, set to 'MULTIPLE'.",
    ),
    VariableMapping(
        domain="DM",
        variable_name="ARM",
        source_field="Randomization.Arm",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Set to the randomized trial arm description. Defaults to 'SCREEN FAILURE' if not randomized.",
    ),
    # --- AE (Adverse Events) ---
    VariableMapping(
        domain="AE",
        variable_name="STUDYID",
        source_field="Metadata.StudyID",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Direct copy of the unique protocol identifier.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="USUBJID",
        source_field="Subject.UID",
        data_type="Char",
        transformation_kind="CONCATENATION",
        rule_description="Derived unique subject identifier.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AESEQ",
        source_field="System Generated",
        data_type="Num",
        transformation_kind="COMPUTED",
        rule_description="Monotonically increasing sequence integer per subject, sorted by AESTDTC.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AETERM",
        source_field="AE.AETERM",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Verbatim term of the adverse event as entered by investigator.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AELOC",
        source_field="AE.AELOC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Anatomical location, if applicable. Maps to custom qualifiers if non-standard.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AELDTC",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Date/Time of local adverse event onset (captured on device).",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AESTDTC",
        source_field="AE.AESTDTC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Start date/time of adverse event in ISO 8601 format. Partial dates allowed.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AEENDTC",
        source_field="AE.AEENDTC",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="End date/time of adverse event. If ongoing, set to null and flag AEENGRY as 'ONGOING'.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AESEV",
        source_field="AE.AESEV",
        data_type="Char",
        transformation_kind="CONTROLLED_TERMINOLOGY",
        rule_description="Mapped to CDISC CT: 'MILD', 'MODERATE', 'SEVERE'.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AESER",
        source_field="AE.AESER",
        data_type="Char",
        transformation_kind="CONTROLLED_TERMINOLOGY",
        rule_description="Serious Adverse Event flag: 'Y' or 'N'.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AEREL",
        source_field="AE.AEREL",
        data_type="Char",
        transformation_kind="CONTROLLED_TERMINOLOGY",
        rule_description="Relationship to treatment: 'RELATED', 'NOT RELATED', 'POSSIBLY RELATED'.",
    ),
    VariableMapping(
        domain="AE",
        variable_name="AEOUT",
        source_field="AE.AEOUT",
        data_type="Char",
        transformation_kind="CONTROLLED_TERMINOLOGY",
        rule_description="Outcome: 'RECOVERED/RESOLVED', 'RECOVERING/RESOLVING', 'FATAL', etc.",
    ),
    # --- VS (Vital Signs) ---
    VariableMapping(
        domain="VS",
        variable_name="STUDYID",
        source_field="Metadata.StudyID",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Direct copy of unique protocol identifier.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="USUBJID",
        source_field="Subject.UID",
        data_type="Char",
        transformation_kind="CONCATENATION",
        rule_description="Derived unique subject identifier.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSSEQ",
        source_field="System Generated",
        data_type="Num",
        transformation_kind="COMPUTED",
        rule_description="Monotonically increasing sequence integer per subject, sorted by VSDTC.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSTESTCD",
        source_field="VS.VSTESTCD",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Short test code (e.g., 'SYSBP', 'DIABP', 'PULSE', 'TEMP', 'HEIGHT', 'WEIGHT').",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSTEST",
        source_field="VS.VSTEST",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Full test name (e.g., 'Systolic Blood Pressure', 'Pulse Rate').",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSORRES",
        source_field="VS.VSORRES",
        data_type="Num",
        transformation_kind="DIRECT",
        rule_description="Original verbatim result captured in eCRF.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSORRESU",
        source_field="VS.VSORRESU",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Original unit (e.g., 'mmHg', 'beats/min', '[degF]').",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSSTRESC",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Standardized result represented as a character string.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSSTRESN",
        source_field="Computed",
        data_type="Num",
        transformation_kind="COMPUTED",
        rule_description="Standardized numeric result. Converted to UCUM standards.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSSTRESU",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Standardized unit derived from UCUM target standards (e.g., 'mmHg', 'Cel').",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSPOS",
        source_field="VS.VSPOS",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Subject position during measurement: 'SUPINE', 'SITTING', 'STANDING'.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSDTC",
        source_field="VS.VSDTC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Date/Time of vital signs measurement in ISO 8601 format.",
    ),
    VariableMapping(
        domain="VS",
        variable_name="VSBLFL",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Vital signs Baseline Flag: Set to 'Y' if baseline record, otherwise null.",
    ),
    # --- LB (Laboratory Findings) ---
    VariableMapping(
        domain="LB",
        variable_name="STUDYID",
        source_field="Metadata.StudyID",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Direct copy of unique protocol identifier.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="USUBJID",
        source_field="Subject.UID",
        data_type="Char",
        transformation_kind="CONCATENATION",
        rule_description="Derived unique subject identifier.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBSEQ",
        source_field="System Generated",
        data_type="Num",
        transformation_kind="COMPUTED",
        rule_description="Monotonically increasing sequence integer, sorted by LBDTC and LBSPEC.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBTESTCD",
        source_field="LB.LBTESTCD",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Lab test short code (e.g., 'ALT', 'AST', 'CREAT', 'GLUC', 'HEMOG').",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBTEST",
        source_field="LB.LBTEST",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Full lab test name (e.g., 'Alanine Aminotransferase', 'Glucose').",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBORRES",
        source_field="LB.LBORRES",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Original verbatim result (alphanumeric).",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBORRESU",
        source_field="LB.LBORRESU",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Original result unit.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBSTRESC",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Standardized character result.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBSTRESN",
        source_field="Computed",
        data_type="Num",
        transformation_kind="COMPUTED",
        rule_description="Standardized numeric result. Standardized using UCUM matrices.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBSTRESU",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Standardized unit (e.g., 'g/L', 'umol/L').",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBNRIND",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="Normal range reference indicator: 'LOW', 'NORMAL', 'HIGH'.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBDTC",
        source_field="LB.LBDTC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Date/Time of specimen collection in ISO 8601 format.",
    ),
    VariableMapping(
        domain="LB",
        variable_name="LBLOINC",
        source_field="LB.LBLOINC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="LOINC code mapped to the lab test.",
    ),
    # --- MH (Medical History) ---
    VariableMapping(
        domain="MH",
        variable_name="STUDYID",
        source_field="Metadata.StudyID",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Direct copy of unique protocol identifier.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="USUBJID",
        source_field="Subject.UID",
        data_type="Char",
        transformation_kind="CONCATENATION",
        rule_description="Derived unique subject identifier.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="MHSEQ",
        source_field="System Generated",
        data_type="Num",
        transformation_kind="COMPUTED",
        rule_description="Monotonically increasing sequence integer, sorted by MHDTC.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="MHTERM",
        source_field="MH.MHTERM",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Medical history verbatim term.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="MHDECOD",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="MedDRA Preferred Term (PT) code derived from dictionary coding.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="MHBODSYS",
        source_field="Computed",
        data_type="Char",
        transformation_kind="COMPUTED",
        rule_description="MedDRA System Organ Class (SOC) description.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="MHSTDTC",
        source_field="MH.MHSTDTC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="Onset date of medical history condition in ISO 8601 format. Partial dates common.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="MHENDTC",
        source_field="MH.MHENDTC",
        data_type="Char",
        transformation_kind="DIRECT",
        rule_description="End date of medical history condition. Null if ongoing.",
    ),
    VariableMapping(
        domain="MH",
        variable_name="MHENRTP",
        source_field="MH.MHENRTP",
        data_type="Char",
        transformation_kind="CONTROLLED_TERMINOLOGY",
        rule_description="Relationship to study start: 'BEFORE', 'ONGOING'.",
    ),
]


def get_mappings_for_domain(domain: str) -> List[VariableMapping]:
    """Retrieves all variable mappings for a specific SDTM domain."""
    return [m for m in SDTM_MAPPINGS if m.domain.upper() == domain.upper()]


def get_mappings_by_domain() -> Dict[str, List[VariableMapping]]:
    """Groups all mappings by their target SDTM domain."""
    grouped: Dict[str, List[VariableMapping]] = {}
    for m in SDTM_MAPPINGS:
        grouped.setdefault(m.domain, []).append(m)
    return grouped
