using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    private const string DemoStudyId = "CADENCE-101";
    private const string DemoDraftReason = "Seed baseline CADENCE-101 randomization scheme for local demo workspace.";
    private const string DemoApproveReason = "Approve baseline CADENCE-101 randomization scheme for local demo workspace.";
    private const string DemoKitSeedReason = "Seed baseline CADENCE-101 site IP kit inventory for local demo workspace.";

    private static readonly ExecutionDemoSeed Seed = ReadEmbeddedDemoSeed();
    private static readonly DateTimeOffset DemoSeedTimestamp = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero); // deid-ignore
    private static readonly DateTimeOffset DemoSeedApprovalTimestamp = new(2026, 1, 5, 9, 15, 0, TimeSpan.Zero); // deid-ignore
    private static readonly DateTimeOffset DemoKitExpiration = new(2030, 12, 31, 0, 0, 0, TimeSpan.Zero); // deid-ignore

    private static readonly (string SiteId, string KitNumber, string Arm)[] DemoKitDefinitions =
    [
        ("SITE-101", "KIT-1001", "PLACEBO"),
        ("SITE-101", "KIT-1002", "ACTIVE"),
        ("SITE-101", "KIT-1003", "PLACEBO"),
        ("SITE-101", "KIT-1004", "ACTIVE"),
        ("SITE-102", "KIT-2001", "PLACEBO"),
        ("SITE-102", "KIT-2002", "ACTIVE"),
        ("SITE-102", "KIT-2003", "PLACEBO"),
        ("SITE-102", "KIT-2004", "ACTIVE"),
    ];

    private static readonly string[] DemoSlotArms =
    [
        "PLACEBO",
        "ACTIVE",
        "ACTIVE",
        "PLACEBO",
        "ACTIVE",
        "PLACEBO",
        "PLACEBO",
        "ACTIVE",
    ];

    public static Task SeedAsync(ExecutionDbContext dbContext, CancellationToken ct = default) =>
        SeedAsync(dbContext, tenantScope: null, allocationProtector: null, ct);

    public static Task SeedAsync(ExecutionDbContext dbContext, ITenantScope? tenantScope, CancellationToken ct = default) =>
        SeedAsync(dbContext, tenantScope, allocationProtector: null, ct);

    public static async Task SeedAsync(
        ExecutionDbContext dbContext,
        ITenantScope? tenantScope,
        IAllocationProtector? allocationProtector,
        CancellationToken ct = default)
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

        // 2. Seed canonical demo ClinicalSubjects matching packages/demo-fixtures/demo-seed.json and DevelopmentDemoData.DemoIds.
        if (Seed.Participants.Length > 0 &&
            !await dbContext.ClinicalSubjects.AnyAsync(s => s.TenantId == demoTenantId, ct).ConfigureAwait(false))
        {
            var initialSubjects = Seed.Participants
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

        var protector = ResolveAllocationProtector(dbContext, allocationProtector);
        var demoStudyCode = new StudyCode(DemoStudyId);

        // 3. Seed approved baseline randomization scheme and encrypted list slots for CADENCE-101 in cadence-local only.
        bool hasDemoScheme = await dbContext.RandomizationSchemes
            .AnyAsync(
                s => EF.Property<Guid>(s, StandaloneTenantModelExtensions.TenantPropertyName) == demoTenantId
                    && s.StudyCode == demoStudyCode,
                ct)
            .ConfigureAwait(false);

        if (!hasDemoScheme)
        {
            var draftResult = RandomizationScheme.Draft(
                studyCode: demoStudyCode,
                version: 1,
                method: RandomizationMethod.PermutedBlock,
                arms: new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["PLACEBO"] = 1,
                    ["ACTIVE"] = 1,
                },
                stratificationFactors: null,
                blockSizes: [4],
                slotsPerStratum: DemoSlotArms.Length,
                preferredArmProbability: 0,
                requiresKit: true,
                draftedBy: "stat.author",
                reasonForChange: DemoDraftReason,
                clock: new FixedDemoClock(DemoSeedTimestamp));

            var scheme = draftResult.Value
                ?? throw new InvalidOperationException($"Failed to build baseline demo randomization scheme: {draftResult.Error?.Description}");

            var approveResult = scheme.Approve("stat.approver", DemoApproveReason, new FixedDemoClock(DemoSeedApprovalTimestamp));
            if (approveResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to approve baseline demo randomization scheme: {approveResult.Error?.Description}");
            }

            await dbContext.RandomizationSchemes.AddAsync(scheme, ct).ConfigureAwait(false);
            dbContext.Entry(scheme).Property<Guid>(StandaloneTenantModelExtensions.TenantPropertyName).CurrentValue = demoTenantId;

            var slotPlaintexts = DemoSlotArms
                .Select((arm, idx) => (
                    Arm: arm,
                    Purpose: RandomizationListSlot.ArmPurpose(scheme.Id, RandomizationScheme.DefaultStratum, idx + 1)))
                .ToArray();
            var slotCiphertexts = protector.ProtectBatch(slotPlaintexts);

            for (int idx = 0; idx < DemoSlotArms.Length; idx++)
            {
                var slot = RandomizationListSlot.Create(
                    scheme.Id,
                    RandomizationScheme.DefaultStratum,
                    idx + 1,
                    slotCiphertexts[idx]);
                await dbContext.RandomizationListSlots.AddAsync(slot, ct).ConfigureAwait(false);
                dbContext.Entry(slot).Property<Guid>(StandaloneTenantModelExtensions.TenantPropertyName).CurrentValue = demoTenantId;
            }
        }

        // 4. Seed ACTIVE_AT_SITE IP kit inventory for CADENCE-101 in cadence-local only.
        var existingKitNumbers = await dbContext.IpKitInventoryItems
            .Where(k => EF.Property<Guid>(k, StandaloneTenantModelExtensions.TenantPropertyName) == demoTenantId
                && k.StudyId == DemoStudyId)
            .Select(k => k.KitNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existingKitSet = existingKitNumbers.ToHashSet(StringComparer.Ordinal);

        foreach (var (siteId, kitNumber, arm) in DemoKitDefinitions)
        {
            if (existingKitSet.Contains(kitNumber))
            {
                continue;
            }

            var kit = new IpKitInventoryItem(
                id: Guid.NewGuid(),
                kitNumber: kitNumber,
                studyId: DemoStudyId,
                siteId: siteId,
                lotNumber: "LOT-2026A",
                expirationDate: DemoKitExpiration,
                status: IpKitInventoryItem.ActiveAtSiteStatus,
                createdBy: "svc:execution-db-seeder",
                reasonForChange: DemoKitSeedReason);
            kit.LabelTreatment(protector.Protect(arm, IpKitInventoryItem.ArmPurpose(DemoStudyId, kitNumber)));

            await dbContext.IpKitInventoryItems.AddAsync(kit, ct).ConfigureAwait(false);
            dbContext.Entry(kit).Property<Guid>(StandaloneTenantModelExtensions.TenantPropertyName).CurrentValue = demoTenantId;
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static IAllocationProtector ResolveAllocationProtector(
        ExecutionDbContext dbContext,
        IAllocationProtector? explicitProtector)
    {
        if (explicitProtector is not null)
        {
            return explicitProtector;
        }

        var options = ((IInfrastructure<IServiceProvider>)dbContext).Instance.GetService<IDbContextOptions>();
        var appProvider = options?.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        if (appProvider?.GetService<IAllocationProtector>() is { } registeredProtector)
        {
            return registeredProtector;
        }

        if (!dbContext.Database.IsRelational())
        {
            string ephemeralInMemoryKey = Convert.ToBase64String(SHA256.HashData("cadence-local-inmemory-demo-seeder-key"u8));
            return new AesGcmAllocationProtector(Options.Create(new AllocationProtectionOptions
            {
                KeyBase64 = ephemeralInMemoryKey,
            }));
        }

        throw new InvalidOperationException(
            "ExecutionDbSeeder requires an IAllocationProtector registered in DI or supplied explicitly for relational seeding.");
    }

    private sealed class FixedDemoClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
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
