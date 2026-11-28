using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

internal static class KernelProtocolVersionLifecycleEndpoints
{
    private static readonly Capability ApproveProtocolVersionCapability = new("protocol.approve", VerbClass.Approve);
    private static readonly Capability WithdrawProtocolVersionCapability = new("protocol.withdraw", VerbClass.Approve);

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> SubmitProtocolVersionForReviewAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        if (KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason) is var (_, _, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var (idempotencyKey, reason, _) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
        var tenant = KernelRequestTenant.Get(context);

        var result = await sender.SendAsync(
            new SubmitProtocolVersionForReview(tenant, versionId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToProtocolVersionResponse(updated, result.Value!));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ReturnProtocolVersionToDraftAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new ReturnProtocolVersionToDraft(tenant, versionId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToProtocolVersionResponse(updated, result.Value!));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ApproveProtocolVersionAsync(
        string versionRef,
        KernelApproveProtocolVersionRequest? request,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this protocol version approval.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        if (request?.Meaning is { Length: > 0 } rawMeaning
            && (!Enum.TryParse<AttestationMeaning>(rawMeaning, ignoreCase: true, out var parsedMeaning)
                || parsedMeaning != AttestationMeaning.Approved))
        {
            return KernelErrorEndpoints.Problem(
                ProtocolErrors.AttestationRejected.WithMessage("Protocol approval requires an attestation with meaning Approved."));
        }

        var tenant = KernelRequestTenant.Get(context);
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is { } entitlementReader)
        {
            var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
            var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (entitlements.IsReadOnly)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
            }

            if (!entitlements.ProductFamilies.Contains(ProductFamily.Blueprint))
            {
                return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
            }
        }

        var existing = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.NotFound);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(KernelProtocolGovernanceHelpers.DeriveGuid(tenant, "approve-protocol-version-attest", idempotencyKey));

        AttestationId? reviewAttestationId = null;
        if (!string.IsNullOrWhiteSpace(request?.ReviewAttestationRef))
        {
            if (!KernelProtocolGovernanceHelpers.TryParseAttestation(request.ReviewAttestationRef.Trim(), out var parsedReviewAttestationId))
            {
                return KernelProblems.BadRequest(
                    "Invalid NodeRef",
                    $"'{request.ReviewAttestationRef}' is not an attestation NodeRef (attestation:<uuid>).");
            }

            reviewAttestationId = parsedReviewAttestationId;
        }

