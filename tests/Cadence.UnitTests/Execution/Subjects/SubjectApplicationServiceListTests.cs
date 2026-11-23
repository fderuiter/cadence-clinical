using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// <see cref="SubjectApplicationService.ListAsync"/>: study-scoped, filtered, paged in enrollment order, and
/// strict about bad input (#4828).
/// </summary>
public class SubjectApplicationServiceListTests
{
    private readonly InMemorySubjectRepository _subjects = new();

    private SubjectApplicationService CreateService() =>
        new(_subjects, new InMemoryConsentRepository(), new InMemorySiteComplianceRepository(),
            new FakeDemographicsEncryptionService(), new RecordingAuditLogWriter(), new FakeCurrentAuditContextAccessor());

    private async Task SeedAsync(string subjectCode, string studyCode, string? siteCode, int enrollmentIndex,
        SubjectState status = SubjectState.Screening)
    {
        var subject = ClinicalSubject.Create(new SubjectCode(subjectCode), new StudyCode(studyCode),
            siteCode is null ? null : new SiteCode(siteCode), enrollmentIndex, encryptedDemographics: "cipher",
            stratFactors: null);
        if (status != SubjectState.Screening)
        {
            subject.TransitionTo(status).IsSuccess.Should().BeTrue();
        }

        await _subjects.AddAsync(subject, CancellationToken.None);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyTheStudysSubjectsInEnrollmentOrder()
    {
        await SeedAsync("SUBJ-003", "STUDY-001", "SITE-001", 2);
        await SeedAsync("SUBJ-001", "STUDY-001", "SITE-001", 0);
        await SeedAsync("SUBJ-002", "STUDY-001", "SITE-002", 1);
        await SeedAsync("OTHER-001", "STUDY-002", "SITE-001", 0);

        var result = await CreateService().ListAsync("STUDY-001", null, null, null, null, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Select(s => s.SubjectId_.Value).Should().Equal("SUBJ-001", "SUBJ-002", "SUBJ-003");
        result.Value.Total.Should().Be(3);
        result.Value.Limit.Should().Be(SubjectApplicationService.DefaultListLimit);
        result.Value.Offset.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_FiltersBySiteAndStatus()
    {
        await SeedAsync("SUBJ-001", "STUDY-001", "SITE-001", 0, SubjectState.Enrolled);
        await SeedAsync("SUBJ-002", "STUDY-001", "SITE-001", 1);
        await SeedAsync("SUBJ-003", "STUDY-001", "SITE-002", 2, SubjectState.Enrolled);

        var result = await CreateService().ListAsync("STUDY-001", "SITE-001", "enrolled", null, null,
            TestContext.Current.CancellationToken);

        result.Value!.Items.Should().ContainSingle().Which.SubjectId_.Value.Should().Be("SUBJ-001");
        result.Value.Items[0].Status.Should().Be("ENROLLED");
    }

    [Fact]
    public async Task ListAsync_PagesAndReportsTheTotalAcrossPages()
    {
        for (int i = 0; i < 5; i++)
        {
            await SeedAsync($"SUBJ-00{i}", "STUDY-001", "SITE-001", i);
        }

        var result = await CreateService().ListAsync("STUDY-001", null, null, 2, 2, TestContext.Current.CancellationToken);

        result.Value!.Items.Select(s => s.SubjectId_.Value).Should().Equal("SUBJ-002", "SUBJ-003");
        result.Value.Total.Should().Be(5);
        result.Value.Limit.Should().Be(2);
        result.Value.Offset.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_FiltersByCaseInsensitiveSubjectCodeQueryAndTrimsWhitespace()
    {
        await SeedAsync("SUBJ-ALPHA-01", "STUDY-001", "SITE-001", 0);
        await SeedAsync("SUBJ-BETA-02", "STUDY-001", "SITE-001", 1);
        await SeedAsync("SUBJ-ALPHA-03", "STUDY-001", "SITE-002", 2);

        var result = await CreateService().ListAsync("STUDY-001", null, null, null, null,
            TestContext.Current.CancellationToken, query: "  alpha-0  ");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(2);
        result.Value.Items.Select(s => s.SubjectId_.Value).Should().Equal("SUBJ-ALPHA-01", "SUBJ-ALPHA-03");
    }

    [Theory]
    [InlineData(null, null, null, null, "STUDY_ID_REQUIRED")]
    [InlineData("STUDY-001", "NOT_A_STATUS", null, null, "INVALID_SUBJECT_STATUS")]
    [InlineData("STUDY-001", null, 0, null, "INVALID_LIMIT")]
    [InlineData("STUDY-001", null, 201, null, "INVALID_LIMIT")]
    [InlineData("STUDY-001", null, null, -1, "INVALID_OFFSET")]
    public async Task ListAsync_RejectsBadInputInsteadOfWideningTheList(string? studyId, string? status, int? limit,
        int? offset, string expectedCode)
    {
        var result = await CreateService().ListAsync(studyId, null, status, limit, offset,
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task GetCountsAsync_GroupsCountsByStatusAndHonorsSiteAndScopeFilters()
    {
        await SeedAsync("SUBJ-001", "STUDY-001", "SITE-001", 0, SubjectState.Screening);
        await SeedAsync("SUBJ-002", "STUDY-001", "SITE-001", 1, SubjectState.Enrolled);
        await SeedAsync("SUBJ-003", "STUDY-001", "SITE-002", 2, SubjectState.Enrolled);
        await SeedAsync("SUBJ-004", "STUDY-001", "SITE-003", 3, SubjectState.Withdrawn);
        await SeedAsync("OTHER-001", "STUDY-002", "SITE-001", 0, SubjectState.Enrolled);

        var ct = TestContext.Current.CancellationToken;
        var service = CreateService();

        var studyWide = await service.GetCountsAsync("STUDY-001", null, ct);
        studyWide.IsSuccess.Should().BeTrue();
        studyWide.Value!.Total.Should().Be(4);
        studyWide.Value.Counts.Should().Equal(new Dictionary<string, int>
        {
            ["SCREENING"] = 1,
            ["ENROLLED"] = 2,
            ["WITHDRAWN"] = 1,
        });

        var scoped = await service.GetCountsAsync("STUDY-001", null, ct, allowedSiteIds: ["SITE-001", "SITE-002"]);
        scoped.IsSuccess.Should().BeTrue();
        scoped.Value!.Total.Should().Be(3);
        scoped.Value.Counts.Should().Equal(new Dictionary<string, int>
        {
            ["SCREENING"] = 1,
            ["ENROLLED"] = 2,
        });

        var siteFiltered = await service.GetCountsAsync("STUDY-001", "SITE-002", ct, allowedSiteIds: ["SITE-001", "SITE-002"]);
        siteFiltered.IsSuccess.Should().BeTrue();
        siteFiltered.Value!.Total.Should().Be(1);
        siteFiltered.Value.Counts.Should().Equal(new Dictionary<string, int> { ["ENROLLED"] = 1 });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetCountsAsync_RequiresStudyId(string? studyId)
    {
        var result = await CreateService().GetCountsAsync(studyId, null, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("STUDY_ID_REQUIRED");
    }
}
