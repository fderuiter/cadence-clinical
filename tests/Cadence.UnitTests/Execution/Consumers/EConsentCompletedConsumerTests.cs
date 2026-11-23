using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Consumers;

/// <summary>
/// Coverage for `MIG-SUBJ-021`'s <see cref="EConsentCompletedConsumer"/> via
/// <see cref="MassTransit.Testing.ITestHarness"/> (`02_technical_design.md` §6.2), the design's prescribed
/// approach in place of a literal port of the legacy's <c>pytest</c>/<c>TESTING</c>/<c>REDIS_HOST</c>
/// no-start gate — test isolation here comes structurally from the harness's in-memory transport, not an
/// environment-variable check (per the ticket's explicit instruction, no such gate exists anywhere in this
/// file or in <see cref="EConsentCompletedConsumer"/> itself). Every test wires the consumer into a real
/// MassTransit bus (in-memory transport) backed by hand-rolled in-memory fakes for
/// <see cref="ISubjectRepository"/>/<see cref="IConsentRepository"/>/<see cref="IAuditLogWriter"/>, so
/// messages travel the genuine consume pipeline rather than calling <c>Consume</c> directly.
/// </summary>
public class EConsentCompletedConsumerTests
{
    private static readonly StudyCode StudyA = new("STUDY-ECONSENT-A");

    /// <summary>When the participant signed. Deliberately days before any test clock's "now".</summary>
    private static readonly DateTimeOffset SignedAt = new(2026, 1, 2, 9, 15, 0, TimeSpan.Zero);

    // ---- Shared in-memory fakes ----------------------------------------------------------------------

    /// <summary>
    /// Stands in for the single, shared <c>ExecutionDbContext</c>/`ChangeTracker` that
    /// <see cref="FakeSubjectRepository"/> and <see cref="FakeConsentRepository"/> both wrap in production —
    /// mirroring <see cref="ISubjectRepository.SaveChangesAsync"/>'s own doc contract that either
    /// repository's <c>SaveChangesAsync</c> commits <em>both</em> repositories' pending writes in one call.
    /// <see cref="ThrowOnSaveChanges"/> simulates a real downstream persistence failure (e.g. a
    /// <see cref="DbUpdateException"/>) for the error-propagation AC.
    /// </summary>
    private sealed class FakeUnitOfWork
    {
        private readonly List<ClinicalSubject> _pendingSubjects = [];
        private readonly List<SubjectConsent> _pendingConsents = [];

        public List<ClinicalSubject> Subjects { get; } = [];

        public List<SubjectConsent> Consents { get; } = [];

        public Exception? ThrowOnSaveChanges { get; set; }

        public int SaveChangesCallCount { get; private set; }

        public void StageSubject(ClinicalSubject subject) => _pendingSubjects.Add(subject);

        public void StageConsent(SubjectConsent consent) => _pendingConsents.Add(consent);

        public Task SaveChangesAsync()
        {
            SaveChangesCallCount++;
            if (ThrowOnSaveChanges is { } exception)
            {
                throw exception;
            }

            Subjects.AddRange(_pendingSubjects);
            Consents.AddRange(_pendingConsents);
            _pendingSubjects.Clear();
            _pendingConsents.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSubjectRepository(FakeUnitOfWork store) : ISubjectRepository
    {
        /// <summary>Every <see cref="StudyCode"/> this repository's <see cref="EnrollNextIndexAsync"/> was
        /// invoked with, in call order -- the recording the "shared code path" boundary-check AC needs.</summary>
        public List<StudyCode> EnrollNextIndexAsyncCalls { get; } = [];

        public Task<int> EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken)
        {
            EnrollNextIndexAsyncCalls.Add(studyCode);
            int? maxIndex = store.Subjects
                .Where(s => s.StudyCode == studyCode)
                .Select(s => (int?)s.EnrollmentIndex)
                .Max();
            return Task.FromResult(maxIndex is null ? 0 : maxIndex.Value + 1);
        }

        public Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.TryParse(id, out var guid)
                ? store.Subjects.SingleOrDefault(s => s.Id == new SubjectId(guid))
                : null);

