using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
using Cadence.Security.Authentication;
using Cadence.Testing;
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

    private static ExecutionDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new GxPAuditInterceptor(() => "user-compliance-1", SystemClock.Instance))
            .Options);

    private static SiteComplianceApplicationService CreateService(
        ExecutionDbContext context, RecordingAuditLogWriter? auditLog = null) =>
        new(new SiteComplianceRepository(context, SaveChangesResiliencePipelineFactory.Create()),
            auditLog ?? new RecordingAuditLogWriter(),
            new FakeCurrentAuditContextAccessor("user-compliance-1", "system_operation"));

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
                new SiteComplianceRepository(context, SaveChangesResiliencePipelineFactory.Create()),
                new EfAuditLogWriter(context),
                new FakeCurrentAuditContextAccessor("user-compliance-1", "system_operation"));

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
        auditLog.Calls[0].Action.Should().Be("SITE_GREENLIGHT_ISSUED");
        auditLog.Calls[0].RecordId.Should().Be(SiteId);

        // ActivateSiteAsync never itself flips IsComplete -- only the eTMF webhook does that.
        await using var readContext = CreateContext(databaseName);
        var row = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        row.IsComplete.Should().BeTrue();
        row.Version.Should().Be(1, "activation must not touch the compliance row's own version counter");
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

    // ---- 6-Facet Prerequisite Evaluation & Executive Overrides ------------------------------------

    [Fact]
    public async Task ActivateSiteAsync_AllSixFacetsComplete_GeneratesSignedHMACGreenlightToken()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();

        string[] facets = ["REGULATORY_IRB", "CTA_BUDGET", "EISF_BASELINE", "DOA_LOG", "STAFF_TRAINING", "IP_RELEASE"];

        await using (var seed = CreateContext(databaseName))
        {
            foreach (var facet in facets)
            {
                seed.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                    Study, new SiteCode(SiteId), facet, isComplete: true, Array.Empty<string>()));
            }
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(databaseName);
        var service = CreateService(context, auditLog);

        var result = await service.ActivateSiteAsync(
            SiteId, new SiteActivationRequest(Study.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Activated.Should().BeTrue();
        result.Value.GreenlightToken.Should().NotBeNullOrWhiteSpace();
        result.Value.IsOverridden.Should().BeFalse();

        var tokenPayload = GreenlightToken.Verify(result.Value.GreenlightToken, Study.Value, SiteId);
        tokenPayload.Should().NotBeNull();
        tokenPayload!.StudyId.Should().Be(Study.Value);
        tokenPayload.SiteId.Should().Be(SiteId);
        tokenPayload.IsOverridden.Should().BeFalse();

        auditLog.Calls.Should().ContainSingle(c => c.Action == "SITE_GREENLIGHT_ISSUED");
    }

    [Fact]
    public async Task ActivateSiteAsync_OneFacetIncomplete_BlocksActivationAndLogsBlockedActivation()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();

        // 5 facets complete, 1 missing
        string[] facets = ["REGULATORY_IRB", "CTA_BUDGET", "EISF_BASELINE", "DOA_LOG", "STAFF_TRAINING"];

        await using (var seed = CreateContext(databaseName))
        {
            foreach (var facet in facets)
            {
                seed.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                    Study, new SiteCode(SiteId), facet, isComplete: true, Array.Empty<string>()));
            }
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
    public async Task ActivateSiteAsync_IncompleteFacetsWithValidDualExecutiveTokens_SucceedsWithOverrideFlag()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();

        byte[] secret = Encoding.UTF8.GetBytes(
            Environment.GetEnvironmentVariable("GATEWAY_SECRET")
            ?? Environment.GetEnvironmentVariable("INTERNAL_GATEWAY_SECRET")
            ?? "internal-gateway-secret-abcdefabcdef!!");

        var qaGrant = new SigTokenGrant("qa-director-01", "qa-director-01", ["qa_director"], "SITE_ACTIVATION_OVERRIDE", "SITE_ACTIVATION_OVERRIDE");
        var medGrant = new SigTokenGrant("med-monitor-02", "med-monitor-02", ["lead_medical_monitor"], "SITE_ACTIVATION_OVERRIDE", "SITE_ACTIVATION_OVERRIDE");

        string qaToken = SigTokenIssuer.Issue(qaGrant, SystemClock.Instance.UtcNow, secret);
        string medToken = SigTokenIssuer.Issue(medGrant, SystemClock.Instance.UtcNow, secret);

        var request = new SiteActivationRequest(
            Study.Value,
            QaDirectorSigToken: qaToken,
            MedicalMonitorSigToken: medToken,
            Justification: "Urgent GxP compassionate release authorized by executive board",
            QaDirectorUserId: "qa-director-01",
            MedicalMonitorUserId: "med-monitor-02");

        await using var context = CreateContext(databaseName);
        var service = CreateService(context, auditLog);

        var result = await service.ActivateSiteAsync(SiteId, request, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsOverridden.Should().BeTrue();
        result.Value.GreenlightToken.Should().NotBeNullOrWhiteSpace();

        var tokenPayload = GreenlightToken.Verify(result.Value.GreenlightToken, Study.Value, SiteId, secret: secret);
        tokenPayload.Should().NotBeNull();
        tokenPayload!.IsOverridden.Should().BeTrue();

        auditLog.Calls.Should().ContainSingle(c => c.Action == "SITE_GREENLIGHT_ISSUED");
    }
}
