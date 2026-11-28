using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>DTO request for evaluate-only bitemporal simulation across protocols, mapping specifications, and rule packs.</summary>
public record SimulateProtocolRequest(
    string? BaseVersionRef,
    string? StoredVersionRef,
    string? CandidateContent,
    DateTimeOffset? ValidAt = null,
    DateTimeOffset? KnownAt = null,
    string? Kind = null,
    string? StudyRef = null,
    string? LibraryItemRef = null,
    string? BaseLibraryItemRef = null,
    string? StandardVersionRef = null);

/// <summary>DTO request for saving an unmasked <see cref="KernelSimulationResponse"/> as an attestable <c>SimulationReport</c> artifact (K-SIM-5).</summary>
public sealed record SaveSimulationReportRequest(
    string? Title,
    KernelSimulationResponse? Result,
    Guid? ArtifactId = null,
    Guid? ArtifactVersionId = null);

/// <summary>DTO request for real-time schedule matrix gap checking.</summary>
public record MatrixGapCheckRequest(string ProtocolContent);

/// <summary>
/// Minimal API endpoints for bitemporal impact simulation, simulation report persistence, and real-time matrix gap analysis.
/// </summary>
public static class KernelSimulationEndpoints
{
    /// <summary>The semantic action an artifact-version review step-up token must carry (21 CFR Part 11, K-SIM-5).</summary>
    public const string ReviewArtifactVersionSemanticAction = "kernel.artifact_version.review";

    /// <summary>The canonical concrete action path for signing a Reviewed attestation over an artifact version reference.</summary>
    public static string ReviewArtifactVersionAction(string artifactVersionRef)
    {
        var canonical = KernelProtocolGovernanceEndpoints.TryParseArtifactVersion(artifactVersionRef, out var versionId)
            ? NodeRef.For(versionId).ToString()
            : artifactVersionRef;
        return $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/artifact-versions/{canonical}/review";
    }

    /// <summary>Maps the simulation, simulation report, artifact review sign-off, and matrix gap check endpoints.</summary>
    public static RouteGroupBuilder MapSimulationEndpoints(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>();

        group.MapPost("/simulation/simulate", SimulateAsync)
            .WithName("KernelSimulateProtocol")
            .WithSummary("Evaluates a candidate protocol version, mapping specification, or rule pack in memory at a bitemporal (valid_at, known_at) instant without database changes.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
                KernelErrors.ActorUnresolved,
                KernelErrors.AuthorizationDenied,
                SimulationErrors.VersionUnknown,
                SimulationErrors.VersionStale,
                SimulationErrors.StudyUnknown,
                SimulationErrors.LibraryItemUnknown,
                SimulationErrors.BaseNotFrozen,
                SimulationErrors.ContentInvalid,
                SimulationErrors.MappingSpecificationInvalid,
                SimulationErrors.RulePackInvalid,
                SimulationErrors.ElementIdDuplicated);

        group.MapPost("/studies/{studyRef}/simulation-reports", SaveSimulationReportAsync)
            .WithName("KernelSaveSimulationReport")
            .WithSummary("Registers an unmasked SimulationResult as an immutable SimulationReport artifact version for Part 11 review attestation (K-SIM-5).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<SaveSimulationReport>();

        group.MapPost("/artifact-versions/{artifactVersionRef}/review", ReviewArtifactVersionAsync)
            .WithName("KernelReviewArtifactVersion")
            .WithSummary("Records a 21 CFR Part 11 Reviewed attestation bound to an immutable artifact version's content hash (attestation.sign, K-SIM-5).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                ProtocolErrors.AttestationRejected,
            ])
            .RequireElectronicSignature(
                ReviewArtifactVersionSemanticAction,
                ctx => ReviewArtifactVersionAction(ctx.Request.RouteValues["artifactVersionRef"]?.ToString() ?? string.Empty));

        group.MapPost("/protocols/matrix-gap-check", CheckMatrixGapsAsync)
            .WithName("KernelMatrixGapCheck")
            .WithSummary("Performs immediate real-time matrix gap check on a candidate protocol design graph.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return kernel;
    }

    internal static Task<Results<Ok<KernelSimulationResponse>, ProblemHttpResult>> SimulateAsync(
        SimulateProtocolRequest request, HttpContext context, ISimulationEngine simulationEngine,
        INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelSimulationEndpointHandlers.SimulateAsync(request, context, simulationEngine, visibility, actor, cancellationToken);

    internal static Task<Results<Created<KernelArtifactResponse>, ProblemHttpResult>> SaveSimulationReportAsync(
        string studyRef, SaveSimulationReportRequest? request, HttpContext context, IKernelCommandSender sender,
        IKernelChangeReason changeReason, IClock clock, CancellationToken cancellationToken) =>
        KernelSimulationEndpointHandlers.SaveSimulationReportAsync(
            studyRef, request, context, sender, changeReason, clock, cancellationToken);

    internal static Task<Results<Ok<KernelArtifactReviewAttestationResponse>, ProblemHttpResult>> ReviewArtifactVersionAsync(
        string artifactVersionRef, KernelReviewArtifactVersionRequest? request, HttpContext context,
        IKernelCommandSender sender, IKernelChangeReason changeReason, IClock clock, CancellationToken cancellationToken) =>
        KernelSimulationEndpointHandlers.ReviewArtifactVersionAsync(
            artifactVersionRef, request, context, sender, changeReason, clock, cancellationToken);

    internal static Results<Ok<IReadOnlyList<KernelMatrixGapFindingResponse>>, ProblemHttpResult> CheckMatrixGapsAsync(
        MatrixGapCheckRequest request) =>
        KernelSimulationEndpointHandlers.CheckMatrixGapsAsync(request);
}
