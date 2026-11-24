using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Cadence.ArchitectureTests.Architecture.Fixtures;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchLoader = ArchUnitNET.Loader.ArchLoader;
using ArchRuleExtensions = ArchUnitNET.xUnit.ArchRuleExtensions;
using IArchRule = ArchUnitNET.Fluent.IArchRule;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Enforces the Clinical Kernel boundary rules (spec §2 and §9: K-XS-1, K-XS-2, K-MOD-1) over every
/// <c>Cadence.*</c> assembly outside <c>Cadence.Kernel.*</c>, ratcheted against per-assembly shards under
/// <c>tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/&lt;Assembly&gt;.baseline.txt</c>. To prune paid-down entries,
/// run the suite with <c>CADENCE_UPDATE_BASELINE=1</c> (see <see cref="KernelBoundaryBaseline"/>).
/// </summary>
public class KernelBoundaryTests
{
    private static readonly Lazy<IReadOnlyList<Assembly>> NonKernelAssemblies =
        new(KernelBoundaryRules.LoadNonKernelCadenceAssemblies);

    private static readonly Assembly SyntheticAssembly = typeof(KernelBoundaryTests).Assembly;

    /// <summary>
    /// Validates K-XS-1: no assembly outside Cadence.Kernel.* declares a Study/Site/Participant/Subject/
    /// Observation/Organization/Person/Role/Delegation or *AuditLog type, a public string *StudyId/*SiteId/
    /// *ParticipantId/*SubjectId/*TenantId member, or a non-kernel [JsonPropertyName] alias for those central
    /// identities, beyond the recorded (shrink-only) baseline.
    /// </summary>
    [Fact(DisplayName = "K_XS_1_NonKernelAssemblies_MustNotRedefineKernelConcepts_BeyondBaseline")]
    [Trait("Category", "Architecture")]
    [KernelRule("K-XS-1")]
    [KernelRule("K-TRUTH-1")]
    [KernelRule("K-SCOPE-1")]
    public void KXs1NonKernelAssembliesMustNotRedefineKernelConceptsBeyondBaseline()
    {
        NonKernelAssemblies.Value.Should().NotBeEmpty();
        NonKernelAssemblies.Value.Select(a => a.GetName().Name).Should().Contain(
            ["Cadence.Modules.Execution", KernelBoundaryRules.KernelHostAssemblyName],
            "both Cadence.Modules.Execution and Cadence.Kernel.Host must be inspected by non-kernel boundary sentinels");

        var comparison = KernelBoundaryBaseline.Verify(
            KernelBoundaryRules.KXs1, KernelBoundaryRules.EvaluateKXs1(NonKernelAssemblies.Value));

        comparison.IsClean.Should().BeTrue(comparison.Describe(KernelBoundaryRules.KXs1));
    }

