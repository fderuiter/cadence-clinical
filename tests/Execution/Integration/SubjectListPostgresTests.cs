using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Integration;

/// <summary>
/// <see cref="SubjectRepository.ListAsync"/> against real PostgreSQL: the value-object filters and ordering
/// translate to SQL, and paging counts every match (#4828). Skipped unless a PostgreSQL source is configured
/// (see <see cref="ExecutionPostgresFixture"/>).
/// </summary>
public sealed class SubjectListPostgresTests(ExecutionPostgresFixture fixture) : IClassFixture<ExecutionPostgresFixture>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAsync_FiltersBySiteAndStatus_OrdersByEnrollment_AndPages()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var study = new StudyCode($"STUDY-LIST-{Guid.NewGuid():N}");
        var site = new SiteCode("SITE-001");

        await using (var context = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!))
        {
            var repository = new SubjectRepository(context, SaveChangesResiliencePipelineFactory.Create());
            for (int i = 0; i < 4; i++)
            {
                var subject = ClinicalSubject.Create(new SubjectCode($"{study.Value}-S{i}"), study, i < 3 ? site : new SiteCode("SITE-002"),
                    enrollmentIndex: 3 - i, encryptedDemographics: null, stratFactors: null);
                if (i != 1)
                {
                    subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
                }

                await repository.AddAsync(subject, ct);
            }

            await repository.SaveChangesAsync(ct);
        }

        await using var readContext = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!);
        var reader = new SubjectRepository(readContext, SaveChangesResiliencePipelineFactory.Create());

        var enrolledAtSite = await reader.ListAsync(new SubjectListCriteria(study, site, SubjectState.Enrolled, 50, 0), ct);
        enrolledAtSite.Total.Should().Be(2);
        enrolledAtSite.Items.Select(s => s.SubjectCode.Value).Should().Equal($"{study.Value}-S2", $"{study.Value}-S0");

        var secondPage = await reader.ListAsync(new SubjectListCriteria(study, null, null, 2, 2), ct);
        secondPage.Total.Should().Be(4);
        secondPage.Items.Select(s => s.EnrollmentIndex).Should().Equal(2, 3);
    }
}
