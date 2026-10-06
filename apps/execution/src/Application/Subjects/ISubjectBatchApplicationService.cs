using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

public interface ISubjectBatchApplicationService
{
    Task<Result<SubjectBatchUploadResponse>> UploadBatchAsync(
        string studyId,
        Stream csvStream,
        string fileName,
        CancellationToken ct);

    Task<Result<SubjectBatchDetailResponse>> GetBatchDetailAsync(
        string batchId,
        CancellationToken ct);

    Task<Result<SubjectBatchCommitResponse>> CommitBatchAsync(
        string batchId,
        string changeReason,
        CancellationToken ct);
}
