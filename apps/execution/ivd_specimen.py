"""In Vitro Diagnostic (IVD) Specimen Domain Service.

Provides specimen chain-of-custody tracking, freeze-thaw thermal cycle monitoring with
automated quarantine triggers, sample integrity validation (Hemolysis, Lipemia, Icterus - HIL),
and statistical assay performance evaluation.

Requirements:
- PRD-SYS-188: In Vitro Diagnostic (IVD) Specimen & Assay Investigation Suite
- PRD-SYS-053: GxP Quality and Part 11 Audit Trail Verification
- ISO 20916:2019 / EU IVDR 2017/746 / 21 CFR Part 809 / 21 CFR Part 11
- ADR-2209 / ADR-2199
"""

import math
import uuid
from datetime import UTC, datetime

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models import (
    IntegrationOutbox,
    IVDAssayPerformanceRun,
    IVDSpecimenCustodyEvent,
    IVDSpecimenFreezeThawEvent,
    IVDSpecimenRecord,
)
from apps.execution.domain.device_models import (
    IVDAssayPerformanceRequest,
    IVDAssayPerformanceResponse,
    IVDIntegrityStatus,
    IVDSampleIntegrityCheckRequest,
    IVDSampleIntegrityCheckResponse,
    IVDSpecimenCreateRequest,
    IVDSpecimenCustodyEventRequest,
    IVDSpecimenCustodyEventResponse,
    IVDSpecimenFreezeThawEventRequest,
    IVDSpecimenFreezeThawEventResponse,
    IVDSpecimenResponse,
    IVDSpecimenStatus,
)
from apps.execution.trial_lock import TrialLockManager


class IVDSpecimenError(HTTPException):
    """Base domain exception for IVD biospecimen operations."""

    def __init__(self, detail: str, status_code: int = 400):
        super().__init__(status_code=status_code, detail=detail)


class IVDSpecimenNotFoundError(IVDSpecimenError):
    """Raised when an IVD specimen is not found."""

    def __init__(self, detail: str = "IVD biospecimen record not found."):
        super().__init__(detail=detail, status_code=404)


class IVDSpecimenQuarantinedError(IVDSpecimenError):
    """Raised when attempting an operation on a quarantined IVD specimen."""

    def __init__(self, detail: str = "IVD biospecimen is currently QUARANTINED."):
        super().__init__(detail=detail, status_code=409)


def _assert_unlocked(study_id: str | None = None, site_id: str | None = None) -> None:
    """Ensures trial and site are not locked against data mutation."""
    if TrialLockManager.is_locked():
        raise HTTPException(
            status_code=423,
            detail="Locked: Trial system is currently locked against changes.",
        )
    if site_id and TrialLockManager.is_site_locked(site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Locked: Site {site_id} is currently locked against changes.",
        )


# =========================================================================
# Specimen Registration & Retrieval
# =========================================================================


