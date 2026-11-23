using System;
using System.IO;

using Cadence.Testing;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.Validation;

/// <summary>
/// Path Boundary Linter Test Suite.
/// Ported from legacy test_path_boundary_linter.py.
/// </summary>
public class PathBoundaryLinterTests
{
    private static string GetRepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);

    private static (bool IsValid, string Error) ValidateFile(string relPath)
    {
        var normalized = relPath.Replace("\\", "/");
        if (normalized.EndsWith(".vue") && !normalized.StartsWith("apps/") && !normalized.StartsWith("packages/"))
        {
            return (false, "Vue components (*.vue) must reside inside frontend structures");
        }

        if (normalized.EndsWith(".sh") && !normalized.StartsWith("scripts/"))
        {
            return (false, "Shell scripts (*.sh) must reside in scripts");
        }

        if (normalized.StartsWith("apps/") || normalized.StartsWith("packages/") || normalized.StartsWith("tests/") || normalized.StartsWith("scripts/") || normalized.StartsWith("docs/"))
        {
            return (true, string.Empty);
        }

        if (normalized == "pyproject.toml" || normalized == "eslint.config.mjs" || normalized == "Cadence.slnx")
        {
            return (true, string.Empty);
        }

        return (false, "File resides outside permitted root-level directories");
    }

    /// <summary>
    /// Test that compliant file paths pass boundary linter validation.
    /// </summary>
    [Fact(DisplayName = "LinterPositiveCases")]
    [Trait("Category", "Validation")]
    public void TestLinterPositiveCases()
    {
        ValidateFile("apps/gateway/main.py").IsValid.Should().BeTrue();
        ValidateFile("packages/database/audit.py").IsValid.Should().BeTrue();
        ValidateFile("apps/web/src/App.vue").IsValid.Should().BeTrue();
        ValidateFile("pyproject.toml").IsValid.Should().BeTrue();
        ValidateFile("Cadence.slnx").IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Test that invalid or misplaced file paths trigger validation failures.
    /// </summary>
    [Fact(DisplayName = "LinterNegativeCases")]
    [Trait("Category", "Validation")]
    public void TestLinterNegativeCases()
    {
        var res1 = ValidateFile("malicious.py");
        res1.IsValid.Should().BeFalse();
        res1.Error.Should().Contain("permitted root-level directories");

        var res2 = ValidateFile("scripts/confused.vue");
        res2.IsValid.Should().BeFalse();
        res2.Error.Should().Contain("Vue components (*.vue) must reside inside frontend structures");

        var res3 = ValidateFile("apps/web/bad.sh");
        res3.IsValid.Should().BeFalse();
        res3.Error.Should().Contain("Shell scripts (*.sh) must reside in scripts");
    }

    /// <summary>
    /// Test layout integrity assertions against required GxP workspace folder structure.
    /// </summary>
    [Fact(DisplayName = "EnvironmentIntegrityAssertions")]
    [Trait("Category", "Validation")]
    public void TestEnvironmentIntegrityAssertions()
    {
        var repoRoot = GetRepoRoot();
        var requiredDirs = new[] { "apps", "packages", "docs", "tests", "scripts" };

        foreach (var dir in requiredDirs)
        {
            Directory.Exists(Path.Combine(repoRoot, dir)).Should().BeTrue($"Core GxP directory boundary '{dir}' must exist");
        }
    }
}
