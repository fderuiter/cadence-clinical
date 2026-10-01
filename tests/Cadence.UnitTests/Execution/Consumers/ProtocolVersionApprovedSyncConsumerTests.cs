using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Contracts;
using Cadence.Kernel;
using Cadence.Kernel.Events;
using Cadence.Testing;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Consumers;

public class ProtocolVersionApprovedSyncConsumerTests
{
    private sealed class FakeUnitOfWork
    {
        public List<ClinicalSubject> Subjects { get; } = [];
        public List<SubjectConsent> Consents { get; } = [];

        public static Task SaveChangesAsync() => Task.CompletedTask;
    }

    private sealed class FakeSubjectRepository(FakeUnitOfWork uow) : ISubjectRepository
    {
        public Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Subjects.FirstOrDefault(s => s.Id.ToString() == id));

        public Task<ClinicalSubject?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Subjects.FirstOrDefault(s => s.SubjectCode.Value == code));

        public Task<SubjectPage> ListAsync(SubjectListCriteria criteria, CancellationToken cancellationToken)
        {
            var items = uow.Subjects.Where(s => s.StudyCode == criteria.StudyCode).ToList();
            return Task.FromResult(new SubjectPage(items, items.Count));
        }

        public Task<int> EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Subjects.Count + 1);

        public Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken)
        {
            uow.Subjects.Add(subject);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => FakeUnitOfWork.SaveChangesAsync();
    }

    private sealed class FakeConsentRepository(FakeUnitOfWork uow) : IConsentRepository
    {
        public Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Consents.Any(c => c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.ProtocolVersion.VersionIndex == versionIndex));

        public Task<SubjectConsent?> GetAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Consents.FirstOrDefault(c => c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.ProtocolVersion.VersionIndex == versionIndex));

        public Task<IReadOnlyList<SubjectConsent>> GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode, int excludeVersionIndex, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubjectConsent>>(uow.Consents.Where(c => c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.ProtocolVersion.VersionIndex != excludeVersionIndex && c.RequiresReconsent).ToList());

        public Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken)
        {
            uow.Consents.Add(consent);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => FakeUnitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task ProtocolVersionApprovedSyncConsumer_ProcessesEvent_AndPublishesCompletion()
    {
        var uow = new FakeUnitOfWork();
        var subjectRepo = new FakeSubjectRepository(uow);
        var consentRepo = new FakeConsentRepository(uow);
        var auditWriter = new RecordingAuditLogWriter();

        var studyId = StudyId.New();
        var studyCode = new StudyCode(studyId.Value.ToString());

        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-SYNC-01"),
            studyCode: studyCode,
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 1,
            encryptedDemographics: null,
            stratFactors: null
        );
        subject.TransitionTo(SubjectState.Enrolled);
        subject.TransitionTo(SubjectState.Randomized);
        subject.TransitionTo(SubjectState.Active);
        uow.Subjects.Add(subject);

        var services = new ServiceCollection();
        services.AddSingleton<ISubjectRepository>(subjectRepo);
        services.AddSingleton<IConsentRepository>(consentRepo);
        services.AddSingleton<IAuditLogWriter>(auditWriter);
        services.AddScoped<IAuditActorScope>(_ => new AmbientAuditContextAccessor(new HttpContextAccessor()));

        services.AddMassTransitTestHarness(x =>
            x.AddConsumer<ProtocolVersionApprovedSyncConsumer>());

        await using var provider = services.BuildServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var approvedEvent = new ProtocolVersionApproved(
            TenantId: TenantId.New(),
            StudyId: studyId,
            ProtocolVersionId: ProtocolVersionId.New(),
            Number: 2,
            ContentHash: "HASH-12345",
            AttestationId: AttestationId.New()
        );

        await harness.Bus.Publish(approvedEvent, TestContext.Current.CancellationToken);

        var consumedMsg = harness.Consumed.Select<ProtocolVersionApproved>(TestContext.Current.CancellationToken).FirstOrDefault();
        consumedMsg.Should().NotBeNull();
        if (consumedMsg!.Exception != null)
        {
            throw new InvalidOperationException($"Consumer threw exception: {consumedMsg.Exception}");
        }

        (await harness.Consumed.Any<ProtocolVersionApproved>(TestContext.Current.CancellationToken)).Should().BeTrue();
        (await harness.Published.Any<EcrfSyncCompletedEvent>(TestContext.Current.CancellationToken)).Should().BeTrue();

        var publishedList = harness.Published.Select<EcrfSyncCompletedEvent>(TestContext.Current.CancellationToken).ToList();
        publishedList.Should().NotBeEmpty();

        var published = publishedList[0];
        published.Context.Message.StudyId.Should().Be(studyId.Value.ToString());
        published.Context.Message.VersionTag.Should().Be("2.0.0");
        published.Context.Message.Sha256EvidenceDigest.Should().NotBeNullOrWhiteSpace();

        subject.Status.Should().Be(SubjectState.ReconsentRequired);
    }
}
