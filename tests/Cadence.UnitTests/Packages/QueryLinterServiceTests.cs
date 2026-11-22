using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>The query-style gate as the pre-commit hook calls it, with every staged file.</summary>
public sealed class QueryLinterServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"query-lint-{Guid.NewGuid():N}");

    public QueryLinterServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LintQueries_IgnoresStagedMarkdown_ButStillLintsStagedCSharp()
    {
        // Built from parts so this file does not trip the linter it tests.
        string comparison = "=" + "= true";
        File.WriteAllText(Path.Combine(_dir, "rules.md"), $"| `query-linter` | Fails on `{comparison}`. |\n");
        File.WriteAllText(Path.Combine(_dir, "Query.cs"), $"class Q {{ bool M(bool? b) {{ if (b {comparison}) return true; return false; }} }}\n");

        QueryLinterService.LintQueries(["rules.md"], _dir).Should().Be(0);
        QueryLinterService.LintQueries(["rules.md", "Query.cs"], _dir).Should().Be(1);
    }

    [Fact]
    public void LintQueries_ExcludesTestsBinAndObjPaths_EvenWhenExplicitlyPassed_AndSkipsEmptyFileList()
    {
        string comparison = "=" + "= true";
        string[] excludedRelPaths =
        [
            "tests/Cadence.UnitTests/SampleTests.cs",
            "apps/alpha/bin/Debug/net10.0/Generated.cs",
            "apps/alpha/obj/Debug/net10.0/Generated.cs",
        ];

        foreach (string relPath in excludedRelPaths)
        {
            string fullPath = Path.Combine(_dir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, $"class Generated {{ bool Check(bool? flag) => flag {comparison}; }}\n");
        }

        QueryLinterService.LintQueries(excludedRelPaths, _dir).Should().Be(0, "explicit pre-commit/hook paths under tests/, bin/, and obj/ must match unscoped exclusions");
        QueryLinterService.LintQueries(null, _dir).Should().Be(0, "unscoped scan also excludes tests/, bin/, and obj/");

        string prodPath = Path.Combine(_dir, "apps/alpha/src/BadQuery.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(prodPath)!);
        File.WriteAllText(prodPath, $"class BadQuery {{ bool Check(bool? flag) => flag {comparison}; }}\n");

        QueryLinterService.LintQueries([], _dir).Should().Be(0, "an empty file list (e.g. delete-only commit) must not fall back to a full-repository scan");
        QueryLinterService.LintQueries(null, _dir).Should().Be(1, "unscoped scan catches violations in production source");
    }
}
