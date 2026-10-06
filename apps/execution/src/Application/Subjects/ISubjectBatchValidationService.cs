using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Subjects;

public interface ISubjectBatchValidationService
{
    Task ValidateBatchAsync(Guid batchId, CancellationToken ct);
}
