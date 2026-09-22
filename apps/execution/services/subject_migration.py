"""Live subject data migration engine for protocol amendments.

Requirements: PRD-SYS-001
"""

from typing import Any

import packages  # noqa: F401


class LiveSubjectMigrationEngine:
    """Engine migrating existing subject eCRF form submissions to amended protocol versions.

    Requirements: PRD-SYS-001
    """

    def migrate_subject_submissions(
        self,
        subject_id: str,
        old_version: str,
        new_version: str,
        form_submissions: list[dict[str, Any]],
        field_mapping: dict[str, str],
    ) -> dict[str, Any]:
        """Migrate subject eCRF form submissions from old protocol version to new version.

        Args:
            subject_id: Target subject ID.
            old_version: Baseline protocol version string.
            new_version: Target amended protocol version string.
            form_submissions: List of submission dictionaries.
            field_mapping: Mapping dictionary of old_field_name -> new_field_name.

        Returns:
            Migration summary metrics dictionary.
        """
        migrated_count = 0
        updated_fields_count = 0

        for sub in form_submissions:
            if sub.get("protocol_version") == old_version or not sub.get(
                "protocol_version"
            ):
                sub["protocol_version"] = new_version
                migrated_count += 1

                # Re-map fields if mapping present
                data = sub.get("data", {})
                for old_key, new_key in field_mapping.items():
                    if old_key in data:
                        val = data.pop(old_key)
                        data[new_key] = val
                        updated_fields_count += 1

        return {
            "subject_id": subject_id,
            "version_from": old_version,
            "version_to": new_version,
            "migrated_submissions_count": migrated_count,
            "updated_fields_count": updated_fields_count,
            "status": "COMPLETED",
        }

    async def migrate_subject_submissions_db(
        self,
        session: Any,
        subject_id: str,
        old_version: str,
        new_version: str,
        field_mapping: dict[str, str],
    ) -> dict[str, Any]:
        """Migrate subject eCRF form submissions from old protocol version to new version in the database.

        Clones the original row as inactive, marks original as read-only, and creates a new mutated active row
        with direct link pointing to the inactive cloned row, integrating with cryptographic ledger sealing.
        """
        import copy
        import uuid

        from sqlalchemy import select

        from apps.execution.database.models.form import FormSubmission
        from apps.execution.database.sealer import execute_audit_sealing_cycle

        stmt = select(FormSubmission).where(
            FormSubmission.subject_id == subject_id,
            FormSubmission.protocol_version == old_version,
            FormSubmission.is_active.is_(True),
            FormSubmission.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        targets = res.scalars().all()

        migrated_count = 0
        updated_fields_count = 0

        for target_sub in targets:
            # 1. Clone the target submission record to create an inactive historical record
            cloned_id = str(uuid.uuid4())
            cloned_record = FormSubmission(
                id=cloned_id,
                study_id=target_sub.study_id,
                site_id=target_sub.site_id,
                subject_id=target_sub.subject_id,
                visit_id=target_sub.visit_id,
                form_id=target_sub.form_id,
                status=target_sub.status,
                signature_manifest=copy.deepcopy(target_sub.signature_manifest)
                if target_sub.signature_manifest
                else None,
                protocol_version=target_sub.protocol_version,
                payload=copy.deepcopy(target_sub.payload)
                if target_sub.payload
                else None,
                is_active=False,  # flags the copy as inactive
                is_readonly=True,  # cloned record is read-only
                cloned_from_id=target_sub.cloned_from_id,
            )
            session.add(cloned_record)

            # 2. Mark the original row as read-only and inactive
            target_sub.is_readonly = True
            target_sub.is_active = False

            # 3. Create the mutated payload
            mutated_payload = (
                copy.deepcopy(target_sub.payload) if target_sub.payload else {}
            )
            for old_key, new_key in field_mapping.items():
                if old_key in mutated_payload:
                    val = mutated_payload.pop(old_key)
                    mutated_payload[new_key] = val
                    updated_fields_count += 1

            # 4. Write the newly mutated submission payload as a new active row
            mutated_id = str(uuid.uuid4())
            mutated_record = FormSubmission(
                id=mutated_id,
                study_id=target_sub.study_id,
                site_id=target_sub.site_id,
                subject_id=target_sub.subject_id,
                visit_id=target_sub.visit_id,
                form_id=target_sub.form_id,
                status=target_sub.status,
                signature_manifest=copy.deepcopy(target_sub.signature_manifest)
                if target_sub.signature_manifest
                else None,
                protocol_version=new_version,
                payload=mutated_payload,
                is_active=True,  # new row is active
                is_readonly=False,
                cloned_from_id=cloned_id,  # direct link pointing to corresponding inactive cloned row
            )
            session.add(mutated_record)
            migrated_count += 1

        if migrated_count > 0:
            # Force flush and commit to trigger GxP AuditedModel insertion into audit_logs table
            await session.commit()

            # 5. Every cloning and mutation event must integrate with the platform's cryptographic ledger sealing
            await execute_audit_sealing_cycle(session)

        return {
            "subject_id": subject_id,
            "version_from": old_version,
            "version_to": new_version,
            "migrated_submissions_count": migrated_count,
            "updated_fields_count": updated_fields_count,
            "status": "COMPLETED",
        }
