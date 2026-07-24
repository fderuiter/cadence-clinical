import enum
import uuid
from datetime import datetime

from sqlalchemy import (
    JSON,
    Boolean,
    CheckConstraint,
    DateTime,
    Enum,
    Float,
    Index,
    Integer,
    String,
    UniqueConstraint,
    func,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, validates

from apps.execution.subject_lifecycle import (
    LockedFactorMutationError,
    guard_subject_transition,
)


class QueryStatus(str, enum.Enum):
    NONE = "NONE"
    CANDIDATE = "CANDIDATE"
    OPEN = "OPEN"
    ANSWERED = "ANSWERED"
    CLOSED = "CLOSED"
    REOPENED = "REOPENED"
    CANCELLED = "CANCELLED"


class DictionaryType(str, enum.Enum):
    MEDDRA = "MEDDRA"
    WHODRUG = "WHODRUG"
    LOINC = "LOINC"
    SNOMED = "SNOMED"


class ImportState(str, enum.Enum):
    PENDING = "PENDING"
    PROCESSING = "PROCESSING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"


class CodingState(str, enum.Enum):
    UNCODED = "UNCODED"
    SUGGESTED = "SUGGESTED"
    CODED = "CODED"
    RECODING_REQUIRED = "RECODING_REQUIRED"


class RecodingState(str, enum.Enum):
    NONE = "NONE"
    PENDING = "PENDING"
    COMPLETED = "COMPLETED"
    CANCELLED = "CANCELLED"


class Base(DeclarativeBase):
    pass


class AuditLog(Base):
    __tablename__ = "audit_logs"
    __table_args__ = {"schema": "audit_schema"}

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    table_name: Mapped[str] = mapped_column(String(255), nullable=False)
    record_id: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # INSERT, UPDATE, DELETE
    user_id: Mapped[str] = mapped_column(String(255), nullable=True)
    ip_address: Mapped[str] = mapped_column(String(45), nullable=True)
    timestamp: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    old_values: Mapped[dict] = mapped_column(JSON, nullable=True)
    new_values: Mapped[dict] = mapped_column(JSON, nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1)
    change_reason: Mapped[str] = mapped_column(String(255), nullable=True)
    cryptographic_seal: Mapped[str] = mapped_column(String(64), nullable=True)


class AuditLedgerSeal(Base):
    __tablename__ = "audit_ledger_seals"
    __table_args__ = {"schema": "audit_schema"}

    block_index: Mapped[int] = mapped_column(
        Integer, primary_key=True, autoincrement=True
    )
    previous_block_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    current_block_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    timestamp: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    sealed_record_count: Mapped[int] = mapped_column(Integer, nullable=False)
    merkle_root_hash: Mapped[str] = mapped_column(String(64), nullable=False)


class AuditedModel(Base):
    __abstract__ = True

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    version: Mapped[int] = mapped_column(Integer, default=1)
    is_deleted: Mapped[bool] = mapped_column(Boolean, default=False)

    def __init__(self, **kwargs):
        if "id" not in kwargs:
            kwargs["id"] = str(uuid.uuid4())
        super().__init__(**kwargs)


class TranslationJob(AuditedModel):
    """Represents an asynchronous study translation job.

    Inherits from AuditedModel to maintain an immutable audit log of status changes and generated payloads.

    Attributes:
        study_id (str): The unique identifier of the source study.
        status (str): The execution status (e.g., 'PENDING', 'COMPLETED', 'FAILED').
        odm_payload (str): The generated CDISC ODM XML payload.
        openrosa_payload (str): The generated OpenRosa XML layout payload.
        error_message (str): An error message if the translation failed.
    """

    __tablename__ = "translation_jobs"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # PENDING, COMPLETED, FAILED
    odm_payload: Mapped[str] = mapped_column(String, nullable=True)
    openrosa_payload: Mapped[str] = mapped_column(String, nullable=True)
    error_message: Mapped[str] = mapped_column(String, nullable=True)


class ClinicalSubject(AuditedModel):
    """Represents a pseudonymized clinical subject.

    This class stores subject identification details strictly without storing direct, unencrypted
    personally identifiable information (PII) to comply with HIPAA, GDPR, and GxP standards.

    Attributes:
        subject_id (str): The unique pseudonymized identifier of the subject (e.g. SUBJ-001).
        study_id (str): The identifier of the clinical trial study.
        encrypted_demographics (str): Securely encrypted demographic details if provided.
        status (str): The lifecycle state of the subject (defaults to "SCREENING").
        strat_factors (dict): JSON-based stratification factors.
        is_unblinded (bool): Flag indicating if the subject is unblinded.
        unblinded_at (datetime): Timestamp when the subject was unblinded.
        unblinded_by (str): ID of the user who unblinded the subject.
        unblinded_reason (str): Reason for emergency unblinding.
        withdrawn_at (datetime): Timestamp when the subject was withdrawn.
        withdrawal_reason (str): Reason for withdrawal of consent.
        randomization_id (str): ID of the subject's treatment assignment record.
        kit_reference (str): Reference to the investigational product / kit reference.
    """

    __tablename__ = "clinical_subjects"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    encrypted_demographics: Mapped[str] = mapped_column(String, nullable=True)

    status: Mapped[str] = mapped_column(String(50), default="SCREENING", nullable=False)
    strat_factors: Mapped[dict] = mapped_column(JSON, nullable=True)
    is_unblinded: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    unblinded_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    unblinded_by: Mapped[str] = mapped_column(String(255), nullable=True)
    unblinded_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    withdrawn_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    withdrawal_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    randomization_id: Mapped[str] = mapped_column(String(36), nullable=True)
    kit_reference: Mapped[str] = mapped_column(String(255), nullable=True)

    @validates("status")
    def validate_status(self, key, value):
        """Validates that transitions of status obey the allowed-transition guard."""
        curr = getattr(self, "status", None)
        if curr != value:
            guard_subject_transition(curr, value)
        return value

    @validates("strat_factors")
    def validate_strat_factors(self, key, value):
        """Validates that stratification factors are locked and cannot be modified once randomized."""
        curr_status = getattr(self, "status", None)
        if curr_status in (
            "RANDOMIZED",
            "ACTIVE",
            "COMPLETED",
            "UNBLINDED",
            "WITHDRAWN",
        ):
            if self.strat_factors is not None and self.strat_factors != value:
                raise LockedFactorMutationError()
        return value

    def randomize(
        self, randomization_id: str, kit_reference: str, strat_factors: dict
    ) -> None:
        """Assigns randomization details and transitions the subject to the RANDOMIZED state."""
        self.strat_factors = strat_factors
        self.status = "RANDOMIZED"
        self.randomization_id = randomization_id
        self.kit_reference = kit_reference

    def unblind(self, unblinded_by: str, reason: str) -> None:
        """Transitions the subject to the UNBLINDED state and records safety/audit details."""
        self.status = "UNBLINDED"
        self.is_unblinded = True
        self.unblinded_at = datetime.now()
        self.unblinded_by = unblinded_by
        self.unblinded_reason = reason

    def withdraw(self, reason: str) -> None:
        """Transitions the subject to the WITHDRAWN state and locks further progression."""
        self.status = "WITHDRAWN"
        self.withdrawn_at = datetime.now()
        self.withdrawal_reason = reason


class ClinicalVisit(AuditedModel):
    """Represents a scheduled clinical event/visit for a subject.

    Maintains scheduled trial encounters like Screening, Baseline, Week 4, etc.

    Attributes:
        subject_id (str): The unique identifier of the subject.
        visit_name (str): The name or description of the visit.
        visit_date (datetime): The actual datetime of the visit encounter.
        study_id (str): The identifier of the clinical trial study.
    """

    __tablename__ = "clinical_visits"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_name: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_date: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)