        public Task<ClinicalSubject?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(store.Subjects.SingleOrDefault(s => s.SubjectCode.Value == code));

        // The consumer never lists subjects; the fake only has to satisfy the port.
        public Task<SubjectPage> ListAsync(SubjectListCriteria criteria, CancellationToken cancellationToken) =>
            throw new NotSupportedException("EConsentCompletedConsumer does not list subjects.");

        public Task<IReadOnlyDictionary<SubjectState, int>> CountByStatusAsync(
            SubjectCountCriteria criteria,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("EConsentCompletedConsumer does not count subjects.");

        public Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken)
        {
            store.StageSubject(subject);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => store.SaveChangesAsync();

        /// <summary>How many units of work the consumer ran; the fake has no real transaction to open.</summary>
        public int TransactionCount { get; private set; }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> unitOfWork, CancellationToken cancellationToken)
        {
            TransactionCount++;
            return unitOfWork(cancellationToken);
        }
    }

    private sealed class FakeConsentRepository(FakeUnitOfWork unitOfWork) : IConsentRepository
    {
        public Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
            Task.FromResult(unitOfWork.Consents.Any(c =>
                c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.ProtocolVersion.VersionIndex == versionIndex));

        // Added when MIG-SUBJ-018 extended IConsentRepository with the tracked-read methods
        // SubjectApplicationService.RecordConsentAsync needs (this consumer never calls either — it only
        // exercises ExistsAsync/AddAsync/SaveChangesAsync — but the fake must still satisfy the interface).
        public Task<SubjectConsent?> GetAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
            Task.FromResult(unitOfWork.Consents.FirstOrDefault(c =>
                c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.ProtocolVersion.VersionIndex == versionIndex));

        public Task<IReadOnlyList<SubjectConsent>> GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode,
            int excludeVersionIndex, CancellationToken cancellationToken)
        {
            IReadOnlyList<SubjectConsent> others = unitOfWork.Consents
                .Where(c => c.SubjectCode == subjectCode
                    && c.ProtocolVersion.StudyCode == studyCode
                    && c.ProtocolVersion.VersionIndex != excludeVersionIndex
                    && c.RequiresReconsent)
                .ToList();
            return Task.FromResult(others);
        }

        public Task<bool> HasCurrentConsentAsync(SubjectCode subjectCode, StudyCode studyCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The consumer never checks for a current consent.");

        public Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken)
        {
            unitOfWork.StageConsent(consent);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync();
    }

    // ---- Harness plumbing ------------------------------------------------------------------------------

    private static async Task<ServiceProvider> StartHarnessAsync(
        FakeUnitOfWork unitOfWork, RecordingAuditLogWriter auditWriter, IClock clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISubjectRepository>(new FakeSubjectRepository(unitOfWork));
        services.AddSingleton<IConsentRepository>(new FakeConsentRepository(unitOfWork));
        services.AddSingleton<IAuditLogWriter>(auditWriter);
        services.AddScoped<IAuditActorScope>(_ => new AmbientAuditContextAccessor(new HttpContextAccessor()));
        services.AddSingleton(clock);

        // ConcurrentMessageLimit = 1: the redelivery test publishes the same event twice and asserts the
        // second delivery observes what the first one committed. The real, at-most-one-writer guarantee for
        // concurrent enrollment comes from EnrollNextIndexAsync's Postgres advisory lock in production
        // (02_technical_design.md §4.2, proved separately by SubjectRepositoryTests' own N-parallel-callers
        // test and MIG-SUBJ-028's Testcontainers integration test) -- these fakes intentionally do not
        // reimplement that locking, so pinning this test consumer to sequential processing is what makes
        // "redelivered later" (the scenario this ticket's AC describes) deterministic rather than racing
        // against genuinely concurrent delivery (a different scenario, out of this unit-test's scope).
        services.AddMassTransitTestHarness(x =>
            x.AddConsumer<EConsentCompletedConsumer>().Endpoint(e => e.ConcurrentMessageLimit = 1));

        var provider = services.BuildServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start().ConfigureAwait(false);
        return provider;
    }

