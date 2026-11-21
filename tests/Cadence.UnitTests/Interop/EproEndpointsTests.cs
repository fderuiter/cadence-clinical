using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Interop;

/// <summary>
/// Covers all eight participant ePRO routes in <see cref="EproEndpoints.MapEproEndpoints"/>, offline sync sequence
/// idempotency, conflict archival (<c>EproSubmissionDefeated</c>), stale-version quarantine (<c>EproSubmissionQuarantine</c>),
/// canonical Kernel observation forwarding (<c>K-TRUTH-1</c>), <c>X-Change-Reason</c> enforcement, Keycloak realm
/// <c>subject_code</c> seeding, and fail-closed <c>Subject</c> ownership isolation before any SQL executes.
/// </summary>
public sealed class EproEndpointsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RecordingCommandInterceptor _sqlInterceptor = new();
    private readonly RecordingHttpMessageHandler _kernelHttpHandler = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly EproTestFactory _factory;

    public EproEndpointsTests()
    {
        _connection.Open();
        using (var setupDb = CreateDbContext(withInterceptor: false))
        {
            setupDb.Database.EnsureCreated();
            InteropDbSeeder.SeedAsync(setupDb, _clock, CancellationToken.None).GetAwaiter().GetResult();
        }

        _factory = new EproTestFactory(_connection, _sqlInterceptor, _kernelHttpHandler, _clock);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SeededParticipant_ReadsAllFiveQueryEndpointsSuccessfully()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = CreateSubjectClient("subj-uuid-001", "SUBJ-101-001");

        var assignmentsRes = await client.GetAsync("/api/v1/interop/assignments/subject/SUBJ-101-001", ct);
        assignmentsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var assignments = await assignmentsRes.Content.ReadFromJsonAsync<List<EproAssignmentDto>>(cancellationToken: ct);
        assignments.Should().NotBeNull().And.HaveCount(2);
        assignments!.Select(a => a.InstrumentId).Should().Contain(["inst_daily_diary", "inst_weekly_symptoms"]);

        var instrumentsRes = await client.GetAsync("/api/v1/interop/subjects/SUBJ-101-001/instruments", ct);
        instrumentsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var instruments = await instrumentsRes.Content.ReadFromJsonAsync<List<EproInstrumentDto>>(cancellationToken: ct);
        instruments.Should().NotBeNull().And.HaveCount(2);

        var singleInstrumentRes = await client.GetAsync("/api/v1/interop/instruments/inst_daily_diary", ct);
        singleInstrumentRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dailyInstrument = await singleInstrumentRes.Content.ReadFromJsonAsync<EproInstrumentDto>(cancellationToken: ct);
        dailyInstrument.Should().NotBeNull();
        dailyInstrument!.Name.Should().Be("Daily Health & Vital Diary");

        var complianceRes = await client.GetAsync("/api/v1/interop/subjects/SUBJ-101-001/compliance", ct);
        complianceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var compliance = await complianceRes.Content.ReadFromJsonAsync<EproComplianceResponse>(cancellationToken: ct);
        compliance.Should().NotBeNull();
        compliance!.SubjectId.Should().Be("SUBJ-101-001");
        compliance.TotalCount.Should().Be(2);

        var notificationsRes = await client.GetAsync("/api/v1/interop/subjects/SUBJ-101-001/notifications", ct);
        notificationsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var notifications = await notificationsRes.Content.ReadFromJsonAsync<List<EproNotificationDto>>(cancellationToken: ct);
        notifications.Should().NotBeNull().And.HaveCount(2);
        notifications!.All(n => !n.IsRead).Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task AcknowledgeNotification_RequiresChangeReason_AndUpdatesReadStateAndAuditTrail()
    {
        var ct = TestContext.Current.CancellationToken;
        using var noReasonClient = CreateSubjectClient("subj-uuid-001", "SUBJ-101-001", changeReason: null);
        using var missingReasonReq = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/interop/notifications/notif_subj_101_001_01/acknowledge");
        var missingReasonRes = await noReasonClient.SendAsync(missingReasonReq, ct);
        missingReasonRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var reasonClient = CreateSubjectClient(
            "subj-uuid-001",
            "SUBJ-101-001",
            changeReason: "Participant acknowledged scheduled reminder in inbox");
        using var ackReq = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/interop/notifications/notif_subj_101_001_01/acknowledge")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json"),
        };
        var ackRes = await reasonClient.SendAsync(ackReq, ct);
        ackRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ackRes.Content.ReadFromJsonAsync<EproNotificationDto>(cancellationToken: ct);
        updated.Should().NotBeNull();
        updated!.IsRead.Should().BeTrue();
        updated.DeliveryStatus.Should().Be("ACKNOWLEDGED");
        updated.ReadAt.Should().Be(_clock.UtcNow);

        await using var verifyDb = CreateDbContext(withInterceptor: false);
        var audit = await verifyDb.InteropAuditLogs.SingleAsync(a => a.Action == "ACKNOWLEDGE_NOTIFICATION", ct);
        audit.UserId.Should().Be("subj-uuid-001");
        audit.ChangeReason.Should().Be("Participant acknowledged scheduled reminder in inbox");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [KernelRule("K-TRUTH-1")]
    public async Task SubmitAndSync_EnforceSequenceIdempotency_AndForwardCanonicalObservationsToKernel()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = CreateSubjectClient(
            "subj-uuid-001",
            "SUBJ-101-001",
            changeReason: "Participant submitted Daily Health & Vital Diary");

        string payloadJson = """
            {
              "subject_id": "SUBJ-101-001",
              "diary_id": "inst_daily_diary",
              "version_index": 1,
              "device_timestamp": "2026-11-20T11:55:00Z",
              "answers": {
                "vssbp": 122,
                "vsdpb": 78,
                "vshr": 68,
                "pain_scale": 2,
                "has_symptoms": "No"
              },
              "offline_sync_markers": {
                "client_id": "byod-ios-01",
                "sequence_number": 101,
                "conflict_strategy": "CLIENT_WINS"
              }
            }
            """;

        using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/interop/epro/submit")
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
        };
        var submitRes = await client.SendAsync(submitReq, ct);
        submitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstResult = await submitRes.Content.ReadFromJsonAsync<EproReconciliationResultDto>(cancellationToken: ct);
        firstResult.Should().NotBeNull();
        firstResult!.Status.Should().Be("CREATED");
        firstResult.DuplicateSequenceIgnored.Should().BeFalse();
        firstResult.CanonicalObservationsCount.Should().Be(5);

        // K-TRUTH-1: every resolved diary answer was translated and forwarded to POST /api/v1/kernel/observations
        _kernelHttpHandler.RecordedRequests.Should().HaveCount(5);
        _kernelHttpHandler.RecordedRequests.Should().OnlyContain(r =>
            r.Path == "/api/v1/kernel/observations"
            && !string.IsNullOrWhiteSpace(r.IdempotencyKey)
            && r.Body.Contains("\"enrollment_ref\":\"enrollment:", StringComparison.Ordinal));
        _kernelHttpHandler.RecordedRequests.Select(r => r.Body)
            .Should().Contain(b => b.Contains("\"unit\":\"mm[Hg]\"", StringComparison.Ordinal))
            .And.Contain(b => b.Contains("\"type\":\"boolean\"", StringComparison.Ordinal));

        // Replay exact same (client_id, sequence_number) inside /epro/sync batch -> idempotent no-op
        int dispatchedBeforeReplay = _kernelHttpHandler.RecordedRequests.Count;
        string syncReplayJson = $$"""
            {
              "submissions": [ {{payloadJson}} ]
            }
            """;
        using var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/interop/epro/sync")
        {
            Content = new StringContent(syncReplayJson, Encoding.UTF8, "application/json"),
        };
        var replayRes = await client.SendAsync(replayReq, ct);
        replayRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBatch = await replayRes.Content.ReadFromJsonAsync<EproSyncBatchResponse>(cancellationToken: ct);
        replayBatch.Should().NotBeNull();
        replayBatch!.Results.Should().ContainSingle();
        replayBatch.Results[0].Status.Should().Be("SYNCED");
        replayBatch.Results[0].DuplicateSequenceIgnored.Should().BeTrue();
        _kernelHttpHandler.RecordedRequests.Count.Should().Be(dispatchedBeforeReplay);

        await using var verifyDb = CreateDbContext(withInterceptor: false);
        (await verifyDb.EproSubmissions.CountAsync(s => s.SubjectId == "SUBJ-101-001", ct)).Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SyncConflictStrategies_ArchiveDefeatedPayloads_ForClientWinsServerWinsAndMerge()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = CreateSubjectClient(
            "subj-uuid-001",
            "SUBJ-101-001",
            changeReason: "Offline queue reconciliation");

        // 1. Seed initial weekly symptoms submission
        string initialJson = """
            {
              "submissions": [{
                "subject_id": "SUBJ-101-001",
                "diary_id": "inst_weekly_symptoms",
                "version_index": 1,
                "device_timestamp": "2026-11-20T09:00:00Z",
                "answers": { "severity": "Mild", "restricted_activity": "No" },
                "offline_sync_markers": { "client_id": "dev-a", "sequence_number": 1, "conflict_strategy": "CLIENT_WINS" }
              }]
            }
            """;
        (await PostRawAsync(client, "/api/v1/interop/epro/sync", initialJson, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. CLIENT_WINS conflict archives original server answers and overwrites with client answers
        string clientWinsJson = """
            {
              "submissions": [{
                "subject_id": "SUBJ-101-001",
                "diary_id": "inst_weekly_symptoms",
                "version_index": 1,
                "device_timestamp": "2026-11-20T10:00:00Z",
                "answers": { "severity": "Moderate", "restricted_activity": "No" },
                "offline_sync_markers": { "client_id": "dev-b", "sequence_number": 2, "conflict_strategy": "CLIENT_WINS" }
              }]
            }
            """;
        _clock.Advance(TimeSpan.FromSeconds(5));
        var cwRes = await PostRawAsync(client, "/api/v1/interop/epro/sync", clientWinsJson, ct);
        var cwBody = await cwRes.Content.ReadFromJsonAsync<EproSyncBatchResponse>(cancellationToken: ct);
        cwBody!.Results[0].Status.Should().Be("UPDATED_CLIENT_WINS");
        cwBody.Results[0].Answers.GetProperty("severity").GetString().Should().Be("Moderate");

        // 3. SERVER_WINS conflict archives incoming client payload and preserves server answers
        string serverWinsJson = """
            {
              "submissions": [{
                "subject_id": "SUBJ-101-001",
                "diary_id": "inst_weekly_symptoms",
                "version_index": 1,
                "device_timestamp": "2026-11-20T10:30:00Z",
                "answers": { "severity": "Severe", "restricted_activity": "Yes" },
                "offline_sync_markers": { "client_id": "dev-c", "sequence_number": 3, "conflict_strategy": "SERVER_WINS" }
              }]
            }
            """;
        _clock.Advance(TimeSpan.FromSeconds(5));
        var swRes = await PostRawAsync(client, "/api/v1/interop/epro/sync", serverWinsJson, ct);
        var swBody = await swRes.Content.ReadFromJsonAsync<EproSyncBatchResponse>(cancellationToken: ct);
        swBody!.Results[0].Status.Should().Be("IGNORED_SERVER_WINS");
        swBody.Results[0].Answers.GetProperty("severity").GetString().Should().Be("Moderate");

        // 4. MERGE conflict archives pre-merge server payload and merges non-empty incoming fields
        string mergeJson = """
            {
              "submissions": [{
                "subject_id": "SUBJ-101-001",
                "diary_id": "inst_weekly_symptoms",
                "version_index": 1,
                "device_timestamp": "2026-11-20T11:00:00Z",
                "answers": { "missed_doses": "No" },
                "offline_sync_markers": { "client_id": "dev-d", "sequence_number": 4, "conflict_strategy": "MERGE" }
              }]
            }
            """;
        _clock.Advance(TimeSpan.FromSeconds(5));
        var mergeRes = await PostRawAsync(client, "/api/v1/interop/epro/sync", mergeJson, ct);
        var mergeBody = await mergeRes.Content.ReadFromJsonAsync<EproSyncBatchResponse>(cancellationToken: ct);
        mergeBody!.Results[0].Status.Should().Be("MERGED");
        mergeBody.Results[0].Answers.GetProperty("severity").GetString().Should().Be("Moderate");
        mergeBody.Results[0].Answers.GetProperty("missed_doses").GetString().Should().Be("No");

        await using var verifyDb = CreateDbContext(withInterceptor: false);
        var defeated = (await verifyDb.EproSubmissionDefeateds.ToListAsync(ct))
            .OrderBy(d => d.CreatedAt)
            .ToList();
        defeated.Select(d => d.Status)
            .Should().Equal("DEFEATED_BY_CLIENT_WINS", "DEFEATED_BY_SERVER_WINS", "DEFEATED_BY_MERGE");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SyncStaleInstrumentVersion_QuarantinesSubmission_AndDoesNotForwardToKernel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var bumpDb = CreateDbContext(withInterceptor: false))
        {
            var inst = await bumpDb.Instruments.SingleAsync(i => i.Id == "inst_daily_diary", ct);
            inst.VersionIndex = 2;
            await bumpDb.SaveChangesAsync(ct);
        }

        _kernelHttpHandler.Clear();
        using var client = CreateSubjectClient(
            "subj-uuid-001",
            "SUBJ-101-001",
            changeReason: "Offline submission against superseded v1 schema");

        string staleJson = """
            {
              "submissions": [{
                "subject_id": "SUBJ-101-001",
                "diary_id": "inst_daily_diary",
                "version_index": 1,
                "device_timestamp": "2026-11-20T08:00:00Z",
                "answers": { "vssbp": 118, "vsdpb": 76, "vshr": 64, "pain_scale": 1 },
                "offline_sync_markers": { "client_id": "dev-stale", "sequence_number": 9 }
              }]
            }
            """;

        var res = await PostRawAsync(client, "/api/v1/interop/epro/sync", staleJson, ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<EproSyncBatchResponse>(cancellationToken: ct);
        body!.Results[0].Status.Should().Be("QUARANTINED");
        _kernelHttpHandler.RecordedRequests.Should().BeEmpty();

        await using var verifyDb = CreateDbContext(withInterceptor: false);
        var quarantined = await verifyDb.EproSubmissionQuarantines.SingleAsync(ct);
        quarantined.SubjectId.Should().Be("SUBJ-101-001");
        quarantined.Status.Should().Be("QUARANTINED");
        quarantined.ValidationErrorsJson.Should().Contain("submitted version 1 does not match active version 2");

        (await verifyDb.ClinicalQueries.AnyAsync(q =>
            q.SubjectId == "SUBJ-101-001"
            && q.TestCode == "inst_daily_diary"
            && q.Explanation != null
            && q.Explanation.Contains("INSTRUMENT_VERSION_MISMATCH"), ct))
            .Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SubjectCaller_RequestingOrMutatingAnotherParticipant_Returns403BeforeAnySqlExecutes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var subjectClient = CreateSubjectClient(
            "subj-uuid-001",
            "SUBJ-101-001",
            changeReason: "Cross-subject probe");

        _sqlInterceptor.Clear();

        (await subjectClient.GetAsync("/api/v1/interop/assignments/subject/SUBJ-101-002", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await subjectClient.GetAsync("/api/v1/interop/subjects/SUBJ-101-002/instruments", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await subjectClient.GetAsync("/api/v1/interop/subjects/SUBJ-101-002/compliance", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await subjectClient.GetAsync("/api/v1/interop/subjects/SUBJ-101-002/notifications", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostRawAsync(subjectClient, "/api/v1/interop/notifications/notif_subj_101_001_01/acknowledge?subject_id=SUBJ-101-002", "{}", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostRawAsync(subjectClient, "/api/v1/interop/epro/submit",
            """{"subject_id":"SUBJ-101-002","diary_id":"inst_daily_diary","version_index":1,"answers":{"vssbp":120}}""", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostRawAsync(subjectClient, "/api/v1/interop/epro/sync",
            """{"submissions":[{"subject_id":"SUBJ-101-002","diary_id":"inst_daily_diary","version_index":1,"answers":{"vssbp":120}}]}""", ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _sqlInterceptor.ExecutedCommands.Should().BeEmpty(
            "a Subject caller targeting another participant's SubjectCode must be rejected with HTTP 403 before any database read or write executes");
    }

    [Fact]
    public void RealmSeed_DefinesSubjectCodeMapperAndSeededSubj101001User()
    {
        string repoRoot = FindRepoRoot();
        string realmPath = Path.Combine(repoRoot, "docker", "cadence-realm.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(realmPath));
        var root = doc.RootElement;

        var mappers = root.GetProperty("clients").EnumerateArray()
            .Single(c => c.GetProperty("clientId").GetString() == "cadence-web")
            .GetProperty("protocolMappers").EnumerateArray()
            .ToList();

        mappers.Should().Contain(m =>
            m.GetProperty("protocolMapper").GetString() == "oidc-usermodel-attribute-mapper"
            && m.GetProperty("config").GetProperty("claim.name").GetString() == "subject_code"
            && m.GetProperty("config").GetProperty("user.attribute").GetString() == "subject_code");

        var seededSubject = root.GetProperty("users").EnumerateArray()
            .Single(u => u.GetProperty("username").GetString() == "subj.101.001");

        seededSubject.GetProperty("realmRoles").EnumerateArray()
            .Select(r => r.GetString()).Should().Contain("Subject");
        seededSubject.GetProperty("attributes").GetProperty("subject_code")[0].GetString()
            .Should().Be("SUBJ-101-001");
        seededSubject.GetProperty("attributes").GetProperty("study_ids")[0].GetString()
            .Should().Be("CADENCE-101");
        seededSubject.GetProperty("attributes").GetProperty("site_id")[0].GetString()
            .Should().Be("SITE-101");
        seededSubject.GetProperty("attributes").GetProperty("tenant_id")[0].GetString()
            .Should().Be("cadence-local");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RedeemedActivationBinding_WithoutTokenSubjectCodeClaim_AllowsBoundParticipantAndRejectsOtherBeforeSql()
    {
        var ct = TestContext.Current.CancellationToken;
        _kernelHttpHandler.SetRedeemedBinding("byod-oidc-uuid-42", "SUBJ-101-001");

        var identityWithoutSubjectClaim = new GatewayIdentity(
            UserId: "byod-oidc-uuid-42",
            Roles: ["Subject"],
            SiteIds: ["SITE-101"],
            TenantId: "cadence-local",
            StudyIds: ["CADENCE-101"],
            SubjectCode: null);
        using var redeemedClient = _factory.CreateSignedClient(identityWithoutSubjectClaim);

        _sqlInterceptor.Clear();
        var crossSubjectRes = await redeemedClient.GetAsync("/api/v1/interop/assignments/subject/SUBJ-101-002", ct);
        crossSubjectRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _sqlInterceptor.ExecutedCommands.Should().BeEmpty();

        var ownRes = await redeemedClient.GetAsync("/api/v1/interop/assignments/subject/SUBJ-101-001", ct);
        ownRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownAssignments = await ownRes.Content.ReadFromJsonAsync<List<EproAssignmentDto>>(cancellationToken: ct);
        ownAssignments.Should().NotBeNull().And.HaveCount(2);
    }

    private HttpClient CreateSubjectClient(string userId, string subjectCode, string? changeReason = null)
    {
        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: ["Subject"],
            SiteIds: ["SITE-101"],
            TenantId: "cadence-local",
            StudyIds: ["CADENCE-101"],
            ChangeReason: changeReason,
            SubjectCode: subjectCode);
        return _factory.CreateSignedClient(identity);
    }

    private static async Task<HttpResponseMessage> PostRawAsync(
        HttpClient client,
        string path,
        string json,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(req, ct);
    }

    private InteropDbContext CreateDbContext(bool withInterceptor)
    {
        var builder = new DbContextOptionsBuilder<InteropDbContext>().UseSqlite(_connection);
        if (withInterceptor)
        {
            builder.AddInterceptors(_sqlInterceptor);
        }

        return new InteropDbContext(builder.Options);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cadence.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate Cadence.sln");
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyCollection<string> ExecutedCommands => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record RecordedKernelCall(string Path, string? IdempotencyKey, string Body);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<RecordedKernelCall> _calls = new();
        private readonly ConcurrentDictionary<string, string> _redeemedBindings = new(StringComparer.Ordinal);

        public IReadOnlyCollection<RecordedKernelCall> RecordedRequests => _calls;

        public void Clear() => _calls.Clear();

        public void SetRedeemedBinding(string userId, string subjectCode) => _redeemedBindings[userId] = subjectCode;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (string.Equals(path, "/api/v1/execution/subjects/activation-codes/me", StringComparison.Ordinal))
            {
                string? callerId = request.Headers.TryGetValues(GatewayHeaders.UserId, out var uids)
                    ? uids.FirstOrDefault()
                    : null;
                if (!string.IsNullOrWhiteSpace(callerId) && _redeemedBindings.TryGetValue(callerId, out string? boundSubject))
                {
                    string json = JsonSerializer.Serialize(new
                    {
                        subject_id = boundSubject,
                        study_id = "CADENCE-101",
                        site_id = "SITE-101",
                    });
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string? idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var vals)
                ? vals.FirstOrDefault()
                : null;
            _calls.Enqueue(new RecordedKernelCall(path, idempotencyKey, body));

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"status\":\"recorded\"}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class EproTestFactory(
        SqliteConnection connection,
        RecordingCommandInterceptor interceptor,
        RecordingHttpMessageHandler kernelHandler,
        IClock clock) : WebApplicationFactory<Cadence.Apps.Interop.Program>
    {
        protected override IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KERNEL_SERVICE_URL"] = "http://kernel.test",
                    ["EXECUTION_SERVICE_URL"] = "http://execution.test",
                    ["GATEWAY_SECRET"] = TestSecurityContext.GatewaySecret,
                }))
                .ConfigureWebHost(webHost => webHost
                    .UseTestServer()
                    .UseTestGatewaySecret()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddCadenceSecurity();
                        services.AddSingleton(clock);
                        services.AddScoped(_ =>
                            new InteropDbContext(
                                new DbContextOptionsBuilder<InteropDbContext>()
                                    .UseSqlite(connection)
                                    .AddInterceptors(interceptor)
                                    .Options));
                        services.AddHttpClient(CanonicalObservationForwarder.HttpClientName)
                            .ConfigurePrimaryHttpMessageHandler(() => kernelHandler);
                        services.AddScoped<ICanonicalObservationForwarder, CanonicalObservationForwarder>();
                        services.AddHttpClient(ActivationBindingResolver.HttpClientName)
                            .ConfigurePrimaryHttpMessageHandler(() => kernelHandler);
                        services.AddScoped<IActivationBindingResolver, ActivationBindingResolver>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCadenceSecurity();
                        app.UseEndpoints(endpoints => endpoints.MapEproEndpoints());
                    }));
    }
}
