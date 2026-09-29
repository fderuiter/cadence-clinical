using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Integration;

/// <summary>
/// Audit entries written through <see cref="GxPAuditInterceptor"/> must still verify after a round trip through
/// PostgreSQL, which stores timestamps to the microsecond (#4768). Skipped unless a PostgreSQL source is
/// configured (see <see cref="ExecutionPostgresFixture"/>).
/// @req:PRD-SYS-053
/// </summary>
public sealed class AuditChainPostgresRoundTripTests(ExecutionPostgresFixture fixture) : IClassFixture<ExecutionPostgresFixture>
{
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task ChainWrittenToPostgres_VerifiesWhenReadBack()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string connectionString = fixture.ConnectionString!;
        string code = $"SUBJ-RT-{System.Guid.NewGuid():N}"[..20];

        await using (var context = ExecutionPostgresFixture.CreateDbContext(connectionString))
        {
            var subject = ClinicalSubject.Create(new SubjectCode(code), new StudyCode("STUDY-RT"), siteCode: null,
                enrollmentIndex: 0, encryptedDemographics: "cipher-1", stratFactors: null);
            context.ClinicalSubjects.Add(subject);
            await context.SaveChangesAsync(ct);

            subject.UpdateDemographics("cipher-2");
            await context.SaveChangesAsync(ct);
        }

        await using var verify = ExecutionPostgresFixture.CreateDbContext(connectionString);
        var entries = await verify.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == nameof(ClinicalSubject))
            .ToListAsync(ct);

        entries.Should().HaveCountGreaterThanOrEqualTo(2);
        AuditLogChainVerifier.Verify(entries).Should().ContainSingle().Which.Valid.Should().BeTrue();
    }
}
