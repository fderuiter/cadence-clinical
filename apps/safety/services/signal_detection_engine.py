"""Vectorized Disproportionality Safety Signal Detection Engine (PRR, ROR, EBGM).

Requirements: PRD-PVG-003, PRD-SYS-001 (CIOMS VIII, FDA FAERS / EMA Guidelines)
"""

import math
from typing import Any

from pydantic import BaseModel, Field


class DisproportionalityContingency(BaseModel):
    """2x2 contingency counts for a specific Drug-Event pair."""

    drug_name: str
    meddra_pt: str  # MedDRA Preferred Term
    a: int = Field(ge=0, description="Reports with target drug and target event")
    b: int = Field(ge=0, description="Reports with target drug and other events")
    c: int = Field(ge=0, description="Reports with other drugs and target event")
    d: int = Field(ge=0, description="Reports with other drugs and other events")


class DisproportionalityMetricResult(BaseModel):
    """Calculated statistical signal detection metrics for a Drug-Event pair."""

    drug_name: str
    meddra_pt: str
    case_count_a: int
    expected_count_e: float
    prr: float
    prr_ci_lower: float
    prr_ci_upper: float
    chi_square_yates: float
    ror: float
    ror_ci_lower: float
    ror_ci_upper: float
    ebgm: float
    eb05: float  # 5th percentile lower bound of Empirical Bayes shrinkage
    is_prr_signal: bool = False
    is_ror_signal: bool = False
    is_ebgm_signal: bool = False
    overall_signal_flag: bool = False