async def register_ivd_specimen(
    session: AsyncSession,
    request: IVDSpecimenCreateRequest,
    user_id: str,
    change_reason: str,
) -> IVDSpecimenResponse:
    """Registers an IVD clinical biospecimen collection in the specimen ledger."""
    _assert_unlocked(study_id=request.study_id, site_id=request.site_id)

    specimen_id = request.specimen_identifier or (
        f"SPEC-{request.study_id}-{request.subject_id[-4:] if len(request.subject_id) >= 4 else '0000'}-{uuid.uuid4().hex[:4].upper()}"
    )

    collector = request.collector_user_id or user_id

    specimen = IVDSpecimenRecord(
        study_id=request.study_id,
        site_id=request.site_id,
        subject_id=request.subject_id,
        specimen_identifier=specimen_id,
        specimen_type=request.specimen_type.value,
        collection_date=request.collection_date.replace(tzinfo=None)
        if request.collection_date
        else datetime.now(UTC).replace(tzinfo=None),
        collector_user_id=collector,
        container_type=request.container_type.value,
        initial_volume_ul=request.initial_volume_ul,
        current_volume_ul=request.initial_volume_ul,
        storage_temperature_celsius=request.storage_temperature_celsius,
        storage_location=request.storage_location,
        status=IVDSpecimenStatus.COLLECTED.value,
        is_quarantined=False,
        quarantine_reason=None,
        freeze_thaw_count=0,
        max_allowed_freeze_thaw=request.max_allowed_freeze_thaw,
        hemolysis_index_mg_dl=0.0,
        lipemia_index_mg_dl=0.0,
        icterus_index_mg_dl=0.0,
        clot_detected=False,
        temperature_excursion=False,
        integrity_status=IVDIntegrityStatus.VALID.value,
    )

    session.add(specimen)
    await session.flush()

    outbox = IntegrationOutbox(
        event_type="IVDSpecimenCollected",
        payload={
            "specimen_id": specimen.id,
            "specimen_identifier": specimen.specimen_identifier,
            "study_id": specimen.study_id,
            "site_id": specimen.site_id,
            "subject_id": specimen.subject_id,
            "specimen_type": specimen.specimen_type,
            "storage_location": specimen.storage_location,
        },
        correlation_id=f"ivd:specimen:{specimen.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox)

    return _map_specimen_to_response(specimen, user_id, change_reason, [], [])


async def list_ivd_specimens(
    session: AsyncSession,
    study_id: str | None = None,
    site_id: str | None = None,
    subject_id: str | None = None,
    status: str | None = None,
) -> list[IVDSpecimenResponse]:
    """Queries and lists IVD specimens filtered by study, site, subject, or status."""
    stmt = select(IVDSpecimenRecord).where(IVDSpecimenRecord.is_deleted.is_(False))
    if study_id:
        stmt = stmt.where(IVDSpecimenRecord.study_id == study_id)
    if site_id:
        stmt = stmt.where(IVDSpecimenRecord.site_id == site_id)
    if subject_id:
        stmt = stmt.where(IVDSpecimenRecord.subject_id == subject_id)
    if status:
        stmt = stmt.where(IVDSpecimenRecord.status == status.upper())

    stmt = stmt.order_by(IVDSpecimenRecord.created_at.desc())
    res = await session.execute(stmt)
    records = res.scalars().all()

    results: list[IVDSpecimenResponse] = []
    for rec in records:
        custody_events = await _get_custody_events(session, rec.specimen_identifier)
        ft_events = await _get_freeze_thaw_events(session, rec.specimen_identifier)
        results.append(
            _map_specimen_to_response(
                rec,
                rec.created_by or "system",
                "Query list",
                custody_events,
                ft_events,
            )
        )
    return results


async def get_ivd_specimen(
    session: AsyncSession,
    specimen_identifier: str,
) -> IVDSpecimenResponse:
    """Retrieves a single IVD specimen record with its full custody and thermal history."""
    stmt = select(IVDSpecimenRecord).where(
        (IVDSpecimenRecord.specimen_identifier == specimen_identifier)
        | (IVDSpecimenRecord.id == specimen_identifier),
        IVDSpecimenRecord.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    rec = res.scalars().first()
    if not rec:
        raise IVDSpecimenNotFoundError(
            f"IVD biospecimen '{specimen_identifier}' not found."
        )

    custody_events = await _get_custody_events(session, rec.specimen_identifier)
    ft_events = await _get_freeze_thaw_events(session, rec.specimen_identifier)
    return _map_specimen_to_response(
        rec, rec.created_by or "system", "Retrieve detail", custody_events, ft_events
    )


# =========================================================================
# Chain-of-Custody Tracking
# =========================================================================


async def log_specimen_custody_event(
    session: AsyncSession,
    specimen_identifier: str,
    request: IVDSpecimenCustodyEventRequest,
    user_id: str,
    change_reason: str,
) -> IVDSpecimenCustodyEventResponse:
    """Logs an auditable chain-of-custody transfer event between operators or locations."""
    stmt = select(IVDSpecimenRecord).where(
        (IVDSpecimenRecord.specimen_identifier == specimen_identifier)
        | (IVDSpecimenRecord.id == specimen_identifier),
        IVDSpecimenRecord.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    specimen = res.scalars().first()
    if not specimen:
        raise IVDSpecimenNotFoundError(
            f"IVD biospecimen '{specimen_identifier}' not found."
        )

    _assert_unlocked(study_id=specimen.study_id, site_id=specimen.site_id)

    now_ts = datetime.now(UTC).replace(tzinfo=None)
    event = IVDSpecimenCustodyEvent(
        specimen_identifier=specimen.specimen_identifier,
        transfer_timestamp=now_ts,
        from_user_id=request.from_user_id,
        to_user_id=request.to_user_id,
        from_location=request.from_location,
        to_location=request.to_location,
        transport_condition=request.transport_condition,
        temperature_at_transfer_celsius=request.temperature_at_transfer_celsius,
        seal_intact=request.seal_intact,
        signature_token=request.signature_token,
    )
    session.add(event)

    # Update specimen location and status
    specimen.storage_location = request.to_location
    if request.temperature_at_transfer_celsius is not None:
        specimen.storage_temperature_celsius = request.temperature_at_transfer_celsius

    if not request.seal_intact:
        specimen.status = IVDSpecimenStatus.QUARANTINED.value
        specimen.is_quarantined = True
        specimen.quarantine_reason = (
            "Chain-of-custody security seal broken during transit."
        )
        specimen.integrity_status = IVDIntegrityStatus.COMPROMISED.value

    session.add(specimen)
    await session.flush()

    outbox = IntegrationOutbox(
        event_type="IVDSpecimenCustodyTransferred",
        payload={
            "custody_event_id": event.id,
            "specimen_identifier": specimen.specimen_identifier,
            "from_user": request.from_user_id,
            "to_user": request.to_user_id,
            "to_location": request.to_location,
            "seal_intact": request.seal_intact,
        },
        correlation_id=f"ivd:custody:{event.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox)

    return IVDSpecimenCustodyEventResponse(
        id=event.id,
        specimen_identifier=event.specimen_identifier,
        transfer_timestamp=event.transfer_timestamp,
        from_user_id=event.from_user_id,
        to_user_id=event.to_user_id,
        from_location=event.from_location,
        to_location=event.to_location,
        transport_condition=event.transport_condition,
        temperature_at_transfer_celsius=event.temperature_at_transfer_celsius,
        seal_intact=event.seal_intact,
        signature_token=event.signature_token,
    )


# =========================================================================
# Freeze-Thaw Thermal Cycle Tracking & Auto-Quarantine
# =========================================================================


async def log_specimen_freeze_thaw_event(
    session: AsyncSession,
    specimen_identifier: str,
    request: IVDSpecimenFreezeThawEventRequest,
    user_id: str,
    change_reason: str,
) -> IVDSpecimenFreezeThawEventResponse:
    """Logs a freeze-thaw thermal cycle.

    Automatically quarantines the specimen if cycle count exceeds max allowed limit
    or if a thermal excursion occurred during the cycle.
    """
    stmt = select(IVDSpecimenRecord).where(
        (IVDSpecimenRecord.specimen_identifier == specimen_identifier)
        | (IVDSpecimenRecord.id == specimen_identifier),
        IVDSpecimenRecord.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    specimen = res.scalars().first()
    if not specimen:
        raise IVDSpecimenNotFoundError(
            f"IVD biospecimen '{specimen_identifier}' not found."
        )

    _assert_unlocked(study_id=specimen.study_id, site_id=specimen.site_id)

    new_cycle = specimen.freeze_thaw_count + 1
    start_ts = (
        request.thaw_start_timestamp.replace(tzinfo=None)
        if request.thaw_start_timestamp
        else datetime.now(UTC).replace(tzinfo=None)
    )
    end_ts = (
        request.thaw_end_timestamp.replace(tzinfo=None)
        if request.thaw_end_timestamp
        else None
    )

    event = IVDSpecimenFreezeThawEvent(
        specimen_identifier=specimen.specimen_identifier,
        cycle_number=new_cycle,
        thaw_start_timestamp=start_ts,
        thaw_end_timestamp=end_ts,
        thaw_duration_minutes=request.thaw_duration_minutes,
        thaw_temperature_celsius=request.thaw_temperature_celsius,
        operator_user_id=request.operator_user_id,
        purpose=request.purpose,
        is_excursion=request.is_excursion,
    )
    session.add(event)

    # Update specimen cycle count
    specimen.freeze_thaw_count = new_cycle

    # Automated Quarantine Check
    is_limit_exceeded = new_cycle > specimen.max_allowed_freeze_thaw
    if is_limit_exceeded or request.is_excursion:
        specimen.status = IVDSpecimenStatus.QUARANTINED.value
        specimen.is_quarantined = True
        reasons: list[str] = []
        if is_limit_exceeded:
            reasons.append(
                f"Freeze-thaw cycles ({new_cycle}) exceeded maximum allowed limit ({specimen.max_allowed_freeze_thaw})."
            )
        if request.is_excursion:
            reasons.append(
                f"Thermal excursion logged during thaw cycle {new_cycle} (temp: {request.thaw_temperature_celsius}°C, duration: {request.thaw_duration_minutes}m)."
            )
        specimen.quarantine_reason = " | ".join(reasons)
        specimen.temperature_excursion = True
        specimen.integrity_status = IVDIntegrityStatus.COMPROMISED.value

    session.add(specimen)
    await session.flush()

    outbox = IntegrationOutbox(
        event_type="IVDSpecimenFreezeThawLogged",
        payload={
            "ft_event_id": event.id,
            "specimen_identifier": specimen.specimen_identifier,
            "cycle_number": new_cycle,
            "is_quarantined": specimen.is_quarantined,
            "quarantine_reason": specimen.quarantine_reason,
        },
        correlation_id=f"ivd:ft:{event.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox)

    return IVDSpecimenFreezeThawEventResponse(
        id=event.id,
        specimen_identifier=event.specimen_identifier,
        cycle_number=event.cycle_number,
        thaw_start_timestamp=event.thaw_start_timestamp,
        thaw_end_timestamp=event.thaw_end_timestamp,
        thaw_duration_minutes=event.thaw_duration_minutes,
        thaw_temperature_celsius=event.thaw_temperature_celsius,
        operator_user_id=event.operator_user_id,
        purpose=event.purpose,
        is_excursion=event.is_excursion,
    )


# =========================================================================
# Sample Integrity Validation (HIL Indices)
# =========================================================================


async def validate_sample_integrity(
    session: AsyncSession,
    specimen_identifier: str,
    request: IVDSampleIntegrityCheckRequest,
    user_id: str,
    change_reason: str,
) -> IVDSampleIntegrityCheckResponse:
    """Validates specimen analytical integrity using Hemolysis, Lipemia, and Icterus (HIL) indices.

    Thresholds:
    - Hemolysis > 100 mg/dL: Significant erythrocyte lysis -> Flagged/Compromised
    - Lipemia > 300 mg/dL: Lipid turbidity interference -> Flagged/Compromised
    - Icterus > 20 mg/dL: Bilirubin spectral interference -> Flagged/Compromised
    - Clot detected or temperature excursion -> Quarantine/Compromised
    """
    stmt = select(IVDSpecimenRecord).where(
        (IVDSpecimenRecord.specimen_identifier == specimen_identifier)
        | (IVDSpecimenRecord.id == specimen_identifier),
        IVDSpecimenRecord.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    specimen = res.scalars().first()
    if not specimen:
        raise IVDSpecimenNotFoundError(
            f"IVD biospecimen '{specimen_identifier}' not found."
        )

    _assert_unlocked(study_id=specimen.study_id, site_id=specimen.site_id)

    now_ts = datetime.now(UTC).replace(tzinfo=None)

    specimen.hemolysis_index_mg_dl = request.hemolysis_index_mg_dl
    specimen.lipemia_index_mg_dl = request.lipemia_index_mg_dl
    specimen.icterus_index_mg_dl = request.icterus_index_mg_dl
    specimen.clot_detected = request.clot_detected
    specimen.temperature_excursion = (
        specimen.temperature_excursion or request.temperature_excursion
    )
    specimen.integrity_validated_at = now_ts
    specimen.integrity_validated_by = user_id

    # Evaluate integrity thresholds
    compromised_reasons: list[str] = []
    if request.hemolysis_index_mg_dl > 100.0:
        compromised_reasons.append(
            f"Elevated Hemolysis index ({request.hemolysis_index_mg_dl} mg/dL > 100 mg/dL)"
        )
    if request.lipemia_index_mg_dl > 300.0:
        compromised_reasons.append(
            f"Elevated Lipemia index ({request.lipemia_index_mg_dl} mg/dL > 300 mg/dL)"
        )
    if request.icterus_index_mg_dl > 20.0:
        compromised_reasons.append(
            f"Elevated Icterus index ({request.icterus_index_mg_dl} mg/dL > 20 mg/dL)"
        )
    if request.clot_detected:
        compromised_reasons.append("Microclots detected in sample matrix")
    if request.temperature_excursion:
        compromised_reasons.append("Sample subjected to undocumented thermal excursion")
    if not request.volume_adequate:
        compromised_reasons.append("Sample volume is insufficient for diagnostic assay")

    if compromised_reasons:
        specimen.integrity_status = IVDIntegrityStatus.COMPROMISED.value
        specimen.status = IVDSpecimenStatus.QUARANTINED.value
        specimen.is_quarantined = True
        specimen.quarantine_reason = " | ".join(compromised_reasons)
    else:
        specimen.integrity_status = IVDIntegrityStatus.VALID.value

    session.add(specimen)
    await session.flush()

    outbox = IntegrationOutbox(
        event_type="IVDSpecimenIntegrityAssessed",
        payload={
            "specimen_identifier": specimen.specimen_identifier,
            "integrity_status": specimen.integrity_status,
            "is_quarantined": specimen.is_quarantined,
            "quarantine_reason": specimen.quarantine_reason,
            "hemolysis_index": specimen.hemolysis_index_mg_dl,
            "lipemia_index": specimen.lipemia_index_mg_dl,
            "icterus_index": specimen.icterus_index_mg_dl,
        },
        correlation_id=f"ivd:integrity:{specimen.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox)

    return IVDSampleIntegrityCheckResponse(
        specimen_identifier=specimen.specimen_identifier,
        integrity_status=specimen.integrity_status,
        hemolysis_index_mg_dl=specimen.hemolysis_index_mg_dl,
        lipemia_index_mg_dl=specimen.lipemia_index_mg_dl,
        icterus_index_mg_dl=specimen.icterus_index_mg_dl,
        clot_detected=specimen.clot_detected,
        temperature_excursion=specimen.temperature_excursion,
        is_quarantined=specimen.is_quarantined,
        quarantine_reason=specimen.quarantine_reason,
        validated_at=now_ts,
        validated_by=user_id,
    )


# =========================================================================
# IVD Assay Performance Evaluation
# =========================================================================


async def compute_and_record_assay_performance(
    session: AsyncSession,
    request: IVDAssayPerformanceRequest,
    user_id: str,
    change_reason: str,
) -> IVDAssayPerformanceResponse:
    """Computes clinical and analytical performance metrics for an IVD diagnostic assay:

    - Diagnostic Sensitivity: TP / (TP + FN) * 100
    - Diagnostic Specificity: TN / (TN + FP) * 100
    - Positive Predictive Value (PPV): TP / (TP + FP) * 100
    - Negative Predictive Value (NPV): TN / (TN + FN) * 100
    - Overall Accuracy: (TP + TN) / (TP + TN + FP + FN) * 100
    - Cohen's Kappa concordance
    - Intra-assay CV% and Inter-assay CV% (Repeatability & Reproducibility)
    - Limit of Detection (LOD) & Limit of Quantitation (LOQ)
    - Linearity R²
    """
    _assert_unlocked(study_id=request.study_id)

    tp = request.true_positives
    fp = request.false_positives
    tn = request.true_negatives
    fn = request.false_negatives

    total_pos = tp + fn
    total_neg = tn + fp
    total_pred_pos = tp + fp
    total_pred_neg = tn + fn
    total_samples = tp + fp + tn + fn

    sensitivity = (tp / total_pos * 100.0) if total_pos > 0 else 0.0
    specificity = (tn / total_neg * 100.0) if total_neg > 0 else 0.0
    ppv = (tp / total_pred_pos * 100.0) if total_pred_pos > 0 else 0.0
    npv = (tn / total_pred_neg * 100.0) if total_pred_neg > 0 else 0.0
    accuracy = ((tp + tn) / total_samples * 100.0) if total_samples > 0 else 0.0

    # Cohen's Kappa
    if total_samples > 0:
        po = (tp + tn) / total_samples
        pe = (total_pred_pos / total_samples) * (total_pos / total_samples) + (
            total_pred_neg / total_samples
        ) * (total_neg / total_samples)
        kappa = (po - pe) / (1 - pe) if (1 - pe) != 0 else 1.0
    else:
        kappa = 0.0

    # Precision CV% (Intra-assay)
    if request.replicate_measurements and len(request.replicate_measurements) >= 2:
        mean_val = sum(request.replicate_measurements) / len(
            request.replicate_measurements
        )
        var_val = sum((x - mean_val) ** 2 for x in request.replicate_measurements) / (
            len(request.replicate_measurements) - 1
        )
        std_val = math.sqrt(var_val)
        intra_cv = (std_val / mean_val * 100.0) if mean_val != 0 else 0.0
    else:
        intra_cv = 0.0

    # Precision CV% (Inter-assay)
    if request.inter_assay_runs and len(request.inter_assay_runs) >= 2:
        run_means = [sum(r) / len(r) for r in request.inter_assay_runs if len(r) > 0]
        if len(run_means) >= 2:
            grand_mean = sum(run_means) / len(run_means)
            run_var = sum((m - grand_mean) ** 2 for m in run_means) / (
                len(run_means) - 1
            )
            run_std = math.sqrt(run_var)
            inter_cv = (run_std / grand_mean * 100.0) if grand_mean != 0 else 0.0
        else:
            inter_cv = 0.0
    else:
        inter_cv = intra_cv * 1.2 if intra_cv > 0 else 0.0

    # LOD / LOQ
    lod: float | None = None
    loq: float | None = None
    if request.blank_measurements and len(request.blank_measurements) >= 2:
        b_mean = sum(request.blank_measurements) / len(request.blank_measurements)
        b_var = sum((b - b_mean) ** 2 for b in request.blank_measurements) / (
            len(request.blank_measurements) - 1
        )
        b_sd = math.sqrt(b_var)
        lod = round(b_mean + 3.0 * b_sd, 4)
        loq = round(b_mean + 10.0 * b_sd, 4)

    # Linearity R²
    r_squared: float | None = None
    if (
        request.linearity_concentrations
        and request.linearity_signals
        and len(request.linearity_concentrations) == len(request.linearity_signals)
        and len(request.linearity_concentrations) >= 3
    ):
        n = len(request.linearity_concentrations)
        x_vals = request.linearity_concentrations
        y_vals = request.linearity_signals
        mean_x = sum(x_vals) / n
        mean_y = sum(y_vals) / n
        ss_xx = sum((x - mean_x) ** 2 for x in x_vals)
        ss_yy = sum((y - mean_y) ** 2 for y in y_vals)
        ss_xy = sum(
            (x - mean_x) * (y - mean_y) for x, y in zip(x_vals, y_vals, strict=False)
        )
        if ss_xx > 0 and ss_yy > 0:
            r = ss_xy / math.sqrt(ss_xx * ss_yy)
            r_squared = round(r**2, 4)

    # Acceptance Criteria: Sens >= 85%, Spec >= 85%, Intra-CV <= 15%
    passes_acceptance = (
        (sensitivity >= 85.0 or total_pos == 0)
        and (specificity >= 85.0 or total_neg == 0)
        and (intra_cv <= 15.0 or intra_cv == 0.0)
    )

    status_str = "PASSED" if passes_acceptance else "FAILED"

    perf_record = IVDAssayPerformanceRun(
        study_id=request.study_id,
        assay_name=request.assay_name,
        assay_lot_number=request.assay_lot_number,
        device_udi_di=request.device_udi_di,
        true_positives=tp,
        false_positives=fp,
        true_negatives=tn,
        false_negatives=fn,
        sensitivity_percent=round(sensitivity, 2),
        specificity_percent=round(specificity, 2),
        ppv_percent=round(ppv, 2),
        npv_percent=round(npv, 2),
        accuracy_percent=round(accuracy, 2),
        intra_assay_cv_percent=round(intra_cv, 2),
        inter_assay_cv_percent=round(inter_cv, 2),
        limit_of_detection=lod,
        limit_of_quantitation=loq,
        linearity_r_squared=r_squared,
        status=status_str,
    )
    session.add(perf_record)
    await session.flush()

    outbox = IntegrationOutbox(
        event_type="IVDAssayPerformanceEvaluated",
        payload={
            "performance_run_id": perf_record.id,
            "study_id": perf_record.study_id,
            "assay_name": perf_record.assay_name,
            "sensitivity": perf_record.sensitivity_percent,
            "specificity": perf_record.specificity_percent,
            "accuracy": perf_record.accuracy_percent,
            "status": perf_record.status,
        },
        correlation_id=f"ivd:assay:perf:{perf_record.id}:{uuid.uuid4()}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(outbox)

    return IVDAssayPerformanceResponse(
        id=perf_record.id,
        study_id=perf_record.study_id,
        assay_name=perf_record.assay_name,
        assay_lot_number=perf_record.assay_lot_number,
        device_udi_di=perf_record.device_udi_di,
        true_positives=perf_record.true_positives,
        false_positives=perf_record.false_positives,
        true_negatives=perf_record.true_negatives,
        false_negatives=perf_record.false_negatives,
        sensitivity_percent=perf_record.sensitivity_percent,
        specificity_percent=perf_record.specificity_percent,
        ppv_percent=perf_record.ppv_percent,
        npv_percent=perf_record.npv_percent,
        accuracy_percent=perf_record.accuracy_percent,
        cohens_kappa=round(kappa, 4),
        intra_assay_cv_percent=perf_record.intra_assay_cv_percent,
        inter_assay_cv_percent=perf_record.inter_assay_cv_percent,
        limit_of_detection=perf_record.limit_of_detection,
        limit_of_quantitation=perf_record.limit_of_quantitation,
        linearity_r_squared=perf_record.linearity_r_squared,
        status=perf_record.status,
        passes_acceptance_criteria=passes_acceptance,
        created_at=datetime.now(UTC),
        created_by=user_id,
    )


# =========================================================================
# Internal Helpers
# =========================================================================


async def _get_custody_events(
    session: AsyncSession, specimen_id: str
) -> list[IVDSpecimenCustodyEventResponse]:
    stmt = (
        select(IVDSpecimenCustodyEvent)
        .where(
            IVDSpecimenCustodyEvent.specimen_identifier == specimen_id,
            IVDSpecimenCustodyEvent.is_deleted.is_(False),
        )
        .order_by(IVDSpecimenCustodyEvent.transfer_timestamp.asc())
    )
    res = await session.execute(stmt)
    events = res.scalars().all()
    return [
        IVDSpecimenCustodyEventResponse(
            id=e.id,
            specimen_identifier=e.specimen_identifier,
            transfer_timestamp=e.transfer_timestamp,
            from_user_id=e.from_user_id,
            to_user_id=e.to_user_id,
            from_location=e.from_location,
            to_location=e.to_location,
            transport_condition=e.transport_condition,
            temperature_at_transfer_celsius=e.temperature_at_transfer_celsius,
            seal_intact=e.seal_intact,
            signature_token=e.signature_token,
        )
        for e in events
    ]


async def _get_freeze_thaw_events(
    session: AsyncSession, specimen_id: str
) -> list[IVDSpecimenFreezeThawEventResponse]:
    stmt = (
        select(IVDSpecimenFreezeThawEvent)
        .where(
            IVDSpecimenFreezeThawEvent.specimen_identifier == specimen_id,
            IVDSpecimenFreezeThawEvent.is_deleted.is_(False),
        )
        .order_by(IVDSpecimenFreezeThawEvent.cycle_number.asc())
    )
    res = await session.execute(stmt)
    events = res.scalars().all()
    return [
        IVDSpecimenFreezeThawEventResponse(
            id=e.id,
            specimen_identifier=e.specimen_identifier,
            cycle_number=e.cycle_number,
            thaw_start_timestamp=e.thaw_start_timestamp,
            thaw_end_timestamp=e.thaw_end_timestamp,
            thaw_duration_minutes=e.thaw_duration_minutes,
            thaw_temperature_celsius=e.thaw_temperature_celsius,
            operator_user_id=e.operator_user_id,
            purpose=e.purpose,
            is_excursion=e.is_excursion,
        )
        for e in events
    ]


def _map_specimen_to_response(
    rec: IVDSpecimenRecord,
    user_id: str,
    change_reason: str,
    custody_events: list[IVDSpecimenCustodyEventResponse],
    ft_events: list[IVDSpecimenFreezeThawEventResponse],
) -> IVDSpecimenResponse:
    return IVDSpecimenResponse(
        id=rec.id,
        study_id=rec.study_id,
        site_id=rec.site_id,
        subject_id=rec.subject_id,
        specimen_identifier=rec.specimen_identifier,
        specimen_type=rec.specimen_type,
        collection_date=rec.collection_date,
        collector_user_id=rec.collector_user_id,
        container_type=rec.container_type,
        initial_volume_ul=rec.initial_volume_ul,
        current_volume_ul=rec.current_volume_ul,
        storage_temperature_celsius=rec.storage_temperature_celsius,
        storage_location=rec.storage_location,
        status=rec.status,
        is_quarantined=rec.is_quarantined,
        quarantine_reason=rec.quarantine_reason,
        freeze_thaw_count=rec.freeze_thaw_count,
        max_allowed_freeze_thaw=rec.max_allowed_freeze_thaw,
        hemolysis_index_mg_dl=rec.hemolysis_index_mg_dl,
        lipemia_index_mg_dl=rec.lipemia_index_mg_dl,
        icterus_index_mg_dl=rec.icterus_index_mg_dl,
        clot_detected=rec.clot_detected,
        temperature_excursion=rec.temperature_excursion,
        integrity_status=rec.integrity_status,
        integrity_validated_at=rec.integrity_validated_at,
        integrity_validated_by=rec.integrity_validated_by,
        custody_events=custody_events,
        freeze_thaw_events=ft_events,
        created_at=datetime.now(UTC),
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=rec.version,
    )
