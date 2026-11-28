using System;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Modules.Execution.Endpoints;

internal readonly record struct ParsedQueryFilters(
    FormQueryStatus? Status,
    FormQueryOwnerRole? OwnerRole,
    FormQueryOrigin? Origin,
    FormQueryPriority? Priority,
    string? Assignee);

internal sealed record EnrollmentHeader(Guid StudyId, Guid? StudySiteId, string? SiteCode, string? SubjectCode);

internal static class FormQueryFilterParser
{
    public static bool TryParseFilters(FormQueryListFilter filter, out ParsedQueryFilters parsed, out string? error)
    {
        FormQueryStatus? status = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<FormQueryStatus>(filter.Status.Trim(), ignoreCase: true, out var s) || !Enum.IsDefined(s))
            {
                parsed = default;
                error = $"'{filter.Status}' is not a query status: open, answered, closed or cancelled.";
                return false;
            }

            status = s;
        }

        if (!TryParseOwnerRole(filter.Owner, out var ownerRole))
        {
            parsed = default;
            error = $"'{filter.Owner}' is not an owner role: site, monitor or data_manager.";
            return false;
        }

        FormQueryOrigin? origin = null;
        if (!string.IsNullOrWhiteSpace(filter.Origin))
        {
            if (!Enum.TryParse<FormQueryOrigin>(filter.Origin.Trim(), ignoreCase: true, out var o) || !Enum.IsDefined(o))
            {
                parsed = default;
                error = $"'{filter.Origin}' is not a query origin: manual or system.";
                return false;
            }

            origin = o;
        }

        if (!TryParsePriority(filter.Priority, out var priority))
        {
            parsed = default;
            error = $"'{filter.Priority}' is not a query priority: low, normal or high.";
            return false;
        }

        string? assignee = null;
        if (!string.IsNullOrWhiteSpace(filter.Assignee))
        {
            var trimmedAssignee = filter.Assignee.Trim();
            if (trimmedAssignee.Length > FormQuery.AssigneeMaxLength)
            {
                parsed = default;
                error = $"assignee may be at most {FormQuery.AssigneeMaxLength} characters.";
                return false;
            }

            assignee = trimmedAssignee;
        }

        parsed = new ParsedQueryFilters(status, ownerRole, origin, priority, assignee);
        error = null;
        return true;
    }

    public static bool TryParseOwnerRole(string? raw, out FormQueryOwnerRole? role)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            role = null;
            return true;
        }

        var normalized = raw.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<FormQueryOwnerRole>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            role = parsed;
            return true;
        }

        role = null;
        return false;
    }

    public static bool TryParsePriority(string? raw, out FormQueryPriority? priority)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            priority = null;
            return true;
        }

        if (Enum.TryParse<FormQueryPriority>(raw.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            priority = parsed;
            return true;
        }

        priority = null;
        return false;
    }

    public static bool MatchesSiteFilter(EnrollmentHeader header, string? rawSite)
    {
        if (string.IsNullOrWhiteSpace(rawSite))
        {
            return true;
        }

        var site = rawSite.Trim();
        return string.Equals(header.SiteCode, site, StringComparison.OrdinalIgnoreCase)
            || (header.StudySiteId is { } siteId && string.Equals(siteId.ToString("D"), site, StringComparison.OrdinalIgnoreCase));
    }

    public static bool MatchesParticipantFilter(EnrollmentId enrollmentId, EnrollmentHeader header, string? rawParticipant)
    {
        if (string.IsNullOrWhiteSpace(rawParticipant))
        {
            return true;
        }

        var participant = rawParticipant.Trim();
        return (header.SubjectCode is not null && header.SubjectCode.Contains(participant, StringComparison.OrdinalIgnoreCase))
            || string.Equals(enrollmentId.Value.ToString("D"), participant, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesAssigneeFilter(string? queryAssignee, string filterAssignee)
    {
        if (string.IsNullOrWhiteSpace(queryAssignee))
        {
            return false;
        }

        if (string.Equals(queryAssignee, filterAssignee, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedQuery = queryAssignee.StartsWith("u:", StringComparison.OrdinalIgnoreCase) ? queryAssignee[2..] : queryAssignee;
        var normalizedFilter = filterAssignee.StartsWith("u:", StringComparison.OrdinalIgnoreCase) ? filterAssignee[2..] : filterAssignee;
        return string.Equals(normalizedQuery, normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    public static ProblemHttpResult BadRequest(string detail) =>
        TypedResults.Problem(title: "Invalid request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    public static ProblemHttpResult FormNotFound(Guid formId) =>
        TypedResults.Problem(title: "Not found", detail: $"Form {formId:D} does not exist.", statusCode: StatusCodes.Status404NotFound);

    public static ProblemHttpResult NotVisible(string what) =>
        TypedResults.Problem(title: "Not visible", detail: $"You may not see {what}.", statusCode: StatusCodes.Status403Forbidden);
}

/// <summary>Bitemporal query-string parameters for point-in-time query reads.</summary>
internal sealed record FormQueryTemporalFilter(
    [FromQuery(Name = "as_of")] DateTimeOffset? AsOf = null,
    [FromQuery(Name = "valid_at")] DateTimeOffset? ValidAt = null,
    [FromQuery(Name = "known_at")] DateTimeOffset? KnownAt = null)
{
    public bool HasCoordinate => AsOf.HasValue || ValidAt.HasValue || KnownAt.HasValue;
}

/// <summary>Query-string parameters for <c>GET /api/v1/execution/forms/queries</c>.</summary>
/// <param name="EnrollmentId">Lists queries for one enrollment.</param>
/// <param name="StudyId">Lists queries across visible enrollments of one study.</param>
/// <param name="Status">Optional status filter (<c>open</c>, <c>answered</c>, <c>closed</c>, <c>cancelled</c>).</param>
/// <param name="Owner">Optional owner role filter (<c>site</c>, <c>monitor</c>, <c>data_manager</c>).</param>
/// <param name="Origin">Optional query origin filter (<c>manual</c>, <c>system</c>).</param>
/// <param name="Priority">Optional query priority filter (<c>low</c>, <c>normal</c>, <c>high</c>).</param>
/// <param name="Escalated">Optional filter for whether the query has been escalated past its 14-day SLA.</param>
/// <param name="Assignee">Optional assignee identifier filter.</param>
/// <param name="Site">Optional study site code or id filter.</param>
/// <param name="Participant">Optional participant subject code or enrollment id filter.</param>
/// <param name="ValueChanged">Optional filter for whether the value changed after the query opened.</param>
/// <param name="AsOf">Optional point-in-time coordinate applied to both valid and recorded time.</param>
/// <param name="ValidAt">Optional valid-time coordinate for bitemporal query reconstruction.</param>
/// <param name="KnownAt">Optional recorded-time coordinate for bitemporal query reconstruction.</param>
/// <param name="Limit">Optional page size between 1 and 1000 (defaults to 500).</param>
/// <param name="Offset">Optional zero-based page offset (defaults to 0).</param>
internal sealed record FormQueryListFilter(
    [FromQuery(Name = "enrollment_id")] Guid? EnrollmentId = null,
    [FromQuery(Name = "study_id")] Guid? StudyId = null,
    [FromQuery(Name = "status")] string? Status = null,
    [FromQuery(Name = "owner")] string? Owner = null,
    [FromQuery(Name = "origin")] string? Origin = null,
    [FromQuery(Name = "priority")] string? Priority = null,
    [FromQuery(Name = "escalated")] bool? Escalated = null,
    [FromQuery(Name = "assignee")] string? Assignee = null,
    [FromQuery(Name = "site")] string? Site = null,
    [FromQuery(Name = "participant")] string? Participant = null,
    [FromQuery(Name = "value_changed")] bool? ValueChanged = null,
    [FromQuery(Name = "as_of")] DateTimeOffset? AsOf = null,
    [FromQuery(Name = "valid_at")] DateTimeOffset? ValidAt = null,
    [FromQuery(Name = "known_at")] DateTimeOffset? KnownAt = null,
    [FromQuery(Name = "limit")] int? Limit = null,
    [FromQuery(Name = "offset")] int? Offset = null)
{
    public FormQueryTemporalFilter Temporal => new(AsOf, ValidAt, KnownAt);

    public bool HasTemporalCoordinate => Temporal.HasCoordinate;
}

/// <summary>The services a query step needs, bound from DI as one parameter.</summary>
/// <param name="Forms">The kernel's form records, for the value's current revision.</param>
/// <param name="Contexts">The module's tenant-scoped reads.</param>
/// <param name="Sender">The kernel command path.</param>
/// <param name="ChangeReason">The signed change reason.</param>
/// <param name="Navigator">Kernel navigation, for site and subject codes.</param>
/// <param name="Access">Read-access and unblinding probe for masking responses.</param>
internal sealed record FormQueryCommandServices(
    [FromServices] ICollectionInstanceRecords Forms,
    [FromServices] IModuleTenantContextFactory<ExecutionModuleDbContext> Contexts,
    [FromServices] IKernelCommandSender Sender,
    [FromServices] IKernelChangeReason ChangeReason,
    [FromServices] IKernelNavigator Navigator,
    [FromServices] IKernelReadAccess Access);
