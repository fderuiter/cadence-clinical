using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Observations.Errors;

/// <summary>
/// Domain error factory for clinical observations.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-001
/// </summary>
public static class ObservationErrors
{
    public static DomainError NotFound(string id) =>
        DomainError.NotFound("OBSERVATION_NOT_FOUND", $"Clinical observation '{id}' was not found.");

    public static DomainError AlreadyVerified(ObservationId id) =>
        DomainError.Conflict("OBSERVATION_ALREADY_VERIFIED", $"Clinical observation '{id}' has already been verified via SDV.");

    public static DomainError ObservationLocked(ObservationId id) =>
        DomainError.Forbidden("OBSERVATION_LOCKED", $"Clinical observation '{id}' is locked and cannot be modified.");

    public static DomainError InvalidCoordinates(string message) =>
        DomainError.Validation("INVALID_COORDINATES", message);

    public static DomainError InvalidValue(string message) =>
        DomainError.Validation("INVALID_OBSERVATION_VALUE", message);
}
