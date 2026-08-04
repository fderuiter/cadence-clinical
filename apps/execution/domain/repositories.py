import copy
from typing import Protocol

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import (
    AuditLog,
    ClinicalSubject,
    ConsentFormRecord,
    ConsentSignature,
)
from apps.execution.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ConsentFormRecordDomain,
    ConsentSignatureDomain,
)

# ---------------------------------------------------------------------------
# Repository Interfaces (Protocols)
# ---------------------------------------------------------------------------


class SubjectRepository(Protocol):
    async def get_by_id(self, id: str) -> ClinicalSubjectDomain | None: ...

    async def save(self, subject: ClinicalSubjectDomain) -> None: ...


class ConsentRepository(Protocol):
    async def get_signature_by_id(self, id: str) -> ConsentSignatureDomain | None: ...

    async def save_signature(self, signature: ConsentSignatureDomain) -> None: ...

    async def get_form_record_by_id(
        self, id: str
    ) -> ConsentFormRecordDomain | None: ...

    async def save_form_record(self, record: ConsentFormRecordDomain) -> None: ...


class AuditRepository(Protocol):
    async def get_by_id(self, id: str) -> AuditLogDomain | None: ...

    async def save(self, log: AuditLogDomain) -> None: ...


# ---------------------------------------------------------------------------
# SQLAlchemy Repository Adapters
# ---------------------------------------------------------------------------


