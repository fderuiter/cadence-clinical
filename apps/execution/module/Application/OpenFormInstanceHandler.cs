using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Modules.Execution.Domain;

using FluentValidation;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Opens the Execution side of a kernel form instance from the kernel's record of it: its enrollment, participant,
/// study, governing protocol version and the specification's item order become the form's own. The kernel record is
/// committed before <c>CollectionInstanceOpened</c> is delivered, so it is read outside the command's transaction.
/// </summary>
internal sealed class OpenFormInstanceHandler(
    ICollectionInstanceRecords records,
    IProtocolVersionReader? versions = null) : ICommandHandler<OpenFormInstance, FormInstanceState>
{
    public async Task<CommandResult<FormInstanceState>> HandleAsync(
        OpenFormInstance command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<FormInstance>(command.CollectionInstanceId.Value, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var record = await records.FindAsync(context.TenantId, command.CollectionInstanceId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.EnrollmentId != command.EnrollmentId)
        {
            return FormErrors.CollectionInstanceUnknown;
        }

        var form = FormInstance.Open(
            record.CollectionInstanceId,
            context.TenantId,
            record.EnrollmentId,
            record.ParticipantId,
            record.StudyId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.CollectedAt,
            [.. record.Items.Select(i => i.Id)]);
        context.Work.Add(form);

        if (record.PlannedActivityId is { Length: > 0 } activityId)
        {
            var assessment = await VisitTimingEvaluator.AssessAsync(
                context.TenantId,
                record.ProtocolVersionId,
                activityId,
                record.CollectedAt,
                record.EnrollmentReferenceAt,
                versions,
                cancellationToken).ConfigureAwait(false);
            if (assessment is { Timing: VisitTiming.Early or VisitTiming.Late })
            {
                context.Work.Raise(
                    new VisitWindowDeviationDetected(
                        context.TenantId,
                        record.CollectionInstanceId,
                        record.EnrollmentId,
                        record.StudyId,
                        record.StudySiteId,
                        record.ProtocolVersionId,
                        record.CollectionSpecId,
                        activityId,
                        VisitTimingEvaluator.VisitWindowCategory,
                        assessment.Timing.ToString(),
                        assessment.Expected.Target,
                        assessment.Expected.WindowStart,
                        assessment.Expected.WindowEnd,
                        assessment.Actual,
                        assessment.DaysFromTarget,
                        assessment.DaysOutsideWindow,
                        record.CollectedAt),
                    record.CollectedAt);
            }
        }

        return CommandResult<FormInstanceState>.Success(
            new FormInstanceState(form.CollectionInstanceId, form.EnrollmentId, form.CollectionSpecId, form.ItemOrder, 1));
    }
}

/// <summary>Checks the shape of <see cref="OpenFormInstance"/>: it names a kernel form instance and its enrollment.</summary>
internal sealed class OpenFormInstanceValidator : AbstractValidator<OpenFormInstance>
{
    public OpenFormInstanceValidator()
    {
        RuleFor(c => c.CollectionInstanceId).Must(id => !id.IsEmpty).WithMessage("Name the kernel form instance.");
        RuleFor(c => c.EnrollmentId).Must(id => !id.IsEmpty).WithMessage("Name the form instance's enrollment.");
    }
}
