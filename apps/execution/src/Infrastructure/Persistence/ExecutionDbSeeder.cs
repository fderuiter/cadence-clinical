using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// Native C# DbContext seeder for Execution microservice.
/// Populates GxP demo reference data and initial entities strictly inside <see cref="DemoTenantCode"/>
/// (<c>cadence-local</c>) so customer and self-serve trial tenants never observe seeded demo participants or compliance
/// rows. Writes no audit entries: every audit chain entry must come from the audit writer or interceptor so its hashes verify.
/// @req:PRD-SUB-001
/// @req:PRD-SYS-192
/// </summary>
public static class ExecutionDbSeeder
{
    public const string DemoTenantCode = "cadence-local";

    public static Task SeedAsync(ExecutionDbContext dbContext, CancellationToken ct = default) =>
        SeedAsync(dbContext, tenantScope: null, ct);

    public static async Task SeedAsync(ExecutionDbContext dbContext, ITenantScope? tenantScope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        using IDisposable? bound = tenantScope?.Bind(DemoTenantCode);

        Guid demoTenantId;
        if (dbContext.ActiveBoundTenantId is { } resolvedTenantId)
        {
            demoTenantId = resolvedTenantId;
        }
        else if (!dbContext.Database.IsRelational())
        {
            demoTenantId = DeterministicTenantCodeResolver.ComputeDeterministicGuid(DemoTenantCode);
        }
        else
        {
            // Relational database without an active `cadence-local` tenant in kernel.tenants: skip demo seeding cleanly.
            return;
        }

        using IDisposable? rebound = bound is null ? tenantScope?.Bind(demoTenantId) : null;

        // 1. Seed Site Compliance Caches for the demo tenant only.
        if (!await dbContext.SiteComplianceCaches.AnyAsync(c => c.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var site101Activation = SiteComplianceCache.Create(
                new StudyCode("CADENCE-101"),
                new SiteCode("SITE-101"),
                "SITE_ACTIVATION",
                isComplete: true,
                missingDocuments: Array.Empty<string>()
            );
            site101Activation.MarkActivated(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "system_seed");

            var initialCaches = new[]
            {
                site101Activation,
                SiteComplianceCache.Create(
                    new StudyCode("CADENCE-101"),
                    new SiteCode("SITE-102"),
                    "CONDUCT",
                    isComplete: true,
                    missingDocuments: Array.Empty<string>()
                )
            };

            foreach (var cache in initialCaches)
            {
                cache.TenantId = demoTenantId;
            }

            await dbContext.SiteComplianceCaches.AddRangeAsync(initialCaches, ct).ConfigureAwait(false);
        }

        // 2. Seed Initial Clinical Subject for the demo tenant only.
        if (!await dbContext.ClinicalSubjects.AnyAsync(s => s.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var initialSubject = ClinicalSubject.Create(
                subjectCode: new SubjectCode("SUBJ-101-001"),
                studyCode: new StudyCode("CADENCE-101"),
                siteCode: new SiteCode("SITE-101"),
                enrollmentIndex: 0,
                encryptedDemographics: null,
                stratFactors: null
            );
            initialSubject.TenantId = demoTenantId;

            await dbContext.ClinicalSubjects.AddAsync(initialSubject, ct).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
