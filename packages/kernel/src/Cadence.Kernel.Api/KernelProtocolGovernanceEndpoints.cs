using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

/// <summary>
/// Protocol review, Part 11 signed approval, evidence artifact registration, bitemporal effectivity governance, and
/// kernel study-site activation endpoints (spec §6.2, §7.1, §8.3, §9.5, K-TIME-4, K-TIME-5). Every mutating route
/// requires <c>Idempotency-Key</c> and <c>X-Change-Reason</c> headers and executes through <see cref="IKernelCommandSender"/>.
/// </summary>
public static class KernelProtocolGovernanceEndpoints
{
    /// <summary>The semantic action a protocol-version approval step-up token must carry (21 CFR Part 11).</summary>
    public const string ApproveProtocolVersionSemanticAction = "kernel.protocol_version.approve";

    private static readonly Capability ApproveProtocolVersionCapability = new("protocol.approve", VerbClass.Approve);

    /// <summary>The canonical concrete action path for approving a protocol version reference.</summary>
    public static string ApproveProtocolVersionAction(string versionRef)
    {
        var canonical = TryParseProtocolVersion(versionRef, out var versionId)
            ? NodeRef.For(versionId).ToString()
            : versionRef;
        return $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{canonical}/approve";
    }

    /// <summary>Maps protocol governance, evidence artifact, effectivity, and study-site activation routes.</summary>
    public static RouteGroupBuilder MapKernelProtocolGovernance(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel protocol governance");

        group.MapPost("/protocol-versions/{versionRef}/submit-for-review", SubmitProtocolVersionForReviewAsync)
            .WithName("KernelSubmitProtocolVersionForReview")
            .WithSummary("Freezes a draft protocol version's content, computes its content hash, and moves it to InReview (protocol.submit_for_review).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                KernelErrors.NotFound,
                ProtocolErrors.InvalidTransition,
                ProtocolErrors.ContentRequired,
                ProtocolErrors.ContentInvalid,
            ]);

