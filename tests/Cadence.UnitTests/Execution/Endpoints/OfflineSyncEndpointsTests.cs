using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Unit tests for offline batch synchronization endpoint with itemized signature token verification.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public sealed class OfflineSyncEndpointsTests
{
    private static readonly byte[] SecretBytes = "test-secret-32-bytes-long-secret-key-123!"u8.ToArray();
    private static readonly string SecretString = "test-secret-32-bytes-long-secret-key-123!";

    private static DefaultHttpContext CreateHttpContext(string username = "pi-01", string role = "Site Investigator")
    {
        var context = new DefaultHttpContext { User = TestSecurityContext.CreatePrincipal(username, role) };
        context.Request.Headers["X-User-Id"] = username;
        return context;
    }

    private static IConfiguration CreateConfiguration()
    {
        Environment.SetEnvironmentVariable("GATEWAY_SECRET", SecretString);
        var myConfiguration = new Dictionary<string, string?>
        {
            { "GATEWAY_SECRET", SecretString }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();
    }

    private static FormSubmissionApplicationService CreateService(InMemoryFormSubmissionRepository repo, FakeClock clock)
    {
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor();
        return new FormSubmissionApplicationService(
            formSubmissionRepository: repo,
            observationRepository: obsRepo,
            ucumService: ucum,
            auditContextAccessor: audit,
            clock: clock);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessOfflineBatchSync_NonSignatureDelta_SucceedsWithoutSigToken()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);
        var config = CreateConfiguration();
        var httpContext = CreateHttpContext("user-1");

        var deltaPayload = JsonSerializer.SerializeToElement(new { name = "draft test" });
        var delta = new OfflineDeltaItem(
            DeltaId: "delta_1",
            EntityType: "FormSubmission",
            EntityId: "F-1",
            Action: "UPDATE",
            Payload: deltaPayload,
            ClientTimestampUtc: clock.UtcNow.ToString("o"),
            ReasonForChange: "Draft update"
        );

        var request = new OfflineBatchSyncRequest(
            ClientBatchId: "batch_non_sig_1",
            DeviceId: "device_1",
            Deltas: new[] { delta }
        );

        var result = await OfflineSyncEndpoints.ProcessOfflineBatchSyncAsync(
            request, httpContext, repo, svc, config, clock, CancellationToken.None);

        result.Should().BeOfType<Ok<OfflineBatchSyncResponse>>();
        var okResult = (Ok<OfflineBatchSyncResponse>)result;
        okResult.Value!.Status.Should().Be("SUCCESS");
        okResult.Value.ProcessedCount.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessOfflineBatchSync_SignatureGatedDelta_WithValidSigToken_VerifiesTokenAndApproves()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);
        var config = CreateConfiguration();
        var httpContext = CreateHttpContext("pi-01", "Site Investigator");

        // Create and complete a form submission
        var createReq = new FormSubmissionCreateRequest("STUDY-001", "SUBJ-101", "FORM-VS", "SITE-01", "VISIT-01", "1.0.0", "{\"vssbp\": 120, \"observation_date\": \"2026-10-06T10:00:00Z\"}");
        var sub = (await svc.CreateAsync(createReq, CancellationToken.None)).Value!;
        await svc.CompleteAsync(sub.Id, CancellationToken.None);

        // Issue a valid signature token for pi-01
        var grant = new SigTokenGrant(
            Subject: "pi-01",
            Username: "pi-01",
            Roles: new[] { "Site Investigator" },
            Action: "/api/v1/offline/sync-batch",
            SemanticAction: "offline.sync.batch"
        );
        string sigToken = SigTokenIssuer.Issue(grant, SystemClock.Instance.UtcNow, SecretBytes);

        var deltaPayload = JsonSerializer.SerializeToElement(new { sig_token = sigToken });
        var delta = new OfflineDeltaItem(
            DeltaId: "delta_sig_1",
            EntityType: "FormSubmission",
            EntityId: sub.Id.ToString(),
            Action: "APPROVE",
            Payload: deltaPayload,
            ClientTimestampUtc: clock.UtcNow.ToString("o"),
            ReasonForChange: "Principal Investigator Approval",
            SigToken: sigToken
        );

        var request = new OfflineBatchSyncRequest(
            ClientBatchId: "batch_sig_1",
            DeviceId: "device_1",
            Deltas: new[] { delta }
        );

        var result = await OfflineSyncEndpoints.ProcessOfflineBatchSyncAsync(
            request, httpContext, repo, svc, config, clock, CancellationToken.None);

        result.Should().BeOfType<Ok<OfflineBatchSyncResponse>>();
        var okResult = (Ok<OfflineBatchSyncResponse>)result;
        okResult.Value!.Status.Should().Be("SUCCESS");
        okResult.Value.ProcessedCount.Should().Be(1);

        // Verify form submission status is Approved
        FormSubmissionId.TryParse(sub.Id, out var subFormId);
        var updatedSub = await repo.GetByIdAsync(subFormId, CancellationToken.None);
        updatedSub.Should().NotBeNull();
        updatedSub!.Status.Should().Be(FormSubmissionStatus.Approved);
        updatedSub.SignatureManifest.Should().NotBeNull();
        updatedSub.SignatureManifest!.SignerId.Should().Be("pi-01");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessOfflineBatchSync_SignatureGatedDelta_WithoutSigToken_ReturnsReauthenticationRequired()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);
        var config = CreateConfiguration();
        var httpContext = CreateHttpContext("pi-01", "Site Investigator");

        var deltaPayload = JsonSerializer.SerializeToElement(new { });
        var delta = new OfflineDeltaItem(
            DeltaId: "delta_sig_missing",
            EntityType: "FormSubmission",
            EntityId: "F-100",
            Action: "APPROVE",
            Payload: deltaPayload,
            ClientTimestampUtc: clock.UtcNow.ToString("o"),
            ReasonForChange: "PI Approval",
            SigToken: null
        );

        var request = new OfflineBatchSyncRequest(
            ClientBatchId: "batch_missing_sig",
            DeviceId: "device_1",
            Deltas: new[] { delta }
        );

        var result = await OfflineSyncEndpoints.ProcessOfflineBatchSyncAsync(
            request, httpContext, repo, svc, config, clock, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().NotBeOfType<Ok<OfflineBatchSyncResponse>>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessOfflineBatchSync_SignatureGatedDelta_WithExpiredSigToken_ReturnsReauthenticationRequired()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);
        var config = CreateConfiguration();
        var httpContext = CreateHttpContext("pi-01", "Site Investigator");

        var grant = new SigTokenGrant(
            Subject: "pi-01",
            Username: "pi-01",
            Roles: new[] { "Site Investigator" },
            Action: "/api/v1/offline/sync-batch",
            SemanticAction: "offline.sync.batch"
        );
        string expiredToken = SigTokenIssuer.Issue(grant, SystemClock.Instance.UtcNow.AddSeconds(-120), SecretBytes);

        var deltaPayload = JsonSerializer.SerializeToElement(new { sig_token = expiredToken });
        var delta = new OfflineDeltaItem(
            DeltaId: "delta_expired",
            EntityType: "FormSubmission",
            EntityId: "F-100",
            Action: "APPROVE",
            Payload: deltaPayload,
            ClientTimestampUtc: clock.UtcNow.ToString("o"),
            ReasonForChange: "PI Approval",
            SigToken: expiredToken
        );

        var request = new OfflineBatchSyncRequest(
            ClientBatchId: "batch_expired",
            DeviceId: "device_1",
            Deltas: new[] { delta }
        );

        var result = await OfflineSyncEndpoints.ProcessOfflineBatchSyncAsync(
            request, httpContext, repo, svc, config, clock, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().NotBeOfType<Ok<OfflineBatchSyncResponse>>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ProcessOfflineBatchSync_ReplayingConsumedSigToken_FailsClosedWith401()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var clock = new FakeClock();
        var svc = CreateService(repo, clock);
        var config = CreateConfiguration();
        var httpContext = CreateHttpContext("pi-01", "Site Investigator");

        var createReq1 = new FormSubmissionCreateRequest("STUDY-001", "SUBJ-101", "FORM-VS", "SITE-01", "VISIT-01", "1.0.0", "{\"vssbp\": 120, \"observation_date\": \"2026-10-06T10:00:00Z\"}");
        var sub1 = (await svc.CreateAsync(createReq1, CancellationToken.None)).Value!;
        await svc.CompleteAsync(sub1.Id, CancellationToken.None);

        var createReq2 = new FormSubmissionCreateRequest("STUDY-001", "SUBJ-102", "FORM-VS", "SITE-01", "VISIT-01", "1.0.0", "{\"vssbp\": 122, \"observation_date\": \"2026-10-06T10:00:00Z\"}");
        var sub2 = (await svc.CreateAsync(createReq2, CancellationToken.None)).Value!;
        await svc.CompleteAsync(sub2.Id, CancellationToken.None);

        var grant = new SigTokenGrant(
            Subject: "pi-01",
            Username: "pi-01",
            Roles: new[] { "Site Investigator" },
            Action: "/api/v1/offline/sync-batch",
            SemanticAction: "offline.sync.batch"
        );
        string sigToken = SigTokenIssuer.Issue(grant, SystemClock.Instance.UtcNow, SecretBytes);

        var delta1 = new OfflineDeltaItem(
            DeltaId: "delta_replay_1",
            EntityType: "FormSubmission",
            EntityId: sub1.Id.ToString(),
            Action: "APPROVE",
            Payload: JsonSerializer.SerializeToElement(new { sig_token = sigToken }),
            ClientTimestampUtc: clock.UtcNow.ToString("o"),
            ReasonForChange: "Principal Investigator Approval",
            SigToken: sigToken
        );

        var request1 = new OfflineBatchSyncRequest(
            ClientBatchId: "batch_replay_1",
            DeviceId: "device_1",
            Deltas: new[] { delta1 }
        );

        // Batch 1 succeeds
        var result1 = await OfflineSyncEndpoints.ProcessOfflineBatchSyncAsync(
            request1, httpContext, repo, svc, config, clock, CancellationToken.None);

        result1.Should().BeOfType<Ok<OfflineBatchSyncResponse>>();

        // Batch 2 attempts to reuse the same sigToken for sub2
        var delta2 = new OfflineDeltaItem(
            DeltaId: "delta_replay_2",
            EntityType: "FormSubmission",
            EntityId: sub2.Id.ToString(),
            Action: "APPROVE",
            Payload: JsonSerializer.SerializeToElement(new { sig_token = sigToken }),
            ClientTimestampUtc: clock.UtcNow.ToString("o"),
            ReasonForChange: "PI Approval Replay Attempt",
            SigToken: sigToken
        );

        var request2 = new OfflineBatchSyncRequest(
            ClientBatchId: "batch_replay_2",
            DeviceId: "device_1",
            Deltas: new[] { delta2 }
        );

        // Batch 2 must fail closed because token was consumed in batch 1
        var result2 = await OfflineSyncEndpoints.ProcessOfflineBatchSyncAsync(
            request2, httpContext, repo, svc, config, clock, CancellationToken.None);

        result2.Should().NotBeOfType<Ok<OfflineBatchSyncResponse>>();
    }
}
