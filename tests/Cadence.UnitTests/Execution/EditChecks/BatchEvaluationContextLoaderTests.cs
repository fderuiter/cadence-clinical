using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.EditChecks;

/// <summary>
/// Unit tests for <see cref="BatchEvaluationContextLoader"/> ensuring sequential EF Core query execution
/// and proper filtering/ordering of form submissions and clinical observations.
/// </summary>
public sealed class BatchEvaluationContextLoaderTests
{
    private static ExecutionDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new ExecutionDbContext(options);
    }

    [Fact]
    public async Task LoadAsync_SequentiallyLoadsSubmissionsAndObservations_MatchingStudyAndSubject()
    {
        var dbName = $"BatchLoaderTest_{Guid.NewGuid():N}";
        var studyId = new StudyCode("STUDY-001");
        var subjectId = new SubjectCode("SUBJ-001");
        var otherSubjectId = new SubjectCode("SUBJ-002");

        await using var seedContext = CreateDbContext(dbName);
        var form1 = FormSubmission.Initialize(
            studyId: studyId,
            subjectId: subjectId,
            formId: new FormId("FORM-01"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            protocolVersion: "1.0.0",
            initialPayload: "{}").Value!;

        var formOtherSubject = FormSubmission.Initialize(
            studyId: studyId,
            subjectId: otherSubjectId,
            formId: new FormId("FORM-01"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            protocolVersion: "1.0.0",
            initialPayload: "{}").Value!;

        var obsEarlier = ClinicalObservation.Create(
            studyId: studyId,
            subjectId: subjectId,
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic BP",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 120.0,
            visitId: "VISIT-01").Value!;

        var obsLater = ClinicalObservation.Create(
            studyId: studyId,
            subjectId: subjectId,
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic BP",
            observationDate: new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc),
            value: 125.0,
            visitId: "VISIT-02").Value!;

        seedContext.FormSubmissions.AddRange(form1, formOtherSubject);
        seedContext.ClinicalObservations.AddRange(obsEarlier, obsLater);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var context = CreateDbContext(dbName);
        var loader = new BatchEvaluationContextLoader(context);
        var result = await loader.LoadAsync(
            studyId,
            subjectId,
            TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Submissions.Should().HaveCount(1);
        result.Submissions[0].SubjectId.Should().Be(subjectId);

        result.Observations.Should().HaveCount(2);
        result.Observations[0].ObservationDate.Should().BeAfter(result.Observations[1].ObservationDate);
    }

    [Fact]
    public async Task LoadAsync_ExcludesInactiveSubmissionsAndObservations()
    {
        var dbName = $"BatchLoaderTest_{Guid.NewGuid():N}";
        var studyId = new StudyCode("STUDY-001");
        var subjectId = new SubjectCode("SUBJ-001");

        await using var seedContext = CreateDbContext(dbName);
        var activeForm = FormSubmission.Initialize(
            studyId: studyId,
            subjectId: subjectId,
            formId: new FormId("FORM-01"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            protocolVersion: "1.0.0",
            initialPayload: "{}").Value!;

        var inactiveObs = ClinicalObservation.Create(
            studyId: studyId,
            subjectId: subjectId,
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic BP",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 120.0,
            visitId: "VISIT-01").Value!;
        inactiveObs.Deactivate();

        seedContext.FormSubmissions.Add(activeForm);
        seedContext.ClinicalObservations.Add(inactiveObs);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var context = CreateDbContext(dbName);
        var loader = new BatchEvaluationContextLoader(context);
        var result = await loader.LoadAsync(studyId, subjectId, TestContext.Current.CancellationToken);

        result.Submissions.Should().HaveCount(1);
        result.Observations.Should().BeEmpty();
    }
}
