using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Core.Domain;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Offline batch synchronization endpoints enforcing 21 CFR Part 11 signature token verification.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-053
/// </summary>
public static class OfflineSyncEndpoints
{
    private static readonly ConcurrentDictionary<string, byte> ProcessedBatches = new();
    private static readonly ReplayCache SharedReplayCache = new();

    public static IEndpointRouteBuilder MapOfflineSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/offline/sync-batch", ProcessOfflineBatchSyncAsync)
            .WithName("SyncOfflineBatch")
            .WithTags("Offline Sync")
            .Produces<OfflineBatchSyncResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/v1/execution/offline/sync", ProcessOfflineBatchSyncAsync)
            .ExcludeFromDescription();

        return app;
    }

    public static async Task<IResult> ProcessOfflineBatchSyncAsync(
        OfflineBatchSyncRequest request,
        HttpContext httpContext,
        IFormSubmissionRepository formSubmissionRepository,
        IFormSubmissionApplicationService formSubmissionAppService,
        IConfiguration configuration,
        Cadence.Kernel.Time.IClock? clock,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ClientBatchId) || request.Deltas is null)
        {
            return Results.BadRequest(new { detail = "Invalid batch sync request." });
        }

        var clockService = clock ?? Cadence.Kernel.Time.SystemClock.Instance;

        // Idempotency check
        if (ProcessedBatches.ContainsKey(request.ClientBatchId))
        {
            return Results.Ok(new OfflineBatchSyncResponse(request.ClientBatchId, "ALREADY_PROCESSED", 0));
        }

        string userId = httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Request.Headers["X-User-Id"].ToString();

        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = "svc:offline-sync";
        }

        string secretStr = configuration["GATEWAY_SECRET"]
            ?? Environment.GetEnvironmentVariable("GATEWAY_SECRET")
            ?? string.Empty;

        byte[] secretBytes = Encoding.UTF8.GetBytes(secretStr);

        var verifier = new SigTokenVerifier(SharedReplayCache);
        int processedCount = 0;

        foreach (var delta in request.Deltas)
        {
            if (delta.IsSignatureGated())
            {
                string? token = delta.ResolveSigToken();
                if (string.IsNullOrWhiteSpace(token))
                {
                    return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
                }

                try
                {
                    // Verify signature, expiration (60s), user identity, and single-use replay
                    verifier.VerifyAndConsumeSigToken(
                        token,
                        userId,
                        secretBytes);
                }
                catch (Exception)
                {
                    return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
                }

                // Apply signature-gated eCRF approval state change
                if (FormSubmissionId.TryParse(delta.EntityId, out var subId))
                {
                    var submission = await formSubmissionRepository.GetForUpdateAsync(subId, ct).ConfigureAwait(false);
                    if (submission != null)
                    {
                        string computedHash = FormSubmissionApplicationService.ComputePayloadHash(submission.Payload ?? "{}")
                            ?? "0000000000000000000000000000000000000000000000000000000000000000";
                        string hmacSignature = !string.IsNullOrEmpty(secretStr)
                            ? Cadence.Security.HmacSigner.ComputeSignature(computedHash, secretStr)
                            : "mock-sig";

                        string signingReason = !string.IsNullOrWhiteSpace(delta.ReasonForChange)
                            ? delta.ReasonForChange
                            : "PI Approval via Batch Sync";

                        var manifest = SignatureManifest.Create(
                            userId,
                            "site_investigator",
                            signingReason,
                            clockService.UtcNow.UtcDateTime,
                            computedHash,
                            userId,
                            hmacSignature);

                        var approveResult = submission.Approve(manifest);
                        if (approveResult.IsSuccess)
                        {
                            submission.ReasonForChange = signingReason;
                            processedCount++;
                        }
                    }
                }
            }
            else
            {
                // Process non-signature delta (e.g. draft updates or creation)
                if (string.Equals(delta.Action, "UPDATE", StringComparison.OrdinalIgnoreCase) && FormSubmissionId.TryParse(delta.EntityId, out var subId))
                {
                    string newPayload = delta.Payload.ValueKind == JsonValueKind.Object
                        ? delta.Payload.GetRawText()
                        : "{}";
                    await formSubmissionAppService.UpdatePayloadAsync(subId.ToString(), newPayload, ct).ConfigureAwait(false);
                }
                processedCount++;
            }
        }

        ProcessedBatches.TryAdd(request.ClientBatchId, 1);
        await formSubmissionRepository.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new OfflineBatchSyncResponse(request.ClientBatchId, "SUCCESS", processedCount));
    }
}
