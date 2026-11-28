using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Intelligence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.Validation;

internal static class KernelValidationFindingReadEndpoints
{
    internal static async Task<Results<Ok<KernelValidationFindingPageResponse>, ProblemHttpResult>> ListStudyFindingsAsync(
        string studyRef,
        string? stage,
        [FromQuery(Name = "status")] string[]? status,
        string? severity,
        string? dataset,
        string? pack_key,
        int? limit,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        ValidationStage? parsedStage = null;
        if (!string.IsNullOrWhiteSpace(stage))
        {
            if (!Enum.TryParse<ValidationStage>(stage, ignoreCase: true, out var s) || !Enum.IsDefined(s) || int.TryParse(stage, out _))
            {
                return KernelProblems.BadRequest("Invalid stage", $"'{stage}' is not a valid validation stage.");
            }

            parsedStage = s;
        }

        HashSet<FindingStatus>? parsedStatuses = null;
        if (status is { Length: > 0 })
        {
            foreach (var rawStatus in status)
            {
                if (string.IsNullOrWhiteSpace(rawStatus))
                {
                    continue;
                }

                if (!Enum.TryParse<FindingStatus>(rawStatus, ignoreCase: true, out var st) || !Enum.IsDefined(st) || int.TryParse(rawStatus, out _))
                {
                    return KernelProblems.BadRequest("Invalid status", $"'{rawStatus}' is not a valid finding status.");
                }

                parsedStatuses ??= [];
                parsedStatuses.Add(st);
            }
        }

        ValidationSeverity? parsedSeverity = null;
        if (!string.IsNullOrWhiteSpace(severity))
        {
            if (!Enum.TryParse<ValidationSeverity>(severity, ignoreCase: true, out var sev) || !Enum.IsDefined(sev))
            {
                return KernelProblems.BadRequest("Invalid severity", $"'{severity}' is not a valid validation severity.");
            }

            parsedSeverity = sev;
        }

        if (limit is < 1 or > FindingQuery.MaxTake)
        {
            return KernelProblems.BadRequest("Invalid limit", $"Limit must be between 1 and {FindingQuery.MaxTake}.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await KernelRulePackGovernanceEndpoints.ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var findings = context.RequestServices.GetService<IValidationFindings>();
        if (findings is null)
        {
            return TypedResults.Ok(new KernelValidationFindingPageResponse(0, []));
        }

        var rawPage = await findings.ListAsync(
            tenant,
            new FindingQuery(resolved.StudyId)
            {
                Stage = parsedStage,
                Statuses = parsedStatuses,
                Severity = parsedSeverity,
                Dataset = string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim(),
                PackKey = string.IsNullOrWhiteSpace(pack_key) ? null : pack_key.Trim(),
                Take = FindingQuery.MaxTake,
            },
            cancellationToken).ConfigureAwait(false);

        if (rawPage.Findings.Count == 0)
        {
            return TypedResults.Ok(new KernelValidationFindingPageResponse(0, []));
        }

        var visibleFindings = await KernelValidationFindingVisibility.FilterVisibleFindingsAsync(
            tenant,
            resolved.StudyId,
            rawPage.Findings,
            context.RequestServices,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);
        var take = limit ?? 100;

        return TypedResults.Ok(new KernelValidationFindingPageResponse(
            visibleFindings.Count,
            [.. visibleFindings.Take(take).Select(KernelValidationFindingResponse.From)]));
    }

    internal static async Task<Results<Ok<KernelStudyAttentionResponse>, ProblemHttpResult>> GetStudyAttentionAsync(
        string studyRef,
        [FromQuery] int? limit,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var take = limit ?? 20;
        if (take is < 1 or > KernelProposalEndpoints.MaxLimit)
        {
            return KernelProblems.BadRequest("Invalid limit", $"Limit must be between 1 and {KernelProposalEndpoints.MaxLimit}.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await KernelStudyLookup.ResolveVisibleStudyAsync(
            tenant,
            studyRef,
            context,
            visibility,
            actor,
            static (studyNode, mask) => $"You may not access study {studyNode} ({mask}).",
            allowVersionedStudyRef: true,
            cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        IReadOnlyList<KernelProposalResponse> proposalResponses = [];
        if (context.RequestServices.GetService<IProposalReader>() is { } proposalReader)
        {
            var proposals = await proposalReader
                .ListForStudyAsync(tenant, resolved.Study.StudyId, ProposalStatus.Open, clock.UtcNow, take, cancellationToken)
                .ConfigureAwait(false);
            if (proposals.Count > 0)
            {
                var masks = await KernelProposalEndpoints
                    .MaskProposalsAsync(visibility, actor, tenant, proposals, cancellationToken)
                    .ConfigureAwait(false);
                proposalResponses = [.. proposals.Select(p => KernelProposalResponse.From(p, masks))];
            }
        }

        IReadOnlyList<KernelValidationFindingResponse> findingResponses = [];
        if (context.RequestServices.GetService<IValidationFindings>() is { } findings)
        {
            var rawPage = await findings.ListAsync(
                tenant,
                new FindingQuery(resolved.Study.StudyId)
                {
                    Statuses = [FindingStatus.Open, FindingStatus.Investigating],
                    Take = FindingQuery.MaxTake,
                },
                cancellationToken).ConfigureAwait(false);
            if (rawPage.Findings.Count > 0)
            {
                var visibleFindings = await KernelValidationFindingVisibility.FilterVisibleFindingsAsync(
                    tenant,
                    resolved.Study.StudyId,
                    rawPage.Findings,
                    context.RequestServices,
                    visibility,
                    actor,
                    cancellationToken).ConfigureAwait(false);
                findingResponses = [.. visibleFindings.Take(take).Select(KernelValidationFindingResponse.From)];
            }
        }

        IReadOnlyList<KernelStudySdvAlertResponse> sdvAlertResponses = [];
        if (context.RequestServices.GetService<IStudySdvAlertReader>() is { } sdvAlerts)
        {
            var rawAlerts = await sdvAlerts
                .ListForStudyAsync(tenant, resolved.Study.StudyId, resolved.Study.StudyCode, cancellationToken)
                .ConfigureAwait(false);
            if (rawAlerts.Count > 0)
            {
                sdvAlertResponses = [.. rawAlerts.Select(KernelStudySdvAlertResponse.From)];
            }
        }

        return TypedResults.Ok(new KernelStudyAttentionResponse(
            resolved.Study.PinnedRef.ToString(),
            proposalResponses,
            findingResponses,
            sdvAlertResponses));
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> GetFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(findingRef, out var node) || node.Kind != NodeKinds.ValidationFinding)
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var view = await findings.FindAsync(tenant, ValidationFindingId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return KernelProblems.NotFound(node);
        }

        var studyNode = NodeRef.For(view.Study);
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(studyNode) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You may not see findings for study {studyNode} ({studyMask}).");
        }

        var visibleFindings = await KernelValidationFindingVisibility.FilterVisibleFindingsAsync(
            tenant,
            view.Study,
            [view],
            context.RequestServices,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);
        if (visibleFindings.Count == 0)
        {
            return KernelProblems.NotVisible($"You may not see validation finding {node}.");
        }

        return TypedResults.Ok(new KernelValidationFindingDetailResponse(
            KernelValidationFindingResponse.From(view),
            view.Explain()));
    }
}
