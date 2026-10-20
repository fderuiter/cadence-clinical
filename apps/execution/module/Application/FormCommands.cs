using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Opens the Execution side of a kernel form instance (ADR-2622 §4.1): the second step of opening a form, sent by
/// <see cref="OpenFormOnCollectionOpened"/> when the kernel commits <c>CollectionInstanceOpened</c>. The form takes its
/// enrollment, protocol version and layout from the kernel's record of the instance, never from the caller.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="CollectionInstanceId">The kernel form instance; the Execution form gets the same id.</param>
/// <param name="EnrollmentId">The kernel form instance's enrollment; the command is authorized against it.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the form is opened.</param>
[KernelCapability("execution.form.open", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(typeof(FormErrors), nameof(FormErrors.CollectionInstanceUnknown))]
public sealed record OpenFormInstance(
    TenantId TenantId,
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormInstanceState>;

/// <summary>An Execution form instance after a command.</summary>
/// <param name="CollectionInstanceId">The form, the same id as its kernel form instance.</param>
/// <param name="EnrollmentId">The enrollment of the participant the form is for.</param>
/// <param name="CollectionSpecId">The CollectionSpec the form collects.</param>
/// <param name="ItemOrder">The form's layout: item ids in the order the screen shows them.</param>
/// <param name="Version">The form's version, 1 when opened.</param>
public sealed record FormInstanceState(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    string CollectionSpecId,
    IReadOnlyList<string> ItemOrder,
    long Version);

/// <summary>The errors Execution's form commands raise (ADR-2600 §4.5: module codes start <c>M.Execution.</c>).</summary>
public static class FormErrors
{
    /// <summary>The kernel has no such form instance for that enrollment.</summary>
    public static readonly KernelError CollectionInstanceUnknown =
        new("M.Execution.Form.CollectionInstanceUnknown", 404, "The kernel has no such form instance for that enrollment.");

    /// <summary>Every Execution form error, for the API's documented problem set.</summary>
    public static IReadOnlyList<KernelError> All { get; } = [CollectionInstanceUnknown];
}