class ClinicalObservation(AuditedModel):
    """Represents a specific clinical trial measurement observation.

    Stores normalized values, original and normalized units, and outlier flags
    for individual parameters (e.g., vital signs, lab test measurements).

    Attributes:
        subject_id (str): The unique identifier of the subject.
        visit_id (str): Reference to the clinical visit ID if applicable.
        domain (str): The CDISC domain code (e.g., 'VS', 'LB').
        observation_date (datetime): The datetime when the measurement was captured.
        test_code (str): The standard CDASH/SDTM test code (e.g., 'SYSBP', 'TEMP').
        test_name (str): The full descriptive name of the test parameter.
        value (float): The raw numeric value of the observation if applicable.
        value_string (str): The raw nominal or text value of the observation.
        unit (str): The standard UCUM or clinical unit.
        normalized_value (float): The normalized numeric value in standard reference units.
        normalized_unit (str): The standard normalized UCUM unit.
        is_outlier (bool): Flag indicating if this is a statistical outlier within the cohort.
        is_sdv_verified (bool): Non-null boolean indicating if field-level SDV is verified.
        sdv_verified_by (str): Nullable identifier of the verifier/CRA.
        sdv_verified_at (datetime): Nullable timestamp of SDV verification.
        page_id (str): Nullable eCRF page/form grouping key.
    """

    __tablename__ = "clinical_observations"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    visit_id: Mapped[str] = mapped_column(String(255), nullable=True)
    domain: Mapped[str] = mapped_column(String(50), nullable=False)
    observation_date: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    test_code: Mapped[str] = mapped_column(String(100), nullable=False)
    test_name: Mapped[str] = mapped_column(String(255), nullable=False)
    value: Mapped[float] = mapped_column(Float, nullable=True)
    value_string: Mapped[str] = mapped_column(String, nullable=True)
    unit: Mapped[str] = mapped_column(String(50), nullable=True)
    normalized_value: Mapped[float] = mapped_column(Float, nullable=True)
    normalized_unit: Mapped[str] = mapped_column(String(50), nullable=True)
    is_outlier: Mapped[bool] = mapped_column(Boolean, default=False)
    is_sdv_verified: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    sdv_verified_by: Mapped[str] = mapped_column(String(255), nullable=True)
    sdv_verified_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    page_id: Mapped[str] = mapped_column(String(255), nullable=True)

    # Lab reference range snapshot fields
    lab_source: Mapped[str] = mapped_column(String(50), nullable=True)
    lab_site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    lab_indicator: Mapped[str] = mapped_column(String(50), nullable=True)
    lab_out_of_range: Mapped[bool] = mapped_column(Boolean, nullable=True)
    matched_normal_bounds: Mapped[str] = mapped_column(String(255), nullable=True)


