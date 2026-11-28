using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

internal static class KernelSearchAndScheduleReadEndpoints
{
    internal static async Task<Results<Ok<IReadOnlyList<KernelSearchHitResponse>>, ProblemHttpResult>> SearchAsync(
        string q,
        string[]? kind,
        int? limit,
        HttpContext context,
        IKernelSearch search,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var pageSize = limit ?? 20;
        if (pageSize is < 1 or > SearchRequest.MaxLimit)
        {
            return KernelProblems.BadRequest("Invalid limit", $"limit must be between 1 and {SearchRequest.MaxLimit}.");
        }

        var request = new SearchRequest(q, kind is { Length: > 0 } ? kind : null, pageSize);
        var tenant = KernelRequestTenant.Get(context);
        var hits = await search.SearchAsync(tenant, request, cancellationToken).ConfigureAwait(false);

        // A hit matched the caller's text, so a masked one would confirm the hidden object exists: it is left out.
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, hits.Select(h => h.Ref), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelSearchHitResponse>>(
            [.. hits.Where(h => masks.For(h.Ref) is null).Select(KernelSearchHitResponse.From)]);
    }

    internal static async Task<Results<Ok<KernelEnrollmentLookupResponse>, ProblemHttpResult>> LookupEnrollmentAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "subject_code")] string? subjectCode,
        [FromQuery(Name = "site_code")] string? siteCode,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var trimmedStudyCode = studyCode?.Trim();
        var trimmedSubjectCode = subjectCode?.Trim();
        var trimmedSiteCode = string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim();

        if (string.IsNullOrEmpty(trimmedStudyCode)
            || trimmedStudyCode.Length > SubjectCode.MaxLength
            || trimmedStudyCode.Any(char.IsControl)
            || (trimmedSiteCode is not null && (trimmedSiteCode.Length > SubjectCode.MaxLength || trimmedSiteCode.Any(char.IsControl)))
            || !SubjectCode.TryFrom(trimmedSubjectCode, out var parsedSubjectCode))
        {
            return KernelProblems.BadRequest(
                "Invalid lookup query",
                "Name a valid study_code, subject_code, and optional site_code.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await enrollments.LookupByCodeAsync(tenant, trimmedStudyCode, parsedSubjectCode, trimmedSiteCode, cancellationToken).ConfigureAwait(false)
            is not { } match)
        {
            return KernelProblems.EnrollmentLookupNotFound();
        }

        var studyRef = NodeRef.For(match.StudyId, match.StudyVersion);
        var studySiteRef = NodeRef.For(match.StudySiteId, match.StudySiteVersion);
        var enrollmentRef = NodeRef.For(match.EnrollmentId, match.EnrollmentVersion);

        // As in SearchAsync, a code lookup matches caller-supplied codes, so confirming a hidden study, study site,
        // or enrollment would reveal that it exists (ADR-2471): any masked node returns the same 404 as absent.
        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            [studyRef, studySiteRef, enrollmentRef],
            cancellationToken).ConfigureAwait(false);
        if (masks.For(studyRef) is not null || masks.For(studySiteRef) is not null || masks.For(enrollmentRef) is not null)
        {
            return KernelProblems.EnrollmentLookupNotFound();
        }

        return TypedResults.Ok(KernelEnrollmentLookupResponse.From(match));
    }

    internal static async Task<Results<Ok<KernelEnrollmentScheduleResponse>, ProblemHttpResult>> GetEnrollmentScheduleAsync(
        string enrollmentRef,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IProtocolVersionReader versionReader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken,
        [FromQuery(Name = "as_of")] DateOnly? asOf = null,
        [FromQuery(Name = "time_zone")] string? timeZone = null)
    {
        var trimmed = (enrollmentRef ?? string.Empty).Trim();
        NodeRef node;
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (!NodeRef.TryParse(trimmed, out var parsedNode) || parsedNode is null)
            {
                return KernelProblems.InvalidNodeRef(trimmed);
            }

            node = parsedNode;
            if (node.Kind != NodeKinds.Enrollment)
            {
                return KernelProblems.WrongKind(node, NodeKinds.Enrollment);
            }
        }
        else if (Guid.TryParse(trimmed, out var rawGuid) && rawGuid != Guid.Empty)
        {
            node = NodeRef.For(EnrollmentId.FromGuid(rawGuid));
        }
        else
        {
            return KernelProblems.InvalidNodeRef(trimmed);
        }

        TimeZoneInfo? explicitTimeZone = null;
        if (!string.IsNullOrWhiteSpace(timeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out explicitTimeZone))
        {
            return KernelProblems.BadRequest("Invalid time_zone", $"Time zone '{timeZone}' is not recognized.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var enrollmentId = EnrollmentId.FromGuid(node.Id);
        if (await enrollments.FindAsync(tenant, enrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return KernelProblems.NotFound(NodeRef.For(enrollmentId));
        }

        var studyNode = NodeRef.For(enrollment.StudyId);
        var studySiteNode = NodeRef.For(enrollment.StudySiteId);
        var enrollmentNode = NodeRef.For(enrollment.EnrollmentId);
        var scopeMasks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            [studyNode, studySiteNode, enrollmentNode],
            cancellationToken).ConfigureAwait(false);
        if (scopeMasks.For(studyNode) is not null
            || scopeMasks.For(studySiteNode) is not null
            || scopeMasks.For(enrollmentNode) is not null)
        {
            // Non-confirming 404 when the study, study site, or enrollment is hidden or blinded for the caller (ADR-2471).
            return KernelProblems.NotFound(enrollmentNode);
        }

        var clock = context.RequestServices?.GetService<IClock>() ?? SystemClock.Instance;
        var now = clock.UtcNow;
        var referenceDate = ToSiteLocalDate(enrollment.StatusSince, explicitTimeZone);
        var effectiveAsOf = asOf ?? ToSiteLocalNowDate(now, enrollment.StatusSince.Offset, explicitTimeZone);

        StudySiteGovernanceSummary? siteGovernance = null;
        if (context.RequestServices?.GetService<IStudyGovernanceReader>() is { } governanceReader)
        {
            siteGovernance = await governanceReader.FindStudySiteAsync(
                tenant,
                enrollment.StudySiteId,
                now,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<EffectiveVersion> effectiveVersions = siteGovernance?.EffectiveVersions ?? [];
        if (siteGovernance is null && context.RequestServices?.GetService<IEffectivityReader>() is { } effectivityReader)
        {
            effectiveVersions = await effectivityReader.EffectiveAtAsync(
                tenant,
                enrollment.StudySiteId,
                now,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        string? siteCode = siteGovernance?.SiteCode;
        string? country = siteGovernance?.Country;
        if ((siteCode is null || country is null) && context.RequestServices?.GetService<IStudyDirectory>() is { } studyDirectory)
        {
            var siteEntry = await studyDirectory.FindStudySiteAsync(tenant, enrollment.StudySiteId, cancellationToken).ConfigureAwait(false);
            siteCode ??= siteEntry?.SiteCode;
            country ??= siteEntry?.Country;
        }

        var studyVersions = await versionReader.ListForStudyAsync(tenant, enrollment.StudyId, cancellationToken).ConfigureAwait(false);
        var studyVersionMasks = studyVersions.Count == 0
            ? new Dictionary<NodeRef, MaskReason>()
            : await KernelReadMasking.MaskAsync(
                visibility,
                actor,
                tenant,
                studyVersions.Select(v => NodeRef.For(v.Id)),
                cancellationToken).ConfigureAwait(false);
        var latestVisibleVersion = studyVersions
            .FirstOrDefault(v => studyVersionMasks.For(NodeRef.For(v.Id)) is null);
        var latestStatus = latestVisibleVersion?.Status.ToString();

        if (effectiveVersions.Count == 0)
        {
            var guidance = BuildNoEffectiveVersionGuidance(latestVisibleVersion?.Status);
            return TypedResults.Ok(new KernelEnrollmentScheduleResponse(
                enrollmentNode.ToString(),
                studyNode.ToString(),
                studySiteNode.ToString(),
                siteCode,
                country,
                enrollment.Status.ToString(),
                enrollment.StatusSince,
                referenceDate,
                effectiveAsOf,
                GovernanceStatus: "NoEffectiveVersion",
                LatestStudyVersionStatus: latestStatus,
                Guidance: guidance,
                GoverningVersion: null,
                EffectiveVersions: [],
                Schedule: null,
                ExpectedVisits: []));
        }

        var governing = effectiveVersions
            .OrderByDescending(v => v.Number)
            .First();
        var governingVersionNode = NodeRef.For(governing.ProtocolVersionId);
        var versionMasks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            [governingVersionNode],
            cancellationToken).ConfigureAwait(false);
        if (versionMasks.For(governingVersionNode) is not null)
        {
            return KernelProblems.NotFound(enrollmentNode);
        }

        var versionSummary = studyVersions.FirstOrDefault(v => v.Id == governing.ProtocolVersionId)
            ?? await versionReader.GetAsync(tenant, governing.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        var design = await versionReader.GetDesignAsync(tenant, governing.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        KernelScheduleOfActivitiesResponse? scheduleResponse = null;
        IReadOnlyList<KernelExpectedVisitResponse> expectedVisitResponses = [];

        if (design is not null && versionSummary is not null)
        {
            var matrix = ScheduleOfActivities.Compile(design);
            scheduleResponse = KernelScheduleOfActivitiesResponse.From(versionSummary, matrix);
            var expected = ExpectedSchedule.Compute(design, referenceDate, effectiveAsOf);
            expectedVisitResponses = [.. expected.Select(KernelExpectedVisitResponse.From)];
        }

        return TypedResults.Ok(new KernelEnrollmentScheduleResponse(
            enrollmentNode.ToString(),
            studyNode.ToString(),
            studySiteNode.ToString(),
            siteCode,
            country,
            enrollment.Status.ToString(),
            enrollment.StatusSince,
            referenceDate,
            effectiveAsOf,
            GovernanceStatus: "Effective",
            LatestStudyVersionStatus: latestStatus ?? versionSummary?.Status.ToString(),
            Guidance: null,
            GoverningVersion: KernelEffectiveVersionResponse.From(governing),
            EffectiveVersions: [.. effectiveVersions.OrderByDescending(v => v.Number).Select(KernelEffectiveVersionResponse.From)],
            Schedule: scheduleResponse,
            ExpectedVisits: expectedVisitResponses));
    }

    private static DateOnly ToSiteLocalDate(DateTimeOffset instant, TimeZoneInfo? explicitTimeZone) =>
        explicitTimeZone is not null
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, explicitTimeZone).DateTime)
            : DateOnly.FromDateTime(instant.DateTime);

    private static DateOnly ToSiteLocalNowDate(DateTimeOffset utcNow, TimeSpan siteOffset, TimeZoneInfo? explicitTimeZone) =>
        explicitTimeZone is not null
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, explicitTimeZone).DateTime)
            : DateOnly.FromDateTime(utcNow.ToOffset(siteOffset).DateTime);

    private static string BuildNoEffectiveVersionGuidance(ProtocolVersionStatus? latestStatus) =>
        latestStatus switch
        {
            ProtocolVersionStatus.Draft or ProtocolVersionStatus.InReview =>
                "No protocol version is effective at this study site yet. Submit and approve the protocol draft, then declare and activate site effectivity before capturing scheduled forms.",
            ProtocolVersionStatus.Approved =>
                "An approved protocol version exists for this study, but no effectivity is active at this study site yet. Declare and activate site effectivity in Governance to unlock casebook schedule columns.",
            _ =>
                "No protocol version is effective at this study site yet. Import and approve a protocol version, then activate site effectivity before capturing scheduled forms.",
        };

    internal static async Task<Results<Ok<IReadOnlyList<KernelProtocolVersionResponse>>, ProblemHttpResult>> ListProtocolVersionsAsync(
        string studyRef,
        HttpContext context,
        IKernelNavigator navigator,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(studyRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(studyRef);
        }

        if (node.Kind != NodeKinds.Study)
        {
            return KernelProblems.WrongKind(node, NodeKinds.Study);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } study)
        {
            return KernelProblems.NotFound(node);
        }

        // Listing a hidden study's versions would describe the study, so the list is refused, not masked.
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [study.Ref], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(study.Ref) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You can't see this study ({studyMask}), so its protocol versions are not listed.");
        }

        var versions = await reader.ListForStudyAsync(tenant, StudyId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false);
        var masks = await KernelReadMasking.MaskAsync(
            visibility, actor, tenant, versions.Select(v => NodeRef.For(v.Id)), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelProtocolVersionResponse>>(
            [.. versions.Select(v => KernelProtocolVersionResponse.From(v, masks.For(NodeRef.For(v.Id))))]);
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> GetProtocolVersionAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(versionRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await reader.GetAsync(tenant, ProtocolVersionId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(node);
        }

        var versionNode = NodeRef.For(version.Id);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelProtocolVersionResponse.From(version, masks.For(versionNode)));
    }

    internal static async Task<Results<Ok<KernelScheduleOfActivitiesResponse>, ProblemHttpResult>> GetScheduleOfActivitiesAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(versionRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var tenant = KernelRequestTenant.Get(context);
        var id = ProtocolVersionId.FromGuid(node.Id);
        if (await reader.GetAsync(tenant, id, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(node);
        }

        // The schedule is the version's content, so a version the caller may not see is refused, not masked.
        var versionNode = NodeRef.For(version.Id);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(versionNode) is { } mask)
        {
            return KernelProblems.NotVisible($"You can't see this protocol version ({mask}), so its Schedule of Activities is not shown.");
        }

        return await reader.GetScheduleOfActivitiesAsync(tenant, id, cancellationToken).ConfigureAwait(false) is { } matrix
            ? TypedResults.Ok(KernelScheduleOfActivitiesResponse.From(version, matrix))
            : KernelProblems.BadRequest("Not a typed design", $"{node} holds content that is not a typed protocol design, so it has no Schedule of Activities.");
    }
}
