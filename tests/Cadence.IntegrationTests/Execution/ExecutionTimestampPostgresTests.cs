using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// The times Execution stamps from <see cref="Cadence.Kernel.Time.IClock"/> (a toxicity review's decision time) save
/// to and read back from real PostgreSQL <c>timestamptz</c> columns unchanged (#4784). Npgsql refuses a
/// <see cref="DateTime"/> that is not UTC, so a clock-stamped write that is not UTC fails on PostgreSQL. Skipped unless
/// a PostgreSQL source is configured (see <see cref="ExecutionPostgresFixture"/>).
/// </summary>
public sealed class ExecutionTimestampPostgresTests(ExecutionPostgresFixture fixture) : IClassFixture<ExecutionPostgresFixture>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ClockStampedTimes_RoundTripThroughTimestamptz()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock();

        var review = StagedAdverseEventReview.Create(
            studyId: new StudyCode($"STUDY-TIME-{Guid.NewGuid():N}"),
            subjectId: new SubjectCode("SUBJ-TIME-1"),
            testCode: new TestCode("ALT"),
            testName: "Alanine Aminotransferase",
            labValue: 250.0,
            unit: "U/L",
            ctcaeGrade: 3,
            toxicityTerm: "ALT increased",
            clinicalDescription: "Grade 3 ALT elevation",
            isHysLawWarning: false,
            isDeltaCheckAlert: false,
            deltaPercentage: null);
        review.Confirm("pi-1", "Confirmed", "Confirmed Grade 3 AE by PI", clock.UtcNow);

        await using (var write = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!))
        {
            write.StagedAdverseEventReviews.Add(review);
            await write.SaveChangesAsync(ct);
        }

        await using var read = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!);
        var storedReview = await read.StagedAdverseEventReviews.AsNoTracking().SingleAsync(r => r.Id == review.Id, ct);
        storedReview.ReviewedAt.Should().Be(clock.UtcNow);
    }
}
