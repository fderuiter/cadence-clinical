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
            stratFactors: null, treatmentGroup: "ARM-A");
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
}
