using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Modules.Execution.Domain;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// Raises a query about the current revision of one value of a form (#5461, <c>PRD-QRY-001</c>). Refused when the
/// revision is not the value's current one, when the observation is not a value of the form, or when a Locked lock covers it.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The new query's strongly-typed id, chosen by the client or the endpoint.</param>
/// <param name="FormId">The form the value belongs to.</param>
/// <param name="EnrollmentId">The form's enrollment; the command is authorized against it.</param>
/// <param name="ObservationId">The kernel observation holding the value.</param>
/// <param name="Revision">The revision questioned: the value's current one.</param>
/// <param name="Text">The question.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Verbatim reason why the query is raised.</param>
/// <param name="Origin">Whether the query was raised manually or by an automated system edit check.</param>
/// <param name="OwnerRole">Role initially responsible for acting on the query (defaults to <see cref="FormQueryOwnerRole.Site"/>).</param>
/// <param name="Priority">Initial query priority (defaults to <see cref="FormQueryPriority.Normal"/>).</param>
/// <param name="Assignee">Optional specific assignee identifier.</param>
/// <param name="ResolvedHeader">Optional pre-resolved form header passed from the HTTP endpoint to avoid re-reading it.</param>
[KernelCapability("execution.query.raise", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(typeof(FormErrors), nameof(FormErrors.CollectionInstanceUnknown))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.ObservationUnknown), nameof(FormQueryErrors.RevisionStale))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record RaiseFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    CollectionInstanceId FormId,
    EnrollmentId EnrollmentId,
    ObservationId ObservationId,
    int Revision,
    string Text,
    string IdempotencyKey,
    string ReasonForChange,
    FormQueryOrigin Origin = FormQueryOrigin.Manual,
    FormQueryOwnerRole OwnerRole = FormQueryOwnerRole.Site,
    FormQueryPriority Priority = FormQueryPriority.Normal,
    string? Assignee = null,
    [property: JsonIgnore] CollectionInstanceHeader? ResolvedHeader = null) : ICommand<FormQueryState>;

/// <summary>The site answers an open query (#5461). Refused when the query is not open or a Locked lock covers its value.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="Text">The answer.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Verbatim reason why the query is answered.</param>
/// <param name="Assignee">Optional replacement assignee for the monitor review; otherwise any stale site assignee is cleared.</param>
[KernelCapability("execution.query.answer", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record AnswerFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    EnrollmentId EnrollmentId,
    string Text,
    string IdempotencyKey,
    string ReasonForChange,
    string? Assignee = null) : ICommand<FormQueryState>;

/// <summary>
/// Automatically transitions an open manual query to <see cref="FormQueryStatus.Answered"/> and assigns ownership to
/// <see cref="FormQueryOwnerRole.Monitor"/> when a user edits/corrects the queried observation (<c>PRD-QRY-001</c>).
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The open manual query.</param>
/// <param name="EnrollmentId">The query's enrollment.</param>
/// <param name="ObservationId">The corrected observation.</param>
/// <param name="NewRevision">The new observation revision produced by the edit.</param>
/// <param name="AnsweredBy">The real acting user who edited the observation.</param>
/// <param name="IdempotencyKey">Deterministic delivery idempotency key.</param>
/// <param name="ReasonForChange">The editing user's verbatim change reason.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound), nameof(KernelErrors.AuthorizationDenied))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record AutoAnswerFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    EnrollmentId EnrollmentId,
    ObservationId ObservationId,
    int NewRevision,
    string AnsweredBy,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryState>;

/// <summary>The monitor reopens an answered or closed query with why the answer does not settle it (#5461).</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="Text">Why it is reopened, which the site answers.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Verbatim reason why the query is reopened.</param>
/// <param name="Assignee">Optional replacement assignee for the site response; otherwise any stale monitor/DM assignee is cleared.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record ReopenFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    EnrollmentId EnrollmentId,
    string Text,
    string IdempotencyKey,
    string ReasonForChange,
    string? Assignee = null) : ICommand<FormQueryState>;

/// <summary>
/// Reassigns an open or answered query's assignee (or clears it when null/empty) and optional owner role with full
/// bitemporal period and message history (<c>PRD-QRY-001</c>).
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="Assignee">New assignee identifier, or null/empty to clear the current assignee.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Verbatim reason for the reassignment.</param>
/// <param name="OwnerRole">Optional updated owner role (<c>site</c>, <c>monitor</c>, <c>data_manager</c>).</param>
/// <param name="Text">Optional note recorded in the query message thread.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record ReassignFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    EnrollmentId EnrollmentId,
    string? Assignee,
    string IdempotencyKey,
    string ReasonForChange,
    FormQueryOwnerRole? OwnerRole = null,
    string? Text = null) : ICommand<FormQueryState>;

/// <summary>
/// Closes an answered manual query (with separation of duties) or an open/answered system query (<c>PRD-QRY-001</c>).
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Verbatim change reason for the closure.</param>
/// <param name="Text">Optional closing note when distinct from <paramref name="ReasonForChange"/>.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition), nameof(FormQueryErrors.SeparationOfDuties))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record CloseFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    EnrollmentId EnrollmentId,
    string IdempotencyKey,
    string ReasonForChange,
    string? Text = null) : ICommand<FormQueryState>;

/// <summary>An open or answered query is cancelled as raised in error (#5461).</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The query.</param>
/// <param name="EnrollmentId">The query's enrollment; the command is authorized against it.</param>
/// <param name="IdempotencyKey">The client's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Verbatim change reason for the cancellation.</param>
/// <param name="Text">Optional cancellation note when distinct from <paramref name="ReasonForChange"/>.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record CancelFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    EnrollmentId EnrollmentId,
    string IdempotencyKey,
    string ReasonForChange,
    string? Text = null) : ICommand<FormQueryState>;

