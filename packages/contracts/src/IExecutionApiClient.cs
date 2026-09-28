using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Contracts;

public record SubjectSummaryDto(string Id, string SiteId, string SubjectIdentifier, string Status, long VersionIndex);
public record ObservationSummaryDto(string Id, string SubjectId, string Code, string Value, long VersionIndex);

/// <summary>
/// Strongly-typed REST client interface for the Cadence Execution Engine (EDC).
/// @req:PRD-SYS-053
/// </summary>
public interface IExecutionApiClient
{
    Task<Result<SubjectSummaryDto>> GetSubjectAsync(string subjectId, CancellationToken ct = default);
    Task<Result<ObservationSummaryDto>> RecordObservationAsync(string subjectId, string code, string value, CancellationToken ct = default);
}

