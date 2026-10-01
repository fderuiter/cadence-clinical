using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Locks;

/// <summary>
/// Application service interface managing 6-tier data lock lifecycle and pre-lock discrepancy enforcement.
/// </summary>
public interface IDataLockApplicationService
{
    Task<Result<PreflightCheckResponse>> PreflightCheckAsync(PreflightCheckRequest request, CancellationToken ct = default);

    Task<Result<DataLockRecordResponse>> LockAsync(DataLockExecuteRequest request, string callerUserId, CancellationToken ct = default);

    Task<Result<DataLockRecordResponse>> UnlockAsync(DataLockUnlockRequest request, string callerUserId, CancellationToken ct = default);

    Task<IReadOnlyList<DataLockRecordResponse>> ListLocksAsync(string? studyId = null, string? scopeType = null, string? scopeId = null, CancellationToken ct = default);
}
