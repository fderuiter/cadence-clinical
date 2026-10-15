using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Observations;

/// <summary>
/// Service abstraction for bulk reference range file parsing, dry-run validation, batch transactional commit, and CRUD management.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public interface ILabRangeImportService
{
    Task<BulkLabRangeDryRunResponse> DryRunAsync(BulkLabRangeDryRunRequest request, CancellationToken ct = default);

    Task<BulkLabRangeImportJobResponse> CommitImportAsync(BulkLabRangeImportCommitRequest request, string submittedBy, CancellationToken ct = default);

    Task<IReadOnlyList<LabReferenceRangeResponse>> GetReferenceRangesAsync(string studyId, string? siteId = null, CancellationToken ct = default);

    Task<LabReferenceRangeResponse?> GetReferenceRangeByIdAsync(Guid id, CancellationToken ct = default);

    Task<LabReferenceRangeResponse> CreateReferenceRangeAsync(LabReferenceRangeCreateRequest request, string createdBy, CancellationToken ct = default);

    Task<LabReferenceRangeResponse?> UpdateReferenceRangeAsync(Guid id, LabReferenceRangeUpdateRequest request, string updatedBy, CancellationToken ct = default);

    Task<bool> DeleteReferenceRangeAsync(Guid id, string updatedBy, string reasonForChange, CancellationToken ct = default);

    Task<IReadOnlyList<BulkLabRangeImportJobResponse>> GetImportJobsAsync(string studyId, CancellationToken ct = default);

    Task<BulkLabRangeImportJobResponse?> GetImportJobByIdAsync(Guid id, CancellationToken ct = default);

    UnitConversionResponse ConvertUnit(UnitConversionRequest request);

    LoincCatalogResponse GetLoincCatalog();
}
