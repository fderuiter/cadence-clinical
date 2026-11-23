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
/// Loads the canonical embedded <c>demo-seed.json</c> via source-generated <see cref="System.Text.Json"/> and populates
/// GxP demo reference data strictly inside <see cref="DemoTenantCode"/> (<c>cadence-local</c>) so customer and
/// self-serve trial tenants never observe seeded demo participants or compliance rows. Writes no audit entries:
/// every audit chain entry must come from the audit writer or interceptor so its hashes verify.
/// @req:PRD-SUB-001
/// @req:PRD-SYS-192
/// </summary>
public static class ExecutionDbSeeder
{
    public const string DemoTenantCode = "cadence-local";

    private const string DemoSeedResourceName = "Cadence.Apps.Execution.demo-seed.json";

    private static readonly Lazy<ExecutionDemoSeed> Seed = new(LoadSeed);

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

        var seed = Seed.Value;
        var studyCode = new StudyCode(seed.Study.Code);

        // 1. Seed Site Compliance Caches for the demo tenant only from canonical demo-seed.json.
        if (!await dbContext.SiteComplianceCaches.AnyAsync(c => c.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var initialCaches = seed.Sites
                .Select(site =>
                {
                    var cache = SiteComplianceCache.Create(
                        studyCode,
                        new SiteCode(site.Code),
                        site.ComplianceStage,
                        isComplete: true,
                        missingDocuments: []);
                    cache.TenantId = demoTenantId;
                    return cache;
                })
                .ToArray();

            await dbContext.SiteComplianceCaches.AddRangeAsync(initialCaches, ct).ConfigureAwait(false);
        }

        // 2. Seed Initial Clinical Subject for the demo tenant only from canonical demo-seed.json.
        if (seed.Participants.Length > 0 &&
            !await dbContext.ClinicalSubjects.AnyAsync(s => s.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var firstParticipant = seed.Participants[0];
            var initialSubject = ClinicalSubject.Create(
                subjectCode: new SubjectCode(firstParticipant.SubjectCode),
                studyCode: studyCode,
                siteCode: new SiteCode(firstParticipant.SiteCode),
                enrollmentIndex: 0,
                encryptedDemographics: null,
                stratFactors: null);
            initialSubject.TenantId = demoTenantId;

            await dbContext.ClinicalSubjects.AddAsync(initialSubject, ct).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static ExecutionDemoSeed LoadSeed()
    {
        using Stream stream = typeof(ExecutionDbSeeder).Assembly.GetManifestResourceStream(DemoSeedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DemoSeedResourceName}' was not found.");
        return JsonSerializer.Deserialize(stream, ExecutionDemoSeedJsonContext.Default.ExecutionDemoSeed)
            ?? throw new InvalidOperationException($"Embedded resource '{DemoSeedResourceName}' deserialized to null.");
    }
}

internal sealed record ExecutionDemoSeed(
    string TenantCode,
    ExecutionDemoStudyEntry Study,
    ExecutionDemoSiteEntry[] Sites,
    ExecutionDemoParticipantEntry[] Participants);

internal sealed record ExecutionDemoStudyEntry(string Code);

internal sealed record ExecutionDemoSiteEntry(string Code, string ComplianceStage);

internal sealed record ExecutionDemoParticipantEntry(string SubjectCode, string SiteCode, string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExecutionDemoSeed))]
internal sealed partial class ExecutionDemoSeedJsonContext : JsonSerializerContext;
