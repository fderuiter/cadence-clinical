using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Application;

public class SignalDetectionEngine
{
    private readonly SafetyDbContext _dbContext;
    private readonly IClock _clock;

    public SignalDetectionEngine(SafetyDbContext dbContext, IClock? clock = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? SystemClock.Instance;
    }

    public static DisproportionalityMetricResult CalculateMetrics(
        int a,
        int b,
        int c,
        int d,
        string drugName,
        string meddraPt,
        string? meddraSoc = null,
        string termLevel = "PT",
        double prrThreshold = 2.0,
        double chiSquareThreshold = 4.0,
        double rorCiLowerThreshold = 1.0,
        double eb05Threshold = 2.0,
        int minCaseCount = 3,
        string? benchmarkDataset = "INTERNAL_TRIAL_POOL")
    {
        int totalN = a + b + c + d;
        double expectedCountE = totalN > 0 ? ((double)(a + b) * (a + c)) / totalN : 0.0;

        // 1. Proportional Reporting Ratio (PRR)
        double prr = 0.0;
        double prrCiLower = 0.0;
        double prrCiUpper = 0.0;

        int targetTotal = a + b;
        int controlTotal = c + d;

        if (a > 0 && targetTotal > 0 && controlTotal > 0)
        {
            double cVal = c > 0 ? c : 0.5;
            double pTarget = (double)a / targetTotal;
            double pControl = cVal / controlTotal;
            prr = pTarget / pControl;

            double seLnPrr = Math.Sqrt(
                (1.0 / Math.Max(1, a)) - (1.0 / Math.Max(1, targetTotal)) +
                (1.0 / Math.Max(1, cVal)) - (1.0 / Math.Max(1, controlTotal)));

            double lnPrr = Math.Log(Math.Max(0.0001, prr));
            prrCiLower = Math.Max(0.0, Math.Exp(lnPrr - 1.96 * seLnPrr));
            prrCiUpper = Math.Exp(lnPrr + 1.96 * seLnPrr);
        }

        // 2. Yates' Chi-Square Correction
        double chiSquareYates = 0.0;
        double denom = (double)targetTotal * controlTotal * (a + c) * (b + d);
        if (denom > 0)
        {
            double ad_bc = Math.Abs((double)a * d - (double)b * c);
            double diff = ad_bc - (totalN / 2.0);
            if (diff > 0)
            {
                chiSquareYates = (totalN * diff * diff) / denom;
            }
        }

        // 3. Reporting Odds Ratio (ROR)
        double ror = 0.0;
        double rorCiLower = 0.0;
        double rorCiUpper = 0.0;

        if (b * c > 0 && a > 0 && d > 0)
        {
            ror = ((double)a * d) / ((double)b * c);
        }
        else if (a > 0 || d > 0)
        {
            ror = ((a + 0.5) * (d + 0.5)) / ((b + 0.5) * (c + 0.5));
        }

        if (ror > 0)
        {
            double aAdj = a > 0 ? a : a + 0.5;
            double bAdj = b > 0 ? b : b + 0.5;
            double cAdj = c > 0 ? c : c + 0.5;
            double dAdj = d > 0 ? d : d + 0.5;

            double seLnRor = Math.Sqrt((1.0 / aAdj) + (1.0 / bAdj) + (1.0 / cAdj) + (1.0 / dAdj));
            double lnRor = Math.Log(ror);
            rorCiLower = Math.Max(0.0, Math.Exp(lnRor - 1.96 * seLnRor));
            rorCiUpper = Math.Exp(lnRor + 1.96 * seLnRor);
        }

        // 4. Empirical Bayes Geometric Mean (EBGM) & Shrinkage Scores
        double ebgm = 0.0;
        double eb05 = 0.0;
        double eb95 = 0.0;

        if (expectedCountE > 0)
        {
            ebgm = (a + 0.5) / (expectedCountE + 0.5);
            double seEbgm = 1.645 / Math.Sqrt(a + 0.5);
            double lnEbgm = Math.Log(Math.Max(0.0001, ebgm));
            eb05 = Math.Max(0.0, Math.Exp(lnEbgm - seEbgm));
            eb95 = Math.Exp(lnEbgm + seEbgm);
        }

        // 5. Signal Threshold Evaluations
        bool isPrrSignal = (a >= minCaseCount) && (prr >= prrThreshold) && (chiSquareYates >= chiSquareThreshold);
        bool isRorSignal = (a >= minCaseCount) && (rorCiLower >= rorCiLowerThreshold);
        bool isEbgmSignal = (a >= minCaseCount) && (eb05 >= eb05Threshold);
        bool overallSignalFlag = isPrrSignal || isRorSignal || isEbgmSignal;

        string signalStrength = "NONE";
        if (overallSignalFlag && a >= minCaseCount)
        {
            if (prr >= 5.0 || chiSquareYates >= 15.0 || eb05 >= 4.0)
            {
                signalStrength = "CRITICAL";
            }
            else if (prr >= 3.0 || chiSquareYates >= 8.0 || eb05 >= 2.5)
            {
                signalStrength = "STRONG";
            }
            else if (prr >= 2.0 || chiSquareYates >= 4.0)
            {
                signalStrength = "MODERATE";
            }
            else
            {
                signalStrength = "WEAK";
            }
        }

        return new DisproportionalityMetricResult
        {
            DrugName = drugName,
            MeddraPt = meddraPt,
            MeddraSoc = meddraSoc,
            TermLevel = termLevel,
            CaseCountA = a,
            CaseCountB = b,
            CaseCountC = c,
            CaseCountD = d,
            TotalN = totalN,
            ExpectedCountE = Math.Round(expectedCountE, 4),
            Prr = Math.Round(prr, 4),
            PrrCiLower = Math.Round(prrCiLower, 4),
            PrrCiUpper = Math.Round(prrCiUpper, 4),
            ChiSquareYates = Math.Round(chiSquareYates, 4),
            Ror = Math.Round(ror, 4),
            RorCiLower = Math.Round(rorCiLower, 4),
            RorCiUpper = Math.Round(rorCiUpper, 4),
            Ebgm = Math.Round(ebgm, 4),
            Eb05 = Math.Round(eb05, 4),
            Eb95 = Math.Round(eb95, 4),
            IsPrrSignal = isPrrSignal,
            IsRorSignal = isRorSignal,
            IsEbgmSignal = isEbgmSignal,
            OverallSignalFlag = overallSignalFlag,
            SignalStrength = signalStrength,
            BenchmarkDataset = benchmarkDataset
        };
    }