class ClinicalQuery(AuditedModel):
    """Represents a clinical query state record for GxP data discrepancy tracking.

    Inherits from AuditedModel to maintain an immutable audit log of status changes and history,
    and prevent hard deletions through automatic trigger-based protection.

    Attributes:
        study_id (str): The unique identifier of the study.
        subject_id (str): The unique identifier of the subject (target coordinate).
        visit_id (str): Reference to the clinical visit ID if applicable (target coordinate).
        domain (str): The CDISC domain code if applicable (target coordinate).
        test_code (str): The standard CDASH/SDTM test code (target coordinate).
        status (str): The query status (e.g., 'NONE', 'OPEN', 'ANSWERED', 'CLOSED', 'REOPENED').
        explanation (str): The user-inputted explanation/justification when opening the query.
        response (str): The investigator-submitted response when answering the query.
        created_at (datetime): Timestamp of query creation.
        updated_at (datetime): Timestamp of last query modification.
    """

    __tablename__ = "clinical_queries"
    __table_args__ = (
        Index(
            "idx_query_target",
            "study_id",
            "subject_id",
            "visit_id",
            "domain",
            "test_code",
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    subject_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    visit_id: Mapped[str] = mapped_column(String(255), index=True, nullable=True)
    domain: Mapped[str] = mapped_column(String(50), index=True, nullable=True)
    test_code: Mapped[str] = mapped_column(String(100), index=True, nullable=False)

    status: Mapped[str] = mapped_column(String(50), default="NONE", nullable=False)
    explanation: Mapped[str] = mapped_column(String(255), nullable=True)
    response: Mapped[str] = mapped_column(String(255), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    updated_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), onupdate=func.now()
    )

    observation_id: Mapped[str] = mapped_column(String(255), index=True, nullable=True)
    field_link: Mapped[str] = mapped_column(String(255), index=True, nullable=True)
    message: Mapped[str] = mapped_column(String(1000), nullable=True)
    origin: Mapped[str] = mapped_column(String(50), nullable=True)
    priority: Mapped[str] = mapped_column(String(50), nullable=True)
    rule_id: Mapped[str] = mapped_column(String(255), nullable=True)
    created_by: Mapped[str] = mapped_column(String(255), nullable=True)
    responder: Mapped[str] = mapped_column(String(255), nullable=True)
    resolver: Mapped[str] = mapped_column(String(255), nullable=True)
    resolved_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    cancellation_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    escalated_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)


