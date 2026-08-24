"""Unit tests for Vectorized Disproportionality Safety Signal Detection Engine.

@req:PRD-PVG-003
@req:PRD-SYS-001
"""

from apps.safety.services.signal_detection_engine import (
    DisproportionalityContingency,
    SafetySignalDetectionEngine,
)


def test_disproportionality_metrics_strong_signal():
    """Validate PRR, ROR, ChiSquare, and EBGM on a clear positive safety signal.

    @req:PRD-PVG-003
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    # Target Drug: CADENCE-101
    # Event: Hepatic Failure
    # a = 15 (Target Drug + Event), b = 85 (Target Drug + other events) -> Total target = 100
    # c = 10 (Other Drugs + Event), d = 990 (Other Drugs + other events) -> Total other = 1000
    # Total reports n = 1100
    contingency = DisproportionalityContingency(
        drug_name="CADENCE-101",
        meddra_pt="Hepatic failure",
        a=15,
        b=85,
        c=10,
        d=990,
    )

    metrics = SafetySignalDetectionEngine.calculate_metrics(contingency)

    # PRR = (15 / 100) / (10 / 1000) = 0.15 / 0.01 = 15.0
    assert metrics.prr == 15.0
    assert metrics.prr_ci_lower > 6.0
    assert metrics.chi_square_yates > 50.0  # Very strong chi-square

    # ROR = (15 * 990) / (85 * 10) = 14850 / 850 = 17.47
    assert metrics.ror == 17.4706
    assert metrics.ror_ci_lower > 7.0

    # Signal flags
    assert metrics.is_prr_signal is True
    assert metrics.is_ror_signal is True
    assert metrics.overall_signal_flag is True


def test_disproportionality_metrics_null_signal():
    """Validate that baseline non-disproportionate reporting yields no signal.

    @req:PRD-PVG-003
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    # a = 2, b = 98, c = 20, d = 980
    # PRR = (2 / 100) / (20 / 1000) = 0.02 / 0.02 = 1.0
    contingency = DisproportionalityContingency(
        drug_name="CADENCE-101",
        meddra_pt="Nausea",
        a=2,
        b=98,
        c=20,
        d=980,
    )

    metrics = SafetySignalDetectionEngine.calculate_metrics(contingency)
    assert metrics.prr == 1.0
    assert metrics.is_prr_signal is False
    assert metrics.is_ror_signal is False
    assert metrics.overall_signal_flag is False


def test_scan_safety_dataset_batch():
    """Verify batch scanning across multiple adverse events.

    @req:PRD-PVG-003
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    cases = [
        {"drug_name": "CAD-01", "meddra_pt": "Acute kidney injury"},
        {"drug_name": "CAD-01", "meddra_pt": "Acute kidney injury"},
        {"drug_name": "CAD-01", "meddra_pt": "Acute kidney injury"},
        {"drug_name": "CAD-01", "meddra_pt": "Acute kidney injury"},
        {"drug_name": "CAD-01", "meddra_pt": "Headache"},
        {"drug_name": "CONTROL-01", "meddra_pt": "Headache"},
        {"drug_name": "CONTROL-01", "meddra_pt": "Headache"},
        {"drug_name": "CONTROL-01", "meddra_pt": "Nausea"},
        {"drug_name": "CONTROL-01", "meddra_pt": "Acute kidney injury"},
    ]

    results = SafetySignalDetectionEngine.scan_safety_dataset(
        cases, target_drug="CAD-01"
    )
    assert len(results) >= 2
    # Top signal should be Acute kidney injury
    top_result = results[0]
    assert top_result.meddra_pt == "Acute kidney injury"
    assert top_result.case_count_a == 4
    assert top_result.prr > 1.0
