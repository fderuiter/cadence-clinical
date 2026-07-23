import uuid
from datetime import datetime

from sqlalchemy import JSON, Boolean, DateTime, Float, Index, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


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
    """

    __tablename__ = "clinical_subjects"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    encrypted_demographics: Mapped[str] = mapped_column(String, nullable=True)


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
    """

    __tablename__ = "clinical_observations"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
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
