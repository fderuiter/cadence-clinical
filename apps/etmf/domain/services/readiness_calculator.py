"""Domain service for evaluating eTMF inspection readiness and quality metrics.

Calculates multi-dimensional readiness scores, zone matrices, milestone completion,
QC bottlenecks, and expiration risks in accordance with DIA TMF standards and GxP guidelines.
"""

from collections import defaultdict
from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.etmf.domain.tmf_reference_model import (
    get_active_catalog,
    normalize_milestone,
)


class ZoneReadinessMetric(BaseModel):
    """Inspection readiness metrics for a single DIA TMF Zone (1-11)."""

    model_config = ConfigDict(frozen=True)

    zone_code: int = Field(..., description="DIA Zone code (1-11)")
    zone_name: str = Field(..., description="DIA Zone name")
    expected_count: int = Field(0, description="Total expected artifacts in zone")
    present_count: int = Field(0, description="Total present artifacts uploaded")
    approved_count: int = Field(0, description="Total approved artifacts")
    pending_qc_count: int = Field(0, description="Artifacts undergoing QC review")
    rejected_count: int = Field(0, description="Artifacts rejected in QC")
    missing_count: int = Field(0, description="Missing expected artifacts")
    completeness_percentage: float = Field(
        0.0, description="Completeness percentage for this zone (0-100)"
    )


class MilestoneReadinessMetric(BaseModel):
    """Inspection readiness metrics for a clinical milestone."""

    model_config = ConfigDict(frozen=True)

    milestone: str = Field(..., description="Normalized milestone name")
    is_complete: bool = Field(..., description="Whether all expected artifacts exist")
    expected_count: int = Field(..., description="Total required artifacts")
    present_count: int = Field(..., description="Total present artifacts")
    approved_count: int = Field(..., description="Total approved artifacts")
    missing_artifacts: list[str] = Field(
        default_factory=list, description="Missing artifact names"
    )
    completeness_percentage: float = Field(
        0.0, description="Completeness percentage (0-100)"
    )


class InspectionReadinessReport(BaseModel):
    """Comprehensive eTMF Inspection Readiness and Quality Assessment Report."""

    model_config = ConfigDict(frozen=True)

    study_id: str = Field(..., description="Study identifier")
    generated_at: str = Field(..., description="ISO 8601 UTC timestamp of evaluation")
    overall_readiness_score: float = Field(
        ..., description="Weighted inspection readiness score (0-100)"
    )
    readiness_rating: str = Field(
        ...,
        description="Categorical rating: READY, ACCEPTABLE, REQUIRES_ATTENTION, CRITICAL_GAPS",
    )
    total_documents: int = Field(..., description="Total document records in study")
    total_expected: int = Field(..., description="Total expected documents across EDL")
    approved_documents_count: int = Field(
        ..., description="Documents in APPROVED / ARCHIVED state"
    )
    pending_qc_count: int = Field(..., description="Documents in QC review")
    unsigned_documents_count: int = Field(
        ..., description="Documents requiring but missing e-signature"
    )
    expired_documents_count: int = Field(
        ..., description="Documents with past expiration date"
    )
    expiring_soon_count: int = Field(
        ..., description="Documents expiring within 30 days"
    )
    milestones: list[MilestoneReadinessMetric] = Field(
        default_factory=list, description="Milestone-level readiness"
    )
    zones: list[ZoneReadinessMetric] = Field(
        default_factory=list, description="Zone-by-zone matrix (1-11)"
    )
    action_items: list[str] = Field(
        default_factory=list, description="Recommended remediation actions"
    )