    public async Task<SignalDetectionScanResponse> ExecuteScanAsync(
        SignalDetectionScanRequest request,
        string callerUserId = "svc:safety-signal-engine",
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // Collect all cases: from request or from SafetyDbContext SafetyCases
        List<CaseReactionInput> allCases = new();

        if (request.Cases != null && request.Cases.Count > 0)
        {
            allCases.AddRange(request.Cases);
        }

        if (request.BenchmarkCases != null && request.BenchmarkCases.Count > 0)
        {
            allCases.AddRange(request.BenchmarkCases);
        }

        // If no cases provided in request, load from database
        if (allCases.Count == 0)
        {
            var dbCases = await _dbContext.SafetyCases
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var dbCase in dbCases)
            {
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(dbCase.CaseDataJson) ? "{}" : dbCase.CaseDataJson);
                    var root = doc.RootElement;

                    string drug = root.TryGetProperty("drug_name", out var drugProp) ? drugProp.GetString() ?? "TARGET_DRUG" : "TARGET_DRUG";
                    string pt = root.TryGetProperty("meddra_pt", out var ptProp) ? ptProp.GetString() ?? "Unknown Event" : "Unknown Event";
                    string? soc = root.TryGetProperty("meddra_soc", out var socProp) ? socProp.GetString() : null;
                    bool isBlinded = root.TryGetProperty("is_blinded", out var blindProp) && blindProp.GetBoolean();

                    allCases.Add(new CaseReactionInput
                    {
                        CaseId = dbCase.Id,
                        StudyId = request.StudyId,
                        DrugName = drug,
                        MeddraPt = pt,
                        MeddraSoc = soc,
                        IsBlinded = isBlinded
                    });
                }
                catch
                {
                    // Fallback if parsing fails
                }
            }
        }

        // Respect blinding air-gap: filter out blinded cases unless unblinded
        var unblindedCases = allCases.Where(c => !c.IsBlinded).ToList();

        // Filter by study if specified
        if (!string.IsNullOrWhiteSpace(request.StudyId))
        {
            var studyCases = unblindedCases.Where(c => string.Equals(c.StudyId, request.StudyId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (studyCases.Count > 0)
            {
                unblindedCases = studyCases;
            }
        }

        string targetDrug = request.TargetDrug;
        var targetCases = unblindedCases
            .Where(c => string.Equals(c.DrugName, targetDrug, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var controlCases = unblindedCases
            .Where(c => !string.Equals(c.DrugName, targetDrug, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Distinct case counts per term
        var results = new List<DisproportionalityMetricResult>();

        var evaluatePts = request.StratificationLevels.Contains("PT", StringComparer.OrdinalIgnoreCase);
        var evaluateSocs = request.StratificationLevels.Contains("SOC", StringComparer.OrdinalIgnoreCase);

        if (evaluatePts)
        {
            var ptTerms = unblindedCases
                .Select(c => c.MeddraPt)
                .Where(pt => !string.IsNullOrWhiteSpace(pt))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var pt in ptTerms)
            {
                var soc = unblindedCases.FirstOrDefault(c => string.Equals(c.MeddraPt, pt, StringComparison.OrdinalIgnoreCase))?.MeddraSoc;

                int a = targetCases.Where(c => string.Equals(c.MeddraPt, pt, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();
                int b = targetCases.Where(c => !string.Equals(c.MeddraPt, pt, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();
                int c = controlCases.Where(c => string.Equals(c.MeddraPt, pt, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();
                int d = controlCases.Where(c => !string.Equals(c.MeddraPt, pt, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();

                var metric = CalculateMetrics(
                    a, b, c, d,
                    targetDrug, pt, soc, "PT",
                    request.PrrThreshold, request.ChiSquareThreshold, request.RorCiLowerThreshold, request.Eb05Threshold,
                    request.MinCaseCount, request.BenchmarkDataset);

                results.Add(metric);
            }
        }

        if (evaluateSocs)
        {
            var socTerms = unblindedCases
                .Select(c => c.MeddraSoc)
                .Where(soc => !string.IsNullOrWhiteSpace(soc))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var soc in socTerms)
            {
                if (string.IsNullOrWhiteSpace(soc)) continue;

                int a = targetCases.Where(c => string.Equals(c.MeddraSoc, soc, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();
                int b = targetCases.Where(c => !string.Equals(c.MeddraSoc, soc, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();
                int c = controlCases.Where(c => string.Equals(c.MeddraSoc, soc, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();
                int d = controlCases.Where(c => !string.Equals(c.MeddraSoc, soc, StringComparison.OrdinalIgnoreCase)).Select(c => c.CaseId).Distinct().Count();

                var metric = CalculateMetrics(
                    a, b, c, d,
                    targetDrug, soc, soc, "SOC",
                    request.PrrThreshold, request.ChiSquareThreshold, request.RorCiLowerThreshold, request.Eb05Threshold,
                    request.MinCaseCount, request.BenchmarkDataset);

                results.Add(metric);
            }
        }

        int totalCasesAnalyzed = unblindedCases.Select(c => c.CaseId).Distinct().Count();
        int signalsDetectedCount = results.Count(r => r.OverallSignalFlag);
        int alertsTriggeredCount = 0;

        // Persist SafetySignalDetectionRun
        var run = new SafetySignalDetectionRun
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = request.StudyId,
            TargetDrug = targetDrug,
            BenchmarkDataset = request.BenchmarkDataset,
            TotalCasesAnalyzed = totalCasesAnalyzed,
            SignalsDetectedCount = signalsDetectedCount,
            SummaryMetricsJson = JsonSerializer.Serialize(results),
            CreatedAt = now,
            CreatedBy = callerUserId,
            ReasonForChange = "Automated vectorized disproportionality signal detection scan run",
            VersionIndex = 1
        };

        _dbContext.SafetySignalDetectionRuns.Add(run);

        // Auto trigger SafetySignalAlerts if requested
        if (request.AutoTriggerAlerts)
        {
            foreach (var result in results.Where(r => r.OverallSignalFlag))
            {
                var alert = new SafetySignalAlert
                {
                    Id = Guid.NewGuid().ToString(),
                    DetectionRunId = run.Id,
                    StudyId = request.StudyId,
                    TargetDrug = targetDrug,
                    MeddraTerm = result.MeddraPt,
                    TermLevel = result.TermLevel,
                    MeddraSoc = result.MeddraSoc,
                    CaseCountA = result.CaseCountA,
                    ExpectedCountE = result.ExpectedCountE,
                    Prr = result.Prr,
                    PrrCiLower = result.PrrCiLower,
                    PrrCiUpper = result.PrrCiUpper,
                    ChiSquareYates = result.ChiSquareYates,
                    Ror = result.Ror,
                    RorCiLower = result.RorCiLower,
                    RorCiUpper = result.RorCiUpper,
                    Ebgm = result.Ebgm,
                    Eb05 = result.Eb05,
                    Eb95 = result.Eb95,
                    Severity = DetermineSeverity(result.SignalStrength),
                    Status = SafetySignalAlertStatus.Triggered,
                    TriageSummary = $"Signal breach for {result.MeddraPt} ({result.TermLevel}): PRR={result.Prr:F2}, Chi2={result.ChiSquareYates:F2}, ROR={result.Ror:F2}, EBGM={result.Ebgm:F2}, N={result.CaseCountA}.",
                    CreatedAt = now,
                    CreatedBy = callerUserId,
                    ReasonForChange = "Automated signal alert threshold breach trigger",
                    VersionIndex = 1
                };

                _dbContext.SafetySignalAlerts.Add(alert);
                alertsTriggeredCount++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SignalDetectionScanResponse
        {
            RunId = run.Id,
            StudyId = request.StudyId,
            TargetDrug = targetDrug,
            BenchmarkDataset = request.BenchmarkDataset,
            TotalCasesAnalyzed = totalCasesAnalyzed,
            TotalTermsEvaluated = results.Count,
            SignalsDetectedCount = signalsDetectedCount,
            AlertsTriggeredCount = alertsTriggeredCount,
            Results = results,
            CreatedAt = now
        };
    }

    private static string DetermineSeverity(string signalStrength)
    {
        if (string.Equals(signalStrength, "CRITICAL", StringComparison.OrdinalIgnoreCase))
        {
            return "CRITICAL";
        }

        if (string.Equals(signalStrength, "STRONG", StringComparison.OrdinalIgnoreCase))
        {
            return "HIGH";
        }

        return "MEDIUM";
    }
}
