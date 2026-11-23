using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Covers the kernel clock port (#4784, K-TIME-3): <see cref="SystemClock"/>, the shared
/// <see cref="FakeClock"/> test double, and the banned-API wiring that forces kernel code through them.
/// </summary>
public class ClockTests
{
    private static readonly string[] BannedClockSymbols =
    [
        "P:System.DateTime.Now",
        "P:System.DateTime.UtcNow",
        "P:System.DateTimeOffset.Now",
        "P:System.DateTimeOffset.UtcNow",
        "P:System.TimeProvider.System",
        "M:System.TimeProvider.GetUtcNow",
    ];

    /// <summary>
    /// Validates that the production clock reports the current instant in UTC (offset zero).
    /// </summary>
    [Fact(DisplayName = "SystemClock_ReturnsCurrentUtcInstant")]
    [Trait("Category", "GxP_OQ")]
    public void SystemClockReturnsCurrentUtcInstant()
    {
        // Arrange
        IClock clock = SystemClock.Instance;
#pragma warning disable RS0030 // SystemClock's unit test brackets UtcNow against the underlying TimeProvider.System reference.
        var before = TimeProvider.System.GetUtcNow();

        // Act
        var now = clock.UtcNow;

        // Assert
        var after = TimeProvider.System.GetUtcNow();
#pragma warning restore RS0030
        now.Offset.Should().Be(TimeSpan.Zero);
        now.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    /// <summary>
    /// Validates that the fake clock is frozen until moved, so audit timestamps can be asserted exactly.
    /// </summary>
    [Fact(DisplayName = "FakeClock_IsFrozen_UntilAdvanced")]
    [Trait("Category", "GxP_OQ")]
    public void FakeClockIsFrozenUntilAdvanced()
    {
        // Arrange
        var start = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);

        // Act
        var first = clock.UtcNow;
        var second = clock.UtcNow;
        var advanced = clock.Advance(TimeSpan.FromMinutes(5));

        // Assert
        first.Should().Be(start);
        second.Should().Be(start);
        advanced.Should().Be(start.AddMinutes(5));
        clock.UtcNow.Should().Be(advanced);
    }

    /// <summary>
    /// Validates that the fake clock defaults to a fixed instant and normalises set values to UTC.
    /// </summary>
    [Fact(DisplayName = "FakeClock_Set_NormalisesToUtc")]
    [Trait("Category", "GxP_OQ")]
    public void FakeClockSetNormalisesToUtc()
    {
        // Arrange
        var clock = new FakeClock();
        var local = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(2));

        // Act
        var initial = clock.UtcNow;
        clock.Set(local);

        // Assert
        initial.Should().Be(FakeClock.DefaultStart);
        clock.UtcNow.Should().Be(local);
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
        clock.UtcNow.Hour.Should().Be(10);
    }

    /// <summary>
    /// Validates that the fake clock refuses to move backwards through <see cref="FakeClock.Advance"/>.
    /// </summary>
    [Fact(DisplayName = "FakeClock_Advance_RejectsNegativeDelta")]
    [Trait("Category", "GxP_OQ")]
    public void FakeClockAdvanceRejectsNegativeDelta()
    {
        // Arrange
        var clock = new FakeClock();

        // Act
        var act = () => clock.Advance(TimeSpan.FromSeconds(-1));

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        clock.UtcNow.Should().Be(FakeClock.DefaultStart);
    }

    /// <summary>
    /// Validates that <c>BannedSymbols.txt</c> bans wall-clock and <see cref="TimeProvider"/> members with doc-comment IDs
    /// that resolve to real members, and that the kernel build opts into the banned-API analyzer.
    /// </summary>
    [Fact(DisplayName = "BannedSymbols_BanWallClock_AndKernelIsWired")]
    [Trait("Category", "GxP_OQ")]
    [KernelRule("K-TIME-3")]
    public void BannedSymbolsBanWallClockAndKernelIsWired()
    {
        // Arrange
        var repoRoot = FindRepoRoot();

        // Act
        var bannedIds = File.ReadAllLines(Path.Combine(repoRoot, "BannedSymbols.txt"))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(line => line.Split(';')[0].Trim())
            .ToArray();
        var rootProps = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"));

        // Assert
        bannedIds.Should().Contain(BannedClockSymbols);
        foreach (var id in BannedClockSymbols)
        {
            var lastDot = id.LastIndexOf('.');
            var type = Type.GetType(id[2..lastDot], throwOnError: true)!;
            var memberName = id[(lastDot + 1)..];
            MemberInfo? member = id[0] switch
            {
                'P' => type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance),
                'M' => type.GetMethod(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, Type.EmptyTypes),
                _ => throw new InvalidOperationException($"Unexpected doc-comment prefix in {id}."),
            };
            member.Should().NotBeNull($"{id} must name a real public member");
        }

        rootProps.Should().Contain("Microsoft.CodeAnalysis.BannedApiAnalyzers")
            .And.Contain("BannedSymbols.txt");
    }

    private static string FindRepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);
}
