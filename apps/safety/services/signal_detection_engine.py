"""Vectorized Disproportionality Safety Signal Detection Engine (PRR, ROR, EBGM / MGPS).

Requirements: PRD-PVG-003, PRD-SYS-001, ADR-2199
(CIOMS VIII, FDA FAERS / EMA EudraVigilance Disproportionality Guidelines)
"""

import math
from typing import Any

from apps.safety.domain.signal_models import (
    BenchmarkReferenceDataset,
    DisproportionalityContingency,
    DisproportionalityMetricResult,
    MGPSMultiItemResult,
    SignalStrength,
    StratificationLevel,
)


class SafetySignalDetectionEngine:
    """Disproportionality analysis engine implementing PRR, ROR, Yates' Chi-Square, and Empirical Bayes MGPS."""

    @classmethod
    def calculate_metrics(
        cls,
        contingency: DisproportionalityContingency,
    ) -> DisproportionalityMetricResult:
        """Calculates PRR, ROR, Yates' Chi-Square, and EBGM/MGPS metrics for a Drug-Event contingency.

        Args:
            contingency: 2x2 table counts (a, b, c, d) and MedDRA hierarchy info.

        Returns:
            DisproportionalityMetricResult with calculated indices and regulatory signal classifications.
        """
        a = float(contingency.a)
        b = float(contingency.b)
        c = float(contingency.c)
        d = float(contingency.d)
        n = a + b + c + d

        if n <= 0.0:
            raise ValueError("Total report count n must be greater than zero.")

        # Expected count under null hypothesis: E = (a+b)(a+c) / n
        expected_e = ((a + b) * (a + c)) / n if n > 0 else 0.0

        # --- 1. Proportional Reporting Ratio (PRR) ---
        if (a + b) > 0 and (c + d) > 0 and c > 0:
            prr = (a / (a + b)) / (c / (c + d))
            # Standard error of ln(PRR) with zero-cell guard
            var_ln_prr = (
                (1.0 / max(a, 0.5))
                - (1.0 / (a + b))
                + (1.0 / max(c, 0.5))
                - (1.0 / (c + d))
            )
            se_prr = math.sqrt(max(var_ln_prr, 0.0))
            prr_lower = math.exp(math.log(max(prr, 1e-6)) - 1.96 * se_prr)
            prr_upper = math.exp(math.log(max(prr, 1e-6)) + 1.96 * se_prr)
        elif (a + b) > 0 and a > 0 and (c + d) > 0 and c == 0:
            # Haldane-Anscombe 0.5 continuity correction for zero background cell
            adj_a = a + 0.5
            adj_b = b + 0.5
            adj_c = 0.5
            adj_d = d + 0.5
            prr = (adj_a / (adj_a + adj_b)) / (adj_c / (adj_c + adj_d))
            var_ln_prr = (
                (1.0 / adj_a)
                - (1.0 / (adj_a + adj_b))
                + (1.0 / adj_c)
                - (1.0 / (adj_c + adj_d))
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
        if b > 0 and c > 0 and d > 0 and a > 0:
            ror = (a * d) / (b * c)
            var_ln_ror = (1.0 / a) + (1.0 / b) + (1.0 / c) + (1.0 / d)
            se_ror = math.sqrt(var_ln_ror)
            ror_lower = math.exp(math.log(max(ror, 1e-6)) - 1.96 * se_ror)
            ror_upper = math.exp(math.log(max(ror, 1e-6)) + 1.96 * se_ror)
        elif (b + c + d) > 0 and a > 0:
            # Continuity corrected ROR
            adj_a = a + 0.5
            adj_b = max(b, 0.5)
            adj_c = max(c, 0.5)
            adj_d = max(d, 0.5)
            ror = (adj_a * adj_d) / (adj_b * adj_c)
            var_ln_ror = (1.0 / adj_a) + (1.0 / adj_b) + (1.0 / adj_c) + (1.0 / adj_d)
            se_ror = math.sqrt(var_ln_ror)
            ror_lower = math.exp(math.log(max(ror, 1e-6)) - 1.96 * se_ror)
            ror_upper = math.exp(math.log(max(ror, 1e-6)) + 1.96 * se_ror)
        else:
            ror = 0.0
            ror_lower = 0.0
            ror_upper = 0.0

        # --- 4. Multi-Item Gamma Poisson Shrinker (MGPS / EBGM) ---
        # Empirical Bayes shrinkage with prior hyperparameters alpha0=1.0, beta0=1.0
        alpha0 = 1.0
        beta0 = 1.0
        ebgm = (a + alpha0) / (expected_e + beta0) if (expected_e + beta0) > 0 else 0.0
        # 5th percentile lower bound: EB05 = EBGM * exp(-1.645 * 1 / sqrt(a))
        eb05 = (
            ebgm * math.exp(-1.645 * (1.0 / math.sqrt(a)))
            if a > 0
            else (ebgm * math.exp(-1.645 * (1.0 / math.sqrt(alpha0))))
        )
        # 95th percentile upper bound: EB95 = EBGM * exp(+1.645 * 1 / sqrt(a))
        eb95 = (
            ebgm * math.exp(+1.645 * (1.0 / math.sqrt(a)))
            if a > 0
            else (ebgm * math.exp(+1.645 * (1.0 / math.sqrt(alpha0))))
        )

        # --- 5. Regulatory Signal Criteria & Consensus ---
        # Evans / MHRA Criteria: a >= 3, PRR >= 2.0, ChiSquare >= 4.0
        is_prr_signal = (contingency.a >= 3) and (prr >= 2.0) and (chi_square >= 4.0)

        # ROR Criteria: a >= 3, ROR_lower > 1.0
        is_ror_signal = (contingency.a >= 3) and (ror_lower > 1.0)

        # FDA / MGPS Criteria: a >= 3, EB05 >= 2.0
        is_ebgm_signal = (contingency.a >= 3) and (eb05 >= 2.0)

        overall_signal = is_prr_signal or is_ror_signal or is_ebgm_signal

        # Determine signal strength classification
        if is_prr_signal and is_ror_signal and is_ebgm_signal and prr >= 4.0:
            signal_strength = SignalStrength.CRITICAL
        elif (is_prr_signal and is_ror_signal) or is_ebgm_signal:
            signal_strength = SignalStrength.STRONG
        elif is_prr_signal or is_ror_signal:
            signal_strength = SignalStrength.MODERATE
        elif contingency.a >= 3 and (prr >= 1.5 or ror >= 1.5):
            signal_strength = SignalStrength.WEAK
        else:
            signal_strength = SignalStrength.NONE

        return DisproportionalityMetricResult(
            drug_name=contingency.drug_name,
            meddra_pt=contingency.meddra_pt,
            meddra_soc=contingency.meddra_soc,
            term_level=contingency.term_level,
            case_count_a=contingency.a,
            case_count_b=contingency.b,
            case_count_c=contingency.c,
            case_count_d=contingency.d,
            total_n=int(n),
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
            eb95=round(eb95, 4),
            is_prr_signal=is_prr_signal,
            is_ror_signal=is_ror_signal,
            is_ebgm_signal=is_ebgm_signal,
            overall_signal_flag=overall_signal,
            signal_strength=signal_strength,
            stratum=contingency.stratum,
            benchmark_dataset=contingency.benchmark_dataset,
        )

    @classmethod
    def calculate_mgps_multi_item(
        cls,
        combinations: list[dict[str, Any]],
        total_reports: int = 1000,
    ) -> list[MGPSMultiItemResult]:
        """Calculates multi-item interaction shrinkage for drug-drug-event or polypharmacy tuples.

        Args:
            combinations: List of dicts with keys {'items': list[str], 'observed_n': int, 'expected_e': float}.
            total_reports: Normalization population.

        Returns:
            List of MGPSMultiItemResult.
        """
        results: list[MGPSMultiItemResult] = []
        alpha0 = 1.0
        beta0 = 1.0

        for comb in combinations:
            items = comb.get("items", [])
            obs = int(comb.get("observed_n", 0))
            exp = float(comb.get("expected_e", 1.0))
            if exp <= 0:
                exp = 0.5

            ebgm = (obs + alpha0) / (exp + beta0)
            eb05 = (
                ebgm * math.exp(-1.645 * (1.0 / math.sqrt(max(obs, 1))))
                if obs > 0
                else 0.0
            )
            eb95 = (
                ebgm * math.exp(+1.645 * (1.0 / math.sqrt(max(obs, 1))))
                if obs > 0
                else 0.0
            )

            # Interaction score is ratio of multi-item EBGM over expected marginal baseline
            interaction_score = round(ebgm / max(exp, 0.1), 4)
            is_signal = (obs >= 3) and (eb05 >= 2.0) and (interaction_score > 1.5)

            results.append(
                MGPSMultiItemResult(
                    item_set=items,
                    observed_n=obs,
                    expected_e=round(exp, 2),
                    ebgm=round(ebgm, 4),
                    eb05=round(eb05, 4),
                    eb95=round(eb95, 4),
                    interaction_score=interaction_score,
                    is_interaction_signal=is_signal,
                )
            )

        results.sort(key=lambda x: x.ebgm, reverse=True)
        return results

    @classmethod
    def scan_safety_dataset(
        cls,
        cases: list[dict[str, Any]],
        target_drug: str,
        stratification_levels: list[StratificationLevel] | None = None,
        benchmark_dataset: str = BenchmarkReferenceDataset.INTERNAL_TRIAL_POOL.value,
        benchmark_cases: list[dict[str, Any]] | None = None,
    ) -> list[DisproportionalityMetricResult]:
        """Scans adverse event datasets and computes disproportionality metrics across PT and SOC levels.

        Args:
            cases: List of trial case dicts with keys {'drug_name', 'meddra_pt', 'meddra_soc', 'case_id'}.
            target_drug: Target investigational substance.
            stratification_levels: List of levels to analyze ([PT, SOC] by default).
            benchmark_dataset: Background comparator identifier.
            benchmark_cases: Optional external benchmark cases (e.g. FAERS / EudraVigilance).

        Returns:
            List of DisproportionalityMetricResult sorted by PRR descending.
        """
        if stratification_levels is None:
            stratification_levels = [
                StratificationLevel.PT,
                StratificationLevel.SOC,
            ]

        target_norm = target_drug.strip().upper()

        # Combine internal and external benchmark cases
        all_cases = list(cases)
        if benchmark_cases:
            all_cases.extend(benchmark_cases)

        results: list[DisproportionalityMetricResult] = []

        for level in stratification_levels:
            term_key = "meddra_pt" if level == StratificationLevel.PT else "meddra_soc"
            all_terms: set[str] = set()
            term_soc_map: dict[str, str] = {}
            drug_term_counts: dict[str, int] = {}
            drug_total: int = 0
            other_term_counts: dict[str, int] = {}
            other_total: int = 0

            for c in all_cases:
                d = str(c.get("drug_name", "")).strip().upper()
                term = str(c.get(term_key, "")).strip()
                soc = str(c.get("meddra_soc", "")).strip() or None

                if not term:
                    # Fallback if meddra_soc is requested but not in record, use meddra_pt as fallback
                    if level == StratificationLevel.SOC:
                        term = str(c.get("meddra_pt", "")).strip()
                    else:
                        continue

                if not term:
                    continue

                all_terms.add(term)
                if soc:
                    term_soc_map[term] = soc

                if d == target_norm:
                    drug_term_counts[term] = drug_term_counts.get(term, 0) + 1
                    drug_total += 1
                else:
                    other_term_counts[term] = other_term_counts.get(term, 0) + 1
                    other_total += 1

            for term in all_terms:
                a = drug_term_counts.get(term, 0)
                b = max(drug_total - a, 0)
                c = other_term_counts.get(term, 0)
                d = max(other_total - c, 0)

                if (a + b + c + d) == 0:
                    continue

                contingency = DisproportionalityContingency(
                    drug_name=target_drug,
                    meddra_pt=term,
                    meddra_soc=term_soc_map.get(term),
                    term_level=level,
                    a=a,
                    b=b,
                    c=c,
                    d=d,
                    benchmark_dataset=benchmark_dataset,
                )
                res = cls.calculate_metrics(contingency)
                results.append(res)

        results.sort(key=lambda r: (r.overall_signal_flag, r.prr), reverse=True)
        return results
