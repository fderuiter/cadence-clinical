using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// End-to-end PostgreSQL host integration tests covering validation finding disposition routes, automated query
/// reconciliation on closed or concluded discrepancies, form and scope lock readiness gates, and capability-keyed
/// work queues across all seven registered kernel sources.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReadinessGatesAndWorkQueuesHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string SiteACrc = "crc.a@readiness-queues.test";
    private const string SiteAPi = "pi.a@readiness-queues.test";
    private const string BlindedMonitor = "cra.blinded@readiness-queues.test";
    private const string UnblindedReviewer = "pharm.unblinded@readiness-queues.test";
    private const string DataManager = "dm@readiness-queues.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose()
    {
        _host?.Dispose();
        if (fixture.ConnectionString is not null)
        {
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    [Fact(DisplayName = "FindingDecisions_AndClosedMonitorQueries_SuppressDuplicateAutoQueries_UntilObservationChanges")]
    [KernelRule("K-FIND-1")]
    [KernelRule("K-RULE-3")]
    public async Task FindingDecisionsAndClosedMonitorQueriesSuppressDuplicateAutoQueriesUntilObservationChanges()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedWorkspaceAsync(ct);
        await RelayOutboxAsync(ct);

        using var crc = CreateClient(seed.TenantCode, SiteACrc);
        using var monitor = CreateClient(seed.TenantCode, BlindedMonitor);

        // 1. Save out-of-range Systolic (240 mm[Hg]) + valid Diastolic (84 mm[Hg]) -> opens Error finding + episode-0 auto-query.
        await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Initial vital signs entry",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 240m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 84m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var initialQueries = await GetJsonAsync<FormQueryResponse[]>(monitor, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries", ct);
        var episodeZeroQuery = initialQueries.Should().ContainSingle().Subject;
        episodeZeroQuery.Status.Should().Be("open");
        episodeZeroQuery.Revision.Should().Be(1);

        var studyRef = NodeRef.For(seed.Study).ToString();
        var findingsPage = await GetJsonAsync<KernelValidationFindingPageResponse>(
            monitor,
            $"/api/v1/kernel/studies/{studyRef}/validation/findings",
            ct);
        var finding = findingsPage.Findings.Should().ContainSingle(f => f.RuleId == "VS_SBP_CRITICAL").Subject;
        finding.Severity.Should().Be("Error");
        finding.Status.Should().Be("Open");

        // 2. Monitor closes the automated query on unchanged revision 1; saving an unrelated field does NOT reopen a duplicate query.
        await SendJsonAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries/{episodeZeroQuery.QueryId:D}/close",
            "Confirmed against bedside monitor printout",
            token: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Added cuff position note",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Comment, value = new { type = "text", value = "Seated left arm" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var queriesAfterUnchangedSave = await GetJsonAsync<FormQueryResponse[]>(monitor, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries", ct);
        queriesAfterUnchangedSave.Should().ContainSingle().Which.Status.Should().Be("closed");

        // 3. Editing Systolic to a new violating revision (250 mm[Hg], rev 2) raises a fresh automated query.
        await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Repeat blood pressure measurement",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 250m, unit = "mm[Hg]" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var queriesOnRevTwo = await GetJsonAsync<FormQueryResponse[]>(monitor, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries", ct);
        queriesOnRevTwo.Should().HaveCount(2);
        var episodeOneQuery = queriesOnRevTwo.Single(q => q.QueryId != episodeZeroQuery.QueryId);
        episodeOneQuery.Status.Should().Be("open");
        episodeOneQuery.Revision.Should().Be(2);

        // 4. Finding lifecycle: Investigate -> Classify -> step-up guardrails -> signed Accept closes active automated query!
        var investigated = await SendJsonAsync<KernelValidationFindingResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{finding.Ref}/investigate",
            "Reviewing hypertensive reading with site PI",
            token: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        investigated.Status.Should().Be("Investigating");

        var classified = await SendJsonAsync<KernelValidationFindingResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{finding.Ref}/classify",
            "Confirmed true physiological elevation",
            token: null,
            new { classification = "DataIssue" },
            HttpStatusCode.OK,
            ct);
        classified.Classification.Should().Be("DataIssue");

        // Missing step-up token -> 401; unauthorized CRC with valid token -> 403 and zero orphan Attestations.
        using (var missingSig = await monitor.SendAsync(BuildRequest(
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{finding.Ref}/accept",
            "Missing token check",
            token: null,
            new { rationale = "Confirmed physiological value." }), ct))
        {
            missingSig.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        int attestationsBeforeForbidden;
        await using (var db = fixture.CreateContext(seed.Tenant))
        {
            attestationsBeforeForbidden = await db.Attestations.CountAsync(ct);
        }

        var crcAcceptToken = TestSecurityContext.CreateSigToken(
            SiteACrc,
            KernelValidationEndpoints.AcceptFindingAction(finding.Ref),
            KernelValidationEndpoints.AcceptFindingSemanticAction);
        using (var forbiddenAccept = await crc.SendAsync(BuildRequest(
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{finding.Ref}/accept",
            "Coordinator attempted accept",
            crcAcceptToken,
            new { rationale = "Coordinator cannot accept findings." }), ct))
        {
            forbiddenAccept.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReadProblemCodeAsync(forbiddenAccept, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        await using (var db = fixture.CreateContext(seed.Tenant))
        {
            (await db.Attestations.CountAsync(ct)).Should().Be(attestationsBeforeForbidden);
        }

        var acceptToken = TestSecurityContext.CreateSigToken(
            BlindedMonitor,
            KernelValidationEndpoints.AcceptFindingAction(finding.Ref),
            KernelValidationEndpoints.AcceptFindingSemanticAction);
        var accepted = await SendJsonAsync<KernelValidationFindingResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{finding.Ref}/accept",
            "Accepted after source chart review",
            acceptToken,
            new { rationale = "Source ECG and arterial cuff confirm 250 mm[Hg]." },
            HttpStatusCode.OK,
            ct);
        accepted.Status.Should().Be("Accepted");
        accepted.Decision!.AttestationRef.Should().StartWith("attestation:");

        await RelayOutboxAsync(ct);
        var queriesAfterAccept = await GetJsonAsync<FormQueryResponse[]>(monitor, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries", ct);
        queriesAfterAccept.Single(q => q.QueryId == episodeOneQuery.QueryId).Status.Should().Be("closed");

        // 5. Reopen -> Explain with Approved signature -> unchanged save stays quiet -> new violating revision (260, rev 3) reopens & queries!
        var reopened = await SendJsonAsync<KernelValidationFindingResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{finding.Ref}/reopen",
            "Switching disposition to formal medical explanation",
            token: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        reopened.Status.Should().Be("Open");

        var explainToken = TestSecurityContext.CreateSigToken(
            BlindedMonitor,
            KernelValidationEndpoints.ExplainFindingAction(finding.Ref),
            KernelValidationEndpoints.ExplainFindingSemanticAction);
        var explained = await SendJsonAsync<KernelValidationFindingResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{finding.Ref}/explain",
            "Explained with Approved signature",
            explainToken,
            new { explanation = "Participant missed morning antihypertensive dose; documented in AE log." },
            HttpStatusCode.OK,
            ct);
        explained.Status.Should().Be("Explained");
        explained.Decision!.AttestationRef.Should().StartWith("attestation:");

        await RelayOutboxAsync(ct);
        await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Updated comment without changing blood pressure",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Comment, value = new { type = "text", value = "Dose hold documented" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        (await GetJsonAsync<FormQueryResponse[]>(monitor, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries", ct))
            .Should().HaveCount(2).And.OnlyContain(q => q.Status == "closed");

        await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Third measurement rose further",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 260m, unit = "mm[Hg]" }, expected_revision = 2 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var queriesOnRevThree = await GetJsonAsync<FormQueryResponse[]>(monitor, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries", ct);
        queriesOnRevThree.Should().HaveCount(3);
        queriesOnRevThree.Should().ContainSingle(q => q.Status == "open" && q.Revision == 3);
    }

    [Fact(DisplayName = "ReadinessGates_BlockIncompleteDiscrepantSignaturesAndLocks_UntilAllBlockersAreCleared")]
    public async Task ReadinessGatesBlockIncompleteDiscrepantSignaturesAndLocksUntilAllBlockersAreCleared()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedWorkspaceAsync(ct);
        await RelayOutboxAsync(ct);

        using var crc = CreateClient(seed.TenantCode, SiteACrc);
        using var pi = CreateClient(seed.TenantCode, SiteAPi);
        using var monitor = CreateClient(seed.TenantCode, BlindedMonitor);
        using var dm = CreateClient(seed.TenantCode, DataManager);

        // Save violating Systolic (240 mm[Hg]) while leaving required Diastolic blank.
        var saved = await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Partial entry with high systolic BP",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 240m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        var sbpObs = saved.Values.Single().ObservationId;
        await RelayOutboxAsync(ct);

        // Form and casebook report incomplete status with missing required item, open query, and open Error finding.
        var dirtyForm = await GetJsonAsync<FormResponse>(pi, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}", ct);
        dirtyForm.Complete.Should().BeFalse();
        dirtyForm.MissingRequired.Should().Contain(Diastolic);
        dirtyForm.OpenQueryCount.Should().Be(1);
        dirtyForm.OpenErrorFindingCount.Should().Be(1);
        dirtyForm.ReadinessBlockers.Should().HaveCountGreaterThanOrEqualTo(3);

        var enrollmentARef = NodeRef.For(seed.EnrollmentA).ToString();
        var enrollmentForms = await GetJsonAsync<FormSummaryResponse[]>(pi, $"{FormEndpoints.RoutePrefix}?enrollment_id={seed.EnrollmentA.Value:D}", ct);
        var casebookForm = enrollmentForms.Single(f => f.FormId == seed.FormA);
        casebookForm.Complete.Should().BeFalse();
        casebookForm.MissingRequiredCount.Should().Be(1);
        casebookForm.OpenQueryCount.Should().Be(1);
        casebookForm.OpenErrorFindingCount.Should().Be(1);

        // Signing an unclean form fails with 409 K.Collection.NotReadyToSign.
        using (var signBlocked = await pi.SendAsync(BuildRequest(
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/signatures",
            "Attempted premature PI signature",
            TestSecurityContext.CreateSigToken(SiteAPi, FormSignatureEndpoints.SignAction(seed.FormA), FormSignatureEndpoints.SignSemanticAction),
            new { meaning = "approved", content_hash = dirtyForm.ContentHash }), ct))
        {
            signBlocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(signBlocked, ct)).Should().Be(CollectionErrors.NotReadyToSign.Code);
        }

        // Scope readiness reports missing required item, open query, and open Error finding; Frozen stays available (spec §10.5)
        // while Locked is rejected with 409 K.Assertion.ScopeNotReady + structured Explanation.
        var studyRef = NodeRef.For(seed.Study).ToString();
        var formARef = $"collection_instance:{seed.FormA:D}";
        var readinessDirty = await GetJsonAsync<KernelLockReadinessResponse>(
            dm,
            $"/api/v1/kernel/studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(enrollmentARef)}",
            ct);
        readinessDirty.ReadyForLock.Should().BeFalse();
        readinessDirty.CanFreeze.Should().BeTrue();
        readinessDirty.MissingRequiredItemCount.Should().Be(1);
        readinessDirty.OpenQueryCount.Should().Be(1);
        readinessDirty.OpenErrorFindingCount.Should().Be(1);
        readinessDirty.Blockers.Should().Contain(b => b.Kind == "MissingRequiredItem");
        readinessDirty.Blockers.Should().Contain(b => b.Kind == "OpenQuery");
        readinessDirty.Blockers.Should().Contain(b => b.Kind == "ErrorFinding");

        var frozenForm = await SendJsonAsync<KernelLockResponse>(
            dm,
            HttpMethod.Post,
            "/api/v1/kernel/locks",
            "Freeze form while resolving discrepancies",
            TestSecurityContext.CreateSigToken(DataManager, KernelLockEndpoints.ApplyLockAction, KernelLockEndpoints.ApplyLockSemanticAction),
            new { study_ref = studyRef, scope_ref = formARef, kind = "Frozen", meaning = "Approved" },
            HttpStatusCode.Created,
            ct);
        frozenForm.Kind.Should().Be("Frozen");
        await SendJsonAsync<KernelLockResponse>(
            dm,
            HttpMethod.Post,
            $"/api/v1/kernel/locks/{frozenForm.Ref}/release",
            "Release freeze to complete missing item",
            TestSecurityContext.CreateSigToken(DataManager, KernelLockEndpoints.ReleaseLockAction(frozenForm.Ref!), KernelLockEndpoints.ReleaseLockSemanticAction),
            new { study_ref = studyRef, meaning = "Responsibility" },
            HttpStatusCode.OK,
            ct);

        using (var lockBlocked = await dm.SendAsync(BuildRequest(
            HttpMethod.Post,
            "/api/v1/kernel/locks",
            "Attempted Locked on unclean study scope",
            TestSecurityContext.CreateSigToken(DataManager, KernelLockEndpoints.ApplyLockAction, KernelLockEndpoints.ApplyLockSemanticAction),
            new { study_ref = studyRef, scope_ref = studyRef, kind = "Locked", meaning = "Approved" }), ct))
        {
            lockBlocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await lockBlocked.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("code").GetString().Should().Be(AssertionErrors.ScopeNotReady.Code);
            doc.RootElement.GetProperty("explanation").GetProperty("conditions").GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        }

        // Fill Diastolic and close the auto-query while Systolic (240 mm[Hg]) still violates the Error rule -> ErrorFinding alone still blocks Locked!
        await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Completed required diastolic",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Diastolic, value = new { type = "quantity", value = 76m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var autoQueries = await GetJsonAsync<FormQueryResponse[]>(monitor, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries", ct);
        var autoQueryId = autoQueries.Single(q => q.Status == "open").QueryId;
        await SendJsonAsync<FormQueryResponse>(
            crc,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries/{autoQueryId:D}/answer",
            "Answering auto-query before correcting value",
            token: null,
            new { text = "Rechecking blood pressure cuff reading." },
            HttpStatusCode.OK,
            ct);
        await SendJsonAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries/{autoQueryId:D}/close",
            "Closed auto-query while Error finding remains active",
            token: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var readinessErrorOnly = await GetJsonAsync<KernelLockReadinessResponse>(
            dm,
            $"/api/v1/kernel/studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(enrollmentARef)}",
            ct);
        readinessErrorOnly.ReadyForLock.Should().BeFalse();
        readinessErrorOnly.MissingRequiredItemCount.Should().Be(0);
        readinessErrorOnly.OpenQueryCount.Should().Be(0);
        readinessErrorOnly.OpenErrorFindingCount.Should().Be(1);

        using (var errorFindingBlockedLock = await dm.SendAsync(BuildRequest(
            HttpMethod.Post,
            "/api/v1/kernel/locks",
            "Attempted Locked with open Error finding",
            TestSecurityContext.CreateSigToken(DataManager, KernelLockEndpoints.ApplyLockAction, KernelLockEndpoints.ApplyLockSemanticAction),
            new { study_ref = studyRef, scope_ref = enrollmentARef, kind = "Locked", meaning = "Approved" }), ct))
        {
            errorFindingBlockedLock.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(errorFindingBlockedLock, ct)).Should().Be(AssertionErrors.ScopeNotReady.Code);
        }

        // Correct Systolic to 118 mm[Hg] -> resolves Error finding, then verify an Answered manual query still blocks both Sign and Locked!
        await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Corrected systolic to in-range value",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 118m, unit = "mm[Hg]" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var manualQuery = await SendJsonAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries",
            "Manual source check",
            token: null,
            new { observation_id = sbpObs, revision = 2, text = "Confirm calibration timestamp." },
            HttpStatusCode.Created,
            ct);
        await RelayOutboxAsync(ct);

        await SendJsonAsync<FormQueryResponse>(
            crc,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries/{manualQuery.QueryId:D}/answer",
            "Answered calibration query",
            token: null,
            new { text = "Calibrated at morning clinic opening." },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var answeredForm = await GetJsonAsync<FormResponse>(pi, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}", ct);
        answeredForm.Complete.Should().BeFalse("answered queries still await monitor closure");
        answeredForm.AnsweredQueryCount.Should().Be(1);

        using (var answeredSignBlocked = await pi.SendAsync(BuildRequest(
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/signatures",
            "Attempted sign with answered query open",
            TestSecurityContext.CreateSigToken(SiteAPi, FormSignatureEndpoints.SignAction(seed.FormA), FormSignatureEndpoints.SignSemanticAction),
            new { meaning = "approved", content_hash = answeredForm.ContentHash }), ct))
        {
            answeredSignBlocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(answeredSignBlocked, ct)).Should().Be(CollectionErrors.NotReadyToSign.Code);
        }

        // Close the answered manual query (and complete FormB on Site B so study-wide lock is also clean).
        await SendJsonAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/queries/{manualQuery.QueryId:D}/close",
            "Calibration verified",
            token: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        await SendJsonAsync<FormSavedResponse>(
            dm,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormB:D}/values",
            "Completed Site B vital signs",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 122m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 78m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        var cleanForm = await GetJsonAsync<FormResponse>(pi, $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}", ct);
        cleanForm.Complete.Should().BeTrue();
        cleanForm.ReadinessBlockers.Should().BeEmpty();

        var cleanEnrollmentForms = await GetJsonAsync<FormSummaryResponse[]>(pi, $"{FormEndpoints.RoutePrefix}?enrollment_id={seed.EnrollmentA.Value:D}", ct);
        cleanEnrollmentForms.Single(f => f.FormId == seed.FormA).Complete.Should().BeTrue();

        var readinessClean = await GetJsonAsync<KernelLockReadinessResponse>(
            dm,
            $"/api/v1/kernel/studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}",
            ct);
        readinessClean.ReadyForLock.Should().BeTrue();
        readinessClean.OpenErrorFindingCount.Should().Be(0);
        readinessClean.Blockers.Should().BeEmpty();

        var signature = await SendJsonAsync<FormSignatureResponse>(
            pi,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/signatures",
            "Investigator sign-off on clean form",
            TestSecurityContext.CreateSigToken(SiteAPi, FormSignatureEndpoints.SignAction(seed.FormA), FormSignatureEndpoints.SignSemanticAction),
            new { meaning = "approved", content_hash = cleanForm.ContentHash },
            HttpStatusCode.Created,
            ct);
        signature.Meaning.Should().Be("Approved");

        var appliedLock = await SendJsonAsync<KernelLockResponse>(
            dm,
            HttpMethod.Post,
            "/api/v1/kernel/locks",
            "Study clean-data lock",
            TestSecurityContext.CreateSigToken(DataManager, KernelLockEndpoints.ApplyLockAction, KernelLockEndpoints.ApplyLockSemanticAction),
            new { study_ref = studyRef, scope_ref = studyRef, kind = "Locked", meaning = "Approved" },
            HttpStatusCode.Created,
            ct);
        appliedLock.Kind.Should().Be("Locked");
    }

    [Fact(DisplayName = "CapabilityWorkQueues_RouteAllSevenSources_MaskBlindedItemsInPlace_AndEnforce200CandidateCap")]
    [KernelRule("K-AUTH-4")]
    [KernelRule("K-AUTH-8")]
    public async Task CapabilityWorkQueuesRouteAllSevenSourcesMaskBlindedItemsInPlaceAndEnforce200CandidateCap()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedWorkspaceAsync(ct);
        await RelayOutboxAsync(ct);

        using var crcA = CreateClient(seed.TenantCode, SiteACrc);
        using var piA = CreateClient(seed.TenantCode, SiteAPi);
        using var blindedMonitor = CreateClient(seed.TenantCode, BlindedMonitor);
        using var unblinded = CreateClient(seed.TenantCode, UnblindedReviewer);
        using var dm = CreateClient(seed.TenantCode, DataManager);

        // Define all 7 canonical work queues via HTTP POST /api/v1/kernel/work-queues.
        var queueDefinitions = new (string Code, string Title, string Capability, string VerbClass, string Source)[]
        {
            ("protocol_reviews", "Protocol reviews", "protocol.approve", "Approve", KernelWorkItemSources.ProtocolVersionsInReview),
            ("open_queries", "Queries to answer", "observation.query.answer", "Execute", KernelWorkItemSources.QueriesOpen),
            ("answered_queries", "Answered queries to close", "assertion.retract", "Execute", KernelWorkItemSources.QueriesAnswered),
            ("open_findings", "Validation findings to investigate", "validation_finding.investigate", "Execute", KernelWorkItemSources.ValidationFindingsOpen),
            ("forms_to_sign", "Forms awaiting signature", "collection.sign", "Execute", KernelWorkItemSources.FormsAwaitingSignature),
            ("source_verifications", "Observations awaiting SDV", "observation.verify", "Execute", KernelWorkItemSources.ObservationsAwaitingVerification),
            ("pending_proposals", "AI proposals awaiting approval", "proposal.approve", "Approve", KernelWorkItemSources.ProposalsPending),
        };

        foreach (var def in queueDefinitions)
        {
            var created = await SendJsonAsync<KernelWorkQueueDefinitionResponse>(
                dm,
                HttpMethod.Post,
                "/api/v1/kernel/work-queues",
                $"Define {def.Code}",
                token: null,
                new
                {
                    code = def.Code,
                    title = def.Title,
                    capability = def.Capability,
                    verb_class = def.VerbClass,
                    source = def.Source,
                },
                HttpStatusCode.Created,
                ct);
            created.Code.Should().Be(def.Code);
        }

        // 1. Clean complete FormA on Site A -> appears in forms_to_sign for Site A PI (not Site B).
        var savedA = await SendJsonAsync<FormSavedResponse>(
            crcA,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormA:D}/values",
            "Clean Form A values",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 120m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 80m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);

        // 2. Violating FormB on Site B -> opens Error finding + automated open query on Site B, plus an answered query on FormB.
        var savedB = await SendJsonAsync<FormSavedResponse>(
            dm,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{seed.FormB:D}/values",
            "Out-of-range Form B values",
            token: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 245m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 88m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        // Answered query on Site B (for Monitor); Site B already has an open auto-query on Systolic.
        savedA.Values.Should().HaveCount(2);
        var dbpB = savedB.Values.Single(v => v.ItemId == Diastolic).ObservationId;
        var siteBQuery = await SendJsonAsync<FormQueryResponse>(
            blindedMonitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormB:D}/queries",
            "Site B diastolic check",
            token: null,
            new { observation_id = dbpB, revision = 1, text = "Verify Site B diastolic cuff log." },
            HttpStatusCode.Created,
            ct);
        await SendJsonAsync<FormQueryResponse>(
            dm,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{seed.FormB:D}/queries/{siteBQuery.QueryId:D}/answer",
            "Site B answered query",
            token: null,
            new { text = "Verified against clinic chart." },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(ct);

        // 3. Submit a protocol draft for review on a second study (portfolio queue) + create an open AI proposal + record a blinded treatment observation on Site A.
        var (_, reviewStudy) = await _scenario.Protocol.NewStudyAsync(seed.Tenant, ct);
        var inReviewProtocol = await _scenario.Protocol.InReviewAsync(seed.Tenant, reviewStudy, ct);

        var planBytes = Encoding.UTF8.GetBytes("{\"steps\":[\"review-site-b-sbp\"]}");
        var planArtifact = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                seed.Tenant,
                ArtifactId.New(),
                planArtifact,
                ArtifactKind.Other,
                seed.Study,
                "Triage plan",
                new ArtifactContent(ContentHash.OfBytes(planBytes), planBytes.Length, "application/json", "blob://plans/triage.json"),
                KernelProtocolScenario.Key(),
                "Register plan"),
            ct);

        var agent = Actor.AutomationAgent("triage-copilot").ActingFor(Actor.Person(DataManager));
        await _scenario.Protocol.OkAsync(
            new IssueGrant(seed.Tenant, GrantId.New(), agent.AsIdentity(), "validation_finding.investigate", null, AuthorityScope.Study(seed.Study), _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Agent grant"),
            ct);
        var proposalId = ProposalId.New();
        fixture.CurrentActor.Actor = agent;
        try
        {
            await _scenario.Protocol.OkAsync(
                new Propose(
                    seed.Tenant,
                    proposalId,
                    seed.Study,
                    "Prioritize hypertensive review on Site B",
                    planArtifact,
                    [NodeRef.For(seed.Study)],
                    null,
                    ["validation_finding.investigate"],
                    fixture.Clock.UtcNow.AddDays(5),
                    "cadence-clinical-1",
                    KernelProtocolScenario.Key(),
                    "Automated triage suggestion"),
                ct);
        }
        finally
        {
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }

        var blindedConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(
                seed.Tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                "Assigned arm (randomization_treatment_assignment)",
                "Blinded randomization allocation.",
                KernelProtocolScenario.Key(),
                "Blinding concept"),
            ct);
        var blindedObsId = ObservationId.New();
        await _scenario.Protocol.OkAsync(
            new RecordObservation(
                seed.Tenant,
                blindedObsId,
                seed.EnrollmentA,
                blindedConcept,
                new TextValue("Active Arm 200mg"),
                _scenario.Tick(),
                KernelProtocolScenario.Key(),
                "IRT treatment assignment"),
            ct);

        var studyRef = NodeRef.For(seed.Study).ToString();

        // Verify Site A PI sees forms_to_sign for clean FormA with deep-link Target, and zero items from Site B or ungranted queues!
        var piQueues = await GetJsonAsync<KernelWorkQueuesResponse>(piA, $"/api/v1/kernel/work-queues?within={Uri.EscapeDataString(studyRef)}", ct);
        var piSignQueue = piQueues.Queues.Single(q => q.Code == "forms_to_sign");
        var formSignItem = piSignQueue.Items.Should().ContainSingle().Subject;
        formSignItem.Masked.Should().BeNull();
        formSignItem.Target.Should().NotBeNull();
        formSignItem.Target!.FormId.Should().Be(seed.FormA);
        formSignItem.Target.EnrollmentId.Should().Be(seed.EnrollmentA.Value);
        piQueues.Queues.Single(q => q.Code == "open_queries").Items.Should().BeEmpty("Site B's auto-query is outside Site A PI's StudySite grant");
        piQueues.Queues.Single(q => q.Code == "protocol_reviews").Items.Should().BeEmpty();
        piQueues.Queues.Single(q => q.Code == "pending_proposals").Items.Should().BeEmpty();

        // Verify Data Manager sees portfolio protocol_reviews, study pending_proposals, and open_queries (on Site B).
        var dmQueues = await GetJsonAsync<KernelWorkQueuesResponse>(dm, "/api/v1/kernel/work-queues", ct);
        dmQueues.Queues.Single(q => q.Code == "protocol_reviews").Items.Should().ContainSingle()
            .Which.Target!.ProtocolVersionRef.Should().StartWith($"protocol_version:{inReviewProtocol.ProtocolVersionId.Value:D}");
        dmQueues.Queues.Single(q => q.Code == "pending_proposals").Items.Should().ContainSingle()
            .Which.Target!.ProposalRef.Should().StartWith($"proposal:{proposalId.Value:D}");
        dmQueues.Queues.Single(q => q.Code == "open_queries").Items.Should().ContainSingle();

        // Verify Blinded Monitor sees answered_queries, open_findings, and source_verifications with the blinded observation masked in place!
        var monitorQueues = await GetJsonAsync<KernelWorkQueuesResponse>(blindedMonitor, $"/api/v1/kernel/work-queues?within={Uri.EscapeDataString(studyRef)}", ct);
        monitorQueues.Queues.Single(q => q.Code == "answered_queries").Items.Should().ContainSingle();
        monitorQueues.Queues.Single(q => q.Code == "open_findings").Items.Should().ContainSingle()
            .Which.Target!.FindingRef.Should().StartWith("validation_finding:");

        var monitorVerifications = monitorQueues.Queues.Single(q => q.Code == "source_verifications");
        monitorVerifications.Items.Should().HaveCount(5, "2 observations on FormA + 2 on FormB + 1 blinded treatment observation");
        var maskedBlinded = monitorVerifications.Items.Should().ContainSingle(i => i.Masked == "Blinded").Subject;
        maskedBlinded.Subject.Should().BeNull();
        maskedBlinded.Title.Should().BeNull();
        maskedBlinded.Target.Should().BeNull();

        // Verify Unblinded Reviewer sees all 5 source_verifications items unmasked, including the treatment arm observation!
        var unblindedQueues = await GetJsonAsync<KernelWorkQueuesResponse>(
            unblinded,
            $"/api/v1/kernel/work-queues?within={Uri.EscapeDataString(studyRef)}&code=source_verifications",
            ct);
        var unblindedVerifications = unblindedQueues.Queues.Should().ContainSingle().Subject;
        unblindedVerifications.Items.Should().HaveCount(5).And.OnlyContain(i => i.Masked == null && i.Subject != null);

        // Seed 200 additional unverified observations (total 205 > MaxExamined = 200) and verify truncated: true at limit=200.
        var noteConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(seed.Tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.DataElement, "Clinic observation", "Routine observation.", KernelProtocolScenario.Key(), "Cap concept"),
            ct);
        for (var idx = 0; idx < 200; idx++)
        {
            await _scenario.Protocol.OkAsync(
                new RecordObservation(
                    seed.Tenant,
                    ObservationId.New(),
                    seed.EnrollmentA,
                    noteConcept,
                    new TextValue($"obs-{idx}"),
                    _scenario.Tick(),
                    KernelProtocolScenario.Key(),
                    "Bulk observation for 200-item queue cap"),
                ct);
        }

        var cappedQueues = await GetJsonAsync<KernelWorkQueuesResponse>(
            unblinded,
            $"/api/v1/kernel/work-queues?within={Uri.EscapeDataString(studyRef)}&code=source_verifications&limit=200",
            ct);
        var cappedVerifications = cappedQueues.Queues.Should().ContainSingle().Subject;
        cappedVerifications.Items.Should().HaveCount(200);
        cappedVerifications.Truncated.Should().BeTrue();
    }

    private sealed record WorkspaceSeed(
        string TenantCode,
        TenantId Tenant,
        StudyId Study,
        ProtocolVersionId EffectiveVersion,
        StudySiteId SiteA,
        StudySiteId SiteB,
        EnrollmentId EnrollmentA,
        EnrollmentId EnrollmentB,
        Guid FormA,
        Guid FormB);

    private async Task<WorkspaceSeed> SeedWorkspaceAsync(CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-3));
        var tenantCode = $"T-RG-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, tenantCode, "Readiness gates and queues tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var effectiveVersion = await CreateEffectiveProtocolAsync(tenant, study, ct);

        var siteA = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var siteB = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollmentA = await _scenario.Observations.ScreenAsync(tenant, siteA, ct);
        var enrollmentB = await _scenario.Observations.ScreenAsync(tenant, siteB, ct);

        var grantMatrix = new (Actor Principal, string[] Capabilities, AuthorityScope Scope)[]
        {
            (Actor.Person(SiteACrc), ["collection.open", "collection.record", "observation.query.answer", "execution.query.answer", "attestation.sign"], AuthorityScope.StudySite(siteA)),
            (Actor.Person(SiteAPi), ["collection.record", "collection.sign", "observation.correct", "observation.query.answer", "execution.query.answer", "attestation.sign"], AuthorityScope.StudySite(siteA)),
            (Actor.Person(BlindedMonitor), [
                "observation.verify",
                "assertion.retract",
                "execution.query.raise",
                "execution.query.manage",
                "validation_finding.investigate",
                "validation_finding.classify",
                "validation_finding.accept",
                "validation_finding.explain",
                "validation_finding.reopen",
                "attestation.sign"
            ], AuthorityScope.Study(study)),
            (Actor.Person(UnblindedReviewer), ["observation.verify", "enrollment.unblind"], AuthorityScope.Study(study)),
            (Actor.Person(DataManager), [
                "collection.open",
                "collection.record",
                "observation.query.answer",
                "execution.query.answer",
                "lock.apply",
                "lock.release",
                "protocol.approve",
                "proposal.approve",
                "proposal.reject",
                "work_queue.define",
                "attestation.sign"
            ], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(EvaluateEditChecksOnCollectionChanged.Name), ["validation_run.record", "execution.query.raise", "execution.query.manage"], AuthorityScope.Tenant()),
        };

        foreach (var (principal, capabilities, scope) in grantMatrix)
        {
            foreach (var cap in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), principal, cap, null, scope, _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Workspace grant"),
                    ct);
            }
        }

        await PinErrorRulePackAsync(tenant, study, ct);

        var subjectA = new KernelObservationScenario.Subject(tenant, study, siteA, enrollmentA);
        var subjectB = new KernelObservationScenario.Subject(tenant, study, siteB, enrollmentB);
        var formA = await _scenario.Protocol.OkAsync(Open(subjectA, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var formB = await _scenario.Protocol.OkAsync(Open(subjectB, CollectionInstanceId.New(), _scenario.Tick()), ct);

        return new WorkspaceSeed(
            tenantCode,
            tenant,
            study,
            effectiveVersion,
            siteA,
            siteB,
            enrollmentA,
            enrollmentB,
            formA.CollectionInstanceId.Value,
            formB.CollectionInstanceId.Value);
    }

    private async Task<ProtocolVersionId> CreateEffectiveProtocolAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var sbp = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic blood pressure.", KernelProtocolScenario.Key(), "Concept"),
            ct);
        var dbp = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Diastolic BP", "Diastolic blood pressure.", KernelProtocolScenario.Key(), "Concept"),
            ct);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.main", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(VisitId, "Week 12 visit", "ep.main", [ActivityLocation.Site], [SpecId], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new CollectionItem(Systolic, sbp, "mm[Hg]", Required: true),
                        new CollectionItem(Diastolic, dbp, "mm[Hg]", Required: true),
                        new CollectionItem(Comment, sbp, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };

        var versionId = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, versionId, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var submitted = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, versionId, KernelProtocolScenario.Key(), "Submit"), ct);
        var approvalSig = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(submitted)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, versionId, approvalSig, KernelProtocolScenario.Key(), "Approve"), ct);

        var effectivity = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, versionId, EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Declare"),
            ct);
        var artifactBytes = Encoding.UTF8.GetBytes($"irb-{Guid.NewGuid():N}");
        var artifactVersion = ArtifactVersionId.New();
        var artifactContent = new ArtifactContent(ContentHash.OfBytes(artifactBytes), artifactBytes.Length, "application/pdf", "blob://irb/approval.pdf");
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), artifactVersion, ArtifactKind.EthicsApproval, study, "IRB approval", artifactContent, KernelProtocolScenario.Key(), "Register"),
            ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, _scenario.Tick(), [artifactVersion], KernelProtocolScenario.Key(), "Activate"),
            ct);

        return versionId;
    }

    private async Task PinErrorRulePackAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var sbpVar = new VariableExpression(Systolic);
        var rulePack = new RulePack(
            RuleAuthority.Study,
            "Critical Vital Signs Edit Checks",
            [
                new Rule(
                    "VS_SBP_CRITICAL",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Data Validation Plan", "Section 2", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, SpecId),
                    ValidationSeverity.Error,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [sbpVar]),
                            new ComparisonExpression(ComparisonOperator.Greater, sbpVar, new LiteralExpression(RuleValue.FromNumber(220m))),
                        ]),
                    "Systolic blood pressure exceeds 220 mm[Hg] critical threshold.",
                    "Confirm measurement or record medical explanation.",
                    EffectivePeriod.Always),
            ]);

        var itemId = LibraryItemId.New();
        var drafted = await _scenario.Protocol.OkAsync(
            new DraftLibraryItem(tenant, itemId, LibraryItemKind.RulePack, "critical-vitals-pack", rulePack.Title, RulePackJson.ToJson(rulePack), [], KernelProtocolScenario.Key(), "Draft pack"),
            ct);
        var packSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, itemId.Value, drafted.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(new ApproveLibraryItem(tenant, itemId, packSig, KernelProtocolScenario.Key(), "Approve pack"), ct);

        var stdId = StandardVersionId.New();
        var stdPkg = new StandardPackage(
            "CDASHIG",
            "2.3",
            "CDASHIG",
            "CDISC",
            "/mdr/cdashig/2-3",
            new DateOnly(2023, 1, 1),
            [
                new DatasetDefinitionData(
                    "VS",
                    "Vital Signs",
                    "Findings",
                    "Vital signs measurements",
                    1,
                    [new VariableDefinitionData("VSORRES", "Result", 1, StandardDataType.Num, "Qualifier", VariableCore.Expected, [], [], null, null)]),
            ],
            [],
            []);
        await _scenario.Protocol.OkAsync(new RegisterStandardVersion(tenant, stdId, stdPkg, null, KernelProtocolScenario.Key(), "Register std"), ct);
        var stdInfo = (await fixture.Standards.FindStandardVersionAsync(tenant, stdId, ct))!;
        var stdSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.StandardVersion, stdId.Value, stdInfo.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(new ApproveStandardVersion(tenant, stdId, stdSig, KernelProtocolScenario.Key(), "Approve std"), ct);
        await _scenario.Protocol.OkAsync(
            new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [stdId], [], [itemId], KernelProtocolScenario.Key(), "Pin pack"),
            ct);
    }

    private async Task RelayOutboxAsync(CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var batch = await fixture.OutboxRelay.RelayBatchAsync(500, ct);
            batch.Failed.Should().Be(0);
            if (batch.Dispatched == 0 && !batch.MoreDue)
            {
                break;
            }
        }
    }

    private HttpClient CreateClient(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddDeclaredKernelErrorsCheck();
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static Task<T> GetJsonAsync<T>(HttpClient client, string path, CancellationToken ct) =>
        SendJsonAsync<T>(client, HttpMethod.Get, path, reason: null, token: null, body: null, HttpStatusCode.OK, ct);

    private static async Task<T> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        string? reason,
        string? token,
        object? body,
        HttpStatusCode expected,
        CancellationToken ct)
    {
        using var response = await client.SendAsync(BuildRequest(method, path, reason, token, body), ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, payload);
        return JsonSerializer.Deserialize<T>(payload, Json)!;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, string? reason, string? token, object? body)
    {
        var req = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (method != HttpMethod.Get)
        {
            req.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
        }

        if (reason is not null)
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (token is not null)
        {
            req.Headers.Add(GatewayHeaders.SigToken, token);
        }

        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
