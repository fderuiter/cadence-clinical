using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Kernel.Api.Locks;

internal static class KernelLockProbeEndpoints
{
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
                    var decision = await probe.CanIAsync(
                        tenant,
                        KernelLockReadinessEndpoints.EnrollmentUnblindCapability,
                        unblindTarget,
                        cancellationToken).ConfigureAwait(false);
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
}