    /// <summary>Polls <paramref name="condition"/> until it is true or 5 seconds elapse -- used where the
    /// completion signal is this test's own fake state (e.g. a second <c>SaveChangesAsync</c> call) rather
    /// than something <see cref="ITestHarness"/>'s <c>Consumed</c>/<c>Published</c> collections can key on,
    /// since two structurally-identical redelivered messages are otherwise indistinguishable to
    /// <c>Consumed.Any&lt;T&gt;()</c> (already satisfied by the first).</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("Condition was not met within the expected time.");
            }

            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    // ---- Happy path: no existing subject --------------------------------------------------------------

    [Fact]
    public async Task Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork();
        var auditWriter = new RecordingAuditLogWriter();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero));

        await using var provider = await StartHarnessAsync(unitOfWork, auditWriter, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        try
        {
            var evt = new EConsentCompletedEvent("SUBJ-AUTO-01", "STUDY-AUTO", null, "v1.0", 1, SignedAt);
            await harness.Bus.Publish(evt, ct);

            (await harness.Consumed.Any<EConsentCompletedEvent>(ct)).Should().BeTrue();

            unitOfWork.Subjects.Should().ContainSingle();
            var subject = unitOfWork.Subjects[0];
            subject.SubjectCode.Value.Should().Be("SUBJ-AUTO-01");
            subject.StudyCode.Value.Should().Be("STUDY-AUTO");
            subject.Status.Should().Be(SubjectState.Screening);
            subject.EnrollmentIndex.Should().Be(0);
            subject.ReasonForChange.Should().Be(ServiceAccounts.EConsentConsumer.ChangeReason);

            unitOfWork.Consents.Should().ContainSingle();
            var consent = unitOfWork.Consents[0];
            consent.SubjectCode.Should().Be(subject.SubjectCode);
            consent.IcfSigned.Should().BeTrue();
            consent.IsPaperOverride.Should().BeFalse();
            consent.RequiresReconsent.Should().BeFalse();
            consent.IcfSignedDate.Should().Be(SignedAt, "the signed time is the event's, not when the consumer ran");

            auditWriter.Calls.Should().HaveCount(2);
            var subjectAudit = auditWriter.Calls.Should().ContainSingle(c => c.EntityName == "ClinicalSubject").Which;
            subjectAudit.Action.Should().Be("INSERT");
            subjectAudit.RecordId.Should().Be(subject.Id.ToString());
            subjectAudit.UserId.Should().Be(ServiceAccounts.EConsentConsumer.UserId);
            subjectAudit.ChangeReason.Should().Be(ServiceAccounts.EConsentConsumer.ChangeReason);

            var consentAudit = auditWriter.Calls.Should().ContainSingle(c => c.EntityName == "SubjectConsent").Which;
            consentAudit.Action.Should().Be("INSERT");
            consentAudit.RecordId.Should().Be(consent.Id.ToString());
            consentAudit.UserId.Should().Be(ServiceAccounts.EConsentConsumer.UserId);
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    // ---- Boundary check: shared EnrollNextIndexAsync code path ----------------------------------------

    [Fact]
    public async Task Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod()
    {
        // Structural half of the boundary check: ISubjectRepository names exactly one method whose purpose
        // is enrollment-index assignment, so there is no second, independently-implemented index query this
        // (or SubjectApplicationService.CreateAsync's, MIG-SUBJ-018) call site could have used instead.
        typeof(ISubjectRepository).GetMethods()
            .Where(m => m.Name.Contains("Index", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Name.Should().Be(nameof(ISubjectRepository.EnrollNextIndexAsync));

        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork();
        var auditWriter = new RecordingAuditLogWriter();
        var clock = new FakeClock();

        await using var provider = await StartHarnessAsync(unitOfWork, auditWriter, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        var subjectsRepository = (FakeSubjectRepository)provider.GetRequiredService<ISubjectRepository>();
        try
        {
            await harness.Bus.Publish(new EConsentCompletedEvent("SUBJ-SHARED-PATH", StudyA.Value, null, "v1.0", 1, SignedAt), ct);
            (await harness.Consumed.Any<EConsentCompletedEvent>(ct)).Should().BeTrue();

            // Behavioral half: this exact call site actually invoked that single shared method -- not merely
            // producing a matching output value some other, hand-rolled path could equally have produced.
            subjectsRepository.EnrollNextIndexAsyncCalls.Should().ContainSingle().Which.Should().Be(StudyA);
            subjectsRepository.TransactionCount.Should().Be(1, "the index, the subject and the consent are one unit of work (#5383)");
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    // ---- Malformed events: silent drop -----------------------------------------------------------------

    [Theory]
    [InlineData("", "STUDY-MALFORMED")]
    [InlineData("   ", "STUDY-MALFORMED")]
    [InlineData("SUBJ-MALFORMED", "")]
    [InlineData("SUBJ-MALFORMED", "   ")]
    public async Task Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(
        string subjectId, string studyId)
    {
        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork();
        var auditWriter = new RecordingAuditLogWriter();
        var clock = new FakeClock();

        await using var provider = await StartHarnessAsync(unitOfWork, auditWriter, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        var subjectsRepository = (FakeSubjectRepository)provider.GetRequiredService<ISubjectRepository>();
        try
        {
            var evt = new EConsentCompletedEvent(subjectId, studyId, null, "v1.0", 1, SignedAt);
            await harness.Bus.Publish(evt, ct);

            // "Completes normally" (no exception, no fault, no dead-letter): ITestHarness only records a
            // message under Consumed once its consumer pipeline finishes successfully -- a message that
            // threw would show up as a published Fault<T> instead (proven the other way around by
            // Consume_GivenSaveChangesThrowsADbUpdateException_..., below), never here.
            (await harness.Consumed.Any<EConsentCompletedEvent>(ct)).Should().BeTrue();

            unitOfWork.Subjects.Should().BeEmpty();
            unitOfWork.Consents.Should().BeEmpty();
            subjectsRepository.EnrollNextIndexAsyncCalls.Should().BeEmpty();
            auditWriter.Calls.Should().BeEmpty();
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    // ---- Redelivery idempotency -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo()
    {
        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork();
        var auditWriter = new RecordingAuditLogWriter();
        var clock = new FakeClock();

        await using var provider = await StartHarnessAsync(unitOfWork, auditWriter, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        var subjectsRepository = (FakeSubjectRepository)provider.GetRequiredService<ISubjectRepository>();
        try
        {
            var evt = new EConsentCompletedEvent("SUBJ-REDELIVERED", "STUDY-REDELIVERED", null, "v1.0", 1, SignedAt);

            await harness.Bus.Publish(evt, ct);
            await harness.Bus.Publish(evt, ct);

            // Two structurally-identical messages are indistinguishable to Consumed.Any<T>() (already
            // satisfied by the first), so wait on the fakes' own completion signal instead: Consume() always
            // calls SaveChangesAsync exactly once per delivery, redelivered or not, so two completed
            // deliveries means SaveChangesCallCount reaches 2.
            await WaitUntilAsync(() => unitOfWork.SaveChangesCallCount >= 2, ct);

            unitOfWork.Subjects.Should().ContainSingle("the second delivery's idempotency check must skip subject creation entirely");
            unitOfWork.Consents.Should().ContainSingle("the consent-cache backfill is independently idempotent across redelivery too");
            subjectsRepository.EnrollNextIndexAsyncCalls.Should().ContainSingle("EnrollNextIndexAsync must only run on the delivery that actually creates the subject");
            auditWriter.Calls.Should().HaveCount(2, "the second, purely-duplicate delivery must write no additional audit entries");
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    // ---- Independent consent-cache backfill -------------------------------------------------------------

    [Fact]
    public async Task Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject()
    {
        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork();
        var existingSubject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-PRE-EXISTING"), StudyA, siteCode: null,
            enrollmentIndex: 4, encryptedDemographics: null, stratFactors: null);
        unitOfWork.Subjects.Add(existingSubject);
        var auditWriter = new RecordingAuditLogWriter();
        var clock = new FakeClock(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        await using var provider = await StartHarnessAsync(unitOfWork, auditWriter, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        var subjectsRepository = (FakeSubjectRepository)provider.GetRequiredService<ISubjectRepository>();
        try
        {
            var evt = new EConsentCompletedEvent("SUBJ-PRE-EXISTING", StudyA.Value, null, "v2.0", 2, SignedAt);
            await harness.Bus.Publish(evt, ct);

            (await harness.Consumed.Any<EConsentCompletedEvent>(ct)).Should().BeTrue();

            unitOfWork.Subjects.Should().ContainSingle().Which.Should().BeSameAs(existingSubject,
                "an already-existing subject must never be re-created");
            subjectsRepository.EnrollNextIndexAsyncCalls.Should().BeEmpty(
                "the subject-creation idempotency check must skip index assignment entirely when the subject already exists");

            unitOfWork.Consents.Should().ContainSingle("the consent-cache row must still be backfilled independently of the subject-existence check");
            var consent = unitOfWork.Consents[0];
            consent.SubjectCode.Should().Be(existingSubject.SubjectCode);
            consent.ProtocolVersion.VersionIndex.Should().Be(2);
            consent.IcfSigned.Should().BeTrue();
            consent.IcfSignedDate.Should().Be(SignedAt);

            auditWriter.Calls.Should().ContainSingle("only the independent consent backfill is audited -- no subject INSERT audit entry is written")
                .Which.EntityName.Should().Be("SubjectConsent");
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    // ---- Error propagation: a real persistence failure must not be swallowed --------------------------

    [Fact]
    public async Task Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork { ThrowOnSaveChanges = new DbUpdateException("Simulated persistence failure.") };
        var auditWriter = new RecordingAuditLogWriter();
        var clock = new FakeClock();

        await using var provider = await StartHarnessAsync(unitOfWork, auditWriter, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        try
        {
            var evt = new EConsentCompletedEvent("SUBJ-FAULT", "STUDY-FAULT", null, "v1.0", 1, SignedAt);
            await harness.Bus.Publish(evt, ct);

            // A broad try/catch inside Consume swallowing this exception would leave the message showing up
            // as an ordinary successful Consumed completion and would never publish a Fault<T> -- exactly the
            // "caught and swallowed by this consumer" failure mode the AC forbids. MassTransit only ever
            // publishes Fault<T> when a consumer's Consume method actually throws, so this one assertion is
            // both necessary and sufficient to prove the exception propagated rather than being swallowed.
            (await harness.Published.Any<Fault<EConsentCompletedEvent>>(ct)).Should().BeTrue(
                "an unhandled persistence failure must propagate out of Consume so MassTransit's own retry/fault handling observes it");
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Consume_AfterAConsumerDelay_RecordsTheSignedTimeFromTheEventNotTheProcessingTime()
    {
        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork();
        var clock = new FakeClock(SignedAt.AddDays(3));

        await using var provider = await StartHarnessAsync(unitOfWork, new RecordingAuditLogWriter(), clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        try
        {
            await harness.Bus.Publish(new EConsentCompletedEvent("SUBJ-DELAYED", StudyA.Value, null, "v1.0", 1, SignedAt), ct);

            (await harness.Consumed.Any<EConsentCompletedEvent>(ct)).Should().BeTrue();
            unitOfWork.Consents.Should().ContainSingle()
                .Which.IcfSignedDate.Should().Be(SignedAt).And.NotBe(clock.UtcNow);
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Consume_WithoutASignedTime_FaultsToTheErrorQueueAndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var unitOfWork = new FakeUnitOfWork();
        var auditWriter = new RecordingAuditLogWriter();

        await using var provider = await StartHarnessAsync(unitOfWork, auditWriter, new FakeClock());
        var harness = provider.GetRequiredService<ITestHarness>();
        try
        {
            await harness.Bus.Publish(new EConsentCompletedEvent("SUBJ-UNSIGNED", StudyA.Value, null, "v1.0", 1), ct);

            (await harness.Published.Any<Fault<EConsentCompletedEvent>>(ct)).Should().BeTrue(
                "a consent without its signed time must be rejected, not recorded with the processing time");
            unitOfWork.Subjects.Should().BeEmpty();
            unitOfWork.Consents.Should().BeEmpty();
            auditWriter.Calls.Should().BeEmpty();
        }
        finally
        {
            await harness.Stop(ct);
        }
    }
}
