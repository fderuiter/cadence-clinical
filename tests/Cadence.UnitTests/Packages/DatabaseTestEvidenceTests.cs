using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Compliance;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// How the test runs find database tests that did not execute (#5325), and how the database fixtures pick their
/// server. Docker itself is not started here: these tests cover the selection and the TRX reading only.
/// </summary>
public sealed class DatabaseTestEvidenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"db-evidence-{Guid.NewGuid():N}");

    public DatabaseTestEvidenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(null, null, PostgresTestServerKind.None)]
    [InlineData("", "0", PostgresTestServerKind.None)]
    [InlineData(null, "1", PostgresTestServerKind.Docker)]
    [InlineData("Host=db", null, PostgresTestServerKind.External)]
    [InlineData("Host=db", "1", PostgresTestServerKind.External)]
    public void Select_PrefersAConnectionString_ThenTheDockerSwitch(string? postgres, string? docker, PostgresTestServerKind expected)
    {
        var variables = new Dictionary<string, string?> { ["CADENCE_TEST_POSTGRES"] = postgres, ["CADENCE_TEST_DOCKER"] = docker };

        PostgresTestServer.Select(name => variables.GetValueOrDefault(name)).Should().Be(expected);
    }

    [Fact]
    public async Task StartAsync_WithNeitherOptIn_ReturnsNoServer()
    {
        (await PostgresTestServer.StartAsync(_ => null)).Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_WithAConnectionString_UsesItWithoutStartingAContainer()
    {
        await using var server = await PostgresTestServer.StartAsync(name => name == "CADENCE_TEST_POSTGRES" ? "Host=db;Username=postgres" : null);

        server.Should().NotBeNull();
        server!.Kind.Should().Be(PostgresTestServerKind.External);
        server.SuperuserConnectionString.Should().Be("Host=db;Username=postgres");
    }

    [Fact]
    public void FindSkipped_ReportsSkippedAndNotExecutedDatabaseTests_ButNotUnitTestsOrTheOptInBenchmark()
    {
        WriteTrx("run.trx",
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.RlsTests", "Isolates", "NotExecuted"),
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.RlsTests", "Passes", "Passed"),
            ("Cadence.E2ETests.dll", "Cadence.E2ETests.E2E.Flow", "Pending", "Inconclusive"),
            ("Cadence.UnitTests.dll", "Cadence.UnitTests.Some", "Skipped", "NotExecuted"),
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.LineageBenchmarkPostgresTests", "BenchmarkTenMillionEdgesTraceLatency", "NotExecuted"),
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Execution.FormQueryWorklistBenchmarkPostgresTests", "BenchmarkThreeHundredStudyFormsWorklistSqlLatencyAndAllocations", "NotExecuted"),
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests", "BrowserCorrectionReachesTheDatabaseWithItsReason", "NotExecuted"));

        var skipped = DatabaseTestEvidence.FindSkipped(_dir);

        skipped.Should().HaveCount(2);
        skipped[0].FullName.Should().Be("Cadence.E2ETests.E2E.Flow.Pending");
        skipped[1].FullName.Should().Be("Cadence.IntegrationTests.Kernel.RlsTests.Isolates");
        skipped[1].Reason.Should().Be("skipped for a reason");
    }

    [Fact]
    public void FindSkipped_ReturnsNothingForAMissingDirectory()
    {
        DatabaseTestEvidence.FindSkipped(Path.Combine(_dir, "missing")).Should().BeEmpty();
    }

    [Fact]
    public void FindSkipped_WithRepoRoot_ReportsDiscoveredDatabaseTestsMissingFromTrx()
    {
        string repoRoot = Path.Combine(_dir, "repo");
        string integrationDir = Path.Combine(repoRoot, "tests", "Cadence.IntegrationTests", "Kernel");
        string e2eDir = Path.Combine(repoRoot, "tests", "Cadence.E2ETests", "E2E");
        string unitDir = Path.Combine(repoRoot, "tests", "Cadence.UnitTests", "Packages");
        Directory.CreateDirectory(integrationDir);
        Directory.CreateDirectory(e2eDir);
        Directory.CreateDirectory(unitDir);

        File.WriteAllText(Path.Combine(integrationDir, "RlsTests.cs"), string.Join("\n",
            "namespace Cadence.IntegrationTests.Kernel;",
            "public class RlsTests",
            "{",
            "    [Fact]",
            "    public void Executed() { }",
            "    [Fact]",
            "    public void MissingFromTrx() { }",
            "}",
            "public class LineageBenchmarkPostgresTests",
            "{",
            "    [Fact]",
            "    public void BenchmarkTenMillionEdgesTraceLatency() { }",
            "}"));
        string executionDir = Path.Combine(repoRoot, "tests", "Cadence.IntegrationTests", "Execution");
        Directory.CreateDirectory(executionDir);
        File.WriteAllText(Path.Combine(executionDir, "FormQueryWorklistBenchmarkPostgresTests.cs"), string.Join("\n",
            "namespace Cadence.IntegrationTests.Execution;",
            "public class FormQueryWorklistBenchmarkPostgresTests",
            "{",
            "    [Fact]",
            "    public void BenchmarkThreeHundredStudyFormsWorklistSqlLatencyAndAllocations() { }",
            "}"));
        File.WriteAllText(Path.Combine(e2eDir, "FlowTests.cs"), string.Join("\n",
            "namespace Cadence.E2ETests.E2E;",
            "public class FlowTests",
            "{",
            "    [Fact]",
            "    public void NeverRan() { }",
            "}"));
        File.WriteAllText(Path.Combine(unitDir, "UnitTests.cs"), string.Join("\n",
            "namespace Cadence.UnitTests.Packages;",
            "public class UnitTests",
            "{",
            "    [Fact]",
            "    public void UnitNotInTrx() { }",
            "}"));

        WriteTrx("run.trx",
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.RlsTests", "Executed", "Passed"));

        var skipped = DatabaseTestEvidence.FindSkipped(_dir, repoRoot);

        skipped.Should().HaveCount(2);
        skipped[0].FullName.Should().Be("Cadence.E2ETests.E2E.FlowTests.NeverRan");
        skipped[1].FullName.Should().Be("Cadence.IntegrationTests.Kernel.RlsTests.MissingFromTrx");
    }

    [Fact]
    public void HasRecordedEnvironment_RequiresDatabaseFixtureEvidenceWithFixtureAndServerVersion()
    {
        DatabaseTestEvidence.HasRecordedEnvironment(_dir).Should().BeFalse();

        string metadataFile = Path.Combine(_dir, "kernel.database.txt");
        File.WriteAllText(metadataFile, "fixture=kernel\nsource=CADENCE_TEST_POSTGRES\n"); // pragma: allowlist secret
        DatabaseTestEvidence.HasRecordedEnvironment(_dir).Should().BeFalse();

        File.WriteAllText(metadataFile, "fixture=kernel\nserver_version=18.1\nsource=CADENCE_TEST_POSTGRES\n"); // pragma: allowlist secret
        DatabaseTestEvidence.HasRecordedEnvironment(_dir).Should().BeTrue();
    }

    [Fact]
    public void FindSkipped_WhenBrowserEvidenceRequired_FailsWithoutBrowserExecutionAndPassesWhenBrowserShardExecuted()
    {
        WriteTrx("dotnet/gxp_integration.trx",
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.RlsTests", "Passes", "Passed"),
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.LineageBenchmarkPostgresTests", "BenchmarkTenMillionEdgesTraceLatency", "NotExecuted"),
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Execution.FormQueryWorklistBenchmarkPostgresTests", "BenchmarkThreeHundredStudyFormsWorklistSqlLatencyAndAllocations", "NotExecuted"),
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests", "BrowserCorrectionReachesTheDatabaseWithItsReason", "NotExecuted"));

        DatabaseTestEvidence.FindSkipped(_dir, repoRoot: null, requireBrowserEvidence: false).Should().BeEmpty();

        var missingBrowser = DatabaseTestEvidence.FindSkipped(_dir, repoRoot: null, requireBrowserEvidence: true);
        missingBrowser.Should().ContainSingle()
            .Which.FullName.Should().Be(DatabaseTestEvidence.BrowserCorrectionTest);

        WriteTrx("browser/gxp_browser.trx",
            ("Cadence.IntegrationTests.dll", "Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests", "BrowserCorrectionReachesTheDatabaseWithItsReason", "Passed"));

        DatabaseTestEvidence.FindSkipped(_dir, repoRoot: null, requireBrowserEvidence: true).Should().BeEmpty();
    }

    private void WriteTrx(string fileName, params (string Assembly, string ClassName, string Method, string Outcome)[] results)
    {
        string targetPath = Path.Combine(_dir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var definitions = new XElement(ns + "TestDefinitions");
        var outcomes = new XElement(ns + "Results");
        foreach (var (assembly, className, method, outcome) in results)
        {
            string id = Guid.NewGuid().ToString();
            definitions.Add(new XElement(ns + "UnitTest",
                new XAttribute("id", id),
                new XElement(ns + "TestMethod",
                    new XAttribute("codeBase", $"/repo/tests/bin/{assembly}"),
                    new XAttribute("className", className),
                    new XAttribute("name", method))));
            outcomes.Add(new XElement(ns + "UnitTestResult",
                new XAttribute("testId", id),
                new XAttribute("outcome", outcome),
                new XElement(ns + "Output", new XElement(ns + "ErrorInfo", new XElement(ns + "Message", "skipped for a reason")))));
        }

        new XDocument(new XElement(ns + "TestRun", definitions, outcomes)).Save(targetPath);
    }
}