class SDVSignOff(AuditedModel):
    """Represents an aggregate sign-off record for SDV/TSDV verification.

    Maintains page-, visit- or field-level verification signatures and drop states
    for auditing and GxP traceability.

    Attributes:
        scope (str): The verification scope (FIELD, PAGE, or VISIT).
        target_id (str): The identifier of the verified entity.
        subject_id (str): The clinical subject identifier.
        study_id (str): The clinical trial study identifier.
        is_verified (bool): Non-null boolean indicating if aggregate verification is active.
        verified_by (str): Nullable identifier of the verifier/CRA.
        verified_at (datetime): Nullable timestamp of verification.
        dropped_reason (str): Nullable reason text for dropping/rescinding verification.
        dropped_at (datetime): Nullable timestamp when verification was dropped/rescinded.
    """

    __tablename__ = "sdv_sign_offs"

    scope: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # FIELD, PAGE, or VISIT
    target_id: Mapped[str] = mapped_column(String(255), nullable=False)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    is_verified: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    verified_by: Mapped[str] = mapped_column(String(255), nullable=True)
    verified_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    dropped_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    dropped_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)


class TSDVConfig(AuditedModel):
    """Represents the Targeted SDV (TSDV) sampling configuration for a study.

    Maintains study-specific parameters governing subject-based or field-based
    sampling models and domain-level SDV requirements.

    Attributes:
        study_id (str): Unique clinical trial study identifier.
        sampling_model (str): SUBJECT_BASED, FIELD_BASED, or COMBINED.
        initial_full_sdv_subject_count (int): Count of subjects requiring full SDV before sampling begins.
        random_sample_percentage (float): Probability percentage of standard random subject sampling.
        full_sdv_domains (list): JSON list of SDTM domain codes requiring 100% full SDV.
        safety_endpoints (list): JSON list of safety endpoint variables/domains requiring 100% full SDV.
        zero_sdv_domains (list): JSON list of SDTM domain codes needing zero (no) SDV.
        trial_random_seed (int): Integer seed value for stable, deterministic pseudo-random sampling.
    """

    __tablename__ = "tsdv_configs"

    study_id: Mapped[str] = mapped_column(String(255), unique=True, nullable=False)
    sampling_model: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # SUBJECT_BASED, FIELD_BASED, or COMBINED
    initial_full_sdv_subject_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    random_sample_percentage: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    full_sdv_domains: Mapped[list] = mapped_column(JSON, nullable=True)
    safety_endpoints: Mapped[list] = mapped_column(JSON, nullable=True)
    zero_sdv_domains: Mapped[list] = mapped_column(JSON, nullable=True)
    trial_random_seed: Mapped[int] = mapped_column(Integer, nullable=True)


class MedDRATerm(AuditedModel):
    """Represents a term in the MedDRA dictionary.

    Models five levels: LLT, PT, HLT, HLGT, and SOC.
    """

    __tablename__ = "meddra_terms"
    __table_args__ = (
        UniqueConstraint(
            "dictionary_version",
            "code",
            "level",
            name="uq_meddra_term_version_code_level",
        ),
        Index("idx_meddra_term_lookup", "dictionary_version", "code", "level"),
        Index("idx_meddra_term_search", "dictionary_version", "term_name"),
    )

    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    code: Mapped[str] = mapped_column(String(50), nullable=False)
    term_name: Mapped[str] = mapped_column(String(255), nullable=False)
    level: Mapped[str] = mapped_column(
        String(10), nullable=False
    )  # "LLT", "PT", "HLT", "HLGT", "SOC"


class MedDRAHierarchy(AuditedModel):
    """Represents the MedDRA mdhier relationship/hierarchy data.

    Includes primary SOC indication.
    """

    __tablename__ = "meddra_hierarchies"
    __table_args__ = (
        UniqueConstraint(
            "dictionary_version",
            "pt_code",
            "hlt_code",
            "hlgt_code",
            "soc_code",
            "llt_code",
            name="uq_meddra_hier_version_codes",
        ),
        Index("idx_meddra_hier_lookup", "dictionary_version", "pt_code"),
        Index("idx_meddra_hier_llt", "dictionary_version", "llt_code"),
    )

    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    llt_code: Mapped[str] = mapped_column(
        String(50), default="NONE", server_default="NONE", nullable=False
    )
    pt_code: Mapped[str] = mapped_column(String(50), nullable=False)
    hlt_code: Mapped[str] = mapped_column(String(50), nullable=False)
    hlgt_code: Mapped[str] = mapped_column(String(50), nullable=False)
    soc_code: Mapped[str] = mapped_column(String(50), nullable=False)
    primary_soc_flag: Mapped[str] = mapped_column(String(1), nullable=True)  # "Y", "N"


