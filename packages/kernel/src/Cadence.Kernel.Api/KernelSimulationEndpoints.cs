using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

    internal static async Task<Results<Ok<KernelSimulationResponse>, ProblemHttpResult>> SimulateAsync(
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

        var kind = CandidateKind.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(request.Kind)
            && (!Enum.TryParse(request.Kind.Trim(), ignoreCase: true, out kind) || !Enum.IsDefined(kind)))
        {
            return KernelProblems.BadRequest("Invalid candidate kind", $"'{request.Kind}' is not a supported CandidateKind (ProtocolVersion, MappingSpecification, RulePack).");
        }

        var tenant = KernelRequestTenant.Get(context);
        SimulationCandidate candidate;
        var visibleRefs = new List<NodeRef>(3);

        switch (kind)
        {
            case CandidateKind.ProtocolVersion:
                {
                    if (!string.IsNullOrWhiteSpace(request.StoredVersionRef))
                    {
                        if (!NodeRef.TryParse(request.StoredVersionRef, out var storedNode) || storedNode.Kind != NodeKinds.ProtocolVersion)
                        {
                            return KernelProblems.InvalidNodeRef(request.StoredVersionRef);
                        }

                        candidate = SimulationCandidate.StoredProtocolVersion(ProtocolVersionId.FromGuid(storedNode.Id), storedNode.Version);
                        visibleRefs.Add(storedNode);
                    }
                    else if (!string.IsNullOrWhiteSpace(request.BaseVersionRef) && request.CandidateContent is not null)
                    {
                        if (!NodeRef.TryParse(request.BaseVersionRef, out var baseNode) || baseNode.Kind != NodeKinds.ProtocolVersion)
                        {
                            return KernelProblems.InvalidNodeRef(request.BaseVersionRef);
                        }

                        candidate = SimulationCandidate.UnsavedProtocolVersion(
                            ProtocolVersionId.FromGuid(baseNode.Id),
                            request.CandidateContent,
                            baseNode.Version);
                        visibleRefs.Add(baseNode);
                    }
                    else
                    {
                        return KernelProblems.BadRequest("Missing candidate specification", "Must specify StoredVersionRef or (BaseVersionRef and CandidateContent).");
                    }

                    break;
                }

            case CandidateKind.MappingSpecification:
                {
                    var (studyId, studyProblem) = await KernelProtocolGovernanceEndpoints
                        .ResolveStudyIdAsync(tenant, request.StudyRef ?? string.Empty, context, cancellationToken)
                        .ConfigureAwait(false);
                    if (studyProblem is not null)
                    {
                        return studyProblem;
                    }

                    visibleRefs.Add(NodeRef.For(studyId));
                    if (!TryParseOptionalLibraryItem(request.BaseLibraryItemRef, visibleRefs, out var baseItemId, out var baseProblem))
                    {
                        return baseProblem!;
                    }

                    if (!TryParseOptionalStandardVersion(request.StandardVersionRef, out var standardVersionId, out var stdProblem))
                    {
                        return stdProblem!;
                    }

                    var storedRefText = !string.IsNullOrWhiteSpace(request.LibraryItemRef) ? request.LibraryItemRef : request.StoredVersionRef;
                    if (!string.IsNullOrWhiteSpace(storedRefText))
                    {
                        if (!TryParseLibraryItem(storedRefText, out var itemId))
                        {
                            return KernelProblems.InvalidNodeRef(storedRefText);
                        }

                        visibleRefs.Add(NodeRef.For(itemId));
                        candidate = SimulationCandidate.StoredMappingSpecification(studyId, itemId, baseItemId);
                    }
                    else if (request.CandidateContent is not null)
                    {
                        candidate = SimulationCandidate.UnsavedMappingSpecification(studyId, request.CandidateContent, baseItemId, standardVersionId);
                    }
                    else
                    {
                        return KernelProblems.BadRequest("Missing candidate specification", "Must specify LibraryItemRef or CandidateContent.");
                    }

                    break;
                }

            case CandidateKind.RulePack:
                {
                    var (studyId, studyProblem) = await KernelProtocolGovernanceEndpoints
                        .ResolveStudyIdAsync(tenant, request.StudyRef ?? string.Empty, context, cancellationToken)
                        .ConfigureAwait(false);
                    if (studyProblem is not null)
                    {
                        return studyProblem;
                    }

                    visibleRefs.Add(NodeRef.For(studyId));
                    if (!TryParseOptionalLibraryItem(request.BaseLibraryItemRef, visibleRefs, out var baseItemId, out var baseProblem))
                    {
                        return baseProblem!;
                    }

                    var storedRefText = !string.IsNullOrWhiteSpace(request.LibraryItemRef) ? request.LibraryItemRef : request.StoredVersionRef;
                    if (!string.IsNullOrWhiteSpace(storedRefText))
                    {
                        if (!TryParseLibraryItem(storedRefText, out var itemId))
                        {
                            return KernelProblems.InvalidNodeRef(storedRefText);
                        }

                        visibleRefs.Add(NodeRef.For(itemId));
                        candidate = SimulationCandidate.StoredRulePack(studyId, itemId, baseItemId);
                    }
                    else if (request.CandidateContent is not null)
                    {
                        candidate = SimulationCandidate.UnsavedRulePack(studyId, request.CandidateContent, baseItemId);
                    }
                    else
                    {
                        return KernelProblems.BadRequest("Missing candidate specification", "Must specify LibraryItemRef or CandidateContent.");
                    }

                    break;
                }

            default:
                return KernelProblems.BadRequest("Invalid candidate kind", $"Unsupported CandidateKind '{kind}'.");
        }

        // The simulation reads the referenced target/scope, so the caller must be able to see it (ADR-2457, ADR-2516).
        var refsToCheck = visibleRefs
            .SelectMany(r => new[] { r, r.Unversioned() })
            .Distinct()
            .ToArray();
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, refsToCheck, cancellationToken).ConfigureAwait(false);
        foreach (var visibleRef in visibleRefs)
        {
            if ((masks.For(visibleRef) ?? masks.For(visibleRef.Unversioned())) is { } mask)
            {
                return KernelProblems.NotVisible($"You can't see this {visibleRef.Kind} ({mask}), so it can't be simulated.");
            }
        }

        var simRequest = new SimulationRequest(candidate, request.ValidAt, request.KnownAt);
        var result = await simulationEngine.SimulateAsync(tenant, simRequest, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return TypedResults.Ok(KernelSimulationResponse.From(result.Value!));
    }

    internal static async Task<Results<Created<KernelArtifactResponse>, ProblemHttpResult>> SaveSimulationReportAsync(
        string studyRef,
        SaveSimulationReportRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request?.Result is null)
        {
            return KernelProblems.BadRequest("Invalid simulation report", "Supply a title and an unmasked SimulationResult.");
        }

        if (!request.Result.TryToDomain(out var domainResult, out var parseError) || domainResult is null)
        {
            return KernelErrorEndpoints.Problem(
                EvidenceErrors.SimulationReportInvalid.WithMessage($"{EvidenceErrors.SimulationReportInvalid.Message} {parseError}"));
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceEndpoints.ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolvedStudy = await KernelProtocolGovernanceEndpoints.ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken).ConfigureAwait(false);
        if (resolvedStudy.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var artifactId = request.ArtifactId is { } givenArtifact && givenArtifact != Guid.Empty
            ? ArtifactId.FromGuid(givenArtifact)
            : ArtifactId.FromGuid(KernelProtocolGovernanceEndpoints.DeriveGuid(tenant, "save-simulation-report", idempotencyKey));
        var versionId = request.ArtifactVersionId is { } givenVersion && givenVersion != Guid.Empty
            ? ArtifactVersionId.FromGuid(givenVersion)
            : ArtifactVersionId.FromGuid(KernelProtocolGovernanceEndpoints.DeriveGuid(tenant, "save-simulation-report-version", idempotencyKey));

        var sent = await sender.SendAsync(
            new SaveSimulationReport(
                tenant,
                resolvedStudy.StudyId,
                artifactId,
                versionId,
                request.Title ?? string.Empty,
                domainResult,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!sent.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(sent.Error!);
        }

        var content = SimulationArtifactBinding.ToArtifactContent(domainResult)!;
        var response = KernelArtifactResponse.From(sent.Value!, content.SizeBytes, content.MediaType, content.StorageRef, clock.UtcNow);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(sent.Value!.ArtifactId)}",
            response);
    }

    internal static async Task<Results<Ok<KernelArtifactReviewAttestationResponse>, ProblemHttpResult>> ReviewArtifactVersionAsync(
        string artifactVersionRef,
        KernelReviewArtifactVersionRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this artifact version review.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!KernelProtocolGovernanceEndpoints.TryParseArtifactVersion(artifactVersionRef, out var artifactVersionId))
        {
            return KernelProblems.BadRequest(
                "Invalid NodeRef",
                $"'{artifactVersionRef}' is not an artifact_version NodeRef (artifact_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceEndpoints.ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        if (request?.Meaning is { Length: > 0 } rawMeaning
            && (!Enum.TryParse<AttestationMeaning>(rawMeaning, ignoreCase: true, out var parsedMeaning)
                || parsedMeaning != AttestationMeaning.Reviewed))
        {
            return KernelErrorEndpoints.Problem(
                ProtocolErrors.AttestationRejected.WithMessage("Artifact review requires an attestation with meaning Reviewed."));
        }

        var tenant = KernelRequestTenant.Get(context);
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is { } entitlementReader)
        {
            var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (entitlements.IsReadOnly)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
            }

            if (!entitlements.ProductFamilies.Contains(ProductFamily.Blueprint))
            {
                return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
            }
        }

        Actor actor;
        if (context.RequestServices.GetService<ICurrentActor>()?.Actor is { } resolvedActor
            && !string.IsNullOrWhiteSpace(resolvedActor.Id))
        {
            actor = resolvedActor;
        }
        else if (!string.IsNullOrWhiteSpace(sigToken.Subject))
        {
            actor = Actor.Person(sigToken.Subject);
        }
        else
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ActorUnresolved);
        }

        var unitOfWorkFactory = context.RequestServices.GetService<IKernelUnitOfWorkFactory>();
        if (unitOfWorkFactory is null)
        {
            return KernelErrorEndpoints.Problem(TrustErrors.TargetUnknown);
        }

        var targetInfo = await ArtifactVersionTargetLookup.FindAsync(
            tenant,
            unitOfWorkFactory,
            actor,
            clock.UtcNow,
            artifactVersionId,
            cancellationToken).ConfigureAwait(false);
        if (targetInfo is null)
        {
            return KernelErrorEndpoints.Problem(TrustErrors.TargetUnknown);
        }

        if (!string.IsNullOrWhiteSpace(request?.ExpectedContentHash))
        {
            var normalizedExpected = KernelProtocolGovernanceEndpoints.NormalizeSha256(request.ExpectedContentHash);
            if (!string.Equals(normalizedExpected, targetInfo.ContentHash, StringComparison.Ordinal))
            {
                return KernelErrorEndpoints.Problem(TrustErrors.TargetContentChanged);
            }
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(KernelProtocolGovernanceEndpoints.DeriveGuid(tenant, "review-artifact-version-attest", idempotencyKey));

        var attestResult = await sender.SendAsync(
            new Attest(
                tenant,
                attestationId,
                AttestationMeaning.Reviewed,
                new AttestationTarget(NodeKinds.ArtifactVersion, artifactVersionId.Value, targetInfo.ContentHash),
                new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                KernelLockEndpoints.StepUpMethod,
                idempotencyKey,
                reason,
                Scope: targetInfo.StudyId is { } sid ? NodeRef.For(sid) : null),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
        {
            return KernelErrorEndpoints.Problem(attestResult.Error!);
        }

        return TypedResults.Ok(new KernelArtifactReviewAttestationResponse(
            NodeRef.For(attestationId).ToString(),
            attestationId.Value,
            NodeRef.For(targetInfo.ArtifactId).ToString(),
            NodeRef.For(artifactVersionId).ToString(),
            targetInfo.StudyId is { } studyId ? NodeRef.For(studyId).ToString() : null,
            AttestationMeaning.Reviewed.ToString(),
            targetInfo.ContentHash,
            clock.UtcNow));
    }

    internal static Results<Ok<IReadOnlyList<KernelMatrixGapFindingResponse>>, ProblemHttpResult> CheckMatrixGapsAsync(MatrixGapCheckRequest request)
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
        return TypedResults.Ok<IReadOnlyList<KernelMatrixGapFindingResponse>>(
            [.. findings.Select(KernelMatrixGapFindingResponse.From)]);
    }

    private static bool TryParseOptionalLibraryItem(
        string? raw,
        List<NodeRef> visibleRefs,
        out LibraryItemId? itemId,
        out ProblemHttpResult? problem)
    {
        itemId = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!TryParseLibraryItem(raw, out var parsed))
        {
            problem = KernelProblems.InvalidNodeRef(raw);
            return false;
        }

        itemId = parsed;
        visibleRefs.Add(NodeRef.For(parsed));
        return true;
    }

    private static bool TryParseLibraryItem(string raw, out LibraryItemId id)
    {
        var trimmed = raw.Trim();
        if (NodeRef.TryParse(trimmed, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(trimmed, out var guid) && guid != Guid.Empty)
        {
            id = LibraryItemId.FromGuid(guid);
            return true;
        }

        id = LibraryItemId.Empty;
        return false;
    }

    private static bool TryParseOptionalStandardVersion(
        string? raw,
        out StandardVersionId? standardVersionId,
        out ProblemHttpResult? problem)
    {
        standardVersionId = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var trimmed = raw.Trim();
        if (NodeRef.TryParse(trimmed, out var parsed) && parsed is not null && parsed.TryGetId<StandardVersionId>(out var fromRef))
        {
            standardVersionId = fromRef;
            return true;
        }

        if (Guid.TryParse(trimmed, out var guid) && guid != Guid.Empty)
        {
            standardVersionId = StandardVersionId.FromGuid(guid);
            return true;
        }

        problem = KernelProblems.InvalidNodeRef(raw);
        return false;
    }
}
