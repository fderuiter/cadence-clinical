using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class DevEnvironmentValidatorServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dev-env-validator-tests-" + Guid.NewGuid().ToString("N"));

    public DevEnvironmentValidatorServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidateDevEnvironment_WithCurrentRepo_ReturnsSuccess()
    {
        int result = DevEnvironmentValidatorService.ValidateDevEnvironment();
        result.Should().Be(0);
    }

    [Fact]
    public void NormalizeServiceName_NormalizesVariousFormats()
    {
        DevEnvironmentValidatorService.NormalizeServiceName("front proxy (nginx) → gateway").Should().Be("gateway");
        DevEnvironmentValidatorService.NormalizeServiceName("kernel (`apps/kernel-api`)").Should().Be("kernel");
        DevEnvironmentValidatorService.NormalizeServiceName("DESIGNER_SERVICE_URL").Should().Be("designer");
        DevEnvironmentValidatorService.NormalizeServiceName("Keycloak").Should().Be("keycloak");
    }

    [Fact]
    public void ValidateDevEnvironment_WhenPortMismatchedInGateway_ReturnsError()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 9002,
            appHostExecutionPort: 8002);

        int result = DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot);
        result.Should().Be(1);
    }

    [Fact]
    public void ValidateDevEnvironment_WhenPortMismatchedInAppHost_ReturnsError()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 8002,
            appHostExecutionPort: 9002);

        int result = DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot);
        result.Should().Be(1);
    }

    [Fact]
    public void ValidateDevEnvironment_WhenServiceMissingFromDoc_ReturnsError()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 8002,
            appHostExecutionPort: 8002,
            includeExtraGatewayService: true);

        int result = DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot);
        result.Should().Be(1);
    }

    [Theory]
    [InlineData("| execution                     | 8002      |", "| execution                     | 8099      |")]
    [InlineData("| execution                     | 8002      | `edc`", "| execution                     | 8002      | `wrong_schema`")]
    [InlineData("5174", "5173")]
    public void ValidateDevEnvironment_WhenTargetGuideMutatesPortOrSchema_FailsClosed(string original, string mutated)
    {
        string realRoot = FindRepoRoot();
        CloneMinimalDevEnvWorkspace(realRoot, _tempRoot);
        string docPath = Path.Combine(_tempRoot, "docs", "LOCAL_DEV_ENVIRONMENT.md");
        string content = File.ReadAllText(docPath);
        content.Should().Contain(original);
        File.WriteAllText(docPath, content.Replace(original, mutated, StringComparison.Ordinal));

        DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot).Should().Be(1);
    }

    private static void CloneMinimalDevEnvWorkspace(string sourceRoot, string destRoot)
    {
        CopyRelativeFile(sourceRoot, destRoot, "Cadence.sln");
        CopyRelativeFile(sourceRoot, destRoot, "docs/LOCAL_DEV_ENVIRONMENT.md");
        CopyRelativeFile(sourceRoot, destRoot, "packages/topology/src/ServiceTopology.cs");
        CopyRelativeFile(sourceRoot, destRoot, "apps/gateway/src/Program.cs");
        CopyRelativeFile(sourceRoot, destRoot, "apps/apphost/src/Program.cs");
        CopyRelativeFile(sourceRoot, destRoot, "apps/web/vite.config.js");
        CopyRelativeFile(sourceRoot, destRoot, "apps/subject-portal/vite.config.js");

        string srcSchemaDir = Path.Combine(sourceRoot, "docs", "schema");
        string dstSchemaDir = Path.Combine(destRoot, "docs", "schema");
        Directory.CreateDirectory(dstSchemaDir);
        foreach (string schemaFile in Directory.EnumerateFiles(srcSchemaDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            File.Copy(schemaFile, Path.Combine(dstSchemaDir, Path.GetFileName(schemaFile)), overwrite: true);
        }
    }

    private static void CopyRelativeFile(string sourceRoot, string destRoot, string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string destPath = Path.Combine(destRoot, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(Path.Combine(sourceRoot, normalized), destPath, overwrite: true);
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Cadence.sln not found.");
    }

    private void SetupMockRepo(
        int docExecutionPort,
        int gatewayExecutionPort,
        int appHostExecutionPort,
        bool includeExtraGatewayService = false)
    {
        File.WriteAllText(Path.Combine(_tempRoot, "Cadence.sln"), "");

        string docsDir = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsDir);

        string docContent = $"""
        # Local Development Environment
        ## 4. Services and ports
        | Service | Host port | Database schema | Service | Host port | Database schema |
        | --- | --- | --- | --- | --- | --- |
        | front proxy (nginx) → gateway | 8000 | none | eisf | 8010 | `eisf` |
        | execution | {docExecutionPort} | `edc` | Keycloak | 8080 | (own store) |
        """;
        File.WriteAllText(Path.Combine(docsDir, "LOCAL_DEV_ENVIRONMENT.md"), docContent);

        string gatewayDir = Path.Combine(_tempRoot, "apps", "gateway", "src");
        Directory.CreateDirectory(gatewayDir);

        string extraLine = includeExtraGatewayService ? "string analyticsUrl = GetServiceUrl(\"ANALYTICS_SERVICE_URL\", 8099);" : "";
        string gatewayContent = $"""
        using System;
        var builder = WebApplication.CreateBuilder(args);
        string executionUrl = GetServiceUrl("EXECUTION_SERVICE_URL", {gatewayExecutionPort});
        {extraLine}
        """;
        File.WriteAllText(Path.Combine(gatewayDir, "Program.cs"), gatewayContent);

        string appHostDir = Path.Combine(_tempRoot, "apps", "apphost", "src");
        Directory.CreateDirectory(appHostDir);

        string appHostContent = $"""
        using System;
        var builder = DistributedApplication.CreateBuilder(args);
        Service("EXECUTION_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Execution>("execution"), {appHostExecutionPort});
        var gateway = builder.AddProject<Projects.Cadence_Apps_Gateway>("gateway")
            .WithHttpEndpoint(port: 8000, name: "http");
        """;
        File.WriteAllText(Path.Combine(appHostDir, "Program.cs"), appHostContent);
    }
}
