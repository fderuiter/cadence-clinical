using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Compliance;

/// <summary>
/// Coverage for `MIG-SUBJ-020`'s <see cref="SiteComplianceApplicationService"/>: the eTMF-webhook
/// upsert-vs-create branch with explicit "no audit log row" assertions, the site-activation blocked/success
/// paths with an audit-entry-ordering assertion, and the always-200 compliance-status query for both the
/// row-exists and no-row cases. Uses the real <see cref="SiteComplianceRepository"/> against an EF Core
/// InMemory-backed <see cref="ExecutionDbContext"/> (matching `SiteComplianceRepositoryTests`'/
/// `SiteComplianceCacheConfigurationTests`' own established convention for this aggregate, and no
/// mocking-framework usage appears anywhere else in <c>tests/Cadence.UnitTests/Execution/</c>) plus a hand-rolled
/// <see cref="RecordingAuditLogWriter"/> that records every call for zero-calls/ordering assertions.
/// </summary>
public class SiteComplianceApplicationServiceTests
{
    private static readonly StudyCode Study = new("STUDY-COMPLIANCE-APP");
    private const string SiteId = "SITE-COMPLIANCE-APP";
    private const string Milestone = "SITE_ACTIVATION";

    private static readonly DateTimeOffset FixedActivatedAt = new(2026, 11, 20, 12, 30, 0, TimeSpan.Zero);

