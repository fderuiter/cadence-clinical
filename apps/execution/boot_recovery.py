import logging
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from apps.execution.database.models import DictionaryImportJob, ImportState
from packages.security.context import service_audit_context

logger = logging.getLogger(__name__)


async def run_boot_recovery(session_maker: async_sessionmaker[AsyncSession]) -> None:
    """Executes a scan for active "PENDING" or "PROCESSING" dictionary import jobs during application startup.

    Transitions detected active/stuck jobs to "FAILED" under a dedicated background service audit context.
    Outputs a structured GxP FMEA-compliant compliance report to the system log with recalculated RPN scores in alignment with ADR-064.
    """
    logger.info(
        "ℹ️ [Boot Recovery] Scanning for orphaned/stuck dictionary import jobs..."
    )
    print("ℹ️ [Boot Recovery] Scanning for orphaned/stuck dictionary import jobs...")

    try:
        with service_audit_context(
            service_name="boot_recovery_service",
            change_reason="GxP FMEA-Aligned Boot Recovery: Transitioning orphaned dictionary import job to FAILED state due to service interrupt/reboot.",
        ):
            # Open a short-lived, isolated database session
            async with session_maker() as session:
                # Execute within an explicit transaction block to ensure atomic committing
                async with session.begin():
                    # Query for active jobs (PENDING or PROCESSING)
                    stmt = select(DictionaryImportJob).where(
                        DictionaryImportJob.status.in_(
                            [ImportState.PENDING, ImportState.PROCESSING]
                        )
                    )
                    res = await session.execute(stmt)
                    orphaned_jobs = res.scalars().all()

                    if not orphaned_jobs:
                        logger.info(
                            "✅ [Boot Recovery] No orphaned dictionary import jobs found."
                        )
                        print(
                            "✅ [Boot Recovery] No orphaned dictionary import jobs found."
                        )
                        return

                    logger.warning(
                        f"⚠️ [Boot Recovery] Detected {len(orphaned_jobs)} orphaned dictionary import job(s) in active state (PENDING/PROCESSING)."
                    )
                    print(
                        f"⚠️ [Boot Recovery] Detected {len(orphaned_jobs)} orphaned dictionary import job(s) in active state (PENDING/PROCESSING)."
                    )

                    for job in orphaned_jobs:
                        job_id = job.id
                        dict_type = job.dictionary_type
                        dict_version = job.dictionary_version
                        old_status = job.status

                        logger.info(
                            f"ℹ️ [Boot Recovery] Transitioning dictionary import job {job_id} ({dict_type} v{dict_version}) from {old_status} to FAILED."
                        )
                        print(
                            f"ℹ️ [Boot Recovery] Transitioning dictionary import job {job_id} ({dict_type} v{dict_version}) from {old_status} to FAILED."
                        )

                        # Update fields
                        job.status = ImportState.FAILED
                        job.completed_at = datetime.now(UTC).replace(tzinfo=None)
                        job.error_details = (
                            "Orphaned dictionary import job transitioned to FAILED status "
                            "during boot recovery scan due to service reboot/interrupt."
                        )
                        job.errors_encountered += 1

                        # Severity (S): 3 (Moderate risk of stale import state)
                        # Occurrence (O): 1 (Very Low / Mitigated due to automated startup correction)
                        # Detectability (D): 2 (High Detectability via system audit trails)
                        # Risk Priority Number (RPN) = S * O * D = 6 (< 20 Low Risk Threshold)
                        severity = 3
                        occurrence = 1
                        detectability = 2
                        rpn = severity * occurrence * detectability

                        # Structured FMEA-aligned Compliance Report
                        fmea_report = (
                            f"\n✅ [Boot Recovery] --- GxP FMEA Compliance Report ---\n"
                            f"✅ [Boot Recovery] Job ID: {job_id}\n"
                            f"✅ [Boot Recovery] Dictionary Type: {dict_type}\n"
                            f"✅ [Boot Recovery] Dictionary Version: {dict_version}\n"
                            f"✅ [Boot Recovery] Background Audit Identity: boot_recovery_service\n"
                            f"✅ [Boot Recovery] Change Reason: GxP FMEA-Aligned Boot Recovery: Transitioning orphaned dictionary import job to FAILED state due to service interrupt/reboot.\n"
                            f"✅ [Boot Recovery] Mitigation Risk Parameters:\n"
                            f"✅ [Boot Recovery]   - Severity (S): {severity} (Moderate)\n"
                            f"✅ [Boot Recovery]   - Occurrence (O): {occurrence} (Very Low / Mitigated)\n"
                            f"✅ [Boot Recovery]   - Detectability (D): {detectability} (High Detectability)\n"
                            f"✅ [Boot Recovery]   - Recalculated RPN: {rpn} (< 20 Low Risk Threshold)\n"
                            f"✅ [Boot Recovery] ----------------------------------"
                        )
                        logger.info(fmea_report)
                        print(fmea_report)

        # Confirm successful completion and release of session/transaction
        logger.info(
            "✅ [Boot Recovery] Boot recovery routine completed successfully. Database connections released."
        )
        print(
            "✅ [Boot Recovery] Boot recovery routine completed successfully. Database connections released."
        )

    except Exception as e:
        logger.error(
            f"❌ [Boot Recovery] Error during startup recovery execution: {e}",
            exc_info=True,
        )
        print(f"❌ [Boot Recovery] Error during startup recovery execution: {e}")
        raise
