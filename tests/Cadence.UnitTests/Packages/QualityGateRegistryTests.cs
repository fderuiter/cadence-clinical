using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Cli.Services;
using Cadence.UnitTests.Packages.Gates;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The quality gate registry (<see cref="QualityGates"/>, #5332): every gate can fail and names are unique.
/// Domain fixture slices under <c>Packages/Gates/*GateFixtures.cs</c> build minimal repositories that break each gate;
/// a gate added to the registry without a fixture slice entry fails <see cref="EveryGate_HasAFailingFixture"/> without
/// requiring a fragile hardcoded gate count.
/// </summary>
public sealed class QualityGateRegistryTests : IDisposable
{
    /// <summary>
    /// Gates whose real check needs a toolchain a unit test cannot provide hermetically. Their fixture lacks that
    /// toolchain, and the test proves the gate fails closed instead of passing. The comparison each one makes is the
    /// TypeScript compiler's or is covered elsewhere.
    /// </summary>
    private static readonly Dictionary<string, string> FailsClosedOnly = new(StringComparer.Ordinal)
    {
        ["prettier"] = "runs `pnpm exec prettier` against workspace node_modules; the fixture has no node_modules",
        ["eslint"] = "runs `pnpm exec eslint` against workspace node_modules; the fixture has no node_modules",
        ["typecheck"] = "runs `pnpm -r typecheck` against workspace node_modules; the fixture has no node_modules",
        ["api-clients"] = "runs `pnpm --filter web` and `pnpm -r typecheck` over the real web workspace; the fixture has no apps/web/tsconfig.json",
        ["schema-doc"] = "builds Cadence.SchemaDocExporter and every module's EF Core model; the fixture has no exporter project " +
                         "(the rendering is covered by SchemaDocumentationTests)",
    };

    private readonly QualityGateFixtureWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    public static TheoryData<string> GateNames() => new(QualityGates.All.Select(g => g.Name));

    private Dictionary<string, Func<GateContext>> Fixtures()
    {
        var all = new Dictionary<string, Func<GateContext>>(StringComparer.Ordinal);
        foreach (var slice in new[]
        {
            FormattingGateFixtures.Create(_workspace),
            HygieneAndSecretsGateFixtures.Create(_workspace),
            DocsAndGovernanceGateFixtures.Create(_workspace),
            ContractAndCodeGateFixtures.Create(_workspace),
            ReleaseAndTestSuiteGateFixtures.Create(_workspace),
        })
        {
            foreach (var (name, factory) in slice)
            {
                all.Add(name, factory);
            }
        }

        return all;
    }

    [Theory]
    [MemberData(nameof(GateNames))]
    public void EveryGate_FailsOnAFixtureThatBreaksIt(string gateName)
    {
        var gate = QualityGates.Find(gateName)!;
        var context = Fixtures()[gateName]();

        gate.Run(context).Should().BeFalse(
            FailsClosedOnly.TryGetValue(gateName, out string? why)
                ? $"'{gateName}' must fail closed when its toolchain is missing ({why})"
                : $"the fixture breaks what '{gateName}' checks");
    }

    [Fact]
    public void EveryGate_HasAFailingFixture()
    {
        Fixtures().Keys.Should().BeEquivalentTo(QualityGates.All.Select(g => g.Name));
        FailsClosedOnly.Keys.Should().BeSubsetOf(QualityGates.All.Select(g => g.Name));
    }

    [Fact]
    public void GateNamesAndAliases_AreUniqueAndFound()
    {
        var names = QualityGates.All.SelectMany(g => g.AllNames).ToList();

        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n == n.ToLowerInvariant() && !n.Contains('_'));
        QualityGates.All.Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.Checks));
        QualityGates.All.Where(g => g.PreCommit != PreCommitMode.Full)
            .Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.PreCommitNote));

        QualityGates.Find("Detect_Code_Duplication").Should().BeSameAs(QualityGates.Find("duplication"));
        QualityGates.Find("prettier-check").Should().BeNull("Prettier runs in the pre-commit hook and CI, not as a gate");
        QualityGates.Find("contracts").Should().BeNull("the architecture tests enforce the layering it claimed to check");
    }
}