    /// <summary>
    /// Validates K-XS-2: no project or assembly outside the kernel (except the exempted composition host
    /// Cadence.Kernel.Host and out-of-process schema/migration tools) references Cadence.Kernel.Persistence,
    /// by csproj reference, compiled assembly reference, or type dependency. Zero tolerance (no baseline).
    /// </summary>
    [Fact(DisplayName = "K_XS_2_NoProjectOutsideKernel_MayReference_KernelPersistence")]
    [Trait("Category", "Architecture")]
    [KernelRule("K-XS-2")]
    public void KXs2NoProjectOutsideKernelMayReferenceKernelPersistence()
    {
        var projects = KernelBoundaryRules.LoadRepositoryProjects(KernelBoundaryBaseline.RepositoryRoot()).ToList();
        projects.Should().Contain(p => p.ProjectName == "Cadence.ArchitectureTests", "the csproj scan must see the repository");

        var violations = KernelBoundaryRules.EvaluateKXs2Projects(projects)
            .Concat(KernelBoundaryRules.EvaluateKXs2(NonKernelAssemblies.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            "product modules reference Cadence.Kernel.Abstractions and Cadence.Kernel.Modules only, never kernel Persistence, Application, Domain or Api (K-XS-2, ADR-2600)");

        var nonExemptAssemblies = NonKernelAssemblies.Value
            .Where(a => !KernelBoundaryRules.IsExemptFromKXs2(a.GetName().Name))
            .ToArray();

        IArchRule kernelPersistenceRule = Types()
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"^Cadence\.Kernel\.Persistence(\..*)?$"));
        ArchRuleExtensions.Check(
            kernelPersistenceRule,
            new ArchLoader().LoadAssemblies(nonExemptAssemblies).Build());
    }

    /// <summary>
    /// Validates K-MOD-1: entity types in module DbContexts store no string or Guid
    /// *StudyId/*SiteId/*ParticipantId/*SubjectId/*TenantId column (kernel ID types only), beyond the shrink-only baseline.
    /// </summary>
    [Fact(DisplayName = "K_MOD_1_ModuleDbContexts_MustNotDuplicateKernelIdentity_BeyondBaseline")]
    [Trait("Category", "Architecture")]
    [KernelRule("K-MOD-1")]
    [KernelRule("K-TRUTH-1")]
    public void KMod1ModuleDbContextsMustNotDuplicateKernelIdentityBeyondBaseline()
    {
        var comparison = KernelBoundaryBaseline.Verify(
            KernelBoundaryRules.KMod1, KernelBoundaryRules.EvaluateKMod1(NonKernelAssemblies.Value));

        comparison.IsClean.Should().BeTrue(comparison.Describe(KernelBoundaryRules.KMod1));
    }

    /// <summary>
    /// Validates spec §14 scenario 7: a deliberately introduced duplicate Site entity, *AuditLog table,
    /// public string StudyId/SiteId/ParticipantId/SubjectId/TenantId members, and non-kernel [JsonPropertyName]
    /// wire aliases are reported by K-XS-1 and fail the ratchet.
    /// </summary>
    [Fact(DisplayName = "K_XS_1_Bites_OnSyntheticDuplicateSiteStringIdsAndJsonPropertyNameAliases")]
    [Trait("Category", "Architecture")]
    [KernelRule("K-XS-1")]
    public void KXs1BitesOnSyntheticDuplicateSiteStringIdsAndJsonPropertyNameAliases()
    {
        const string prefix = "K-XS-1|Cadence.ArchitectureTests|Cadence.ArchitectureTests.Architecture.Fixtures.";

        var violations = KernelBoundaryRules.EvaluateKXs1([SyntheticAssembly]);

        violations.Should().Contain(
        [
            prefix + "Site",
            prefix + "Site.StudyId",
            prefix + "SyntheticModuleAuditLog",
            prefix + "SyntheticSiteRegistration.StudyId",
            prefix + "SyntheticSiteRegistration.PrimarySiteId",
            prefix + "SyntheticSiteRegistration.ParticipantId",
            prefix + "SyntheticSiteRegistration.SubjectId",
            prefix + "SyntheticSiteRegistration.TenantId",
            prefix + "SyntheticWirePayload.DisguisedStudyRef",
            prefix + "SyntheticWirePayload.DisguisedSiteGuid",
            prefix + "SyntheticWirePayload.ConstructorOnlySubjectRef",
            prefix + "SyntheticWirePayload.DisguisedTenantGuid",
            prefix + "SyntheticWirePayload.DisguisedParticipantField",
        ]);
        violations.Should().NotContain(v => v.EndsWith(".TypedStudyId", StringComparison.Ordinal),
            "a non-string C# member without a wire alias is checked at persistence level by K-MOD-1");
        violations.Should().NotContain(v => v.EndsWith(".CanonicalStudy", StringComparison.Ordinal) || v.EndsWith(".CanonicalTenant", StringComparison.Ordinal),
            "canonical kernel ID types (StudyId, TenantId) remain allowed on serialized wire contracts");
        violations.Should().NotContain(v => v.EndsWith(".CompositeCorrelation", StringComparison.Ordinal),
            "unrelated words ending in 'siteId' letters such as 'compositeId' must not false-match 'SiteId'");
        violations.Should().NotContain(v => v.EndsWith(".InternalStudyId", StringComparison.Ordinal),
            "only public members are in scope for K-XS-1");
        violations.Should().NotContain(v => v.EndsWith(".HeaderSiteId", StringComparison.Ordinal),
            "a const string holds a name such as a header, not an ID value");

        var ratchet = KernelBoundaryBaseline.Compare(violations, KernelBoundaryBaseline.Read(KernelBoundaryRules.KXs1));
        ratchet.IsClean.Should().BeFalse();
        ratchet.NewViolations.Should().Contain(
        [
            prefix + "Site",
            prefix + "SyntheticSiteRegistration.SubjectId",
            prefix + "SyntheticSiteRegistration.TenantId",
            prefix + "SyntheticWirePayload.DisguisedStudyRef",
        ]);
        ratchet.Describe(KernelBoundaryRules.KXs1).Should().Contain(KernelBoundaryBaseline.UpdateEnvironmentVariable);
    }

    /// <summary>
    /// Validates spec §14 scenario 7 for K-MOD-1: a synthetic module DbContext mapping duplicate Site and registration
    /// entities with string/Guid StudyId, SiteId, ParticipantId, SubjectId, and TenantId columns (including shadow
    /// columns) is reported.
    /// </summary>
    [Fact(DisplayName = "K_MOD_1_Bites_OnSyntheticDbContextWithStringAndGuidKernelIds")]
    [Trait("Category", "Architecture")]
    [KernelRule("K-MOD-1")]
    public void KMod1BitesOnSyntheticDbContextWithStringAndGuidKernelIds()
    {
        const string prefix = "K-MOD-1|Cadence.ArchitectureTests|Cadence.ArchitectureTests.Architecture.Fixtures.";

        var violations = KernelBoundaryRules.EvaluateKMod1([typeof(SyntheticModuleDbContext)]);

        violations.Should().Equal(
            prefix + "Site.LegacySiteId",
            prefix + "Site.LegacyTenantId",
            prefix + "Site.OwningSiteId",
            prefix + "Site.StudyId",
            prefix + "SyntheticSiteRegistration.ParticipantId",
            prefix + "SyntheticSiteRegistration.PrimarySiteId",
            prefix + "SyntheticSiteRegistration.StudyId",
            prefix + "SyntheticSiteRegistration.SubjectId",
            prefix + "SyntheticSiteRegistration.TenantId");
    }

    /// <summary>
    /// Validates the K-XS-2 source scan: a synthetic non-kernel csproj referencing Cadence.Kernel.Persistence is
    /// reported, while the kernel's own projects and Cadence.Kernel.Host may reference it.
    /// </summary>
    [Fact(DisplayName = "K_XS_2_Bites_OnSyntheticProjectReferencingKernelPersistence")]
    [Trait("Category", "Architecture")]
    [KernelRule("K-XS-2")]
    public void KXs2BitesOnSyntheticProjectReferencingKernelPersistence()
    {
        const string offending =
            """<Project><ItemGroup><ProjectReference Include="..\..\packages\kernel\src\Cadence.Kernel.Persistence\Cadence.Kernel.Persistence.csproj" /></ItemGroup></Project>""";

        var violations = KernelBoundaryRules.EvaluateKXs2Projects(
        [
            ("Cadence.Apps.Synthetic", offending),
            ("Cadence.Kernel.Api", offending),
            ("Cadence.Kernel.Host", offending),
            ("Cadence.UnitTests", offending),
            ("Cadence.Apps.Clean", """<Project><ItemGroup><ProjectReference Include="Cadence.Kernel.Abstractions.csproj" /></ItemGroup></Project>"""),
        ]);

        violations.Should().Equal("K-XS-2|Cadence.Apps.Synthetic|Cadence.Kernel.Persistence");
    }

    /// <summary>
    /// Validates that the K-XS-2 project rule reports a module that references kernel Application or Domain, and allows
    /// the module SDK (ADR-2600).
    /// </summary>
    [Fact(DisplayName = "K_XS_2_Bites_OnSyntheticProjectReferencingKernelApplication_AndAllowsModuleSdk")]
    [Trait("Category", "Architecture")]
    [KernelRule("K-XS-2")]
    public void KXs2BitesOnSyntheticProjectReferencingKernelApplicationAndAllowsModuleSdk()
    {
        var violations = KernelBoundaryRules.EvaluateKXs2Projects(
        [
            ("Cadence.Modules.Synthetic", """<Project><ItemGroup><ProjectReference Include="..\Cadence.Kernel.Application\Cadence.Kernel.Application.csproj" /><ProjectReference Include="..\Cadence.Kernel.Domain\Cadence.Kernel.Domain.csproj" /></ItemGroup></Project>"""),
            ("Cadence.Modules.Clean", """<Project><ItemGroup><ProjectReference Include="..\Cadence.Kernel.Abstractions\Cadence.Kernel.Abstractions.csproj" /><ProjectReference Include="..\Cadence.Kernel.Modules\Cadence.Kernel.Modules.csproj" /></ItemGroup></Project>"""),
        ]);

        violations.Should().Equal(
            "K-XS-2|Cadence.Modules.Synthetic|Cadence.Kernel.Application",
            "K-XS-2|Cadence.Modules.Synthetic|Cadence.Kernel.Domain");
    }

    /// <summary>
    /// Validates the ratchet contract: new violations and stale baseline entries are both reported, and the
    /// baseline file is sorted, duplicate-free and well-formed.
    /// </summary>
    [Fact(DisplayName = "KernelBoundaryBaseline_Ratchet_ReportsNewAndStaleEntries_AndIsSorted")]
    [Trait("Category", "Architecture")]
    public void KernelBoundaryBaselineRatchetReportsNewAndStaleEntriesAndIsSorted()
    {
        var comparison = KernelBoundaryBaseline.Compare(["K-XS-1|A|X", "K-XS-1|A|Y"], ["K-XS-1|A|Y", "K-XS-1|A|Z"]);
        comparison.NewViolations.Should().Equal("K-XS-1|A|X");
        comparison.StaleEntries.Should().Equal("K-XS-1|A|Z");
        comparison.Describe("K-XS-1").Should().Contain("Delete these lines").And.Contain("K-XS-1|A|Z");

        foreach (var rule in new[] { KernelBoundaryRules.KXs1, KernelBoundaryRules.KMod1 })
        {
            var entries = KernelBoundaryBaseline.Read(rule);
            entries.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
            entries.Should().OnlyContain(e => e.Split('|').Length == 3, "each line is RULE|Assembly|Type[.Member]");
            entries.Should().NotContain(e => KernelBoundaryRules.IsKernelAssembly(e.Split('|')[1]));
        }

        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();
        File.Exists(Path.Combine(repoRoot, "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt"))
            .Should().BeFalse("monolithic kernel-boundaries.baseline.txt must not exist once sharded by assembly");

        string shardDir = Path.Combine(repoRoot, KernelBoundaryBaseline.RelativeDirectory);
        Directory.Exists(shardDir).Should().BeTrue();
        var shardFiles = Directory.GetFiles(shardDir, "*.baseline.txt", SearchOption.TopDirectoryOnly);
        shardFiles.Should().NotBeEmpty();
        foreach (string shardFile in shardFiles)
        {
            string expectedAssembly = Path.GetFileName(shardFile)[..^".baseline.txt".Length];
            var lines = File.ReadAllLines(shardFile)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();
            lines.Should().NotBeEmpty("empty shards must be deleted automatically");
            lines.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
            lines.Should().OnlyContain(e => KernelBoundaryBaseline.ExtractAssembly(e) == expectedAssembly);
        }
    }

    [Fact(DisplayName = "KernelBoundaryBaseline_UpdateMode_SubtractsOnlyStaleEntries_DeletesEmptyShards_AndNeverAddsNewViolations")]
    [Trait("Category", "Architecture")]
    public void KernelBoundaryBaselineUpdateModeSubtractsOnlyStaleEntriesDeletesEmptyShardsAndNeverAddsNewViolations()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"kernel-boundary-shards-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            KernelBoundaryBaseline.WriteShards(
                [
                    "K-MOD-1|Cadence.Apps.Alpha|AlphaEntity.StudyId",
                    "K-XS-1|Cadence.Apps.Alpha|AlphaDto.StudyId",
                    "K-XS-1|Cadence.Apps.Beta|BetaDto.SiteId",
                    "K-XS-1|Cadence.Apps.Untouched|UntouchedDto.StudyId",
                ],
                tempDir);

            string untouchedPath = Path.Combine(tempDir, "Cadence.Apps.Untouched.baseline.txt");
            string betaPath = Path.Combine(tempDir, "Cadence.Apps.Beta.baseline.txt");
            string alphaPath = Path.Combine(tempDir, "Cadence.Apps.Alpha.baseline.txt");
            string untouchedBefore = File.ReadAllText(untouchedPath);

            // Beta's K-XS-1 entry is resolved (empty), Alpha keeps its K-XS-1 entry and introduces a NEW violation, Untouched keeps its entry.
            var result = KernelBoundaryBaseline.Verify(
                KernelBoundaryRules.KXs1,
                [
                    "K-XS-1|Cadence.Apps.Alpha|AlphaDto.StudyId",
                    "K-XS-1|Cadence.Apps.Alpha|AlphaDto.BrandNewViolation",
                    "K-XS-1|Cadence.Apps.Untouched|UntouchedDto.StudyId",
                ],
                baselineDirectory: tempDir,
                updateOverride: true);

            // Subtract-only: Beta's empty shard is deleted, Untouched is untouched, Alpha keeps K-MOD-1 + old K-XS-1, and BrandNewViolation is NOT added!
            File.Exists(betaPath).Should().BeFalse("Beta shard reached zero entries and must be deleted");
            File.ReadAllText(untouchedPath).Should().Be(untouchedBefore);
            KernelBoundaryBaseline.ReadAll(tempDir).Should().Equal(
                "K-MOD-1|Cadence.Apps.Alpha|AlphaEntity.StudyId",
                "K-XS-1|Cadence.Apps.Alpha|AlphaDto.StudyId",
                "K-XS-1|Cadence.Apps.Untouched|UntouchedDto.StudyId");
            File.Exists(alphaPath).Should().BeTrue();
            result.StaleEntries.Should().BeEmpty();
            result.NewViolations.Should().Equal("K-XS-1|Cadence.Apps.Alpha|AlphaDto.BrandNewViolation");
            result.IsClean.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
