using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Cli.Services;
using Cadence.Compliance;
using Cadence.Database;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Regression coverage for <c>scripts/build-docs.js</c>, <see cref="CiPlan"/>, and committed schema/GxP documentation:
/// <c>pnpm docs:build</c> validates OpenAPI, schema, Markdown, and GxP RTM snapshots without mutating tracked files.
/// </summary>
public sealed class BuildDocsScriptTests : IDisposable
{
    private const string InitialRtmContent = """
        # Requirements Traceability Matrix (RTM)

        - **Qualified (every mapped test passed):** 35
        """;

    private const string InitialIqContent = """
        # GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report

        - **Requirement-Tagged Test Methods:** 295
        - **Passed:** 295
        """;

    private const string InitialSchemaContent = """
        # `payments` database schema

        - `ck_payments_payables_status`: `"Status" IN ('TRIGGERED', 'VERIFIED', 'INVOICED', 'CANCELLED')`
        """;

    private const string InitialGuideContent = """
        # Developer Guide

        See [RTM](docs/SDLC/Requirements_Traceability_Matrix.md) and [License](LICENSE).
        """;

    private const string InitialPortalHomeContent = """
        # Cadence Portal

        Launch [Staff Demo](/demo/) or [Participant Demo](/subject-portal/).
        """;

    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), $"build-docs-tests-{Guid.NewGuid():N}");

    public BuildDocsScriptTests()
    {
        SetupFixtureRepository();
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureRoot))
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }
    }

    [Fact]
    public void BuildDocs_LeavesRtmDraftUnset_RunsReadOnlyChecks_AndNeverMutatesSdlcOrSchema()
    {
        // A legacy report.xml in the repository root must be ignored rather than triggering generate-rtm.
        File.WriteAllText(Path.Combine(_fixtureRoot, "report.xml"), "<testsuites><testsuite tests=\"1\"/></testsuites>\n");

        var result = RunBuildDocs();

        result.ExitCode.Should().Be(0, result.Output);
        ReadCliLog().Should().Equal(
            "check openapi|RTM_DRAFT=|GENERATE_RTM_DRAFT=",
            "check schema-doc|RTM_DRAFT=|GENERATE_RTM_DRAFT=",
            "check markdown|RTM_DRAFT=|GENERATE_RTM_DRAFT=",
            "check rtm|RTM_DRAFT=|GENERATE_RTM_DRAFT=");

        ReadPnpmLog().Should().Contain("exec vitepress build docs");

        string snapshotDuringBuild = File.ReadAllText(Path.Combine(_fixtureRoot, "vitepress-snapshot.txt"));
        snapshotDuringBuild.Should().Contain("**Qualified (every mapped test passed):** 35");
        snapshotDuringBuild.Should().Contain("**Passed:** 295");
        snapshotDuringBuild.Should().Contain("ck_payments_payables_status");
        snapshotDuringBuild.Should().Contain("STAGED_ROOT_DOCS=true");

        AssertTrackedDocsUnchangedAndTempDocsCleaned();
    }

    [Fact]
    public void BuildDocs_WhenSkipChecksIsSet_SkipsDuplicateGates_ButStillRunsCheckRtm()
    {
        var result = RunBuildDocs(new Dictionary<string, string?>
        {
            ["CADENCE_DOCS_SKIP_CHECKS"] = "1",
        });

        result.ExitCode.Should().Be(0, result.Output);
        ReadCliLog().Should().Equal("check rtm|RTM_DRAFT=|GENERATE_RTM_DRAFT=");
        ReadPnpmLog().Should().Contain("exec vitepress build docs");
        AssertTrackedDocsUnchangedAndTempDocsCleaned();
    }

    [Fact]
    public void BuildDocs_WhenSkipChecksIsSetAndCheckRtmFails_BlocksDraftPublishAndExitsNonZero()
    {
        var result = RunBuildDocs(new Dictionary<string, string?>
        {
            ["CADENCE_DOCS_SKIP_CHECKS"] = "1",
            ["FAKE_CLI_FAIL_ON"] = "check rtm",
        });

        result.ExitCode.Should().Be(1);
        ReadCliLog().Should().Equal("check rtm|RTM_DRAFT=|GENERATE_RTM_DRAFT=");
        ReadPnpmLog().Should().NotContain("exec vitepress build docs");
        AssertTrackedDocsUnchangedAndTempDocsCleaned();
    }

    [Theory]
    [InlineData("check openapi")]
    [InlineData("check schema-doc")]
    [InlineData("check rtm")]
    public void BuildDocs_WhenDriftCheckFails_ExitsNonZero_SkipsVitePress_AndLeavesTrackedFilesClean(string failingCheck)
    {
        var result = RunBuildDocs(new Dictionary<string, string?>
        {
            ["FAKE_CLI_FAIL_ON"] = failingCheck,
        });

        result.ExitCode.Should().Be(1);
        ReadPnpmLog().Should().NotContain("exec vitepress build docs");
        AssertTrackedDocsUnchangedAndTempDocsCleaned();
    }

    [Fact]
    public void BuildDocs_PrepareOnly_StagesRootDocsWithoutRunningCliGates_AndLeavesSdlcAndSchemaUntouched()
    {
        var result = RunBuildDocs(args: ["--prepare-only"]);

        result.ExitCode.Should().Be(0, result.Output);
        ReadCliLog().Should().BeEmpty();
        File.Exists(Path.Combine(_fixtureRoot, "docs", "README.md")).Should().BeTrue();
        File.Exists(Path.Combine(_fixtureRoot, "docs", "ARCHITECTURE.md")).Should().BeTrue();
        File.Exists(Path.Combine(_fixtureRoot, "docs", "AGENTS.md")).Should().BeTrue();
        File.Exists(Path.Combine(_fixtureRoot, "docs", "LICENSE.md")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "index.md"))
            .Should().Be(InitialPortalHomeContent);
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "SDLC", "Requirements_Traceability_Matrix.md"))
            .Should().Be(InitialRtmContent);
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md"))
            .Should().Be(InitialIqContent);
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "schema", "payments.md"))
            .Should().Be(InitialSchemaContent);
    }

    [Fact]
    public void CiPlan_DocsBuildStep_DoesNotRestoreGxpReports()
    {
        var docsStep = CiPlan.Jobs
            .Single(j => j.Key == "docs-build")
            .Steps
            .Single(s => s.Name == "Run documentation compilation");

        docsStep.RestoreFiles.Should().BeEmpty("pnpm docs:build validates GxP snapshots without rewriting docs/SDLC");
        docsStep.EnvironmentIfPassed.Should().ContainSingle(e =>
            e.StepKey == "dotnet-quality/Run Cadence quality sentinels" &&
            e.Name == "CADENCE_DOCS_SKIP_CHECKS" &&
            e.Value == "1");
    }

    [Fact]
    public void BuildDocs_IsolatesSigningSecretsFromVitePressSubprocess_WithoutMutatingProcessEnv()
    {
        var result = RunBuildDocs(new Dictionary<string, string?>
        {
            ["CADENCE_DOCS_SKIP_CHECKS"] = "1",
            ["AUDIT_LOG_SECRET_KEY"] = "super-secret-audit-key",
            ["INBOUND_EMAIL_HMAC_SECRET"] = "super-secret-hmac-key",
            ["GATEWAY_SECRET_KEY"] = "super-secret-gateway-key",
            ["GITHUB_TOKEN"] = "ghp_shouldNotLeakIntoVitePressBuild",
        });

        result.ExitCode.Should().Be(0, result.Output);
        string snapshotDuringBuild = File.ReadAllText(Path.Combine(_fixtureRoot, "vitepress-snapshot.txt"));
        snapshotDuringBuild.Should().Contain("VITEPRESS_SECRETS=|||");
    }

    [Fact]
    public void PaymentsSchemaDoc_MatchesEfCoreModel_IncludingAllFourStatusCheckConstraints()
    {
        string repoRoot = FindRepoRoot();
        string committedPaymentsDoc = Path.Combine(repoRoot, "docs", "schema", "payments.md");

        using var context = new PaymentsDbContextFactory().CreateDbContext(
            ["--connection", "Host=localhost;Database=cadence_schema_export"]);
        string rendered = SchemaDocumentation.Render(
            context,
            "`payments` database schema",
            "pnpm cadence generate-schema-doc");

        string renderDir = Directory.CreateDirectory(Path.Combine(_fixtureRoot, "rendered-payments")).FullName;
        string committedDir = Directory.CreateDirectory(Path.Combine(_fixtureRoot, "committed-payments")).FullName;
        File.WriteAllText(Path.Combine(renderDir, "payments.md"), rendered);
        File.Copy(committedPaymentsDoc, Path.Combine(committedDir, "payments.md"));

        SchemaDocGeneratorService.FindDrift(renderDir, committedDir).Should().BeEmpty();

        string committedText = File.ReadAllText(committedPaymentsDoc);
        committedText.Should().Contain("ck_payments_payables_status");
        committedText.Should().Contain("ck_payments_self_billing_invoices_status");
        committedText.Should().Contain("ck_payments_participant_expense_claims_status");
        committedText.Should().Contain("ck_payments_participant_disbursements_status");
    }

    [Fact]
    public void CommittedGxpSnapshot_PassesCheckRtmWithoutMutation()
    {
        string repoRoot = FindRepoRoot();
        string rtmPath = Path.Combine(repoRoot, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
        string iqPath = Path.Combine(repoRoot, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md");
        string originalRtm = File.ReadAllText(rtmPath);
        string originalIq = File.ReadAllText(iqPath);

        int exitCode = RtmGeneratorService.GenerateRtm(
            draft: true,
            validateOnly: true,
            reportPath: Path.Combine(_fixtureRoot, "no-trx"),
            repoRoot: repoRoot,
            honorDraftEnvironment: false,
            requireCurrentSnapshot: false,
            requireRecordedEvidence: true);

        exitCode.Should().Be(0);
        originalRtm.Should().NotContain(RtmGeneratorService.DraftBanner);
        originalIq.Should().NotContain(RtmGeneratorService.DraftBanner);
        File.ReadAllText(rtmPath).Should().Be(originalRtm);
        File.ReadAllText(iqPath).Should().Be(originalIq);
    }

    [Fact]
    public void BuildDocs_WhenAllEightRootGuidesExist_StagesRewritesAndCleansUpAllEight()
    {
        string repoRoot = FindRepoRoot();
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "docs", ".vitepress"));
        File.Copy(
            Path.Combine(repoRoot, "docs", ".vitepress", "rootDocs.cjs"),
            Path.Combine(_fixtureRoot, "docs", ".vitepress", "rootDocs.cjs"));

        File.WriteAllText(Path.Combine(_fixtureRoot, "CONTEXT.md"), "# Glossary\n\nSee [Contributing](CONTRIBUTING.md).\n");
        File.WriteAllText(Path.Combine(_fixtureRoot, "CONTRIBUTING.md"), "# Contributing\n");
        File.WriteAllText(Path.Combine(_fixtureRoot, "SECURITY.md"), "# Security\n");
        File.WriteAllText(Path.Combine(_fixtureRoot, "SUPPORT.md"), "# Support\n");

        var prepResult = RunBuildDocs(args: ["--prepare-only"]);
        prepResult.ExitCode.Should().Be(0, prepResult.Output);

        string[] allEight =
        [
            "README.md",
            "ARCHITECTURE.md",
            "AGENTS.md",
            "CONTEXT.md",
            "CONTRIBUTING.md",
            "SECURITY.md",
            "SUPPORT.md",
            "LICENSE.md"
        ];

        foreach (string staged in allEight)
        {
            File.Exists(Path.Combine(_fixtureRoot, "docs", staged)).Should().BeTrue($"{staged} should be staged by --prepare-only");
        }

        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "CONTEXT.md"))
            .Should().Contain("(CONTRIBUTING.md)");

        var buildResult = RunBuildDocs(new Dictionary<string, string?>
        {
            ["CADENCE_DOCS_SKIP_CHECKS"] = "1",
        });
        buildResult.ExitCode.Should().Be(0, buildResult.Output);

        foreach (string staged in allEight)
        {
            File.Exists(Path.Combine(_fixtureRoot, "docs", staged)).Should().BeFalse($"temporary {staged} must be cleaned up after build");
        }

        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "index.md"))
            .Should().Be(InitialPortalHomeContent);
    }

    private void SetupFixtureRepository()
    {
        string repoRoot = FindRepoRoot();
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "scripts"));
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "docs", "SDLC"));
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "docs", "schema"));
        string fakeBin = Directory.CreateDirectory(Path.Combine(_fixtureRoot, "fake-bin")).FullName;

        File.Copy(
            Path.Combine(repoRoot, "scripts", "build-docs.js"),
            Path.Combine(_fixtureRoot, "scripts", "build-docs.js"));

        File.WriteAllText(Path.Combine(_fixtureRoot, "README.md"), "# Cadence\n\nSee [Architecture](ARCHITECTURE.md).\n");
        File.WriteAllText(Path.Combine(_fixtureRoot, "ARCHITECTURE.md"), "# Architecture\n");
        File.WriteAllText(Path.Combine(_fixtureRoot, "AGENTS.md"), "# Agents\n");
        File.WriteAllText(Path.Combine(_fixtureRoot, "LICENSE"), "Copyright Cadence\n");
        File.WriteAllText(Path.Combine(_fixtureRoot, "docs", "SDLC", "Requirements_Traceability_Matrix.md"), InitialRtmContent);
        File.WriteAllText(Path.Combine(_fixtureRoot, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md"), InitialIqContent);
        File.WriteAllText(Path.Combine(_fixtureRoot, "docs", "schema", "payments.md"), InitialSchemaContent);
        File.WriteAllText(Path.Combine(_fixtureRoot, "docs", "guide.md"), InitialGuideContent);
        File.WriteAllText(Path.Combine(_fixtureRoot, "docs", "index.md"), InitialPortalHomeContent);

        File.WriteAllText(
            Path.Combine(_fixtureRoot, "scripts", "run-cli.js"),
            """
            const fs = require("fs");
            const path = require("path");
            const args = process.argv.slice(2).join(" ");
            const rtmDraft = process.env.RTM_DRAFT ?? "";
            const genDraft = process.env.GENERATE_RTM_DRAFT ?? "";
            const logFile = path.join(__dirname, "..", "cli-invocations.log");
            fs.appendFileSync(logFile, `${args}|RTM_DRAFT=${rtmDraft}|GENERATE_RTM_DRAFT=${genDraft}\n`, "utf8");
            if (process.env.FAKE_CLI_FAIL_ON === args) {
              process.exit(1);
            }
            process.exit(0);
            """);

        string pnpmStubJs = Path.Combine(fakeBin, "pnpm-stub.js");
        File.WriteAllText(
            pnpmStubJs,
            """
            const fs = require("fs");
            const path = require("path");
            const args = process.argv.slice(2).join(" ");
            const root = path.resolve(__dirname, "..");
            fs.appendFileSync(path.join(root, "pnpm-invocations.log"), `${args}\n`, "utf8");
            if (args === "-v") {
              console.log("10.34.6");
              process.exit(0);
            }
            if (args === "exec vitepress build docs") {
              const rtm = fs.readFileSync(path.join(root, "docs", "SDLC", "Requirements_Traceability_Matrix.md"), "utf8");
              const iq = fs.readFileSync(path.join(root, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md"), "utf8");
              const schema = fs.readFileSync(path.join(root, "docs", "schema", "payments.md"), "utf8");
              const staged = ["README.md", "ARCHITECTURE.md", "AGENTS.md", "LICENSE.md"]
                .every((f) => fs.existsSync(path.join(root, "docs", f)));
              const secrets = [
                process.env.AUDIT_LOG_SECRET_KEY ?? "",
                process.env.INBOUND_EMAIL_HMAC_SECRET ?? "",
                process.env.GATEWAY_SECRET_KEY ?? "",
                process.env.GITHUB_TOKEN ?? "",
              ].join("|");
              fs.writeFileSync(
                path.join(root, "vitepress-snapshot.txt"),
                `${rtm}\n${iq}\n${schema}\nSTAGED_ROOT_DOCS=${staged}\nVITEPRESS_SECRETS=${secrets}\n`,
                "utf8"
              );
            }
            process.exit(0);
            """);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(fakeBin, "pnpm.cmd"), "@node \"%~dp0pnpm-stub.js\" %*\r\n");
        }
        else
        {
            string unixPnpm = Path.Combine(fakeBin, "pnpm");
            File.WriteAllText(unixPnpm, $"#!/usr/bin/env node\nrequire({System.Text.Json.JsonSerializer.Serialize(pnpmStubJs)});\n");
            File.SetUnixFileMode(
                unixPnpm,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private (int ExitCode, string Output) RunBuildDocs(
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        IReadOnlyList<string>? args = null)
    {
        DeleteIfExists(Path.Combine(_fixtureRoot, "cli-invocations.log"));
        DeleteIfExists(Path.Combine(_fixtureRoot, "pnpm-invocations.log"));
        DeleteIfExists(Path.Combine(_fixtureRoot, "vitepress-snapshot.txt"));

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = _fixtureRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(Path.Combine(_fixtureRoot, "scripts", "build-docs.js"));
        if (args is not null)
        {
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        string fakeBin = Path.Combine(_fixtureRoot, "fake-bin");
        psi.Environment["PATH"] = $"{fakeBin}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}";
        psi.Environment.Remove("RTM_DRAFT");
        psi.Environment.Remove("GENERATE_RTM_DRAFT");
        psi.Environment.Remove("CADENCE_DOCS_SKIP_CHECKS");
        psi.Environment.Remove("FAKE_CLI_FAIL_ON");

        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                if (value is null)
                {
                    psi.Environment.Remove(key);
                }
                else
                {
                    psi.Environment[key] = value;
                }
            }
        }

        using var process = Process.Start(psi)!;
        var stderrTask = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        string stderr = stderrTask.GetAwaiter().GetResult();
        return (process.ExitCode, stdout + stderr);
    }

    private string[] ReadCliLog() => ReadLinesIfExists(Path.Combine(_fixtureRoot, "cli-invocations.log"));

    private string[] ReadPnpmLog() => ReadLinesIfExists(Path.Combine(_fixtureRoot, "pnpm-invocations.log"));

    private void AssertTrackedDocsUnchangedAndTempDocsCleaned()
    {
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "SDLC", "Requirements_Traceability_Matrix.md"))
            .Should().Be(InitialRtmContent);
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md"))
            .Should().Be(InitialIqContent);
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "schema", "payments.md"))
            .Should().Be(InitialSchemaContent);
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "guide.md"))
            .Should().Be(InitialGuideContent);
        File.ReadAllText(Path.Combine(_fixtureRoot, "docs", "index.md"))
            .Should().Be(InitialPortalHomeContent);

        foreach (string staged in new[] { "README.md", "ARCHITECTURE.md", "AGENTS.md", "LICENSE.md" })
        {
            File.Exists(Path.Combine(_fixtureRoot, "docs", staged)).Should().BeFalse($"temporary {staged} must be cleaned up");
        }
    }

    private static string[] ReadLinesIfExists(string path) =>
        File.Exists(path)
            ? File.ReadAllLines(path).Where(l => l.Length > 0).ToArray()
            : [];

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cadence.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Cadence.sln not found above the test output.");
    }
}
