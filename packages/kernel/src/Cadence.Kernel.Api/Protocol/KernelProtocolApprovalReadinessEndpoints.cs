using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

internal static class KernelProtocolApprovalReadinessEndpoints
{
    internal static async Task<Results<Ok<KernelProtocolApprovalReadinessResponse>, ProblemHttpResult>> GetProtocolApprovalReadinessAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader versions,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (NodeRef.TryParse(versionRef, out var parsedRef) && parsedRef is not null && parsedRef.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(parsedRef, NodeKinds.ProtocolVersion);
        }

        if (!KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        var versionNode = NodeRef.For(versionId);
        if (await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(versionNode);
        }

        var visibilityMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
        if (visibilityMasks.For(versionNode) is { } hiddenMask)
        {
            return KernelProblems.NotVisible($"You can't see this protocol version ({hiddenMask}), so its approval readiness is not shown.");
        }

        ProtocolApprovalReadinessEvaluation? evaluation = null;
        if (context.RequestServices.GetService<IKernelUnitOfWorkFactory>() is { } unitOfWorkFactory
            && context.RequestServices.GetService<IAttestationVerifier>() is { } verifier)
        {
            var evalActor = actor.Actor is { } resolvedActor && !string.IsNullOrWhiteSpace(resolvedActor.Id)
                ? resolvedActor
                : Actor.Person("anonymous");
            evaluation = await ProtocolApprovalSimulationGate.EvaluateAsync(
                tenant,
                unitOfWorkFactory,
                verifier,
                evalActor,
                clock.UtcNow,
                versionId,
                explicitReviewAttestationId: null,
                cancellationToken).ConfigureAwait(false);
        }

        if (evaluation is null)
        {
            var fallbackReady = version.Status == ProtocolVersionStatus.InReview && version.ParentVersionId is null;
            KernelError? fallbackError = null;
            if (!fallbackReady)
            {
                fallbackError = version.Status == ProtocolVersionStatus.InReview
                    ? ProtocolErrors.SimulationReviewRequired
                    : ProtocolErrors.InvalidTransition;
            }

            var fallbackExplanation = new Explanation(
                versionNode,
                fallbackReady
                    ? $"Initial protocol version {version.Number} is frozen in review and ready for approval."
                    : $"Protocol version {version.Number} is not ready for approval.",
                "kernel.protocol.approval_readiness",
                [new ExplanationStep($"Protocol version {version.Number} has status {version.Status}.", [versionNode])],
                [
                    version.Status == ProtocolVersionStatus.InReview
                        ? EvaluatedCondition.Satisfied("protocol.version.in_review", $"Protocol version {version.Number} is in review.", versionNode)
                        : EvaluatedCondition.Unsatisfied("protocol.version.in_review", $"Protocol version {version.Number} is {version.Status}.", versionNode),
                ],
                []);
            evaluation = new ProtocolApprovalReadinessEvaluation(
                version.Id,
                version.Status,
                version.ParentVersionId is not null,
                version.ParentVersionId,
                Ready: fallbackReady,
                SimulationReportSaved: false,
                ReviewSigned: false,
                SimulationReportArtifactVersionId: null,
                ReviewAttestationId: null,
                fallbackExplanation,
                fallbackError);
        }

        var extraNodes = new List<NodeRef>(3);
        if (evaluation.ParentVersionId is { } pid)
        {
            extraNodes.Add(NodeRef.For(pid));
        }

        if (evaluation.SimulationReportArtifactVersionId is { } simVersionId)
        {
            extraNodes.Add(NodeRef.For(simVersionId));
        }

        if (evaluation.ReviewAttestationId is { } reviewAttestId)
        {
            extraNodes.Add(NodeRef.For(reviewAttestId));
        }

        var allRefsToMask = ExplanationMasking.CollectNodes(evaluation.Explanation).Concat(extraNodes).Distinct().ToList();
        var refMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, allRefsToMask, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new KernelProtocolApprovalReadinessResponse(
            NodeRef.For(evaluation.ProtocolVersionId).ToString(),
            evaluation.Status.ToString(),
            evaluation.IsAmendment,
            ExplanationMasking.FormatOptional(evaluation.ParentVersionId is { } parentId ? NodeRef.For(parentId) : null, refMasks),
            evaluation.Ready,
            evaluation.SimulationReportSaved,
            evaluation.ReviewSigned,
            ExplanationMasking.FormatOptional(evaluation.SimulationReportArtifactVersionId is { } avId ? NodeRef.For(avId) : null, refMasks),
            ExplanationMasking.FormatOptional(evaluation.ReviewAttestationId is { } attId ? NodeRef.For(attId) : null, refMasks),
            evaluation.BlockingError?.Code,
            KernelExplanationResponse.From(evaluation.Explanation, refMasks)));
    }
}
