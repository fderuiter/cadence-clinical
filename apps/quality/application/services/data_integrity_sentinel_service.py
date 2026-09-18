"""Cross-Site Forensic Data Integrity Sentinels & Surveillance Engine.

Requirements:
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- PRD-CSM-003: Calendar & Timestamp Pattern Clustering (Weekend/Holiday Entry Inconsistencies)
- PRD-CSM-004: Duplicate Patient Record & Anatomical Fabrication Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ICH E6(R2)/(R3) Risk-Based Quality Surveillance
"""

import base64
import hashlib
import io
import math
import random
from collections.abc import Sequence
from datetime import datetime
from typing import Any

from apps.quality.domain.models import (
    DataIntegrityFindingStatus,
    DataIntegritySentinelType,
    DeviationSeverity,
    DeviationType,
    ForensicsAnomalyFlag,
    ImageTransformationType,
    RiskTier,
    SimilarityMatchTier,
)
from apps.quality.domain.ports import QualityRepositoryPort
from packages.hexagonal import DomainError


class DataIntegritySentinelError(DomainError):
    def __init__(self, message: str, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


# ---------------------------------------------------------------------------
# Sentinel 1: Duplicate Patient Record Sentinel
# ---------------------------------------------------------------------------


class DuplicatePatientRecordEngine:
    """
    Cross-site identity & clinical feature vector similarity matching engine (PRD-CSM-004).
    Detects duplicate enrollments, identity fabrication, and cloned baseline records.
    """

    # Feature standard deviations / normal ranges for clinical distance normalization
    SCALE_FACTORS: dict[str, float] = {
        # Demographics
        "age": 15.0,
        "height_cm": 12.0,
        "weight_kg": 18.0,
        "bmi": 5.0,
        # Vitals
        "systolic_bp": 15.0,
        "diastolic_bp": 10.0,
        "heart_rate": 12.0,
        "respiratory_rate": 3.0,
        "temperature_c": 0.6,
        # ECG Intervals (Highly discriminating biometric fingerprint)
        "pr_interval_ms": 20.0,
        "qrs_duration_ms": 12.0,
        "qt_interval_ms": 25.0,
        "qtc_ms": 20.0,
        "rr_interval_ms": 120.0,
        "heart_axis_deg": 25.0,
        # Laboratory Panels
        "creatinine": 0.35,
        "alt": 18.0,
        "ast": 15.0,
        "fasting_glucose": 25.0,
        "total_bilirubin": 0.4,
        "hemoglobin": 2.0,
        "platelets": 50.0,
        "wbc": 2.5,
    }

    @classmethod
    def compute_continuous_vector_similarity(
        cls, v1: dict[str, Any], v2: dict[str, Any]
    ) -> float:
        """Calculates normalized Minkowski/Manhattan similarity across continuous clinical variables."""
        common_keys = [
            k for k in v1 if k in v2 and v1[k] is not None and v2[k] is not None
        ]
        if not common_keys:
            return 1.0

        diff_sum = 0.0
        valid_count = 0
        for k in common_keys:
            try:
                val1 = float(v1[k])
                val2 = float(v2[k])
            except (ValueError, TypeError):
                continue
            scale = cls.SCALE_FACTORS.get(k, 10.0)
            norm_diff = min(1.0, abs(val1 - val2) / scale)
            diff_sum += norm_diff
            valid_count += 1

        if valid_count == 0:
            return 1.0
        avg_diff = diff_sum / valid_count
        return max(0.0, min(1.0, 1.0 - avg_diff))

    @classmethod
    def compute_categorical_similarity(
        cls, d1: dict[str, Any], d2: dict[str, Any], keys: list[str]
    ) -> float:
        """Calculates exact match ratio across categorical demographic/clinical attributes."""
        evaluated = 0
        matches = 0
        for k in keys:
            if k in d1 and k in d2 and d1[k] is not None and d2[k] is not None:
                evaluated += 1
                if str(d1[k]).strip().upper() == str(d2[k]).strip().upper():
                    matches += 1
        if evaluated == 0:
            return 1.0
        return matches / evaluated

    @classmethod
    def compute_jaccard_similarity(cls, s1: list[str], s2: list[str]) -> float:
        """Calculates Jaccard similarity index on sets of medical history / conmed codes."""
        set1 = {str(x).strip().upper() for x in (s1 or []) if x}
        set2 = {str(x).strip().upper() for x in (s2 or []) if x}
        if not set1 and not set2:
            return 1.0
        if not set1 or not set2:
            return 0.0
        intersection = len(set1.intersection(set2))
        union = len(set1.union(set2))
        return intersection / union if union > 0 else 0.0

    @classmethod
    def compute_subject_fingerprint(cls, subject_data: dict[str, Any]) -> str:
        """Computes deterministic SHA-256 fingerprint from canonical normalized baseline features."""
        demo = subject_data.get("demographics", {})
        vitals = subject_data.get("vital_signs", {})
        ecg = subject_data.get("ecg_intervals", {})
        labs = subject_data.get("lab_panel", {})
        med_hist = sorted(
            [str(x).upper() for x in subject_data.get("medical_history", [])]
        )

        canonical_tokens = [
            f"AGE:{demo.get('age', '')}",
            f"SEX:{str(demo.get('sex', '')).upper()}",
            f"RACE:{str(demo.get('race', '')).upper()}",
            f"HT:{demo.get('height_cm', '')}",
            f"WT:{demo.get('weight_kg', '')}",
            f"SBP:{vitals.get('systolic_bp', '')}",
            f"DBP:{vitals.get('diastolic_bp', '')}",
            f"HR:{vitals.get('heart_rate', '')}",
            f"PR:{ecg.get('pr_interval_ms', '')}",
            f"QRS:{ecg.get('qrs_duration_ms', '')}",
            f"QT:{ecg.get('qt_interval_ms', '')}",
            f"QTC:{ecg.get('qtc_ms', '')}",
            f"CR:{labs.get('creatinine', '')}",
            f"ALT:{labs.get('alt', '')}",
            f"MH:{','.join(med_hist)}",
        ]
        payload = "|".join(canonical_tokens)
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()

    @classmethod
    def compare_subject_pair(
        cls, s1: dict[str, Any], s2: dict[str, Any]
    ) -> dict[str, Any]:
        """Calculates multi-dimensional composite similarity score between two subject profiles."""
        # 1. Exact canonical hash match check
        fp1 = cls.compute_subject_fingerprint(s1)
        fp2 = cls.compute_subject_fingerprint(s2)
        if fp1 == fp2:
            return {
                "composite_similarity": 1.0,
                "confidence_score": 1.0,
                "tier": SimilarityMatchTier.EXACT,
                "is_match": True,
                "sub_scores": {
                    "demographics": 1.0,
                    "vital_signs": 1.0,
                    "ecg_intervals": 1.0,
                    "lab_panel": 1.0,
                    "medical_history": 1.0,
                },
                "fingerprint_matched": True,
            }

        # 2. Sub-component similarity evaluations
        demo1 = s1.get("demographics", {})
        demo2 = s2.get("demographics", {})
        cat_sim = cls.compute_categorical_similarity(
            demo1, demo2, ["sex", "race", "ethnicity", "blood_type"]
        )
        demo_cont_sim = cls.compute_continuous_vector_similarity(demo1, demo2)
        demo_score = 0.5 * cat_sim + 0.5 * demo_cont_sim

        vitals_score = cls.compute_continuous_vector_similarity(
            s1.get("vital_signs", {}), s2.get("vital_signs", {})
        )

        ecg_score = cls.compute_continuous_vector_similarity(
            s1.get("ecg_intervals", {}), s2.get("ecg_intervals", {})
        )

        labs_score = cls.compute_continuous_vector_similarity(
            s1.get("lab_panel", {}), s2.get("lab_panel", {})
        )

        mh_score = cls.compute_jaccard_similarity(
            s1.get("medical_history", []), s2.get("medical_history", [])
        )

        # Weighted composite score
        # Weights: Demographics (0.25), ECG Intervals (0.25), Labs (0.20), Vitals (0.15), Medical History (0.15)
        composite = (
            0.25 * demo_score
            + 0.25 * ecg_score
            + 0.20 * labs_score
            + 0.15 * vitals_score
            + 0.15 * mh_score
        )
        composite = round(max(0.0, min(1.0, composite)), 4)

        if composite >= 0.98:
            tier = SimilarityMatchTier.EXACT
            is_match = True
        elif composite >= 0.88:
            tier = SimilarityMatchTier.HIGH_CONFIDENCE
            is_match = True
        elif composite >= 0.78:
            tier = SimilarityMatchTier.SUSPICIOUS
            is_match = True
        else:
            tier = SimilarityMatchTier.NOMINAL
            is_match = False

        return {
            "composite_similarity": composite,
            "confidence_score": composite,
            "tier": tier,
            "is_match": is_match,
            "sub_scores": {
                "demographics": round(demo_score, 4),
                "vital_signs": round(vitals_score, 4),
                "ecg_intervals": round(ecg_score, 4),
                "lab_panel": round(labs_score, 4),
                "medical_history": round(mh_score, 4),
            },
            "fingerprint_matched": False,
        }

    @classmethod
    def scan_all_subjects(
        cls,
        study_id: str,
        subjects: list[dict[str, Any]],
        similarity_threshold: float = 0.82,
    ) -> list[dict[str, Any]]:
        """Executes full $O(N^2)$ cross-site duplicate patient pairwise scan."""
        n = len(subjects)
        findings = []

        for i in range(n):
            for j in range(i + 1, n):
                s1 = subjects[i]
                s2 = subjects[j]
                res = cls.compare_subject_pair(s1, s2)

                if res["composite_similarity"] >= similarity_threshold:
                    s1_id = s1.get("subject_id", f"SUBJ-{i}")
                    s2_id = s2.get("subject_id", f"SUBJ-{j}")
                    s1_site = s1.get("site_id", "SITE-UNKNOWN")
                    s2_site = s2.get("site_id", "SITE-UNKNOWN")
                    is_cross_site = s1_site != s2_site

                    flag = (
                        ForensicsAnomalyFlag.CROSS_SITE_CLONING_SUSPICION
                        if is_cross_site and res["composite_similarity"] >= 0.88
                        else ForensicsAnomalyFlag.DUPLICATE_PATIENT_RECORD
                    )

                    severity = (
                        DeviationSeverity.CRITICAL
                        if res["composite_similarity"] >= 0.90 or is_cross_site
                        else DeviationSeverity.MAJOR
                    )

                    findings.append(
                        {
                            "study_id": study_id,
                            "sentinel_type": DataIntegritySentinelType.DUPLICATE_PATIENT_RECORD,
                            "severity": severity,
                            "anomaly_flag": flag,
                            "primary_subject_id": s1_id,
                            "primary_site_id": s1_site,
                            "matched_subject_id": s2_id,
                            "matched_site_id": s2_site,
                            "is_cross_site": is_cross_site,
                            "confidence_score": res["confidence_score"],
                            "similarity_score": res["composite_similarity"],
                            "tier": res["tier"],
                            "evidence_summary": (
                                f"{'Cross-Site' if is_cross_site else 'Intra-Site'} duplicate clinical vector match "
                                f"between Subject '{s1_id}' ({s1_site}) and Subject '{s2_id}' ({s2_site}) "
                                f"with {res['composite_similarity'] * 100:.1f}% composite similarity "
                                f"(ECG: {res['sub_scores']['ecg_intervals'] * 100:.1f}%, Labs: {res['sub_scores']['lab_panel'] * 100:.1f}%)."
                            ),
                            "forensic_metadata": {
                                "sub_scores": res["sub_scores"],
                                "fingerprint_matched": res["fingerprint_matched"],
                                "primary_subject_site": s1_site,
                                "matched_subject_site": s2_site,
                            },
                        }
                    )

        # Sort findings descending by similarity
        findings.sort(key=lambda x: x["similarity_score"], reverse=True)
        return findings


# ---------------------------------------------------------------------------
# Sentinel 2: Temporal & Behavioral Anomaly Scanner
# ---------------------------------------------------------------------------


class TemporalBehavioralAnomalyEngine:
    """
    Surveillance engine for data entry timing anomalies (PRD-CSM-003).
    Detects off-hours/weekend clustering, superhuman burst submissions, and identical procedure intervals.
    """

    @classmethod
    def parse_datetime(cls, dt_val: Any) -> datetime | None:
        """Safely parses timestamp string or datetime object."""
        if isinstance(dt_val, datetime):
            return dt_val
        if isinstance(dt_val, str):
            try:
                return datetime.fromisoformat(dt_val.replace("Z", "+00:00"))
            except Exception:
                try:
                    return datetime.strptime(dt_val, "%Y-%m-%d %H:%M:%S")
                except Exception:
                    return None
        return None

    @classmethod
    def scan_temporal_anomalies(
        cls,
        study_id: str,
        audit_events: list[dict[str, Any]],
        work_hour_start: int = 7,
        work_hour_end: int = 19,
    ) -> dict[str, Any]:
        """
        Evaluates data entry audit events across sites for:
        1. Non-working hours & weekend entry clusters
        2. Rapid burst submissions (< 10 seconds)
        3. Identical inter-procedure elapsed intervals
        """
        if not audit_events:
            return {
                "total_events": 0,
                "anomalies_detected": 0,
                "findings": [],
                "site_metrics": {},
            }

        # Group events by site
        events_by_site: dict[str, list[dict[str, Any]]] = {}
        for ev in audit_events:
            site_id = ev.get("site_id", "SITE-DEFAULT")
            events_by_site.setdefault(site_id, []).append(ev)

        findings: list[dict[str, Any]] = []
        site_metrics: dict[str, Any] = {}

        for site_id, site_evs in events_by_site.items():
            valid_evs = []
            for ev in site_evs:
                dt = cls.parse_datetime(ev.get("timestamp"))
                if dt:
                    valid_evs.append({**ev, "_dt": dt})

            if not valid_evs:
                continue

            # Sort chronologically
            valid_evs.sort(key=lambda x: x["_dt"])
            total_n = len(valid_evs)

            # 1. Non-working hours & weekend metrics
            off_hours_count = 0
            weekend_count = 0
            deep_night_count = 0  # 00:00 - 05:00

            for ev in valid_evs:
                dt = ev["_dt"]
                is_weekend = dt.weekday() in (5, 6)
                is_off_hour = dt.hour < work_hour_start or dt.hour >= work_hour_end
                is_deep_night = dt.hour in (0, 1, 2, 3, 4)

                if is_weekend:
                    weekend_count += 1
                if is_off_hour:
                    off_hours_count += 1
                if is_deep_night:
                    deep_night_count += 1

            off_hours_ratio = off_hours_count / total_n
            weekend_ratio = weekend_count / total_n
            deep_night_ratio = deep_night_count / total_n

            # Flag: Suspicious off-hours clustering
            if total_n >= 10 and (
                off_hours_ratio >= 0.35
                or weekend_ratio >= 0.30
                or deep_night_count >= 4
            ):
                flag = ForensicsAnomalyFlag.SUSPICIOUS_TIMESTAMP_CLUSTER
                findings.append(
                    {
                        "study_id": study_id,
                        "sentinel_type": DataIntegritySentinelType.TEMPORAL_BEHAVIORAL_ANOMALY,
                        "severity": DeviationSeverity.MAJOR
                        if off_hours_ratio < 0.60
                        else DeviationSeverity.CRITICAL,
                        "anomaly_flag": flag,
                        "primary_subject_id": f"SITE-CLUSTER-{site_id}",
                        "primary_site_id": site_id,
                        "matched_subject_id": None,
                        "matched_site_id": None,
                        "confidence_score": min(
                            0.99, round(max(off_hours_ratio, weekend_ratio), 2)
                        ),
                        "similarity_score": round(off_hours_ratio, 3),
                        "evidence_summary": f"Site '{site_id}' exhibits {off_hours_ratio * 100:.1f}% off-hours entries, {weekend_count} weekend submissions, and {deep_night_count} midnight entries.",
                        "forensic_metadata": {
                            "off_hours_count": off_hours_count,
                            "weekend_count": weekend_count,
                            "deep_night_count": deep_night_count,
                            "total_entries": total_n,
                            "off_hours_ratio": round(off_hours_ratio, 3),
                            "weekend_ratio": round(weekend_ratio, 3),
                            "deep_night_ratio": round(deep_night_ratio, 3),
                        },
                    }
                )

            # 2. Rapid burst submission detection (< 10 seconds between sequential entries)
            burst_clusters = 0
            rapid_intervals = []
            for k in range(1, total_n):
                delta_sec = (
                    valid_evs[k]["_dt"] - valid_evs[k - 1]["_dt"]
                ).total_seconds()
                if 0.0 <= delta_sec < 10.0:
                    rapid_intervals.append(delta_sec)
                    burst_clusters += 1

            burst_ratio = burst_clusters / max(1, total_n - 1)
            if total_n >= 8 and (burst_clusters >= 4 or burst_ratio >= 0.20):
                findings.append(
                    {
                        "study_id": study_id,
                        "sentinel_type": DataIntegritySentinelType.TEMPORAL_BEHAVIORAL_ANOMALY,
                        "severity": DeviationSeverity.CRITICAL
                        if burst_ratio >= 0.40
                        else DeviationSeverity.MAJOR,
                        "anomaly_flag": ForensicsAnomalyFlag.RAPID_BURST_SUBMISSION,
                        "primary_subject_id": valid_evs[0].get(
                            "subject_id", "MULTIPLE"
                        ),
                        "primary_site_id": site_id,
                        "matched_subject_id": None,
                        "matched_site_id": None,
                        "confidence_score": round(min(1.0, burst_ratio * 2.0), 2),
                        "similarity_score": round(burst_ratio, 4),
                        "evidence_summary": (
                            f"Site '{site_id}' triggered rapid burst submission sentinel: "
                            f"{burst_clusters} entries submitted in rapid succession (< 10s delta-t, "
                            f"min delta: {min(rapid_intervals):.1f}s), indicating scripted or automated batch entry."
                        ),
                        "forensic_metadata": {
                            "burst_clusters_count": burst_clusters,
                            "burst_ratio": round(burst_ratio, 4),
                            "min_delta_seconds": round(min(rapid_intervals), 2)
                            if rapid_intervals
                            else 0.0,
                        },
                    }
                )

            # 3. Identical Inter-Procedure Elapsed Intervals
            # Detects identical elapsed times between procedures across distinct subjects (e.g. exactly 870.0 seconds)
            intervals_by_pair: dict[str, dict[str, list[float]]] = {}
            for ev in valid_evs:
                subj = ev.get("subject_id")
                proc_pair = ev.get("procedure_pair") or ev.get(
                    "assessment_type", "GENERAL_VISIT"
                )
                elapsed = ev.get("elapsed_time_seconds")
                if subj and elapsed is not None:
                    intervals_by_pair.setdefault(proc_pair, {}).setdefault(
                        subj, []
                    ).append(float(elapsed))

            for proc_pair, subj_map in intervals_by_pair.items():
                if len(subj_map) >= 3:
                    # Collect first elapsed time per subject
                    elapsed_vals = [times[0] for times in subj_map.values() if times]
                    if len(elapsed_vals) >= 3:
                        # Check frequency of identical values (within 1 second)
                        counts: dict[float, int] = {}
                        for val in elapsed_vals:
                            rounded_val = round(val, 0)
                            counts[rounded_val] = counts.get(rounded_val, 0) + 1

                        for val_sec, count in counts.items():
                            if count >= 3:
                                findings.append(
                                    {
                                        "study_id": study_id,
                                        "sentinel_type": DataIntegritySentinelType.TEMPORAL_BEHAVIORAL_ANOMALY,
                                        "severity": DeviationSeverity.CRITICAL,
                                        "anomaly_flag": ForensicsAnomalyFlag.IDENTICAL_PROCEDURE_INTERVALS,
                                        "primary_subject_id": list(subj_map.keys())[0],
                                        "primary_site_id": site_id,
                                        "matched_subject_id": list(subj_map.keys())[1],
                                        "matched_site_id": site_id,
                                        "confidence_score": 0.98,
                                        "similarity_score": 1.0,
                                        "evidence_summary": (
                                            f"Site '{site_id}' recorded identical procedure elapsed interval of "
                                            f"{val_sec:.0f} seconds across {count} different subjects for '{proc_pair}', "
                                            f"indicating fabricated synthetic timestamp recording."
                                        ),
                                        "forensic_metadata": {
                                            "procedure_pair": proc_pair,
                                            "identical_elapsed_seconds": val_sec,
                                            "affected_subjects_count": count,
                                            "affected_subjects": list(subj_map.keys())[
                                                :count
                                            ],
                                        },
                                    }
                                )

            site_metrics[site_id] = {
                "total_events": total_n,
                "off_hours_ratio": round(off_hours_ratio, 4),
                "weekend_ratio": round(weekend_ratio, 4),
                "deep_night_count": deep_night_count,
                "burst_ratio": round(burst_ratio, 4),
                "anomalies_count": len(
                    [f for f in findings if f["primary_site_id"] == site_id]
                ),
            }

        return {
            "total_events": len(audit_events),
            "anomalies_detected": len(findings),
            "findings": findings,
            "site_metrics": site_metrics,
        }


# ---------------------------------------------------------------------------
# Sentinel 3: Perceptual Image Hashing (pHash / dHash) Sentinel
# ---------------------------------------------------------------------------


class PerceptualImageHashingEngine:
    """
    DCT-based Perceptual Image Hash (pHash) and Difference Hash (dHash) Engine (PRD-CSM-004).
    Provides robust detection of reused, rotated, cropped, and brightness-adjusted radiological images.
    """

    # Precomputed 32x32 1D DCT Basis Matrix for zero-dependency matrix multiplication
    @staticmethod
    def _create_dct_basis(n: int = 32) -> list[list[float]]:
        basis = []
        for u in range(n):
            row = []
            alpha = 1.0 / math.sqrt(n) if u == 0 else math.sqrt(2.0 / n)
            for x in range(n):
                val = alpha * math.cos(((2.0 * x + 1.0) * u * math.pi) / (2.0 * n))
                row.append(val)
            basis.append(row)
        return basis

    _DCT_BASIS_32: list[list[float]] = []

    @classmethod
    def _get_dct_basis(cls) -> list[list[float]]:
        if not cls._DCT_BASIS_32:
            cls._DCT_BASIS_32 = cls._create_dct_basis(32)
        return cls._DCT_BASIS_32

    @classmethod
    def resize_and_grayscale_matrix(
        cls,
        matrix_or_bytes: list[list[float]] | bytes | str,
        target_w: int = 32,
        target_h: int = 32,
    ) -> list[list[float]]:
        """
        Normalizes any input (2D numeric grid, byte string, base64 payload)
        into a target_h x target_w float matrix in range [0.0, 255.0].
        """
        if isinstance(matrix_or_bytes, str):
            # Check if base64
            try:
                raw_b = base64.b64decode(matrix_or_bytes)
                return cls.resize_and_grayscale_matrix(raw_b, target_w, target_h)
            except Exception:
                # Interpret as comma/newline separated matrix string
                rows = [
                    [float(x.strip()) for x in line.split(",") if x.strip()]
                    for line in matrix_or_bytes.strip().split("\n")
                    if line.strip()
                ]
                if rows:
                    return cls.resize_and_grayscale_matrix(rows, target_w, target_h)

        if isinstance(matrix_or_bytes, bytes):
            # Attempt PIL image reading if available
            try:
                from PIL import Image

                img = Image.open(io.BytesIO(matrix_or_bytes)).convert("L")
                img = img.resize((target_w, target_h), Image.Resampling.BILINEAR)
                pixels = list(img.getdata())
                return [
                    [float(pixels[r * target_w + c]) for c in range(target_w)]
                    for r in range(target_h)
                ]
            except Exception:
                # Pure-Python fallback for byte buffer (e.g. grayscale byte stream)
                b_len = len(matrix_or_bytes)
                side = int(math.isqrt(b_len))
                if side >= 4:
                    raw_grid = [
                        [float(matrix_or_bytes[r * side + c]) for c in range(side)]
                        for r in range(side)
                    ]
                    return cls.resize_and_grayscale_matrix(raw_grid, target_w, target_h)
                # Fallback: hash bytes deterministically to seed synthetic pixel grid
                seed_val = int(hashlib.sha256(matrix_or_bytes).hexdigest()[:8], 16)
                rng = random.Random(seed_val)
                return [
                    [float(rng.randint(0, 255)) for _ in range(target_w)]
                    for _ in range(target_h)
                ]

        if isinstance(matrix_or_bytes, list) and matrix_or_bytes:
            src_h = len(matrix_or_bytes)
            src_w = len(matrix_or_bytes[0]) if src_h > 0 else 0
            if src_w == 0 or src_h == 0:
                return [[0.0] * target_w for _ in range(target_h)]

            # Bilinear interpolation
            out = []
            for r in range(target_h):
                row = []
                src_y = (r / max(1, target_h - 1)) * (src_h - 1)
                y0 = int(src_y)
                y1 = min(src_h - 1, y0 + 1)
                dy = src_y - y0

                for c in range(target_w):
                    src_x = (c / max(1, target_w - 1)) * (src_w - 1)
                    x0 = int(src_x)
                    x1 = min(src_w - 1, x0 + 1)
                    dx = src_x - x0

                    p00 = float(matrix_or_bytes[y0][x0])
                    p01 = float(matrix_or_bytes[y0][x1])
                    p10 = float(matrix_or_bytes[y1][x0])
                    p11 = float(matrix_or_bytes[y1][x1])

                    top = (1.0 - dx) * p00 + dx * p01
                    bot = (1.0 - dx) * p10 + dx * p11
                    val = (1.0 - dy) * top + dy * bot
                    row.append(max(0.0, min(255.0, val)))
                out.append(row)
            return out

        return [[0.0] * target_w for _ in range(target_h)]

    @classmethod
    def compute_dct_2d(cls, grid_32: list[list[float]]) -> list[list[float]]:
        """Computes 2D Discrete Cosine Transform via orthonormal basis multiplication: D = M * I * M^T."""
        m = cls._get_dct_basis()
        n = 32

        # Step 1: Temp = M * I
        temp = [[0.0] * n for _ in range(n)]
        for i in range(n):
            for j in range(n):
                s = 0.0
                for k in range(n):
                    s += m[i][k] * grid_32[k][j]
                temp[i][j] = s

        # Step 2: D = Temp * M^T
        dct_out = [[0.0] * n for _ in range(n)]
        for i in range(n):
            for j in range(n):
                s = 0.0
                for k in range(n):
                    s += temp[i][k] * m[j][k]
                dct_out[i][j] = s

        return dct_out

    @classmethod
    def compute_phash(cls, image_data: Any) -> str:
        """
        Computes 64-bit DCT-based perceptual image hash (pHash) formatted as 16-char hexadecimal string.
        """
        grid_32 = cls.resize_and_grayscale_matrix(image_data, 32, 32)
        dct = cls.compute_dct_2d(grid_32)

        # Extract top-left 8x8 low frequency coefficients
        low_freq = []
        for r in range(8):
            for c in range(8):
                if r == 0 and c == 0:
                    continue  # Exclude DC component from median
                low_freq.append(dct[r][c])

        # Compute median
        sorted_coeffs = sorted(low_freq)
        med_idx = len(sorted_coeffs) // 2
        median_val = sorted_coeffs[med_idx]

        # Generate 64 bits (include DC in bit generation based on median)
        bit_str = ""
        for r in range(8):
            for c in range(8):
                bit_str += "1" if dct[r][c] > median_val else "0"

        # Convert 64 bits to 16 hex characters
        return f"{int(bit_str, 2):016x}"

    @classmethod
    def compute_dhash(cls, image_data: Any) -> str:
        """
        Computes 64-bit Difference Hash (dHash) from 9x8 horizontal gradient comparison.
        """
        grid_9x8 = cls.resize_and_grayscale_matrix(image_data, 9, 8)
        bit_str = ""
        for r in range(8):
            for c in range(8):
                bit_str += "1" if grid_9x8[r][c + 1] > grid_9x8[r][c] else "0"

        return f"{int(bit_str, 2):016x}"

    @classmethod
    def compute_hamming_distance(cls, hex1: str, hex2: str) -> int:
        """Calculates Hamming distance (count of differing bits) between two 64-bit hexadecimal hashes."""
        try:
            val1 = int(hex1, 16)
            val2 = int(hex2, 16)
            xor_val = val1 ^ val2
            return bin(xor_val).count("1")
        except Exception:
            return 64

    @classmethod
    def generate_transformations(
        cls, grid: list[list[float]]
    ) -> dict[ImageTransformationType, list[list[float]]]:
        """Generates transformed matrices for rotation (90, 180, 270) and flips."""
        h = len(grid)
        w = len(grid[0]) if h > 0 else 0

        # Rotate 90 CW: (r, c) -> (c, h - 1 - r)
        rot_90 = [[grid[h - 1 - r][c] for r in range(h)] for c in range(w)]

        # Rotate 180: (r, c) -> (h - 1 - r, w - 1 - c)
        rot_180 = [[grid[h - 1 - r][w - 1 - c] for c in range(w)] for r in range(h)]

        # Rotate 270 CW: (r, c) -> (w - 1 - c, r)
        rot_270 = [[grid[r][w - 1 - c] for r in range(h)] for c in range(w)]

        # Flip Horizontal: (r, c) -> (r, w - 1 - c)
        flip_h = [[grid[r][w - 1 - c] for c in range(w)] for r in range(h)]

        # Flip Vertical: (r, c) -> (h - 1 - r, c)
        flip_v = [[grid[h - 1 - r][c] for c in range(w)] for r in range(h)]

        return {
            ImageTransformationType.EXACT: grid,
            ImageTransformationType.ROTATED_90: rot_90,
            ImageTransformationType.ROTATED_180: rot_180,
            ImageTransformationType.ROTATED_270: rot_270,
            ImageTransformationType.FLIPPED_HORIZONTAL: flip_h,
            ImageTransformationType.FLIPPED_VERTICAL: flip_v,
        }

    @classmethod
    def compare_images(cls, img1_data: Any, img2_data: Any) -> dict[str, Any]:
        """
        Compares two images across all geometric transformations and computes minimum Hamming distance.
        """
        grid1 = cls.resize_and_grayscale_matrix(img1_data, 32, 32)
        grid2 = cls.resize_and_grayscale_matrix(img2_data, 32, 32)

        phash1 = cls.compute_phash(grid1)
        dhash1 = cls.compute_dhash(grid1)

        # Generate transformations for Image 2
        transforms2 = cls.generate_transformations(grid2)

        best_hamming = 64
        best_transform = ImageTransformationType.EXACT
        best_phash2 = phash1

        for trans_type, grid2_trans in transforms2.items():
            phash2_trans = cls.compute_phash(grid2_trans)
            dist = cls.compute_hamming_distance(phash1, phash2_trans)
            if dist < best_hamming:
                best_hamming = dist
                best_transform = trans_type
                best_phash2 = phash2_trans

        # Similarity score S = 1.0 - (Hamming / 64.0)
        similarity = max(0.0, min(1.0, 1.0 - (best_hamming / 64.0)))

        # Classification
        if best_hamming == 0:
            tier = SimilarityMatchTier.EXACT
            is_match = True
        elif best_hamming <= 5:
            tier = SimilarityMatchTier.HIGH_CONFIDENCE
            is_match = True
        elif best_hamming <= 10:
            tier = SimilarityMatchTier.SUSPICIOUS
            is_match = True
        else:
            tier = SimilarityMatchTier.NOMINAL
            is_match = False

        return {
            "phash_1": phash1,
            "dhash_1": dhash1,
            "phash_2": best_phash2,
            "hamming_distance": best_hamming,
            "similarity_score": round(similarity, 4),
            "transformation_type": best_transform,
            "tier": tier,
            "is_match": is_match,
        }

    @classmethod
    def scan_all_images(
        cls,
        study_id: str,
        image_attachments: list[dict[str, Any]],
        hamming_threshold: int = 10,
    ) -> list[dict[str, Any]]:
        """
        Performs cross-subject perceptual hash duplication scan across all medical images.
        """
        n = len(image_attachments)
        findings = []

        # Precompute hashes for speed
        prepared_images = []
        for img in image_attachments:
            data = (
                img.get("image_data")
                or img.get("pixel_matrix")
                or img.get("base64_data")
            )
            phash = img.get("phash")
            if not phash and data is not None:
                phash = cls.compute_phash(data)
            prepared_images.append({**img, "_phash": phash, "_data": data})

        for i in range(n):
            for j in range(i + 1, n):
                img1 = prepared_images[i]
                img2 = prepared_images[j]

                # Only match across different subjects (or different visits/scans)
                s1 = img1.get("subject_id")
                s2 = img2.get("subject_id")
                if s1 == s2 and img1.get("image_id") == img2.get("image_id"):
                    continue

                res = cls.compare_images(
                    img1.get("_data") or img1.get("_phash"),
                    img2.get("_data") or img2.get("_phash"),
                )

                if res["hamming_distance"] <= hamming_threshold:
                    img1_id = img1.get("image_id", f"IMG-{i}")
                    img2_id = img2.get("image_id", f"IMG-{j}")
                    s1_id = img1.get("subject_id", f"SUBJ-{i}")
                    s2_id = img2.get("subject_id", f"SUBJ-{j}")
                    s1_site = img1.get("site_id", "SITE-UNKNOWN")
                    s2_site = img2.get("site_id", "SITE-UNKNOWN")
                    modality = img1.get("modality", "RADIOLOGY")

                    severity = (
                        DeviationSeverity.CRITICAL
                        if res["hamming_distance"] <= 5 or s1_site != s2_site
                        else DeviationSeverity.MAJOR
                    )

                    findings.append(
                        {
                            "study_id": study_id,
                            "sentinel_type": DataIntegritySentinelType.PERCEPTUAL_IMAGE_HASH,
                            "severity": severity,
                            "anomaly_flag": ForensicsAnomalyFlag.PERCEPTUAL_IMAGE_HASH_MATCH,
                            "primary_subject_id": s1_id,
                            "primary_site_id": s1_site,
                            "matched_subject_id": s2_id,
                            "matched_site_id": s2_site,
                            "confidence_score": round(res["similarity_score"], 2),
                            "similarity_score": res["similarity_score"],
                            "hamming_distance": res["hamming_distance"],
                            "transformation_type": res["transformation_type"],
                            "evidence_summary": (
                                f"Medical image duplication detected ({modality}): Image '{img1_id}' "
                                f"for Subject '{s1_id}' ({s1_site}) matches Image '{img2_id}' "
                                f"for Subject '{s2_id}' ({s2_site}) with Hamming distance {res['hamming_distance']} "
                                f"({res['similarity_score'] * 100:.1f}% similarity, transformation: {res['transformation_type']})."
                            ),
                            "forensic_metadata": {
                                "primary_image_id": img1_id,
                                "matched_image_id": img2_id,
                                "primary_phash": res["phash_1"],
                                "matched_phash": res["phash_2"],
                                "modality": modality,
                                "primary_visit": img1.get("visit_name", "Baseline"),
                                "matched_visit": img2.get("visit_name", "Baseline"),
                            },
                        }
                    )

        findings.sort(key=lambda x: x["similarity_score"], reverse=True)
        return findings


# ---------------------------------------------------------------------------
# Coordinator Service & Automated Investigation Dispatch
# ---------------------------------------------------------------------------


class DataIntegritySentinelService:
    """
    Coordinates automated cross-site forensic data integrity sentinels under ICH E6(R2)/(R3).
    Executes Duplicate Patient Record, Temporal & Behavioral, and pHash Image Hashing scans,
    and automatically dispatches 21 CFR Part 11 investigation tickets.
    """

    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    async def execute_duplicate_patient_scan(
        self,
        study_id: str,
        subjects: list[dict[str, Any]],
        similarity_threshold: float = 0.82,
        user_id: str = "csm.integrity.sentinel",
        user_role: str = "quality_manager",
        change_reason: str = "Cross-site duplicate patient record surveillance",
        auto_dispatch_investigation: bool = True,
    ) -> Any:
        """Executes automated Duplicate Patient Record similarity scan."""
        if not subjects:
            raise DataIntegritySentinelError(
                "No subject records provided for duplicate patient scan."
            )

        findings_raw = DuplicatePatientRecordEngine.scan_all_subjects(
            study_id=study_id,
            subjects=subjects,
            similarity_threshold=similarity_threshold,
        )

        risk_tier = RiskTier.LOW
        if any(f["severity"] == DeviationSeverity.CRITICAL for f in findings_raw):
            risk_tier = RiskTier.CRITICAL
        elif findings_raw:
            risk_tier = RiskTier.HIGH

        run = self.repo.create_data_integrity_run_entity(
            study_id=study_id,
            run_name=f"DUPLICATE-PATIENT-SCAN-{datetime.now().strftime('%Y%m%d-%H%M%S')}",
            scan_timestamp=datetime.now(),
            sentinel_types=[DataIntegritySentinelType.DUPLICATE_PATIENT_RECORD],
            total_subjects_scanned=len(subjects),
            total_images_scanned=0,
            total_events_scanned=0,
            anomalies_detected_count=len(findings_raw),
            risk_tier=risk_tier,
            status="COMPLETED",
            summary_findings={
                "duplicate_matches_count": len(findings_raw),
                "cross_site_matches_count": len(
                    [f for f in findings_raw if f["is_cross_site"]]
                ),
                "highest_similarity": findings_raw[0]["similarity_score"]
                if findings_raw
                else 0.0,
            },
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_data_integrity_run(run)

        saved_findings = []
        for f_dict in findings_raw:
            finding_rec = self.repo.create_data_integrity_finding_entity(
                run_id=run.id,
                study_id=study_id,
                sentinel_type=f_dict["sentinel_type"],
                severity=f_dict["severity"],
                anomaly_flag=f_dict["anomaly_flag"],
                primary_subject_id=f_dict["primary_subject_id"],
                primary_site_id=f_dict["primary_site_id"],
                matched_subject_id=f_dict["matched_subject_id"],
                matched_site_id=f_dict["matched_site_id"],
                confidence_score=f_dict["confidence_score"],
                similarity_score=f_dict["similarity_score"],
                hamming_distance=None,
                transformation_type=None,
                evidence_summary=f_dict["evidence_summary"],
                forensic_metadata=f_dict["forensic_metadata"],
                status=DataIntegrityFindingStatus.OPEN,
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason,
            )

            # Automated investigation dispatch
            if auto_dispatch_investigation and f_dict["severity"] in (
                DeviationSeverity.CRITICAL,
                DeviationSeverity.MAJOR,
            ):
                dev_id, kri_id = await self._dispatch_investigation_entities(
                    study_id=study_id,
                    site_id=f_dict["primary_site_id"],
                    title=f"Data Integrity: Duplicate Patient Record ({f_dict['primary_subject_id']} & {f_dict['matched_subject_id']})",
                    description=f_dict["evidence_summary"],
                    severity=f_dict["severity"],
                    confidence=f_dict["confidence_score"],
                    user_id=user_id,
                    user_role=user_role,
                    change_reason=change_reason,
                )
                finding_rec.investigation_deviation_id = dev_id
                finding_rec.kri_evaluation_id = kri_id
                finding_rec.status = DataIntegrityFindingStatus.INVESTIGATING

            await self.repo.save_data_integrity_finding(finding_rec)
            saved_findings.append(finding_rec)

        # Append-only 21 CFR Part 11 Audit Log
        audit_log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DATA_INTEGRITY_DUPLICATE_SCAN_COMPLETED",
            details=f"Completed duplicate patient scan for study '{study_id}'. Scanned {len(subjects)} subjects, flagged {len(saved_findings)} matches.",
            record_id=run.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(audit_log)

        return await self.get_run_by_id(run.id)

    async def execute_temporal_anomaly_scan(
        self,
        study_id: str,
        audit_events: list[dict[str, Any]],
        user_id: str = "csm.integrity.sentinel",
        user_role: str = "quality_manager",
        change_reason: str = "Temporal and behavioral data entry anomaly scan",
        auto_dispatch_investigation: bool = True,
    ) -> Any:
        """Executes automated Temporal & Behavioral Anomaly scan."""
        if not audit_events:
            raise DataIntegritySentinelError(
                "No audit trail events provided for temporal scan."
            )

        res = TemporalBehavioralAnomalyEngine.scan_temporal_anomalies(
            study_id=study_id, audit_events=audit_events
        )
        findings_raw = res["findings"]

        risk_tier = RiskTier.LOW
        if any(f["severity"] == DeviationSeverity.CRITICAL for f in findings_raw):
            risk_tier = RiskTier.CRITICAL
        elif findings_raw:
            risk_tier = RiskTier.HIGH

        run = self.repo.create_data_integrity_run_entity(
            study_id=study_id,
            run_name=f"TEMPORAL-ANOMALY-SCAN-{datetime.now().strftime('%Y%m%d-%H%M%S')}",
            scan_timestamp=datetime.now(),
            sentinel_types=[DataIntegritySentinelType.TEMPORAL_BEHAVIORAL_ANOMALY],
            total_subjects_scanned=0,
            total_images_scanned=0,
            total_events_scanned=len(audit_events),
            anomalies_detected_count=len(findings_raw),
            risk_tier=risk_tier,
            status="COMPLETED",
            summary_findings={
                "total_events": len(audit_events),
                "anomalies_count": len(findings_raw),
                "site_metrics": res["site_metrics"],
            },
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_data_integrity_run(run)

        saved_findings = []
        for f_dict in findings_raw:
            finding_rec = self.repo.create_data_integrity_finding_entity(
                run_id=run.id,
                study_id=study_id,
                sentinel_type=f_dict["sentinel_type"],
                severity=f_dict["severity"],
                anomaly_flag=f_dict["anomaly_flag"],
                primary_subject_id=f_dict["primary_subject_id"],
                primary_site_id=f_dict["primary_site_id"],
                matched_subject_id=f_dict.get("matched_subject_id"),
                matched_site_id=f_dict.get("matched_site_id"),
                confidence_score=f_dict["confidence_score"],
                similarity_score=f_dict["similarity_score"],
                hamming_distance=None,
                transformation_type=None,
                evidence_summary=f_dict["evidence_summary"],
                forensic_metadata=f_dict["forensic_metadata"],
                status=DataIntegrityFindingStatus.OPEN,
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason,
            )

            if auto_dispatch_investigation and f_dict["severity"] in (
                DeviationSeverity.CRITICAL,
                DeviationSeverity.MAJOR,
            ):
                dev_id, kri_id = await self._dispatch_investigation_entities(
                    study_id=study_id,
                    site_id=f_dict["primary_site_id"],
                    title=f"Data Integrity: Temporal Anomaly at Site {f_dict['primary_site_id']} ({f_dict['anomaly_flag']})",
                    description=f_dict["evidence_summary"],
                    severity=f_dict["severity"],
                    confidence=f_dict["confidence_score"],
                    user_id=user_id,
                    user_role=user_role,
                    change_reason=change_reason,
                )
                finding_rec.investigation_deviation_id = dev_id
                finding_rec.kri_evaluation_id = kri_id
                finding_rec.status = DataIntegrityFindingStatus.INVESTIGATING

            await self.repo.save_data_integrity_finding(finding_rec)
            saved_findings.append(finding_rec)

        audit_log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DATA_INTEGRITY_TEMPORAL_SCAN_COMPLETED",
            details=f"Completed temporal anomaly scan for study '{study_id}'. Scanned {len(audit_events)} events, flagged {len(saved_findings)} anomalies.",
            record_id=run.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(audit_log)

        return await self.get_run_by_id(run.id)

    async def execute_image_phash_scan(
        self,
        study_id: str,
        image_attachments: list[dict[str, Any]],
        hamming_threshold: int = 10,
        user_id: str = "csm.integrity.sentinel",
        user_role: str = "quality_manager",
        change_reason: str = "Perceptual image hash medical image duplication scan",
        auto_dispatch_investigation: bool = True,
    ) -> Any:
        """Executes automated Perceptual Image Hashing (pHash) scan."""
        if not image_attachments:
            raise DataIntegritySentinelError(
                "No medical image attachments provided for pHash scan."
            )

        findings_raw = PerceptualImageHashingEngine.scan_all_images(
            study_id=study_id,
            image_attachments=image_attachments,
            hamming_threshold=hamming_threshold,
        )

        risk_tier = RiskTier.LOW
        if any(f["severity"] == DeviationSeverity.CRITICAL for f in findings_raw):
            risk_tier = RiskTier.CRITICAL
        elif findings_raw:
            risk_tier = RiskTier.HIGH

        run = self.repo.create_data_integrity_run_entity(
            study_id=study_id,
            run_name=f"IMAGE-PHASH-SCAN-{datetime.now().strftime('%Y%m%d-%H%M%S')}",
            scan_timestamp=datetime.now(),
            sentinel_types=[DataIntegritySentinelType.PERCEPTUAL_IMAGE_HASH],
            total_subjects_scanned=0,
            total_images_scanned=len(image_attachments),
            total_events_scanned=0,
            anomalies_detected_count=len(findings_raw),
            risk_tier=risk_tier,
            status="COMPLETED",
            summary_findings={
                "total_images": len(image_attachments),
                "duplicated_images_count": len(findings_raw),
                "min_hamming_distance": min(
                    [f["hamming_distance"] for f in findings_raw]
                )
                if findings_raw
                else 64,
            },
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_data_integrity_run(run)

        saved_findings = []
        for f_dict in findings_raw:
            finding_rec = self.repo.create_data_integrity_finding_entity(
                run_id=run.id,
                study_id=study_id,
                sentinel_type=f_dict["sentinel_type"],
                severity=f_dict["severity"],
                anomaly_flag=f_dict["anomaly_flag"],
                primary_subject_id=f_dict["primary_subject_id"],
                primary_site_id=f_dict["primary_site_id"],
                matched_subject_id=f_dict["matched_subject_id"],
                matched_site_id=f_dict["matched_site_id"],
                confidence_score=f_dict["confidence_score"],
                similarity_score=f_dict["similarity_score"],
                hamming_distance=f_dict["hamming_distance"],
                transformation_type=f_dict["transformation_type"],
                evidence_summary=f_dict["evidence_summary"],
                forensic_metadata=f_dict["forensic_metadata"],
                status=DataIntegrityFindingStatus.OPEN,
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason,
            )

            if auto_dispatch_investigation and f_dict["severity"] in (
                DeviationSeverity.CRITICAL,
                DeviationSeverity.MAJOR,
            ):
                dev_id, kri_id = await self._dispatch_investigation_entities(
                    study_id=study_id,
                    site_id=f_dict["primary_site_id"],
                    title=f"Data Integrity: Reused Medical Image ({f_dict['primary_subject_id']} & {f_dict['matched_subject_id']})",
                    description=f_dict["evidence_summary"],
                    severity=f_dict["severity"],
                    confidence=f_dict["confidence_score"],
                    user_id=user_id,
                    user_role=user_role,
                    change_reason=change_reason,
                )
                finding_rec.investigation_deviation_id = dev_id
                finding_rec.kri_evaluation_id = kri_id
                finding_rec.status = DataIntegrityFindingStatus.INVESTIGATING

            await self.repo.save_data_integrity_finding(finding_rec)
            saved_findings.append(finding_rec)

        audit_log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DATA_INTEGRITY_IMAGE_PHASH_SCAN_COMPLETED",
            details=f"Completed perceptual image hash scan for study '{study_id}'. Scanned {len(image_attachments)} images, detected {len(saved_findings)} duplicates.",
            record_id=run.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(audit_log)

        return await self.get_run_by_id(run.id)

    async def execute_comprehensive_scan(
        self,
        study_id: str,
        subjects: list[dict[str, Any]] | None = None,
        audit_events: list[dict[str, Any]] | None = None,
        image_attachments: list[dict[str, Any]] | None = None,
        user_id: str = "csm.integrity.sentinel",
        user_role: str = "quality_manager",
        change_reason: str = "Comprehensive automated data integrity surveillance scan",
        auto_dispatch_investigation: bool = True,
    ) -> Any:
        """Executes all 3 sentinels under a single unified Data Integrity Sentinel run."""
        sentinel_types = []
        all_findings_raw = []

        if subjects:
            sentinel_types.append(DataIntegritySentinelType.DUPLICATE_PATIENT_RECORD)
            f_subjects = DuplicatePatientRecordEngine.scan_all_subjects(
                study_id=study_id, subjects=subjects
            )
            all_findings_raw.extend(f_subjects)

        if audit_events:
            sentinel_types.append(DataIntegritySentinelType.TEMPORAL_BEHAVIORAL_ANOMALY)
            res_temp = TemporalBehavioralAnomalyEngine.scan_temporal_anomalies(
                study_id=study_id, audit_events=audit_events
            )
            all_findings_raw.extend(res_temp["findings"])

        if image_attachments:
            sentinel_types.append(DataIntegritySentinelType.PERCEPTUAL_IMAGE_HASH)
            f_images = PerceptualImageHashingEngine.scan_all_images(
                study_id=study_id, image_attachments=image_attachments
            )
            all_findings_raw.extend(f_images)

        if not sentinel_types:
            raise DataIntegritySentinelError(
                "No data streams provided for comprehensive data integrity scan."
            )

        risk_tier = RiskTier.LOW
        if any(f["severity"] == DeviationSeverity.CRITICAL for f in all_findings_raw):
            risk_tier = RiskTier.CRITICAL
        elif all_findings_raw:
            risk_tier = RiskTier.HIGH

        run = self.repo.create_data_integrity_run_entity(
            study_id=study_id,
            run_name=f"COMPREHENSIVE-DATA-INTEGRITY-{datetime.now().strftime('%Y%m%d-%H%M%S')}",
            scan_timestamp=datetime.now(),
            sentinel_types=sentinel_types,
            total_subjects_scanned=len(subjects or []),
            total_images_scanned=len(image_attachments or []),
            total_events_scanned=len(audit_events or []),
            anomalies_detected_count=len(all_findings_raw),
            risk_tier=risk_tier,
            status="COMPLETED",
            summary_findings={
                "total_anomalies": len(all_findings_raw),
                "sentinels_active": [s.value for s in sentinel_types],
            },
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_data_integrity_run(run)

        saved_findings = []
        for f_dict in all_findings_raw:
            finding_rec = self.repo.create_data_integrity_finding_entity(
                run_id=run.id,
                study_id=study_id,
                sentinel_type=f_dict["sentinel_type"],
                severity=f_dict["severity"],
                anomaly_flag=f_dict["anomaly_flag"],
                primary_subject_id=f_dict["primary_subject_id"],
                primary_site_id=f_dict["primary_site_id"],
                matched_subject_id=f_dict.get("matched_subject_id"),
                matched_site_id=f_dict.get("matched_site_id"),
                confidence_score=f_dict["confidence_score"],
                similarity_score=f_dict["similarity_score"],
                hamming_distance=f_dict.get("hamming_distance"),
                transformation_type=f_dict.get("transformation_type"),
                evidence_summary=f_dict["evidence_summary"],
                forensic_metadata=f_dict["forensic_metadata"],
                status=DataIntegrityFindingStatus.OPEN,
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason,
            )

            if auto_dispatch_investigation and f_dict["severity"] in (
                DeviationSeverity.CRITICAL,
                DeviationSeverity.MAJOR,
            ):
                dev_id, kri_id = await self._dispatch_investigation_entities(
                    study_id=study_id,
                    site_id=f_dict["primary_site_id"],
                    title=f"Data Integrity: {f_dict['sentinel_type']} Sentinel Breach",
                    description=f_dict["evidence_summary"],
                    severity=f_dict["severity"],
                    confidence=f_dict["confidence_score"],
                    user_id=user_id,
                    user_role=user_role,
                    change_reason=change_reason,
                )
                finding_rec.investigation_deviation_id = dev_id
                finding_rec.kri_evaluation_id = kri_id
                finding_rec.status = DataIntegrityFindingStatus.INVESTIGATING

            await self.repo.save_data_integrity_finding(finding_rec)
            saved_findings.append(finding_rec)

        audit_log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="COMPREHENSIVE_DATA_INTEGRITY_SCAN_COMPLETED",
            details=f"Completed comprehensive data integrity scan for study '{study_id}'. Anomalies detected: {len(saved_findings)}.",
            record_id=run.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(audit_log)

        return await self.get_run_by_id(run.id)

    async def _dispatch_investigation_entities(
        self,
        study_id: str,
        site_id: str,
        title: str,
        description: str,
        severity: DeviationSeverity,
        confidence: float,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> tuple[str, str]:
        """Creates Deviation and KRI evaluation records under 21 CFR Part 11."""
        dev = self.repo.create_deviation_entity(
            study_id=study_id,
            site_id=site_id,
            title=title,
            description=description,
            severity=severity,
            type=DeviationType.GCP_COMPLIANCE,
            category="DATA_INTEGRITY_SENTINEL",
            is_protocol_violation=True,
            impact_safety=False,
            impact_data=True,
            impact_compliance=True,
            source_system="CSM_DATA_INTEGRITY_SENTINEL",
            source_reference_id=f"SENTINEL-{datetime.now().strftime('%Y%m%d%H%M%S')}",
            created_by=user_id,
            version_index=1,
            reason_for_change=f"Automated Part 11 investigation dispatch: {change_reason}",
        )
        await self.repo.save_deviation(dev)

        kri = self.repo.create_kri_evaluation_entity(
            study_id=study_id,
            site_id=site_id,
            kri_code="KRI_CSM_DATA_INTEGRITY",
            evaluation_date=datetime.now(),
            raw_value=round(confidence * 100.0, 2),
            standardized_z_score=round(confidence * 3.5, 2),
            risk_tier=RiskTier.CRITICAL
            if severity == DeviationSeverity.CRITICAL
            else RiskTier.HIGH,
            created_by=user_id,
            version_index=1,
            reason_for_change=f"Automated CSM Sentinel Alert: {title}",
        )
        await self.repo.save_kri_evaluation(kri)

        return dev.id, kri.id

    async def dispatch_investigation_for_finding(
        self,
        finding_id: str,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        """Manually dispatches an investigation deviation for an existing finding."""
        finding = await self.repo.get_data_integrity_finding_by_id(finding_id)
        if not finding:
            raise DataIntegritySentinelError(
                f"Data integrity finding '{finding_id}' not found.", status_code=404
            )

        if finding.investigation_deviation_id:
            return finding

        dev_id, kri_id = await self._dispatch_investigation_entities(
            study_id=finding.study_id,
            site_id=finding.primary_site_id,
            title=f"Data Integrity Manual Investigation: {finding.anomaly_flag}",
            description=finding.evidence_summary,
            severity=DeviationSeverity(finding.severity)
            if finding.severity in DeviationSeverity.__members__.values()
            else DeviationSeverity.MAJOR,
            confidence=finding.confidence_score,
            user_id=user_id,
            user_role=user_role,
            change_reason=change_reason,
        )

        finding.investigation_deviation_id = dev_id
        finding.kri_evaluation_id = kri_id
        finding.status = DataIntegrityFindingStatus.INVESTIGATING
        await self.repo.save_data_integrity_finding(finding)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="DATA_INTEGRITY_INVESTIGATION_MANUALLY_DISPATCHED",
            details=f"Dispatched Part 11 investigation deviation '{dev_id}' for finding '{finding_id}'.",
            record_id=finding_id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return finding

    async def list_runs(self, study_id: str | None = None) -> Sequence[Any]:
        return await self.repo.get_data_integrity_runs(study_id)

    async def get_run_by_id(self, run_id: str) -> Any:
        run = await self.repo.get_data_integrity_run_by_id(run_id)
        if not run:
            raise DataIntegritySentinelError(
                f"Data integrity run '{run_id}' not found.", status_code=404
            )
        return run

    async def get_findings_for_run(self, run_id: str) -> Sequence[Any]:
        return await self.repo.get_data_integrity_findings_by_run(run_id)

    # ---------------------------------------------------------------------------
    # Synthetic Dataset Simulation Generator
    # ---------------------------------------------------------------------------

    @classmethod
    def generate_simulated_sentinel_dataset(
        cls,
        study_id: str = "STUDY-ONC-2026",
        num_subjects_per_site: int = 15,
        include_duplicate_patient: bool = True,
        include_temporal_burst: bool = True,
        include_identical_intervals: bool = True,
        include_image_duplication: bool = True,
        seed: int = 42,
    ) -> dict[str, Any]:
        """
        Generates realistic cross-site synthetic clinical datasets with calibrated fraud anomalies
        for sentinel testing, calibration, and regression validation.
        """
        rng = random.Random(seed)
        sites = ["SITE-101", "SITE-102", "SITE-103", "SITE-104"]
        subjects = []
        audit_events = []
        image_attachments = []

        # 1. Compliant baseline subjects across sites
        for s_idx, site in enumerate(sites):
            for i in range(1, num_subjects_per_site + 1):
                subj_id = f"SUBJ-{site[-3:]}-{i:03d}"
                age = float(rng.randint(35, 75))
                ht = float(round(rng.gauss(172.0, 9.0), 1))
                wt = float(round(rng.gauss(76.0, 14.0), 1))
                bmi = round(wt / ((ht / 100.0) ** 2), 1)
                sbp = float(round(rng.gauss(124.0, 12.0)))
                dbp = float(round(0.65 * sbp + rng.gauss(0.0, 5.0)))
                hr = float(round(rng.gauss(72.0, 8.0)))
                pr = float(round(rng.gauss(160.0, 15.0)))
                qrs = float(round(rng.gauss(92.0, 8.0)))
                qt = float(round(rng.gauss(410.0, 20.0)))
                qtc = float(round(qt / math.sqrt(60.0 / max(40.0, hr)), 1))
                creat = float(round(math.exp(rng.gauss(0.1, 0.25)), 2))
                alt = float(round(rng.gauss(24.0, 8.0), 1))

                subjects.append(
                    {
                        "subject_id": subj_id,
                        "site_id": site,
                        "study_id": study_id,
                        "demographics": {
                            "age": age,
                            "sex": "M" if rng.random() > 0.5 else "F",
                            "race": "WHITE" if rng.random() > 0.3 else "ASIAN",
                            "height_cm": ht,
                            "weight_kg": wt,
                            "bmi": bmi,
                            "blood_type": rng.choice(["A+", "O+", "B+", "AB+"]),
                        },
                        "vital_signs": {
                            "systolic_bp": sbp,
                            "diastolic_bp": dbp,
                            "heart_rate": hr,
                            "respiratory_rate": 16.0,
                            "temperature_c": 36.6,
                        },
                        "ecg_intervals": {
                            "pr_interval_ms": pr,
                            "qrs_duration_ms": qrs,
                            "qt_interval_ms": qt,
                            "qtc_ms": qtc,
                            "rr_interval_ms": round(60000.0 / hr, 1),
                        },
                        "lab_panel": {
                            "creatinine": creat,
                            "alt": alt,
                            "ast": round(alt * 0.85, 1),
                            "fasting_glucose": float(round(rng.gauss(98.0, 12.0), 1)),
                        },
                        "medical_history": ["I10", "E11.9"]
                        if rng.random() > 0.5
                        else ["K21.9"],
                    }
                )

                # Generate compliant audit entry timestamps (weekday business hours 08:00 - 17:00)
                base_hour = 8 + (i % 8)
                base_min = (i * 7) % 55
                audit_events.append(
                    {
                        "entry_id": f"EV-{subj_id}-01",
                        "subject_id": subj_id,
                        "site_id": site,
                        "user_id": f"crc.{site.lower()}@cadence.io",
                        "timestamp": f"2026-08-10T{base_hour:02d}:{base_min:02d}:00Z",
                        "assessment_type": "VISIT_01_SCREENING",
                        "procedure_pair": "VITALS_TO_ECG",
                        "elapsed_time_seconds": float(round(rng.gauss(900.0, 60.0))),
                    }
                )

                # Generate synthetic image pattern (e.g. 32x32 gradient with unique noise)
                img_grid = [
                    [
                        float((r * 8 + c * 8 + rng.randint(-5, 5)) % 256)
                        for c in range(32)
                    ]
                    for r in range(32)
                ]
                image_attachments.append(
                    {
                        "image_id": f"IMG-{subj_id}-BL",
                        "subject_id": subj_id,
                        "site_id": site,
                        "study_id": study_id,
                        "visit_name": "Screening",
                        "modality": "CHEST_CT",
                        "pixel_matrix": img_grid,
                    }
                )

        # 2. Inject Cross-Site Duplicate Patient Record
        if include_duplicate_patient and len(subjects) >= 2:
            target_source = subjects[0]
            cloned_subj = {
                "subject_id": "SUBJ-CLONED-DUPLICATE",
                "site_id": "SITE-104",  # Different site
                "study_id": study_id,
                "demographics": {
                    **target_source["demographics"],
                    "age": target_source["demographics"]["age"],
                    "weight_kg": target_source["demographics"]["weight_kg"] + 0.5,
                },
                "vital_signs": {
                    **target_source["vital_signs"],
                    "systolic_bp": target_source["vital_signs"]["systolic_bp"] + 1.0,
                },
                "ecg_intervals": {**target_source["ecg_intervals"]},
                "lab_panel": {**target_source["lab_panel"]},
                "medical_history": list(target_source["medical_history"]),
            }
            subjects.append(cloned_subj)

        # 3. Inject Temporal Burst & Off-Hours cluster
        if include_temporal_burst:
            burst_site = "SITE-TEMPORAL-ANOMALY"
            for b_i in range(10):
                audit_events.append(
                    {
                        "entry_id": f"EV-BURST-{b_i:02d}",
                        "subject_id": f"SUBJ-BURST-{b_i:02d}",
                        "site_id": burst_site,
                        "user_id": "bot.coordinator@badactor.org",
                        "timestamp": f"2026-08-16T02:15:{b_i * 4:02d}Z",  # Sunday (Weekend) 02:15 AM with 4s intervals
                        "assessment_type": "VISIT_02_BURST",
                        "procedure_pair": "PHYSICAL_TO_LAB",
                        "elapsed_time_seconds": 300.0,
                    }
                )

        # 4. Inject Identical Procedure Elapsed Intervals
        if include_identical_intervals:
            identical_site = "SITE-IDENTICAL-INTERVALS"
            for id_i in range(5):
                audit_events.append(
                    {
                        "entry_id": f"EV-IDENTICAL-{id_i:02d}",
                        "subject_id": f"SUBJ-IDENTICAL-{id_i:02d}",
                        "site_id": identical_site,
                        "user_id": "cra.identical@cadence.io",
                        "timestamp": f"2026-08-12T10:{id_i * 10:02d}:00Z",
                        "assessment_type": "VISIT_01_ASSESSMENT",
                        "procedure_pair": "VITALS_TO_BLOOD_DRAW",
                        "elapsed_time_seconds": 870.0,  # Exactly 14m 30s across all 5 subjects
                    }
                )

        # 5. Inject Perceptual Image Duplication (Reused image rotated 90 degrees)
        if include_image_duplication and image_attachments:
            orig_img = image_attachments[0]
            # Rotate original 32x32 grid by 90 degrees
            orig_grid = orig_img["pixel_matrix"]
            rot_90_grid = [[orig_grid[31 - r][c] for r in range(32)] for c in range(32)]
            reused_img = {
                "image_id": "IMG-REUSED-ROTATED-90",
                "subject_id": "SUBJ-104-099",
                "site_id": "SITE-104",
                "study_id": study_id,
                "visit_name": "Cycle_1_Day_1",
                "modality": "CHEST_CT",
                "pixel_matrix": rot_90_grid,
            }
            image_attachments.append(reused_img)

        return {
            "study_id": study_id,
            "subjects": subjects,
            "audit_events": audit_events,
            "image_attachments": image_attachments,
        }
