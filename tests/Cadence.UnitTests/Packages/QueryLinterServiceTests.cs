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
}
