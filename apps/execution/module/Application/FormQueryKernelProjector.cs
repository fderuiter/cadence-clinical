using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Domain;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Synchronously projects <see cref="FormQuery"/> assertion steps into <c>kernel.assertions</c> during query command
/// execution so <c>LockReadinessEvaluator</c> and <c>KernelLockRecords</c> observe zero unmirrored query window before
/// HTTP <c>200</c> or <c>201</c> is returned.
/// </summary>
internal interface IFormQueryKernelProjector
{
    Task<KernelError?> ProjectAsync(
        TenantId tenantId,
        FormQueryId queryId,
        EnrollmentId enrollmentId,
        ObservationId observationId,
        int revision,
        FormQueryStatus status,
        string actorId,
        string reasonForChange,
        IReadOnlyList<KernelAssertionStep> steps,
        CancellationToken cancellationToken);
}

/// <summary>
/// Opens an isolated <c>svc:execution-queries</c> command scope and dispatches deterministic idempotent kernel
/// assertion commands (<see cref="RaiseQuery"/>, <see cref="AnswerQuery"/>, <see cref="RetractAssertion"/>) for each
/// <see cref="KernelAssertionStep"/>.
/// </summary>
internal sealed class FormQueryKernelProjector(IServiceScopeFactory scopes) : IFormQueryKernelProjector
{
    public static string StepIdempotencyKey(KernelAssertionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return $"execution-queries:{step.Kind}:{step.AssertionId.Value:D}";
    }

    public async Task<KernelError?> ProjectAsync(
        TenantId tenantId,
        FormQueryId queryId,
        EnrollmentId enrollmentId,
        ObservationId observationId,
        int revision,
        FormQueryStatus status,
        string actorId,
        string reasonForChange,
        IReadOnlyList<KernelAssertionStep> steps,
        CancellationToken cancellationToken)
    {
        if (steps.Count == 0)
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(reasonForChange)
            ? $"Execution query {queryId.Value:D} is {status.ToString().ToLowerInvariant()}"
            : reasonForChange;

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ModuleServiceActor>().ActAsServiceAccount(MirrorFormQueryToKernel.Name);
        var sender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var key = StepIdempotencyKey(step);
            var result = step.Kind switch
            {
                KernelAssertionStepKind.Raise => await sender.SendAsync(
                    new RaiseQuery(
                        tenantId,
                        step.AssertionId,
                        enrollmentId,
                        observationId,
                        step.Text!,
                        key,
                        reason,
                        Revision: revision,
                        AssertedBy: actorId),
                    cancellationToken: cancellationToken).ConfigureAwait(false),
                KernelAssertionStepKind.Answer => await sender.SendAsync(
                    new AnswerQuery(
                        tenantId,
                        step.AssertionId,
                        enrollmentId,
                        step.TargetId!.Value,
                        step.Text!,
                        key,
                        reason,
                        Revision: revision,
                        AssertedBy: actorId),
                    cancellationToken: cancellationToken).ConfigureAwait(false),
                _ => await sender.SendAsync(
                    new RetractAssertion(
                        tenantId,
                        step.AssertionId,
                        enrollmentId,
                        step.TargetId!.Value,
                        key,
                        reason,
                        AssertedBy: actorId),
                    cancellationToken: cancellationToken).ConfigureAwait(false),
            };

            if (result.Error is { } error && error.Code != KernelErrors.AlreadyExists.Code)
            {
                return error;
            }
        }

        return null;
    }
}
