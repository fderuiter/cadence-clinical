using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

using FluentAssertions;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

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
            var repository = new SubjectRepository(context);
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
        var reader = new SubjectRepository(readContext);

        var enrolledAtSite = await reader.ListAsync(new SubjectListCriteria(study, site, SubjectState.Enrolled, 50, 0), ct);
        enrolledAtSite.Total.Should().Be(2);
        enrolledAtSite.Items.Select(s => s.SubjectCode.Value).Should().Equal($"{study.Value}-S2", $"{study.Value}-S0");

        var secondPage = await reader.ListAsync(new SubjectListCriteria(study, null, null, 2, 2), ct);
        secondPage.Total.Should().Be(4);
        secondPage.Items.Select(s => s.EnrollmentIndex).Should().Equal(2, 3);

        var filteredByQuery = await reader.ListAsync(new SubjectListCriteria(study, null, null, 50, 0, Query: "-s2"), ct);
        filteredByQuery.Total.Should().Be(1);
        filteredByQuery.Items.Single().SubjectCode.Value.Should().Be($"{study.Value}-S2");

        var studyCounts = await reader.CountByStatusAsync(new SubjectCountCriteria(study), ct);
        studyCounts.Should().Equal(new System.Collections.Generic.Dictionary<SubjectState, int>
        {
            [SubjectState.Screening] = 1,
            [SubjectState.Enrolled] = 3,
        });

        var siteCounts = await reader.CountByStatusAsync(new SubjectCountCriteria(study, SiteCode: site), ct);
        siteCounts.Should().Equal(new System.Collections.Generic.Dictionary<SubjectState, int>
        {
            [SubjectState.Screening] = 1,
            [SubjectState.Enrolled] = 2,
        });

        var allowedSiteCounts = await reader.CountByStatusAsync(
            new SubjectCountCriteria(study, AllowedSites: [new SiteCode("SITE-002")]),
            ct);
        allowedSiteCounts.Should().Equal(new System.Collections.Generic.Dictionary<SubjectState, int>
        {
            [SubjectState.Enrolled] = 1,
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task KernelEnrollmentId_PersistsInPostgres_AndDemoSeedDerivesCanonicalEnrollmentIds()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var study = new StudyCode($"STUDY-ENR-{Guid.NewGuid():N}");
        var site = new SiteCode("SITE-101");
        var enrollmentGuid = Guid.CreateVersion7();

        await using (var context = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!))
        {
            var repository = new SubjectRepository(context);
            var subject = ClinicalSubject.Create(
                new SubjectCode($"{study.Value}-001"),
                study,
                site,
                enrollmentIndex: 0,
                encryptedDemographics: null,
                stratFactors: null,
                kernelEnrollmentId: enrollmentGuid);
            await repository.AddAsync(subject, ct);
            await repository.SaveChangesAsync(ct);
        }

        await using var readContext = ExecutionPostgresFixture.CreateDbContext(fixture.ConnectionString!);
        var reader = new SubjectRepository(readContext);

        var loaded = await reader.GetByKernelEnrollmentIdAsync(enrollmentGuid, ct);
        loaded.Should().NotBeNull();
        loaded!.KernelEnrollmentId.Should().Be(enrollmentGuid);
        loaded.SubjectCode.Value.Should().Be($"{study.Value}-001");

        var demoTenantId = Cadence.Database.Tenancy.DeterministicTenantCodeResolver.ComputeDeterministicGuid("cadence-local");
        var demoParticipantId = Cadence.Apps.Execution.Infrastructure.Persistence.ExecutionDbSeeder.DeriveDemoGuid(demoTenantId, "participant", "SUBJ-101-001");
        var demoEnrollmentId = Cadence.Apps.Execution.Infrastructure.Persistence.ExecutionDbSeeder.DeriveDemoGuid(demoTenantId, "enrollment", "SUBJ-101-001");
        demoParticipantId.Should().NotBe(Guid.Empty).And.NotBe(demoEnrollmentId);
        demoEnrollmentId.ToString("D")[14].Should().Be('8');
    }
}
