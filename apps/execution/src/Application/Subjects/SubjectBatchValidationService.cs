using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Subjects;

public sealed class SubjectBatchValidationService(
    ISubjectBatchRepository batchRepo,
    ISubjectRepository subjectRepo,
    ISiteComplianceRepository complianceRepo,
    IClock clock) : ISubjectBatchValidationService
{
    public async Task ValidateBatchAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await batchRepo.GetByIdAsync(batchId, ct).ConfigureAwait(false);
        if (batch is null || batch.Status == SubjectBatchStatus.Validated || batch.Status == SubjectBatchStatus.Committed)
        {
            return;
        }

        batch.MarkValidating();
        await batchRepo.SaveChangesAsync(ct).ConfigureAwait(false);

        int validCount = 0;
        int invalidCount = 0;

        var seenSubjectCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in batch.Rows)
        {
            var errors = new List<string>();

            // 1. Subject Identifier Check
            if (string.IsNullOrWhiteSpace(row.SubjectId))
            {
                errors.Add("Subject ID is required.");
            }
            else
            {
                string subjectCodeStr = row.SubjectId.Trim();
                if (!seenSubjectCodes.Add(subjectCodeStr))
                {
                    errors.Add($"Duplicate subject code '{subjectCodeStr}' within import batch.");
                }
                else
                {
                    var existingSubject = await subjectRepo.GetByCodeAsync(subjectCodeStr, ct).ConfigureAwait(false);
                    if (existingSubject is not null)
                    {
                        errors.Add($"Subject code '{subjectCodeStr}' already exists in study.");
                    }
                }
            }

            // 2. Site Activation Compliance Check
            if (row.SiteCode is { } siteCode)
            {
                var gate = await complianceRepo.GetAsync(batch.StudyCode, siteCode, "SITE_ACTIVATION", ct).ConfigureAwait(false);
                if (gate is not { IsComplete: true })
                {
                    errors.Add($"Site '{siteCode.Value}' is not active for study '{batch.StudyCode.Value}'.");
                }
            }

            // 3. Demographic Schema Validation
            if (!string.IsNullOrWhiteSpace(row.Birthdate)
                && !DateOnly.TryParse(row.Birthdate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && !DateTime.TryParse(row.Birthdate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add($"Invalid birthdate format '{row.Birthdate}'. Expected YYYY-MM-DD.");
            }

            if (errors.Count > 0)
            {
                row.MarkInvalid(string.Join("; ", errors));
                invalidCount++;
            }
            else
            {
                row.MarkValid();
                validCount++;
            }
        }

        batch.MarkValidationCompleted(validCount, invalidCount, clock.UtcNow);
        await batchRepo.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
