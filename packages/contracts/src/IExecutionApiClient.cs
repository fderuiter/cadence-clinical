using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Contracts;

/// <summary>A subject as another service sees it through <see cref="IExecutionApiClient"/>.</summary>
/// <param name="Id">The subject's identifier in the Execution service.</param>
/// <param name="SiteId">The identifier of the site the subject is enrolled at.</param>
/// <param name="SubjectIdentifier">The protocol subject number shown to site staff.</param>
/// <param name="Status">The subject's enrollment status.</param>
/// <param name="VersionIndex">The audited version of the subject record (21 CFR Part 11).</param>
public record SubjectSummaryDto(string Id, string SiteId, string SubjectIdentifier, string Status, long VersionIndex);

/// <summary>An observation recorded for a subject through <see cref="IExecutionApiClient"/>.</summary>
/// <param name="Id">The observation's identifier in the Execution service.</param>
/// <param name="SubjectId">The identifier of the subject the observation belongs to.</param>
/// <param name="Code">The code of the observed item.</param>
/// <param name="Value">The recorded value.</param>
/// <param name="VersionIndex">The audited version of the observation record (21 CFR Part 11).</param>
public record ObservationSummaryDto(string Id, string SubjectId, string Code, string Value, long VersionIndex);

/// <summary>
/// Strongly-typed REST client interface for the Cadence Execution Engine (EDC). No service implements or consumes it
/// yet; under ADR-2244 most cross-service calls give way to shared kernel concepts, so check that before building on it.
/// </summary>
public interface IExecutionApiClient
{
    /// <summary>Reads one subject.</summary>
    /// <param name="subjectId">The subject's identifier in the Execution service.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The subject, or a failure result when it does not exist or the call fails.</returns>
    Task<Result<SubjectSummaryDto>> GetSubjectAsync(string subjectId, CancellationToken ct = default);

    /// <summary>Records an observation for a subject.</summary>
    /// <param name="subjectId">The subject's identifier in the Execution service.</param>
    /// <param name="code">The code of the observed item.</param>
    /// <param name="value">The value to record.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The recorded observation, or a failure result.</returns>
    Task<Result<ObservationSummaryDto>> RecordObservationAsync(string subjectId, string code, string value, CancellationToken ct = default);
}

