using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Safety;

public class SignalDetectionEngineTests
{
    private static SafetyDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<SafetyDbContext>()
            .UseInMemoryDatabase(databaseName: $"SafetyTestDb_{Guid.NewGuid():N}")
            .Options;

        return new SafetyDbContext(options);
    }

    [Fact]
    [GxPRequirement("PRD-PVG-003")]
    public void CalculateMetrics_ComputesCorrect2x2Matrix_PRR_Yates_ROR_EBGM()
    {
        // Arrange
        int a = 10;
        int b = 90;
        int c = 2;
        int d = 98;

        // Act
        var result = SignalDetectionEngine.CalculateMetrics(
            a, b, c, d,
            drugName: "INVESTIGATIONAL_COMPOUND",
            meddraPt: "Hepatotoxicity",
            meddraSoc: "Hepatobiliary disorders",
            termLevel: "PT");

        // Assert
        result.CaseCountA.Should().Be(10);
        result.CaseCountB.Should().Be(90);
        result.CaseCountC.Should().Be(2);
        result.CaseCountD.Should().Be(98);
        result.TotalN.Should().Be(200);

        // PRR = (10/100) / (2/100) = 5.0
        result.Prr.Should().Be(5.0);
        result.PrrCiLower.Should().BeGreaterThan(0);
        result.PrrCiUpper.Should().BeGreaterThan(result.PrrCiLower);

        // Yates' Chi-square calculation:
        // |10*98 - 90*2| - 200/2 = |980 - 180| - 100 = 700
        // Denom = 100 * 100 * 12 * 188 = 22,560,000
        // Chi2 = 200 * (700)^2 / 22,560,000 = 98,000,000 / 22,560,000 = 4.34397...
        result.ChiSquareYates.Should().BeInRange(4.3, 4.4);

        // ROR = (10*98) / (90*2) = 980 / 180 = 5.4444
        result.Ror.Should().BeInRange(5.4, 5.5);
        result.RorCiLower.Should().BeGreaterThan(0);
        result.RorCiUpper.Should().BeGreaterThan(result.RorCiLower);

        // EBGM Bayesian shrinkage
        result.Ebgm.Should().BeGreaterThan(0);
        result.Eb05.Should().BeGreaterThan(0);
        result.Eb95.Should().BeGreaterThan(result.Eb05);

        result.IsPrrSignal.Should().BeTrue();
        result.OverallSignalFlag.Should().BeTrue();
        result.SignalStrength.Should().Be("CRITICAL");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-169")]
    public async Task ExecuteScanAsync_PersistsRunAndTriggersAlerts_OnThresholdBreach()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));
        var engine = new SignalDetectionEngine(dbContext, clock);
        var studyId = StudyId.New();

        var request = new SignalDetectionScanRequest
        {
            StudyId = studyId,
            TargetDrug = "COMPOUND-X",
            BenchmarkDataset = "INTERNAL_TRIAL_POOL",
            AutoTriggerAlerts = true,
            MinCaseCount = 3,
            PrrThreshold = 2.0,
            ChiSquareThreshold = 3.0,
            Cases = new List<CaseReactionInput>
            {
                new() { CaseId = "C001", StudyId = studyId, DrugName = "COMPOUND-X", MeddraPt = "Acute Renal Failure", MeddraSoc = "Renal disorders" },
                new() { CaseId = "C002", StudyId = studyId, DrugName = "COMPOUND-X", MeddraPt = "Acute Renal Failure", MeddraSoc = "Renal disorders" },
                new() { CaseId = "C003", StudyId = studyId, DrugName = "COMPOUND-X", MeddraPt = "Acute Renal Failure", MeddraSoc = "Renal disorders" },
                new() { CaseId = "C004", StudyId = studyId, DrugName = "COMPOUND-X", MeddraPt = "Nausea", MeddraSoc = "Gastrointestinal disorders" },
                new() { CaseId = "C005", StudyId = studyId, DrugName = "PLACEBO", MeddraPt = "Nausea", MeddraSoc = "Gastrointestinal disorders" },
                new() { CaseId = "C006", StudyId = studyId, DrugName = "PLACEBO", MeddraPt = "Headache", MeddraSoc = "Nervous system disorders" },
                new() { CaseId = "C007", StudyId = studyId, DrugName = "PLACEBO", MeddraPt = "Headache", MeddraSoc = "Nervous system disorders" },
                new() { CaseId = "C008", StudyId = studyId, DrugName = "PLACEBO", MeddraPt = "Headache", MeddraSoc = "Nervous system disorders" },
                new() { CaseId = "C009", StudyId = studyId, DrugName = "PLACEBO", MeddraPt = "Headache", MeddraSoc = "Nervous system disorders" },
                new() { CaseId = "C010", StudyId = studyId, DrugName = "PLACEBO", MeddraPt = "Headache", MeddraSoc = "Nervous system disorders" }
            }
        };

        // Act
        var response = await engine.ExecuteScanAsync(request, "safety-officer-1", TestContext.Current.CancellationToken);

        // Assert
        response.Should().NotBeNull();
        response.RunId.Should().NotBeNullOrEmpty();
        response.TotalCasesAnalyzed.Should().Be(10);
        response.SignalsDetectedCount.Should().BeGreaterThan(0);
        response.AlertsTriggeredCount.Should().BeGreaterThan(0);

        var dbRun = await dbContext.SafetySignalDetectionRuns.FirstOrDefaultAsync(r => r.Id == response.RunId, TestContext.Current.CancellationToken);
        dbRun.Should().NotBeNull();
        dbRun!.TargetDrug.Should().Be("COMPOUND-X");

        var dbAlerts = await dbContext.SafetySignalAlerts.Where(a => a.DetectionRunId == response.RunId).ToListAsync(TestContext.Current.CancellationToken);
        dbAlerts.Should().NotBeEmpty();
        dbAlerts.Should().Contain(a => a.MeddraTerm == "Acute Renal Failure" && a.Status == SafetySignalAlertStatus.Triggered);
    }

    [Fact]
    [GxPRequirement("PRD-PVG-008")]
    public async Task ReviewAlertAsync_EnforcesPart11Esignature_OnConfirmedSignalAndClosed()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));

        var alert = new SafetySignalAlert
        {
            Id = "ALERT-100",
            TargetDrug = "DRUG-A",
            MeddraTerm = "Anaphylaxis",
            TermLevel = "PT",
            CaseCountA = 5,
            Prr = 3.5,
            ChiSquareYates = 6.2,
            Ror = 4.1,
            Ebgm = 2.8,
            Status = SafetySignalAlertStatus.Triggered,
            CreatedAt = clock.UtcNow,
            CreatedBy = "system"
        };
        dbContext.SafetySignalAlerts.Add(alert);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var httpContext = new DefaultHttpContext();

        // Act 1: Review attempting CONFIRM_SIGNAL without e-signature manifest -> Expect 422 Unprocessable Entity
        var reviewWithoutSig = new SafetySignalAlertReviewRequest
        {
            Decision = SafetySignalReviewDecision.ConfirmSignal,
            Justification = "Confirmed based on clinical trial data."
        };

        var result1 = await SignalEndpoints.ReviewAlertAsync("ALERT-100", reviewWithoutSig, dbContext, clock, httpContext, TestContext.Current.CancellationToken);
        result1.Should().BeOfType<ProblemHttpResult>();
        var problem1 = (ProblemHttpResult)result1;
        problem1.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        // Act 2: Review attempting CONFIRM_SIGNAL with e-signature manifest -> Expect 200 OK and status updated to CONFIRMED_SIGNAL
        var reviewWithSig = new SafetySignalAlertReviewRequest
        {
            Decision = SafetySignalReviewDecision.ConfirmSignal,
            Justification = "Confirmed based on clinical trial data.",
            EsignatureManifestId = "SIG-MANIFEST-999"
        };

        var result2 = await SignalEndpoints.ReviewAlertAsync("ALERT-100", reviewWithSig, dbContext, clock, httpContext, TestContext.Current.CancellationToken);
        result2.Should().BeOfType<Ok<SafetySignalAlertDto>>();
        var okResult = (Ok<SafetySignalAlertDto>)result2;
        okResult.Value!.Status.Should().Be(SafetySignalAlertStatus.ConfirmedSignal);
        okResult.Value.EsignatureManifestId.Should().Be("SIG-MANIFEST-999");
    }

    [Fact]
    [GxPRequirement("PRD-PVG-008")]
    public async Task SignAlertAsync_AppliesPart11Esignature_AndUpdatesStatus()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));

        var alert = new SafetySignalAlert
        {
            Id = "ALERT-200",
            TargetDrug = "DRUG-B",
            MeddraTerm = "Arrhythmia",
            TermLevel = "PT",
            CaseCountA = 4,
            Prr = 2.8,
            ChiSquareYates = 5.1,
            Ror = 3.2,
            Ebgm = 2.1,
            Status = SafetySignalAlertStatus.Triggered,
            CreatedAt = clock.UtcNow,
            CreatedBy = "system"
        };
        dbContext.SafetySignalAlerts.Add(alert);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var httpContext = new DefaultHttpContext();

        var signRequest = new SafetySignalAlertSignRequest
        {
            UserId = "physician-dr-smith",
            SignatureSecret = "secret123",
            Decision = SafetySignalReviewDecision.CloseAlert,
            Justification = "Reviewed and determined to be an expected background noise event.",
            MeaningOfSignature = "Final Medical Approval"
        };

        // Act
        var result = await SignalEndpoints.SignAlertAsync("ALERT-200", signRequest, dbContext, clock, httpContext, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeOfType<Ok<SafetySignalAlertSignResponse>>();
        var okResult = (Ok<SafetySignalAlertSignResponse>)result;
        okResult.Value!.Status.Should().Be(SafetySignalAlertStatus.Closed);
        okResult.Value.EsignatureManifestId.Should().StartWith("SIG-MANIFEST-");
        okResult.Value.ReviewedBy.Should().Be("physician-dr-smith");

        var updatedAlert = await dbContext.SafetySignalAlerts.FirstOrDefaultAsync(a => a.Id == "ALERT-200", TestContext.Current.CancellationToken);
        updatedAlert!.Status.Should().Be(SafetySignalAlertStatus.Closed);
        updatedAlert.EsignatureManifestId.Should().Be(okResult.Value.EsignatureManifestId);
    }

    [Fact]
    public async Task ExecuteScanAsync_FiltersOutBlindedCases_ToRespectBlindingAirGap()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero));
        var engine = new SignalDetectionEngine(dbContext, clock);

        var request = new SignalDetectionScanRequest
        {
            TargetDrug = "INVESTIGATIONAL_DRUG",
            Cases = new List<CaseReactionInput>
            {
                new() { CaseId = "C1", DrugName = "INVESTIGATIONAL_DRUG", MeddraPt = "Rash", IsBlinded = true },
                new() { CaseId = "C2", DrugName = "INVESTIGATIONAL_DRUG", MeddraPt = "Rash", IsBlinded = false },
                new() { CaseId = "C3", DrugName = "INVESTIGATIONAL_DRUG", MeddraPt = "Rash", IsBlinded = false },
                new() { CaseId = "C4", DrugName = "INVESTIGATIONAL_DRUG", MeddraPt = "Rash", IsBlinded = false },
                new() { CaseId = "C5", DrugName = "PLACEBO", MeddraPt = "Rash", IsBlinded = false }
            }
        };

        // Act
        var response = await engine.ExecuteScanAsync(request, "svc:safety-signal-engine", TestContext.Current.CancellationToken);

        // Assert
        // Case C1 is blinded and must be excluded.
        response.TotalCasesAnalyzed.Should().Be(4);
    }
}
