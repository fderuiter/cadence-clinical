using System;
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

    public void Dispose() => Directory.Delete(_dir, recursive: true);

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
    [InlineData("const tenantRef = \"tenant:00000000-0000-0000-0000-000000000000\";")]
    [InlineData("const studyId = \"12345678-1234-4321-8765-123456789012\";")]
    public void ScanText_AllowsKnownDevelopmentValues(string line)
    {
        DeidComplianceService.ScanText("apps/web/src/stores/auth.js", line, Detector).Should().BeEmpty();
    }

    [Theory]
    [InlineData("const contact = \"jane.roe@hospital.test\";", DetectorCategory.Email)]
    [InlineData("const sneaky = \"clinical@gmail.com\";", DetectorCategory.Email)]
    [InlineData("const phone = \"+1-415-555-0199\";", DetectorCategory.TelephoneFax)]
    [InlineData("const site = \"https://patient-portal.test/records\";", DetectorCategory.Urls)]
    [InlineData("const unsplash = \"https://images.unsplash.com/photo-1579684385127\";", DetectorCategory.Urls)]
    [InlineData("const badge = \"https://img.shields.io/badge/.NET-10.0-512BD4.svg\";", DetectorCategory.Urls)]
    [InlineData("const zip = \"90210\";", DetectorCategory.ZipGeographic)]
    [InlineData("const host = \"192.0.2.44\";", DetectorCategory.IpMacAddresses)]
    [InlineData("const dob = \"1971-03-14\";", DetectorCategory.Dates)]
    public void ScanText_ReportsValuesOutsideTheAllowlists(string line, string category)
    {
        DeidComplianceService.ScanText("apps/x/Seed.ts", line, Detector).Select(f => f.Category).Should().Contain(category);
    }

    [Theory]
    [InlineData("fderuiter@cadence.clinical", true)]
    [InlineData("ops@sub.cadence-clinical.com", true)]
    [InlineData("qa@ccrsoft.com", true)]
    [InlineData("support@cadence.example", true)]
    [InlineData("fixture@example.invalid", true)]
    [InlineData("john.doe@example.com", false)]
    [InlineData("clinical@gmail.com", false)]
    [InlineData("cadence@evil-domain.org", false)]
    public void IsApprovedSyntheticEmail_ValidatesDomainStrictly(string email, bool approved)
    {
        DeidComplianceService.IsApprovedSyntheticEmail(email).Should().Be(approved);
    }

    [Theory]
    [InlineData("tests/Cadence.UnitTests/Fixtures.cs", true)]
    [InlineData("docs/CDISC/sdtm-ig.json", true)]
    [InlineData("docs/NCI/thesaurus.xml", true)]
    [InlineData("docs/SDLC/runs/2026-10-01/run.md", true)]
    [InlineData("docs/SDLC/Requirements_Traceability_Matrix.md", true)]
    [InlineData("docs/SDLC/IQ_OQ_PQ_Execution_Report.md", true)]
    [InlineData("docs/openapi/execution_openapi.json", true)]
    [InlineData("docs/schema/execution.md", true)]
    [InlineData("apps/web/node_modules/pkg/index.js", true)]
    [InlineData("apps/execution/src/bin/Debug/app.json", true)]
    [InlineData("apps/web/src/views/Home.test.ts", true)]
    [InlineData(".deid-baseline.txt", true)]
    [InlineData("packages/deid/src/DeidDetector.cs", true)]
    [InlineData("README.md", false)]
    [InlineData("AGENTS.md", false)]
    [InlineData("ARCHITECTURE.md", false)]
    [InlineData("CONTRIBUTING.md", false)]
    [InlineData("LICENSE", false)]
    [InlineData("docs/CI.md", false)]
    [InlineData("docs/SDLC/07_Operations_Deployment_Guide.md", false)]
    [InlineData("apps/web/src/stores/auth.js", false)]
    [InlineData("apps/execution/src/Program.cs", false)]
    [InlineData("apps/web/src/views/Home.vue", false)]
    public void IsExcludedPath_ScansAuthoredDocsRootDocsVueAndStores_WhileSkippingVendoredStandardsAndGxpRuns(string path, bool excluded)
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
        DeidComplianceService.ShouldScanFile("docs/badges/dotnet-10.svg").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("docs/.vitepress/config.mjs").Should().BeTrue();
        DeidComplianceService.ShouldScanFile("apps/web/public/logo.png").Should().BeFalse();
        DeidComplianceService.ShouldScanFile(text).Should().BeTrue();
        DeidComplianceService.ShouldScanFile(binary).Should().BeFalse();
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
}
