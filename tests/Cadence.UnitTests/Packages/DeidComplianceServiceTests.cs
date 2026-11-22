using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Deid;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The <c>check deid</c> gate, ported from the Python reference repository scanner (<c>packages/deid/cli.py</c>,
/// deleted once these tests landed and kept in git history): pragmas, per-category development-value allowlists,
/// path exclusions and the shrink-only baseline. All values are synthetic.
/// </summary>
public sealed class DeidComplianceServiceTests : IDisposable
{
    private static readonly DeidDetector Detector = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("cadence-deid-").FullName;

    public void Dispose()
    {
        if (!Directory.Exists(_dir))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(_dir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ScanText_ReportsLineColumnAndCategory_NotTheValue()
    {
        var findings = DeidComplianceService.ScanText("apps/x/Seed.cs", "var a = 1;\nvar ssn = \"000-12-3456\";\n", Detector);

        findings.Should().ContainSingle().Which.Should().Be(new DeidFinding("apps/x/Seed.cs", 2, 12, DetectorCategory.SsnNationalId));
    }

    [Theory]
    [InlineData("var ssn = \"000-12-3456\"; // deid-ignore")]
    [InlineData("var ssn = \"000-12-3456\"; // deid: ignore")]
    [InlineData("var ssn = \"000-12-3456\"; // pragma: allowlist secret")]
    public void ScanText_SkipsLinesWithAPragma(string line)
    {
        DeidComplianceService.ScanText("apps/x/Seed.cs", line, Detector).Should().BeEmpty();
    }

    [Theory]
    [InlineData("const docs = \"https://github.com/fderuiter/cadence-clinical\";")]
    [InlineData("const api = \"http://localhost:5000/api\";")]
    [InlineData("const ns = \"https://www.w3.org/2000/svg\";")]
    [InlineData("const mail = \"support@cadence.example\";")]
    [InlineData("const bind = \"127.0.0.1\";")]
    [InlineData("const timeoutMs = 30000;")]
    [InlineData("const port = 12345;")]
    [InlineData("const version = \"2024-05-01\";")]
    [InlineData("const ratio = 555.0100;")]
    public void ScanText_AllowsKnownDevelopmentValues(string line)
    {
        DeidComplianceService.ScanText("apps/x/Config.ts", line, Detector).Should().BeEmpty();
    }

    [Theory]
    [InlineData("const contact = \"jane.roe@hospital.test\";", DetectorCategory.Email)]
    [InlineData("const site = \"https://patient-portal.test/records\";", DetectorCategory.Urls)]
    [InlineData("const zip = \"90210\";", DetectorCategory.ZipGeographic)]
    [InlineData("const host = \"192.0.2.44\";", DetectorCategory.IpMacAddresses)]
    [InlineData("const dob = \"1971-03-14\";", DetectorCategory.Dates)]
    public void ScanText_ReportsValuesOutsideTheAllowlists(string line, string category)
    {
        DeidComplianceService.ScanText("apps/x/Seed.ts", line, Detector).Select(f => f.Category).Should().Contain(category);
    }

    [Theory]
    [InlineData("tests/Cadence.UnitTests/Fixtures.cs", true)]
    [InlineData("docs/SDLC/report.md", true)]
    [InlineData("apps/web/node_modules/pkg/index.js", true)]
    [InlineData("apps/execution/src/bin/Debug/app.json", true)]
    [InlineData("apps/web/src/views/Home.test.ts", true)]
    [InlineData("README.md", true)]
    [InlineData(".deid-baseline.txt", true)]
    [InlineData("packages/deid/src/DeidDetector.cs", true)]
    [InlineData("apps/execution/src/Program.cs", false)]
    [InlineData("apps/web/src/views/Home.vue", false)]
    public void IsExcludedPath_SkipsTestsDocsToolingAndGeneratedFolders(string path, bool excluded)
    {
        DeidComplianceService.IsExcludedPath(path).Should().Be(excluded);
    }

    [Fact]
    public void ShouldScanFile_ReadsTextTypesAndSniffsFilesWithoutAnExtension()
    {
        string text = Write("NOTICE", "plain text");
        string binary = Path.Combine(_dir, "blob");
        File.WriteAllBytes(binary, [1, 0, 2]);

        DeidComplianceService.ShouldScanFile("apps/x/Program.cs").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("apps/web/src/App.vue").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("apps/db/schema.sql").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("apps/web/index.html").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("apps/web/Dockerfile").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("Makefile").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("Pipfile").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("apps/web/public/logo.png").Should().BeFalse();
        DeidComplianceService.ShouldScanFile(text).Should().BeTrue();
        DeidComplianceService.ShouldScanFile(binary).Should().BeFalse();
    }

    [Fact]
    public void ScanDeidCompliance_HonoursBaseRefMultiCommitPushAndZeroShaFallback_AcrossVueSqlHtmlAndDockerfile()
    {
        Git("init", "-q", "-b", "main");
        Write("apps/web/Clean.vue", "<template><div>Clean</div></template>\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "main baseline");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        string beforePush = Git("rev-parse", "HEAD").Trim();

        // Commit 1 introduces PHI in a .vue file; Commit 2 only touches a clean .sql file.
        Write("apps/web/PatientCard.vue", "<template><span>000-12-3456</span></template>\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "commit 1 with vue phi");

        Write("apps/execution/query.sql", "SELECT 1;\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "commit 2 clean sql");

        // Multi-commit push range catches Commit 1's .vue finding rather than scanning only HEAD~1..HEAD.
        DeidComplianceService.ScanDeidCompliance(repoRoot: _dir, pushBefore: beforePush).Should().Be(1);

        // Zero-SHA and tag push fall back to git ls-files -z and still catch the tracked .vue finding.
        Git("tag", "v1.0.0");
        DeidComplianceService.ScanDeidCompliance(repoRoot: _dir, pushBefore: "0000000000000000000000000000000000000000").Should().Be(1);
        DeidComplianceService.ScanDeidCompliance(repoRoot: _dir, pushBefore: "v1.0.0").Should().Be(1);

        // If origin/release/1.0 baselines Commit 1 and a hotfix branch adds an unbaselined Dockerfile, --base origin/release/1.0 scans the hotfix diff.
        string key = DeidComplianceService.BaselineKey(
            new DeidFinding("apps/web/PatientCard.vue", 1, 17, DetectorCategory.SsnNationalId),
            "<template><span>000-12-3456</span></template>");
        Write(DeidComplianceService.BaselineFile, $"# baseline\n{key}\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "baseline existing finding on release");
        Git("update-ref", "refs/remotes/origin/release/1.0", "HEAD");

        Git("checkout", "-q", "-b", "hotfix/docker");
        Write("apps/web/Dockerfile", "FROM scratch\n# 1971-03-14\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "dockerfile with unbaselined date");

        DeidComplianceService.ScanDeidCompliance(repoRoot: _dir, baseRef: "origin/release/1.0").Should().Be(1);
    }

    [Fact]
    public void BaselineKey_SurvivesTheLineMoving_AndChangesWhenTheLineChanges()
    {
        var first = new DeidFinding("a.cs", 3, 5, DetectorCategory.Dates);
        var moved = first with { Line = 40, Column = 9 };

        DeidComplianceService.BaselineKey(first, "  x = \"1971-03-14\";").Should()
            .Be(DeidComplianceService.BaselineKey(moved, "x = \"1971-03-14\";  "))
            .And.StartWith("a.cs|dates|")
            .And.NotContain("1971");
        DeidComplianceService.BaselineKey(first, "x = \"1971-03-15\";").Should()
            .NotBe(DeidComplianceService.BaselineKey(first, "x = \"1971-03-14\";"));
    }

    [Fact]
    public void CompareWithBaseline_CountsEntries_AndReportsNewFindingsAndStaleEntries()
    {
        var a1 = new DeidFinding("a.cs", 1, 1, DetectorCategory.Dates);
        var a2 = new DeidFinding("a.cs", 9, 1, DetectorCategory.Dates);
        var b = new DeidFinding("b.cs", 1, 1, DetectorCategory.Email);

        var (newFindings, stale) = DeidComplianceService.CompareWithBaseline(
            [(a1, "a|k"), (a2, "a|k"), (b, "b|k")],
            ["a|k", "c|k"]);

        newFindings.Should().Equal(a2, b);
        stale.Should().Equal("c|k");
    }

    [Fact]
    public void ScanDeidCompliance_FailsOnANewFinding_AndPassesOnceItIsBaselined()
    {
        string seed = Write("apps/x/Seed.cs", "var dob = \"1971-03-14\";\n");
        string clean = Write("apps/x/Clean.cs", "var answer = 42;\n");

        DeidComplianceService.ScanDeidCompliance([seed, clean], _dir).Should().Be(1);

        string key = DeidComplianceService.BaselineKey(new DeidFinding("apps/x/Seed.cs", 1, 12, DetectorCategory.Dates), "var dob = \"1971-03-14\";");
        Write(DeidComplianceService.BaselineFile, $"# header\n{key}\n");
        DeidComplianceService.ScanDeidCompliance([seed, clean], _dir).Should().Be(0);
    }

    [Fact]
    public void ScanDeidCompliance_FailsOnAStaleEntryForAScannedFile_ButIgnoresEntriesForOtherFiles()
    {
        string clean = Write("apps/x/Clean.cs", "var answer = 42;\n");

        Write(DeidComplianceService.BaselineFile, "apps/x/Other.cs|dates|0123456789abcdef\n");
        DeidComplianceService.ScanDeidCompliance([clean], _dir).Should().Be(0);

        Write(DeidComplianceService.BaselineFile, "apps/x/Clean.cs|dates|0123456789abcdef\n");
        DeidComplianceService.ScanDeidCompliance([clean], _dir).Should().Be(1);
    }

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in new List<string> { "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false" })
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {error}");
        return output;
    }
}