def calculate_study_inspection_readiness(
    study_id: str,
    documents: list[Any],
    expected_documents: list[Any],
) -> InspectionReadinessReport:
    """Computes a multi-dimensional inspection readiness score and report for a clinical study.

    Args:
        study_id: Clinical trial identifier.
        documents: List of TMFDocument entities or DTOs in the study.
        expected_documents: List of ExpectedDocument entities or DTOs configured for the study.

    Returns:
        InspectionReadinessReport containing weighted scores, zone breakdowns, and action items.
    """
    catalog = get_active_catalog()
    now_utc = datetime.now(UTC)

    # 1. Classify documents by status and properties
    latest_docs_by_artifact: dict[str, Any] = {}
    for doc in documents:
        art_code = getattr(doc, "artifact_code", "") or getattr(
            doc, "artifact_type", ""
        )
        if art_code not in latest_docs_by_artifact or getattr(
            doc, "version_index", 1
        ) > getattr(latest_docs_by_artifact[art_code], "version_index", 1):
            latest_docs_by_artifact[art_code] = doc

    total_docs = len(documents)
    approved_count = sum(
        1
        for d in documents
        if getattr(d, "status", "") in ("APPROVED", "ARCHIVED", "SIGNED")
    )
    pending_qc_count = sum(
        1
        for d in documents
        if getattr(d, "status", "") in ("TECHNICAL_QC", "CLINICAL_QC")
    )

    unsigned_count = 0
    expired_count = 0
    expiring_soon_count = 0

    for d in documents:
        # Check signature on critical types
        art_code = getattr(d, "artifact_code", "")
        approval_status = getattr(d, "approval_status", "")
        sig_manifest = getattr(d, "signature_manifestation", None)
        if (
            art_code in ("01.01.03", "05.02.01", "05.02.02")
            and approval_status != "APPROVED"
            and not sig_manifest
        ):
            unsigned_count += 1

        exp_dt = getattr(d, "expiration_date", None)
        if exp_dt:
            if not isinstance(exp_dt, datetime):
                exp_dt = datetime.combine(exp_dt, datetime.min.time()).replace(
                    tzinfo=UTC
                )
            elif exp_dt.tzinfo is None:
                exp_dt = exp_dt.replace(tzinfo=UTC)

            if exp_dt < now_utc:
                expired_count += 1
            elif (exp_dt - now_utc).days <= 30:
                expiring_soon_count += 1

    # 2. Zone-by-Zone metrics
    zone_expected_map = defaultdict(int)
    for ed in expected_documents:
        z = getattr(ed, "zone", None)
        if z:
            zone_expected_map[z] += 1

    zone_metrics: list[ZoneReadinessMetric] = []
    for z in catalog.zones:
        z_code = z.code
        z_name = z.name

        expected_in_z = zone_expected_map.get(z_code, 0)
        docs_in_z = [d for d in documents if getattr(d, "zone", None) == z_code]
        present_in_z = len(docs_in_z)
        approved_in_z = sum(
            1
            for d in docs_in_z
            if getattr(d, "status", "") in ("APPROVED", "ARCHIVED", "SIGNED")
        )
        pending_qc_in_z = sum(
            1
            for d in docs_in_z
            if getattr(d, "status", "") in ("TECHNICAL_QC", "CLINICAL_QC")
        )
        rejected_in_z = sum(
            1 for d in docs_in_z if getattr(d, "status", "") == "REJECTED"
        )

        missing_in_z = max(0, expected_in_z - approved_in_z)
        comp_pct = (
            round((approved_in_z / expected_in_z) * 100.0, 1)
            if expected_in_z > 0
            else (100.0 if present_in_z > 0 else 0.0)
        )
        comp_pct = min(100.0, comp_pct)

        zone_metrics.append(
            ZoneReadinessMetric(
                zone_code=z_code,
                zone_name=z_name,
                expected_count=expected_in_z,
                present_count=present_in_z,
                approved_count=approved_in_z,
                pending_qc_count=pending_qc_in_z,
                rejected_count=rejected_in_z,
                missing_count=missing_in_z,
                completeness_percentage=comp_pct,
            )
        )

    # 3. Milestone metrics
    milestones_map = defaultdict(list)
    for ed in expected_documents:
        ms = normalize_milestone(getattr(ed, "milestone", "INITIATION"))
        milestones_map[ms].append(ed)

    milestone_metrics: list[MilestoneReadinessMetric] = []
    for ms, expected_list in milestones_map.items():
        exp_count = len(expected_list)
        missing_names = []
        app_count = 0
        pres_count = 0

        for ed in expected_list:
            art_type = getattr(ed, "artifact_type", "")
            art_code = (
                getattr(ed, "metadata_json", {}).get("artifact_code")
                if getattr(ed, "metadata_json", None)
                else None
            )

            # Check if matching document exists
            matching = [
                d
                for d in documents
                if getattr(d, "artifact_type", "") == art_type
                or (art_code and getattr(d, "artifact_code", "") == art_code)
            ]
            if matching:
                pres_count += 1
                if any(
                    getattr(m, "status", "") in ("APPROVED", "ARCHIVED", "SIGNED")
                    for m in matching
                ):
                    app_count += 1
                else:
                    missing_names.append(f"{art_type} (Pending QC)")
            else:
                missing_names.append(art_type)

        is_comp = len(missing_names) == 0 and exp_count > 0
        m_pct = (
            round((app_count / exp_count) * 100.0, 1)
            if exp_count > 0
            else (100.0 if pres_count > 0 else 0.0)
        )

        milestone_metrics.append(
            MilestoneReadinessMetric(
                milestone=ms,
                is_complete=is_comp,
                expected_count=exp_count,
                present_count=pres_count,
                approved_count=app_count,
                missing_artifacts=missing_names,
                completeness_percentage=m_pct,
            )
        )

    # 4. Compute Weighted Inspection Readiness Score (0 - 100)
    # Milestone Completeness Weight: 40%
    if milestone_metrics:
        avg_milestone_pct = sum(
            m.completeness_percentage for m in milestone_metrics
        ) / len(milestone_metrics)
    else:
        avg_milestone_pct = 100.0 if total_docs > 0 else 0.0

    # Zone Completeness Weight: 30%
    active_zones = [
        z for z in zone_metrics if z.expected_count > 0 or z.present_count > 0
    ]
    if active_zones:
        avg_zone_pct = sum(z.completeness_percentage for z in active_zones) / len(
            active_zones
        )
    else:
        avg_zone_pct = 100.0 if total_docs > 0 else 0.0

    # QC Health Weight: 15%
    qc_health_pct = (approved_count / total_docs) * 100.0 if total_docs > 0 else 100.0

    # Signature Compliance Weight: 10%
    sig_health_pct = max(0.0, 100.0 - (unsigned_count * 10.0))

    # Expiration Risk Penalty: 5%
    exp_health_pct = max(
        0.0, 100.0 - (expired_count * 20.0 + expiring_soon_count * 5.0)
    )

    weighted_score = (
        (avg_milestone_pct * 0.40)
        + (avg_zone_pct * 0.30)
        + (qc_health_pct * 0.15)
        + (sig_health_pct * 0.10)
        + (exp_health_pct * 0.05)
    )
    weighted_score = round(max(0.0, min(100.0, weighted_score)), 1)

    # Categorical Rating
    if weighted_score >= 90.0:
        rating = "READY"
    elif weighted_score >= 75.0:
        rating = "ACCEPTABLE"
    elif weighted_score >= 50.0:
        rating = "REQUIRES_ATTENTION"
    else:
        rating = "CRITICAL_GAPS"

    # 5. Remediation Action Items
    action_items = []
    if expired_count > 0:
        action_items.append(
            f"URGENT: {expired_count} document(s) have expired. Immediate re-qualification/renewal required."
        )
    if expiring_soon_count > 0:
        action_items.append(
            f"WARNING: {expiring_soon_count} document(s) expire within 30 days."
        )
    if pending_qc_count > 0:
        action_items.append(
            f"ACTION: {pending_qc_count} document(s) are awaiting QC approval."
        )
    if unsigned_count > 0:
        action_items.append(
            f"ACTION: {unsigned_count} critical artifact(s) require 21 CFR Part 11 e-signature sign-off."
        )
    for m in milestone_metrics:
        if not m.is_complete and m.missing_artifacts:
            action_items.append(
                f"MILESTONE '{m.milestone}': {len(m.missing_artifacts)} missing or unapproved item(s): {', '.join(m.missing_artifacts[:3])}{'...' if len(m.missing_artifacts) > 3 else ''}"
            )

    return InspectionReadinessReport(
        study_id=study_id,
        generated_at=now_utc.isoformat(),
        overall_readiness_score=weighted_score,
        readiness_rating=rating,
        total_documents=total_docs,
        total_expected=len(expected_documents),
        approved_documents_count=approved_count,
        pending_qc_count=pending_qc_count,
        unsigned_documents_count=unsigned_count,
        expired_documents_count=expired_count,
        expiring_soon_count=expiring_soon_count,
        milestones=milestone_metrics,
        zones=zone_metrics,
        action_items=action_items,
    )
