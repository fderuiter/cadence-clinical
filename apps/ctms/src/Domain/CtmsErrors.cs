using Cadence.Core.Domain;

namespace Cadence.Apps.Ctms.Domain;

/// <summary>
/// Domain error factories for CTMS monitoring visits and regulatory audit gates.
/// </summary>
public static class CtmsErrors
{
    public static DomainError InvalidVisitStatusTransition(MonitoringVisitStatus from, MonitoringVisitStatus to) =>
        DomainError.Validation(
            "INVALID_VISIT_STATUS_TRANSITION",
            $"Transition from {from} to {to} is forbidden.");

    public static DomainError InvalidVisitStatusTransition(string from, string to) =>
        DomainError.Validation(
            "INVALID_VISIT_STATUS_TRANSITION",
            $"Transition from {from} to {to} is forbidden.");

    public static DomainError TerminalStateModification(MonitoringVisitStatus current) =>
        DomainError.Conflict(
            "TERMINAL_STATE_MODIFICATION",
            $"Monitoring visit in terminal state '{current}' cannot be modified and is immutable.");

    public static DomainError InvalidFindingResolutionTransition(FindingResolutionStatus from, FindingResolutionStatus to) =>
        DomainError.Validation(
            "INVALID_FINDING_RESOLUTION_TRANSITION",
            $"Transition from {from} to {to} is forbidden.");

    public static DomainError VisitNotFound(string visitId) =>
        DomainError.NotFound("VISIT_NOT_FOUND", $"Monitoring visit '{visitId}' not found.");

    public static DomainError InvalidVisitStatusForLetter(MonitoringVisitStatus status) =>
        DomainError.Validation(
            "INVALID_VISIT_STATUS_FOR_LETTER",
            $"Monitoring visit must be in 'COMPLETED' or 'APPROVED' status before compiling and dispatching letters. Current status is '{status}'.");
}
