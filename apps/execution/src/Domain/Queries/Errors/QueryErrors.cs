using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Queries.Errors;

/// <summary>
/// Domain error factory for clinical discrepancy queries.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public static class QueryErrors
{
    public static DomainError NotFound(string id) =>
        DomainError.NotFound("QUERY_NOT_FOUND", $"Clinical query '{id}' was not found.");

    public static DomainError InvalidStateTransition(string from, string to) =>
        DomainError.Validation("INVALID_QUERY_TRANSITION", $"Transition from query status '{from}' to '{to}' is forbidden.");

    public static DomainError ActiveQueryAlreadyExists(string coords) =>
        DomainError.Validation("ACTIVE_QUERY_EXISTS", $"An active query already exists on target coordinates '{coords}'.")
            with
        { Type = ErrorType.Conflict };

    public static DomainError ResponseRequired() =>
        DomainError.Validation("QUERY_RESPONSE_REQUIRED", "A response string is required to answer a query.");

    public static DomainError ReasonRequired(string action) =>
        DomainError.Validation("QUERY_REASON_REQUIRED", $"A reason is required to {action} a query.");

    public static DomainError ResolverRequired() =>
        DomainError.Validation("QUERY_RESOLVER_REQUIRED", "Resolver user ID is required to close a query.");

    public static DomainError UnauthorizedRole(string role, string action) =>
        DomainError.Forbidden("UNAUTHORIZED_QUERY_ROLE", $"Role '{role}' is not authorized to {action} clinical queries. Permitted roles: CRA, DataManager.");
}