class SQLAlchemySubjectRepository:
    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_by_id(self, id: str) -> ClinicalSubjectDomain | None:
        stmt = select(ClinicalSubject).where(ClinicalSubject.id == id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            return None
        return ClinicalSubjectDomain(
            id=db_obj.id,
            subject_id=db_obj.subject_id,
            study_id=db_obj.study_id,
            site_id=db_obj.site_id,
            encrypted_demographics=db_obj.encrypted_demographics,
            status=db_obj.status,
            strat_factors=copy.deepcopy(db_obj.strat_factors),
            is_unblinded=db_obj.is_unblinded,
            unblinded_at=db_obj.unblinded_at,
            unblinded_by=db_obj.unblinded_by,
            unblinded_reason=db_obj.unblinded_reason,
            unblinded_signature=db_obj.unblinded_signature,
            withdrawn_at=db_obj.withdrawn_at,
            withdrawal_reason=db_obj.withdrawal_reason,
            randomization_id=db_obj.randomization_id,
            kit_reference=db_obj.kit_reference,
            enrollment_index=db_obj.enrollment_index,
            treatment_group=db_obj.treatment_group,
            randomization_seed=db_obj.randomization_seed,
            investigational_product_id=db_obj.investigational_product_id,
            version=db_obj.version,
            is_deleted=db_obj.is_deleted,
        )

    async def save(self, subject: ClinicalSubjectDomain) -> None:
        stmt = select(ClinicalSubject).where(ClinicalSubject.id == subject.id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            db_obj = ClinicalSubject(id=subject.id)
            self.session.add(db_obj)

        db_obj.subject_id = subject.subject_id
        db_obj.study_id = subject.study_id
        db_obj.site_id = subject.site_id
        db_obj.encrypted_demographics = subject.encrypted_demographics
        db_obj.status = subject.status
        db_obj.strat_factors = copy.deepcopy(subject.strat_factors)
        db_obj.is_unblinded = subject.is_unblinded
        db_obj.unblinded_at = subject.unblinded_at
        db_obj.unblinded_by = subject.unblinded_by
        db_obj.unblinded_reason = subject.unblinded_reason
        db_obj.unblinded_signature = subject.unblinded_signature
        db_obj.withdrawn_at = subject.withdrawn_at
        db_obj.withdrawal_reason = subject.withdrawal_reason
        db_obj.randomization_id = subject.randomization_id
        db_obj.kit_reference = subject.kit_reference
        db_obj.enrollment_index = subject.enrollment_index
        db_obj.treatment_group = subject.treatment_group
        db_obj.randomization_seed = subject.randomization_seed
        db_obj.investigational_product_id = subject.investigational_product_id
        db_obj.version = subject.version
        db_obj.is_deleted = subject.is_deleted
        await self.session.flush()


class SQLAlchemyConsentRepository:
    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_signature_by_id(self, id: str) -> ConsentSignatureDomain | None:
        stmt = select(ConsentSignature).where(ConsentSignature.id == id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            return None
        return ConsentSignatureDomain(
            id=db_obj.id,
            subject_id=db_obj.subject_id,
            site_id=db_obj.site_id,
            icf_version_id=db_obj.icf_version_id,
            printed_name=db_obj.printed_name,
            signature_svg_data=db_obj.signature_svg_data,
            signature_svg=db_obj.signature_svg,
            otp_auth_code=db_obj.otp_auth_code,
            meaning=db_obj.meaning,
            cryptographic_token=db_obj.cryptographic_token,
            verification_hash=db_obj.verification_hash,
            signed_at=db_obj.signed_at,
            timestamp=db_obj.timestamp,
            status=db_obj.status,
            version=db_obj.version,
            is_deleted=db_obj.is_deleted,
            created_at=db_obj.created_at,
            created_by=db_obj.created_by,
            reason_for_change=db_obj.reason_for_change,
        )

    async def save_signature(self, signature: ConsentSignatureDomain) -> None:
        stmt = select(ConsentSignature).where(ConsentSignature.id == signature.id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            db_obj = ConsentSignature(id=signature.id)
            self.session.add(db_obj)

        db_obj.subject_id = signature.subject_id
        db_obj.site_id = signature.site_id
        db_obj.icf_version_id = signature.icf_version_id
        db_obj.printed_name = signature.printed_name
        db_obj.signature_svg_data = signature.signature_svg_data
        db_obj.signature_svg = signature.signature_svg
        db_obj.otp_auth_code = signature.otp_auth_code
        db_obj.meaning = signature.meaning
        db_obj.cryptographic_token = signature.cryptographic_token
        db_obj.verification_hash = signature.verification_hash
        db_obj.signed_at = signature.signed_at
        db_obj.status = signature.status
        db_obj.version = signature.version
        db_obj.is_deleted = signature.is_deleted
        db_obj.created_by = signature.created_by
        db_obj.reason_for_change = signature.reason_for_change
        await self.session.flush()

    async def get_form_record_by_id(self, id: str) -> ConsentFormRecordDomain | None:
        stmt = select(ConsentFormRecord).where(ConsentFormRecord.id == id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            return None
        return ConsentFormRecordDomain(
            id=db_obj.id,
            subject_id=db_obj.subject_id,
            site_id=db_obj.site_id,
            icf_version_id=db_obj.icf_version_id,
            printed_name=db_obj.printed_name,
            relationship_to_subject=db_obj.relationship_to_subject,
            signature_svg=db_obj.signature_svg,
            otp_auth_code=db_obj.otp_auth_code,
            status=db_obj.status,
            signed_at=db_obj.signed_at,
            is_verified=db_obj.is_verified,
            version=db_obj.version,
            is_deleted=db_obj.is_deleted,
        )

    async def save_form_record(self, record: ConsentFormRecordDomain) -> None:
        stmt = select(ConsentFormRecord).where(ConsentFormRecord.id == record.id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            db_obj = ConsentFormRecord(id=record.id)
            self.session.add(db_obj)

        db_obj.subject_id = record.subject_id
        db_obj.site_id = record.site_id
        db_obj.icf_version_id = record.icf_version_id
        db_obj.printed_name = record.printed_name
        db_obj.relationship_to_subject = record.relationship_to_subject
        db_obj.signature_svg = record.signature_svg
        db_obj.otp_auth_code = record.otp_auth_code
        db_obj.status = record.status
        db_obj.signed_at = record.signed_at
        db_obj.is_verified = record.is_verified
        db_obj.version = record.version
        db_obj.is_deleted = record.is_deleted
        await self.session.flush()


class SQLAlchemyAuditRepository:
    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_by_id(self, id: str) -> AuditLogDomain | None:
        stmt = select(AuditLog).where(AuditLog.id == id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            return None
        return AuditLogDomain(
            id=db_obj.id,
            table_name=db_obj.table_name,
            record_id=db_obj.record_id,
            action=db_obj.action,
            user_id=db_obj.user_id,
            ip_address=db_obj.ip_address,
            timestamp=db_obj.timestamp,
            old_values=copy.deepcopy(db_obj.old_values),
            new_values=copy.deepcopy(db_obj.new_values),
            version_index=db_obj.version_index,
            change_reason=db_obj.change_reason,
            cryptographic_seal=db_obj.cryptographic_seal,
        )

    async def save(self, log: AuditLogDomain) -> None:
        stmt = select(AuditLog).where(AuditLog.id == log.id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            db_obj = AuditLog(id=log.id)
            self.session.add(db_obj)

        db_obj.table_name = log.table_name
        db_obj.record_id = log.record_id
        db_obj.action = log.action
        db_obj.user_id = log.user_id
        db_obj.ip_address = log.ip_address
        db_obj.timestamp = log.timestamp
        db_obj.old_values = copy.deepcopy(log.old_values)
        db_obj.new_values = copy.deepcopy(log.new_values)
        db_obj.version_index = log.version_index
        db_obj.change_reason = log.change_reason
        db_obj.cryptographic_seal = log.cryptographic_seal
        await self.session.flush()


# ---------------------------------------------------------------------------
# In-Memory Repository Adapters (Mocks)
# ---------------------------------------------------------------------------


class InMemorySubjectRepository:
    def __init__(self):
        self.store = {}

    async def get_by_id(self, id: str) -> ClinicalSubjectDomain | None:
        subject = self.store.get(id)
        if not subject:
            return None
        # Return a copy to mimic DB fetch
        return copy.deepcopy(subject)

    async def save(self, subject: ClinicalSubjectDomain) -> None:
        self.store[subject.id] = copy.deepcopy(subject)


class InMemoryConsentRepository:
    def __init__(self):
        self.signatures = {}
        self.form_records = {}

    async def get_signature_by_id(self, id: str) -> ConsentSignatureDomain | None:
        sig = self.signatures.get(id)
        if not sig:
            return None
        return copy.deepcopy(sig)

    async def save_signature(self, signature: ConsentSignatureDomain) -> None:
        self.signatures[signature.id] = copy.deepcopy(signature)

    async def delete_signature(self, signature: ConsentSignatureDomain) -> None:
        # Immutability throws
        raise ValueError("Cannot delete consent records")

    async def get_form_record_by_id(self, id: str) -> ConsentFormRecordDomain | None:
        record = self.form_records.get(id)
        if not record:
            return None
        return copy.deepcopy(record)

    async def save_form_record(self, record: ConsentFormRecordDomain) -> None:
        self.form_records[record.id] = copy.deepcopy(record)

    async def delete_form_record(self, record: ConsentFormRecordDomain) -> None:
        # Immutability throws
        raise ValueError("Cannot delete consent records")


class InMemoryAuditRepository:
    def __init__(self):
        self.logs = {}

    async def get_by_id(self, id: str) -> AuditLogDomain | None:
        log = self.logs.get(id)
        if not log:
            return None
        return copy.deepcopy(log)

    async def save(self, log: AuditLogDomain) -> None:
        self.logs[log.id] = copy.deepcopy(log)

    async def delete(self, log: AuditLogDomain) -> None:
        # Immutability throws
        raise ValueError(
            "Deletion of AuditLog is strictly forbidden to comply with 21 CFR Part 11."
        )