class WHODrugRecord(AuditedModel):
    """Represents a drug record in WHODrug."""

    __tablename__ = "whodrug_records"
    __table_args__ = (
        UniqueConstraint(
            "dictionary_version", "drug_code", name="uq_whodrug_record_version_code"
        ),
        Index("idx_whodrug_record_lookup", "dictionary_version", "drug_code"),
        Index("idx_whodrug_record_search", "dictionary_version", "preferred_name"),
    )

    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    drug_code: Mapped[str] = mapped_column(String(50), nullable=False)
    preferred_name: Mapped[str] = mapped_column(String(255), nullable=False)
    drug_name: Mapped[str] = mapped_column(String(255), nullable=True)


class WHODrugIngredient(AuditedModel):
    """Represents an active substance or ingredient in WHODrug."""

    __tablename__ = "whodrug_ingredients"
    __table_args__ = (
        UniqueConstraint(
            "dictionary_version",
            "ingredient_code",
            name="uq_whodrug_ingredient_version_code",
        ),
        Index("idx_whodrug_ingredient_lookup", "dictionary_version", "ingredient_code"),
        Index("idx_whodrug_ingredient_search", "dictionary_version", "ingredient_name"),
    )

    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    ingredient_code: Mapped[str] = mapped_column(String(50), nullable=False)
    ingredient_name: Mapped[str] = mapped_column(String(255), nullable=False)


class WHODrugATC(AuditedModel):
    """Represents an Anatomical Therapeutic Chemical (ATC) hierarchy record in WHODrug."""

    __tablename__ = "whodrug_atc"
    __table_args__ = (
        UniqueConstraint(
            "dictionary_version", "atc_code", name="uq_whodrug_atc_version_code"
        ),
        Index("idx_whodrug_atc_lookup", "dictionary_version", "atc_code"),
    )

    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    atc_code: Mapped[str] = mapped_column(String(50), nullable=False)
    description: Mapped[str] = mapped_column(String(255), nullable=False)


class WHODrugDrugATC(AuditedModel):
    """Represents the relationship between a WHODrug drug record and its ATC classification."""

    __tablename__ = "whodrug_drug_atc"
    __table_args__ = (
        UniqueConstraint(
            "dictionary_version",
            "drug_code",
            "atc_code",
            name="uq_whodrug_drug_atc_version",
        ),
        Index("idx_whodrug_drug_atc_lookup", "dictionary_version", "drug_code"),
    )

    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    drug_code: Mapped[str] = mapped_column(String(50), nullable=False)
    atc_code: Mapped[str] = mapped_column(String(50), nullable=False)


class WHODrugDrugIngredient(AuditedModel):
    """Represents the association of a WHODrug drug record with an ingredient/active substance."""

    __tablename__ = "whodrug_drug_ingredients"
    __table_args__ = (
        UniqueConstraint(
            "dictionary_version",
            "drug_code",
            "ingredient_code",
            name="uq_whodrug_drug_ingredient_version",
        ),
        Index("idx_whodrug_drug_ing_lookup", "dictionary_version", "drug_code"),
    )

    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    drug_code: Mapped[str] = mapped_column(String(50), nullable=False)
    ingredient_code: Mapped[str] = mapped_column(String(50), nullable=False)


class DictionaryImportJob(AuditedModel):
    """Tracks dictionary import execution, status, and summary metrics."""

    __tablename__ = "dictionary_import_jobs"

    dictionary_type: Mapped[DictionaryType] = mapped_column(
        Enum(DictionaryType, name="dictionary_type_enum"), nullable=False
    )
    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    status: Mapped[ImportState] = mapped_column(
        Enum(ImportState, name="import_state_enum"),
        default=ImportState.PENDING,
        nullable=False,
    )
    started_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    completed_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    progress_percentage: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    records_imported: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    errors_encountered: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    error_details: Mapped[str] = mapped_column(String(1000), nullable=True)


