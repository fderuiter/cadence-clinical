using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Coding;

public interface ISynonymImportService
{
    Task<PreflightValidationReportResponse> ValidatePreflightAsync(
        byte[] fileBytes,
        string fileName,
        string scope,
        string? studyId,
        CancellationToken cancellationToken = default);

    Task<BulkImportExecutionResponse> ImportSynonymRulesAsync(
        byte[] fileBytes,
        string fileName,
        string scope,
        string? studyId,
        string changeReason,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SynonymRuleResponse>> GetActiveSynonymRulesAsync(
        string? studyId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BulkSynonymImportJobResponse>> GetImportJobsAsync(
        string? studyId = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateRuleAsync(
        Guid id,
        string changeReason,
        CancellationToken cancellationToken = default);
}
