using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Observations;

/// <summary>
/// Custom lab reference ranges survive a restart, apply only to their own study, and are swapped atomically (#5435).
/// </summary>
public sealed class LabRangeStudyScopeTests
{
    private static readonly StudyCode StudyA = new("STUDY-A");
    private static readonly StudyCode StudyB = new("STUDY-B");
    private static readonly TestCode Glucose = new("GLUC");

    // Study A's glucose range is 50-60, so 85 is HIGH there; the standard catalog says 70-99, so 85 is NORMAL.
    private static LabReferenceRange StudyAGlucoseRange() => new(
        id: Guid.NewGuid(),
        studyId: StudyA.Value,
        testCode: "GLUC",
        testName: "Glucose",
        source: "CENTRAL",
        siteId: null,
        unit: "mg/dL",
        normalizedUnit: "mg/dL",
        sexApplicability: "ALL",
        ageLow: null,
        ageHigh: null,
        lowBound: 50.0,
        highBound: 60.0,
        criticalLow: null,
        criticalHigh: null,
        loincCode: "2345-7",
        createdBy: "test",
        reasonForChange: "Study A central lab ranges",
        createdAt: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>
    /// A new service instance (a restart) loads the persisted ranges before its first evaluation, so the custom
    /// range still applies.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-170")]
    public async Task AfterARestart_TheFirstEvaluationUsesThePersistedCustomRanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);

        var services = new ServiceCollection();
        services.AddDbContext<ExecutionDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            await db.Database.EnsureCreatedAsync(ct);
            db.LabReferenceRanges.Add(StudyAGlucoseRange());
            await db.SaveChangesAsync(ct);
        }

        var restarted = new LabRangeService(provider.GetRequiredService<IServiceScopeFactory>());
        await restarted.EnsureLoadedAsync(ct);

        var result = restarted.Evaluate(StudyA, Glucose, 85.0);
        result.Indicator.Should().Be("HIGH");
        result.RangeHigh.Should().Be(60.0);
    }

    /// <summary>
    /// Study A's custom range never applies to study B's observations; B falls back to the standard catalog.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-170")]
    public void OneStudysCustomRange_NeverAppliesToAnotherStudy()
    {
        var service = new LabRangeService();
        service.UpdateActiveRanges([StudyAGlucoseRange()]);

        service.Evaluate(StudyA, Glucose, 85.0).Indicator.Should().Be("HIGH");

        var other = service.Evaluate(StudyB, Glucose, 85.0);
        other.Indicator.Should().Be("NORMAL");
        other.RangeLow.Should().Be(70.0);
        other.RangeHigh.Should().Be(99.0);
    }

    /// <summary>
    /// Evaluation needs a study: without one no custom range can be chosen.
    /// </summary>
    [Fact]
    public void Evaluate_WithoutAStudy_Throws()
    {
        var service = new LabRangeService();

        var act = () => service.Evaluate(default, Glucose, 85.0);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Evaluations running while ranges are replaced always see a complete set, never an empty one.
    /// </summary>
    [Fact]
    public async Task EvaluationDuringAnImport_NeverSeesAnEmptyRangeSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new LabRangeService();
        LabReferenceRange[] ranges = [StudyAGlucoseRange()];
        service.UpdateActiveRanges(ranges);

        using var stop = new CancellationTokenSource();
        var importer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                service.UpdateActiveRanges(ranges);
            }
        }, ct);

        var indicators = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 20_000).Select(_ => service.Evaluate(StudyA, Glucose, 85.0).Indicator).Distinct().ToArray(), ct)));

        await stop.CancelAsync();
        await importer;

        indicators.SelectMany(i => i).Distinct().Should().Equal("HIGH");
    }
}