class ClinicalCodingAssignment(AuditedModel):
    """Represents a coded-term assignment to a clinical verbatim or observation."""

    __tablename__ = "clinical_coding_assignments"
    __table_args__ = (
        Index("idx_coding_assign_lookup", "dictionary_type", "dictionary_version"),
        Index("idx_coding_assign_verbatim", "verbatim_text"),
        Index("idx_coding_assign_obs", "observation_id"),
        CheckConstraint(
            "(status != 'CODED') OR (coded_code IS NOT NULL AND coded_term IS NOT NULL)",
            name="chk_coding_assignment_coded_fields",
        ),
    )

    verbatim_text: Mapped[str] = mapped_column(String(1000), nullable=False)
    source_field: Mapped[str] = mapped_column(
        String(255), nullable=True
    )  # e.g., "AE.AETERM"
    observation_id: Mapped[str] = mapped_column(String(255), nullable=True)
    dictionary_type: Mapped[DictionaryType] = mapped_column(
        Enum(DictionaryType, name="dictionary_type_assignment_enum"), nullable=False
    )
    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    coded_code: Mapped[str] = mapped_column(String(50), nullable=True)
    coded_term: Mapped[str] = mapped_column(String(255), nullable=True)
    status: Mapped[CodingState] = mapped_column(
        Enum(CodingState, name="coding_state_enum"),
        default=CodingState.UNCODED,
        nullable=False,
    )
    recoding_status: Mapped[RecodingState] = mapped_column(
        Enum(RecodingState, name="recoding_state_enum"),
        default=RecodingState.NONE,
        nullable=False,
    )
    assigned_by: Mapped[str] = mapped_column(String(255), nullable=True)
    assigned_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())


class ClinicalCodingLedger(AuditedModel):
    """Maintains historical record of coding/recoding decisions and audit events."""

    __tablename__ = "clinical_coding_ledger"
    __table_args__ = (
        Index("idx_coding_ledger_assign", "assignment_id"),
        Index("idx_coding_ledger_obs", "observation_id"),
    )

    assignment_id: Mapped[str] = mapped_column(String(36), nullable=False)
    verbatim_text: Mapped[str] = mapped_column(String(1000), nullable=False)
    observation_id: Mapped[str] = mapped_column(String(255), nullable=True)
    dictionary_type: Mapped[DictionaryType] = mapped_column(
        Enum(DictionaryType, name="dictionary_type_ledger_enum"), nullable=False
    )
    old_dictionary_version: Mapped[str] = mapped_column(String(50), nullable=True)
    old_coded_code: Mapped[str] = mapped_column(String(50), nullable=True)
    old_coded_term: Mapped[str] = mapped_column(String(255), nullable=True)
    new_dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    new_coded_code: Mapped[str] = mapped_column(String(50), nullable=False)
    new_coded_term: Mapped[str] = mapped_column(String(255), nullable=False)
    recoding_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    decision_by: Mapped[str] = mapped_column(String(255), nullable=True)
    decision_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())


