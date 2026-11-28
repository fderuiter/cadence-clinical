using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.Validation;

internal static class KernelValidationFindingDecisionEndpoints
{
    private static readonly Capability AcceptFindingCapability = new("validation_finding.accept", VerbClass.Approve);
    private static readonly Capability ExplainFindingCapability = new("validation_finding.explain", VerbClass.Approve);

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> InvestigateFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (_, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new InvestigateFinding(tenant, findingId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ClassifyFindingAsync(
        string findingRef,
        KernelClassifyFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.Classification)
            || !Enum.TryParse<FindingClassification>(request.Classification.Trim(), ignoreCase: true, out var classification)
            || !Enum.IsDefined(classification))
        {
            return KernelProblems.BadRequest(
                "Invalid classification",
                "Classification must be DataIssue, MappingIssue, MetadataIssue, RuleIssue or NotAnIssue.");
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (_, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new ClassifyFinding(tenant, findingId, classification, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> AcceptFindingAsync(
        string findingRef,
        KernelAcceptFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to accepting this validation finding.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var rationale = (request?.Rationale ?? string.Empty).Trim();
        if (!FindingText.IsValid(rationale))
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.DecisionTextInvalid);
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (existing, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(KernelRulePackGovernanceEndpoints.DeriveGuid(tenant, "accept-finding-attest", idempotencyKey));

        var findingNode = NodeRef.For(findingId);
        var canAccept = await probe.CanIAsync(tenant, AcceptFindingCapability, findingNode, cancellationToken).ConfigureAwait(false);
        if (canAccept.Permitted)
        {
            existing ??= await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
            }

            if (existing.Status is FindingStatus.Open or FindingStatus.Investigating)
            {
                var hash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Accepted, rationale);
                var attestResult = await sender.SendAsync(
                    new Attest(
                        tenant,
                        attestationId,
                        AttestationMeaning.Responsibility,
                        new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, hash),
                        new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                        KernelLockEndpoints.StepUpMethod,
                        $"{idempotencyKey}:attest",
                        reason,
                        findingNode),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
                {
                    return KernelErrorEndpoints.Problem(attestResult.Error!);
                }
            }
        }

        var result = await sender.SendAsync(
            new AcceptFinding(tenant, findingId, rationale, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ExplainFindingAsync(
        string findingRef,
        KernelExplainFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to explaining this validation finding.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var explanation = (request?.Explanation ?? string.Empty).Trim();
        if (!FindingText.IsValid(explanation))
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.DecisionTextInvalid);
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (existing, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(KernelRulePackGovernanceEndpoints.DeriveGuid(tenant, "explain-finding-attest", idempotencyKey));

        var findingNode = NodeRef.For(findingId);
        var canExplain = await probe.CanIAsync(tenant, ExplainFindingCapability, findingNode, cancellationToken).ConfigureAwait(false);
        if (canExplain.Permitted)
        {
            existing ??= await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
            }

            if (existing.Status is FindingStatus.Open or FindingStatus.Investigating)
            {
                var hash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Explained, explanation);
                var attestResult = await sender.SendAsync(
                    new Attest(
                        tenant,
                        attestationId,
                        AttestationMeaning.Approved,
                        new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, hash),
                        new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                        KernelLockEndpoints.StepUpMethod,
                        $"{idempotencyKey}:attest",
                        reason,
                        findingNode),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
                {
                    return KernelErrorEndpoints.Problem(attestResult.Error!);
                }
            }
        }

        var result = await sender.SendAsync(
            new ExplainFinding(tenant, findingId, explanation, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ReopenFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (_, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new ReopenFinding(tenant, findingId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(ValidationFindingView? Existing, ProblemHttpResult? Problem)> AuthorizeDecisionVisibilityAsync(
        TenantId tenant,
        ValidationFindingId findingId,
        IValidationFindings findings,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (actor.Actor is null)
        {
            return (null, null);
        }

        var findingNode = NodeRef.For(findingId);
        if (KernelValidationFindingVisibility.ResolveActiveValidationFindings(context.RequestServices) is null)
        {
            var fallbackMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [findingNode], cancellationToken).ConfigureAwait(false);
            return fallbackMasks.For(findingNode) is not null
                ? (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied))
                : (null, null);
        }

        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return await KernelValidationFindingVisibility.IsFindingVisibleAsync(tenant, existing, context.RequestServices, visibility, actor, cancellationToken).ConfigureAwait(false)
                ? (existing, null)
                : (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [findingNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(findingNode) is not null)
        {
            return (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        if (context.RequestServices.GetService<IAuthorizationProbe>() is { } probe)
        {
            var canUnblindTenant = await probe.CanIAsync(
                tenant,
                KernelValidationFindingVisibility.EnrollmentUnblindCapability,
                NodeRef.For(tenant),
                cancellationToken).ConfigureAwait(false);
            if (!canUnblindTenant.Permitted)
            {
                return (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }
        }

        return (null, null);
    }

    private static async Task<ProblemHttpResult?> CheckValidationDecisionEntitlementsAsync(
        HttpContext context,
        TenantId tenant,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is not { } entitlementReader)
        {
            return null;
        }

        var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
        var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (entitlements.IsReadOnly)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
        }

        if (!entitlements.ProductFamilies.Any(static f =>
                f is ProductFamily.Blueprint or ProductFamily.Capture or ProductFamily.Assure))
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
        }

        return null;
    }

    private static (string IdempotencyKey, string Reason, ProblemHttpResult? Problem) ReadCommandHeaders(
        HttpContext context, IKernelChangeReason changeReason)
    {
        var idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired));
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired));
        }

        return (idempotencyKey, reason, null);
    }

    private static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ToFindingDecisionResultAsync(
        CommandResult<ValidationFindingId> result,
        TenantId tenant,
        ValidationFindingId findingId,
        IValidationFindings findings,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
            return KernelProblems.NotFound(NodeRef.For(findingId));
        }

        if (!await KernelValidationFindingVisibility.IsFindingVisibleAsync(tenant, updated, services, visibility, actor, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        return TypedResults.Ok(KernelValidationFindingResponse.From(updated));
    }

    private static bool TryParseFinding(string text, out ValidationFindingId findingId)
    {
        findingId = default;
        if (!NodeRef.TryParse(text, out var node) || node.Kind != NodeKinds.ValidationFinding || node.Version is not null)
        {
            return false;
        }

        findingId = ValidationFindingId.FromGuid(node.Id);
        return !findingId.IsEmpty;
    }
}
