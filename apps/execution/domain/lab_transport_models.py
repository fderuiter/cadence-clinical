from datetime import datetime

from pydantic import BaseModel, Field

from .lab_models import (
    CertificationStatusEnum,
    GradingScaleEnum,
    LabSourceEnum,
)


class LabReferenceRangeCreate(BaseModel):
    """Payload to create a new clinical laboratory reference range.

    Requirements: PRD-SYS-001, PRD-LAB-001
    """

    study_id: str = Field(
        ..., description="Unique protocol study identifier, e.g. 'STUDY-101'"
    )
    test_code: str = Field(
        ..., description="Standardized laboratory test code, e.g. 'WBC'"
    )
    test_name: str = Field(..., description="Descriptive name of the test parameter")
    source: LabSourceEnum = Field(
        ..., description="Source type of the range, either 'CENTRAL' or 'LOCAL'"
    )
    site_id: str | None = Field(
        None, description="Optional investigator site identifier"
    )
    unit: str = Field(..., description="The original/captured unit of measurement")
    normalized_unit: str = Field(
        ..., description="Standardized target unit of measurement"
    )
    sex_applicability: str = Field(
        ..., description="Sex applicability of the reference range"
    )
    ethnicity_applicability: str | None = Field(
        None, description="Optional ethnicity applicability constraint"
    )
    age_low: float | None = Field(
        None, description="Nullable lower bound of age applicability"
    )
    age_high: float | None = Field(
        None, description="Nullable upper bound of age applicability"
    )
    low_bound: float | None = Field(
        None, description="Nullable lower limit of normal range"
    )
    high_bound: float | None = Field(
        None, description="Nullable upper limit of normal range"
    )
    critical_low: float | None = Field(
        None, description="Nullable lower limit of critical/panic alert range"
    )
    critical_high: float | None = Field(
        None, description="Nullable upper limit of critical/panic alert range"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabReferenceRangeUpdate(BaseModel):
    """Payload to modify an existing laboratory reference range.

    Requirements: PRD-SYS-001, PRD-LAB-001
    """

    study_id: str | None = Field(None, description="Unique protocol study identifier")
    test_code: str | None = Field(None, description="Standardized laboratory test code")
    test_name: str | None = Field(
        None, description="Updated descriptive name of the test"
    )
    source: LabSourceEnum | None = Field(
        None, description="Updated source type of the range"
    )
    site_id: str | None = Field(
        None, description="Updated investigator site identifier"
    )
    unit: str | None = Field(None, description="Updated original unit of measurement")
    normalized_unit: str | None = Field(
        None, description="Updated standardized unit of measurement"
    )
    sex_applicability: str | None = Field(None, description="Updated sex applicability")
    ethnicity_applicability: str | None = Field(
        None, description="Updated ethnicity applicability"
    )
    age_low: float | None = Field(
        None, description="Updated lower bound of age applicability"
    )
    age_high: float | None = Field(
        None, description="Updated upper bound of age applicability"
    )
    low_bound: float | None = Field(
        None, description="Updated lower limit of normal range"
    )
    high_bound: float | None = Field(
        None, description="Updated upper limit of normal range"
    )
    critical_low: float | None = Field(
        None, description="Updated lower limit of critical alert range"
    )
    critical_high: float | None = Field(
        None, description="Updated upper limit of critical alert range"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabReferenceRangeResponse(BaseModel):
    """Response payload representing a registered laboratory reference range.

    Requirements: PRD-SYS-001, PRD-LAB-001
    """

    id: str = Field(..., description="Unique database identifier of the range")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Standardized lab test code")
    test_name: str = Field(..., description="Descriptive test name")
    source: LabSourceEnum = Field(..., description="Source type of the range")
    site_id: str | None = Field(
        None, description="Optional investigator site identifier"
    )
    unit: str = Field(..., description="Original unit of measurement")
    normalized_unit: str = Field(..., description="Standardized unit")
    sex_applicability: str = Field(..., description="Sex applicability")
    ethnicity_applicability: str | None = Field(
        None, description="Ethnicity applicability constraint"
    )
    age_low: float | None = Field(None, description="Lower age applicability limit")
    age_high: float | None = Field(None, description="Upper age applicability limit")
    low_bound: float | None = Field(None, description="Lower normal limit")
    high_bound: float | None = Field(None, description="Upper normal limit")
    critical_low: float | None = Field(None, description="Lower critical bound")
    critical_high: float | None = Field(None, description="Upper critical bound")
    version: int = Field(
        ..., description="Optimistic locking entity version identifier"
    )
    is_deleted: bool = Field(
        ..., description="Flag indicating if the record is soft-deleted"
    )


class LocalLabCertificationCreate(BaseModel):
    """Payload to register a new local laboratory accreditation or certification.

    Requirements: PRD-LAB-001
    """

    study_id: str = Field(..., description="Unique protocol study identifier")
    site_id: str = Field(..., description="Investigator site identifier")
    lab_name: str = Field(..., description="Laboratory facility name")
    lab_identifier: str | None = Field(None, description="Optional facility identifier")
    certification_number: str = Field(
        ..., description="CLIA, CAP, or ISO certification number"
    )
    accreditation_body: str = Field(
        ..., description="Accrediting agency (e.g. CLIA, CAP, ISO 15189)"
    )
    effective_date: datetime = Field(
        ..., description="Certification effective begin date"
    )
    expiration_date: datetime = Field(
        ..., description="Certification expiration end date"
    )
    director_name: str | None = Field(None, description="Name of laboratory director")
    is_accredited: bool = Field(default=True, description="Accreditation status flag")
    notes: str | None = Field(None, description="Operational notes")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LocalLabCertificationUpdate(BaseModel):
    """Payload to update an existing local laboratory certification.

    Requirements: PRD-LAB-001
    """

    lab_name: str | None = Field(None, description="Updated facility name")
    lab_identifier: str | None = Field(None, description="Updated facility identifier")
    certification_number: str | None = Field(
        None, description="Updated certification number"
    )
    accreditation_body: str | None = Field(
        None, description="Updated accrediting agency"
    )
    effective_date: datetime | None = Field(None, description="Updated effective date")
    expiration_date: datetime | None = Field(
        None, description="Updated expiration date"
    )
    status: CertificationStatusEnum | None = Field(
        None, description="Updated operational status"
    )
    director_name: str | None = Field(None, description="Updated laboratory director")
    is_accredited: bool | None = Field(None, description="Updated accreditation flag")
    notes: str | None = Field(None, description="Updated operational notes")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LocalLabCertificationResponse(BaseModel):
    """Response representing a local laboratory certification.

    Requirements: PRD-LAB-001
    """

    id: str = Field(..., description="Unique database identifier")
    study_id: str = Field(..., description="Unique protocol study identifier")
    site_id: str = Field(..., description="Investigator site identifier")
    lab_name: str = Field(..., description="Laboratory facility name")
    lab_identifier: str | None = Field(None, description="Optional facility identifier")
    certification_number: str = Field(..., description="Certification number")
    accreditation_body: str = Field(..., description="Accreditation body")
    effective_date: datetime = Field(..., description="Effective begin date")
    expiration_date: datetime = Field(..., description="Expiration end date")
    status: CertificationStatusEnum = Field(..., description="Status")
    director_name: str | None = Field(None, description="Laboratory director name")
    is_accredited: bool = Field(..., description="Accredited status")
    notes: str | None = Field(None, description="Notes")
    version: int = Field(..., description="Version index")
    is_deleted: bool = Field(..., description="Soft deletion flag")


class LocalLabCertificationVerifyRequest(BaseModel):
    """Request to verify local laboratory certification validity."""

    study_id: str = Field(..., description="Unique protocol study identifier")
    site_id: str = Field(..., description="Target site identifier")
    observation_date: datetime | None = Field(
        None, description="Observation collection date (defaults to current time)"
    )


class LocalLabCertificationVerifyResponse(BaseModel):
    """Response from local lab certification verification."""

    is_valid: bool = Field(
        ..., description="True if site has active, unexpired certification"
    )
    status: str = Field(..., description="Certification status code")
    site_id: str = Field(..., description="Site identifier")
    study_id: str = Field(..., description="Study identifier")
    certification_number: str | None = Field(
        None, description="Certification number if found"
    )
    accreditation_body: str | None = Field(
        None, description="Accreditation body if found"
    )
    expiration_date: datetime | None = Field(
        None, description="Expiration date if found"
    )
    days_until_expiration: int | None = Field(
        None, description="Days remaining until expiration"
    )
    uncalibrated_safeguard_triggered: bool = Field(
        default=False,
        description="True if safeguard fired due to expired or missing certification",
    )
    message: str = Field(..., description="Human-readable status summary")


class StagedAdverseEventReviewResponse(BaseModel):
    """Response payload for a staged Grade 3/4 AE laboratory toxicity review item."""

    id: str = Field(..., description="Unique review record ID")
    study_id: str = Field(..., description="Trial study identifier")
    site_id: str | None = Field(None, description="Investigator site identifier")
    subject_id: str = Field(..., description="Subject identifier")
    visit_id: str | None = Field(None, description="Visit identifier")
    observation_id: str | None = Field(
        None, description="Linked ClinicalObservation ID"
    )
    form_submission_id: str | None = Field(
        None, description="Drafted AE FormSubmission ID"
    )
    query_id: str | None = Field(
        None, description="Linked discrepancy ClinicalQuery ID"
    )
    test_code: str = Field(..., description="Laboratory test code")
    test_name: str = Field(..., description="Descriptive test name")
    lab_value: float = Field(..., description="Observed lab measurement value")
    unit: str = Field(..., description="Unit of measurement")
    ctcae_grade: int = Field(..., description="Toxicity grade (3 or 4)")
    grading_scale: str = Field(..., description="Grading scale standard")
    toxicity_term: str = Field(..., description="Standard adverse event term")
    clinical_description: str = Field(..., description="Clinical severity description")
    is_hys_law_warning: bool = Field(..., description="Potential Hy's Law DILI alert")
    is_delta_check_alert: bool = Field(..., description="Acute delta check alert")
    delta_percentage: float | None = Field(
        None, description="Delta change percentage from baseline"
    )
    review_status: str = Field(..., description="Current review status")
    reviewed_by: str | None = Field(
        None, description="User ID of reviewing Investigator"
    )
    reviewed_at: datetime | None = Field(None, description="Review timestamp")
    investigator_notes: str | None = Field(
        None, description="Investigator clinical notes"
    )
    version: int = Field(..., description="Version index")
    is_deleted: bool = Field(..., description="Soft deletion flag")
    created_at: datetime | None = Field(None, description="Creation timestamp")


class StagedAdverseEventActionRequest(BaseModel):
    """Payload to perform an Investigator action on a staged Grade 3/4 AE."""

    action: str = Field(
        ...,
        description="Action to take: 'CONFIRM' (promote to formal AE), 'DISMISS' (not clinically significant), or 'ESCALATE_SAE'",
    )
    investigator_notes: str | None = Field(
        None, description="Clinical commentary and rationale"
    )
    electronic_signature_password: str | None = Field(
        None, description="21 CFR Part 11 authentication credential"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class StagedAdverseEventActionResponse(BaseModel):
    """Response from executing an Investigator action on a staged AE."""

    id: str = Field(..., description="Review ID")
    review_status: str = Field(..., description="Updated review status")
    action_taken: str = Field(..., description="Action that was executed")
    form_submission_id: str | None = Field(
        None, description="FormSubmission ID if confirmed"
    )
    query_id: str | None = Field(None, description="ClinicalQuery ID updated")
    message: str = Field(..., description="Result message")
    timestamp: datetime = Field(..., description="Action timestamp")


class LabToxicityEvaluateRequest(BaseModel):
    """Request payload to simulate/evaluate laboratory toxicity grading in real time."""

    test_code: str = Field(
        ...,
        description="Standardized laboratory test code (e.g. 'ALT', 'CREAT', 'ANC')",
    )
    test_name: str | None = Field(None, description="Optional descriptive test name")
    value: float = Field(..., description="Observed numeric test measurement")
    unit: str = Field(..., description="Reporting unit of measurement")
    ref_low: float | None = Field(
        None, description="Lower limit of demographic normal range (LLN)"
    )
    ref_high: float | None = Field(
        None, description="Upper limit of demographic normal range (ULN)"
    )
    baseline_value: float | None = Field(
        None, description="Optional subject baseline value for acute delta calculation"
    )
    previous_value: float | None = Field(
        None, description="Optional previous observation value"
    )
    grading_scale: GradingScaleEnum = Field(
        default=GradingScaleEnum.CTCAE_V5,
        description="Target grading scale: 'CTCAE_V5' or 'DAIDS_V2_1'",
    )
    # Additional co-analytes for Hy's Law compound assessment
    alt_value: float | None = Field(
        None, description="Co-analyte ALT value for Hy's Law evaluation"
    )
    ast_value: float | None = Field(
        None, description="Co-analyte AST value for Hy's Law evaluation"
    )
    bili_value: float | None = Field(
        None, description="Co-analyte Total Bilirubin value for Hy's Law evaluation"
    )
    alkp_value: float | None = Field(
        None,
        description="Co-analyte Alkaline Phosphatase value for Hy's Law evaluation",
    )


class LabToxicityEvaluateResponse(BaseModel):
    """Response payload with computed CTCAE / DAIDS toxicity grade and alert flags."""

    test_code: str = Field(..., description="Analyzed laboratory test code")
    test_name: str = Field(..., description="Descriptive test name")
    observed_value: float = Field(..., description="Evaluated measurement value")
    unit: str = Field(..., description="Unit of measurement")
    ctcae_grade: int = Field(
        ..., description="Computed severity grade integer (0 to 4)"
    )
    grading_scale: str = Field(..., description="Grading scale applied")
    toxicity_description: str = Field(
        ..., description="Detailed clinical severity criterion description"
    )
    is_critical_ae_trigger: bool = Field(
        ..., description="True if Grade >= 3 triggering automated AE drafting"
    )
    is_hys_law_warning: bool = Field(
        ..., description="True if Hy's Law criteria for severe DILI are met"
    )
    is_delta_check_alert: bool = Field(
        ..., description="True if acute delta threshold exceeded"
    )
    delta_percentage: float | None = Field(
        None, description="Calculated percentage deviation from baseline"
    )


class LoincMappingItem(BaseModel):
    """LOINC dictionary mapping metadata item."""

    test_code: str = Field(..., description="Standardized internal analyte code")
    test_name: str = Field(..., description="Analyte name")
    loinc_code: str = Field(..., description="Standard LOINC code")
    standard_unit: str = Field(..., description="Standardized SI/UCUM unit")
    category: str = Field(..., description="Clinical laboratory domain category")
    molecular_weight: float | None = Field(
        None, description="Molecular weight (g/mol) for molar/mass conversions"
    )


class LoincCatalogResponse(BaseModel):
    """Response payload containing standardized LOINC mappings."""

    total_count: int = Field(..., description="Total mapped LOINC test codes")
    items: list[LoincMappingItem] = Field(
        ..., description="List of mapped LOINC analyte items"
    )


class LoincConvertRequest(BaseModel):
    """Request to convert laboratory value between conventional and SI units."""

    test_code: str = Field(..., description="Laboratory analyte code")
    from_unit: str = Field(..., description="Source unit")
    to_unit: str = Field(..., description="Target unit")
    value: float = Field(..., description="Numeric value to convert")


class LoincConvertResponse(BaseModel):
    """Response from mathematical laboratory unit conversion."""

    test_code: str = Field(..., description="Analyte test code")
    from_unit: str = Field(..., description="Original source unit")
    to_unit: str = Field(..., description="Target converted unit")
    original_value: float = Field(..., description="Original input value")
    converted_value: float = Field(..., description="Computed converted value")
    factor: float = Field(..., description="Multiplicative conversion factor")
    offset: float | None = Field(None, description="Additive offset if applicable")
    loinc_code: str | None = Field(
        None, description="Associated LOINC code if resolved"
    )


class LabTestMasterCreate(BaseModel):
    """Payload to create a new laboratory test master catalog entry.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(
        ..., description="Unique standardized test code, e.g. 'HEMOGLOBIN'"
    )
    test_name: str = Field(..., description="Descriptive test name")
    default_unit: str = Field(..., description="Default/Captured unit of measurement")
    normalized_unit: str = Field(..., description="Standardized normalized target unit")
    loinc_code: str | None = Field(
        None, description="Optional LOINC dictionary standard code"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabTestMasterResponse(BaseModel):
    """Response payload representing a registered laboratory test master catalog entry.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique database identifier of the master")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Unique standardized test code")
    test_name: str = Field(..., description="Descriptive test name")
    default_unit: str = Field(..., description="Default unit of measurement")
    normalized_unit: str = Field(..., description="Standardized normalized target unit")
    loinc_code: str | None = Field(None, description="Optional LOINC standard code")
    created_at: datetime | None = Field(
        None, description="Chronological creation timestamp"
    )
    created_by: str | None = Field(
        None, description="Identifier of the user who created this master"
    )
    reason_for_change: str | None = Field(
        None, description="GxP Part 11 justification description"
    )
    version_index: int = Field(..., description="Sequence row version identifier")
    version: int = Field(
        ..., description="Optimistic locking entity version identifier"
    )
    is_deleted: bool = Field(
        ..., description="Flag indicating if the record is soft-deleted"
    )


class LabUnitConversionCreate(BaseModel):
    """Payload to create a new laboratory unit conversion factor record.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(
        ..., description="Unique standardized test code, e.g. 'HEMOGLOBIN'"
    )
    from_unit: str = Field(..., description="Original unit of measurement")
    to_unit: str = Field(..., description="Target unit of measurement")
    factor: float = Field(
        ..., description="Multiplicative conversion multiplier factor"
    )
    offset: float | None = Field(None, description="Optional additive offset value")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class LabUnitConversionResponse(BaseModel):
    """Response payload representing a registered laboratory unit conversion factor record.

    Requirements: PRD-SYS-001
    """

    id: str = Field(..., description="Unique database identifier of the conversion")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Unique standardized test code")
    from_unit: str = Field(..., description="Original unit of measurement")
    to_unit: str = Field(..., description="Target unit of measurement")
    factor: float = Field(
        ..., description="Multiplicative conversion multiplier factor"
    )
    offset: float | None = Field(None, description="Optional additive offset value")
    created_at: datetime | None = Field(
        None, description="Chronological creation timestamp"
    )
    created_by: str | None = Field(
        None, description="Identifier of the user who created this conversion"
    )
    reason_for_change: str | None = Field(
        None, description="GxP Part 11 justification description"
    )
    version_index: int = Field(..., description="Sequence row version identifier")
    version: int = Field(
        ..., description="Optimistic locking entity version identifier"
    )
    is_deleted: bool = Field(
        ..., description="Flag indicating if the record is soft-deleted"
    )


class LabRangeRecalculateRequest(BaseModel):
    """Pydantic schema for triggering lab range recalculations.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(
        ..., description="Standardized laboratory test code, e.g. 'WBC'"
    )


class LabRangeRecalculateResponse(BaseModel):
    """Pydantic schema returning recalculation status.

    Requirements: PRD-SYS-001
    """

    status: str = Field(..., description="Status of the recalculation action")
    study_id: str = Field(..., description="Unique trial study identifier")
    test_code: str = Field(..., description="Standardized laboratory test code")
    updated_count: int = Field(
        ..., description="Total number of impacted records successfully updated"
    )
