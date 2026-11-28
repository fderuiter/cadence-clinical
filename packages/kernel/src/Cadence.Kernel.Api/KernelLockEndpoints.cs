using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Api.Locks;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Signed clinical lock and freeze routes (spec §10.5, 21 CFR Part 11, ADR-2621). Applying or releasing a lock
/// verifies a 60-second single-use <c>X-Sig-Token</c> and dispatches <see cref="ApplyLock"/> or
/// <see cref="ReleaseLock"/> through the kernel command pipeline, recording the <c>Attestation</c> over the canonical
/// <see cref="LockStatement"/> atomically under <c>StudyId</c> authority. Listing study locks evaluates caller
/// visibility and blinding (<c>K-AUTH-4</c>) so unauthorized or blinded scopes appear as explicit <c>masked</c>
/// placeholders in place.
/// </summary>
public static class KernelLockEndpoints
{
    /// <summary>The semantic action an apply-lock step-up token must carry.</summary>
    public const string ApplyLockSemanticAction = "kernel.lock.apply";

    /// <summary>The semantic action a release-lock step-up token must carry.</summary>
    public const string ReleaseLockSemanticAction = "kernel.lock.release";

    /// <summary>The canonical concrete action path for applying a kernel lock.</summary>
    public const string ApplyLockAction = $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/locks";

    /// <summary>How the signer re-authenticated at the gateway (step-up contract v3).</summary>
    public const string StepUpMethod = "gateway-step-up:v3";

    /// <summary>The canonical concrete action path for releasing a specific lock reference.</summary>
    public static string ReleaseLockAction(string lockRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/locks/{lockRef}/release";

    /// <summary>Maps <c>/locks</c> and <c>/studies/{studyRef}/locks</c> on the kernel group.</summary>
    public static RouteGroupBuilder MapKernelLocks(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var locks = kernel.MapGroup("/locks").AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel locks");

        locks.MapPost(string.Empty, ApplyAsync)
            .AllowExpiredTenantWrite()
            .WithName("KernelApplyLock")
            .WithSummary("Applies a Frozen or Locked assertion to a study, study site, enrollment, form instance or observation (lock.apply).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ApplyLock>()
            .RequireElectronicSignature(ApplyLockSemanticAction, _ => ApplyLockAction);

        locks.MapPost("/{lockRef}/release", ReleaseAsync)
            .AllowExpiredTenantWrite()
            .WithName("KernelReleaseLock")
            .WithSummary("Releases a Frozen or Locked assertion with a signed retracting assertion (lock.release).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ReleaseLock>()
            .RequireElectronicSignature(
                ReleaseLockSemanticAction,
                ctx => ReleaseLockAction(ctx.Request.RouteValues["lockRef"]?.ToString() ?? string.Empty));

        locks.MapGet("/in-force", GetInForceAsync)
            .AllowBaselineProductFamily()
            .WithName("KernelGetScopeWriteLock")
            .WithSummary("Checks whether any active write-denying lock (Frozen or Locked) covers a study, study site, or enrollment scope.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        locks.MapPost("/in-force/batch", GetBatchInForceAsync)
            .AllowExpiredTenantWrite()
            .AllowBaselineProductFamily()
            .WithName("KernelGetBatchScopeWriteLocks")
            .WithSummary("Checks in one batch pass whether any active write-denying lock (Frozen or Locked) covers a study or any supplied study sites.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
                KernelErrors.ActorUnresolved,
                KernelErrors.AuthorizationDenied,
                KernelErrors.NotFound);

        var studies = kernel.MapGroup("/studies").AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel locks");
        studies.MapGet("/{studyRef}/locks", ListByStudyAsync)
            .WithName("KernelListStudyLocks")
            .WithSummary("Lists Frozen and Locked assertions recorded for a study at an optional bitemporal as_of cut.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        studies.MapGet("/{studyRef}/locks/readiness", GetReadinessAsync)
            .AllowBaselineProductFamily()
            .WithName("KernelGetStudyLockReadiness")
            .WithSummary("Evaluates lock readiness across queries, missing required items, unverified revisions, unsigned forms, unopened scheduled visits, and Error validation findings.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return kernel;
    }

    internal static Task<Results<Created<KernelLockResponse>, ProblemHttpResult>> ApplyAsync(
        KernelApplyLockRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        ILockRecords locks,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelLockCommandEndpoints.ApplyAsync(request, context, sender, locks, changeReason, cancellationToken);

    internal static Task<Results<Ok<KernelLockResponse>, ProblemHttpResult>> ReleaseAsync(
        string lockRef,
        KernelReleaseLockRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        ILockRecords locks,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelLockCommandEndpoints.ReleaseAsync(lockRef, request, context, sender, locks, changeReason, cancellationToken);

    internal static Task<Results<Ok<KernelStudyLocksResponse>, ProblemHttpResult>> ListByStudyAsync(
        string studyRef,
        [FromQuery(Name = "as_of")] DateTimeOffset? asOf,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        [FromQuery(Name = "active_only")] bool? activeOnly,
        HttpContext context,
        IKernelNavigator navigator,
        ILockRecords locks,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken) =>
        KernelLockProbeEndpoints.ListByStudyAsync(
            studyRef, asOf, validAt, knownAt, activeOnly, context, navigator, locks, visibility, probe, actor, clock, cancellationToken);

    internal static Task<Results<Ok<KernelLockReadinessResponse>, ProblemHttpResult>> GetReadinessAsync(
        string studyRef,
        [FromQuery(Name = "scope_ref")] string? scopeRef,
        HttpContext context,
        IKernelNavigator navigator,
        ILockRecords locks,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelLockReadinessEndpoints.GetReadinessAsync(
            studyRef, scopeRef, context, navigator, locks, visibility, probe, actor, cancellationToken);

    internal static Task<Results<Ok<KernelScopeWriteLockResponse>, ProblemHttpResult>> GetInForceAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "site_code")] string? siteCode,
        [FromQuery(Name = "subject_code")] string? subjectCode,
        [FromQuery(Name = "enrollment_id")] Guid? enrollmentId,
        HttpContext context,
        ILockRecords locks,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelLockProbeEndpoints.GetInForceAsync(
            studyCode, siteCode, subjectCode, enrollmentId, context, locks, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelScopeWriteLockResponse>, ProblemHttpResult>> GetBatchInForceAsync(
        KernelBatchScopeWriteLockRequest? request,
        HttpContext context,
        ILockRecords locks,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelLockProbeEndpoints.GetBatchInForceAsync(request, context, locks, visibility, actor, cancellationToken);
}
