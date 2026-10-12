using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Safety;

public class SignalAlertEndpointsTests
{
    private static SafetyDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<SafetyDbContext>()
            .UseInMemoryDatabase(databaseName: $"SafetyTestDb_{Guid.NewGuid()}")
            .Options;

        return new SafetyDbContext(options);
    }

    private static SafetySignalAlert CreateTestAlert(string id, string status = "TRIGGERED")
    {
        return new SafetySignalAlert
        {
            Id = id,
            TargetDrug = "CADENCE-101",
            MeddraTerm = "Hepatic failure",
            TermLevel = "PT",
            MeddraSoc = "Hepatobiliary disorders",
            CaseCountA = 15,
            ExpectedCountE = 1.0,
            Prr = 15.0,
            PrrCiLower = 7.2,
            PrrCiUpper = 31.4,
            ChiSquareYates = 64.2,
            Ror = 17.47,
            RorCiLower = 8.1,
            RorCiUpper = 37.8,
            Ebgm = 8.0,
            Eb05 = 5.2,
            Eb95 = 12.1,
            Severity = "CRITICAL",
            Status = status,
            TriageSummary = "Disproportionality threshold breach.",
            StudyId = "STUDY-2026",
            CreatedAt = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero),
            CreatedBy = "system",
            ReasonForChange = "Initial Alert Trigger"
        };
    }

    /// <summary>
    /// Validates bulk e-signature disposition of safety signal alerts with 21 CFR Part 11 audit logging per alert ID.
    /// @req:PRD-PVG-003
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-003")]
    public async Task BulkSignSignalAlerts_AppliesEsignatureAndCreatesAuditTrailPerAlert()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

        var alert1 = CreateTestAlert("alt-001", "TRIGGERED");
        var alert2 = CreateTestAlert("alt-002", "UNDER_REVIEW");
        dbContext.SafetySignalAlerts.AddRange(alert1, alert2);
        await dbContext.SaveChangesAsync(cancellationToken);

        var request = new SafetySignalAlertBulkSignRequest(
            AlertIds: new List<string> { "alt-001", "alt-002" },
            UserId: "dr.jenkins",
            SignatureSecret: "valid-secret-key-123",
            Decision: "REFUTE_SIGNAL",
            Justification: "Known background incidence verified against historical safety dataset.",
            MeaningOfSignature: "Approval of Signal Review Assessment & Disposition"
        );

        // Act
        var result = await (Task<IResult>)typeof(SignalAlertEndpoints)
            .GetMethod("BulkSignSignalAlertsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { request, dbContext, fakeClock, cancellationToken })!;

        // Assert
        result.Should().BeOfType<Ok<SafetySignalAlertBulkSignResponse>>();
        var okResult = (Ok<SafetySignalAlertBulkSignResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.UpdatedCount.Should().Be(2);
        okResult.Value.Status.Should().Be("REFUTED");
        okResult.Value.EsignatureManifestId.Should().StartWith("SIG-PVG-");

        // Verify entity updates in DB
        var updated1 = await dbContext.SafetySignalAlerts.FindAsync(new object[] { "alt-001" }, cancellationToken);
        var updated2 = await dbContext.SafetySignalAlerts.FindAsync(new object[] { "alt-002" }, cancellationToken);

        updated1!.Status.Should().Be("REFUTED");
        updated1.ReviewDecision.Should().Be("REFUTE_SIGNAL");
        updated1.ReviewedBy.Should().Be("dr.jenkins");

        updated2!.Status.Should().Be("REFUTED");
        updated2.ReviewDecision.Should().Be("REFUTE_SIGNAL");
        updated2.ReviewedBy.Should().Be("dr.jenkins");

        // Verify audit log entries
        var auditLogs = await dbContext.SafetyAuditLogs.ToListAsync(cancellationToken);
        auditLogs.Should().HaveCount(2);
        auditLogs.Select(a => a.RecordId).Should().Contain(new[] { "alt-001", "alt-002" });
        auditLogs.All(a => a.Action == "BULK_SIGNAL_ALERT_SIGNATURE").Should().BeTrue();
    }

    /// <summary>
    /// Validates that invalid Part 11 credentials reject the bulk signature transaction with 401 Unauthorized without mutating database state.
    /// @req:PRD-PVG-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-008")]
    public async Task BulkSignSignalAlerts_RejectsInvalidCredentialsWith401AndNoMutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

        var alert1 = CreateTestAlert("alt-001", "TRIGGERED");
        dbContext.SafetySignalAlerts.Add(alert1);
        await dbContext.SaveChangesAsync(cancellationToken);

        var request = new SafetySignalAlertBulkSignRequest(
            AlertIds: new List<string> { "alt-001" },
            UserId: "dr.jenkins",
            SignatureSecret: "invalid",
            Decision: "CONFIRM_SIGNAL",
            Justification: "Escalating signal to Safety Board due to disproportionality breach."
        );

        // Act
        var result = await (Task<IResult>)typeof(SignalAlertEndpoints)
            .GetMethod("BulkSignSignalAlertsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { request, dbContext, fakeClock, cancellationToken })!;

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        // Verify DB unchanged
        var alertInDb = await dbContext.SafetySignalAlerts.FindAsync(new object[] { "alt-001" }, cancellationToken);
        alertInDb!.Status.Should().Be("TRIGGERED");
        alertInDb.ReviewDecision.Should().BeNull();

        var auditLogs = await dbContext.SafetyAuditLogs.ToListAsync(cancellationToken);
        auditLogs.Should().BeEmpty();
    }

    /// <summary>
    /// Validates state conflict handling (e.g. non-existent or already closed alert) returning 409 Conflict with transaction rollback.
    /// @req:PRD-PVG-003
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-003")]
    public async Task BulkSignSignalAlerts_DetectsStateConflictAndReturns409Conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

        var alert1 = CreateTestAlert("alt-001", "TRIGGERED");
        var alert2 = CreateTestAlert("alt-002", "CLOSED"); // Already closed alert
        dbContext.SafetySignalAlerts.AddRange(alert1, alert2);
        await dbContext.SaveChangesAsync(cancellationToken);

        var request = new SafetySignalAlertBulkSignRequest(
            AlertIds: new List<string> { "alt-001", "alt-002" },
            UserId: "dr.jenkins",
            SignatureSecret: "valid-secret-key-123",
            Decision: "CONFIRM_SIGNAL",
            Justification: "Confirming safety signal across selected alerts."
        );

        // Act
        var result = await (Task<IResult>)typeof(SignalAlertEndpoints)
            .GetMethod("BulkSignSignalAlertsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { request, dbContext, fakeClock, cancellationToken })!;

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        // Verify alert1 was NOT updated (transaction rolled back)
        var alert1InDb = await dbContext.SafetySignalAlerts.FindAsync(new object[] { "alt-001" }, cancellationToken);
        alert1InDb!.Status.Should().Be("TRIGGERED");
    }

    /// <summary>
    /// Validates batch size limit enforcing a max limit of 100 alerts per request.
    /// @req:PRD-PVG-003
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-003")]
    public async Task BulkSignSignalAlerts_EnforcesMaxBatchLimitOf100Alerts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

        var ids = Enumerable.Range(1, 101).Select(i => $"alt-{i:D3}").ToList();

        var request = new SafetySignalAlertBulkSignRequest(
            AlertIds: ids,
            UserId: "dr.jenkins",
            SignatureSecret: "valid-secret-key-123",
            Decision: "REFUTE_SIGNAL",
            Justification: "Refuting batch of alerts."
        );

        // Act
        var result = await (Task<IResult>)typeof(SignalAlertEndpoints)
            .GetMethod("BulkSignSignalAlertsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { request, dbContext, fakeClock, cancellationToken })!;

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Detail.Should().Contain("100");
    }
}
