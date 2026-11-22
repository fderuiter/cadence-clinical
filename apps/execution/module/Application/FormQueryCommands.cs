using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Raises a query about the current revision of one value of a form, or about a blank item (#5461). Refused when the
/// revision is not the value's current one, when the observation or item does not belong to the form, or when a Locked
/// lock covers it.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The new query's id, chosen by the client or the endpoint.</param>
/// <param name="FormId">The form the value belongs to.</param>
/// <param name="EnrollmentId">The form's enrollment; the command is authorized against it.</param>
/// <param name="ItemId">The CollectionSpec item whose value or absence is queried.</param>
/// <param name="ObservationId">The kernel observation holding the value, or null when querying a blank item.</param>
/// <param name="Revision">The revision questioned: the value's current one, or 0 for a blank item.</param>
/// <param name="Text">The question.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the query is raised.</param>
[KernelCapability("execution.query.raise", VerbClass.Execute, Resource = nameof(EnrollmentId), OfferedOn = [NodeKinds.CollectionInstance, NodeKinds.Observation])]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(typeof(FormErrors), nameof(FormErrors.CollectionInstanceUnknown))]
[RaisesKernelErrors(typeof(CollectionErrors), nameof(CollectionErrors.ValuesInvalid))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.ObservationUnknown), nameof(FormQueryErrors.RevisionStale))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record RaiseFormQuery(
    TenantId TenantId,
    Guid QueryId,
    CollectionInstanceId FormId,
    EnrollmentId EnrollmentId,
    string ItemId,
    ObservationId? ObservationId,
    int Revision,
    string Text,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryState>;

/// <summary>The site answers an open query (#5461). Refused when the query is not open or a Locked lock covers its value.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="Text">The answer.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the query is answered.</param>
[KernelCapability("execution.query.answer", VerbClass.Execute, Resource = nameof(EnrollmentId), OfferedOn = [NodeKinds.CollectionInstance, NodeKinds.Observation])]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record AnswerFormQuery(
    TenantId TenantId,
    Guid QueryId,
    EnrollmentId EnrollmentId,
    string Text,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryState>;

/// <summary>The monitor reopens an answered or closed query with why the answer does not settle it (#5461).</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="Text">Why it is reopened, which the site answers.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the query is reopened.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId), OfferedOn = [NodeKinds.CollectionInstance, NodeKinds.Observation])]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record ReopenFormQuery(
    TenantId TenantId,
    Guid QueryId,
    EnrollmentId EnrollmentId,
    string Text,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryState>;

/// <summary>
/// The monitor closes an open or answered query (#5461); the reason for change is the closing note. Refused when a
/// Locked lock covers the value.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the query is closed.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId), OfferedOn = [NodeKinds.CollectionInstance, NodeKinds.Observation])]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record CloseFormQuery(
    TenantId TenantId,
    Guid QueryId,
    EnrollmentId EnrollmentId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryState>;

/// <summary>An open or answered query is cancelled as raised in error (#5461); the reason for change says why.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the query is cancelled.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId), OfferedOn = [NodeKinds.CollectionInstance, NodeKinds.Observation])]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record CancelFormQuery(
    TenantId TenantId,
    Guid QueryId,
    EnrollmentId EnrollmentId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryState>;

/// <summary>A query after a command.</summary>
/// <param name="QueryId">The query.</param>
/// <param name="FormId">The form the queried value belongs to.</param>
/// <param name="EnrollmentId">The form's enrollment.</param>
/// <param name="ItemId">The item whose value is queried.</param>
/// <param name="ObservationId">The kernel observation holding the value, or null for a blank-item query.</param>
/// <param name="Revision">The revision questioned, or 0 for a blank-item query.</param>
/// <param name="Status">Where the query is in its workflow.</param>
/// <param name="Messages">What happened to it, oldest first.</param>
/// <param name="Version">The query's version, 1 when raised.</param>
public sealed record FormQueryState(
    Guid QueryId,
    CollectionInstanceId FormId,
    EnrollmentId EnrollmentId,
    string ItemId,
    ObservationId? ObservationId,
    int Revision,
    FormQueryStatus Status,
    IReadOnlyList<FormQueryMessage> Messages,
    long Version)
{
    /// <summary>The state of <paramref name="query"/> at <paramref name="version"/>.</summary>
    public static FormQueryState Of(FormQuery query, long version)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new(query.Id, query.FormId, query.EnrollmentId, query.ItemId, query.ObservationId, query.Revision, query.Status, [.. query.Messages], version);
    }
}

/// <summary>
/// A query changed (#5461): what the kernel must now assert about it, in order. The consumer that mirrors queries to
/// kernel assertions sends one kernel command per step when the query targets a recorded observation.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="FormId">The form the queried value belongs to.</param>
/// <param name="EnrollmentId">The form's enrollment.</param>
/// <param name="ObservationId">The kernel observation the query is about, or null for a blank-item query.</param>
/// <param name="Status">The query's status after the change.</param>
/// <param name="Steps">The kernel assertion commands to send, in order.</param>
[KernelEvent("execution.FormQueryChanged", 1)]
public sealed record FormQueryChanged(
    TenantId TenantId,
    Guid QueryId,
    CollectionInstanceId FormId,
    EnrollmentId EnrollmentId,
    ObservationId? ObservationId,
    FormQueryStatus Status,
    IReadOnlyList<KernelAssertionStep> Steps);

/// <summary>The errors Execution's query commands raise (ADR-2600 §4.5).</summary>
public static class FormQueryErrors
{
    /// <summary>The observation is not a value of the form.</summary>
    public static readonly KernelError ObservationUnknown =
        new("M.Execution.Query.ObservationUnknown", 404, "The observation is not a value of that form.");

    /// <summary>The revision is not the value's current one.</summary>
    public static readonly KernelError RevisionStale =
        new("M.Execution.Query.RevisionStale", 409, "The value changed since it was read; reload the form and query the current value.");

    /// <summary>The query's status does not allow the step.</summary>
    public static readonly KernelError InvalidTransition =
        new("M.Execution.Query.InvalidTransition", 409, "The query's status does not allow that step.");

    /// <summary>Every Execution query error, for the API's documented problem set.</summary>
    public static IReadOnlyList<KernelError> All { get; } = [ObservationUnknown, RevisionStale, InvalidTransition];
}
