using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// Native C# DbContext seeder for Execution microservice.
/// Populates required GxP reference data and initial entities. It writes no audit entries: every audit
/// chain entry must come from the audit writer or interceptor so its hashes verify.
/// @req:PRD-SUB-001, PRD-SYS-053
/// </summary>
public static class ExecutionDbSeeder
{
    public static async Task SeedAsync(ExecutionDbContext dbContext, CancellationToken ct = default)
    {
        // 1. Seed Site Compliance Caches
        if (!await dbContext.SiteComplianceCaches.AnyAsync(ct).ConfigureAwait(false))
        {
            var initialCaches = new[]
            {
                SiteComplianceCache.Create(
                    new StudyCode("CADENCE-101"),
                    new SiteCode("SITE-101"),
                    "SITE_ACTIVATION",
                    isComplete: true,
                    missingDocuments: Array.Empty<string>()
                ),
                SiteComplianceCache.Create(
                    new StudyCode("CADENCE-101"),
                    new SiteCode("SITE-102"),
                    "CONDUCT",
                    isComplete: true,
                    missingDocuments: Array.Empty<string>()
                )
            };

            await dbContext.SiteComplianceCaches.AddRangeAsync(initialCaches, ct).ConfigureAwait(false);
        }

        // 2. Seed Initial Clinical Subject
        if (!await dbContext.ClinicalSubjects.AnyAsync(ct).ConfigureAwait(false))
        {
            var initialSubject = ClinicalSubject.Create(
                subjectCode: new SubjectCode("SUBJ-101-001"),
                studyCode: new StudyCode("CADENCE-101"),
                siteCode: new SiteCode("SITE-101"),
                enrollmentIndex: 0,
                encryptedDemographics: null,
                stratFactors: null
            );

            await dbContext.ClinicalSubjects.AddAsync(initialSubject, ct).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
