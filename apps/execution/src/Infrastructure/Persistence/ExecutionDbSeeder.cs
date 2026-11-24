using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        // 2. Seed canonical demo ClinicalSubjects from embedded demo-seed.json matching DevelopmentDemoData.DemoIds.
        if (seed.Participants.Length > 0 &&
            !await dbContext.ClinicalSubjects.AnyAsync(s => s.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var initialSubjects = seed.Participants
                .Select((participant, index) =>
                {
                    var subject = ClinicalSubject.Create(
                        subjectCode: new SubjectCode(participant.SubjectCode),
                        studyCode: studyCode,
                        siteCode: new SiteCode(participant.SiteCode),
                        enrollmentIndex: index,
                        encryptedDemographics: null,
                        stratFactors: null,
                        id: new SubjectId(DeriveDemoGuid(demoTenantId, "participant", participant.SubjectCode)),
                        kernelEnrollmentId: DeriveDemoGuid(demoTenantId, "enrollment", participant.SubjectCode));
                    subject.TenantId = demoTenantId;
                    return subject;
                })
                .ToArray();

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
