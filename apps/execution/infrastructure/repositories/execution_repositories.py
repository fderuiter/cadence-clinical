import copy

from fastapi import Depends
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database import db_manager
from apps.execution.database.models import (
    AuditLog,
    ClinicalSubject,
    ConsentFormRecord,
    ConsentSignature,
    DOAAuditLog,
    DOADelegationRecord,
    SiteStaffMember,
)
from apps.execution.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ConsentFormRecordDomain,
    ConsentSignatureDomain,
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
)
from apps.execution.domain.ports import (
    IAuditRepository,
    IConsentRepository,
    IExecutionDOARepository,
    ISubjectRepository,
)
from packages.database import map_database_exceptions


class SQLAlchemyExecutionDOARepository(IExecutionDOARepository):
    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> ExecutionDelegationEntity | None:
        return await self.get_delegation_by_id(entity_id)

    @map_database_exceptions
    async def save(
        self, entity: ExecutionDelegationEntity
    ) -> ExecutionDelegationEntity:
        return await self.save_delegation(entity)

    @map_database_exceptions
    async def get_staff(
        self, site_id: str, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        stmt = select(SiteStaffMember).where(
            SiteStaffMember.site_id == site_id,
            SiteStaffMember.staff_user_id == staff_user_id,
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        if not model:
            return None
        return ExecutionStaffEntity(
            id=model.id,
            site_id=model.site_id,
            staff_user_id=model.staff_user_id,
            name=model.name,
            email=model.email,
            has_gcp_training=model.has_gcp_training,
        )

    @map_database_exceptions
    async def get_staff_by_user_id(
        self, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        stmt = select(SiteStaffMember).where(
            SiteStaffMember.staff_user_id == staff_user_id
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        if not model:
            return None
        return ExecutionStaffEntity(
            id=model.id,
            site_id=model.site_id,
            staff_user_id=model.staff_user_id,
            name=model.name,
            email=model.email,
            has_gcp_training=model.has_gcp_training,
        )

    @map_database_exceptions
    async def save_staff(self, staff: ExecutionStaffEntity) -> ExecutionStaffEntity:
        if staff.id:
            stmt = select(SiteStaffMember).where(SiteStaffMember.id == staff.id)
            res = await self.session.execute(stmt)
            model = res.scalar_one_or_none()
            if model:
                model.site_id = staff.site_id
                model.name = staff.name
                model.email = staff.email
                model.has_gcp_training = staff.has_gcp_training
                self.session.add(model)
                await self.session.commit()
                return staff

        model = SiteStaffMember(
            site_id=staff.site_id,
            staff_user_id=staff.staff_user_id,
            name=staff.name,
            email=staff.email,
            has_gcp_training=staff.has_gcp_training,
        )
        self.session.add(model)
        await self.session.commit()
        await self.session.refresh(model)
        staff.id = model.id
        return staff

    @map_database_exceptions
    async def get_delegation_by_id(
        self, delegation_id: str
    ) -> ExecutionDelegationEntity | None:
        stmt = select(DOADelegationRecord).where(
            DOADelegationRecord.id == delegation_id,
            DOADelegationRecord.is_active.is_(True),
        )
        res = await self.session.execute(stmt)
        model = res.scalar_one_or_none()
        if not model:
            return None
        return ExecutionDelegationEntity(
            id=model.id,
            site_id=model.site_id,
            staff_user_id=model.staff_user_id,
            task_code=model.task_code,
            pi_user_id=model.pi_user_id,
            status=model.status,
            pi_signature_hash=model.pi_signature_hash,
            pi_approved_at=model.pi_approved_at,
            end_date=model.end_date,
            reason_for_change=model.reason_for_change,
            is_active=model.is_active,
        )

    @map_database_exceptions
    async def save_delegation(
        self, delegation: ExecutionDelegationEntity
    ) -> ExecutionDelegationEntity:
        if delegation.id:
            stmt = select(DOADelegationRecord).where(
                DOADelegationRecord.id == delegation.id
            )
            res = await self.session.execute(stmt)
            model = res.scalar_one_or_none()
            if model:
                model.status = delegation.status
                model.pi_approved_at = delegation.pi_approved_at
                model.pi_signature_hash = delegation.pi_signature_hash
                model.reason_for_change = delegation.reason_for_change
                model.end_date = delegation.end_date
                model.is_active = delegation.is_active
                self.session.add(model)
                await self.session.commit()
                return delegation

        model = DOADelegationRecord(
            site_id=delegation.site_id,
            staff_user_id=delegation.staff_user_id,
            task_code=delegation.task_code,
            pi_user_id=delegation.pi_user_id,
            status=delegation.status,
            reason_for_change=delegation.reason_for_change,
            is_active=delegation.is_active,
        )
        self.session.add(model)
        await self.session.commit()
        await self.session.refresh(model)
        delegation.id = model.id
        return delegation

    @map_database_exceptions
    async def save_audit_log(self, audit: ExecutionAuditLogEntity) -> None:
        model = DOAAuditLog(
            user_id=audit.user_id,
            action=audit.action,
            details=audit.details,
        )
        self.session.add(model)
        await self.session.commit()

    @map_database_exceptions
    async def get_all_audit_logs(self) -> list[ExecutionAuditLogEntity]:
        stmt = select(DOAAuditLog).order_by(DOAAuditLog.timestamp.desc())
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [
            ExecutionAuditLogEntity(
                id=m.id,
                user_id=m.user_id,
                action=m.action,
                details=m.details,
                timestamp=m.timestamp,
            )
            for m in models
        ]

    @map_database_exceptions
    async def get_all_delegations(self) -> list[ExecutionDelegationEntity]:
        stmt = select(DOADelegationRecord)
        res = await self.session.execute(stmt)
        models = res.scalars().all()
        return [
            ExecutionDelegationEntity(
                id=m.id,
                site_id=m.site_id,
                staff_user_id=m.staff_user_id,
                task_code=m.task_code,
                pi_user_id=m.pi_user_id,
                status=m.status,
                pi_signature_hash=m.pi_signature_hash,
                pi_approved_at=m.pi_approved_at,
                end_date=m.end_date,
                reason_for_change=m.reason_for_change,
                is_active=m.is_active,
            )
            for m in models
        ]


SQLAlchemExecutionDOARepository = SQLAlchemyExecutionDOARepository


async def get_execution_db_session():
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        yield session


async def get_execution_doa_repository(
    session: AsyncSession = Depends(get_execution_db_session),
) -> SQLAlchemyExecutionDOARepository:
    return SQLAlchemyExecutionDOARepository(session)


class SQLAlchemySubjectRepository(ISubjectRepository):
    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_by_id(self, entity_id: str) -> ClinicalSubjectDomain | None:
        stmt = select(ClinicalSubject).where(ClinicalSubject.id == entity_id)
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

    async def save(self, entity: ClinicalSubjectDomain) -> ClinicalSubjectDomain:
        stmt = select(ClinicalSubject).where(ClinicalSubject.id == entity.id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            db_obj = ClinicalSubject(id=entity.id)
            self.session.add(db_obj)

        db_obj.subject_id = entity.subject_id
        db_obj.study_id = entity.study_id
        db_obj.site_id = entity.site_id
        db_obj.encrypted_demographics = entity.encrypted_demographics
        db_obj.status = entity.status
        db_obj.strat_factors = copy.deepcopy(entity.strat_factors)
        db_obj.is_unblinded = entity.is_unblinded
        db_obj.unblinded_at = entity.unblinded_at
        db_obj.unblinded_by = entity.unblinded_by
        db_obj.unblinded_reason = entity.unblinded_reason
        db_obj.unblinded_signature = entity.unblinded_signature
        db_obj.withdrawn_at = entity.withdrawn_at
        db_obj.withdrawal_reason = entity.withdrawal_reason
        db_obj.randomization_id = entity.randomization_id
        db_obj.kit_reference = entity.kit_reference
        db_obj.enrollment_index = entity.enrollment_index
        db_obj.treatment_group = entity.treatment_group
        db_obj.randomization_seed = entity.randomization_seed
        db_obj.investigational_product_id = entity.investigational_product_id
        db_obj.version = entity.version
        db_obj.is_deleted = entity.is_deleted
        await self.session.flush()
        return entity


class SQLAlchemyConsentRepository(IConsentRepository):
    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_by_id(self, entity_id: str) -> ConsentSignatureDomain | None:
        return await self.get_signature_by_id(entity_id)

    async def save(self, entity: ConsentSignatureDomain) -> ConsentSignatureDomain:
        await self.save_signature(entity)
        return entity

    async def get_signature_by_id(
        self, record_id: str
    ) -> ConsentSignatureDomain | None:
        stmt = select(ConsentSignature).where(ConsentSignature.id == record_id)
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

    async def get_form_record_by_id(
        self, record_id: str
    ) -> ConsentFormRecordDomain | None:
        stmt = select(ConsentFormRecord).where(ConsentFormRecord.id == record_id)
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

    async def save_form_record(
        self, record: ConsentFormRecordDomain
    ) -> ConsentFormRecordDomain:
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
        return record


class SQLAlchemyAuditRepository(IAuditRepository):
    def __init__(self, session: AsyncSession):
        self.session = session

    async def get_by_id(self, entity_id: str) -> AuditLogDomain | None:
        stmt = select(AuditLog).where(AuditLog.id == entity_id)
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

    async def save(self, entity: AuditLogDomain) -> AuditLogDomain:
        stmt = select(AuditLog).where(AuditLog.id == entity.id)
        result = await self.session.execute(stmt)
        db_obj = result.scalars().first()
        if not db_obj:
            db_obj = AuditLog(id=entity.id)
            self.session.add(db_obj)

        db_obj.table_name = entity.table_name
        db_obj.record_id = entity.record_id
        db_obj.action = entity.action
        db_obj.user_id = entity.user_id
        db_obj.ip_address = entity.ip_address
        db_obj.timestamp = entity.timestamp
        db_obj.old_values = copy.deepcopy(entity.old_values)
        db_obj.new_values = copy.deepcopy(entity.new_values)
        db_obj.version_index = entity.version_index
        db_obj.change_reason = entity.change_reason
        db_obj.cryptographic_seal = entity.cryptographic_seal
        await self.session.flush()
        return entity


class InMemorySubjectRepository(ISubjectRepository):
    def __init__(self):
        self.store = {}

    async def get_by_id(self, entity_id: str) -> ClinicalSubjectDomain | None:
        subject = self.store.get(entity_id)
        if not subject:
            return None
        return copy.deepcopy(subject)

    async def save(self, entity: ClinicalSubjectDomain) -> ClinicalSubjectDomain:
        self.store[entity.id] = copy.deepcopy(entity)
        return entity


class InMemoryConsentRepository(IConsentRepository):
    def __init__(self):
        self.signatures = {}
        self.form_records = {}

    async def get_by_id(self, entity_id: str) -> ConsentSignatureDomain | None:
        return await self.get_signature_by_id(entity_id)

    async def save(self, entity: ConsentSignatureDomain) -> ConsentSignatureDomain:
        await self.save_signature(entity)
        return entity

    async def get_signature_by_id(
        self, record_id: str
    ) -> ConsentSignatureDomain | None:
        sig = self.signatures.get(record_id)
        if not sig:
            return None
        return copy.deepcopy(sig)

    async def save_signature(self, signature: ConsentSignatureDomain) -> None:
        self.signatures[signature.id] = copy.deepcopy(signature)

    async def delete_signature(self, signature: ConsentSignatureDomain) -> None:
        raise ValueError("Cannot delete consent records")

    async def get_form_record_by_id(
        self, record_id: str
    ) -> ConsentFormRecordDomain | None:
        record = self.form_records.get(record_id)
        if not record:
            return None
        return copy.deepcopy(record)

    async def save_form_record(
        self, record: ConsentFormRecordDomain
    ) -> ConsentFormRecordDomain:
        self.form_records[record.id] = copy.deepcopy(record)
        return record

    async def delete_form_record(self, record: ConsentFormRecordDomain) -> None:
        raise ValueError("Cannot delete consent records")


class InMemoryAuditRepository(IAuditRepository):
    def __init__(self):
        self.logs = {}

    async def get_by_id(self, entity_id: str) -> AuditLogDomain | None:
        log = self.logs.get(entity_id)
        if not log:
            return None
        return copy.deepcopy(log)

    async def save(self, entity: AuditLogDomain) -> AuditLogDomain:
        self.logs[entity.id] = copy.deepcopy(entity)
        return entity

    async def delete(self, log: AuditLogDomain) -> None:
        raise ValueError(
            "Deletion of AuditLog is strictly forbidden to comply with 21 CFR Part 11."
        )
