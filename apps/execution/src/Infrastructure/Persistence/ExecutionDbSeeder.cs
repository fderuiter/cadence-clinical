using System;
using System.Security.Cryptography;
using System.Text;
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

            foreach (var cache in initialCaches)
            {
                cache.TenantId = demoTenantId;
            }

            await dbContext.SiteComplianceCaches.AddRangeAsync(initialCaches, ct).ConfigureAwait(false);
        }

        // 2. Seed canonical demo ClinicalSubjects matching packages/demo-fixtures/demo-seed.json and DevelopmentDemoData.DemoIds.
        if (!await dbContext.ClinicalSubjects.AnyAsync(s => s.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var initialSubjects = new[]
            {
                ClinicalSubject.Create(
                    subjectCode: new SubjectCode("SUBJ-101-001"),
                    studyCode: new StudyCode("CADENCE-101"),
                    siteCode: new SiteCode("SITE-101"),
                    enrollmentIndex: 0,
                    encryptedDemographics: null,
                    stratFactors: null,
                    id: new SubjectId(DeriveDemoGuid(demoTenantId, "participant", "SUBJ-101-001")),
                    kernelEnrollmentId: DeriveDemoGuid(demoTenantId, "enrollment", "SUBJ-101-001")
                ),
                ClinicalSubject.Create(
                    subjectCode: new SubjectCode("SUBJ-101-002"),
                    studyCode: new StudyCode("CADENCE-101"),
                    siteCode: new SiteCode("SITE-101"),
                    enrollmentIndex: 1,
                    encryptedDemographics: null,
                    stratFactors: null,
                    id: new SubjectId(DeriveDemoGuid(demoTenantId, "participant", "SUBJ-101-002")),
                    kernelEnrollmentId: DeriveDemoGuid(demoTenantId, "enrollment", "SUBJ-101-002")
                ),
                ClinicalSubject.Create(
                    subjectCode: new SubjectCode("SUBJ-102-001"),
                    studyCode: new StudyCode("CADENCE-101"),
                    siteCode: new SiteCode("SITE-102"),
                    enrollmentIndex: 2,
                    encryptedDemographics: null,
                    stratFactors: null,
                    id: new SubjectId(DeriveDemoGuid(demoTenantId, "participant", "SUBJ-102-001")),
                    kernelEnrollmentId: DeriveDemoGuid(demoTenantId, "enrollment", "SUBJ-102-001")
                ),
            };

            foreach (var subject in initialSubjects)
            {
                subject.TenantId = demoTenantId;
            }

            await dbContext.ClinicalSubjects.AddRangeAsync(initialSubjects, ct).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Derives the same UUID v8 identifier as <c>DevelopmentDemoData.DemoIds</c> and <c>DemoManifestGeneratorService</c>:
    /// SHA-256 of <c>cadence-development-demo:{tenantUuid}:{kind}:{name}</c>.
    /// </summary>
    public static Guid DeriveDemoGuid(Guid tenantId, string kind, string name)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-development-demo:{tenantId:D}:{kind}:{name}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
