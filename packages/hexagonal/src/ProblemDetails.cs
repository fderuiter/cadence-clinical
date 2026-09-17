using System.Collections.Generic;

namespace Cadence.Hexagonal;

public record ProblemDetails(
    string Type = "about:blank",
    string Title = "An error occurred",
    int Status = 400,
    string Detail = "",
    string? Instance = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? InvalidParams = null,
    IReadOnlyDictionary<string, object?>? Extensions = null
);

public static class DomainErrorMapper
{
    public static int MapDomainExceptionToHttpStatus(DomainException err)
    {
        return err switch
        {
            EntityNotFoundException => 404,
            EntityAlreadyExistsException => 409,
            ValidationException => 422,
            UnauthorizedActionException => 403,
            PreconditionFailedException => 412,
            ConflictException => 409,
            DatabaseException => 500,
            _ => 400
        };
    }

    public static ProblemDetails CreateProblemDetailsFromDomainException(DomainException err, string? instance = null)
    {
        var status = MapDomainExceptionToHttpStatus(err);
        var (title, typeSlug) = err switch
        {
            EntityNotFoundException => ("Entity Not Found", "entity-not-found"),
            EntityAlreadyExistsException => ("Entity Already Exists", "entity-already-exists"),
            ValidationException => ("Validation Error", "validation-error"),
            UnauthorizedActionException => ("Unauthorized Action", "unauthorized-action"),
            PreconditionFailedException => ("Precondition Failed", "precondition-failed"),
            ConflictException => ("Conflict", "conflict"),
            DatabaseException => ("Database Error", "database-error"),
            _ => ("Domain Error", "domain-error")
        };

        return new ProblemDetails(
            Type: $"https://cadence.clinical/errors/{typeSlug}",
            Title: title,
            Status: status,
            Detail: err.Message,
            Instance: instance,
            Extensions: err.Details
        );
    }
}
