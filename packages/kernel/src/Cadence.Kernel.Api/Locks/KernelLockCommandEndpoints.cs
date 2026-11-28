using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Kernel.Api.Locks;

internal static class KernelLockCommandEndpoints
{
    internal static async Task<Results<Created<KernelLockResponse>, ProblemHttpResult>> ApplyAsync(
        KernelApplyLockRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        ILockRecords locks,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } token)
        {
            return ElectronicSignatureRequiredProblem();
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelProblems.BadRequest("Idempotency-Key required", "Supply an Idempotency-Key header for lock commands.");
        }

        var reason = changeReason.Read(context);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelProblems.BadRequest("Reason for change required", "Supply a signed X-Change-Reason header explaining why this scope is being frozen or locked.");
        }

        if (request is null
            || !NodeRef.TryParse(request.StudyRef, out var studyNode)
            || studyNode.Kind != NodeKinds.Study
            || studyNode.Version is not null)
        {
            return KernelProblems.BadRequest("Invalid study reference", "Name study_ref as an unversioned study NodeRef (study:<uuid>).");
        }

        if (!NodeRef.TryParse(request.ScopeRef, out var scope) || !LockScopes.IsScope(scope))
        {
            return KernelProblems.BadRequest(
                "Invalid lock scope",
                "Name scope_ref as an unversioned study, study_site, enrollment, collection_instance or observation NodeRef.");
        }

        if (!Enum.TryParse<LockKind>(request.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return KernelProblems.BadRequest("Invalid lock kind", "Lock kind must be Frozen or Locked.");
        }

        if (!TryParseMeaning(request.Meaning, out var meaning))
        {
            return KernelProblems.BadRequest("Invalid signature meaning", "Signature meaning must be Approved or Responsibility.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var studyId = StudyId.FromGuid(studyNode.Id);
        var lockId = request.LockId is { } givenLock && givenLock != Guid.Empty
            ? AssertionId.FromGuid(givenLock)
            : AssertionId.FromGuid(DeriveDeterministicGuid(tenant, idempotencyKey, "lock"));
        var attestationId = request.AttestationId is { } givenAtt && givenAtt != Guid.Empty
            ? AttestationId.FromGuid(givenAtt)
            : AttestationId.FromGuid(DeriveDeterministicGuid(tenant, idempotencyKey, "attest"));

        var applyResult = await sender.SendAsync(
            new ApplyLock(
                tenant,
                lockId,
                studyId,
                scope,
                kind,
                attestationId,
                idempotencyKey,
                reason,
                meaning,
                new DateTimeOffset(token.IssuedAt, TimeSpan.Zero),
                KernelLockEndpoints.StepUpMethod),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (applyResult.Error is { } applyError)
        {
            if (string.Equals(applyError.Code, AssertionErrors.ScopeNotReady.Code, StringComparison.Ordinal))
            {
                applyError = await KernelLockReadinessEndpoints.MaskScopeNotReadyErrorAsync(
                    context,
                    tenant,
                    studyId,
                    studyNode,
                    scope,
                    locks,
                    applyError,
                    cancellationToken).ConfigureAwait(false);
            }

            return KernelErrorEndpoints.Problem(applyError);
        }

        var recorded = await locks.FindAsync(tenant, applyResult.Value!.LockId, cancellationToken).ConfigureAwait(false);
        var response = recorded is not null
            ? KernelLockResponse.FromVisible(recorded)
            : KernelLockResponse.FromState(studyId, applyResult.Value, meaning, reason);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/studies/{studyNode}/locks",
            response);
    }

    internal static async Task<Results<Ok<KernelLockResponse>, ProblemHttpResult>> ReleaseAsync(
        string lockRef,
        KernelReleaseLockRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        ILockRecords locks,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } token)
        {
            return ElectronicSignatureRequiredProblem();
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelProblems.BadRequest("Idempotency-Key required", "Supply an Idempotency-Key header for lock release commands.");
        }

        var reason = changeReason.Read(context);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelProblems.BadRequest("Reason for change required", "Supply a signed X-Change-Reason header explaining why this lock is being released.");
        }

        if (ParseLockId(lockRef) is not { } lockId)
        {
            return KernelProblems.BadRequest("Invalid lock reference", $"'{lockRef}' is not a valid lock reference (assertion:<uuid> or <uuid>).");
        }

        if (!TryParseMeaning(request?.Meaning, out var meaning))
        {
            return KernelProblems.BadRequest("Invalid signature meaning", "Signature meaning must be Approved or Responsibility.");
        }

        var tenant = KernelRequestTenant.Get(context);
        StudyId? requestedStudyId = null;
        if (!string.IsNullOrWhiteSpace(request?.StudyRef))
        {
            if (!NodeRef.TryParse(request.StudyRef, out var studyNode) || studyNode.Kind != NodeKinds.Study || studyNode.Version is not null)
            {
                return KernelProblems.BadRequest("Invalid study reference", "Name study_ref as an unversioned study NodeRef (study:<uuid>).");
            }

            requestedStudyId = StudyId.FromGuid(studyNode.Id);
        }

        var existing = await locks.FindAsync(tenant, lockId, cancellationToken).ConfigureAwait(false);
        if (existing is null || (requestedStudyId is { } expectedStudy && existing.StudyId != expectedStudy))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.NotFound);
        }

        var studyId = existing.StudyId;

        var releaseId = request?.AssertionId is { } givenRel && givenRel != Guid.Empty
            ? AssertionId.FromGuid(givenRel)
            : AssertionId.FromGuid(DeriveDeterministicGuid(tenant, idempotencyKey, "release"));
        var attestationId = request?.AttestationId is { } givenAtt && givenAtt != Guid.Empty
            ? AttestationId.FromGuid(givenAtt)
            : AttestationId.FromGuid(DeriveDeterministicGuid(tenant, idempotencyKey, "release-attest"));

        var releaseResult = await sender.SendAsync(
            new ReleaseLock(
                tenant,
                releaseId,
                studyId,
                lockId,
                attestationId,
                idempotencyKey,
                reason,
                meaning,
                new DateTimeOffset(token.IssuedAt, TimeSpan.Zero),
                KernelLockEndpoints.StepUpMethod),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (releaseResult.Error is { } releaseError)
        {
            return KernelErrorEndpoints.Problem(releaseError);
        }

        var updated = await locks.FindAsync(tenant, lockId, cancellationToken).ConfigureAwait(false);
        var response = updated is not null
            ? KernelLockResponse.FromVisible(updated)
            : KernelLockResponse.FromState(studyId, releaseResult.Value!, meaning, reason);
        return TypedResults.Ok(response);
    }

    private static string IdempotencyKey(HttpContext context) =>
        context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();

    private static AssertionId? ParseLockId(string text)
    {
        if (NodeRef.TryParse(text, out var node))
        {
            return node.Kind == NodeKinds.Assertion && node.Version is null ? AssertionId.FromGuid(node.Id) : null;
        }

        return Guid.TryParse(text, out var raw) && raw != Guid.Empty ? AssertionId.FromGuid(raw) : null;
    }

    private static bool TryParseMeaning(string? raw, out AttestationMeaning meaning)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            meaning = AttestationMeaning.Approved;
            return true;
        }

        if (Enum.TryParse(raw.Trim(), ignoreCase: true, out meaning)
            && meaning is AttestationMeaning.Approved or AttestationMeaning.Responsibility)
        {
            return true;
        }

        meaning = default;
        return false;
    }

    private static Guid DeriveDeterministicGuid(TenantId tenant, string idempotencyKey, string slot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"cadence:lock:{tenant}:{slot}:{idempotencyKey}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static ProblemHttpResult ElectronicSignatureRequiredProblem() =>
        TypedResults.Problem(
            title: "Re-authentication required",
            detail: SigTokenVerifier.ReauthenticationRequired,
            statusCode: StatusCodes.Status401Unauthorized);
}
