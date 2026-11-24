using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// Native C# DbContext seeder for Execution microservice.
/// Populates GxP demo reference data and initial entities from embedded <c>packages/demo-fixtures/demo-seed.json</c>
/// strictly inside <see cref="DemoTenantCode"/> (<c>cadence-local</c>) so customer and self-serve trial tenants never
/// observe seeded demo participants or compliance rows. Writes no audit entries: every audit chain entry must come from
/// the audit writer or interceptor so its hashes verify.
/// @req:PRD-SUB-001
/// @req:PRD-SYS-192
/// </summary>
public static partial class ExecutionDbSeeder
{
    public const string DemoTenantCode = "cadence-local";
    private const string SeedResourceName = "Cadence.Apps.Execution.demo-seed.json";
    private static readonly ExecutionDemoSeed Seed = ReadEmbeddedDemoSeed();

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
        var studyCode = new StudyCode(Seed.Study.Code);

        // 1. Seed Site Compliance Caches for the demo tenant only.
        if (!await dbContext.SiteComplianceCaches.AnyAsync(c => c.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var initialCaches = Seed.Sites
                .Select(site =>
                {
                    var cache = SiteComplianceCache.Create(
                        studyCode,
                        new SiteCode(site.Code),
                        site.ComplianceStage,
                        isComplete: true,
                        missingDocuments: Array.Empty<string>());
                    cache.TenantId = demoTenantId;
                    return cache;
                })
                .ToArray();

            await dbContext.SiteComplianceCaches.AddRangeAsync(initialCaches, ct).ConfigureAwait(false);
        }

        // 2. Seed Initial Clinical Subject for the demo tenant only.
        if (Seed.Participants.Length > 0 &&
            !await dbContext.ClinicalSubjects.AnyAsync(s => s.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var firstParticipant = Seed.Participants[0];
            var initialSubject = ClinicalSubject.Create(
                subjectCode: new SubjectCode(firstParticipant.SubjectCode),
                studyCode: studyCode,
                siteCode: new SiteCode(firstParticipant.SiteCode),
                enrollmentIndex: 0,
                encryptedDemographics: null,
                stratFactors: null
            );
            initialSubject.TenantId = demoTenantId;

            await dbContext.ClinicalSubjects.AddAsync(initialSubject, ct).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static ExecutionDemoSeed ReadEmbeddedDemoSeed()
    {
        using Stream stream = typeof(ExecutionDbSeeder).Assembly.GetManifestResourceStream(SeedResourceName)
            ?? throw new InvalidOperationException($"Missing embedded Execution demo seed '{SeedResourceName}'.");
        return JsonSerializer.Deserialize(stream, ExecutionDemoSeedJsonContext.Default.ExecutionDemoSeed)
            ?? throw new InvalidOperationException($"Null Execution demo seed payload in '{SeedResourceName}'.");
    }
}

internal sealed record ExecutionDemoSeed(
    ExecutionStudySeed Study,
    ExecutionSiteSeed[] Sites,
    ExecutionParticipantSeed[] Participants);

internal sealed record ExecutionStudySeed(string Code);

internal sealed record ExecutionSiteSeed(string Code, string ComplianceStage);

internal sealed record ExecutionParticipantSeed(string SubjectCode, string SiteCode);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExecutionDemoSeed))]
internal sealed partial class ExecutionDemoSeedJsonContext : JsonSerializerContext;