        var canApprove = await probe.CanIAsync(tenant, ApproveProtocolVersionCapability, NodeRef.For(versionId), cancellationToken).ConfigureAwait(false);
        if (canApprove.Permitted)
        {
            if (existing.Status != ProtocolVersionStatus.InReview || string.IsNullOrWhiteSpace(existing.ContentHash))
            {
                return KernelErrorEndpoints.Problem(ProtocolErrors.InvalidTransition);
            }

            if (!string.IsNullOrWhiteSpace(request?.ExpectedContentHash))
            {
                var normalizedExpected = KernelProtocolGovernanceHelpers.NormalizeSha256(request.ExpectedContentHash);
                if (!string.Equals(normalizedExpected, existing.ContentHash, StringComparison.Ordinal))
                {
                    return KernelErrorEndpoints.Problem(TrustErrors.TargetContentChanged);
                }
            }

            Actor actor;
            if (context.RequestServices.GetService<ICurrentActor>()?.Actor is { } resolvedActor
                && !string.IsNullOrWhiteSpace(resolvedActor.Id))
            {
                actor = resolvedActor;
            }
            else if (!string.IsNullOrWhiteSpace(sigToken.Subject))
            {
                actor = Actor.Person(sigToken.Subject);
            }
            else
            {
                return KernelErrorEndpoints.Problem(KernelErrors.ActorUnresolved);
            }

            if (existing.ParentVersionId is not null
                && context.RequestServices.GetService<IKernelUnitOfWorkFactory>() is { } unitOfWorkFactory
                && context.RequestServices.GetService<IAttestationVerifier>() is { } verifier)
            {
                var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
                var precheck = await ProtocolApprovalSimulationGate.CheckAsync(
                    tenant,
                    unitOfWorkFactory,
                    verifier,
                    actor,
                    clock.UtcNow,
                    versionId,
                    reviewAttestationId,
                    cancellationToken).ConfigureAwait(false);
                if (precheck is not null)
                {
                    return KernelErrorEndpoints.Problem(precheck);
                }
            }

            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(AttestationTarget.ProtocolVersionKind, versionId.Value, existing.ContentHash),
                    new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(attestResult.Error!);
            }
        }

        var approveResult = await sender.SendAsync(
            new ApproveProtocolVersion(tenant, versionId, attestationId, idempotencyKey, reason, reviewAttestationId),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!approveResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(approveResult.Error!);
        }

        var updated = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToProtocolVersionResponse(updated, approveResult.Value!));
    }

    internal static async Task<Results<Created<KernelProtocolVersionResponse>, ProblemHttpResult>> ForkProtocolVersionAsync(
        string versionRef,
        KernelForkProtocolVersionRequest? request,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var parentVersionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        if (request is null)
        {
            return KernelErrorEndpoints.Problem(ProtocolErrors.AmendmentInvalid);
        }

        var classification = AmendmentClassification.Substantial;
        if (!string.IsNullOrWhiteSpace(request.Classification)
            && (!Enum.TryParse(request.Classification.Trim(), ignoreCase: true, out classification) || !Enum.IsDefined(classification)))
        {
            return KernelErrorEndpoints.Problem(
                ProtocolErrors.AmendmentInvalid.WithMessage("Amendment classification must be Substantial, NonSubstantial, or UrgentSafetyMeasure."));
        }

        var transitionPolicy = TransitionPolicy.RequiresReconsent;
        if (!string.IsNullOrWhiteSpace(request.TransitionPolicy)
            && (!Enum.TryParse(request.TransitionPolicy.Trim(), ignoreCase: true, out transitionPolicy) || !Enum.IsDefined(transitionPolicy)))
        {
            return KernelErrorEndpoints.Problem(
                ProtocolErrors.AmendmentInvalid.WithMessage("Transition policy must be NewParticipantsOnly, RequiresReconsent, Automatic, or ByCohort."));
        }

        var impactedElements = (request.ImpactedElements ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .ToList();

        var amendment = new ProtocolAmendment(
            (request.Intent ?? string.Empty).Trim(),
            (request.Rationale ?? string.Empty).Trim(),
            classification,
            (request.SummaryOfChanges ?? string.Empty).Trim(),
            impactedElements,
            transitionPolicy);

        var tenant = KernelRequestTenant.Get(context);
        var newVersionId = request.ProtocolVersionId is { } given && given != Guid.Empty
            ? ProtocolVersionId.FromGuid(given)
            : ProtocolVersionId.FromGuid(KernelProtocolGovernanceHelpers.DeriveGuid(tenant, $"fork-protocol-version:{parentVersionId.Value:D}", idempotencyKey));

        var result = await sender.SendAsync(
            new ForkProtocolVersion(tenant, parentVersionId, newVersionId, amendment, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var created = await versions.GetAsync(tenant, newVersionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{NodeRef.For(newVersionId)}",
            ToProtocolVersionResponse(created, result.Value!));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> WithdrawProtocolVersionAsync(
        string versionRef,
        KernelWithdrawProtocolVersionRequest? request,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this protocol version withdrawal.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        if (request?.Meaning is { Length: > 0 } rawMeaning
            && (!Enum.TryParse<AttestationMeaning>(rawMeaning, ignoreCase: true, out var parsedMeaning)
                || parsedMeaning != AttestationMeaning.Withdrawn))
        {
            return KernelErrorEndpoints.Problem(
                ProtocolErrors.AttestationRejected.WithMessage("Protocol withdrawal requires an attestation with meaning Withdrawn."));
        }

        var tenant = KernelRequestTenant.Get(context);
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is { } entitlementReader)
        {
            var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
            var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (entitlements.IsReadOnly)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
            }

            if (!entitlements.ProductFamilies.Contains(ProductFamily.Blueprint))
            {
                return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
            }
        }

        var existing = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.NotFound);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(KernelProtocolGovernanceHelpers.DeriveGuid(tenant, "withdraw-protocol-version-attest", idempotencyKey));

        var canWithdraw = await probe.CanIAsync(tenant, WithdrawProtocolVersionCapability, NodeRef.For(versionId), cancellationToken).ConfigureAwait(false);
        if (canWithdraw.Permitted)
        {
            if (existing.Status is not (ProtocolVersionStatus.InReview or ProtocolVersionStatus.Approved)
                || string.IsNullOrWhiteSpace(existing.ContentHash))
            {
                return KernelErrorEndpoints.Problem(ProtocolErrors.InvalidTransition);
            }

            if (!string.IsNullOrWhiteSpace(request?.ExpectedContentHash))
            {
                var normalizedExpected = KernelProtocolGovernanceHelpers.NormalizeSha256(request.ExpectedContentHash);
                if (!string.Equals(normalizedExpected, existing.ContentHash, StringComparison.Ordinal))
                {
                    return KernelErrorEndpoints.Problem(TrustErrors.TargetContentChanged);
                }
            }

            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Withdrawn,
                    new AttestationTarget(AttestationTarget.ProtocolVersionKind, versionId.Value, existing.ContentHash),
                    new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:attest",
                    reason,
                    Scope: NodeRef.For(existing.StudyId)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(attestResult.Error!);
            }
        }

        var withdrawResult = await sender.SendAsync(
            new WithdrawProtocolVersion(tenant, versionId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!withdrawResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(withdrawResult.Error!);
        }

        var updated = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToProtocolVersionResponse(updated, withdrawResult.Value!));
    }

    private static KernelProtocolVersionResponse ToProtocolVersionResponse(ProtocolVersionSummary? readBack, ProtocolVersionState state) =>
        readBack is not null
            ? KernelProtocolVersionResponse.From(readBack, mask: null)
            : new KernelProtocolVersionResponse(
                NodeRef.For(state.ProtocolVersionId).ToString(),
                NodeRef.For(state.StudyId).ToString(),
                state.Number,
                ParentVersion: null,
                state.Status.ToString(),
                state.ContentHash,
                AmendmentIntent: null,
                ApprovalAttestation: null,
                WithdrawalReason: null,
                Mask: null);
}
