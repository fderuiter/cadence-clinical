from typing import Protocol

from apps.execution.src.domain.models import (
    AuditLogDomain,
    ClinicalSubjectDomain,
    ConsentFormRecordDomain,
    ConsentSignatureDomain,
)


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
