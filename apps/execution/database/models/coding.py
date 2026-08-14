import enum
from datetime import datetime

from sqlalchemy import (
    JSON,
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
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class DictionaryType(enum.StrEnum):
    MEDDRA = "MEDDRA"
    WHODRUG = "WHODRUG"
    LOINC = "LOINC"
    SNOMED = "SNOMED"


class ImportState(enum.StrEnum):
    PENDING = "PENDING"
    PROCESSING = "PROCESSING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"


class CodingState(enum.StrEnum):
    UNCODED = "UNCODED"
    SUGGESTED = "SUGGESTED"
    CODED = "CODED"
    AUTO_CODED = "AUTO_CODED"
    QUERY_PENDING = "QUERY_PENDING"
    RECODING_REQUIRED = "RECODING_REQUIRED"


class RecodingState(enum.StrEnum):
    NONE = "NONE"
    PENDING = "PENDING"
    COMPLETED = "COMPLETED"
    CANCELLED = "CANCELLED"


class MedDRATerm(AuditedModel):
    """Represents a term in the MedDRA dictionary.

    Models five levels: LLT, PT, HLT, HLGT, and SOC.
    Satisfies Epic #109 / Issue #1122 / Phase 16: Dictionary Ingestion & Persistence.
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
    """Represents a drug record in WHODrug.

    Satisfies Epic #109 / Issue #1122 / Phase 16: Dictionary Ingestion & Persistence.
    """

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
    """Tracks dictionary import execution, status, and summary metrics.

    Satisfies Epic #109 / Issue #1122 / Phase 16: Dictionary Ingestion & Persistence.
    """

    __tablename__ = "dictionary_import_jobs"

    dictionary_type: Mapped[DictionaryType] = mapped_column(
        Enum(DictionaryType, name="dictionary_type_enum", native_enum=False),
        nullable=False,
    )
    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    status: Mapped[ImportState] = mapped_column(
        Enum(ImportState, name="import_state_enum", native_enum=False),
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
            "(status NOT IN ('CODED', 'AUTO_CODED')) OR (coded_code IS NOT NULL AND coded_term IS NOT NULL)",
            name="chk_coding_assignment_coded_fields",
        ),
    )

    verbatim_text: Mapped[str] = mapped_column(String(1000), nullable=False)
    source_field: Mapped[str] = mapped_column(
        String(255), nullable=True
    )  # e.g., "AE.AETERM"
    observation_id: Mapped[str] = mapped_column(String(255), nullable=True)
    dictionary_type: Mapped[DictionaryType] = mapped_column(
        Enum(DictionaryType, name="dictionary_type_assignment_enum", native_enum=False),
        nullable=False,
    )
    dictionary_version: Mapped[str] = mapped_column(String(50), nullable=False)
    coded_code: Mapped[str] = mapped_column(String(50), nullable=True)
    coded_term: Mapped[str] = mapped_column(String(255), nullable=True)
    status: Mapped[CodingState] = mapped_column(
        Enum(CodingState, name="coding_state_enum", native_enum=False),
        default=CodingState.UNCODED,
        nullable=False,
    )
    recoding_status: Mapped[RecodingState] = mapped_column(
        Enum(RecodingState, name="recoding_state_enum", native_enum=False),
        default=RecodingState.NONE,
        nullable=False,
    )
    assigned_by: Mapped[str] = mapped_column(String(255), nullable=True)
    assigned_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())

    # Expanded fields to persist coding/matching results comprehensively
    score: Mapped[float] = mapped_column(Float, nullable=True)
    hierarchy: Mapped[dict] = mapped_column(JSON, nullable=True)
    suggestions: Mapped[dict] = mapped_column(JSON, nullable=True)
    domain: Mapped[str] = mapped_column(String(50), nullable=True)


class ClinicalCodingLedger(AuditedModel):
    """Maintains historical record of coding/recoding decisions and audit events.

    Satisfies Epic #109 / Issue #1122 / Phase 16: Dictionary Ingestion & Persistence.
    """

    __tablename__ = "clinical_coding_ledger"
    __table_args__ = (
        Index("idx_coding_ledger_assign", "assignment_id"),
        Index("idx_coding_ledger_obs", "observation_id"),
    )

    assignment_id: Mapped[str] = mapped_column(String(36), nullable=False)
    verbatim_text: Mapped[str] = mapped_column(String(1000), nullable=False)
    observation_id: Mapped[str] = mapped_column(String(255), nullable=True)
    dictionary_type: Mapped[DictionaryType] = mapped_column(
        Enum(DictionaryType, name="dictionary_type_ledger_enum", native_enum=False),
        nullable=False,
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
    old_hierarchy: Mapped[dict] = mapped_column(JSON, nullable=True)
    new_hierarchy: Mapped[dict] = mapped_column(JSON, nullable=True)
    recoding_status: Mapped[RecodingState] = mapped_column(
        Enum(RecodingState, name="recoding_state_ledger_enum", native_enum=False),
        nullable=True,
    )
