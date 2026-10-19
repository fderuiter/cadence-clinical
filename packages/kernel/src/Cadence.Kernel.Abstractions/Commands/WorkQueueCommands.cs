using System.Collections.Generic;

using Cadence.Kernel.Authorization;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Defines the next version of a tenant's work queue (Canvas D10, ADR-2486): the items a named source reports as due,
/// shown to whoever may exercise <c>Capability</c> on them. The first definition of a code is version 1; defining the
/// code again adds the next version, which replaces the previous one from then on. Versions never change.
/// </summary>
/// <remarks>
/// <para><c>Code</c>: The queue's stable code, lower snake_case, for example <c>protocol_approvals</c>.</para>
/// <para><c>Capability</c>: The capability token an item needs, for example <c>protocol.approve</c>. Never a role name.</para>
/// <para><c>VerbClass</c>: The capability's verb class. For a kernel command's capability it must be the one the command
/// declares; automation agents never see Execute or Approve items, since they may not act on them (spec §11).</para>
/// <para><c>Source</c>: The registered item source, for example <c>protocol.versions_in_review</c>.</para>
/// </remarks>
[KernelCapability("work_queue.define", VerbClass.Approve)]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record DefineWorkQueue(
    TenantId TenantId,
    WorkQueueDefinitionId WorkQueueDefinitionId,
    string Code,
    string Title,
    string Capability,
    VerbClass VerbClass,
    string Source,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<WorkQueueDefinitionState>;

/// <summary>A work-queue definition version after a command.</summary>
public sealed record WorkQueueDefinitionState(
    WorkQueueDefinitionId WorkQueueDefinitionId,
    string Code,
    int Version,
    string Title,
    string Capability,
    VerbClass VerbClass,
    string Source);

/// <summary>Errors raised by the work-queue commands and reads (ADR-2486).</summary>
public static class WorkQueueErrors
{
    public static readonly KernelError CodeInvalid =
        new("K.WorkQueue.CodeInvalid", 422, "A work-queue code is 1 to 64 lower-case letters, digits or '_', starting with a letter.");

    public static readonly KernelError TitleInvalid =
        new("K.WorkQueue.TitleInvalid", 422, "A work queue needs a title of at most 200 characters.");

    public static readonly KernelError CapabilityInvalid =
        new("K.WorkQueue.CapabilityInvalid", 422, "A work queue is keyed by one capability token (resource.action) with its verb class, never a role name.");

    public static readonly KernelError SourceUnknown =
        new("K.WorkQueue.SourceUnknown", 422, "The work-item source is not registered.");

    public static readonly KernelError QueryInvalid =
        new("K.WorkQueue.QueryInvalid", 400, "A work-queue read returns 1 to 200 items per queue.");
}

/// <summary>The work-item sources the kernel itself registers (ADR-2486); applications register their own.</summary>
public static class KernelWorkItemSources
{
    /// <summary>Protocol versions submitted for review and not yet approved, returned to draft or withdrawn.</summary>
    public const string ProtocolVersionsInReview = "protocol.versions_in_review";

    /// <summary>Every source name the kernel registers.</summary>
    public static IReadOnlyList<string> All { get; } = [ProtocolVersionsInReview];
}
