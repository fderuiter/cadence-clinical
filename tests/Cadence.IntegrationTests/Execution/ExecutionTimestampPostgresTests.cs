using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// The times Execution stamps from <see cref="Cadence.Kernel.Time.IClock"/> (query resolution) save to and read back
/// from real PostgreSQL <c>timestamptz</c> columns unchanged (#4784). Npgsql refuses a <see cref="DateTime"/> that is
/// not UTC, so before this fix the query-close write failed on PostgreSQL. Skipped unless a
/// PostgreSQL source is configured (see <see cref="ExecutionPostgresFixture"/>).
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
        var study = new StudyCode($"STUDY-TIME-{Guid.NewGuid():N}");
        var subject = new SubjectCode("SUBJ-TIME-1");

        var query = ClinicalQuery.Raise(study, subject, new TestCode("SYSBP"), "Out of range", QueryStatus.Candidate).Value!;
        query.Close("cra-1", clock.UtcNow).IsSuccess.Should().BeTrue();

        await using (var write = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!))
        {
            write.ClinicalQueries.Add(query);
            await write.SaveChangesAsync(ct);
        }

        await using var read = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!);
        var storedQuery = await read.ClinicalQueries.AsNoTracking().SingleAsync(q => q.Id == query.Id, ct);
        storedQuery.ResolvedAt.Should().Be(clock.UtcNow);
    }
}
