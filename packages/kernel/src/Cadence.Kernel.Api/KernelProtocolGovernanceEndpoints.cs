using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

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

    /// <summary>The semantic action a protocol-version withdrawal step-up token must carry (21 CFR Part 11).</summary>
    public const string WithdrawProtocolVersionSemanticAction = "kernel.protocol_version.withdraw";

    /// <summary>The canonical concrete action path for approving a protocol version reference.</summary>
    public static string ApproveProtocolVersionAction(string versionRef)
    {
        var canonical = KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId)
            ? NodeRef.For(versionId).ToString()
            : versionRef;
        return $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{canonical}/approve";
    }

    /// <summary>The canonical concrete action path for withdrawing a protocol version reference.</summary>
    public static string WithdrawProtocolVersionAction(string versionRef)
    {
        var canonical = KernelProtocolGovernanceHelpers.TryParseProtocolVersion(versionRef, out var versionId)
            ? NodeRef.For(versionId).ToString()
            : versionRef;
        return $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{canonical}/withdraw";
    }

    /// <summary>Maps protocol governance, evidence artifact, effectivity, and study-site activation routes.</summary>
    public static RouteGroupBuilder MapKernelProtocolGovernance(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel protocol governance");

        group.MapPost("/protocol-versions/{versionRef}/fork", ForkProtocolVersionAsync)
            .WithName("KernelForkProtocolVersion")
            .WithSummary("Forks an approved protocol version into the study's next numbered Draft protocol amendment without importing external files (protocol.amend).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                KernelErrors.NotFound,
                ProtocolErrors.InvalidTransition,
                ProtocolErrors.AmendmentInvalid,
            ]);

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
                ProtocolErrors.ReferenceUnknown,
                ProtocolErrors.UndeclaredImpactedElements,
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
                ProtocolErrors.SimulationReviewRequired,
            ])
            .RequireElectronicSignature(
                ApproveProtocolVersionSemanticAction,
                ctx => ApproveProtocolVersionAction(ctx.Request.RouteValues["versionRef"]?.ToString() ?? string.Empty));

        group.MapGet("/protocol-versions/{versionRef}/approval-readiness", GetProtocolApprovalReadinessAsync)
            .WithName("KernelGetProtocolApprovalReadiness")
            .WithSummary("Evaluates read-only approval readiness, simulation report binding, and wire-masked explanation items for a protocol version prior to step-up authentication (K-PROT-2, K-SIM-5).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/protocol-versions/{versionRef}/withdraw", WithdrawProtocolVersionAsync)
            .WithName("KernelWithdrawProtocolVersion")
            .WithSummary("Records a 21 CFR Part 11 Withdrawn attestation bound to the frozen protocol content hash and withdraws an InReview or Approved protocol version (protocol.withdraw).")
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
                WithdrawProtocolVersionSemanticAction,
                ctx => WithdrawProtocolVersionAction(ctx.Request.RouteValues["versionRef"]?.ToString() ?? string.Empty));

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

    internal static Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> SubmitProtocolVersionForReviewAsync(
        string versionRef, HttpContext context, IProtocolVersionReader versions, IKernelCommandSender sender,
        IKernelChangeReason changeReason, CancellationToken cancellationToken) =>
        KernelProtocolVersionLifecycleEndpoints.SubmitProtocolVersionForReviewAsync(
            versionRef, context, versions, sender, changeReason, cancellationToken);

    internal static Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ReturnProtocolVersionToDraftAsync(
        string versionRef, HttpContext context, IProtocolVersionReader versions, IKernelCommandSender sender,
        IKernelChangeReason changeReason, CancellationToken cancellationToken) =>
        KernelProtocolVersionLifecycleEndpoints.ReturnProtocolVersionToDraftAsync(
            versionRef, context, versions, sender, changeReason, cancellationToken);

    internal static Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ApproveProtocolVersionAsync(
        string versionRef, KernelApproveProtocolVersionRequest? request, HttpContext context, IProtocolVersionReader versions,
        IKernelCommandSender sender, IAuthorizationProbe probe, IKernelChangeReason changeReason, CancellationToken cancellationToken) =>
        KernelProtocolVersionLifecycleEndpoints.ApproveProtocolVersionAsync(
            versionRef, request, context, versions, sender, probe, changeReason, cancellationToken);

    internal static Task<Results<Created<KernelProtocolVersionResponse>, ProblemHttpResult>> ForkProtocolVersionAsync(
        string versionRef, KernelForkProtocolVersionRequest? request, HttpContext context, IProtocolVersionReader versions,
        IKernelCommandSender sender, IKernelChangeReason changeReason, CancellationToken cancellationToken) =>
        KernelProtocolVersionLifecycleEndpoints.ForkProtocolVersionAsync(
            versionRef, request, context, versions, sender, changeReason, cancellationToken);

    internal static Task<Results<Ok<KernelProtocolApprovalReadinessResponse>, ProblemHttpResult>> GetProtocolApprovalReadinessAsync(
        string versionRef, HttpContext context, IProtocolVersionReader versions, INodeVisibility visibility,
        ICurrentActor actor, IClock clock, CancellationToken cancellationToken) =>
        KernelProtocolApprovalReadinessEndpoints.GetProtocolApprovalReadinessAsync(
            versionRef, context, versions, visibility, actor, clock, cancellationToken);

    internal static Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> WithdrawProtocolVersionAsync(
        string versionRef, KernelWithdrawProtocolVersionRequest? request, HttpContext context, IProtocolVersionReader versions,
        IKernelCommandSender sender, IAuthorizationProbe probe, IKernelChangeReason changeReason, CancellationToken cancellationToken) =>
        KernelProtocolVersionLifecycleEndpoints.WithdrawProtocolVersionAsync(
            versionRef, request, context, versions, sender, probe, changeReason, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelArtifactResponse>>, ProblemHttpResult>> ListStudyArtifactsAsync(
        string studyRef, HttpContext context, INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelStudyEffectivityAndArtifactEndpoints.ListStudyArtifactsAsync(studyRef, context, visibility, actor, cancellationToken);

    internal static Task<Results<Created<KernelArtifactResponse>, ProblemHttpResult>> RegisterStudyArtifactAsync(
        string studyRef, KernelRegisterArtifactRequest? request, HttpContext context, IKernelCommandSender sender,
        IKernelChangeReason changeReason, IClock clock, CancellationToken cancellationToken) =>
        KernelStudyEffectivityAndArtifactEndpoints.RegisterStudyArtifactAsync(
            studyRef, request, context, sender, changeReason, clock, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelEffectivityResponse>>, ProblemHttpResult>> ListStudyEffectivitiesAsync(
        string studyRef, HttpContext context, INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelStudyEffectivityAndArtifactEndpoints.ListStudyEffectivitiesAsync(studyRef, context, visibility, actor, cancellationToken);

    internal static Task<Results<Created<KernelEffectivityResponse>, ProblemHttpResult>> DeclareEffectivityAsync(
        string versionRef, KernelDeclareEffectivityRequest? request, HttpContext context, IProtocolVersionReader versions,
        IKernelCommandSender sender, IKernelChangeReason changeReason, IClock clock, CancellationToken cancellationToken) =>
        KernelStudyEffectivityAndArtifactEndpoints.DeclareEffectivityAsync(
            versionRef, request, context, versions, sender, changeReason, clock, cancellationToken);

    internal static Task<Results<Ok<KernelEffectivityResponse>, ProblemHttpResult>> ChangeEffectivityStatusAsync(
        string effectivityRef, KernelChangeEffectivityStatusRequest? request, HttpContext context, IKernelCommandSender sender,
        IKernelChangeReason changeReason, IClock clock, CancellationToken cancellationToken) =>
        KernelStudyEffectivityAndArtifactEndpoints.ChangeEffectivityStatusAsync(
            effectivityRef, request, context, sender, changeReason, clock, cancellationToken);

    internal static Task<Results<Ok<KernelStudySiteResponse>, ProblemHttpResult>> ActivateStudySiteAsync(
        string studySiteRef, HttpContext context, IKernelCommandSender sender, IKernelChangeReason changeReason,
        IClock clock, CancellationToken cancellationToken) =>
        KernelStudyEffectivityAndArtifactEndpoints.ActivateStudySiteAsync(
            studySiteRef, context, sender, changeReason, clock, cancellationToken);

    internal static (string IdempotencyKey, string Reason, ProblemHttpResult? Problem) ReadMutationHeaders(
        HttpContext context, IKernelChangeReason changeReason) =>
        KernelProtocolGovernanceHelpers.ReadMutationHeaders(context, changeReason);

    internal static Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveStudyIdAsync(
        TenantId tenant, string studyRef, HttpContext context, CancellationToken cancellationToken) =>
        KernelProtocolGovernanceHelpers.ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken);

    internal static bool TryParseArtifactVersion(string raw, out ArtifactVersionId id) =>
        KernelProtocolGovernanceHelpers.TryParseArtifactVersion(raw, out id);

    internal static string NormalizeSha256(string raw) =>
        KernelProtocolGovernanceHelpers.NormalizeSha256(raw);

    internal static Guid DeriveGuid(TenantId tenant, string kind, string key) =>
        KernelProtocolGovernanceHelpers.DeriveGuid(tenant, kind, key);
}
