"""Statistical Forensics & Centralized Statistical Monitoring (CSM) Surveillance Engine.

Requirements:
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- PRD-CSM-001: Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis
- PRD-CSM-002: Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection
- ICH E6(R2)/(R3) Risk-Based Quality Surveillance
"""

import math
import random
from collections.abc import Sequence
from datetime import datetime
from typing import Any

from apps.quality.domain.models import (
    BenfordConformity,
    ForensicsAnomalyFlag,
    RiskTier,
    RoundingBiasTier,
)
from apps.quality.domain.ports import QualityRepositoryPort
from packages.hexagonal import DomainError


class StatisticalForensicsError(DomainError):
    def __init__(self, message: str, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


# Theoretical Benford's Law leading digit distribution: P(d) = log10(1 + 1/d)
BENFORD_THEORETICAL_PROBS: dict[int, float] = {
    d: math.log10(1.0 + 1.0 / d) for d in range(1, 10)
}


class ChiSquareDistribution:
    """Analytical Chi-Square cumulative distribution and survival functions."""

    @staticmethod
    def _gamma_p(a: float, z: float) -> float:
        """Lower regularized incomplete gamma function P(a, z) via series expansion."""
        if z <= 0.0:
            return 0.0
        sum_val = 1.0 / a
        term = 1.0 / a
        for k in range(1, 250):
            term *= z / (a + k)
            sum_val += term
            if abs(term) < abs(sum_val) * 1e-15:
                break
        log_factor = a * math.log(z) - z - math.lgamma(a)
        return max(0.0, min(1.0, math.exp(log_factor) * sum_val))

    @classmethod
    def _gamma_q(cls, a: float, z: float) -> float:
        """Upper regularized incomplete gamma function Q(a, z) via Lentz continued fraction."""
        if z <= 0.0:
            return 1.0
        if z < a + 1.0:
            return max(0.0, min(1.0, 1.0 - cls._gamma_p(a, z)))

        tiny = 1e-30
        b = z + 1.0 - a
        c = 1.0 / tiny
        d = 1.0 / b
        h = d
        for i in range(1, 250):
            an = -i * (i - a)
            b += 2.0
            d = an * d + b
            if abs(d) < tiny:
                d = tiny
            c = b + an / c
            if abs(c) < tiny:
                c = tiny
            d = 1.0 / d
            delta = d * c
            h *= delta
            if abs(delta - 1.0) < 1e-15:
                break
        log_factor = a * math.log(z) - z - math.lgamma(a)
        return max(0.0, min(1.0, math.exp(log_factor) * h))

    @classmethod
    def survival(cls, df: float, chi2: float) -> float:
        """Returns upper tail probability P(ChiSq >= chi2) for given degrees of freedom."""
        if chi2 <= 0.0:
            return 1.0
        if df <= 0.0:
            return 0.0
        return cls._gamma_q(df / 2.0, chi2 / 2.0)

    @classmethod
    def cdf(cls, df: float, chi2: float) -> float:
        """Returns lower tail probability P(ChiSq <= chi2) for given degrees of freedom."""
        if chi2 <= 0.0:
            return 0.0
        if df <= 0.0:
            return 1.0
        return cls._gamma_p(df / 2.0, chi2 / 2.0)


class BenfordAnalysisEngine:
    """Evaluates continuous variable leading digit frequencies against Benford's Law."""

    @staticmethod
    def extract_leading_digit(value: float) -> int | None:
        """Extracts the first non-zero leading digit (1-9) from a numeric value."""
        val = abs(value)
        if val == 0.0 or math.isnan(val) or math.isinf(val):
            return None
        while val >= 10.0:
            val /= 10.0
        while val < 1.0:
            val *= 10.0
        digit = int(val)
        return digit if 1 <= digit <= 9 else None

    @classmethod
    def analyze_digits(cls, values: list[float]) -> dict[str, Any]:
        """Calculates leading digit distribution, Chi-Square goodness-of-fit, p-value, and MAD."""
        observed_counts: dict[int, int] = {d: 0 for d in range(1, 10)}
        total_valid = 0

        for v in values:
            d = cls.extract_leading_digit(v)
            if d is not None:
                observed_counts[d] += 1
                total_valid += 1

        if total_valid == 0:
            return {
                "sample_size": 0,
                "chi_square": 0.0,
                "p_value": 1.0,
                "mad": 0.0,
                "conformity": BenfordConformity.CLOSE_CONFORMITY,
                "observed_frequencies": {str(d): 0 for d in range(1, 10)},
                "expected_frequencies": {str(d): 0.0 for d in range(1, 10)},
            }

        expected_counts: dict[int, float] = {
            d: total_valid * BENFORD_THEORETICAL_PROBS[d] for d in range(1, 10)
        }

        chi2 = 0.0
        mad_sum = 0.0
        for d in range(1, 10):
            obs = observed_counts[d]
            exp = expected_counts[d]
            if exp > 0:
                chi2 += ((obs - exp) ** 2) / exp
            obs_prop = obs / total_valid
            mad_sum += abs(obs_prop - BENFORD_THEORETICAL_PROBS[d])

        mad = mad_sum / 9.0
        df = 8.0
        p_val = ChiSquareDistribution.survival(df, chi2)

        # Nigrini Conformity Categories
        if mad < 0.006:
            conformity = BenfordConformity.CLOSE_CONFORMITY
        elif mad < 0.012:
            conformity = BenfordConformity.ACCEPTABLE_CONFORMITY
        elif mad < 0.015:
            conformity = BenfordConformity.MARGINAL_CONFORMITY
        else:
            conformity = BenfordConformity.NON_CONFORMING

        return {
            "sample_size": total_valid,
            "chi_square": round(chi2, 4),
            "p_value": round(p_val, 6),
            "mad": round(mad, 5),
            "conformity": conformity,
            "observed_frequencies": {str(d): observed_counts[d] for d in range(1, 10)},
            "expected_frequencies": {
                str(d): round(expected_counts[d], 2) for d in range(1, 10)
            },
        }


class TerminalDigitRoundingEngine:
    """Evaluates continuous variable terminal digit distribution for rounding bias."""

    @staticmethod
    def extract_terminal_digit(value: float) -> int | None:
        """Extracts the terminal integer digit (0-9)."""
        if math.isnan(value) or math.isinf(value):
            return None
        rounded_int = int(round(abs(value)))
        return rounded_int % 10

    @classmethod
    def analyze_terminal_digits(cls, values: list[float]) -> dict[str, Any]:
        """Calculates terminal digit counts, uniform Chi-Square test, and Zero-Five index."""
        observed_counts: dict[int, int] = {d: 0 for d in range(10)}
        total_valid = 0

        for v in values:
            d = cls.extract_terminal_digit(v)
            if d is not None:
                observed_counts[d] += 1
                total_valid += 1

        if total_valid == 0:
            return {
                "sample_size": 0,
                "chi_square": 0.0,
                "p_value": 1.0,
                "zero_five_index": 0.2,
                "rounding_bias_tier": RoundingBiasTier.NORMAL,
                "observed_frequencies": {str(d): 0 for d in range(10)},
                "expected_frequencies": {str(d): 0.0 for d in range(10)},
            }

        expected_per_digit = total_valid * 0.10
        chi2 = 0.0
        for d in range(10):
            obs = observed_counts[d]
            chi2 += ((obs - expected_per_digit) ** 2) / expected_per_digit

        df = 9.0
        p_val = ChiSquareDistribution.survival(df, chi2)
        zero_five_count = observed_counts[0] + observed_counts[5]
        zero_five_index = zero_five_count / total_valid

        # Rounding Bias Tiering
        if zero_five_index >= 0.45 or (p_val < 0.001 and zero_five_index >= 0.35):
            tier = RoundingBiasTier.SEVERE_ROUNDING
        elif zero_five_index >= 0.30 or (p_val < 0.01 and zero_five_index >= 0.25):
            tier = RoundingBiasTier.MODERATE_ROUNDING
        else:
            tier = RoundingBiasTier.NORMAL

        return {
            "sample_size": total_valid,
            "chi_square": round(chi2, 4),
            "p_value": round(p_val, 6),
            "zero_five_index": round(zero_five_index, 4),
            "rounding_bias_tier": tier,
            "observed_frequencies": {str(d): observed_counts[d] for d in range(10)},
            "expected_frequencies": {
                str(d): round(expected_per_digit, 2) for d in range(10)
            },
        }


class MultivariateMahalanobisEngine:
    """Calculates multivariate centroids, covariance matrices, and Mahalanobis distances."""

    @staticmethod
    def _invert_matrix(
        matrix: list[list[float]], ridge: float = 1e-6
    ) -> list[list[float]]:
        """Inverts a square matrix using Gauss-Jordan elimination with diagonal ridge stabilization."""
        n = len(matrix)
        # Create augmented matrix [A + lambda*I | I]
        aug = [
            [matrix[i][j] + (ridge if i == j else 0.0) for j in range(n)]
            + [1.0 if i == j else 0.0 for j in range(n)]
            for i in range(n)
        ]

        for i in range(n):
            # Pivot selection
            max_row = i
            max_val = abs(aug[i][i])
            for r in range(i + 1, n):
                if abs(aug[r][i]) > max_val:
                    max_val = abs(aug[r][i])
                    max_row = r

            if max_row != i:
                aug[i], aug[max_row] = aug[max_row], aug[i]

            pivot = aug[i][i]
            if abs(pivot) < 1e-12:
                pivot = 1e-12

            # Scale pivot row
            for c in range(2 * n):
                aug[i][c] /= pivot

            # Eliminate column entries
            for r in range(n):
                if r != i:
                    factor = aug[r][i]
                    for c in range(2 * n):
                        aug[r][c] -= factor * aug[i][c]

        return [[aug[i][n + j] for j in range(n)] for i in range(n)]

    @classmethod
    def compute_centroid_and_cov(
        cls, rows: list[list[float]]
    ) -> tuple[list[float], list[list[float]], list[list[float]]]:
        """Computes mean vector, covariance matrix, and Pearson correlation matrix."""
        n = len(rows)
        if n == 0:
            return [], [], []
        p = len(rows[0])
        if p == 0:
            return [], [], []

        means = [sum(rows[i][j] for i in range(n)) / n for j in range(p)]

        cov = [[0.0] * p for _ in range(p)]
        if n > 1:
            for i in range(n):
                diffs = [rows[i][j] - means[j] for j in range(p)]
                for r in range(p):
                    for c in range(p):
                        cov[r][c] += diffs[r] * diffs[c]
            for r in range(p):
                for c in range(p):
                    cov[r][c] /= n - 1

        corr = [[1.0 if r == c else 0.0 for c in range(p)] for r in range(p)]
        for r in range(p):
            for c in range(p):
                std_r = math.sqrt(max(cov[r][r], 1e-9))
                std_c = math.sqrt(max(cov[c][c], 1e-9))
                corr[r][c] = cov[r][c] / (std_r * std_c)

        return means, cov, corr

    @classmethod
    def compute_mahalanobis_distance(
        cls,
        site_mean: list[float],
        pooled_mean: list[float],
        pooled_cov_inv: list[list[float]],
        sample_size: int = 1,
    ) -> tuple[float, float]:
        """Computes Mahalanobis distance D_M and Hotelling T2 Chi-Square p-value."""
        p = len(site_mean)
        if p == 0 or len(pooled_mean) != p or len(pooled_cov_inv) != p:
            return 0.0, 1.0

        diff = [site_mean[j] - pooled_mean[j] for j in range(p)]
        # v = diff^T * cov_inv
        v = [sum(diff[i] * pooled_cov_inv[i][j] for i in range(p)) for j in range(p)]
        d2 = sum(v[j] * diff[j] for j in range(p))
        d2 = max(0.0, d2)
        d_m = math.sqrt(d2)

        # Scale by site sample size for Hotelling's T^2 multivariate cluster test
        t2_stat = max(1, sample_size) * d2
        p_val = ChiSquareDistribution.survival(float(p), t2_stat)
        return round(d_m, 4), round(p_val, 6)

    @classmethod
    def compute_correlation_matrix_distance(
        cls, r1: list[list[float]], r2: list[list[float]]
    ) -> float:
        """Computes Frobenius norm distance ||R1 - R2||_F between two correlation matrices."""
        p = len(r1)
        if p == 0 or len(r2) != p:
            return 0.0
        sum_sq = 0.0
        for i in range(p):
            for j in range(p):
                sum_sq += (r1[i][j] - r2[i][j]) ** 2
        return round(math.sqrt(sum_sq), 4)


class VarianceShrinkageKLScanner:
    """Detects artificially deflated variance (variance shrinkage) and computes KL divergence."""

    @classmethod
    def analyze_variance_and_kl(
        cls,
        site_values_by_var: dict[str, list[float]],
        pooled_means_by_var: dict[str, float],
        pooled_vars_by_var: dict[str, float],
    ) -> dict[str, Any]:
        """Computes variance ratio, one-tailed shrinkage p-value, and KL divergence."""
        if not site_values_by_var:
            return {
                "variance_ratio": 1.0,
                "p_value": 1.0,
                "kl_divergence": 0.0,
                "is_variance_shrunk": False,
            }

        ratios = []
        p_values = []
        kl_terms = []

        for var_name, values in site_values_by_var.items():
            n = len(values)
            if n <= 1:
                continue

            site_mean = sum(values) / n
            site_var = sum((x - site_mean) ** 2 for x in values) / (n - 1)
            pooled_mean = pooled_means_by_var.get(var_name, site_mean)
            pooled_var = pooled_vars_by_var.get(var_name, site_var)

            safe_pooled_var = max(pooled_var, 1e-6)
            safe_site_var = max(site_var, 1e-6)

            var_ratio = safe_site_var / safe_pooled_var
            ratios.append(var_ratio)

            # Variance Shrinkage Chi-Square test: Chi2 = (n - 1) * s^2 / sigma^2
            df = float(n - 1)
            chi2_stat = df * var_ratio
            # Lower-tail p-value
            p_val = ChiSquareDistribution.cdf(df, chi2_stat)
            p_values.append(p_val)

            # Univariate Gaussian KL Divergence: KL(Site || Pooled)
            # KL = ln(sigma_pool / sigma_site) + (sigma_site^2 + (mu_site - mu_pool)^2)/(2 * sigma_pool^2) - 0.5
            std_pool = math.sqrt(safe_pooled_var)
            std_site = math.sqrt(safe_site_var)
            kl = (
                math.log(std_pool / std_site)
                + (safe_site_var + (site_mean - pooled_mean) ** 2)
                / (2.0 * safe_pooled_var)
                - 0.5
            )
            kl_terms.append(max(0.0, kl))

        avg_ratio = sum(ratios) / len(ratios) if ratios else 1.0
        min_p_val = min(p_values) if p_values else 1.0
        total_kl = sum(kl_terms) if kl_terms else 0.0

        # Variance shrinkage flag: ratio <= 0.30 AND shrinkage p-value < 0.001
        is_shrunk = avg_ratio <= 0.30 and min_p_val < 0.001

        return {
            "variance_ratio": round(avg_ratio, 4),
            "p_value": round(min_p_val, 6),
            "kl_divergence": round(total_kl, 4),
            "is_variance_shrunk": is_shrunk,
        }


class StatisticalForensicsService:
    """
    Coordinates multi-site statistical surveillance under ICH E6(R2)/(R3).
    Executes Benford's Law, Terminal Digit rounding, Mahalanobis distances, and Variance Shrinkage tests.
    """

    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    async def execute_forensics_surveillance(
        self,
        study_id: str,
        target_variables: list[str],
        site_observation_data: dict[str, list[dict[str, float]]],
        run_name: str | None = None,
        user_id: str = "csm.sentinel",
        user_role: str = "quality_manager",
        change_reason: str = "Automated cross-site statistical surveillance",
        auto_trigger_kri: bool = True,
    ) -> Any:
        """Executes full statistical forensics scan across clinical sites."""
        if not site_observation_data:
            raise StatisticalForensicsError("No site observation data provided.")

        actual_run_name = (
            run_name
            or f"CSM-SCAN-{study_id}-{datetime.now().strftime('%Y%m%d-%H%M%S')}"
        )

        # 1. Pool all observations to compute benchmark distributions
        all_pooled_rows: list[list[float]] = []
        site_rows: dict[str, list[list[float]]] = {}
        all_continuous_values: dict[str, list[float]] = {
            v: [] for v in target_variables
        }
        site_values_by_var: dict[str, dict[str, list[float]]] = {
            site_id: {v: [] for v in target_variables}
            for site_id in site_observation_data
        }

        total_obs_count = 0
        for site_id, obs_list in site_observation_data.items():
            site_rows[site_id] = []
            for obs in obs_list:
                row = [float(obs.get(v, 0.0)) for v in target_variables]
                site_rows[site_id].append(row)
                all_pooled_rows.append(row)
                total_obs_count += 1
                for v in target_variables:
                    val = float(obs.get(v, 0.0))
                    all_continuous_values[v].append(val)
                    site_values_by_var[site_id][v].append(val)

        # 2. Compute study-level pooled multivariate parameters
        pooled_means, pooled_cov, pooled_corr = (
            MultivariateMahalanobisEngine.compute_centroid_and_cov(all_pooled_rows)
        )
        pooled_cov_inv = MultivariateMahalanobisEngine._invert_matrix(pooled_cov)

        pooled_means_dict = {
            target_variables[i]: pooled_means[i] for i in range(len(target_variables))
        }
        pooled_vars_dict = {
            target_variables[i]: pooled_cov[i][i] for i in range(len(target_variables))
        }

        # 3. Create parent ForensicsRun entity
        run = self.repo.create_forensics_run_entity(
            study_id=study_id,
            run_name=actual_run_name,
            analysis_timestamp=datetime.now(),
            target_variables=target_variables,
            total_sites=len(site_observation_data),
            total_observations=total_obs_count,
            anomalous_sites_count=0,
            status="COMPLETED",
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_forensics_run(run)

        anomalous_count = 0
        site_records = []

        # 4. Evaluate each site individually
        for site_id, rows in site_rows.items():
            n_site = len(rows)
            site_all_vals: list[float] = []
            for v in target_variables:
                site_all_vals.extend(site_values_by_var[site_id][v])

            # Select variables appropriate for Benford's Law (e.g. wide-spanning / laboratory / biomarker metrics)
            benford_vals: list[float] = []
            for v in target_variables:
                v_lower = v.lower()
                if any(
                    kw in v_lower
                    for kw in (
                        "lab",
                        "creat",
                        "tumor",
                        "viral",
                        "titer",
                        "biomarker",
                        "volume",
                        "pk",
                        "dose",
                        "log",
                        "count",
                    )
                ):
                    benford_vals.extend(site_values_by_var[site_id][v])

            if not benford_vals:
                benford_vals = site_all_vals

            # A. Benford's Law analysis
            benford_res = BenfordAnalysisEngine.analyze_digits(benford_vals)

            # B. Terminal Digit Rounding analysis (applies across all continuous clinical variables)
            terminal_res = TerminalDigitRoundingEngine.analyze_terminal_digits(
                site_all_vals
            )

            # C. Multivariate Mahalanobis & Correlation Distance
            site_means, site_cov, site_corr = (
                MultivariateMahalanobisEngine.compute_centroid_and_cov(rows)
            )
            d_m, p_mahalanobis = (
                MultivariateMahalanobisEngine.compute_mahalanobis_distance(
                    site_means, pooled_means, pooled_cov_inv, sample_size=n_site
                )
            )

            corr_dist = (
                MultivariateMahalanobisEngine.compute_correlation_matrix_distance(
                    site_corr, pooled_corr
                )
            )

            # D. Variance Shrinkage & KL Divergence
            var_kl_res = VarianceShrinkageKLScanner.analyze_variance_and_kl(
                site_values_by_var[site_id], pooled_means_dict, pooled_vars_dict
            )

            # E. Anomaly Flags & Composite Scoring
            flags: list[str] = []

            # Benford flag
            if (
                benford_res["conformity"] == BenfordConformity.NON_CONFORMING
                or benford_res["p_value"] < 0.001
            ):
                flags.append(ForensicsAnomalyFlag.BENFORD_NON_CONFORMANCE)

            # Rounding bias flag
            if (
                terminal_res["rounding_bias_tier"] == RoundingBiasTier.SEVERE_ROUNDING
                or terminal_res["p_value"] < 0.001
            ):
                flags.append(ForensicsAnomalyFlag.TERMINAL_DIGIT_ROUNDING_BIAS)

            # Mahalanobis outlier flag
            if p_mahalanobis < 0.001 or d_m >= 4.0:
                flags.append(ForensicsAnomalyFlag.MULTIVARIATE_MAHALANOBIS_OUTLIER)

            # Variance shrinkage flag
            if var_kl_res["is_variance_shrunk"] or var_kl_res["p_value"] < 0.001:
                flags.append(ForensicsAnomalyFlag.ANOMALOUS_VARIANCE_SHRINKAGE)

            # Suspicion of data fabrication if multiple sentinel triggers or high KL divergence
            if len(flags) >= 2 or (
                var_kl_res["is_variance_shrunk"] and benford_res["p_value"] < 0.01
            ):
                flags.append(ForensicsAnomalyFlag.DATA_FABRICATION_SUSPICION)

            # Composite Risk Score Calculation (0.0 - 100.0)
            score = 0.0
            # Benford contribution
            if benford_res["p_value"] < 0.001:
                score += 30.0
            elif benford_res["p_value"] < 0.01:
                score += 15.0
            elif benford_res["conformity"] != BenfordConformity.CLOSE_CONFORMITY:
                score += 5.0

            # Rounding contribution
            if terminal_res["rounding_bias_tier"] == RoundingBiasTier.SEVERE_ROUNDING:
                score += 25.0
            elif (
                terminal_res["rounding_bias_tier"] == RoundingBiasTier.MODERATE_ROUNDING
            ):
                score += 10.0

            # Mahalanobis contribution
            if p_mahalanobis < 0.001:
                score += 25.0
            elif p_mahalanobis < 0.01:
                score += 12.0

            # Variance shrinkage contribution
            if var_kl_res["is_variance_shrunk"]:
                score += 30.0
            elif var_kl_res["p_value"] < 0.05:
                score += 10.0

            composite_score = min(100.0, round(score, 1))

            # Risk Tier & Outlier classification (P < 0.001 threshold for strict anomaly flag)
            has_critical_p = (
                (
                    benford_res["p_value"] < 0.001
                    and benford_res["conformity"] == BenfordConformity.NON_CONFORMING
                )
                or (
                    terminal_res["p_value"] < 0.001
                    and terminal_res["rounding_bias_tier"]
                    == RoundingBiasTier.SEVERE_ROUNDING
                )
                or p_mahalanobis < 0.001
                or (var_kl_res["is_variance_shrunk"] and var_kl_res["p_value"] < 0.001)
            )
            is_outlier = has_critical_p or composite_score >= 60.0

            if composite_score >= 60.0 or has_critical_p:
                risk_tier = RiskTier.CRITICAL
                anomalous_count += 1
            elif composite_score >= 35.0:
                risk_tier = RiskTier.HIGH
            elif composite_score >= 15.0:
                risk_tier = RiskTier.MEDIUM
            else:
                risk_tier = RiskTier.LOW

            # F. Persist SiteForensicsRecord
            site_means_map = {
                target_variables[i]: round(site_means[i], 2)
                for i in range(len(target_variables))
            }
            site_cov_map = {
                target_variables[i]: {
                    target_variables[j]: round(site_cov[i][j], 2)
                    for j in range(len(target_variables))
                }
                for i in range(len(target_variables))
            }

            site_record = self.repo.create_site_forensics_record_entity(
                run_id=run.id,
                study_id=study_id,
                site_id=site_id,
                sample_size=n_site,
                benford_chi_square=benford_res["chi_square"],
                benford_p_value=benford_res["p_value"],
                benford_mad=benford_res["mad"],
                benford_conformity=benford_res["conformity"],
                benford_leading_digits=benford_res["observed_frequencies"],
                terminal_digit_chi_square=terminal_res["chi_square"],
                terminal_digit_p_value=terminal_res["p_value"],
                terminal_digit_zero_five_index=terminal_res["zero_five_index"],
                terminal_digit_distribution=terminal_res["observed_frequencies"],
                rounding_bias_tier=terminal_res["rounding_bias_tier"],
                mahalanobis_distance=d_m,
                mahalanobis_p_value=p_mahalanobis,
                correlation_matrix_distance=corr_dist,
                site_mean_vector=site_means_map,
                site_covariance_matrix=site_cov_map,
                variance_ratio=var_kl_res["variance_ratio"],
                variance_shrinkage_p_value=var_kl_res["p_value"],
                kl_divergence=var_kl_res["kl_divergence"],
                is_variance_shrunk=var_kl_res["is_variance_shrunk"],
                composite_risk_score=composite_score,
                risk_tier=risk_tier,
                is_anomalous_outlier=is_outlier,
                flagged_anomaly_reasons=flags,
                kri_evaluation_id=None,
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason,
            )

            # Auto-trigger KRI breach alert if enabled and anomalous
            if auto_trigger_kri and is_outlier:
                kri_eval = self.repo.create_kri_evaluation_entity(
                    study_id=study_id,
                    site_id=site_id,
                    kri_code="KRI_CSM_FORENSICS",
                    evaluation_date=datetime.now(),
                    raw_value=composite_score,
                    standardized_z_score=round(composite_score / 20.0, 2),
                    risk_tier=RiskTier.CRITICAL
                    if composite_score >= 60.0
                    else RiskTier.HIGH,
                    created_by=user_id,
                    version_index=1,
                    reason_for_change=f"Automated CSM Forensics Sentinel trigger: {', '.join(flags)}",
                )
                await self.repo.save_kri_evaluation(kri_eval)
                site_record.kri_evaluation_id = kri_eval.id

            await self.repo.save_site_forensics_record(site_record)
            site_records.append(site_record)

        run.anomalous_sites_count = anomalous_count
        await self.repo.save_forensics_run(run)

        # 5. Append-only GxP audit log
        audit_log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CSM_FORENSICS_RUN_COMPLETED",
            details=(
                f"Completed statistical forensics surveillance run '{run.id}' for study '{study_id}'. "
                f"Sites analyzed: {len(site_observation_data)}, Anomalous sites flagged: {anomalous_count}."
            ),
            record_id=run.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(audit_log)

        return await self.get_run_details(run.id)

    async def list_runs(self, study_id: str | None = None) -> Sequence[Any]:
        return await self.repo.get_forensics_runs(study_id)

    async def get_run_details(self, run_id: str) -> Any:
        run = await self.repo.get_forensics_run_by_id(run_id)
        if not run:
            raise StatisticalForensicsError(
                f"Forensics run with ID '{run_id}' not found.", status_code=404
            )
        return run

    async def get_site_history(self, site_id: str) -> Sequence[Any]:
        return await self.repo.get_site_forensics_records_by_site(site_id)

    async def trigger_kri_alerts_for_run(
        self,
        run_id: str,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> list[Any]:
        """Dispatches KRI breach events for all anomalous sites in a forensics run."""
        await self.get_run_details(run_id)
        records = await self.repo.get_site_forensics_records_by_run(run_id)

        created_kris = []
        for rec in records:
            if rec.is_anomalous_outlier and not rec.kri_evaluation_id:
                kri_eval = self.repo.create_kri_evaluation_entity(
                    study_id=rec.study_id,
                    site_id=rec.site_id,
                    kri_code="KRI_CSM_FORENSICS",
                    evaluation_date=datetime.now(),
                    raw_value=rec.composite_risk_score,
                    standardized_z_score=round(rec.composite_risk_score / 20.0, 2),
                    risk_tier=rec.risk_tier,
                    created_by=user_id,
                    version_index=1,
                    reason_for_change=change_reason
                    or "Dispatched KRI breach alert from forensics inspection",
                )
                await self.repo.save_kri_evaluation(kri_eval)
                rec.kri_evaluation_id = kri_eval.id
                await self.repo.save_site_forensics_record(rec)
                created_kris.append(kri_eval)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CSM_KRI_ALERTS_DISPATCHED",
            details=f"Dispatched {len(created_kris)} KRI breach alerts for forensics run '{run_id}'.",
            record_id=run_id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return created_kris

    @classmethod
    def generate_simulated_clinical_dataset(
        cls,
        study_id: str = "STUDY-ONC-2026",
        num_normal_sites: int = 4,
        include_benford_anomaly: bool = True,
        include_rounding_bias: bool = True,
        include_variance_shrinkage: bool = True,
        include_multivariate_outlier: bool = True,
        samples_per_site: int = 60,
        seed: int = 42,
    ) -> dict[str, list[dict[str, float]]]:
        """Generates realistic continuous clinical datasets for cross-site forensics calibration."""
        rng = random.Random(seed)
        site_data: dict[str, list[dict[str, float]]] = {}

        # 1. Normal compliant clinical sites
        for i in range(1, num_normal_sites + 1):
            site_id = f"SITE-10{i}"
            obs_list = []
            for _ in range(samples_per_site):
                # Realistic integer systolic & diastolic blood pressure, heart rate, lognormal lab creatinine
                sbp = float(round(rng.gauss(122.0, 12.0)))
                dbp = float(round(0.65 * sbp + rng.gauss(0.0, 5.0)))
                hr = float(round(rng.gauss(72.0, 8.5)))
                creat = float(round(math.exp(rng.gauss(2.0, 1.6)), 2))
                obs_list.append(
                    {
                        "systolic_bp": sbp,
                        "diastolic_bp": dbp,
                        "heart_rate": hr,
                        "lab_creatinine": creat,
                    }
                )
            site_data[site_id] = obs_list

        # 2. Fabricated site with non-conforming Benford's Law (e.g. uniform leading digits 7-9)
        if include_benford_anomaly:
            site_id = "SITE-BENFORD-ANOMALY"
            obs_list = []
            for _ in range(samples_per_site):
                sbp = float(round(rng.gauss(122.0, 12.0)))
                dbp = float(round(0.65 * sbp + rng.gauss(0.0, 5.0)))
                hr = float(round(rng.gauss(72.0, 8.5)))
                forced_digit = rng.randint(7, 9)
                creat = float(round(forced_digit * 10.0 + rng.uniform(1.0, 9.0), 2))
                obs_list.append(
                    {
                        "systolic_bp": sbp,
                        "diastolic_bp": dbp,
                        "heart_rate": hr,
                        "lab_creatinine": creat,
                    }
                )
            site_data[site_id] = obs_list

        # 3. Rounding bias site (Blood pressure and heart rate rounded to nearest 5 or 10)
        if include_rounding_bias:
            site_id = "SITE-ROUNDING-BIAS"
            obs_list = []
            for _ in range(samples_per_site):
                raw_sbp = rng.gauss(124.0, 10.0)
                raw_dbp = 0.65 * raw_sbp + rng.gauss(0.0, 4.0)
                raw_hr = rng.gauss(74.0, 8.0)
                sbp = float(round(raw_sbp / 5.0) * 5.0)
                dbp = float(round(raw_dbp / 5.0) * 5.0)
                hr = float(round(raw_hr / 5.0) * 5.0)
                creat = float(round(math.exp(rng.gauss(2.0, 1.6)), 2))
                obs_list.append(
                    {
                        "systolic_bp": sbp,
                        "diastolic_bp": dbp,
                        "heart_rate": hr,
                        "lab_creatinine": creat,
                    }
                )
            site_data[site_id] = obs_list

        # 4. Variance shrinkage site (cloned / fabricated low-standard-deviation data)
        if include_variance_shrinkage:
            site_id = "SITE-VARIANCE-SHRINKAGE"
            obs_list = []
            for _ in range(samples_per_site):
                sbp = float(round(rng.gauss(120.0, 0.4)))
                dbp = float(round(rng.gauss(80.0, 0.3)))
                hr = float(round(rng.gauss(72.0, 0.2)))
                creat = float(round(rng.gauss(1.05, 0.01), 2))
                obs_list.append(
                    {
                        "systolic_bp": sbp,
                        "diastolic_bp": dbp,
                        "heart_rate": hr,
                        "lab_creatinine": creat,
                    }
                )
            site_data[site_id] = obs_list

        # 5. Multivariate outlier site (distorted correlation and centroid shift)
        if include_multivariate_outlier:
            site_id = "SITE-MULTIVARIATE-OUTLIER"
            obs_list = []
            for _ in range(samples_per_site):
                sbp = float(round(rng.gauss(175.0, 12.0)))
                dbp = float(round(rng.gauss(120.0, 6.0)))
                hr = float(round(rng.gauss(110.0, 10.0)))
                creat = float(round(rng.gauss(5.5, 0.6), 2))
                obs_list.append(
                    {
                        "systolic_bp": sbp,
                        "diastolic_bp": dbp,
                        "heart_rate": hr,
                        "lab_creatinine": creat,
                    }
                )
            site_data[site_id] = obs_list

        return site_data
