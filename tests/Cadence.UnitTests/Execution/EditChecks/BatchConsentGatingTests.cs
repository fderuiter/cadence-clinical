using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.EditChecks;

public sealed class BatchConsentGatingTests
{
    private static readonly StudyCode TestStudy = new("STUDY-001");
    private static readonly SubjectCode TestSubject = new("SUBJ-001");
    private static readonly SiteCode TestSite = new("SITE-001");
    private static readonly DateTimeOffset TestDate = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BatchEvaluationContext_WithSignedIcf_IsConsentedIsTrue()
    {
        var subject = ClinicalSubject.Create(TestSubject, TestStudy, TestSite, 1, null, null);
        var consent = SubjectConsent.Create(
            TestSubject,
            new ProtocolVersionRef(TestStudy, "1.0.0", 1),
            icfSigned: true,
            icfSignedDate: TestDate,
            requiresReconsent: false,
            isPaperOverride: false);

        var context = new BatchEvaluationContext(
            TestStudy.Value,
            TestSubject.Value,
            Array.Empty<ClinicalObservation>(),
            Array.Empty<FormSubmission>(),
            subject,
            [consent]);

        context.IsConsented.Should().BeTrue();
        context.ConsentStatus.Should().Be("CONSENTED");
    }

    [Fact]
    public void BatchEvaluationContext_WithPaperOverride_IsConsentedIsTrue()
    {
        var subject = ClinicalSubject.Create(TestSubject, TestStudy, TestSite, 1, null, null);
        var consent = SubjectConsent.Create(
            TestSubject,
            new ProtocolVersionRef(TestStudy, "1.0.0", 1),
            icfSigned: false,
            icfSignedDate: null,
            requiresReconsent: false,
            isPaperOverride: true);

        var context = new BatchEvaluationContext(
            TestStudy.Value,
            TestSubject.Value,
            Array.Empty<ClinicalObservation>(),
            Array.Empty<FormSubmission>(),
            subject,
            [consent]);

        context.IsConsented.Should().BeTrue();
        context.ConsentStatus.Should().Be("CONSENTED");
    }

    [Fact]
    public void BatchEvaluationContext_WithoutSignedIcfOrOverride_IsConsentedIsFalse()
    {
        var subject = ClinicalSubject.Create(TestSubject, TestStudy, TestSite, 1, null, null);
        var consent = SubjectConsent.Create(
            TestSubject,
            new ProtocolVersionRef(TestStudy, "1.0.0", 1),
            icfSigned: false,
            icfSignedDate: null,
            requiresReconsent: false,
            isPaperOverride: false);

        var context = new BatchEvaluationContext(
            TestStudy.Value,
            TestSubject.Value,
            Array.Empty<ClinicalObservation>(),
            Array.Empty<FormSubmission>(),
            subject,
            [consent]);

        context.IsConsented.Should().BeFalse();
        context.ConsentStatus.Should().Be("UNCONSENTED");
    }

    [Fact]
    public void BatchEvaluationContext_WithdrawnSubject_IsConsentedIsFalse()
    {
        var subject = ClinicalSubject.Create(TestSubject, TestStudy, TestSite, 1, null, null);
        var clock = new FakeClock(TestDate);
        subject.Withdraw("Subject withdrew from study", clock);

        var consent = SubjectConsent.Create(
            TestSubject,
            new ProtocolVersionRef(TestStudy, "1.0.0", 1),
            icfSigned: true,
            icfSignedDate: TestDate,
            requiresReconsent: false,
            isPaperOverride: false);

        var context = new BatchEvaluationContext(
            TestStudy.Value,
            TestSubject.Value,
            Array.Empty<ClinicalObservation>(),
            Array.Empty<FormSubmission>(),
            subject,
            [consent]);

        context.IsConsented.Should().BeFalse();
        context.ConsentStatus.Should().Be("WITHDRAWN");
    }

