using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Cadence.Core.Domain;

/// <summary>
/// Categorization of domain errors for deterministic HTTP and regulatory mapping.
/// </summary>
public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden
}

/// <summary>
/// Strongly-typed domain error record providing machine-readable error codes and regulatory context.
/// </summary>
public record DomainError(
    string Code,
    string Description,
    ErrorType Type = ErrorType.Failure,
    IReadOnlyDictionary<string, object?>? Metadata = null)
{
    public static DomainError Validation(string code, string description, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, description, ErrorType.Validation, metadata);

    public static DomainError NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static DomainError Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    public static DomainError Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    public static DomainError Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);
}

/// <summary>
/// Strongly-typed monadic result representing either successful outcome or explicit domain error.
/// Eliminates runtime exception allocation overhead on expected clinical validation failures.
/// </summary>
public readonly record struct Result<TValue>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public TValue? Value { get; }
    public DomainError? Error { get; }

    private Result(TValue value)
    {
        IsSuccess = true;
        Value = value;
        Error = null;
    }

    private Result(DomainError error)
    {
        IsSuccess = false;
        Value = default;
        Error = error;
    }

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
        Justification = "Factory methods are the way to build a Result; callers write Result<T>.Success(value), and the implicit conversions cover the common path.")]
    public static Result<TValue> Success(TValue value) => new(value);

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
        Justification = "Factory methods are the way to build a Result; callers write Result<T>.Success(value), and the implicit conversions cover the common path.")]
    public static Result<TValue> Failure(DomainError error) => new(error);

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(DomainError error) => Failure(error);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<DomainError, TResult> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}