    private static ExecutionDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new GxPAuditInterceptor(() => "user-compliance-1", new FakeClock(FixedActivatedAt)))
            .Options);

    private static SiteComplianceApplicationService CreateService(
        ExecutionDbContext context,
        RecordingAuditLogWriter? auditLog = null,
        IClock? clock = null,
        IKernelLockGuard? lockGuard = null) =>
        new(new SiteComplianceRepository(context),
            auditLog ?? new RecordingAuditLogWriter(),
            new FakeCurrentAuditContextAccessor("user-compliance-1", "eTMF completeness evaluation sync"),
            clock ?? new FakeClock(FixedActivatedAt),
            lockGuard);

    // ---- ApplyEtmfWebhookAsync: create branch ------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndAuditsTheCaller()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();
        var payload = new EtmfCompletenessWebhookPayload(
            Study.Value, SiteId, Milestone, IsComplete: true, MissingArtifacts: new[] { "Form-A" });

        await using (var context = CreateContext(databaseName))
        {
            var service = CreateService(context, auditLog);
            var result = await service.ApplyEtmfWebhookAsync(payload, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
        }

        await using var readContext = CreateContext(databaseName);
        var row = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.Version.Should().Be(1);
        row.IsComplete.Should().BeTrue();
        row.MissingDocuments.Should().Equal("Form-A");

        var call = auditLog.Calls.Should().ContainSingle().Which;
        call.Action.Should().Be("ETMF_COMPLIANCE_UPDATE");
        call.EntityName.Should().Be("SiteComplianceCache");
        call.RecordId.Should().Be(row.Id.ToString());
        call.UserId.Should().Be("user-compliance-1");
    }

    [Fact]
    public async Task ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments()
    {
        string databaseName = Guid.NewGuid().ToString();
        var payload = new EtmfCompletenessWebhookPayload(
            Study.Value, SiteId, Milestone, IsComplete: false, MissingArtifacts: null);

        await using (var context = CreateContext(databaseName))
        {
            var service = CreateService(context);
            await service.ApplyEtmfWebhookAsync(payload, TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateContext(databaseName);
        var row = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.MissingDocuments.Should().BeEmpty();
    }

    // ---- ApplyEtmfWebhookAsync: upsert (update) branch ---------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndAuditsTheCaller()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                Study, new SiteCode(SiteId), Milestone, isComplete: false, new[] { "Form-A" }));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var payload = new EtmfCompletenessWebhookPayload(
            Study.Value, SiteId, Milestone, IsComplete: true, MissingArtifacts: Array.Empty<string>());

        await using (var context = CreateContext(databaseName))
        {
            var service = CreateService(context, auditLog);
            var result = await service.ApplyEtmfWebhookAsync(payload, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
        }

        await using var readContext = CreateContext(databaseName);
        var row = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.Version.Should().Be(2, "an upsert against an existing row increments the legacy plain-integer counter");
        row.IsComplete.Should().BeTrue();
        row.MissingDocuments.Should().BeEmpty("ApplyWebhookUpdate always fully replaces, never merges");

        var call = auditLog.Calls.Should().ContainSingle("both webhook branches audit the change").Which;
        call.Action.Should().Be("ETMF_COMPLIANCE_UPDATE");
        call.RecordId.Should().Be(row.Id.ToString());
        call.UserId.Should().Be("user-compliance-1");
    }

    [Fact]
    public async Task ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow()
    {
        // EtmfCompletenessWebhookPayload.SiteId is documented nullable ("study-level milestones are a
        // legitimate case"), but SiteComplianceCache.SiteCode (MIG-SUBJ-006) is non-nullable -- a
        // pre-existing gap between already-merged tickets, out of this ticket's scope to resolve (see this
        // ticket's own final report). The safest available behavior is a documented no-op: no exception, no
        // fabricated site, and -- per the "always upserts" contract -- still a successful result.
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();
        var payload = new EtmfCompletenessWebhookPayload(
            Study.Value, SiteId: null, Milestone, IsComplete: true, MissingArtifacts: new[] { "Form-A" });

        await using var context = CreateContext(databaseName);
        var service = CreateService(context, auditLog);

        var result = await service.ApplyEtmfWebhookAsync(payload, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        (await context.SiteComplianceCaches.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        auditLog.Calls.Should().BeEmpty();
    }

    // ---- ActivateSiteAsync: blocked path ------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();

        await using var context = CreateContext(databaseName);
        var service = CreateService(context, auditLog);

        var result = await service.ActivateSiteAsync(
            SiteId, new SiteActivationRequest(Study.Value), TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("BLOCKED_ACTIVATION");

        auditLog.Calls.Should().ContainSingle();
        auditLog.Calls[0].Action.Should().Be("BLOCKED_ACTIVATION");
        auditLog.Calls[0].EntityName.Should().Be("SiteComplianceCache");
        auditLog.Calls[0].RecordId.Should().Be(SiteId);
        auditLog.Calls[0].UserId.Should().Be("user-compliance-1");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                Study, new SiteCode(SiteId), Milestone, isComplete: false, new[] { "1572" }));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(databaseName);
        var service = CreateService(context, auditLog);

        var result = await service.ActivateSiteAsync(
            SiteId, new SiteActivationRequest(Study.Value), TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("BLOCKED_ACTIVATION");
        auditLog.Calls.Should().ContainSingle(c => c.Action == "BLOCKED_ACTIVATION");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller()
    {
        // Strongest form of the "writes the audit entry before returning the failure" acceptance criterion:
        // IAuditLogWriter.WriteAsync only labels the next save (#4768), so a real EfAuditLogWriter + a
        // fresh, independent ExecutionDbContext re-query after ActivateSiteAsync returns proves the entry
        // was actually committed -- not merely staged -- by the time the caller observes the failure result.
        string databaseName = Guid.NewGuid().ToString();

        await using (var context = CreateContext(databaseName))
        {
            var service = new SiteComplianceApplicationService(
                new SiteComplianceRepository(context),
                new EfAuditLogWriter(context),
                new FakeCurrentAuditContextAccessor("user-compliance-1", "Site activation check"),
                new FakeClock(FixedActivatedAt));

            var result = await service.ActivateSiteAsync(
                SiteId, new SiteActivationRequest(Study.Value), TestContext.Current.CancellationToken);

            result.IsFailure.Should().BeTrue();
        }

        await using var readContext = CreateContext(databaseName);
        var entry = await readContext.AuditLogEntries.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        entry.Action.Should().Be("BLOCKED_ACTIVATION");
        entry.EntityId.Should().Be(SiteId);
    }

    // ---- ActivateSiteAsync: success path -------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                Study, new SiteCode(SiteId), Milestone, isComplete: true, Array.Empty<string>()));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = CreateContext(databaseName))
        {
            var service = CreateService(context, auditLog);
            var result = await service.ActivateSiteAsync(
                SiteId, new SiteActivationRequest(Study.Value), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
        }

        auditLog.Calls.Should().ContainSingle();
        auditLog.Calls[0].Action.Should().Be("SITE_ACTIVATION");

        // ActivateSiteAsync never itself flips IsComplete -- only the eTMF webhook does that.
        await using var readContext = CreateContext(databaseName);
        var row = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        auditLog.Calls[0].RecordId.Should().Be(row.Id.ToString());
        row.IsComplete.Should().BeTrue();
        row.IsActivated.Should().BeTrue();
        row.ActivatedAt.Should().Be(FixedActivatedAt);
        row.ActivatedBy.Should().Be("user-compliance-1");
        row.Version.Should().Be(1, "activation must not touch the compliance row's own version counter");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ActivateSiteAsync_CalledTwiceOnCompliantSite_IsIdempotentAcrossReloadsAndWritesSingleAuditEntry()
    {
        string databaseName = Guid.NewGuid().ToString();
        var clock = new FakeClock(FixedActivatedAt);

        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                Study, new SiteCode(SiteId), Milestone, isComplete: true, Array.Empty<string>()));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // First activation persists state and writes exactly one SITE_ACTIVATION entry.
        await using (var firstCtx = CreateContext(databaseName))
        {
            var service = new SiteComplianceApplicationService(
                new SiteComplianceRepository(firstCtx),
                new EfAuditLogWriter(firstCtx),
                new FakeCurrentAuditContextAccessor("sponsor-dm-1", "Initial activation sign-off"),
                clock);

            var firstResult = await service.ActivateSiteAsync(
                SiteId, new SiteActivationRequest(Study.Value), TestContext.Current.CancellationToken);
            firstResult.IsSuccess.Should().BeTrue();
        }

        // Second activation at a later time is a clean no-op: returns true without adding duplicate audit rows.
        clock.Advance(TimeSpan.FromHours(1));
        await using (var secondCtx = CreateContext(databaseName))
        {
            var service = new SiteComplianceApplicationService(
                new SiteComplianceRepository(secondCtx),
                new EfAuditLogWriter(secondCtx),
                new FakeCurrentAuditContextAccessor("sponsor-dm-2", "Duplicate retry"),
                clock);

            var secondResult = await service.ActivateSiteAsync(
                SiteId, new SiteActivationRequest(Study.Value), TestContext.Current.CancellationToken);
            secondResult.IsSuccess.Should().BeTrue();

            var status = await service.GetComplianceStatusAsync(
                SiteId, Study.Value, Milestone, TestContext.Current.CancellationToken);
            status.IsActivated.Should().BeTrue();
            status.ActivatedAt.Should().Be(FixedActivatedAt);
            status.ActivatedBy.Should().Be("sponsor-dm-1");
        }

        await using var auditCtx = CreateContext(databaseName);
        var activationEntries = await auditCtx.AuditLogEntries.AsNoTracking()
            .Where(e => e.Action == "SITE_ACTIVATION")
            .ToListAsync(TestContext.Current.CancellationToken);
        activationEntries.Should().ContainSingle();
        activationEntries[0].UserId.Should().Be("sponsor-dm-1");
    }

    // ---- GetComplianceStatusAsync: always-200, no Result<T> wrapper --------------------------------

    [Fact]
    public void GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath()
    {
        // Direct boundary check for the acceptance criterion's own wording: "verify the method signature
        // itself has no failure return path" -- the interface method must return the response directly,
        // never wrapped in Result<T>.
        MethodInfo method = typeof(ISiteComplianceApplicationService)
            .GetMethod(nameof(ISiteComplianceApplicationService.GetComplianceStatusAsync))!;

        method.ReturnType.Should().Be(typeof(Task<ComplianceStatusResponse>));
    }

    [Fact]
    public async Task GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        ComplianceStatusResponse response = await service.GetComplianceStatusAsync(
            SiteId, Study.Value, Milestone, TestContext.Current.CancellationToken);

        response.IsComplete.Should().BeFalse();
        response.MissingDocuments.Should().BeEmpty();
        response.StudyId.Should().Be(Study);
        response.Milestone.Should().Be(Milestone);
    }

    [Fact]
    public async Task GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState()
    {
        string databaseName = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(databaseName))
        {
            seed.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                Study, new SiteCode(SiteId), Milestone, isComplete: true, new[] { "Form-B" }));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(databaseName);
        var service = CreateService(context);

        var response = await service.GetComplianceStatusAsync(
            SiteId, Study.Value, Milestone, TestContext.Current.CancellationToken);

        response.IsComplete.Should().BeTrue();
        response.MissingDocuments.Should().Equal("Form-B");
        response.SiteId.Should().Be(new SiteCode(SiteId));
    }

    [Fact]
    public async Task ApplyEtmfWebhookAsync_And_ActivateSiteAsync_WhenKernelLockGuardReportsLocked_ReturnObservationLockedWithoutWrites()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();
        var lockGuard = new AlwaysLockedGuard();

        await using var context = CreateContext(databaseName);
        var service = CreateService(context, auditLog, lockGuard: lockGuard);

        var webhookResult = await service.ApplyEtmfWebhookAsync(
            new EtmfCompletenessWebhookPayload(Study.Value, SiteId, Milestone, IsComplete: true, MissingArtifacts: []),
            TestContext.Current.CancellationToken);
        webhookResult.IsFailure.Should().BeTrue();
        webhookResult.Error!.Code.Should().Be("K.Observation.Locked");

        var activateResult = await service.ActivateSiteAsync(
            SiteId,
            new SiteActivationRequest(Study.Value),
            TestContext.Current.CancellationToken);
        activateResult.IsFailure.Should().BeTrue();
        activateResult.Error!.Code.Should().Be("K.Observation.Locked");

        (await context.SiteComplianceCaches.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        auditLog.Calls.Should().BeEmpty("locked compliance mutations must short-circuit before writing BLOCKED_ACTIVATION or ETMF_COMPLIANCE_UPDATE audit rows");
    }

    private sealed class AlwaysLockedGuard : IKernelLockGuard
    {
        public Task<Cadence.Core.Domain.Result<bool>> EnsureUnlockedAsync(
            string? studyCode,
            string? siteCode = null,
            string? subjectCode = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Cadence.Core.Domain.Result<bool>>(
                Cadence.Core.Domain.DomainError.Conflict("K.Observation.Locked", "Scope is frozen or locked."));

        public Task<Cadence.Core.Domain.Result<bool>> EnsureUnlockedForServiceAsync(
            ServiceAccount serviceAccount,
            string? tenantCode,
            string? studyCode,
            string? siteCode = null,
            string? subjectCode = null,
            CancellationToken cancellationToken = default) =>
            EnsureUnlockedAsync(studyCode, siteCode, subjectCode, cancellationToken);
    }
}
