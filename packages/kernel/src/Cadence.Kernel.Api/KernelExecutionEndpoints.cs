using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

/// <summary>
/// Kernel enrollment endpoints (spec §7.4). Every write runs through <see cref="IKernelCommandSender"/> with the signed
/// caller identity, <c>Idempotency-Key</c> header, and <see cref="IKernelChangeReason"/>.
/// </summary>
public static class KernelExecutionEndpoints
{
    /// <summary>The header carrying the command's idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly Actor FormsServiceActor = Actor.ServiceAccount("execution-forms");

    /// <summary>Maps <c>/enrollments</c> on the kernel route group.</summary>
    public static RouteGroupBuilder MapKernelExecution(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup("/enrollments").AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel enrollments");

        group.MapPost("/screen", ScreenAsync)
            .WithName("KernelScreenEnrollment")
            .WithSummary("Registers a pseudonymous participant when needed and starts a screening enrollment at a study site (participant.screen).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ScreenParticipant>();

        return kernel;
    }

    internal static async Task<Results<Created<KernelEnrollmentStateResponse>, Ok<KernelEnrollmentStateResponse>, ProblemHttpResult>> ScreenAsync(
        KernelScreenEnrollmentRequest request,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.StudyCode)
            || string.IsNullOrWhiteSpace(request.SiteCode)
            || !SubjectCode.TryFrom(request.SubjectCode?.Trim() ?? string.Empty, out var subjectCode))
        {
            return KernelProblems.BadRequest(
                "Invalid screening request",
                "Name the study code, site code and a valid subject code.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await enrollments.FindStudySiteInfoAsync(tenant, request.StudyCode.Trim(), request.SiteCode.Trim(), cancellationToken).ConfigureAwait(false)
            is not { } siteInfo)
        {
            return KernelErrorEndpoints.Problem(ExecutionErrors.StudySiteUnknown);
        }

        var (studyId, studySiteId) = (siteInfo.StudyId, siteInfo.StudySiteId);
        string key = IdempotencyKey(context);
        string reason = changeReason.Read(context) ?? string.Empty;
        var participantId = request.ParticipantId is { } givenParticipant && givenParticipant != Guid.Empty
            ? ParticipantId.FromGuid(givenParticipant)
            : ParticipantId.New();
        var enrollmentId = request.EnrollmentId is { } givenEnrollment && givenEnrollment != Guid.Empty
            ? EnrollmentId.FromGuid(givenEnrollment)
            : EnrollmentId.New();

        var screenEntry = catalog.Find(typeof(ScreenParticipant))!;
        var screenDecision = await probe.CanIAsync(tenant, screenEntry.Capability, NodeRef.For(studySiteId), cancellationToken).ConfigureAwait(false);
        if (!screenDecision.Permitted)
        {
            // Run through the command pipeline so the authorization denial is audited against the caller before any
            // participant identity is registered.
            var denied = await sender.SendAsync(
                new ScreenParticipant(tenant, enrollmentId, participantId, studySiteId, subjectCode, request.ScreenedAt, key, reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return KernelErrorEndpoints.Problem(denied.Error!);
        }

        if (!string.Equals(siteInfo.Status, "Active", StringComparison.Ordinal))
        {
            return KernelErrorEndpoints.Problem(ExecutionErrors.StudySiteNotActive);
        }

        if (await enrollments.ResolveParticipantAsync(tenant, studyId, subjectCode, cancellationToken).ConfigureAwait(false) is { } existing
            && existing.StudySiteId == studySiteId)
        {
            return TypedResults.Ok(KernelEnrollmentStateResponse.From(existing));
        }

        string participantKey = string.IsNullOrEmpty(key) ? string.Empty : $"{key}:participant";
        var registerCommand = new RegisterParticipant(tenant, participantId, participantKey, reason);
        var registerEntry = catalog.Find(typeof(RegisterParticipant))!;
        var registerDecision = await probe.CanIAsync(tenant, registerEntry.Capability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);

        CommandResult<ParticipantId> registerResult;
        if (registerDecision.Permitted)
        {
            registerResult = await sender.SendAsync(registerCommand, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // A participant record holds no study or site before screening (spec §7.4), so its registration is
            // tenant-wide while site coordinators hold site-scoped grants. Once participant.screen is permitted at the
            // study site above, the module service account registers the pseudonymous participant id and the caller
            // executes ScreenParticipant under their own site grant.
            await using var serviceScope = scopes.CreateAsyncScope();
            serviceScope.ServiceProvider.GetRequiredService<ServiceActorScope>().Actor = FormsServiceActor;
            registerResult = await serviceScope.ServiceProvider.GetRequiredService<IKernelCommandSender>()
                .SendAsync(registerCommand, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (!registerResult.IsSuccess && registerResult.Error?.Code != KernelErrors.AlreadyExists.Code)
        {
            return KernelErrorEndpoints.Problem(registerResult.Error!);
        }

        var screenResult = await sender.SendAsync(
            new ScreenParticipant(tenant, enrollmentId, participantId, studySiteId, subjectCode, request.ScreenedAt, key, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (screenResult.Value is { } state)
        {
            return TypedResults.Created(
                $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(state.EnrollmentId)}",
                KernelEnrollmentStateResponse.From(state));
        }

        if ((screenResult.Error?.Code == KernelErrors.AlreadyExists.Code
                || screenResult.Error?.Code == ExecutionErrors.SubjectCodeTaken.Code)
            && await enrollments.ResolveParticipantAsync(tenant, studyId, subjectCode, cancellationToken).ConfigureAwait(false) is { } concurrent
            && concurrent.StudySiteId == studySiteId)
        {
            return TypedResults.Ok(KernelEnrollmentStateResponse.From(concurrent));
        }

        return KernelErrorEndpoints.Problem(screenResult.Error!);
    }

    private static string IdempotencyKey(HttpContext context) => context.Request.Headers[IdempotencyKeyHeader].ToString().Trim();
}

/// <summary>Body of <c>POST /api/v1/kernel/enrollments/screen</c>.</summary>
/// <param name="ParticipantId">The pseudonymous participant id; assigned when absent.</param>
/// <param name="EnrollmentId">The enrollment id; assigned when absent.</param>
/// <param name="StudyCode">The study code in the caller's tenant.</param>
/// <param name="SiteCode">The site code linked to the study.</param>
/// <param name="SubjectCode">The study-scoped subject code.</param>
/// <param name="ScreenedAt">When screening began in valid time (K-TIME-9).</param>
public sealed record KernelScreenEnrollmentRequest(
    Guid? ParticipantId,
    Guid? EnrollmentId,
    string StudyCode,
    string SiteCode,
    string SubjectCode,
    DateTimeOffset ScreenedAt);

/// <summary>A kernel enrollment after screening.</summary>
/// <param name="Ref">The enrollment's canonical NodeRef.</param>
/// <param name="EnrollmentId">The enrollment's id.</param>
/// <param name="ParticipantId">The pseudonymous participant's id.</param>
/// <param name="StudyId">The study's id.</param>
/// <param name="StudySiteId">The study site's id.</param>
/// <param name="SubjectCode">The study-scoped subject code.</param>
/// <param name="EnrollmentStatus">The enrollment's lifecycle state.</param>
/// <param name="StatusSince">When the status took effect in valid time.</param>
/// <param name="EnrollmentNumber">The gap-free study enrollment number once enrolled.</param>
/// <param name="ContentHash">The content hash for attestations over the enrollment.</param>
public sealed record KernelEnrollmentStateResponse(
    string Ref,
    Guid EnrollmentId,
    Guid ParticipantId,
    Guid StudyId,
    Guid StudySiteId,
    string SubjectCode,
    string EnrollmentStatus,
    DateTimeOffset StatusSince,
    int? EnrollmentNumber,
    string ContentHash)
{
    internal static KernelEnrollmentStateResponse From(EnrollmentState state) =>
        new(
            NodeRef.For(state.EnrollmentId).ToString(),
            state.EnrollmentId.Value,
            state.ParticipantId.Value,
            state.StudyId.Value,
            state.StudySiteId.Value,
            state.SubjectCode.Value,
            state.Status.ToString(),
            state.StatusSince,
            state.EnrollmentNumber,
            state.ContentHash);
}
