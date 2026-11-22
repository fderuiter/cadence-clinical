using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Execution;

/// <summary>Handles <see cref="RegisterParticipant"/>.</summary>
internal sealed class RegisterParticipantHandler : ICommandHandler<RegisterParticipant, ParticipantId>
{
    public async Task<CommandResult<ParticipantId>> HandleAsync(RegisterParticipant command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Participant>(command.ParticipantId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(Participant.Register(command.ParticipantId, command.TenantId));
        context.Work.Raise(new ParticipantRegistered(command.TenantId, command.ParticipantId));
        return CommandResult<ParticipantId>.Success(command.ParticipantId);
    }
}

/// <summary>
/// Handles <see cref="ScreenParticipant"/>. Locks study governance to enforce unique subject codes and non-overlapping
/// active enrollments, and establishes initial bitemporal <see cref="Applicability"/> when a protocol version is effective
/// at the site (spec §7.5, K-TIME-6).
/// </summary>
internal sealed class ScreenParticipantHandler : ICommandHandler<ScreenParticipant, EnrollmentState>
{
    public async Task<CommandResult<EnrollmentState>> HandleAsync(ScreenParticipant command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (!command.SubjectCode.IsValid)
        {
            return ExecutionErrors.SubjectCodeInvalid;
        }

        if (await context.Work.ExistsAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<StudySite>(command.StudySiteId, cancellationToken).ConfigureAwait(false) is not { } studySite)
        {
            return ExecutionErrors.StudySiteUnknown;
        }

        if (!string.Equals(studySite.Status, ActivateStudySiteHandler.ActiveStatus, StringComparison.Ordinal))
        {
            return ExecutionErrors.StudySiteNotActive;
        }

        if (await context.Work.ReadAsync<Participant>(command.ParticipantId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ExecutionErrors.ParticipantUnknown;
        }

        var studyId = studySite.StudyId;
        await context.Work.LockStudyGovernanceAsync(studyId, cancellationToken).ConfigureAwait(false);

        var code = command.SubjectCode;
        if ((await context.Work.ReadAllAsync<Enrollment>(e => e.StudyId == studyId && e.SubjectCode == code, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return ExecutionErrors.SubjectCodeTaken;
        }

        var created = Enrollment.Screen(
            command.EnrollmentId, command.TenantId, command.ParticipantId, studyId, command.StudySiteId, code, command.ScreenedAt, context.RecordedAt);
        if (created.Value is not { } enrollment)
        {
            return created.Error!;
        }

        var participantId = command.ParticipantId;
        var studySiteId = command.StudySiteId;
        var others = await context.Work.ReadAllAsync<Enrollment>(
            e => e.ParticipantId == participantId && e.StudySiteId == studySiteId, cancellationToken).ConfigureAwait(false);
        var opened = enrollment.Periods[0].Period.Valid;
        if (others.SelectMany(e => e.Periods).Any(p =>
                p.Period.Recorded.IsOpen && EnrollmentTransitions.IsActive(p.Status) && p.Period.Valid.Overlaps(opened)))
        {
            return ExecutionErrors.AlreadyActiveAtSite;
        }

        await ApplicabilityGovernance.ClosePriorTerminalParticipantPeriodsAsync(
            studyId, participantId, enrollment.Id, enrollment.StatusSince, context, cancellationToken).ConfigureAwait(false);

        context.Work.Add(enrollment);
        context.Work.Raise(
            new ParticipantScreened(
                enrollment.TenantId, enrollment.Id, enrollment.ParticipantId, studyId, enrollment.StudySiteId, code, enrollment.StatusSince),
            enrollment.StatusSince);

        await ApplicabilityGovernance.EstablishInitialIfNeededAsync(enrollment, studySite, context, cancellationToken).ConfigureAwait(false);
        return EnrollmentStates.Of(enrollment);
    }
}

/// <summary>
/// Handles <see cref="ChangeEnrollmentStatus"/>. Assigns gap-free enrollment numbers under the study governance lock
/// and maintains bitemporal <see cref="Applicability"/> (spec §7.4, §7.5).
/// </summary>
internal sealed class ChangeEnrollmentStatusHandler(IEnrollmentDirectory directory) : ICommandHandler<ChangeEnrollmentStatus, EnrollmentState>
{
    public async Task<CommandResult<EnrollmentState>> HandleAsync(ChangeEnrollmentStatus command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (command.To == EnrollmentStatus.Withdrawn)
        {
            return ExecutionErrors.WithdrawalRequiresCommand;
        }

        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } unlocked)
        {
            return KernelErrors.NotFound;
        }

        var studySite = await context.Work.ReadAsync<StudySite>(unlocked.StudySiteId, cancellationToken).ConfigureAwait(false);
        if (command.To == EnrollmentStatus.Enrolled)
        {
            if (studySite is null)
            {
                return ExecutionErrors.StudySiteUnknown;
            }

            if (!string.Equals(studySite.Status, ActivateStudySiteHandler.ActiveStatus, StringComparison.Ordinal))
            {
                return ExecutionErrors.StudySiteNotActive;
            }
        }

        await context.Work.LockStudyGovernanceAsync(unlocked.StudyId, cancellationToken).ConfigureAwait(false);

        if (await context.Work.FindAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return KernelErrors.NotFound;
        }

        var from = enrollment.Status;
        if (enrollment.CheckTransition(command.To, command.EffectiveAt, context.RecordedAt) is { } invalid)
        {
            return invalid;
        }

        int? number = command.To == EnrollmentStatus.Enrolled
            ? await directory.NextEnrollmentNumberAsync(command.TenantId, enrollment.StudyId, cancellationToken).ConfigureAwait(false)
            : null;
        if (enrollment.ChangeStatus(command.To, command.EffectiveAt, context.RecordedAt, number) is { } error)
        {
            return error;
        }

        context.Work.Raise(
            number is { } assigned
                ? new ParticipantEnrolled(
                    enrollment.TenantId, enrollment.Id, enrollment.ParticipantId, enrollment.StudyId, enrollment.StudySiteId, assigned, enrollment.StatusSince)
                : new EnrollmentStatusChanged(enrollment.TenantId, enrollment.Id, enrollment.ParticipantId, from, enrollment.Status, enrollment.StatusSince),
            enrollment.StatusSince);

        if (command.To == EnrollmentStatus.ScreenFailed)
        {
            var existing = (await context.Work.ReadAllAsync<Applicability>(
                a => a.EnrollmentId == enrollment.Id,
                cancellationToken).ConfigureAwait(false)).SingleOrDefault();
            if (existing is not null
                && await context.Work.FindAsync<Applicability>(existing.Id, cancellationToken).ConfigureAwait(false) is { } tracked)
            {
                tracked.CloseOpenPeriod(enrollment.StatusSince, context.RecordedAt);
            }
        }
        else if (command.To == EnrollmentStatus.Enrolled && studySite is not null)
        {
            await ApplicabilityGovernance.EstablishInitialIfNeededAsync(enrollment, studySite, context, cancellationToken).ConfigureAwait(false);
        }

        return EnrollmentStates.Of(enrollment);
    }
}

/// <summary>
/// Handles <see cref="WithdrawParticipant"/>: the acting person must have signed <c>Withdrawn</c> over the
/// enrollment's current content hash. From then on new capture for the enrollment is refused
/// (<see cref="Evidence.CaptureConditions"/>).
/// </summary>
internal sealed class WithdrawParticipantHandler(IAttestationVerifier verifier) : ICommandHandler<WithdrawParticipant, EnrollmentState>
{
    public async Task<CommandResult<EnrollmentState>> HandleAsync(WithdrawParticipant command, ICommandContext context, CancellationToken cancellationToken)
    {
        // The enrollment's scope lock, exclusively: capture shares it before it reads the enrollment's status, so a
        // capture either commits before the withdrawal or sees it and is refused (CaptureConditions, ADR-2621).
        await context.Work.LockScopeAsync(NodeRef.For(command.EnrollmentId), cancellationToken).ConfigureAwait(false);
        if (await context.Work.FindAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return KernelErrors.NotFound;
        }

        if (!Enum.IsDefined(command.Kind))
        {
            return ExecutionErrors.WithdrawalKindInvalid;
        }

        var from = enrollment.Status;
        if (enrollment.CheckTransition(EnrollmentStatus.Withdrawn, command.EffectiveAt, context.RecordedAt) is { } invalid)
        {
            return invalid;
        }

        var verification = await verifier.VerifyAsync(
            new AttestationRequirement(context.TenantId, command.AttestationId, AttestationMeaning.Withdrawn, enrollment.SignableTarget, context.Actor),
            cancellationToken).ConfigureAwait(false);
        if (!verification.Satisfied)
        {
            return ExecutionErrors.AttestationRejected.WithMessage(verification.Reason ?? ExecutionErrors.AttestationRejected.Message);
        }

        if (enrollment.Withdraw(command.Kind, command.AttestationId, command.EffectiveAt, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(
            new ParticipantWithdrawn(
                enrollment.TenantId, enrollment.Id, enrollment.ParticipantId, from, command.Kind, command.AttestationId, enrollment.StatusSince),
            enrollment.StatusSince);
        return EnrollmentStates.Of(enrollment);
    }
}

/// <summary>Shared steps of the enrollment handlers.</summary>
internal static class EnrollmentStates
{
    public static CommandResult<EnrollmentState> Of(Enrollment enrollment) =>
        CommandResult<EnrollmentState>.Success(new EnrollmentState(
            enrollment.Id,
            enrollment.ParticipantId,
            enrollment.StudyId,
            enrollment.StudySiteId,
            enrollment.SubjectCode,
            enrollment.Status,
            enrollment.StatusSince,
            enrollment.EnrollmentNumber,
            enrollment.ContentHash));
}

/// <summary>Registers the participant and enrollment command handlers (spec §7.4).</summary>
public static class ExecutionServiceCollectionExtensions
{
    /// <summary>
    /// Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>. The handlers need an
    /// <see cref="IEnrollmentDirectory"/> and an <see cref="IAttestationVerifier"/>, which <c>AddKernelPersistence</c> registers.
    /// </summary>
    public static IServiceCollection AddKernelExecutionHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<RegisterParticipant, ParticipantId>, RegisterParticipantHandler>();
        services.AddTransient<ICommandHandler<ScreenParticipant, EnrollmentState>, ScreenParticipantHandler>();
        services.AddTransient<ICommandHandler<ChangeEnrollmentStatus, EnrollmentState>, ChangeEnrollmentStatusHandler>();
        services.AddTransient<ICommandHandler<WithdrawParticipant, EnrollmentState>, WithdrawParticipantHandler>();
        return services;
    }
}
