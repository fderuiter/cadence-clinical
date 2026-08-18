import uuid
from datetime import UTC, datetime

from apps.ctms.domain.exceptions import IPKitNotFoundError, IPQuarantineError
from apps.ctms.domain.models import (
    CTMSAuditLogEntity,
    IPDestructionCertificateEntity,
    IPKitRecordEntity,
    IPTemperatureExcursionEntity,
)
from apps.ctms.domain.ports import (
    ICTMSDelegationRepository,
    IETMFClientPort,
    IIPAccountabilityRepository,
)


class IPAccountabilityService:
    """Application service for Investigational Product (IP) Accountability & Temperature Excursions."""

    def __init__(
        self,
        ip_repo: IIPAccountabilityRepository,
        etmf_client: IETMFClientPort | None = None,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.ip_repo = ip_repo
        self.etmf_client = etmf_client
        self.doa_repo = doa_repo

    async def receive_shipment_kits(
        self,
        study_id: str,
        site_id: str,
        kit_numbers: list[str],
        lot_number: str,
        kit_type: str,
        shipment_tracking_number: str,
        expiration_date: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> list[IPKitRecordEntity]:
        received_kits = []
        for kit_no in kit_numbers:
            entity = IPKitRecordEntity(
                study_id=study_id,
                site_id=site_id,
                kit_number=kit_no,
                lot_number=lot_number,
                kit_type=kit_type.upper(),
                shipment_tracking_number=shipment_tracking_number,
                expiration_date=expiration_date,
                status="RECEIVED_AVAILABLE",
                received_date=datetime.now(UTC).isoformat(),
                created_by=user_id,
                reason_for_change=reason_for_change,
                version_index=1,
            )
            saved = await self.ip_repo.save_ip_kit(entity)
            received_kits.append(saved)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="IP_SHIPMENT_RECEIVED",
                details=f"Received {len(kit_numbers)} IP kits for lot {lot_number} at site {site_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return received_kits

    async def log_temperature_excursion(
        self,
        study_id: str,
        site_id: str,
        kit_ids: list[str],
        excursion_type: str,
        min_temp_celsius: float,
        max_temp_celsius: float,
        duration_hours: float,
        occurred_at: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> IPTemperatureExcursionEntity:
        # Quarantine affected kits
        for kit_id in kit_ids:
            kit = await self.ip_repo.get_ip_kit(kit_id)
            if kit:
                kit.status = "QUARANTINED"
                kit.version_index += 1
                kit.reason_for_change = f"Temperature excursion: {min_temp_celsius}C to {max_temp_celsius}C for {duration_hours}h"
                await self.ip_repo.save_ip_kit(kit)

        entity = IPTemperatureExcursionEntity(
            study_id=study_id,
            site_id=site_id,
            kit_ids=kit_ids,
            excursion_type=excursion_type.upper(),
            min_temp_celsius=min_temp_celsius,
            max_temp_celsius=max_temp_celsius,
            duration_hours=duration_hours,
            occurred_at=occurred_at,
            disposition_status="QUARANTINED",
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.ip_repo.save_temperature_excursion(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="IP_TEMP_EXCURSION_LOGGED",
                details=f"Logged temperature excursion on {len(kit_ids)} kits at site {site_id} ({min_temp_celsius}C - {max_temp_celsius}C). Kits QUARANTINED. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def disposition_temperature_excursion(
        self,
        excursion_id: str,
        disposition_status: str,  # QA_APPROVED_USE, QA_REJECTED_DESTROY
        qa_rationale: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> IPTemperatureExcursionEntity:
        excursion = await self.ip_repo.get_temperature_excursion(excursion_id)
        if not excursion:
            raise ValueError(f"Excursion {excursion_id} not found")

        excursion.disposition_status = disposition_status.upper()
        excursion.qa_reviewed_by = user_id
        excursion.qa_reviewed_at = datetime.now(UTC).isoformat()
        excursion.qa_rationale = qa_rationale
        excursion.version_index += 1
        excursion.reason_for_change = reason_for_change

        saved = await self.ip_repo.save_temperature_excursion(excursion)

        # Update kit statuses based on QA disposition
        new_kit_status = (
            "RECEIVED_AVAILABLE"
            if disposition_status.upper() == "QA_APPROVED_USE"
            else "DESTROYED"
        )
        for kit_id in excursion.kit_ids:
            kit = await self.ip_repo.get_ip_kit(kit_id)
            if kit:
                kit.status = new_kit_status
                kit.version_index += 1
                kit.reason_for_change = (
                    f"QA Disposition: {disposition_status}. Rationale: {qa_rationale}"
                )
                await self.ip_repo.save_ip_kit(kit)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="IP_EXCURSION_QA_DISPOSITION",
                details=f"QA disposition for excursion {excursion_id}: {disposition_status}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def dispense_kit(
        self,
        kit_id: str,
        subject_id: str,
        visit_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> IPKitRecordEntity:
        kit = await self.ip_repo.get_ip_kit(kit_id)
        if not kit:
            raise IPKitNotFoundError(f"Kit {kit_id} not found")
        if kit.status == "QUARANTINED":
            raise IPQuarantineError(
                f"Kit {kit.kit_number} is QUARANTINED and cannot be dispensed."
            )
        if kit.status != "RECEIVED_AVAILABLE":
            raise ValueError(
                f"Kit {kit.kit_number} is in state {kit.status} and cannot be dispensed."
            )

        kit.status = "DISPENSED"
        kit.dispensed_subject_id = subject_id
        kit.dispensed_visit_id = visit_id
        kit.dispensed_date = datetime.now(UTC).isoformat()
        kit.version_index += 1
        kit.reason_for_change = reason_for_change

        saved = await self.ip_repo.save_ip_kit(kit)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="IP_KIT_DISPENSED",
                details=f"Dispensed kit {kit.kit_number} to subject {subject_id} at visit {visit_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def reconcile_returned_kit(
        self,
        kit_id: str,
        returned_units_count: int,
        expected_units_count: int,
        notes: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> IPKitRecordEntity:
        kit = await self.ip_repo.get_ip_kit(kit_id)
        if not kit:
            raise IPKitNotFoundError(f"Kit {kit_id} not found")

        compliance = (
            round((returned_units_count / max(1, expected_units_count)) * 100.0, 2)
            if expected_units_count > 0
            else 100.0
        )

        kit.status = "RETURNED_TO_SITE"
        kit.returned_units_count = returned_units_count
        kit.expected_units_count = expected_units_count
        kit.compliance_percentage = compliance
        kit.notes = notes
        kit.version_index += 1
        kit.reason_for_change = reason_for_change

        saved = await self.ip_repo.save_ip_kit(kit)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="IP_KIT_RECONCILED",
                details=f"Reconciled kit {kit.kit_number}: {returned_units_count}/{expected_units_count} units ({compliance}% compliance). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def generate_destruction_certificate(
        self,
        study_id: str,
        site_id: str,
        kit_ids: list[str],
        destruction_method: str,
        witness_user_id: str,
        witness_role: str,
        pi_signature_hash: str,
        reason_for_destruction: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> IPDestructionCertificateEntity:
        cert_num = f"COD-{site_id[:4]}-{datetime.now(UTC).strftime('%Y%m')}-{uuid.uuid4().hex[:4].upper()}"

        entity = IPDestructionCertificateEntity(
            study_id=study_id,
            site_id=site_id,
            certificate_number=cert_num,
            kit_ids=kit_ids,
            destruction_method=destruction_method.upper(),
            destruction_date=datetime.now(UTC).isoformat(),
            witness_user_id=witness_user_id,
            witness_role=witness_role,
            pi_signature_hash=pi_signature_hash,
            pi_signed_at=datetime.now(UTC).isoformat(),
            reason_for_destruction=reason_for_destruction,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.ip_repo.save_destruction_certificate(entity)

        # Mark kits destroyed
        for kit_id in kit_ids:
            kit = await self.ip_repo.get_ip_kit(kit_id)
            if kit:
                kit.status = "DESTROYED"
                kit.version_index += 1
                kit.reason_for_change = f"Destroyed under Certificate {cert_num}"
                await self.ip_repo.save_ip_kit(kit)

        # eTMF push (DIA Zone 06 IP & Supplies)
        if self.etmf_client:
            roles_list = [r.strip() for r in user_roles.split(",") if r.strip()]
            await self.etmf_client.push_document(
                study_id=study_id,
                site_id=site_id,
                title=f"Certificate of Destruction {cert_num}",
                content_text=f"Certificate: {cert_num}\nMethod: {destruction_method}\nWitness: {witness_user_id} ({witness_role})\nPI Sig Hash: {pi_signature_hash}\nReason: {reason_for_destruction}",
                dia_zone="06",
                dia_section="06.03",
                dia_artifact="Certificate of Destruction",
                user_id=user_id,
                user_roles=roles_list,
                reason_for_change=reason_for_change,
            )

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="IP_DESTRUCTION_CERTIFIED",
                details=f"Witnessed destruction of {len(kit_ids)} kits under Certificate {cert_num}. PI Signed. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_ip_kits(
        self, study_id: str, site_id: str | None = None, status: str | None = None
    ) -> list[IPKitRecordEntity]:
        return await self.ip_repo.list_ip_kits(study_id, site_id, status)
