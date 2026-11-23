using System;
using System.IO;

using Cadence.Cli.Services;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>How <c>cadence doctor</c> reads the pnpm pin and compares it with the installed pnpm (#5331).</summary>
public class PnpmPinTests
{
    [Fact]
    public void PinnedVersion_StripsTheIntegrityHash()
    {
        PnpmPin.PinnedVersion("""{ "packageManager": "pnpm@10.34.6+sha512.abc" }""").Should().Be("10.34.6");
    }

    [Fact]
    public void PinnedVersion_IsNullWithoutAPnpmPin()
    {
        PnpmPin.PinnedVersion("""{ "name": "x" }""").Should().BeNull();
        PnpmPin.PinnedVersion("""{ "packageManager": "yarn@4.0.0" }""").Should().BeNull();
    }

    [Theory]
    [InlineData("10.34.6", "10.34.6", PnpmPinStatus.Matches)]
    [InlineData("10.34.6", "10.34.6\n", PnpmPinStatus.Matches)]
    [InlineData("10.34.6", "12.11.2", PnpmPinStatus.Mismatch)]
    [InlineData("10.34.6", null, PnpmPinStatus.Unusable)]
    [InlineData("10.34.6", "", PnpmPinStatus.Unusable)]
    [InlineData(null, "10.34.6", PnpmPinStatus.NotPinned)]
    public void Compare_ReportsEachCase(string? pinned, string? installed, PnpmPinStatus expected)
    {
        PnpmPin.Compare(pinned, installed).Should().Be(expected);
    }

    [Fact]
    public void TheRepositoryPinsPnpm10()
    {
        string root = RepoRoot();
        string? pinned = PnpmPin.PinnedVersion(File.ReadAllText(Path.Combine(root, "package.json")));
        pinned.Should().NotBeNull().And.StartWith("10.");
    }

    private static string RepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);
}
