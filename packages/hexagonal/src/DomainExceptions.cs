using System;
using System.Collections.Generic;

namespace Cadence.Hexagonal;

public class DomainException : Exception
{
    public IReadOnlyDictionary<string, object?> Details { get; }

    public DomainException(string message = "Domain error occurred", IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        Details = details ?? new Dictionary<string, object?>();
    }
}

public class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string message = "Entity not found", IReadOnlyDictionary<string, object?>? details = null)
        : base(message, details) { }
}

public class EntityAlreadyExistsException : DomainException
{
    public EntityAlreadyExistsException(string message = "Entity already exists", IReadOnlyDictionary<string, object?>? details = null)
        : base(message, details) { }
}

public class ValidationException : DomainException
{
    public ValidationException(string message = "Validation error", IReadOnlyDictionary<string, object?>? details = null)
        : base(message, details) { }
}

public class DatabaseException : DomainException
{
    public DatabaseException(string message = "Database error", IReadOnlyDictionary<string, object?>? details = null)
        : base(message, details) { }
}

public class UnauthorizedActionException : DomainException
{
    public UnauthorizedActionException(string message = "Unauthorized action", IReadOnlyDictionary<string, object?>? details = null)
        : base(message, details) { }
}

public class PreconditionFailedException : DomainException
{
    public PreconditionFailedException(string message = "Precondition failed", IReadOnlyDictionary<string, object?>? details = null)
        : base(message, details) { }
}

public class ConflictException : DomainException
{
    public ConflictException(string message = "Conflict error", IReadOnlyDictionary<string, object?>? details = null)
        : base(message, details) { }
}