        group.MapPost("/protocol-versions/{versionRef}/return-to-draft", ReturnProtocolVersionToDraftAsync)
            .WithName("KernelReturnProtocolVersionToDraft")
            .WithSummary("Returns an InReview protocol version to Draft, clearing its content hash so it can be revised (protocol.return_to_draft).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                KernelErrors.NotFound,
                ProtocolErrors.InvalidTransition,
            ]);

        group.MapPost("/protocol-versions/{versionRef}/approve", ApproveProtocolVersionAsync)
            .WithName("KernelApproveProtocolVersion")
            .WithSummary("Records a 21 CFR Part 11 Approved attestation bound to the frozen protocol content hash and approves the protocol version (protocol.approve).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                KernelErrors.AlreadyExists,
                KernelErrors.NotFound,
                ProtocolErrors.InvalidTransition,
                ProtocolErrors.AttestationRejected,
            ])
            .RequireElectronicSignature(
                ApproveProtocolVersionSemanticAction,
                ctx => ApproveProtocolVersionAction(ctx.Request.RouteValues["versionRef"]?.ToString() ?? string.Empty));

        group.MapGet("/studies/{studyRef}/artifacts", ListStudyArtifactsAsync)
            .WithName("KernelListStudyArtifacts")
            .WithSummary("Lists evidence artifacts registered for a study, newest first.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/studies/{studyRef}/artifacts", RegisterStudyArtifactAsync)
            .WithName("KernelRegisterStudyArtifact")
            .WithSummary("Registers an immutable study evidence artifact version such as an ethics approval, regulatory approval, or CTA acknowledgement (artifact.register).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                EvidenceErrors.ContentInvalid,
                EvidenceErrors.TitleInvalid,
                EvidenceErrors.KindInvalid,
                EvidenceErrors.StudyUnknown,
            ]);

        group.MapGet("/studies/{studyRef}/effectivities", ListStudyEffectivitiesAsync)
            .WithName("KernelListStudyEffectivities")
            .WithSummary("Lists bitemporal protocol-version effectivities declared for a study.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/protocol-versions/{versionRef}/effectivities", DeclareEffectivityAsync)
            .WithName("KernelDeclareEffectivity")
            .WithSummary("Declares a Pending protocol-version effectivity for the whole study, a country, or one study site (effectivity.declare).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                EffectivityErrors.ScopeInvalid,
                EffectivityErrors.ProtocolVersionUnknown,
                EffectivityErrors.ProtocolVersionWithdrawn,
                EffectivityErrors.StudySiteNotInStudy,
                EffectivityErrors.AlreadyDeclared,
                EffectivityErrors.TransitionTimeInvalid,
            ]);

        group.MapPost("/effectivities/{effectivityRef}/status", ChangeEffectivityStatusAsync)
            .WithName("KernelChangeEffectivityStatus")
            .WithSummary("Transitions an effectivity's status in valid time with supporting approval artifact evidence (effectivity.change_status, K-TIME-5).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                KernelErrors.NotFound,
                EffectivityErrors.ProtocolVersionNotApproved,
                EffectivityErrors.InvalidTransition,
                EffectivityErrors.TransitionTimeInvalid,
                EffectivityErrors.EvidenceRequired,
                EffectivityErrors.EvidenceInvalid,
            ]);

        group.MapGet("/studies/{studyRef}/sites", ListStudySitesAsync)
            .WithName("KernelListStudySites")
            .WithSummary("Lists kernel study sites linked to a study together with their activation status and currently effective protocol versions.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/studies/{studyRef}/sites", LinkStudySiteAsync)
            .WithName("KernelLinkStudySite")
            .WithSummary("Registers a site when needed and links it to the study in Selected status (site.register, study_site.link).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                StudyErrors.CodeRequired,
                StudyErrors.NameRequired,
                StudyErrors.CountryInvalid,
                StudyErrors.InstitutionUnknown,
                StudyErrors.SiteCodeTaken,
                StudyErrors.StudyUnknown,
                StudyErrors.SiteUnknown,
                StudyErrors.AlreadyLinked,
            ]);

        group.MapPost("/study-sites/{studySiteRef}/activate", ActivateStudySiteAsync)
            .WithName("KernelActivateStudySite")
            .WithSummary("Activates a kernel study site once at least one protocol version is effective there (study_site.activate, K-TIME-4).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                EffectivityErrors.StudySiteUnknown,
                EffectivityErrors.StudySiteAlreadyActive,
                EffectivityErrors.NoEffectiveVersion,
            ]);

        return kernel;
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> SubmitProtocolVersionForReviewAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        if (ReadMutationHeaders(context, changeReason) is var (_, _, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var (idempotencyKey, reason, _) = ReadMutationHeaders(context, changeReason);
        var tenant = KernelRequestTenant.Get(context);

        var result = await sender.SendAsync(
            new SubmitProtocolVersionForReview(tenant, versionId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToProtocolVersionResponse(updated, result.Value!));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ReturnProtocolVersionToDraftAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new ReturnProtocolVersionToDraft(tenant, versionId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToProtocolVersionResponse(updated, result.Value!));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ApproveProtocolVersionAsync(
        string versionRef,
        KernelApproveProtocolVersionRequest? request,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this protocol version approval.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        if (request?.Meaning is { Length: > 0 } rawMeaning
            && (!Enum.TryParse<AttestationMeaning>(rawMeaning, ignoreCase: true, out var parsedMeaning)
                || parsedMeaning != AttestationMeaning.Approved))
        {
            return KernelErrorEndpoints.Problem(
                ProtocolErrors.AttestationRejected.WithMessage("Protocol approval requires an attestation with meaning Approved."));
        }

        var tenant = KernelRequestTenant.Get(context);
        var existing = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.NotFound);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "approve-protocol-version-attest", idempotencyKey));

        var canApprove = await probe.CanIAsync(tenant, ApproveProtocolVersionCapability, NodeRef.For(versionId), cancellationToken).ConfigureAwait(false);
        if (canApprove.Permitted)
        {
            if (existing.Status != ProtocolVersionStatus.InReview || string.IsNullOrWhiteSpace(existing.ContentHash))
            {
                return KernelErrorEndpoints.Problem(ProtocolErrors.InvalidTransition);
            }

            if (!string.IsNullOrWhiteSpace(request?.ExpectedContentHash))
            {
                var normalizedExpected = NormalizeSha256(request.ExpectedContentHash);
                if (!string.Equals(normalizedExpected, existing.ContentHash, StringComparison.Ordinal))
                {
                    return KernelErrorEndpoints.Problem(TrustErrors.TargetContentChanged);
                }
            }

            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(AttestationTarget.ProtocolVersionKind, versionId.Value, existing.ContentHash),
                    new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(attestResult.Error!);
            }
        }

        var approveResult = await sender.SendAsync(
            new ApproveProtocolVersion(tenant, versionId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!approveResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(approveResult.Error!);
        }

        var updated = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToProtocolVersionResponse(updated, approveResult.Value!));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelArtifactResponse>>, ProblemHttpResult>> ListStudyArtifactsAsync(
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var reader = context.RequestServices.GetService<IStudyGovernanceReader>();
        if (reader is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelArtifactResponse>>([]);
        }

        var rows = await reader.ListStudyArtifactsAsync(tenant, resolved.StudyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelArtifactResponse>>([.. rows.Select(KernelArtifactResponse.From)]);
    }

    internal static async Task<Results<Created<KernelArtifactResponse>, ProblemHttpResult>> RegisterStudyArtifactAsync(
        string studyRef,
        KernelRegisterArtifactRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid artifact registration", "Supply artifact kind, title, content hash, media type, and storage reference.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        if (string.IsNullOrWhiteSpace(request.Kind)
            || !Enum.TryParse<ArtifactKind>(request.Kind.Trim(), ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind))
        {
            return KernelErrorEndpoints.Problem(EvidenceErrors.KindInvalid);
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolvedStudy = await ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken).ConfigureAwait(false);
        if (resolvedStudy.Problem is { } problem)
        {
            return problem;
        }

        var artifactId = request.ArtifactId is { } givenArtifact && givenArtifact != Guid.Empty
            ? ArtifactId.FromGuid(givenArtifact)
            : ArtifactId.FromGuid(DeriveGuid(tenant, "register-artifact", idempotencyKey));
        var versionId = request.ArtifactVersionId is { } givenVersion && givenVersion != Guid.Empty
            ? ArtifactVersionId.FromGuid(givenVersion)
            : ArtifactVersionId.FromGuid(DeriveGuid(tenant, "register-artifact-version", idempotencyKey));

        var normalizedHash = NormalizeSha256(request.ContentHash ?? string.Empty);
        var mediaType = (request.MediaType ?? string.Empty).Trim();
        var storageRef = (request.StorageRef ?? string.Empty).Trim();
        var content = new ArtifactContent(normalizedHash, request.SizeBytes, mediaType, storageRef);

        var result = await sender.SendAsync(
            new RegisterArtifact(
                tenant,
                artifactId,
                versionId,
                kind,
                resolvedStudy.StudyId,
                request.Title ?? string.Empty,
                content,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = KernelArtifactResponse.From(result.Value!, request.SizeBytes, mediaType, storageRef, clock.UtcNow);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(result.Value!.ArtifactId)}",
            response);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelEffectivityResponse>>, ProblemHttpResult>> ListStudyEffectivitiesAsync(
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var reader = context.RequestServices.GetService<IStudyGovernanceReader>();
        if (reader is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelEffectivityResponse>>([]);
        }

        var rows = await reader.ListStudyEffectivitiesAsync(tenant, resolved.StudyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelEffectivityResponse>>([.. rows.Select(KernelEffectivityResponse.From)]);
    }

    internal static async Task<Results<Created<KernelEffectivityResponse>, ProblemHttpResult>> DeclareEffectivityAsync(
        string versionRef,
        KernelDeclareEffectivityRequest? request,
        HttpContext context,
        IProtocolVersionReader versions,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        if (!TryBuildEffectivityScope(request, out var scope, out var scopeProblem))
        {
            return scopeProblem!;
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectivityId = request?.EffectivityId is { } given && given != Guid.Empty
            ? EffectivityId.FromGuid(given)
            : EffectivityId.FromGuid(DeriveGuid(tenant, "declare-effectivity", idempotencyKey));
        var declaredAt = request?.DeclaredAt ?? clock.UtcNow.AddSeconds(-1);

        var result = await sender.SendAsync(
            new DeclareEffectivity(tenant, effectivityId, versionId, scope!, declaredAt, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var version = await versions.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        var response = KernelEffectivityResponse.From(result.Value!, version?.StudyId, version?.Number);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(result.Value!.EffectivityId)}",
            response);
    }

    internal static async Task<Results<Ok<KernelEffectivityResponse>, ProblemHttpResult>> ChangeEffectivityStatusAsync(
        string effectivityRef,
        KernelChangeEffectivityStatusRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseEffectivity(effectivityRef, out var effectivityId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{effectivityRef}' is not an effectivity NodeRef (effectivity:<uuid>).");
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.Status)
            || !Enum.TryParse<EffectivityStatus>(request.Status.Trim(), ignoreCase: true, out var targetStatus)
            || !Enum.IsDefined(targetStatus))
        {
            return KernelProblems.BadRequest(
                "Invalid effectivity status",
                "Specify status as RegulatoryApproved, EthicsApproved, Effective, or Withdrawn.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var evidence = new List<ArtifactVersionId>();
        if (request.EvidenceArtifactVersionRefs is not null)
        {
            foreach (var rawRef in request.EvidenceArtifactVersionRefs)
            {
                if (string.IsNullOrWhiteSpace(rawRef))
                {
                    continue;
                }

                if (!TryParseArtifactVersion(rawRef, out var artifactVersionId))
                {
                    return KernelProblems.BadRequest(
                        "Invalid NodeRef",
                        $"'{rawRef}' is not an artifact_version NodeRef (artifact_version:<uuid>).");
                }

                evidence.Add(artifactVersionId);
            }
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectiveAt = request.EffectiveAt ?? clock.UtcNow;

        var result = await sender.SendAsync(
            new ChangeEffectivityStatus(tenant, effectivityId, targetStatus, effectiveAt, evidence, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var reader = context.RequestServices.GetService<IStudyGovernanceReader>();
        var summary = reader is not null
            ? await reader.FindEffectivityAsync(tenant, effectivityId, cancellationToken).ConfigureAwait(false)
            : null;
        return TypedResults.Ok(summary is not null
            ? KernelEffectivityResponse.From(summary)
            : KernelEffectivityResponse.From(result.Value!));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelStudySiteResponse>>, ProblemHttpResult>> ListStudySitesAsync(
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken,
        DateTimeOffset? valid_at = null,
        DateTimeOffset? known_at = null)
    {
        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var reader = context.RequestServices.GetService<IStudyGovernanceReader>();
        if (reader is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelStudySiteResponse>>([]);
        }

        var now = clock.UtcNow;
        var sites = await reader.ListStudySitesAsync(
            tenant,
            resolved.StudyId,
            valid_at ?? now,
            known_at ?? now,
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelStudySiteResponse>>([.. sites.Select(KernelStudySiteResponse.From)]);
    }

    internal static async Task<Results<Created<KernelStudySiteResponse>, Ok<KernelStudySiteResponse>, ProblemHttpResult>> LinkStudySiteAsync(
        string studyRef,
        KernelLinkStudySiteRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SiteCode))
        {
            return KernelProblems.BadRequest("Invalid study site request", "Supply site_code to link a site to the study.");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var studyDirectory = context.RequestServices.GetService<IStudyDirectory>();
        var governanceReader = context.RequestServices.GetService<IStudyGovernanceReader>();

        var resolvedStudy = await ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken).ConfigureAwait(false);
        if (resolvedStudy.Problem is { } problem)
        {
            return problem;
        }

        var studyId = resolvedStudy.StudyId;
        var siteCode = request.SiteCode.Trim();
        var studyEntry = studyDirectory is not null
            ? await studyDirectory.FindStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false)
            : null;

        var now = clock.UtcNow;
        if (governanceReader is not null)
        {
            var existingSites = await governanceReader
                .ListStudySitesAsync(tenant, studyId, now, now, cancellationToken)
                .ConfigureAwait(false);
            if (existingSites.FirstOrDefault(s => string.Equals(s.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase)) is { } existingSummary)
            {
                return TypedResults.Ok(KernelStudySiteResponse.From(existingSummary));
            }
        }

        SiteId siteId;
        var existingSite = governanceReader is not null
            ? await governanceReader.FindSiteByCodeAsync(tenant, siteCode, cancellationToken).ConfigureAwait(false)
            : null;
        if (existingSite is not null)
        {
            siteId = existingSite.SiteId;
        }
        else
        {
            OrganizationId institutionId;
            if (!string.IsNullOrWhiteSpace(request.InstitutionRef))
            {
                if (!TryParseOrganization(request.InstitutionRef, out institutionId))
                {
                    return KernelProblems.BadRequest("Invalid NodeRef", $"'{request.InstitutionRef}' is not an organization NodeRef (organization:<uuid>).");
                }
            }
            else if (studyEntry is not null)
            {
                institutionId = studyEntry.SponsorId;
            }
            else
            {
                return KernelProblems.BadRequest("Institution required", "Provide institution_ref when the study sponsor organization cannot be resolved.");
            }

            siteId = request.SiteId is { } givenSite && givenSite != Guid.Empty
                ? SiteId.FromGuid(givenSite)
                : SiteId.FromGuid(DeriveGuid(tenant, "register-site", $"{siteCode.ToUpperInvariant()}:{idempotencyKey}"));
            var siteName = string.IsNullOrWhiteSpace(request.SiteName) ? siteCode : request.SiteName.Trim();
            var country = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim().ToUpperInvariant();

            var registerSiteResult = await sender.SendAsync(
                new RegisterSite(tenant, siteId, institutionId, siteCode, siteName, country, $"{idempotencyKey}:site", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!registerSiteResult.IsSuccess && registerSiteResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(registerSiteResult.Error!);
            }
        }

        var studySiteId = request.StudySiteId is { } givenStudySite && givenStudySite != Guid.Empty
            ? StudySiteId.FromGuid(givenStudySite)
            : StudySiteId.FromGuid(DeriveGuid(tenant, "link-study-site", idempotencyKey));

        var linkResult = await sender.SendAsync(
            new LinkSiteToStudy(tenant, studySiteId, studyId, siteId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!linkResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(linkResult.Error!);
        }

        var linked = governanceReader is not null
            ? await governanceReader.FindStudySiteAsync(tenant, linkResult.Value, now, now, cancellationToken).ConfigureAwait(false)
            : null;
        var fallbackSiteName = string.IsNullOrWhiteSpace(request.SiteName) ? siteCode : request.SiteName.Trim();
        var fallbackCountry = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim().ToUpperInvariant();
        var response = linked is not null
            ? KernelStudySiteResponse.From(linked)
            : new KernelStudySiteResponse(
                NodeRef.For(linkResult.Value).ToString(),
                linkResult.Value.Value,
                NodeRef.For(studyId).ToString(),
                NodeRef.For(siteId).ToString(),
                siteCode,
                fallbackSiteName,
                fallbackCountry,
                "Selected",
                []);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(linkResult.Value)}",
            response);
    }

    internal static async Task<Results<Ok<KernelStudySiteResponse>, ProblemHttpResult>> ActivateStudySiteAsync(
        string studySiteRef,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseStudySite(studySiteRef, out var studySiteId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{studySiteRef}' is not a study_site NodeRef (study_site:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = ReadMutationHeaders(context, changeReason);
        if (headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new ActivateStudySite(tenant, studySiteId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var reader = context.RequestServices.GetService<IStudyGovernanceReader>();
        var now = clock.UtcNow;
        var details = reader is not null
            ? await reader.FindStudySiteAsync(tenant, studySiteId, now, now, cancellationToken).ConfigureAwait(false)
            : null;
        return TypedResults.Ok(KernelStudySiteResponse.From(result.Value!, details));
    }

    private static KernelProtocolVersionResponse ToProtocolVersionResponse(ProtocolVersionSummary? readBack, ProtocolVersionState state) =>
        readBack is not null
            ? KernelProtocolVersionResponse.From(readBack, mask: null)
            : new KernelProtocolVersionResponse(
                NodeRef.For(state.ProtocolVersionId).ToString(),
                NodeRef.For(state.StudyId).ToString(),
                state.Number,
                ParentVersion: null,
                state.Status.ToString(),
                state.ContentHash,
                AmendmentIntent: null,
                ApprovalAttestation: null,
                WithdrawalReason: null,
                Mask: null);

    private static (string IdempotencyKey, string Reason, ProblemHttpResult? Problem) ReadMutationHeaders(
        HttpContext context,
        IKernelChangeReason changeReason)
    {
        var idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired));
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired));
        }

        return (idempotencyKey, reason, null);
    }

    private static bool TryBuildEffectivityScope(
        KernelDeclareEffectivityRequest? request,
        out EffectivityScope? scope,
        out ProblemHttpResult? problem)
    {
        var rawLevel = string.IsNullOrWhiteSpace(request?.ScopeLevel) ? "Study" : request.ScopeLevel.Trim();
        if (!Enum.TryParse<EffectivityLevel>(rawLevel, ignoreCase: true, out var level) || !Enum.IsDefined(level))
        {
            scope = null;
            problem = KernelErrorEndpoints.Problem(EffectivityErrors.ScopeInvalid.WithMessage("Scope level must be Study, Country, or StudySite."));
            return false;
        }

        switch (level)
        {
            case EffectivityLevel.Study:
                scope = EffectivityScope.Study();
                problem = null;
                return true;

            case EffectivityLevel.Country:
                {
                    var country = (request?.Country ?? string.Empty).Trim().ToUpperInvariant();
                    var candidate = EffectivityScope.InCountry(country);
                    if (candidate.Problem() is { } countryProblem)
                    {
                        scope = null;
                        problem = KernelErrorEndpoints.Problem(EffectivityErrors.ScopeInvalid.WithMessage(countryProblem));
                        return false;
                    }

                    scope = candidate;
                    problem = null;
                    return true;
                }

            case EffectivityLevel.StudySite:
                {
                    var rawSite = (request?.StudySiteRef ?? string.Empty).Trim();
                    if (!TryParseStudySite(rawSite, out var studySiteId))
                    {
                        scope = null;
                        problem = KernelProblems.BadRequest(
                            "Invalid NodeRef",
                            $"'{rawSite}' is not a study_site NodeRef (study_site:<uuid>).");
                        return false;
                    }

                    scope = EffectivityScope.At(studySiteId);
                    problem = null;
                    return true;
                }

            default:
                scope = null;
                problem = KernelErrorEndpoints.Problem(EffectivityErrors.ScopeInvalid);
                return false;
        }
    }

    private static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveVisibleStudyAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is not null)
        {
            return resolved;
        }

        var studyNode = NodeRef.For(resolved.StudyId);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(studyNode) is not null)
        {
            return (StudyId.Empty, TypedResults.Problem(
                title: "Forbidden",
                detail: "You do not have access to this study.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (resolved.StudyId, null);
    }

    private static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveStudyIdAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyRef))
        {
            return (StudyId.Empty, KernelProblems.BadRequest("Invalid study reference", "Provide a study NodeRef (study:<uuid>) or study code."));
        }

        var trimmed = studyRef.Trim();
        if (NodeRef.TryParse(trimmed, out var parsed) && parsed is not null)
        {
            if (!parsed.TryGetId<StudyId>(out var parsedStudyId))
            {
                return (StudyId.Empty, KernelProblems.BadRequest("Invalid NodeRef", $"'{studyRef}' is a {parsed.Kind}, not a study."));
            }

            return (parsedStudyId, null);
        }

        if (Guid.TryParse(trimmed, out var rawGuid) && rawGuid != Guid.Empty)
        {
            return (StudyId.FromGuid(rawGuid), null);
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is not null)
        {
            var found = await directory.FindStudyByCodeAsync(tenant, trimmed, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                return (found.StudyId, null);
            }
        }

        return (StudyId.Empty, TypedResults.Problem(
            title: "Not found",
            detail: $"Study '{trimmed}' does not exist in your tenant.",
            statusCode: StatusCodes.Status404NotFound));
    }

    private static bool TryParseProtocolVersion(string raw, out ProtocolVersionId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = ProtocolVersionId.FromGuid(guid);
            return true;
        }

        id = ProtocolVersionId.Empty;
        return false;
    }

    private static bool TryParseEffectivity(string raw, out EffectivityId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = EffectivityId.FromGuid(guid);
            return true;
        }

        id = EffectivityId.Empty;
        return false;
    }

    private static bool TryParseArtifactVersion(string raw, out ArtifactVersionId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = ArtifactVersionId.FromGuid(guid);
            return true;
        }

        id = ArtifactVersionId.Empty;
        return false;
    }

    private static bool TryParseStudySite(string raw, out StudySiteId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = StudySiteId.FromGuid(guid);
            return true;
        }

        id = StudySiteId.Empty;
        return false;
    }

    private static bool TryParseOrganization(string raw, out OrganizationId id)
    {
        if (NodeRef.TryParse(raw, out var parsed) && parsed is not null && parsed.TryGetId(out id))
        {
            return true;
        }

        if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
        {
            id = OrganizationId.FromGuid(guid);
            return true;
        }

        id = OrganizationId.Empty;
        return false;
    }

    private static string NormalizeSha256(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return $"sha256:{trimmed["sha256:".Length..].ToLowerInvariant()}";
        }

        return trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit)
            ? $"sha256:{trimmed.ToLowerInvariant()}"
            : trimmed;
    }

    private static Guid DeriveGuid(TenantId tenant, string kind, string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-governance:{tenant}:{kind}:{key}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