class SafetySignalDetectionEngine:
    """Disproportionality analysis engine implementing PRR, ROR, and Empirical Bayes estimation."""

    @classmethod
    def calculate_metrics(
        cls,
        contingency: DisproportionalityContingency,
    ) -> DisproportionalityMetricResult:
        """Calculates PRR, ROR, Chi-Square, and EBGM metrics for a Drug-Event contingency.

        Args:
            contingency: 2x2 table counts (a, b, c, d).

        Returns:
            DisproportionalityMetricResult with calculated indices and signal classifications.
        """
        a = float(contingency.a)
        b = float(contingency.b)
        c = float(contingency.c)
        d = float(contingency.d)
        n = a + b + c + d

        if n <= 0.0:
            raise ValueError("Total report count n must be greater than zero.")

        # Expected count E = (a+b)(a+c) / n
        expected_e = round(((a + b) * (a + c)) / n, 4) if n > 0 else 0.0

        # --- 1. Proportional Reporting Ratio (PRR) ---
        if (a + b) > 0 and (c + d) > 0 and c > 0:
            prr = (a / (a + b)) / (c / (c + d))
            # Standard error of ln(PRR)
            var_ln_prr = (
                (1.0 / max(a, 0.5))
                - (1.0 / (a + b))
                + (1.0 / max(c, 0.5))
                - (1.0 / (c + d))
            )
            se_prr = math.sqrt(max(var_ln_prr, 0.0))
            prr_lower = math.exp(math.log(max(prr, 1e-6)) - 1.96 * se_prr)
            prr_upper = math.exp(math.log(max(prr, 1e-6)) + 1.96 * se_prr)
        else:
            prr = 0.0
            prr_lower = 0.0
            prr_upper = 0.0

        # --- 2. Yates' Corrected Chi-Square ---
        denom_chi = (a + b) * (c + d) * (a + c) * (b + d)
        if denom_chi > 0:
            numerator_inner = abs(a * d - b * c) - (n / 2.0)
            chi_square = (n * (max(numerator_inner, 0.0) ** 2)) / denom_chi
        else:
            chi_square = 0.0

        # --- 3. Reporting Odds Ratio (ROR) ---
        if b > 0 and c > 0 and d > 0:
            ror = (a * d) / (b * c)
            var_ln_ror = (1.0 / max(a, 0.5)) + (1.0 / b) + (1.0 / c) + (1.0 / d)
            se_ror = math.sqrt(var_ln_ror)
            ror_lower = math.exp(math.log(max(ror, 1e-6)) - 1.96 * se_ror)
            ror_upper = math.exp(math.log(max(ror, 1e-6)) + 1.96 * se_ror)
        else:
            ror = 0.0
            ror_lower = 0.0
            ror_upper = 0.0

        # --- 4. Empirical Bayes Geometric Mean (EBGM) & EB05 ---
        # Prior shrinkage parameters (alpha0=1.0, beta0=1.0)
        alpha0 = 1.0
        beta0 = 1.0
        ebgm = (a + alpha0) / (expected_e + beta0) if (expected_e + beta0) > 0 else 0.0
        # 5th percentile lower bound: EB05 = EBGM * exp(-1.645 * 1 / sqrt(a))
        eb05 = ebgm * math.exp(-1.645 * (1.0 / math.sqrt(a))) if a > 0 else 0.0

        # --- 5. Regulatory Signal Criteria ---
        # Evans / MHRA Criteria: PRR >= 2.0, ChiSquare >= 4.0, a >= 3
        is_prr_signal = (contingency.a >= 3) and (prr >= 2.0) and (chi_square >= 4.0)

        # ROR Criteria: ROR_lower > 1.0, a >= 3
        is_ror_signal = (contingency.a >= 3) and (ror_lower > 1.0)

        # FDA / MGPS Criteria: EB05 >= 2.0
        is_ebgm_signal = (contingency.a >= 3) and (eb05 >= 2.0)

        overall_signal = is_prr_signal or is_ror_signal or is_ebgm_signal

        return DisproportionalityMetricResult(
            drug_name=contingency.drug_name,
            meddra_pt=contingency.meddra_pt,
            case_count_a=contingency.a,
            expected_count_e=round(expected_e, 2),
            prr=round(prr, 4),
            prr_ci_lower=round(prr_lower, 4),
            prr_ci_upper=round(prr_upper, 4),
            chi_square_yates=round(chi_square, 4),
            ror=round(ror, 4),
            ror_ci_lower=round(ror_lower, 4),
            ror_ci_upper=round(ror_upper, 4),
            ebgm=round(ebgm, 4),
            eb05=round(eb05, 4),
            is_prr_signal=is_prr_signal,
            is_ror_signal=is_ror_signal,
            is_ebgm_signal=is_ebgm_signal,
            overall_signal_flag=overall_signal,
        )

    @classmethod
    def scan_safety_dataset(
        cls,
        cases: list[dict[str, Any]],
        target_drug: str,
    ) -> list[DisproportionalityMetricResult]:
        """Scans a raw adverse event dataset and computes disproportionality metrics for all reported PTs.

        Args:
            cases: List of dictionaries with keys {'drug_name', 'meddra_pt', 'case_id'}.
            target_drug: Drug identifier to evaluate.

        Returns:
            List of DisproportionalityMetricResult sorted by PRR descending.
        """
        all_pts: set[str] = set()
        drug_pt_counts: dict[str, int] = {}
        drug_total_counts: int = 0
        other_pt_counts: dict[str, int] = {}
        other_total_counts: int = 0

        target_norm = target_drug.strip().upper()

        for c in cases:
            d = str(c.get("drug_name", "")).strip().upper()
            pt = str(c.get("meddra_pt", "")).strip()
            if not pt:
                continue
            all_pts.add(pt)

            if d == target_norm:
                drug_pt_counts[pt] = drug_pt_counts.get(pt, 0) + 1
                drug_total_counts += 1
            else:
                other_pt_counts[pt] = other_pt_counts.get(pt, 0) + 1
                other_total_counts += 1

        results: list[DisproportionalityMetricResult] = []

        for pt in all_pts:
            a = drug_pt_counts.get(pt, 0)
            b = max(drug_total_counts - a, 0)
            c = other_pt_counts.get(pt, 0)
            d = max(other_total_counts - c, 0)

            if a + b + c + d == 0:
                continue

            contingency = DisproportionalityContingency(
                drug_name=target_drug,
                meddra_pt=pt,
                a=a,
                b=b,
                c=c,
                d=d,
            )
            res = cls.calculate_metrics(contingency)
            results.append(res)

        results.sort(key=lambda r: r.prr, reverse=True)
        return results
