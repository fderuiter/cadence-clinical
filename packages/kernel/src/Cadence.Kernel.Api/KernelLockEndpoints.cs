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

        locks.MapGet("/check", CheckActiveLockAsync)
            .AllowBaselineProductFamily()
            .WithName("KernelCheckActiveLock")
            .WithSummary("Checks whether any active Frozen or Locked assertion covers a study, study site, or subject enrollment.")
            .Produces<KernelLockCheckResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status423Locked);

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
            .WithSummary("Evaluates lock readiness (open queries, missing required items, unresolved Error validation findings, unverified revisions, and missing signatures) for a study or child lock scope.")
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
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
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
                StepUpMethod),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (applyResult.Error is { } applyError)
        {
            if (string.Equals(applyError.Code, AssertionErrors.ScopeNotReady.Code, StringComparison.Ordinal)
                && await locks.ReadReadinessAsync(tenant, studyId, scope, cancellationToken).ConfigureAwait(false) is { } readinessSnapshot)
            {
                var (_, maskedExplanation) = await MaskReadinessSnapshotAsync(
                    readinessSnapshot,
                    studyNode,
                    tenant,
                    visibility,
                    probe,
                    actor,
                    cancellationToken).ConfigureAwait(false);
                return KernelErrorEndpoints.Problem(
                    applyError.WithMessage(maskedExplanation.Claim) with { Explanation = maskedExplanation });
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
        if (!NodeRef.TryParse(studyRef, out var studyNode))
        {
            return KernelProblems.InvalidNodeRef(studyRef);
        }

        if (studyNode.Kind != NodeKinds.Study || studyNode.Version is not null)
        {
            return KernelProblems.WrongKind(studyNode, NodeKinds.Study);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is not { } resolvedStudy)
        {
            return KernelProblems.NotFound(studyNode);
        }

        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [resolvedStudy.Ref], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(resolvedStudy.Ref) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You may not see this study ({studyMask}), so its locks are not listed.");
        }

        var now = clock.UtcNow;
        var effectiveValidAt = validAt ?? asOf ?? now;
        var effectiveKnownAt = knownAt ?? asOf ?? now;

        var list = await locks.ListByStudyAsync(
            tenant,
            StudyId.FromGuid(studyNode.Id),
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
        var items = new List<KernelLockResponse>(list.Locks.Count);
        foreach (var entry in list.Locks)
        {
            // CollectionInstance and Observation scopes inherit visibility from their Enrollment and StudySite (ADR-2457).
            var mask = (entry.Scope.Kind is NodeKinds.Study or NodeKinds.StudySite or NodeKinds.Enrollment
                    ? masks.For(entry.Scope)
                    : null)
                ?? (entry.EnrollmentId is { } eid ? masks.For(NodeRef.For(eid)) : null)
                ?? (entry.StudySiteId is { } sid ? masks.For(NodeRef.For(sid)) : null);

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
            items));
    }

    internal static async Task<Results<Ok<KernelLockReadinessResponse>, ProblemHttpResult>> GetReadinessAsync(
        string studyRef,
        [FromQuery(Name = "scope_ref")] string? scopeRef,
        HttpContext context,
        IKernelNavigator navigator,
        ILockRecords locks,
        INodeVisibility visibility,
        IAuthorizationProbe probe,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(studyRef, out var studyNode))
        {
            return KernelProblems.InvalidNodeRef(studyRef);
        }

        if (studyNode.Kind != NodeKinds.Study || studyNode.Version is not null)
        {
            return KernelProblems.WrongKind(studyNode, NodeKinds.Study);
        }

        var targetScope = studyNode;
        if (!string.IsNullOrWhiteSpace(scopeRef)
            && (!NodeRef.TryParse(scopeRef.Trim(), out targetScope) || !LockScopes.IsScope(targetScope)))
        {
            return KernelProblems.BadRequest(
                "Invalid lock scope",
                "Name scope_ref as an unversioned study, study_site, enrollment, collection_instance or observation NodeRef.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, studyNode, cancellationToken).ConfigureAwait(false) is not { } resolvedStudy)
        {
            return KernelProblems.NotFound(studyNode);
        }

        var initialNodes = targetScope == studyNode
            ? new[] { resolvedStudy.Ref }
            : new[] { resolvedStudy.Ref, targetScope };
        var initialMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, initialNodes, cancellationToken).ConfigureAwait(false);
        if (initialMasks.For(resolvedStudy.Ref) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You may not see this study ({studyMask}), so its lock readiness is not available.");
        }

        if (targetScope != studyNode && initialMasks.For(targetScope) is { } scopeMask)
        {
            return KernelProblems.NotVisible($"You may not see this lock scope ({scopeMask}), so its lock readiness is not available.");
        }

        var snapshot = await locks.ReadReadinessAsync(
            tenant,
            StudyId.FromGuid(studyNode.Id),
            targetScope,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return KernelProblems.NotFound(targetScope);
        }

        var (items, maskedExplanation) = await MaskReadinessSnapshotAsync(
            snapshot,
            studyNode,
            tenant,
            visibility,
            probe,
            actor,
            cancellationToken).ConfigureAwait(false);
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
            snapshot.UnverifiedRevisionCount,
            snapshot.MissingSignatureCount,
            items.Count,
            items,
            KernelExplanationResponse.From(maskedExplanation)));
    }

    internal static async Task<Results<Ok<KernelLockCheckResponse>, ProblemHttpResult>> CheckActiveLockAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "site_code")] string? siteCode,
        [FromQuery(Name = "subject_code")] string? subjectCode,
        HttpContext context,
        ILockRecords locks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return KernelProblems.BadRequest("Study code required", "Supply a non-empty study_code query parameter.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var active = await locks.CheckCoveringLocksAsync(tenant, studyCode, siteCode, subjectCode, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            return TypedResults.Ok(new KernelLockCheckResponse(false, studyCode.Trim(), null, null));
        }

        var message = $"An active {active.Kind} assertion on {active.Scope.Kind} scope refuses writes for study '{active.StudyCode}' until that lock is released.";
        return KernelErrorEndpoints.Problem(ObservationErrors.Locked.WithMessage(message));
    }

    private static async Task<(IReadOnlyList<KernelLockBlockerResponse> Items, Explanation MaskedExplanation)> MaskReadinessSnapshotAsync(
        LockReadinessSnapshot snapshot,
        NodeRef studyNode,
        TenantId tenant,
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
        var maskedRefs = new HashSet<NodeRef>();

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
                    maskedRefs.Add(blocker.Target);
                }

                if (blocker.ObservationId is { } oid)
                {
                    maskedRefs.Add(NodeRef.For(oid));
                }

                if (blocker.QueryAssertionId is { } qid)
                {
                    maskedRefs.Add(NodeRef.For(qid));
                }

                if (blocker.FindingId is { } fid)
                {
                    maskedRefs.Add(NodeRef.For(fid));
                }

                items.Add(KernelLockBlockerResponse.FromMasked(blocker, reason));
            }
            else
            {
                items.Add(KernelLockBlockerResponse.FromVisible(blocker));
            }
        }

        var maskedExplanation = BuildMaskedExplanation(snapshot.Explanation, items, maskedRefs);
        return (items, maskedExplanation);
    }

    private static Explanation BuildMaskedExplanation(
        Explanation explanation,
        IReadOnlyList<KernelLockBlockerResponse> blockerItems,
        IReadOnlySet<NodeRef> maskedRefs)
    {
        if (maskedRefs.Count == 0)
        {
            return explanation;
        }

        IReadOnlyList<NodeRef> Scrub(IEnumerable<NodeRef> refs) =>
            [.. refs.Where(r => !maskedRefs.Contains(r))];

        var summaryConditions = explanation.Conditions
            .Where(c => c.Code is LockReadinessEvaluator.QueriesClosedCondition
                or LockReadinessEvaluator.RequiredItemsCompleteCondition
                or LockReadinessEvaluator.ErrorFindingsResolvedCondition
                or LockReadinessEvaluator.RevisionsVerifiedCondition
                or LockReadinessEvaluator.SignaturesCompleteCondition)
            .Select(c => new EvaluatedCondition(c.Code, c.Description, c.Met, Scrub(c.Evidence)))
            .ToList();

        static IReadOnlyList<NodeRef> BlockerEvidence(KernelLockBlockerResponse blocker)
        {
            if (blocker.Masked || blocker.TargetRef is null || !NodeRef.TryParse(blocker.TargetRef, out var target))
            {
                return [];
            }

            if (blocker.QueryRef is not null && NodeRef.TryParse(blocker.QueryRef, out var query))
            {
                return [query, target];
            }

            if (blocker.FindingRef is not null && NodeRef.TryParse(blocker.FindingRef, out var finding))
            {
                return [finding, target];
            }

            return [target];
        }

        static string ConditionCodeForKind(string kind) =>
            Enum.TryParse<LockBlockerKind>(kind, out var parsed)
                ? LockReadinessEvaluator.BlockerConditionCode(parsed)
                : LockReadinessEvaluator.MissingRequiredItemBlockerCondition;

        var blockerConditions = blockerItems
            .Select(b => new EvaluatedCondition(
                ConditionCodeForKind(b.Kind),
                b.NextAction,
                Met: false,
                BlockerEvidence(b)))
            .ToList();

        var steps = blockerItems
            .Select(b => new ExplanationStep(
                b.Masked
                    ? $"{b.Owner} owns a masked {b.Kind} blocker ({b.Mask})."
                    : b.NextAction,
                BlockerEvidence(b)))
            .ToList();

        var nextActions = blockerItems
            .Select(b => new NextAction(
                b.NextAction,
                b.NextCapability,
                !b.Masked && (b.FindingRef ?? b.TargetRef) is { } targetText && NodeRef.TryParse(targetText, out var target) ? target : null))
            .ToList();

        return new Explanation(
            explanation.Subject,
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
public sealed record KernelApplyLockRequest(
    string StudyRef,
    string ScopeRef,
    string Kind,
    string? Meaning = null,
    Guid? LockId = null,
    Guid? AttestationId = null);

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

/// <summary>Locks recorded for a study at an evaluated bitemporal coordinate.</summary>
/// <param name="StudyRef">The study, as a NodeRef.</param>
/// <param name="StudyCode">The study's short code.</param>
/// <param name="ValidAt">The evaluated valid-time coordinate.</param>
/// <param name="KnownAt">The evaluated recorded-time coordinate.</param>
/// <param name="Locks">The locks matching the query, with unauthorized or blinded scopes masked in place.</param>
public sealed record KernelStudyLocksResponse(
    string StudyRef,
    string StudyCode,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<KernelLockResponse> Locks);

/// <summary>A lock assertion or masked placeholder in a study lock list or mutation response.</summary>
/// <param name="Ref">The lock assertion NodeRef (<c>assertion:&lt;uuid&gt;</c>), or null when masked.</param>
/// <param name="StudyRef">The study NodeRef.</param>
/// <param name="ScopeRef">The locked scope NodeRef, or null when masked.</param>
/// <param name="ScopeKind">The kind of scope (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c>, <c>observation</c>).</param>
/// <param name="StudySiteRef">The governing study-site NodeRef when the lock covers a study site or narrower scope and is visible; null otherwise or when masked.</param>
/// <param name="EnrollmentRef">The governing enrollment NodeRef when the lock covers an enrollment or narrower scope and is visible; null otherwise or when masked.</param>
/// <param name="SubjectCode">The pseudonymous subject code when the lock covers an enrollment or narrower scope; null otherwise or when masked.</param>
/// <param name="Kind">Either <c>Frozen</c> or <c>Locked</c>.</param>
/// <param name="AttestationRef">The electronic signature NodeRef (<c>attestation:&lt;uuid&gt;</c>), or null when masked.</param>
/// <param name="AttestationMeaning">The recorded attestation meaning, or null when masked.</param>
/// <param name="LockedAt">When the lock was applied, or null when masked.</param>
/// <param name="LockedBy">Who applied the lock, or null when masked.</param>
/// <param name="ReasonForChange">The signed reason for applying the lock, or null when masked.</param>
/// <param name="Released">True when the lock has been released at the evaluated coordinate.</param>
/// <param name="ReleaseRef">The release assertion NodeRef when released; null otherwise or when masked.</param>
/// <param name="ReleasedAt">When the lock was released; null while active or when masked.</param>
/// <param name="ReleasedBy">Who released the lock; null while active or when masked.</param>
/// <param name="ReleaseReason">Why the lock was released; null while active or when masked.</param>
/// <param name="Masked">True when caller visibility or blinding hides this lock's details.</param>
/// <param name="Mask">Why the lock is masked (<c>NotPermitted</c> or <c>Blinded</c>), or null when visible.</param>
public sealed record KernelLockResponse(
    string? Ref,
    string StudyRef,
    string? ScopeRef,
    string ScopeKind,
    string? StudySiteRef,
    string? EnrollmentRef,
    string? SubjectCode,
    string Kind,
    string? AttestationRef,
    string? AttestationMeaning,
    DateTimeOffset? LockedAt,
    string? LockedBy,
    string? ReasonForChange,
    bool Released,
    string? ReleaseRef,
    DateTimeOffset? ReleasedAt,
    string? ReleasedBy,
    string? ReleaseReason,
    bool Masked,
    string? Mask)
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
            Mask: null);

    internal static KernelLockResponse FromMasked(StudyLockEntry entry, MaskReason reason) =>
        new(
            Ref: null,
            StudyRef: NodeRef.For(entry.StudyId).ToString(),
            ScopeRef: null,
            ScopeKind: entry.Scope.Kind,
            StudySiteRef: null,
            EnrollmentRef: null,
            SubjectCode: null,
            Kind: entry.Kind.ToString(),
            AttestationRef: null,
            AttestationMeaning: null,
            LockedAt: null,
            LockedBy: null,
            ReasonForChange: null,
            Released: entry.Released,
            ReleaseRef: null,
            ReleasedAt: null,
            ReleasedBy: null,
            ReleaseReason: null,
            Masked: true,
            Mask: reason.ToString());

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
            Mask: null);
}

/// <summary>Response returned by <c>GET /api/v1/kernel/locks/check</c> when no covering lock blocks writes.</summary>
public sealed record KernelLockCheckResponse(
    bool Locked,
    string StudyCode,
    string? Kind,
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
    int UnverifiedRevisionCount,
    int MissingSignatureCount,
    int BlockerCount,
    IReadOnlyList<KernelLockBlockerResponse> Blockers,
    KernelExplanationResponse Explanation);

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
    string? Detail = null)
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
            Detail: blocker.FindingMessage ?? blocker.QueryText ?? blocker.ItemLabel);

    internal static KernelLockBlockerResponse FromMasked(LockReadinessBlocker blocker, MaskReason reason)
    {
        var blockerLabel = blocker.Kind switch
        {
            LockBlockerKind.OpenQuery => "open query",
            LockBlockerKind.ErrorFinding => "Error validation finding",
            LockBlockerKind.MissingRequiredItem => "missing required item",
            LockBlockerKind.UnverifiedRevision => "unverified observation revision",
            _ => "missing form signature",
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