    [Fact]
    public void BatchEvaluationContext_ScreenFailedSubject_IsConsentedIsFalse()
    {
        var subject = ClinicalSubject.Create(TestSubject, TestStudy, TestSite, 1, null, null);
        subject.TransitionTo(SubjectState.ScreenFailed);

        var consent = SubjectConsent.Create(
            TestSubject,
            new ProtocolVersionRef(TestStudy, "1.0.0", 1),
            icfSigned: true,
            icfSignedDate: TestDate,
            requiresReconsent: false,
            isPaperOverride: false);

        var context = new BatchEvaluationContext(
            TestStudy.Value,
            TestSubject.Value,
            Array.Empty<ClinicalObservation>(),
            Array.Empty<FormSubmission>(),
            subject,
            [consent]);

        context.IsConsented.Should().BeFalse();
        context.ConsentStatus.Should().Be("SCREEN_FAILED");
    }

    [Fact]
    public void AstEditCheckEngine_UnconsentedContext_BypassesBatchRulesAndReturnsZeroViolations()
    {
        var engine = new AstEditCheckEngine();

        // Observations that would trigger a cross-field violation (diastolic higher than systolic)
        var sysObs = ClinicalObservation.Create(
            studyId: TestStudy,
            subjectId: TestSubject,
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic BP",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 70.0,
            visitId: "VISIT-01").Value!;

        var diaObs = ClinicalObservation.Create(
            studyId: TestStudy,
            subjectId: TestSubject,
            domain: new DomainCode("VS"),
            testCode: new TestCode("DIABP"),
            testName: "Diastolic BP",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 90.0,
            visitId: "VISIT-01").Value!;

        var unconsentedContext = new BatchEvaluationContext(
            TestStudy.Value,
            TestSubject.Value,
            [sysObs, diaObs],
            [],
            isConsented: false,
            consentStatus: "UNCONSENTED");

        var violations = engine.EvaluateBatchRules(unconsentedContext);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoQueryDispatchService_UnconsentedSubject_DispatchesZeroQueries()
    {
        var queryRepo = Substitute.For<IClinicalQueryRepository>();
        var clock = new FakeClock(TestDate);
        var auditAccessor = new FakeCurrentAuditContextAccessor("usr:test", "Testing");

        var service = new AutoQueryDispatchService(queryRepo, clock, auditAccessor);

        var violation = new EditCheckViolation(
            RuleId: "EDIT-CROSS-VS-001",
            Message: "Diastolic BP exceeds Systolic BP.",
            Severity: "ERROR");

        var sysObs = ClinicalObservation.Create(
            studyId: TestStudy,
            subjectId: TestSubject,
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic BP",
            observationDate: new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            value: 70.0,
            visitId: "VISIT-01").Value!;

        var result = await service.ProcessViolationsAndAutoResolutionAsync(
            TestStudy,
            TestSubject,
            TestSite,
            "VISIT-01",
            "FORM-VS",
            [sysObs],
            [violation],
            TestContext.Current.CancellationToken,
            isConsented: false);

        result.Should().BeEmpty();
        await queryRepo.DidNotReceiveWithAnyArgs().AddAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BatchEvaluationContextLoader_QueriesDatabase_CalculatesConsentGatingCorrectly()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ExecutionDbContext(options);
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var subject = ClinicalSubject.Create(TestSubject, TestStudy, TestSite, 1, null, null);
        var consent = SubjectConsent.Create(
            TestSubject,
            new ProtocolVersionRef(TestStudy, "1.0.0", 1),
            icfSigned: true,
            icfSignedDate: TestDate,
            requiresReconsent: false,
            isPaperOverride: false);

        await dbContext.ClinicalSubjects.AddAsync(subject, TestContext.Current.CancellationToken);
        await dbContext.SubjectConsents.AddAsync(consent, TestContext.Current.CancellationToken);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var loader = new BatchEvaluationContextLoader(dbContext);

        var batchContext = await loader.LoadAsync(TestStudy, TestSubject, TestContext.Current.CancellationToken);

        batchContext.IsConsented.Should().BeTrue();
        batchContext.ConsentStatus.Should().Be("CONSENTED");
        batchContext.StudyId.Should().Be(TestStudy.Value);
        batchContext.SubjectId.Should().Be(TestSubject.Value);
    }
}
