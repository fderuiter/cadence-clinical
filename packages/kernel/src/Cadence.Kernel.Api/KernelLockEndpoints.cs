using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

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

        if (!TryParseMilestone(request.Milestone, out var milestone))
        {
            return KernelProblems.BadRequest("Invalid lock milestone", "Lock milestone must be InterimCut or FinalDatabaseLock.");
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
                StepUpMethod,
                milestone),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (applyResult.Error is { } applyError)
        {
            if (string.Equals(applyError.Code, AssertionErrors.ScopeNotReady.Code, StringComparison.Ordinal))
            {
                applyError = await MaskScopeNotReadyErrorAsync(
                    context,
                    tenant,
                    studyId,
                    studyNode,
                    scope,
                    milestone,
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
                StepUpMethod),
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

    internal static async Task<Results<Ok<KernelStudyLocksResponse>, ProblemHttpResult>> ListByStudyAsync(
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
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var resolvedStudy = await KernelStudyLookup.ResolveVisibleStudyAsync(
            tenant,
            studyRef,
            context,
            visibility,
            actor,
            (_, studyMask) => $"You may not see this study ({studyMask}), so its locks are not listed.",
            allowVersionedStudyRef: false,
            cancellationToken).ConfigureAwait(false);
        if (resolvedStudy.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var studyNode = resolvedStudy.Study.UnversionedRef;
        var now = clock.UtcNow;
        var effectiveValidAt = validAt ?? asOf ?? now;
        var effectiveKnownAt = knownAt ?? asOf ?? now;

        var list = await locks.ListByStudyAsync(
            tenant,
            resolvedStudy.Study.StudyId,
            effectiveValidAt,
            effectiveKnownAt,
            activeOnly ?? false,
            cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            return KernelProblems.NotFound(studyNode);
        }

        var nodesToCheck = new List<NodeRef>();
        foreach (var entry in list.Locks)
        {
            if (entry.Scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment)
            {
                nodesToCheck.Add(entry.Scope);
            }

            if (entry.StudySiteId is { } siteId)
            {
                nodesToCheck.Add(NodeRef.For(siteId));
            }

            if (entry.EnrollmentId is { } enrollmentId)
            {
                nodesToCheck.Add(NodeRef.For(enrollmentId));
            }
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);

        // Cache unblinding checks per enrollment (K-AUTH-4).
        var unblindByTarget = new Dictionary<NodeRef, bool>();
        var hiddenReasons = new HashSet<MaskReason>();
        var items = new List<KernelLockResponse>(list.Locks.Count);
        foreach (var entry in list.Locks)
        {
            // CollectionInstance and Observation scopes inherit visibility from their Enrollment and StudySite (ADR-2457).
            var mask = (entry.Scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
                    ? masks.For(entry.Scope)
                    : null)
                ?? (entry.EnrollmentId is { } eid ? masks.For(NodeRef.For(eid)) : null)
                ?? (entry.StudySiteId is { } sid ? masks.For(NodeRef.For(sid)) : null);

            if (mask is { } hiddenScopeReason && hiddenScopeReason != MaskReason.Blinded)
            {
                hiddenReasons.Add(hiddenScopeReason);
                continue;
            }

            if (mask is null && entry.BlindedScope)
            {
                var unblindTarget = entry.EnrollmentId is { } targetEnrollment
                    ? NodeRef.For(targetEnrollment)
                    : studyNode;
                if (!unblindByTarget.TryGetValue(unblindTarget, out var mayUnblind))
                {
                    var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, unblindTarget, cancellationToken).ConfigureAwait(false);
                    mayUnblind = decision.Permitted;
                    unblindByTarget[unblindTarget] = mayUnblind;
                }

                if (!mayUnblind)
                {
                    mask = MaskReason.Blinded;
                }
            }

            items.Add(mask is { } reason
                ? KernelLockResponse.FromMasked(entry, reason)
                : KernelLockResponse.FromVisible(entry));
        }

        return TypedResults.Ok(new KernelStudyLocksResponse(
            studyNode.ToString(),
            list.StudyCode,
            list.ValidAt,
            list.KnownAt,
            items,
            KernelHiddenContentResponse.FromReasons(hiddenReasons)));
    }

    internal static async Task<Results<Ok<KernelLockReadinessResponse>, ProblemHttpResult>> GetReadinessAsync(
        string studyRef,
        [FromQuery(Name = "scope_ref")] string? scopeRef,
        [FromQuery(Name = "milestone")] string? milestoneRaw,
        HttpContext context,
        IKernelNavigator navigator,
        ILockRecords locks,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        NodeRef? explicitScope = null;
        if (!string.IsNullOrWhiteSpace(scopeRef))
        {
            if (!NodeRef.TryParse(scopeRef.Trim(), out var parsedScope) || !LockScopes.IsScope(parsedScope))
            {
                return KernelProblems.BadRequest(
                    "Invalid lock scope",
                    "Name scope_ref as an unversioned study, study_site, enrollment, collection_instance or observation NodeRef.");
            }

            explicitScope = parsedScope;
        }

        if (!TryParseMilestone(milestoneRaw, out var milestone))
        {
            return KernelProblems.BadRequest(
                "Invalid lock milestone",
                "Name milestone as InterimCut or FinalDatabaseLock.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolvedStudy = await KernelStudyLookup.ResolveVisibleStudyAsync(
            tenant,
            studyRef,
            context,
            visibility,
            actor,
            (_, studyMask) => $"You may not see this study ({studyMask}), so its lock readiness is not available.",
            allowVersionedStudyRef: false,
            cancellationToken).ConfigureAwait(false);
        if (resolvedStudy.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var studyNode = resolvedStudy.Study.UnversionedRef;
        var targetScope = explicitScope ?? studyNode;
        if (targetScope != studyNode)
        {
            var scopeMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [targetScope], cancellationToken).ConfigureAwait(false);
            if (scopeMasks.For(targetScope) is { } scopeMask)
            {
                return KernelProblems.NotVisible($"You may not see this lock scope ({scopeMask}), so its lock readiness is not available.");
            }
        }

        var snapshot = await locks.ReadReadinessAsync(
            tenant,
            resolvedStudy.Study.StudyId,
            targetScope,
            milestone,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return KernelProblems.NotFound(targetScope);
        }

        var (items, maskedEvidenceRefs) = await MaskReadinessBlockersAsync(
            snapshot,
            tenant,
            studyNode,
            visibility,
            probe,
            actor,
            cancellationToken).ConfigureAwait(false);

        var explanationResponse = BuildMaskedExplanationResponse(snapshot.Explanation, items, maskedEvidenceRefs);
        return TypedResults.Ok(new KernelLockReadinessResponse(
            studyNode.ToString(),
            snapshot.StudyCode,
            snapshot.Scope.ToString(),
            snapshot.Scope.Kind,
            snapshot.ReadyForLock,
            snapshot.CanFreeze,
            snapshot.OpenQueryCount,
            snapshot.MissingRequiredItemCount,
            snapshot.OpenErrorFindingCount,
            items.Count,
            items,
            explanationResponse,
            snapshot.UnverifiedValueCount,
            snapshot.UnsignedFormCount,
            snapshot.UnopenedVisitCount,
            snapshot.Milestone.ToString(),
            snapshot.ScreeningEnrollmentCount,
            snapshot.PendingScheduledVisitCount,
            snapshot.UnreviewedValueCount));
    }

    private static async Task<(IReadOnlyList<KernelLockBlockerResponse> Items, HashSet<string> MaskedEvidenceRefs)> MaskReadinessBlockersAsync(
        LockReadinessSnapshot snapshot,
        TenantId tenant,
        NodeRef studyNode,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var nodesToCheck = new List<NodeRef>();
        foreach (var blocker in snapshot.Blockers)
        {
            nodesToCheck.Add(blocker.Target);
            if (blocker.StudySiteId is { } siteId)
            {
                nodesToCheck.Add(NodeRef.For(siteId));
            }

            if (blocker.EnrollmentId is { } enrollmentId)
            {
                nodesToCheck.Add(NodeRef.For(enrollmentId));
            }

            if (blocker.ObservationId is { } obsId)
            {
                nodesToCheck.Add(NodeRef.For(obsId));
            }

            if (blocker.QueryAssertionId is { } queryId)
            {
                nodesToCheck.Add(NodeRef.For(queryId));
            }

            if (blocker.FindingId is { } findingId)
            {
                nodesToCheck.Add(NodeRef.For(findingId));
            }
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);
        var unblindByTarget = new Dictionary<NodeRef, bool>();
        var items = new List<KernelLockBlockerResponse>(snapshot.Blockers.Count);
        var maskedEvidenceRefs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var blocker in snapshot.Blockers)
        {
            var mask = masks.For(blocker.Target)
                ?? (blocker.FindingId is { } fidCheck ? masks.For(NodeRef.For(fidCheck)) : null)
                ?? (blocker.QueryAssertionId is { } qidCheck ? masks.For(NodeRef.For(qidCheck)) : null)
                ?? (blocker.ObservationId is { } oidCheck ? masks.For(NodeRef.For(oidCheck)) : null)
                ?? (blocker.EnrollmentId is { } eid ? masks.For(NodeRef.For(eid)) : null)
                ?? (blocker.StudySiteId is { } sid ? masks.For(NodeRef.For(sid)) : null);

            if (mask is null && blocker.Blinded)
            {
                var unblindTarget = blocker.EnrollmentId is { } targetEnrollment
                    ? NodeRef.For(targetEnrollment)
                    : studyNode;
                if (!unblindByTarget.TryGetValue(unblindTarget, out var mayUnblind))
                {
                    var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, unblindTarget, cancellationToken).ConfigureAwait(false);
                    mayUnblind = decision.Permitted;
                    unblindByTarget[unblindTarget] = mayUnblind;
                }

                if (!mayUnblind)
                {
                    mask = MaskReason.Blinded;
                }
            }

            if (mask is { } reason)
            {
                if (blocker.Target != snapshot.Scope)
                {
                    maskedEvidenceRefs.Add(blocker.Target.ToString());
                }

                if (blocker.ObservationId is { } oid)
                {
                    maskedEvidenceRefs.Add(NodeRef.For(oid).ToString());
                }

                if (blocker.QueryAssertionId is { } qid)
                {
                    maskedEvidenceRefs.Add(NodeRef.For(qid).ToString());
                }

                if (blocker.FindingId is { } fid)
                {
                    maskedEvidenceRefs.Add(NodeRef.For(fid).ToString());
                }

                items.Add(KernelLockBlockerResponse.FromMasked(blocker, reason));
            }
            else
            {
                items.Add(KernelLockBlockerResponse.FromVisible(blocker));
            }
        }

        return (items, maskedEvidenceRefs);
    }

    internal static async Task<Results<Ok<KernelScopeWriteLockResponse>, ProblemHttpResult>> GetInForceAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "site_code")] string? siteCode,
        [FromQuery(Name = "subject_code")] string? subjectCode,
        [FromQuery(Name = "enrollment_id")] Guid? enrollmentId,
        HttpContext context,
        ILockRecords locks,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return KernelProblems.BadRequest("Study code required", "Supply a study_code query parameter.");
        }

        var trimmedSiteCode = string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim();
        var trimmedSubjectCode = string.IsNullOrWhiteSpace(subjectCode) ? null : subjectCode.Trim();
        var tenant = KernelRequestTenant.Get(context);
        var check = await locks.CheckScopeWriteLockAsync(
            tenant,
            studyCode.Trim(),
            trimmedSiteCode,
            trimmedSubjectCode,
            enrollmentId,
            cancellationToken).ConfigureAwait(false);

        if (IsVerifiedSystemService(context))
        {
            return TypedResults.Ok(new KernelScopeWriteLockResponse(
                check.StudyExists,
                check.Locked,
                check.LockKind?.ToString(),
                check.ScopeKind));
        }

        if (!check.StudyExists
            || check.StudyId is not { } studyId
            || (trimmedSiteCode is not null && check.StudySiteId is null))
        {
            return KernelProblems.ScopeWriteLockNotFound();
        }

        var studyNode = NodeRef.For(studyId);
        var nodesToCheck = new List<NodeRef> { studyNode };
        if (check.StudySiteId is { } resolvedSiteId)
        {
            nodesToCheck.Add(NodeRef.For(resolvedSiteId));
        }

        if (check.EnrollmentStudySiteId is { } enrollmentSiteId && enrollmentSiteId != check.StudySiteId)
        {
            nodesToCheck.Add(NodeRef.For(enrollmentSiteId));
        }

        if (check.EnrollmentId is { } resolvedEnrollmentId)
        {
            nodesToCheck.Add(NodeRef.For(resolvedEnrollmentId));
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            nodesToCheck,
            cancellationToken).ConfigureAwait(false);

        // Code-based lock checks match caller-supplied study and site codes, so hidden studies or explicit sites must
        // return the same non-confirming 404 as absent scopes (ADR-2471).
        if (masks.For(studyNode) is not null)
        {
            return KernelProblems.ScopeWriteLockNotFound();
        }

        var allowedScopes = new HashSet<NodeRef> { studyNode };
        if (trimmedSiteCode is not null)
        {
            var explicitSiteNode = NodeRef.For(check.StudySiteId!.Value);
            if (masks.For(explicitSiteNode) is not null)
            {
                return KernelProblems.ScopeWriteLockNotFound();
            }

            allowedScopes.Add(explicitSiteNode);
        }

        // When site_code is omitted, ignore masked Enrollment matches (and their inferred study sites) so cross-site
        // subject probes match absent subjects.
        if (check.EnrollmentId is { } matchedEnrollmentId)
        {
            var enrollmentNode = NodeRef.For(matchedEnrollmentId);
            var governingSiteId = check.EnrollmentStudySiteId ?? (trimmedSiteCode is null ? check.StudySiteId : null);
            var governingSiteNode = governingSiteId is { } gsid ? NodeRef.For(gsid) : null;
            if (masks.For(enrollmentNode) is null && (governingSiteNode is null || masks.For(governingSiteNode) is null))
            {
                allowedScopes.Add(enrollmentNode);
                if (governingSiteNode is not null)
                {
                    allowedScopes.Add(governingSiteNode);
                }
            }
        }

        var filtered = check.FilterScopes(allowedScopes);
        return TypedResults.Ok(new KernelScopeWriteLockResponse(
            filtered.StudyExists,
            filtered.Locked,
            filtered.LockKind?.ToString(),
            filtered.ScopeKind));
    }

    internal static async Task<Results<Ok<KernelScopeWriteLockResponse>, ProblemHttpResult>> GetBatchInForceAsync(
        KernelBatchScopeWriteLockRequest? request,
        HttpContext context,
        ILockRecords locks,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.StudyCode))
        {
            return KernelProblems.BadRequest("Study code required", "Supply a study_code field in the batch lock request.");
        }

        var distinctSiteCodes = new List<string>();
        var seenSites = new HashSet<string>(StringComparer.Ordinal);
        if (request.SiteCodes is not null)
        {
            foreach (string? rawSite in request.SiteCodes)
            {
                if (!string.IsNullOrWhiteSpace(rawSite))
                {
                    string trimmedSite = rawSite.Trim();
                    if (seenSites.Add(trimmedSite))
                    {
                        distinctSiteCodes.Add(trimmedSite);
                    }
                }
            }
        }

        var tenant = KernelRequestTenant.Get(context);
        var check = await locks.CheckBatchScopeWriteLocksAsync(
            tenant,
            request.StudyCode.Trim(),
            distinctSiteCodes,
            cancellationToken).ConfigureAwait(false);

        if (IsVerifiedSystemService(context))
        {
            return TypedResults.Ok(new KernelScopeWriteLockResponse(
                check.StudyExists,
                check.Locked,
                check.LockKind?.ToString(),
                check.ScopeKind));
        }

        if (!check.StudyExists || check.StudyId is not { } studyId)
        {
            return KernelProblems.ScopeWriteLockNotFound();
        }

        var resolvedMap = check.ResolvedStudySites;
        if (distinctSiteCodes.Count > 0 && (resolvedMap is null || distinctSiteCodes.Any(code => !resolvedMap.ContainsKey(code))))
        {
            return KernelProblems.ScopeWriteLockNotFound();
        }

        var studyNode = NodeRef.For(studyId);
        var nodesToCheck = new List<NodeRef>(1 + distinctSiteCodes.Count) { studyNode };
        var seenSiteNodes = new HashSet<NodeRef>();
        if (resolvedMap is not null)
        {
            foreach (string siteCode in distinctSiteCodes)
            {
                var siteNode = NodeRef.For(resolvedMap[siteCode]);
                if (seenSiteNodes.Add(siteNode))
                {
                    nodesToCheck.Add(siteNode);
                }
            }
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            nodesToCheck,
            cancellationToken).ConfigureAwait(false);

        if (nodesToCheck.Any(node => masks.For(node) is not null))
        {
            return KernelProblems.ScopeWriteLockNotFound();
        }

        var allowedScopes = nodesToCheck.ToHashSet();
        var filtered = check.FilterScopes(allowedScopes);
        return TypedResults.Ok(new KernelScopeWriteLockResponse(
            filtered.StudyExists,
            filtered.Locked,
            filtered.LockKind?.ToString(),
            filtered.ScopeKind));
    }

    private static bool IsVerifiedSystemService(HttpContext context) =>
        context.User.Identity is { IsAuthenticated: true }
        && context.User.FindFirst(CadenceClaimTypes.Subject)?.Value is { } subject
        && subject.StartsWith(Actor.ServiceAccountPrefix, StringComparison.Ordinal)
        && subject.AsSpan(Actor.ServiceAccountPrefix.Length).Trim().Length > 0
        && (context.User.IsInRole("system_service") || context.User.IsInRole("SYSTEM_SERVICE"));

    private static async Task<KernelError> MaskScopeNotReadyErrorAsync(
        HttpContext context,
        TenantId tenant,
        StudyId studyId,
        NodeRef studyNode,
        NodeRef targetScope,
        LockMilestone milestone,
        ILockRecords locks,
        KernelError applyError,
        CancellationToken cancellationToken)
    {
        if (applyError.Explanation is null
            || context.RequestServices.GetService<INodeVisibility>() is not { } visibility
            || context.RequestServices.GetService<IAuthorizationProbe>() is not { } probe
            || context.RequestServices.GetService<ICurrentActor>() is not { } actor)
        {
            return applyError;
        }

        var snapshot = await locks.ReadReadinessAsync(tenant, studyId, targetScope, milestone, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return applyError;
        }

        var (items, maskedEvidenceRefs) = await MaskReadinessBlockersAsync(
            snapshot,
            tenant,
            studyNode,
            visibility,
            probe,
            actor,
            cancellationToken).ConfigureAwait(false);
        if (!items.Any(b => b.Masked))
        {
            return applyError;
        }

        var maskedResponse = BuildMaskedExplanationResponse(snapshot.Explanation, items, maskedEvidenceRefs);
        static NodeRef[] ParseRefs(IEnumerable<string> refs) =>
            [.. refs.Select(r => NodeRef.TryParse(r, out var parsed) ? parsed : null).Where(r => r is not null).Select(r => r!)];

        var maskedExplanation = new Explanation(
            snapshot.Explanation.Subject,
            snapshot.Explanation.Claim,
            snapshot.Explanation.Producer,
            [.. maskedResponse.Steps.Select(s => new ExplanationStep(s.Claim, ParseRefs(s.Evidence)))],
            [.. maskedResponse.Conditions.Select(c =>
                c.Met
                    ? EvaluatedCondition.Satisfied(c.Code, c.Description, ParseRefs(c.Evidence))
                    : EvaluatedCondition.Unsatisfied(c.Code, c.Description, ParseRefs(c.Evidence)))],
            [.. maskedResponse.NextActions.Select(a =>
                new NextAction(
                    a.Description,
                    a.Capability,
                    a.Target is not null && NodeRef.TryParse(a.Target, out var parsedTarget) ? parsedTarget : null))]);

        return applyError with { Explanation = maskedExplanation };
    }

    private static KernelExplanationResponse BuildMaskedExplanationResponse(
        Explanation explanation,
        IReadOnlyList<KernelLockBlockerResponse> blockerItems,
        IReadOnlySet<string> maskedRefs)
    {
        if (maskedRefs.Count == 0)
        {
            return KernelExplanationResponse.From(explanation);
        }

        IReadOnlyList<string> Scrub(IEnumerable<NodeRef> refs) =>
            [.. refs.Select(r => r.ToString()).Where(r => !maskedRefs.Contains(r))];

        var summaryConditions = explanation.Conditions
            .Where(c => c.Code is LockReadinessEvaluator.QueriesClosedCondition
                or LockReadinessEvaluator.RequiredItemsCompleteCondition
                or LockReadinessEvaluator.ObservationsVerifiedCondition
                or LockReadinessEvaluator.ObservationsReviewedCondition
                or LockReadinessEvaluator.FormsSignedCondition
                or LockReadinessEvaluator.ScreeningEnrollmentsResolvedCondition
                or LockReadinessEvaluator.ScheduledVisitsOpenedCondition
                or LockReadinessEvaluator.ErrorFindingsResolvedCondition)
            .Select(c => new KernelConditionResponse(c.Code, c.Description, c.Met, Scrub(c.Evidence)))
            .ToList();

        static IReadOnlyList<string> BlockerEvidence(KernelLockBlockerResponse blocker)
        {
            if (blocker.Masked || blocker.TargetRef is null)
            {
                return [];
            }

            if (blocker.QueryRef is not null)
            {
                return [blocker.QueryRef, blocker.TargetRef];
            }

            if (blocker.FindingRef is not null)
            {
                return [blocker.FindingRef, blocker.TargetRef];
            }

            return [blocker.TargetRef];
        }

        var blockerConditions = blockerItems
            .Select(b => new KernelConditionResponse(
                b.Kind switch
                {
                    nameof(LockBlockerKind.OpenQuery) => LockReadinessEvaluator.OpenQueryBlockerCondition,
                    nameof(LockBlockerKind.UnverifiedValue) => LockReadinessEvaluator.UnverifiedValueBlockerCondition,
                    nameof(LockBlockerKind.UnreviewedValue) => LockReadinessEvaluator.UnreviewedValueBlockerCondition,
                    nameof(LockBlockerKind.UnsignedForm) => LockReadinessEvaluator.UnsignedFormBlockerCondition,
                    nameof(LockBlockerKind.ScreeningEnrollment) => LockReadinessEvaluator.ScreeningEnrollmentBlockerCondition,
                    nameof(LockBlockerKind.UnopenedVisit) => LockReadinessEvaluator.UnopenedVisitBlockerCondition,
                    nameof(LockBlockerKind.ErrorFinding) => LockReadinessEvaluator.ErrorFindingBlockerCondition,
                    _ => LockReadinessEvaluator.MissingRequiredItemBlockerCondition,
                },
                b.NextAction,
                Met: false,
                BlockerEvidence(b)))
            .ToList();

        var steps = blockerItems
            .Select(b => new KernelExplanationStepResponse(
                b.Masked
                    ? $"{b.Owner} owns a masked {b.Kind} blocker ({b.Mask})."
                    : b.NextAction,
                BlockerEvidence(b)))
            .ToList();

        var nextActions = blockerItems
            .Select(b => new KernelNextActionResponse(
                b.NextAction,
                b.NextCapability,
                b.Masked ? null : b.FindingRef ?? b.TargetRef))
            .ToList();

        return new KernelExplanationResponse(
            explanation.Subject?.ToString(),
            explanation.Claim,
            explanation.Producer,
            steps,
            [.. summaryConditions, .. blockerConditions],
            nextActions);
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

    private static bool TryParseMilestone(string? raw, out LockMilestone milestone)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            milestone = LockMilestone.InterimCut;
            return true;
        }

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "interim_cut", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "InterimCut", StringComparison.OrdinalIgnoreCase))
        {
            milestone = LockMilestone.InterimCut;
            return true;
        }

        if (string.Equals(trimmed, "final_database_lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "FinalDatabaseLock", StringComparison.OrdinalIgnoreCase))
        {
            milestone = LockMilestone.FinalDatabaseLock;
            return true;
        }

        milestone = default;
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

/// <summary>Body of <c>POST /api/v1/kernel/locks</c>.</summary>
/// <param name="StudyRef">The study the scope belongs to, as <c>study:&lt;uuid&gt;</c>.</param>
/// <param name="ScopeRef">The scope to freeze or lock (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c> or <c>observation</c>).</param>
/// <param name="Kind">Either <c>Frozen</c> or <c>Locked</c>.</param>
/// <param name="Meaning">Signature meaning (<c>Approved</c> by default, or <c>Responsibility</c>).</param>
/// <param name="LockId">Optional lock assertion identifier; derived deterministically from the idempotency key when omitted.</param>
/// <param name="AttestationId">Optional attestation identifier; derived deterministically from the idempotency key when omitted.</param>
/// <param name="Milestone">Optional lock milestone (<c>InterimCut</c> by default, or <c>FinalDatabaseLock</c>).</param>
public sealed record KernelApplyLockRequest(
    string StudyRef,
    string ScopeRef,
    string Kind,
    string? Meaning = null,
    Guid? LockId = null,
    Guid? AttestationId = null,
    string? Milestone = null);

/// <summary>Body of <c>POST /api/v1/kernel/locks/{lockRef}/release</c>.</summary>
/// <param name="StudyRef">Optional study NodeRef; resolved from the lock assertion when omitted.</param>
/// <param name="Meaning">Signature meaning (<c>Approved</c> by default, or <c>Responsibility</c>).</param>
/// <param name="AssertionId">Optional retracting assertion identifier; derived deterministically from the idempotency key when omitted.</param>
/// <param name="AttestationId">Optional attestation identifier; derived deterministically from the idempotency key when omitted.</param>
public sealed record KernelReleaseLockRequest(
    string? StudyRef = null,
    string? Meaning = null,
    Guid? AssertionId = null,
    Guid? AttestationId = null);

/// <summary>Body of <c>POST /api/v1/kernel/locks/in-force/batch</c>.</summary>
/// <param name="StudyCode">The study short code, UUID, or <c>study:&lt;uuid&gt;</c> NodeRef to evaluate.</param>
/// <param name="SiteCodes">Optional study site codes, UUIDs, or <c>study_site:&lt;uuid&gt;</c> NodeRefs to evaluate in batch.</param>
public sealed record KernelBatchScopeWriteLockRequest(
    string? StudyCode,
    IReadOnlyList<string>? SiteCodes = null);

/// <summary>Collapsed ADR-2590 indicator reporting whether records outside the caller's scope were omitted.</summary>
/// <param name="MoreExist">True when at least one matching item was omitted because of caller scope visibility.</param>
/// <param name="Reasons">Distinct sorted mask reasons explaining why items were omitted (never counts, kinds, or handles).</param>
public sealed record KernelHiddenContentResponse(
    bool MoreExist,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Canonical empty hidden-content indicator when zero records were omitted.</summary>
    public static KernelHiddenContentResponse None { get; } = new(false, []);

    /// <summary>Builds a collapsed hidden-content indicator from collected mask reasons.</summary>
    public static KernelHiddenContentResponse FromReasons(IEnumerable<MaskReason> reasons)
    {
        var ordered = reasons
            .Select(r => r.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
        return ordered.Count == 0 ? None : new KernelHiddenContentResponse(true, ordered);
    }
}

/// <summary>Locks recorded for a study at an evaluated bitemporal coordinate.</summary>
/// <param name="StudyRef">The study, as a NodeRef.</param>
/// <param name="StudyCode">The study's short code.</param>
/// <param name="ValidAt">The evaluated valid-time coordinate.</param>
/// <param name="KnownAt">The evaluated recorded-time coordinate.</param>
/// <param name="Locks">The locks within the caller's visible scope, with blinded locks masked in place.</param>
/// <param name="Hidden">Collapsed indicator when locks outside the caller's visible scope were omitted.</param>
public sealed record KernelStudyLocksResponse(
    string StudyRef,
    string StudyCode,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<KernelLockResponse> Locks,
    KernelHiddenContentResponse Hidden);

/// <summary>Asymmetrically signed database lock certificate emitted when a study-level <c>Locked</c> assertion is applied.</summary>
public sealed record KernelDatabaseLockCertificateResponse(
    string CertificateId,
    string StudyRef,
    string StudyCode,
    string LockRef,
    string Milestone,
    string AttestationRef,
    string SignedBy,
    DateTimeOffset IssuedAt,
    string KeyId,
    string Algorithm,
    string PayloadSha256,
    string FingerprintSha256,
    string SignatureBase64,
    bool Revoked,
    DateTimeOffset? RevokedAt,
    string? RevokedBy,
    string TenantCode = "")
{
    internal static KernelDatabaseLockCertificateResponse? From(StudyId studyId, AssertionId lockId, DatabaseLockCertificateRecord? record) =>
        record is null
            ? null
            : new(
                record.CertificateId,
                NodeRef.For(studyId).ToString(),
                record.StudyCode,
                NodeRef.For(lockId).ToString(),
                record.Milestone.ToString(),
                $"attestation:{record.AttestationId}",
                record.SignedBy,
                record.IssuedAt,
                record.KeyId,
                record.Algorithm,
                record.PayloadSha256,
                record.FingerprintSha256,
                record.SignatureBase64,
                record.Revoked,
                record.RevokedAt,
                record.RevokedBy,
                record.TenantId);
}

/// <summary>A lock assertion or masked placeholder in a study lock list or mutation response.</summary>
/// <param name="Ref">The lock assertion NodeRef (<c>assertion:&lt;uuid&gt;</c>), or null when masked.</param>
/// <param name="StudyRef">The study NodeRef.</param>
/// <param name="ScopeRef">The locked scope NodeRef, or null when masked.</param>
/// <param name="ScopeKind">The kind of scope (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c>, <c>observation</c>), or null when masked.</param>
/// <param name="StudySiteRef">The governing study-site NodeRef when the lock covers a study site or narrower scope and is visible; null otherwise or when masked.</param>
/// <param name="EnrollmentRef">The governing enrollment NodeRef when the lock covers an enrollment or narrower scope and is visible; null otherwise or when masked.</param>
/// <param name="SubjectCode">The pseudonymous subject code when the lock covers an enrollment or narrower scope; null otherwise or when masked.</param>
/// <param name="Kind">Either <c>Frozen</c> or <c>Locked</c>, or null when masked.</param>
/// <param name="AttestationRef">The electronic signature NodeRef (<c>attestation:&lt;uuid&gt;</c>), or null when masked.</param>
/// <param name="AttestationMeaning">The recorded attestation meaning, or null when masked.</param>
/// <param name="LockedAt">When the lock was applied, or null when masked.</param>
/// <param name="LockedBy">Who applied the lock, or null when masked.</param>
/// <param name="ReasonForChange">The signed reason for applying the lock, or null when masked.</param>
/// <param name="Released">True when the lock has been released at the evaluated coordinate, or null when masked.</param>
/// <param name="ReleaseRef">The release assertion NodeRef when released; null otherwise or when masked.</param>
/// <param name="ReleasedAt">When the lock was released; null while active or when masked.</param>
/// <param name="ReleasedBy">Who released the lock; null while active or when masked.</param>
/// <param name="ReleaseReason">Why the lock was released; null while active or when masked.</param>
/// <param name="Masked">True when caller visibility or blinding hides this lock's details.</param>
/// <param name="Mask">Why the lock is masked (<c>Blinded</c>), or null when visible.</param>
/// <param name="Milestone">The lock milestone (<c>InterimCut</c> or <c>FinalDatabaseLock</c>), or null when masked.</param>
/// <param name="Certificate">The asymmetrically signed database lock certificate for study-level <c>Locked</c> assertions, or null otherwise.</param>
public sealed record KernelLockResponse(
    string? Ref,
    string StudyRef,
    string? ScopeRef,
    string? ScopeKind,
    string? StudySiteRef,
    string? EnrollmentRef,
    string? SubjectCode,
    string? Kind,
    string? AttestationRef,
    string? AttestationMeaning,
    DateTimeOffset? LockedAt,
    string? LockedBy,
    string? ReasonForChange,
    bool? Released,
    string? ReleaseRef,
    DateTimeOffset? ReleasedAt,
    string? ReleasedBy,
    string? ReleaseReason,
    bool Masked,
    string? Mask,
    string? Milestone = null,
    KernelDatabaseLockCertificateResponse? Certificate = null)
{
    internal static KernelLockResponse FromVisible(StudyLockEntry entry) =>
        new(
            NodeRef.For(entry.LockId).ToString(),
            NodeRef.For(entry.StudyId).ToString(),
            entry.Scope.ToString(),
            entry.Scope.Kind,
            entry.StudySiteId is { } studySiteId ? NodeRef.For(studySiteId).ToString() : null,
            entry.EnrollmentId is { } enrollmentId ? NodeRef.For(enrollmentId).ToString() : null,
            entry.SubjectCode,
            entry.Kind.ToString(),
            entry.AttestationId.IsEmpty ? null : NodeRef.For(entry.AttestationId).ToString(),
            entry.AttestationMeaning?.ToString(),
            entry.LockedAt,
            entry.LockedBy,
            entry.ReasonForChange,
            entry.Released,
            entry.ReleaseAssertionId is { } rel ? NodeRef.For(rel).ToString() : null,
            entry.ReleasedAt,
            entry.ReleasedBy,
            entry.ReleaseReason,
            Masked: false,
            Mask: null,
            entry.Milestone.ToString(),
            KernelDatabaseLockCertificateResponse.From(entry.StudyId, entry.LockId, entry.Certificate));

    internal static KernelLockResponse FromMasked(StudyLockEntry entry, MaskReason reason) =>
        new(
            Ref: null,
            StudyRef: NodeRef.For(entry.StudyId).ToString(),
            ScopeRef: null,
            ScopeKind: null,
            StudySiteRef: null,
            EnrollmentRef: null,
            SubjectCode: null,
            Kind: null,
            AttestationRef: null,
            AttestationMeaning: null,
            LockedAt: null,
            LockedBy: null,
            ReasonForChange: null,
            Released: null,
            ReleaseRef: null,
            ReleasedAt: null,
            ReleasedBy: null,
            ReleaseReason: null,
            Masked: true,
            Mask: reason.ToString(),
            Milestone: null,
            Certificate: null);

    internal static KernelLockResponse FromState(StudyId studyId, LockState state, AttestationMeaning meaning, string reason) =>
        new(
            NodeRef.For(state.LockId).ToString(),
            NodeRef.For(studyId).ToString(),
            state.Scope.ToString(),
            state.Scope.Kind,
            StudySiteRef: state.Scope.Kind == NodeKinds.StudySite ? state.Scope.ToString() : null,
            EnrollmentRef: state.Scope.Kind == NodeKinds.Enrollment ? state.Scope.ToString() : null,
            SubjectCode: null,
            state.Kind.ToString(),
            NodeRef.For(state.AttestationId).ToString(),
            meaning.ToString(),
            state.Released ? null : state.At,
            state.Released ? null : state.By,
            state.Released ? null : reason,
            state.Released,
            ReleaseRef: null,
            state.Released ? state.At : null,
            state.Released ? state.By : null,
            state.Released ? reason : null,
            Masked: false,
            Mask: null,
            state.Milestone.ToString(),
            KernelDatabaseLockCertificateResponse.From(studyId, state.LockId, state.Certificate));
}

/// <summary>Live write-lock check response (<c>GET /api/v1/kernel/locks/in-force</c>).</summary>
public sealed record KernelScopeWriteLockResponse(
    bool StudyExists,
    bool Locked,
    string? LockKind,
    string? ScopeKind);

/// <summary>Readiness of a lock scope for a <c>Locked</c> assertion (<c>GET /studies/{studyRef}/locks/readiness</c>).</summary>
public sealed record KernelLockReadinessResponse(
    string StudyRef,
    string StudyCode,
    string ScopeRef,
    string ScopeKind,
    bool ReadyForLock,
    bool CanFreeze,
    int OpenQueryCount,
    int MissingRequiredItemCount,
    int OpenErrorFindingCount,
    int BlockerCount,
    IReadOnlyList<KernelLockBlockerResponse> Blockers,
    KernelExplanationResponse Explanation,
    int UnverifiedValueCount = 0,
    int UnsignedFormCount = 0,
    int UnopenedVisitCount = 0,
    string Milestone = nameof(LockMilestone.InterimCut),
    int ScreeningEnrollmentCount = 0,
    int PendingScheduledVisitCount = 0,
    int UnreviewedValueCount = 0);

/// <summary>One unresolved blocker preventing a <c>Locked</c> assertion at a scope, masked in place under <c>K-AUTH-4</c>.</summary>
public sealed record KernelLockBlockerResponse(
    string Kind,
    string Owner,
    string? TargetRef,
    string TargetKind,
    string? SubjectCode,
    string? CollectionSpecId,
    string? CollectionTitle,
    string? ItemId,
    string? ItemLabel,
    string? QueryRef,
    string? QueryText,
    bool Answered,
    string NextAction,
    string NextCapability,
    bool Masked,
    string? Mask,
    string? FindingRef = null,
    string? RuleId = null,
    string? Detail = null,
    int? CurrentRevision = null,
    bool SignatureSuperseded = false,
    string? PlannedActivityId = null,
    string? PlannedActivityName = null,
    DateOnly? PlannedDate = null,
    DateOnly? WindowEnd = null)
{
    internal static KernelLockBlockerResponse FromVisible(LockReadinessBlocker blocker) =>
        new(
            blocker.Kind.ToString(),
            blocker.Owner,
            blocker.Target.ToString(),
            blocker.Target.Kind,
            blocker.SubjectCode,
            blocker.CollectionSpecId,
            blocker.CollectionTitle,
            blocker.ItemId,
            blocker.ItemLabel,
            blocker.QueryAssertionId is { } qid ? NodeRef.For(qid).ToString() : null,
            blocker.QueryText,
            blocker.Answered,
            blocker.NextAction,
            blocker.NextCapability,
            Masked: false,
            Mask: null,
            FindingRef: blocker.FindingId is { } fid ? NodeRef.For(fid).ToString() : null,
            RuleId: blocker.RuleId,
            Detail: ResolveBlockerDetail(blocker),
            CurrentRevision: blocker.CurrentRevision,
            SignatureSuperseded: blocker.SignatureSuperseded,
            PlannedActivityId: blocker.PlannedActivityId,
            PlannedActivityName: blocker.PlannedActivityName,
            PlannedDate: blocker.PlannedDate,
            WindowEnd: blocker.WindowEnd);

    private static string? ResolveBlockerDetail(LockReadinessBlocker blocker)
    {
        if (blocker.FindingMessage is not null)
        {
            return blocker.FindingMessage;
        }
        if (blocker.QueryText is not null)
        {
            return blocker.QueryText;
        }
        if (blocker.Kind == LockBlockerKind.UnverifiedValue && blocker.CurrentRevision is { } rev)
        {
            return $"Revision {rev} awaiting source verification";
        }
        if (blocker.Kind == LockBlockerKind.UnreviewedValue && blocker.CurrentRevision is { } rRev)
        {
            return $"Revision {rRev} awaiting source data review";
        }
        return blocker.ItemLabel;
    }

    internal static KernelLockBlockerResponse FromMasked(LockReadinessBlocker blocker, MaskReason reason)
    {
        var blockerLabel = blocker.Kind switch
        {
            LockBlockerKind.OpenQuery => "open query",
            LockBlockerKind.UnverifiedValue => "unverified observation revision",
            LockBlockerKind.UnreviewedValue => "unreviewed observation revision",
            LockBlockerKind.UnsignedForm => blocker.SignatureSuperseded ? "superseded form signature" : "unsigned form",
            LockBlockerKind.ScreeningEnrollment => "screening enrollment disposition",
            LockBlockerKind.UnopenedVisit => "unopened scheduled visit",
            LockBlockerKind.ErrorFinding => "Error validation finding",
            _ => "missing required item",
        };
        var maskedAction = reason == MaskReason.Blinded
            ? $"{blocker.Owner}: resolve blinded {blockerLabel} before locking."
            : $"{blocker.Owner}: resolve restricted {blockerLabel} in another scope before locking.";
        return new(
            blocker.Kind.ToString(),
            blocker.Owner,
            TargetRef: null,
            blocker.Target.Kind,
            SubjectCode: null,
            CollectionSpecId: null,
            CollectionTitle: null,
            ItemId: null,
            ItemLabel: null,
            QueryRef: null,
            QueryText: null,
            blocker.Answered,
            maskedAction,
            blocker.NextCapability,
            Masked: true,
            Mask: reason.ToString());
    }
}
