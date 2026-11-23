using System;
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
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>The semantic action a withdrawal step-up token must carry.</summary>
    public const string WithdrawSemanticAction = "kernel.enrollment.withdraw";

    /// <summary>The canonical concrete action path for withdrawing a participant from an enrollment.</summary>
    public const string WithdrawAction = $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/enrollments/withdraw";

    /// <summary>How the signer re-authenticated at the gateway (step-up contract v3).</summary>
    public const string StepUpMethod = "gateway-step-up:v3";

    private static readonly Actor FormsServiceActor = Actor.ServiceAccount("execution-forms");
    private static readonly Capability ChangeStatusCapability = new("enrollment.change_status", VerbClass.Execute);
    private static readonly Capability WithdrawCapability = new("participant.withdraw", VerbClass.Execute);

    /// <summary>Maps <c>/enrollments</c> on the kernel route group.</summary>
    public static RouteGroupBuilder MapKernelExecution(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup("/enrollments").AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel enrollments");

        group.MapGet("/by-subject", GetBySubjectAsync)
            .WithName("KernelGetEnrollmentBySubject")
            .WithSummary("Resolves a study code and subject code to its kernel enrollment state when visible to the caller (K-AUTH-4).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/screen", ScreenAsync)
            .WithName("KernelScreenEnrollment")
            .WithSummary("Registers a pseudonymous participant when needed and starts a screening enrollment at a study site (participant.screen).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ScreenParticipant>();

        group.MapPost("/status", ChangeStatusAsync)
            .WithName("KernelChangeEnrollmentStatus")
            .WithSummary("Transitions an enrollment along EnrollmentTransitions and allocates a gap-free study enrollment number on entering Enrolled (enrollment.change_status).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ChangeEnrollmentStatus>();

        group.MapPost("/withdraw", WithdrawAsync)
            .WithName("KernelWithdrawEnrollment")
            .WithSummary("Verifies a 60-second single-use X-Sig-Token, records a Withdrawn attestation over the enrollment's current content hash, and withdraws the participant (participant.withdraw).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(WithdrawParticipant))!,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
            ])
            .RequireElectronicSignature(WithdrawSemanticAction, _ => WithdrawAction);

        return kernel;
    }

    internal static async Task<Results<Ok<KernelEnrollmentStateResponse>, ProblemHttpResult>> GetBySubjectAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "subject_code")] string? subjectCode,
        HttpContext context,
        IStudyDirectory studies,
        IEnrollmentDirectory enrollments,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyCode)
            || !SubjectCode.TryFrom(subjectCode?.Trim() ?? string.Empty, out var parsedSubjectCode))
        {
            return KernelProblems.BadRequest(
                "Invalid enrollment lookup",
                "Supply both study_code and a valid subject_code query parameter.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var study = await studies.FindStudyByCodeAsync(tenant, studyCode.Trim(), cancellationToken).ConfigureAwait(false);
        if (study is null
            || await enrollments.ResolveParticipantAsync(tenant, study.StudyId, parsedSubjectCode, cancellationToken).ConfigureAwait(false) is not { } existing)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.NotFound);
        }

        var enrollmentNode = NodeRef.For(existing.EnrollmentId);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [enrollmentNode], cancellationToken).ConfigureAwait(false);
        return masks.For(enrollmentNode) is null
            ? TypedResults.Ok(KernelEnrollmentStateResponse.From(existing))
            : KernelProblems.NotVisible("You may not see this enrollment.");
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
        if (await enrollments.FindStudySiteAsync(tenant, request.StudyCode.Trim(), request.SiteCode.Trim(), cancellationToken).ConfigureAwait(false)
            is not var (studyId, studySiteId))
        {
            return KernelErrorEndpoints.Problem(ExecutionErrors.StudySiteUnknown);
        }

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

    internal static async Task<Results<Ok<KernelEnrollmentStateResponse>, ProblemHttpResult>> ChangeStatusAsync(
        KernelChangeEnrollmentStatusRequest request,
        HttpContext context,
        IStudyDirectory studies,
        IEnrollmentDirectory enrollments,
        IAuthorizationProbe probe,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.ToStatus)
            || !Enum.TryParse<EnrollmentStatus>(request.ToStatus.Trim(), ignoreCase: true, out var toStatus)
            || !Enum.IsDefined(toStatus))
        {
            return KernelProblems.BadRequest(
                "Invalid enrollment status",
                "Specify to_status as a valid EnrollmentStatus (ScreenFailed, Enrolled, Randomized, Active or Completed).");
        }

        var tenant = KernelRequestTenant.Get(context);
        var (targetId, existing, problem) = await ResolveTargetEnrollmentAsync(
            tenant,
            request.EnrollmentRef,
            request.StudyCode,
            request.SubjectCode,
            studies,
            enrollments,
            cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return problem;
        }

        if (existing is not null
            && existing.Status == toStatus
            && toStatus != EnrollmentStatus.Withdrawn
            && (request.EffectiveAt is null
                || (TruncateToMicroseconds(request.EffectiveAt.Value) >= TruncateToMicroseconds(existing.StatusSince)
                    && TruncateToMicroseconds(request.EffectiveAt.Value) <= TruncateToMicroseconds(clock.UtcNow))))
        {
            var canChange = await probe.CanIAsync(tenant, ChangeStatusCapability, NodeRef.For(existing.EnrollmentId), cancellationToken).ConfigureAwait(false);
            if (canChange.Permitted)
            {
                return TypedResults.Ok(KernelEnrollmentStateResponse.From(existing));
            }
        }

        var effectiveAt = await ResolveEffectiveAtAsync(existing, request.EffectiveAt, clock, cancellationToken).ConfigureAwait(false);
        var result = await sender.SendAsync(
            new ChangeEnrollmentStatus(
                tenant,
                targetId,
                toStatus,
                effectiveAt,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } updated
            ? TypedResults.Ok(KernelEnrollmentStateResponse.From(updated))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<KernelEnrollmentStateResponse>, ProblemHttpResult>> WithdrawAsync(
        KernelWithdrawEnrollmentRequest request,
        HttpContext context,
        IStudyDirectory studies,
        IEnrollmentDirectory enrollments,
        IAuthorizationProbe probe,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } token)
        {
            return ElectronicSignatureRequiredProblem();
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelProblems.BadRequest("Idempotency-Key required", "Supply an Idempotency-Key header for enrollment withdrawal commands.");
        }

        var reason = changeReason.Read(context);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelProblems.BadRequest("Reason for change required", "Supply a signed X-Change-Reason header explaining why this participant is being withdrawn.");
        }

        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid withdrawal request", "Supply the withdrawal payload.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is { } entitlementReader)
        {
            var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (entitlements.IsReadOnly)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
            }

            if (!entitlements.ProductFamilies.Contains(ProductFamily.Capture))
            {
                return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
            }
        }

        var (targetId, existing, problem) = await ResolveTargetEnrollmentAsync(
            tenant,
            request.EnrollmentRef,
            request.StudyCode,
            request.SubjectCode,
            studies,
            enrollments,
            cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return problem;
        }

        if (string.IsNullOrWhiteSpace(request.WithdrawalKind)
            || !Enum.TryParse<WithdrawalKind>(request.WithdrawalKind.Trim(), ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind))
        {
            return KernelErrorEndpoints.Problem(ExecutionErrors.WithdrawalKindInvalid);
        }

        if (existing is not null
            && existing.Status == EnrollmentStatus.Withdrawn
            && request.EffectiveAt is null)
        {
            var canWithdrawExisting = await probe.CanIAsync(tenant, WithdrawCapability, NodeRef.For(existing.EnrollmentId), cancellationToken).ConfigureAwait(false);
            if (canWithdrawExisting.Permitted)
            {
                return TypedResults.Ok(KernelEnrollmentStateResponse.From(existing));
            }
        }

        var attestationId = request.AttestationId is { } givenAtt && givenAtt != Guid.Empty
            ? AttestationId.FromGuid(givenAtt)
            : AttestationId.FromGuid(DeriveDeterministicGuid(tenant, idempotencyKey, "withdraw-attest"));
        var effectiveAt = await ResolveEffectiveAtAsync(existing, request.EffectiveAt, clock, cancellationToken).ConfigureAwait(false);

        // Pre-check participant.withdraw and enrollment state/time before writing Attest so unauthorized, terminal, or
        // invalid-time requests fail on WithdrawParticipant without leaving an orphan Attestation row.
        if (existing is not null)
        {
            var canWithdraw = await probe.CanIAsync(tenant, WithdrawCapability, NodeRef.For(existing.EnrollmentId), cancellationToken).ConfigureAwait(false);
            var nowStored = TruncateToMicroseconds(clock.UtcNow);
            var effectiveStored = TruncateToMicroseconds(effectiveAt);
            var statusSinceStored = TruncateToMicroseconds(existing.StatusSince);
            if (canWithdraw.Permitted
                && EnrollmentTransitions.IsAllowed(existing.Status, EnrollmentStatus.Withdrawn)
                && effectiveStored > statusSinceStored
                && effectiveStored <= nowStored)
            {
                var attestResult = await sender.SendAsync(
                    new Attest(
                        tenant,
                        attestationId,
                        AttestationMeaning.Withdrawn,
                        new AttestationTarget(NodeKinds.Enrollment, existing.EnrollmentId.Value, existing.ContentHash),
                        new DateTimeOffset(token.IssuedAt, TimeSpan.Zero),
                        StepUpMethod,
                        $"{idempotencyKey}:attest",
                        reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (attestResult.Error is { } attestError)
                {
                    return KernelErrorEndpoints.Problem(attestError);
                }

                attestationId = attestResult.Value;
            }
        }

        var withdrawResult = await sender.SendAsync(
            new WithdrawParticipant(
                tenant,
                targetId,
                kind,
                attestationId,
                effectiveAt,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return withdrawResult.Value is { } withdrawn
            ? TypedResults.Ok(KernelEnrollmentStateResponse.From(withdrawn))
            : KernelErrorEndpoints.Problem(withdrawResult.Error!);
    }

    private static async Task<(EnrollmentId TargetId, EnrollmentState? Existing, ProblemHttpResult? Problem)> ResolveTargetEnrollmentAsync(
        TenantId tenant,
        string? enrollmentRef,
        string? studyCode,
        string? subjectCode,
        IStudyDirectory studies,
        IEnrollmentDirectory enrollments,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(enrollmentRef))
        {
            if (!NodeRef.TryParse(enrollmentRef.Trim(), out var node)
                || node.Kind != NodeKinds.Enrollment
                || node.Version is not null)
            {
                return (default, null, KernelProblems.BadRequest(
                    "Invalid enrollment reference",
                    "Name enrollment_ref as an unversioned enrollment NodeRef (enrollment:<uuid>)."));
            }

            var id = EnrollmentId.FromGuid(node.Id);
            var found = await enrollments.FindAsync(tenant, id, cancellationToken).ConfigureAwait(false);
            return (id, found, null);
        }

        if (!string.IsNullOrWhiteSpace(studyCode)
            && SubjectCode.TryFrom(subjectCode?.Trim() ?? string.Empty, out var parsedSubjectCode))
        {
            var study = await studies.FindStudyByCodeAsync(tenant, studyCode.Trim(), cancellationToken).ConfigureAwait(false);
            if (study is null
                || await enrollments.ResolveParticipantAsync(tenant, study.StudyId, parsedSubjectCode, cancellationToken).ConfigureAwait(false) is not { } resolved)
            {
                return (default, null, KernelErrorEndpoints.Problem(KernelErrors.NotFound));
            }

            return (resolved.EnrollmentId, resolved, null);
        }

        return (default, null, KernelProblems.BadRequest(
            "Invalid enrollment target",
            "Specify either enrollment_ref or both study_code and a valid subject_code."));
    }

    private static async Task<DateTimeOffset> ResolveEffectiveAtAsync(
        EnrollmentState? existing,
        DateTimeOffset? explicitEffectiveAt,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (explicitEffectiveAt is { } given)
        {
            return given;
        }

        var now = clock.UtcNow;
        if (existing is not null && TruncateToMicroseconds(now) <= TruncateToMicroseconds(existing.StatusSince))
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            now = clock.UtcNow;
        }

        return now;
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset instant) =>
        new(instant.UtcTicks - (instant.UtcTicks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);

    private static Guid DeriveDeterministicGuid(TenantId tenant, string idempotencyKey, string slot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"cadence:enrollment:{tenant}:{slot}:{idempotencyKey}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static ProblemHttpResult ElectronicSignatureRequiredProblem() =>
        TypedResults.Problem(
            title: "Re-authentication required",
            detail: SigTokenVerifier.ReauthenticationRequired,
            statusCode: StatusCodes.Status401Unauthorized);

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

/// <summary>Body of <c>POST /api/v1/kernel/enrollments/status</c>.</summary>
/// <param name="EnrollmentRef">Optional unversioned enrollment NodeRef (<c>enrollment:&lt;uuid&gt;</c>).</param>
/// <param name="StudyCode">Study code when resolving the enrollment by subject code.</param>
/// <param name="SubjectCode">Study-scoped subject code when resolving without <paramref name="EnrollmentRef"/>.</param>
/// <param name="ToStatus">Target <see cref="EnrollmentStatus"/> name.</param>
/// <param name="EffectiveAt">When the transition took effect in valid time; defaults to server clock when omitted.</param>
public sealed record KernelChangeEnrollmentStatusRequest(
    string? EnrollmentRef = null,
    string? StudyCode = null,
    string? SubjectCode = null,
    string ToStatus = "",
    DateTimeOffset? EffectiveAt = null);

/// <summary>Body of <c>POST /api/v1/kernel/enrollments/withdraw</c>.</summary>
/// <param name="EnrollmentRef">Optional unversioned enrollment NodeRef (<c>enrollment:&lt;uuid&gt;</c>).</param>
/// <param name="StudyCode">Study code when resolving the enrollment by subject code.</param>
/// <param name="SubjectCode">Study-scoped subject code when resolving without <paramref name="EnrollmentRef"/>.</param>
/// <param name="WithdrawalKind">Disposition <see cref="WithdrawalKind"/> name.</param>
/// <param name="EffectiveAt">When the withdrawal took effect in valid time; defaults to server clock when omitted.</param>
/// <param name="AttestationId">Optional attestation identifier; derived deterministically from the idempotency key when omitted.</param>
public sealed record KernelWithdrawEnrollmentRequest(
    string? EnrollmentRef = null,
    string? StudyCode = null,
    string? SubjectCode = null,
    string WithdrawalKind = "",
    DateTimeOffset? EffectiveAt = null,
    Guid? AttestationId = null);

/// <summary>A kernel enrollment state projection.</summary>
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
