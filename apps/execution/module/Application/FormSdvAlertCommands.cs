using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Domain;

using FluentValidation;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Records an insert-only CRA SDV modification alert when a previously <c>Verified</c> observation revision is superseded
/// by a correction (<c>PRD-QRY-006</c>).
/// </summary>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.AuthorizationDenied))]
public sealed record RecordFormSdvAlert(
    TenantId TenantId,
    Guid AlertId,
    StudyId StudyId,
    StudySiteId? StudySiteId,
    EnrollmentId EnrollmentId,
    CollectionInstanceId? FormId,
    string? ItemId,
    ObservationId ObservationId,
    int SupersededRevision,
    int NewRevision,
    string VerifiedBy,
    DateTimeOffset VerifiedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormSdvAlertState>;

/// <summary>State returned when a <see cref="FormSdvAlert"/> is recorded.</summary>
public sealed record FormSdvAlertState(
    Guid AlertId,
    StudyId StudyId,
    StudySiteId? StudySiteId,
    EnrollmentId EnrollmentId,
    CollectionInstanceId? FormId,
    string? ItemId,
    ObservationId ObservationId,
    int SupersededRevision,
    int NewRevision,
    string VerifiedBy,
    DateTimeOffset VerifiedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    string ModifiedReason)
{
    /// <summary>Projects <paramref name="alert"/> to its command state.</summary>
    public static FormSdvAlertState Of(FormSdvAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return new(
            alert.Id,
            alert.StudyId,
            alert.StudySiteId,
            alert.EnrollmentId,
            alert.FormId,
            alert.ItemId,
            alert.ObservationId,
            alert.SupersededRevision,
            alert.NewRevision,
            alert.VerifiedBy,
            alert.VerifiedAt,
            alert.ModifiedBy,
            alert.ModifiedAt,
            alert.ModifiedReason);
    }
}

/// <summary>Validates <see cref="RecordFormSdvAlert"/> inputs.</summary>
internal sealed class RecordFormSdvAlertValidator : AbstractValidator<RecordFormSdvAlert>
{
    public RecordFormSdvAlertValidator()
    {
        RuleFor(x => x.AlertId).NotEmpty();
        RuleFor(x => x.StudyId).Must(id => !id.IsEmpty);
        RuleFor(x => x.EnrollmentId).Must(id => !id.IsEmpty);
        RuleFor(x => x.ObservationId).Must(id => !id.IsEmpty);
        RuleFor(x => x.SupersededRevision).GreaterThanOrEqualTo(1);
        RuleFor(x => x.NewRevision).GreaterThan(x => x.SupersededRevision);
        RuleFor(x => x.VerifiedBy).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ModifiedBy).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ItemId).MaximumLength(FormInstance.ElementIdMaxLength).When(x => x.ItemId is not null);
    }
}

/// <summary>Handles <see cref="RecordFormSdvAlert"/> on behalf of the SDV alerts event consumer.</summary>
internal sealed class RecordFormSdvAlertHandler : ICommandHandler<RecordFormSdvAlert, FormSdvAlertState>
{
    public async Task<CommandResult<FormSdvAlertState>> HandleAsync(
        RecordFormSdvAlert command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Actor.Kind != ActorKind.ServiceAccount)
        {
            return KernelErrors.AuthorizationDenied;
        }

        if (await context.Work.ExistsAsync<FormSdvAlert>(command.AlertId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var alert = FormSdvAlert.Create(
            command.AlertId,
            context.TenantId,
            command.StudyId,
            command.StudySiteId,
            command.EnrollmentId,
            command.FormId,
            command.ItemId,
            command.ObservationId,
            command.SupersededRevision,
            command.NewRevision,
            command.VerifiedBy,
            command.VerifiedAt,
            command.ModifiedBy,
            command.ModifiedAt,
            command.ReasonForChange);
        context.Work.Add(alert);
        return CommandResult<FormSdvAlertState>.Success(FormSdvAlertState.Of(alert));
    }
}