/// <summary>
/// Escalates an open query that has remained open past 14 calendar days on <c>IClock</c> to <see cref="FormQueryPriority.High"/>
/// (<c>PRD-QRY-002</c>).
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="QueryId">The overdue query.</param>
/// <param name="EnrollmentId">The query's enrollment.</param>
/// <param name="IdempotencyKey">The client's idempotency key.</param>
/// <param name="ReasonForChange">Why the query was escalated.</param>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(FormQueryErrors), nameof(FormQueryErrors.InvalidTransition))]
[RaisesKernelErrors(typeof(AssertionErrors), nameof(AssertionErrors.Locked))]
public sealed record EscalateFormQuery(
    TenantId TenantId,
    FormQueryId QueryId,
    EnrollmentId EnrollmentId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryState>;

/// <summary>
/// Records an insert-only daily aging digest for a study or study site (<c>PRD-QRY-002</c>).
/// </summary>
[KernelCapability("execution.query.manage", VerbClass.Execute, Resource = nameof(StudyId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
public sealed record GenerateFormQueryAgingDigest(
    TenantId TenantId,
    Guid DigestId,
    StudyId StudyId,
    StudySiteId? StudySiteId,
    string RecipientRole,
    DateOnly DigestDate,
    string StudyCode,
    string? SiteCode,
    IReadOnlyList<FormQueryAgingDigestEntry> Entries,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<FormQueryAgingDigestState>;

/// <summary>State returned when a <see cref="FormQueryAgingDigest"/> is recorded.</summary>
public sealed record FormQueryAgingDigestState(
    Guid DigestId,
    StudyId StudyId,
    StudySiteId? StudySiteId,
    string ScopeKey,
    string RecipientRole,
    DateOnly DigestDate,
    DateTimeOffset GeneratedAt,
    string StudyCode,
    string? SiteCode,
    int EscalatedCount,
    IReadOnlyList<FormQueryAgingDigestEntry> Entries);

/// <summary>Transient form scope metadata resolved during command execution so HTTP endpoints avoid re-reading the form.</summary>
public sealed record FormQueryScopeContext(
    StudyId StudyId,
    StudySiteId StudySiteId,
    string CollectionSpecId,
    string? PlannedActivityId,
    int? CurrentRevision);

/// <summary>A query after a command (or reconstructed as of a bitemporal coordinate).</summary>
public sealed record FormQueryState(
    FormQueryId QueryId,
    CollectionInstanceId FormId,
    EnrollmentId EnrollmentId,
    string ItemId,
    ObservationId ObservationId,
    int Revision,
    FormQueryStatus Status,
    FormQueryOrigin Origin,
    FormQueryOwnerRole OwnerRole,
    string? Assignee,
    FormQueryPriority Priority,
    DateTimeOffset OpenedAt,
    DateTimeOffset StatusSince,
    DateTimeOffset? DueAt,
    DateTimeOffset? EscalatedAt,
    string? LastAnsweredBy,
    IReadOnlyList<FormQueryMessage> Messages,
    long Version,
    [property: JsonIgnore] FormQueryScopeContext? Scope = null)
{
    /// <summary>The current state of <paramref name="query"/> at <paramref name="version"/>.</summary>
    public static FormQueryState Of(FormQuery query, long version, FormQueryScopeContext? scope = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new(
            query.Id,
            query.FormId,
            query.EnrollmentId,
            query.ItemId,
            query.ObservationId,
            query.Revision,
            query.Status,
            query.Origin,
            query.OwnerRole,
            query.Assignee,
            query.Priority,
            query.OpenedAt,
            query.StatusSince,
            query.DueAt,
            query.EscalatedAt,
            query.LastAnsweredBy,
            [.. query.Messages],
            version,
            scope);
    }

    /// <summary>Projects a point-in-time bitemporal snapshot of <paramref name="query"/>.</summary>
    public static FormQueryState OfSnapshot(FormQuery query, FormQueryTemporalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            query.Id,
            query.FormId,
            query.EnrollmentId,
            query.ItemId,
            query.ObservationId,
            query.Revision,
            snapshot.Status,
            query.Origin,
            snapshot.OwnerRole,
            snapshot.Assignee,
            snapshot.Priority,
            query.OpenedAt,
            snapshot.StatusSince,
            snapshot.DueAt,
            snapshot.EscalatedAt,
            snapshot.LastAnsweredBy,
            snapshot.Messages,
            query.VersionIndex);
    }
}

/// <summary>
/// A query changed (#5461, <c>PRD-QRY-001</c>): what the kernel must now assert about it, in order, together with the
/// target observation revision, real acting user, and verbatim change reason.
/// </summary>
[KernelEvent("execution.FormQueryChanged", 1)]
public sealed record FormQueryChanged(
    TenantId TenantId,
    FormQueryId QueryId,
    CollectionInstanceId FormId,
    EnrollmentId EnrollmentId,
    ObservationId ObservationId,
    int Revision,
    FormQueryStatus Status,
    FormQueryOrigin Origin,
    FormQueryOwnerRole OwnerRole,
    FormQueryPriority Priority,
    string ActorId,
    string ReasonForChange,
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

    /// <summary>Separation of duties prohibits closing an answered manual query as the same user who answered it.</summary>
    public static readonly KernelError SeparationOfDuties =
        new("M.Execution.Query.SeparationOfDuties", 409, "The user who answered a manual query cannot also close it.");

    /// <summary>Every Execution query error, for the API's documented problem set.</summary>
    public static IReadOnlyList<KernelError> All { get; } =
        [ObservationUnknown, RevisionStale, InvalidTransition, SeparationOfDuties];
}
