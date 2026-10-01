using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Locks;

/// <summary>
/// Service interface for automated pre-lock discrepancy and source data validation.
/// </summary>
public interface IPreLockValidationEngine
{
    /// <summary>
    /// Executes automated pre-flight checks across target scope hierarchy levels.
    /// </summary>
    Task<PreflightCheckResponse> RunPreflightCheckAsync(PreflightCheckRequest request, CancellationToken ct = default);
}
