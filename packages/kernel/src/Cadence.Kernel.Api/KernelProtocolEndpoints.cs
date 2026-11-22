using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

/// <summary>
/// Protocol version lifecycle routes (spec §6.2): draft creation and revision, submission for review (including
/// <c>K-STD-1</c> concept mapping checks), return to draft, 21 CFR Part 11 signed approval and withdrawal, and
/// amendment forking. Every mutating action dispatches through <see cref="IKernelCommandSender"/> with an
/// <c>Idempotency-Key</c> and a signed <c>X-Change-Reason</c>.
/// </summary>
public static class KernelProtocolEndpoints
{
    /// <summary>The semantic action a protocol-version approval step-up token must carry.</summary>
    public const string ApproveProtocolVersionSemanticAction = "kernel.protocol_version.approve";

    /// <summary>The semantic action a protocol-version withdrawal step-up token must carry.</summary>
    public const string WithdrawProtocolVersionSemanticAction = "kernel.protocol_version.withdraw";

    private static readonly Capability ApproveProtocolCapability = new("protocol.approve", VerbClass.Approve);
    private static readonly Capability WithdrawProtocolCapability = new("protocol.withdraw", VerbClass.Approve);

    /// <summary>The canonical concrete action path for approving a protocol version reference.</summary>
    public static string ApproveProtocolVersionAction(string versionRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{versionRef}/approve";

    /// <summary>The canonical concrete action path for withdrawing a protocol version reference.</summary>
    public static string WithdrawProtocolVersionAction(string versionRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{versionRef}/withdraw";

    /// <summary>Maps protocol lifecycle command routes on the kernel group.</summary>
    public static RouteGroupBuilder MapKernelProtocols(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel protocols");

        group.MapPost("/studies/{studyRef}/protocol-versions", CreateProtocolDraftAsync)
            .WithName("KernelCreateProtocolDraft")
            .WithSummary("Creates the first protocol version of a study as a Draft (protocol.draft).")
            .Produces<KernelProtocolVersionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<CreateProtocolDraft>();

        group.MapPut("/protocol-versions/{versionRef}/draft", ReviseProtocolDraftAsync)
            .WithName("KernelReviseProtocolDraft")
            .WithSummary("Revises a protocol draft's content and optional amendment metadata (protocol.revise_draft).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ReviseProtocolDraft>();

        group.MapPost("/protocol-versions/{versionRef}/submit", SubmitProtocolVersionAsync)
            .WithName("KernelSubmitProtocolVersion")
            .WithSummary("Submits a protocol draft for review, checking concept references and K-STD-1 mapping coverage (protocol.submit_for_review).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<SubmitProtocolVersionForReview>();

        group.MapPost("/protocol-versions/{versionRef}/return-to-draft", ReturnProtocolVersionToDraftAsync)
            .WithName("KernelReturnProtocolVersionToDraft")
            .WithSummary("Returns an in-review protocol version to Draft so its content can be revised (protocol.return_to_draft).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ReturnProtocolVersionToDraft>();

        group.MapPost("/protocol-versions/{versionRef}/approve", ApproveProtocolVersionAsync)
            .WithName("KernelApproveProtocolVersion")
            .WithSummary("Approves an in-review protocol version with a step-up Approved attestation bound to its content hash (protocol.approve).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(ApproveProtocolVersion))!)])
            .RequireElectronicSignature(
                ApproveProtocolVersionSemanticAction,
                ctx => ApproveProtocolVersionAction(ctx.Request.RouteValues["versionRef"]?.ToString() ?? string.Empty));

        group.MapPost("/protocol-versions/{versionRef}/withdraw", WithdrawProtocolVersionAsync)
            .WithName("KernelWithdrawProtocolVersion")
            .WithSummary("Withdraws an in-review or approved protocol version with a step-up Withdrawn attestation (protocol.withdraw).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(WithdrawProtocolVersion))!)])
            .RequireElectronicSignature(
                WithdrawProtocolVersionSemanticAction,
                ctx => WithdrawProtocolVersionAction(ctx.Request.RouteValues["versionRef"]?.ToString() ?? string.Empty));

        group.MapPost("/protocol-versions/{versionRef}/amendments", ForkProtocolVersionAsync)
            .WithName("KernelForkProtocolVersion")
            .WithSummary("Forks a new Draft amendment from an Approved protocol version (protocol.amend).")
            .Produces<KernelProtocolVersionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ForkProtocolVersion>();

        return kernel;
    }

    internal static async Task<Results<Created<KernelProtocolVersionResponse>, ProblemHttpResult>> CreateProtocolDraftAsync(
        string studyRef,
        KernelCreateProtocolDraftRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IProtocolVersionReader reader,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide protocol draft content in the request body.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var (studyId, studyProblem) = await ResolveStudyIdAsync(tenant, studyRef, context, cancellationToken).ConfigureAwait(false);
        if (studyProblem is not null)
        {
            return studyProblem;
        }

        var versionId = request.ProtocolVersionId is { } given && given != Guid.Empty
            ? ProtocolVersionId.FromGuid(given)
            : ProtocolVersionId.FromGuid(DeriveGuid(tenant, "protocol-draft", idempotencyKey));

        var result = await sender.SendAsync(
            new CreateProtocolDraft(tenant, studyId, versionId, request.Content ?? string.Empty, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var createdState = result.Value!;
        var response = await LoadResponseAsync(tenant, createdState, reader, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{NodeRef.For(createdState.ProtocolVersionId)}",
            response);
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ReviseProtocolDraftAsync(
        string versionRef,
        KernelReviseProtocolDraftRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IProtocolVersionReader reader,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide revised protocol draft content in the request body.");
        }

        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new ReviseProtocolDraft(
                tenant,
                versionId,
                request.Content ?? string.Empty,
                request.Amendment?.ToDomain(),
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return TypedResults.Ok(await LoadResponseAsync(tenant, result.Value!, reader, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> SubmitProtocolVersionAsync(
        string versionRef,
        HttpContext context,
        IKernelCommandSender sender,
        IProtocolVersionReader reader,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new SubmitProtocolVersionForReview(tenant, versionId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return TypedResults.Ok(await LoadResponseAsync(tenant, result.Value!, reader, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ReturnProtocolVersionToDraftAsync(
        string versionRef,
        HttpContext context,
        IKernelCommandSender sender,
        IProtocolVersionReader reader,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
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

        return TypedResults.Ok(await LoadResponseAsync(tenant, result.Value!, reader, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> ApproveProtocolVersionAsync(
        string versionRef,
        KernelApproveProtocolVersionRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IProtocolVersionReader reader,
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
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var existing = await reader.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.NotFound);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "approve-protocol-version-attest", idempotencyKey));

        if (!string.IsNullOrEmpty(existing.ContentHash))
        {
            var canApprove = await probe.CanIAsync(tenant, ApproveProtocolCapability, NodeRef.For(versionId), cancellationToken).ConfigureAwait(false);
            if (canApprove.Permitted)
            {
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
        }

        var approveResult = await sender.SendAsync(
            new ApproveProtocolVersion(tenant, versionId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!approveResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(approveResult.Error!);
        }

        return TypedResults.Ok(await LoadResponseAsync(tenant, approveResult.Value!, reader, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> WithdrawProtocolVersionAsync(
        string versionRef,
        KernelWithdrawProtocolVersionRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IProtocolVersionReader reader,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this protocol version withdrawal.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseProtocolVersion(versionRef, out var versionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var existing = await reader.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.NotFound);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "withdraw-protocol-version-attest", idempotencyKey));

        if (!string.IsNullOrEmpty(existing.ContentHash))
        {
            var canWithdraw = await probe.CanIAsync(tenant, WithdrawProtocolCapability, NodeRef.For(versionId), cancellationToken).ConfigureAwait(false);
            if (canWithdraw.Permitted)
            {
                var attestResult = await sender.SendAsync(
                    new Attest(
                        tenant,
                        attestationId,
                        AttestationMeaning.Withdrawn,
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
        }

        var withdrawResult = await sender.SendAsync(
            new WithdrawProtocolVersion(tenant, versionId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!withdrawResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(withdrawResult.Error!);
        }

        return TypedResults.Ok(await LoadResponseAsync(tenant, withdrawResult.Value!, reader, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<Results<Created<KernelProtocolVersionResponse>, ProblemHttpResult>> ForkProtocolVersionAsync(
        string versionRef,
        KernelForkProtocolVersionRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IProtocolVersionReader reader,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide amendment details in the request body.");
        }

        if (!TryParseProtocolVersion(versionRef, out var parentVersionId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{versionRef}' is not a protocol_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var newVersionId = request.ProtocolVersionId is { } given && given != Guid.Empty
            ? ProtocolVersionId.FromGuid(given)
            : ProtocolVersionId.FromGuid(DeriveGuid(tenant, "protocol-fork", idempotencyKey));

        var amendment = new KernelProtocolAmendmentInput(
            request.Intent,
            request.Rationale,
            request.SummaryOfChanges,
            request.ImpactedElements,
            request.Classification,
            request.TransitionPolicy).ToDomain();

        var result = await sender.SendAsync(
            new ForkProtocolVersion(tenant, parentVersionId, newVersionId, amendment, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var forkedState = result.Value!;
        var response = await LoadResponseAsync(tenant, forkedState, reader, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{NodeRef.For(forkedState.ProtocolVersionId)}",
            response);
    }

    private static ProblemHttpResult? ReadCommandHeaders(
        HttpContext context,
        IKernelChangeReason changeReason,
        out string idempotencyKey,
        out string reason)
    {
        idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        return null;
    }

    private static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveStudyIdAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (NodeRef.TryParse(studyRef, out var node))
        {
            if (node.Kind != NodeKinds.Study)
            {
                return (default, KernelProblems.WrongKind(node, NodeKinds.Study));
            }

            return (StudyId.FromGuid(node.Id), null);
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        var byCode = directory is not null
            ? await directory.FindStudyByCodeAsync(tenant, studyRef.Trim(), cancellationToken).ConfigureAwait(false)
            : null;
        if (byCode is null)
        {
            return (default, KernelErrorEndpoints.Problem(ProtocolErrors.StudyUnknown));
        }

        return (byCode.StudyId, null);
    }

    private static async Task<KernelProtocolVersionResponse> LoadResponseAsync(
        TenantId tenant,
        ProtocolVersionState state,
        IProtocolVersionReader reader,
        CancellationToken cancellationToken)
    {
        var summary = await reader.GetAsync(tenant, state.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        return summary is not null
            ? KernelProtocolVersionResponse.From(summary, null)
            : new KernelProtocolVersionResponse(
                NodeRef.For(state.ProtocolVersionId).ToString(),
                NodeRef.For(state.StudyId).ToString(),
                state.Number,
                null,
                state.Status.ToString(),
                state.ContentHash,
                null,
                null,
                null,
                null);
    }

    private static bool TryParseProtocolVersion(string text, out ProtocolVersionId versionId)
    {
        versionId = default;
        if (!NodeRef.TryParse(text, out var node) || node.Kind != NodeKinds.ProtocolVersion)
        {
            return false;
        }

        versionId = ProtocolVersionId.FromGuid(node.Id);
        return !versionId.IsEmpty;
    }

    private static KernelError[] DistinctErrors(params System.Collections.Generic.IEnumerable<KernelError>[] groups) =>
        [.. groups.SelectMany(g => g).GroupBy(e => e.Code, StringComparer.Ordinal).Select(g => g.First())];

    private static Guid DeriveGuid(TenantId tenant, string kind, string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-protocol:{tenant}:{kind}:{seed}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
