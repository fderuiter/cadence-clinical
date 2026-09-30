using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class AdrPackageJsonChangeTests
{
    private const string Baseline = """
        {
          "name": "cadence",
          "scripts": { "test": "vitest run" },
          "devDependencies": { "vitest": "^3.0.0" },
          "packageManager": "pnpm@10.0.0"
        }
        """;

    [Fact]
    public void PackageJsonDependenciesChanged_WhenOnlyScriptsChange_ReturnsFalse()
    {
        const string after = """
            {
              "name": "cadence",
              "scripts": { "test": "vitest run", "check": "pnpm cadence check" },
              "devDependencies": { "vitest": "^3.0.0" },
              "packageManager": "pnpm@10.0.0"
            }
            """;

        AdrValidatorService.PackageJsonDependenciesChanged(Baseline, after).Should().BeFalse();
    }

    [Fact]
    public void PackageJsonDependenciesChanged_WhenFormattingOnlyChanges_ReturnsFalse()
    {
        const string after = """{"packageManager":"pnpm@10.0.0","devDependencies":{"vitest":"^3.0.0"},"name":"cadence","scripts":{"test":"vitest run"}}""";

        AdrValidatorService.PackageJsonDependenciesChanged(Baseline, after).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{ "name": "cadence", "scripts": { "test": "vitest run" }, "devDependencies": { "vitest": "^4.0.0" }, "packageManager": "pnpm@10.0.0" }""")]
    [InlineData("""{ "name": "cadence", "scripts": { "test": "vitest run" }, "devDependencies": { "vitest": "^3.0.0" }, "dependencies": { "vue": "^3.5.0" }, "packageManager": "pnpm@10.0.0" }""")]
    [InlineData("""{ "name": "cadence", "scripts": { "test": "vitest run" }, "devDependencies": { "vitest": "^3.0.0" }, "packageManager": "pnpm@11.0.0" }""")]
    public void PackageJsonDependenciesChanged_WhenDependenciesOrToolchainChange_ReturnsTrue(string after)
    {
        AdrValidatorService.PackageJsonDependenciesChanged(Baseline, after).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ not json")]
    public void PackageJsonDependenciesChanged_WhenAVersionIsMissingOrInvalid_ReturnsTrue(string? after)
    {
        AdrValidatorService.PackageJsonDependenciesChanged(Baseline, after).Should().BeTrue();
    }
}