class LabReferenceRange(AuditedModel):
    """Represents lab reference range settings for clinical trials, enabling validation of lab values.

    Attributes:
        study_id (str): The unique identifier of the study.
        test_code (str): The laboratory test code (e.g. 'HEMOGLOBIN').
        test_name (str): The name/description of the test parameter.
        source (str): Source type, either 'CENTRAL' or 'LOCAL'.
        site_id (str): Optional site identifier for local range applicability.
        unit (str): Original unit of measurement.
        normalized_unit (str): Normalized unit of measurement.
        sex_applicability (str): Sex applicability (e.g. 'M', 'F', 'ALL').
        age_low (float): Nullable lower bound for age applicability.
        age_high (float): Nullable upper bound for age applicability.
        low_bound (float): Nullable lower limit of normal range.
        high_bound (float): Nullable upper limit of normal range.
        critical_low (float): Nullable lower limit for critical alert range.
        critical_high (float): Nullable upper limit for critical alert range.
    """

    __tablename__ = "lab_reference_ranges"
    __table_args__ = (
        Index("idx_lab_range_lookup", "study_id", "test_code", "source", "site_id"),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    test_code: Mapped[str] = mapped_column(String(100), index=True, nullable=False)
    test_name: Mapped[str] = mapped_column(String(255), nullable=False)
    source: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # "CENTRAL" or "LOCAL"
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    unit: Mapped[str] = mapped_column(String(50), nullable=True)
    normalized_unit: Mapped[str] = mapped_column(String(50), nullable=True)
    sex_applicability: Mapped[str] = mapped_column(String(50), nullable=True)
    age_low: Mapped[float] = mapped_column(Float, nullable=True)
    age_high: Mapped[float] = mapped_column(Float, nullable=True)
    low_bound: Mapped[float] = mapped_column(Float, nullable=True)
    high_bound: Mapped[float] = mapped_column(Float, nullable=True)
    critical_low: Mapped[float] = mapped_column(Float, nullable=True)
    critical_high: Mapped[float] = mapped_column(Float, nullable=True)


class FormSubmission(AuditedModel):
    """Represents a CRF form submission as the auditable unit of PI sign-off.

    Inherits from AuditedModel to maintain an immutable audit log and participate in
    existing versioning, audit-ledger, Merkle-sealing, and locking conventions.

    Attributes:
        study_id (str): Unique clinical trial study identifier.
        site_id (str): Site identifier for local trial locking applicability.
        subject_id (str): Clinical subject identifier.
        visit_id (str): Clinical visit identifier (nullable).
        form_id (str): CRF form identifier or grouping key.
        status (str): Controlled status: "DRAFT", "COMPLETED", "APPROVED".
        signature_manifest (dict): Nullable JSON dictionary of electronic signature details.
    """

    __tablename__ = "form_submissions"
    __table_args__ = (
        Index(
            "idx_form_submissions_coords",
            "study_id",
            "subject_id",
            "visit_id",
            "form_id",
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_id: Mapped[str] = mapped_column(String(255), nullable=True)
    form_id: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="DRAFT", nullable=False)
    signature_manifest: Mapped[dict] = mapped_column(JSON, nullable=True)


class RandomizationConfig(AuditedModel):
    """Represents a trial's randomization configuration."""

    __tablename__ = "randomization_configs"

    study_id: Mapped[str] = mapped_column(String(255), unique=True, nullable=False)
    algorithm_type: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # e.g. "PERMUTED_BLOCK", "MINIMIZATION"
    arms_ratios: Mapped[dict] = mapped_column(
        JSON, nullable=False
    )  # dictionary mapping arms to ratios
    stratification_factors: Mapped[dict] = mapped_column(
        JSON, nullable=True
    )  # definition of stratification factors
    encrypted_block_config: Mapped[str] = mapped_column(
        String, nullable=True
    )  # sensitive block config encrypted-at-rest
    seed: Mapped[int] = mapped_column(Integer, nullable=True)


class StratumState(AuditedModel):
    """Tracks state independently per stratum."""

    __tablename__ = "stratum_states"
    __table_args__ = (
        UniqueConstraint(
            "study_id", "stratum_key", name="uq_stratum_state_study_stratum"
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    stratum_key: Mapped[str] = mapped_column(
        String(255), nullable=False
    )  # e.g., "M_<18" or "DEFAULT"
    block_index: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    encrypted_sequence: Mapped[str] = mapped_column(
        String, nullable=True
    )  # pre-generated treatments list encrypted-at-rest


class SubjectRandomization(AuditedModel):
    """Represents a randomized subject's treatment assignment."""

    __tablename__ = "subject_randomizations"
    __table_args__ = (Index("idx_subject_randomization_study", "study_id"),)

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=True)
    subject_id: Mapped[str] = mapped_column(
        String(255), unique=True, nullable=False
    )  # Enforces exactly one assignment per subject
    stratum_key: Mapped[str] = mapped_column(String(255), nullable=True)
    encrypted_allocation: Mapped[str] = mapped_column(
        String, nullable=False
    )  # assigned treatment arm encrypted-at-rest
    kit_reference: Mapped[str] = mapped_column(
        String(255), nullable=True
    )  # trial kit/IP reference
    randomized_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


class PendingPredecessorCheck(AuditedModel):
    """Tracks edit check executions deferred because of missing/incomplete predecessor visit data."""

    __tablename__ = "pending_predecessor_checks"
    __table_args__ = (Index("idx_pending_pred_subject_rule", "subject_id", "rule_id"),)

    subject_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    current_visit_id: Mapped[str] = mapped_column(String(255), nullable=True)
    current_visit_name: Mapped[str] = mapped_column(String(255), nullable=False)
    predecessor_visit_name: Mapped[str] = mapped_column(String(255), nullable=False)
    rule_id: Mapped[str] = mapped_column(String(255), nullable=False)
    observation_id: Mapped[str] = mapped_column(String(255), nullable=False)
    test_code: Mapped[str] = mapped_column(String(100), nullable=False)
