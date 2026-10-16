using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>DTO request for evaluate-only protocol simulation.</summary>
public record SimulateProtocolRequest(
    string? BaseVersionRef,
    string? StoredVersionRef,
    string? CandidateContent,
    DateTimeOffset? ValidAt = null,
    DateTimeOffset? KnownAt = null);

/// <summary>DTO request for generative protocol proposal drafting.</summary>
public record GenerateProtocolProposalRequest(string TrialBrief);

/// <summary>DTO request for real-time schedule matrix gap checking.</summary>
public record MatrixGapCheckRequest(string ProtocolContent);

/// <summary>
/// Minimal API endpoints for in-memory protocol simulation, generative proposal authoring, and real-time matrix gap analysis.
/// </summary>
public static class KernelSimulationEndpoints
{
    /// <summary>Maps simulation, generative proposal authoring, and matrix gap check endpoints.</summary>
    public static RouteGroupBuilder MapSimulationEndpoints(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>();

        group.MapPost("/simulation/simulate", SimulateAsync)
            .WithName("KernelSimulateProtocol")
            .WithSummary("Evaluates a candidate USDM protocol version in memory without database changes.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/protocols/generate-proposal", GenerateProposalAsync)
            .WithName("KernelGenerateProtocolProposal")
            .WithSummary("Generates a complete USDM protocol proposal from a trial brief with reasoning lineage.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/protocols/matrix-gap-check", CheckMatrixGapsAsync)
            .WithName("KernelMatrixGapCheck")
            .WithSummary("Performs immediate real-time matrix gap check on a candidate protocol design graph.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return kernel;
    }

    internal static async Task<Results<Ok<SimulationResult>, ProblemHttpResult>> SimulateAsync(
        SimulateProtocolRequest request,
        HttpContext context,
        ISimulationEngine simulationEngine,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Request body is required.");
        }

        var tenant = KernelRequestTenant.Get(context);

        SimulationCandidate candidate;
        NodeRef versionNode;
        if (!string.IsNullOrWhiteSpace(request.StoredVersionRef))
        {
            if (!NodeRef.TryParse(request.StoredVersionRef, out var storedNode) || storedNode.Kind != NodeKinds.ProtocolVersion)
            {
                return KernelProblems.InvalidNodeRef(request.StoredVersionRef);
            }
            candidate = SimulationCandidate.StoredProtocolVersion(ProtocolVersionId.FromGuid(storedNode.Id));
            versionNode = storedNode;
        }
        else if (!string.IsNullOrWhiteSpace(request.BaseVersionRef) && request.CandidateContent is not null)
        {
            if (!NodeRef.TryParse(request.BaseVersionRef, out var baseNode) || baseNode.Kind != NodeKinds.ProtocolVersion)
            {
                return KernelProblems.InvalidNodeRef(request.BaseVersionRef);
            }
            candidate = SimulationCandidate.UnsavedProtocolVersion(ProtocolVersionId.FromGuid(baseNode.Id), request.CandidateContent);
            versionNode = baseNode;
        }
        else
        {
            return KernelProblems.BadRequest("Missing candidate specification", "Must specify StoredVersionRef or (BaseVersionRef and CandidateContent).");
        }

        // The simulation reads the stored version, so the caller must be able to see it (ADR-2457, ADR-2516).
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(versionNode) is { } mask)
        {
            return KernelProblems.NotVisible($"You can't see this protocol version ({mask}), so it can't be simulated.");
        }

        var simRequest = new SimulationRequest(candidate, request.ValidAt, request.KnownAt);
        var result = await simulationEngine.SimulateAsync(tenant, simRequest, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return KernelProblems.BadRequest(result.Error!.Code, result.Error.Message);
        }

        return TypedResults.Ok(result.Value!);
    }

    internal static Ok<GenerativeProtocolProposal> GenerateProposalAsync(GenerateProtocolProposalRequest request)
    {
        var proposal = GenerativeProtocolAuthor.GenerateProposal(request?.TrialBrief ?? string.Empty);
        return TypedResults.Ok(proposal);
    }

    internal static Results<Ok<IReadOnlyList<MatrixGapFinding>>, ProblemHttpResult> CheckMatrixGapsAsync(MatrixGapCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.ProtocolContent))
        {
            return KernelProblems.BadRequest("Missing content", "ProtocolContent is required.");
        }

        var read = ProtocolDesignDocument.Read(request.ProtocolContent);
        if (!read.IsValid)
        {
            return KernelProblems.BadRequest("Invalid protocol design", string.Join("; ", read.Problems));
        }

        var findings = MatrixGapAnalyzer.Analyze(read.Design!);
        return TypedResults.Ok(findings);
    }
}
