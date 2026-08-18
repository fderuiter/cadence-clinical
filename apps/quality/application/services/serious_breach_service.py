from collections.abc import Sequence
from datetime import datetime, timedelta
from typing import Any

from apps.quality.domain.models import BreachStatus, RegulatoryAuthority
from apps.quality.domain.ports import QualityRepositoryPort
from packages.hexagonal import DomainError


class SeriousBreachServiceError(DomainError):
    def __init__(self, message: str, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


class SeriousBreachService:
    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    async def report_serious_breach(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise SeriousBreachServiceError(
                "Missing change justification reason", status_code=403
            )

        deadline = payload.discovery_date + timedelta(
            days=7
        )  # 7-day regulatory clock (168h)

        authorities = getattr(
            payload, "affected_authorities", [RegulatoryAuthority.MHRA.value]
        )

        breach = self.repo.create_serious_breach_entity(
            study_id=payload.study_id,
            site_id=getattr(payload, "site_id", None),
            title=payload.title,
            summary=payload.summary,
            event_date=payload.event_date,
            discovery_date=payload.discovery_date,
            confirmation_date=getattr(payload, "confirmation_date", None),
            reporting_deadline=deadline,
            affected_authorities=authorities,
            status=BreachStatus.UNDER_EVALUATION,
            regulatory_clock_hours_remaining=168.0,
            lead_qa_id=user_id,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_serious_breach(breach)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="SERIOUS_BREACH_REPORTED",
            details=f"Reported potential serious breach '{payload.title}' for study '{payload.study_id}'. 7-day clock started.",
            record_id=breach.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return breach

    async def confirm_serious_breach(
        self,
        breach_id: str,
        affected_authorities: list[str],
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise SeriousBreachServiceError(
                "Missing change justification reason", status_code=403
            )

        breach = await self.repo.get_serious_breach_by_id(breach_id)
        if not breach:
            raise SeriousBreachServiceError(
                f"Serious breach record '{breach_id}' not found.", status_code=404
            )

        now = datetime.now()
        breach.confirmation_date = now
        breach.reporting_deadline = now + timedelta(days=7)
        breach.affected_authorities = affected_authorities
        breach.status = BreachStatus.CONFIRMED_BREACH
        breach.version_index += 1
        breach.reason_for_change = change_reason
        await self.repo.save_serious_breach(breach)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="SERIOUS_BREACH_CONFIRMED",
            details=f"Confirmed serious breach {breach_id}. Authorities: {affected_authorities}.",
            record_id=breach_id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return breach

    async def get_regulatory_clock_status(self, breach_id: str) -> dict[str, Any]:
        breach = await self.repo.get_serious_breach_by_id(breach_id)
        if not breach:
            raise SeriousBreachServiceError(
                f"Serious breach '{breach_id}' not found.", status_code=404
            )

        now = datetime.now()
        if breach.reporting_deadline:
            diff = (breach.reporting_deadline - now).total_seconds() / 3600.0
            hours_remaining = round(diff, 1)
        else:
            hours_remaining = 168.0

        is_overdue = hours_remaining < 0
        is_approaching = 0 <= hours_remaining <= 48.0

        return {
            "breach_id": breach.id,
            "study_id": breach.study_id,
            "status": breach.status,
            "reporting_deadline": breach.reporting_deadline.isoformat()
            if breach.reporting_deadline
            else None,
            "regulatory_clock_hours_remaining": hours_remaining,
            "is_approaching_deadline": is_approaching,
            "is_overdue": is_overdue,
            "affected_authorities": breach.affected_authorities,
        }

    async def update_breach_status(
        self,
        breach_id: str,
        status: BreachStatus,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise SeriousBreachServiceError(
                "Missing change justification reason", status_code=403
            )

        breach = await self.repo.get_serious_breach_by_id(breach_id)
        if not breach:
            raise SeriousBreachServiceError(
                f"Serious breach '{breach_id}' not found.", status_code=404
            )

        breach.status = status
        breach.version_index += 1
        breach.reason_for_change = change_reason
        await self.repo.save_serious_breach(breach)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="SERIOUS_BREACH_STATUS_UPDATE",
            details=f"Updated serious breach {breach_id} status to '{status}'.",
            record_id=breach_id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return breach

    async def list_serious_breaches(
        self,
        study_id: str | None = None,
        status: str | None = None,
        user_id: str = "system",
        user_role: str = "auditor",
    ) -> Sequence[Any]:
        all_breaches = await self.repo.get_serious_breaches()
        filtered = []
        for b in all_breaches:
            if study_id and b.study_id != study_id:
                continue
            if status and b.status != status:
                continue
            filtered.append(b)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="SERIOUS_BREACH_LIST",
            details=f"Listed serious breaches (study={study_id}, status={status}).",
        )
        await self.repo.save_audit_log(log)
        return filtered
