using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

internal static class KernelStudyEffectivityAndArtifactEndpoints
{
    internal static async Task<Results<Ok<IReadOnlyList<KernelArtifactResponse>>, ProblemHttpResult>> ListStudyArtifactsAsync(
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var resolved = await KernelProtocolGovernanceHelpers.ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
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

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
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
        var resolvedStudy = await KernelProtocolGovernanceHelpers.ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken).ConfigureAwait(false);
        if (resolvedStudy.Problem is { } problem)
        {
            return problem;
        }

        var artifactId = request.ArtifactId is { } givenArtifact && givenArtifact != Guid.Empty
            ? ArtifactId.FromGuid(givenArtifact)
            : ArtifactId.FromGuid(KernelProtocolGovernanceHelpers.DeriveGuid(tenant, "register-artifact", idempotencyKey));
        var versionId = request.ArtifactVersionId is { } givenVersion && givenVersion != Guid.Empty
            ? ArtifactVersionId.FromGuid(givenVersion)
            : ArtifactVersionId.FromGuid(KernelProtocolGovernanceHelpers.DeriveGuid(tenant, "register-artifact-version", idempotencyKey));

        var normalizedHash = KernelProtocolGovernanceHelpers.NormalizeSha256(request.ContentHash ?? string.Empty);
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
        var resolved = await KernelProtocolGovernanceHelpers.ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
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
        if (!KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef (protocol_version:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
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
            : EffectivityId.FromGuid(KernelProtocolGovernanceHelpers.DeriveGuid(tenant, "declare-effectivity", idempotencyKey));
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
        if (!KernelProtocolGovernanceHelpers.TryParseEffectivity(effectivityRef, out var effectivityId))
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

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
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

                if (!KernelProtocolGovernanceHelpers.TryParseArtifactVersion(rawRef, out var artifactVersionId))
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

    internal static async Task<Results<Ok<KernelStudySiteResponse>, ProblemHttpResult>> ActivateStudySiteAsync(
        string studySiteRef,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!KernelProtocolGovernanceHelpers.TryParseStudySite(studySiteRef, out var studySiteId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{studySiteRef}' is not a study_site NodeRef (study_site:<uuid>).");
        }

        var (idempotencyKey, reason, headerProblem) = KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);
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
                    if (!KernelProtocolGovernanceHelpers.TryParseStudySite(rawSite, out var studySiteId))
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
}
